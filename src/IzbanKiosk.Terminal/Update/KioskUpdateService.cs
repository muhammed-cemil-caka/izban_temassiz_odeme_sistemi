using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;

namespace IzbanKiosk.Terminal.Update
{
    /// <summary>
    /// Keeps the kiosk on the newest published release without anyone visiting it.
    ///
    /// Once a day at the configured hour the repository's latest release is compared
    /// against the running assembly version. A newer one is downloaded, verified and
    /// staged, and the terminal restarts into it.
    ///
    /// An update never interrupts a passenger. If someone is at the machine when the
    /// check fires, the install waits and retries until the terminal is idle again,
    /// so a card presented at 04:00 is served exactly as it would be at any other
    /// time.
    /// </summary>
    internal sealed class KioskUpdateService
    {
        private const string AppliedTagFileName = "applied-update.txt";
        private const int IdleRetryIntervalMs = 30000;
        private const int MaxDeferralHours = 20;

        private readonly KioskHardwareSettings _settings;
        private readonly Func<bool> _isBusy;
        private readonly Action<string> _log;
        private readonly ManualResetEvent _stop = new ManualResetEvent(false);
        private Thread? _worker;

        internal KioskUpdateService(KioskHardwareSettings settings, Func<bool> isBusy, Action<string> log)
        {
            _settings = settings;
            _isBusy = isBusy;
            _log = log;
        }

        internal void Start()
        {
            if (!_settings.UpdateEnabled)
            {
                _log("Automatic updates are disabled in " + KioskHardwareSettings.FileName + ".");
                return;
            }

            if (_settings.UpdateRepositoryOwner.Length == 0 || _settings.UpdateRepositoryName.Length == 0)
            {
                _log("Automatic updates are idle: no update repository is configured.");
                return;
            }

            _worker = new Thread(Run) { IsBackground = true, Name = "IZBAN Kiosk Updater" };
            _worker.Start();
            _log("Automatic updates armed for " + _settings.UpdateCheckHour.ToString("00") + ":00 daily.");
        }

        internal void Stop()
        {
            _stop.Set();
        }

        private void Run()
        {
            while (!_stop.WaitOne(MillisecondsUntilNextCheck()))
            {
                try
                {
                    CheckAndApply();
                }
                catch (Exception ex)
                {
                    // A failed update must never take the kiosk down; it simply stays on
                    // the version it is running and tries again tomorrow.
                    _log("Update check failed: " + ex.GetType().Name + ": " + ex.Message);
                }
            }
        }

        private int MillisecondsUntilNextCheck()
        {
            DateTime now = DateTime.Now;
            DateTime next = new DateTime(now.Year, now.Month, now.Day, _settings.UpdateCheckHour, 0, 0);
            if (next <= now)
            {
                next = next.AddDays(1);
            }

            double milliseconds = (next - now).TotalMilliseconds;
            return milliseconds > int.MaxValue ? int.MaxValue : (int)milliseconds;
        }

        private void CheckAndApply()
        {
            var client = new GitHubReleaseClient(_settings.UpdateRepositoryOwner, _settings.UpdateRepositoryName);
            GitHubRelease? release = client.GetLatestRelease();
            if (release == null)
            {
                _log("No release with a .zip asset was published.");
                return;
            }

            if (release.Version == null)
            {
                _log("Release tag '" + release.Tag + "' carries no readable version; nothing installed.");
                return;
            }

            Version current = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0);
            if (release.Version <= current)
            {
                _log("Kiosk is up to date (running " + current + ", latest " + release.Version + ").");
                return;
            }

            if (string.Equals(ReadAppliedTag(), release.Tag, StringComparison.OrdinalIgnoreCase))
            {
                // The tag was installed but the running version did not move: the package
                // and the tag disagree. Re-downloading it every night would achieve nothing.
                _log("Release '" + release.Tag + "' was already applied but the version did not change; skipping.");
                return;
            }

            _log("New release " + release.Tag + " found. Waiting for the terminal to be idle.");
            if (!WaitUntilIdle())
            {
                _log("A passenger stayed at the terminal; the update is postponed to the next check.");
                return;
            }

            string staging = Path.Combine(Path.GetTempPath(), "izban-kiosk-update");
            PrepareEmptyDirectory(staging);

