using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;

namespace IzbanKiosk.Terminal.Recovery
{
    /// <summary>
    /// Keeps a crashed kiosk from becoming a dead one.
    ///
    /// Nothing here matters while Windows Explorer is the shell: a crash leaves a
    /// desktop and somebody can start the kiosk again. It matters once this
    /// application *is* the shell, which is how a locked-down kiosk is meant to run.
    /// Then an unhandled exception leaves a station machine showing an empty screen,
    /// with no Start menu, no task bar and no way in except a site visit.
    ///
    /// So a crash is caught, written down, and the kiosk starts itself again. If it
    /// crashes repeatedly in a short window the fault is clearly not transient, and
    /// relaunching only produces a flicker loop; at that point the shell is put back
    /// to Explorer and the desktop is started, because a machine somebody can log
    /// into can be repaired and one that cannot, cannot.
    /// </summary>
    internal static class KioskCrashGuard
    {
        private const string CrashLogFileName = "crash-history.txt";
        private const string CrashReportFileName = "son-cokme.txt";
        private const string WinlogonKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon";

        private static readonly object Lock = new object();
        private static bool _handling;

        internal static void Install(Application application)
        {
            application.DispatcherUnhandledException += OnDispatcherException;
            AppDomain.CurrentDomain.UnhandledException += OnDomainException;
        }

        private static void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            // Marked handled so WPF does not tear the process down before the restart
            // has been arranged.
            e.Handled = true;
            Recover(e.Exception);
        }

        private static void OnDomainException(object sender, UnhandledExceptionEventArgs e)
        {
            Recover(e.ExceptionObject as Exception);
        }

        private static void Recover(Exception? exception)
        {
            lock (Lock)
            {
                // A crash while handling a crash must not recurse into another restart.
                if (_handling)
                {
                    return;
                }
                _handling = true;
            }

            string directory = AppDomain.CurrentDomain.BaseDirectory;
            WriteCrashReport(directory, exception);

            List<DateTime> crashes = ReadHistory(directory);
            crashes.Add(DateTime.Now);
            WriteHistory(directory, CrashRecoveryPolicy.Recent(crashes, DateTime.Now));

            if (CrashRecoveryPolicy.ShouldStopRestarting(crashes, DateTime.Now))
            {
                HandMachineBack();
            }
            else
            {
                RestartSelf();
            }

            Environment.Exit(1);
        }

        /// <summary>
        /// The last crash in plain text next to the executable, because a kiosk has no
        /// console and nobody reads Windows event logs on a machine in a station.
        /// </summary>
        private static void WriteCrashReport(string directory, Exception? exception)
        {
            try
            {
                var report = new StringBuilder();
                report.AppendLine("Zaman  : " + DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.InvariantCulture));
                report.AppendLine("Surum  : " +
                    (Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0)).ToString());
                report.AppendLine("Hata   : " + (exception == null ? "[bilinmiyor]" : exception.GetType().Name));
                report.AppendLine("Mesaj  : " + (exception == null ? string.Empty : exception.Message));
                report.AppendLine();
                report.AppendLine(exception == null ? string.Empty : exception.ToString());
                File.WriteAllText(Path.Combine(directory, CrashReportFileName), report.ToString());
            }
            catch (Exception)
            {
                // Losing the report must never stop the restart.
            }
        }

        private static List<DateTime> ReadHistory(string directory)
        {
            try
            {
                string path = Path.Combine(directory, CrashLogFileName);
                return File.Exists(path)
                    ? CrashRecoveryPolicy.Deserialise(File.ReadAllText(path))
                    : new List<DateTime>();
            }
            catch (Exception)
            {
                return new List<DateTime>();
            }
        }

        private static void WriteHistory(string directory, List<DateTime> crashes)
        {
            try
            {
                File.WriteAllText(
                    Path.Combine(directory, CrashLogFileName), CrashRecoveryPolicy.Serialise(crashes));
            }
            catch (Exception)
            {
            }
        }

        private static void RestartSelf()
        {
            try
            {
                string exe = Assembly.GetExecutingAssembly().Location;
                if (exe.Length == 0)
                {
                    return;
                }

                // A short pause so the dying process releases its window, its pipe and
                // its hardware before the replacement reaches for them.
                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c ping -n 3 127.0.0.1 >nul & start \"\" \"" + exe + "\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Stops the loop and returns the machine to a state a person can work with.
        ///
        /// Only touches the shell when the shell is actually this application; a kiosk
        /// running under Explorer already has a desktop and its registry is none of
        /// this code's business.
        /// </summary>
        private static void HandMachineBack()
        {
            try
            {
                bool weAreTheShell = false;
                using (RegistryKey? key = Registry.LocalMachine.OpenSubKey(WinlogonKey, true))
                {
                    if (key != null)
                    {
                        string shell = Convert.ToString(key.GetValue("Shell", string.Empty)) ?? string.Empty;
                        if (shell.IndexOf("IZBAN-Kiosk", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            key.SetValue("Shell", "explorer.exe", RegistryValueKind.String);
                            weAreTheShell = true;
                        }
                    }
                }

                if (weAreTheShell)
                {
                    // Started now as well as restored for next boot: the technician
                    // standing at the machine should not have to power-cycle it to
                    // get a desktop.
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        UseShellExecute = true
                    });
                }
            }
            catch (Exception)
            {
                // If the shell cannot be restored there is nothing further this
                // process can do; exiting at least stops the loop.
            }
        }
    }
}
