using System;
using System.Runtime.InteropServices;
using System.Windows;
using IzbanKiosk.Terminal.Recovery;
using IzbanKiosk.Terminal.Update;

namespace IzbanKiosk.Terminal
{
    public partial class App : Application
    {
        private const int AttachParentProcess = -1;

        /// <summary>
        /// Lets a WinExe write to the console window that launched it. Without this
        /// the installer script could start the check but never see its answer.
        /// </summary>
        [DllImport("kernel32.dll")]
        private static extern bool AttachConsole(int processId);

        protected override void OnStartup(StartupEventArgs e)
        {
            foreach (string argument in e.Args)
            {
                if (string.Equals(argument, "--check-update", StringComparison.OrdinalIgnoreCase))
                {
                    Environment.Exit(RunUpdateCheck());
                    return;
                }
            }

            // Installed before the window exists, so a failure during start-up is
            // caught too - that is exactly when a bad settings file or a missing
            // device would otherwise leave a shell-mode kiosk on a black screen.
            KioskCrashGuard.Install(this);

            base.OnStartup(e);
            new MainWindow().Show();
        }

        /// <summary>
        /// Answers "can this kiosk actually reach its updates?" from the command line,
        /// so the installer can prove it before the technician leaves the machine.
        ///
        /// Field kiosks have no diagnostics screen, which makes install time the only
        /// moment anyone can find out. A kiosk that cannot reach GitHub still serves
        /// passengers perfectly, so nothing ever draws attention to it - it simply
        /// stops receiving fixes, silently, for as long as it stands there.
        ///
        /// Deliberately runs the same code path the nightly check uses, rather than a
        /// plain ping: what has to work is TLS 1.2 plus the root certificate plus the
        /// configured repository, and only the real request exercises all three.
        /// </summary>
        private static int RunUpdateCheck()
        {
            AttachConsole(AttachParentProcess);

            KioskHardwareSettings settings;
            try
            {
                settings = KioskHardwareSettings.LoadFromApplicationDirectory();
            }
            catch (Exception ex)
            {
                Console.WriteLine("[HATA] Ayar dosyasi okunamadi: " + ex.Message);
                return 3;
            }

            if (!settings.UpdateEnabled)
            {
                Console.WriteLine("[UYARI] Otomatik guncelleme ayar dosyasinda KAPALI.");
                return 4;
            }

            var service = new KioskUpdateService(
                settings,
                delegate { return false; },
                delegate { },
                delegate { });

            UpdateStatusReport report = service.CheckNow();

            Console.WriteLine("        Calisan surum   : " + report.CurrentVersion);
            Console.WriteLine("        Depo            : " +
                settings.UpdateRepositoryOwner + "/" + settings.UpdateRepositoryName);

            if (!report.Reachable)
            {
                Console.WriteLine("        GitHub'a erisim : BASARISIZ");
                Console.WriteLine("        " + report.Message.Replace("\n", "\n        "));
                return 2;
            }

            Console.WriteLine("        GitHub'a erisim : BASARILI");
            if (report.LatestTag.Length > 0)
            {
                Console.WriteLine("        Yayindaki surum : " +
                    (report.LatestVersion.Length > 0 ? report.LatestVersion : "[okunamadi]") +
                    "  (etiket " + report.LatestTag + ")");
            }
            Console.WriteLine("        " + report.Message.Replace("\n", "\n        "));
            return 0;
        }
    }
}