            string packagePath = client.DownloadPackage(release, staging);
            _log("Downloaded " + release.PackageName + " (" + new FileInfo(packagePath).Length + " bytes).");

            string payload = Path.Combine(staging, "payload");
            Directory.CreateDirectory(payload);
            ZipArchiveExtractor.ExtractTo(packagePath, payload);

            string installRoot = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
            string exeName = Path.GetFileName(Assembly.GetExecutingAssembly().Location);
            if (!File.Exists(Path.Combine(payload, exeName)))
            {
                throw new InvalidDataException("Update package does not contain " + exeName + ".");
            }

            // Re-check: downloading and extracting takes time, and a passenger may have
            // arrived meanwhile. The swap itself must happen on an idle terminal.
            if (_isBusy())
            {
                _log("A passenger arrived while the package was downloading; the update is postponed.");
                return;
            }

            WriteAppliedTag(release.Tag);
            string script = WriteInstallerScript(staging, payload, installRoot, exeName);
            _log("Applying " + release.Tag + " and restarting.");

            Process.Start(new ProcessStartInfo
            {
                FileName = script,
                WorkingDirectory = staging,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            Environment.Exit(0);
        }

        /// <summary>
        /// Blocks until nobody is using the terminal. Gives up after a long wait so a
        /// card left permanently on the reader cannot keep the updater running until
        /// the next day's check overlaps this one.
        /// </summary>
        private bool WaitUntilIdle()
        {
            DateTime deadline = DateTime.Now.AddHours(MaxDeferralHours);
            while (DateTime.Now < deadline)
            {
                if (!_isBusy())
                {
                    return true;
                }
                if (_stop.WaitOne(IdleRetryIntervalMs))
                {
                    return false;
                }
            }
            return false;
        }

        private static void PrepareEmptyDirectory(string path)
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
            Directory.CreateDirectory(path);
        }

        private string AppliedTagPath()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, AppliedTagFileName);
        }

        private string ReadAppliedTag()
        {
            try
            {
                string path = AppliedTagPath();
                return File.Exists(path) ? File.ReadAllText(path).Trim() : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private void WriteAppliedTag(string tag)
        {
            try
            {
                File.WriteAllText(AppliedTagPath(), tag);
            }
            catch (Exception ex)
            {
                _log("Could not record the applied release tag: " + ex.Message);
            }
        }

        /// <summary>
        /// The terminal cannot overwrite its own running executable, so the swap is
        /// handed to a small script that waits for this process to exit first.
        ///
        /// The deployment-owned settings file and the local journal are carried across
        /// deliberately: they belong to the machine, not to the release, and losing the
        /// printer name or the kiosk's records to an update would be worse than not
        /// updating at all. The previous install is kept as a backup for rollback.
        /// </summary>
        private static string WriteInstallerScript(string staging, string payload, string installRoot, string exeName)
        {
            string scriptPath = Path.Combine(staging, "apply-update.cmd");
            string backup = Path.Combine(staging, "previous");

            var script = new StringBuilder();
            script.AppendLine("@echo off");
            script.AppendLine("setlocal");
            script.AppendLine("set PID=" + Process.GetCurrentProcess().Id);
            script.AppendLine(":waitloop");
            script.AppendLine("tasklist /FI \"PID eq %PID%\" 2>nul | find \"%PID%\" >nul");
            script.AppendLine("if not errorlevel 1 (");
            script.AppendLine("  ping -n 2 127.0.0.1 >nul");
            script.AppendLine("  goto waitloop");
            script.AppendLine(")");
            script.AppendLine("xcopy \"" + installRoot + "\\*\" \"" + backup + "\\\" /E /I /Y >nul");
            script.AppendLine("copy /Y \"" + installRoot + "\\" + KioskHardwareSettings.FileName + "\" \"" + staging + "\\settings.bak\" >nul 2>&1");
            script.AppendLine("xcopy \"" + payload + "\\*\" \"" + installRoot + "\\\" /E /I /Y >nul");
            script.AppendLine("copy /Y \"" + staging + "\\settings.bak\" \"" + installRoot + "\\" + KioskHardwareSettings.FileName + "\" >nul 2>&1");
            script.AppendLine("start \"\" \"" + installRoot + "\\" + exeName + "\"");
            script.AppendLine("endlocal");

            File.WriteAllText(scriptPath, script.ToString(), Encoding.Default);
            return scriptPath;
        }
    }
}
