using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;

namespace IzbanKiosk.Terminal.Update
{
    /// <summary>
    /// What the updater knows right now, for the technician panel. Without this the
    /// only way to learn that updates are silently failing - a Windows 7 machine
    /// without TLS 1.2 can never reach GitHub - would be to publish a release and
    /// come back the next day.
    /// </summary>
    internal sealed class UpdateStatusReport
    {
        internal bool Armed;
        internal string CurrentVersion = string.Empty;
        internal string CheckedAt = string.Empty;
        internal bool Reachable;
        internal string LatestTag = string.Empty;
        internal string LatestVersion = string.Empty;
        internal bool UpdateAvailable;

        /// <summary>
        /// A release was installed but the kiosk came back on the old version, so the
        /// install root is not actually being written. Distinct from "up to date":
        /// this kiosk is stuck and needs someone, not another download.
        /// </summary>
        internal bool InstallStalled;
        internal string Message = string.Empty;
    }

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
        internal const string InstallFailureFileName = "update-failed.log";
        private const string InstallLogFileName = "apply-update.log";
        private const int IdleRetryIntervalMs = 30000;
        private const int MaxDeferralHours = 20;

        private readonly KioskHardwareSettings _settings;
        private readonly Func<bool> _isBusy;
        private readonly Action<string> _log;
        private readonly Action _beforeRestart;
        private readonly ManualResetEvent _stop = new ManualResetEvent(false);
        private readonly object _reportLock = new object();
        private Thread? _worker;
        private UpdateStatusReport _report = new UpdateStatusReport();
        private GitHubRelease? _pendingRelease;

        /// <param name="beforeRestart">
        /// Releases anything the replaced installation still owns, immediately before
        /// this process ends. The hardware bridge is a separate process that Windows
        /// does not end with its parent, and <see cref="Environment.Exit"/> does not
        /// raise the window events the normal shutdown path hangs off - so without
        /// this the old bridge outlives the update, keeps the single-instance named
        /// pipe, and the freshly installed terminal cannot reach its own bridge.
        /// </param>
        internal KioskUpdateService(
            KioskHardwareSettings settings, Func<bool> isBusy, Action<string> log, Action beforeRestart)
        {
            _settings = settings;
            _isBusy = isBusy;
            _log = log;
            _beforeRestart = beforeRestart;
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

            lock (_reportLock)
            {
                _report.Armed = true;
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

        internal UpdateStatusReport LastStatus()
        {
            lock (_reportLock)
            {
                return _report;
            }
        }

        /// <summary>
        /// Contacts GitHub and reports what it finds. Deliberately installs nothing: a
        /// diagnostic a technician runs while standing at the kiosk must not restart it
        /// as a side effect.
        /// </summary>
        internal UpdateStatusReport CheckNow()
        {
            var report = new UpdateStatusReport
            {
                Armed = _worker != null,
                CurrentVersion = CurrentVersion().ToString(),
                CheckedAt = DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss")
            };

            if (_settings.UpdateRepositoryOwner.Length == 0 || _settings.UpdateRepositoryName.Length == 0)
            {
                report.Message = "Güncelleme deposu tanımlı değil.";
                return Store(report, null);
            }

            try
            {
                var client = new GitHubReleaseClient(_settings.UpdateRepositoryOwner, _settings.UpdateRepositoryName);
                GitHubRelease? release = client.GetLatestRelease();
                report.Reachable = true;

                if (release == null)
                {
                    report.Message = "Yayındaki sürümde .zip dosyası yok.";
                    return Store(report, null);
                }

                report.LatestTag = release.Tag;
                if (release.Version == null)
                {
                    report.Message = "'" + release.Tag + "' etiketinden sürüm okunamadı, bu yüzden kurulamaz.";
                    return Store(report, null);
                }

                report.LatestVersion = release.Version.ToString();
                report.UpdateAvailable = release.Version > CurrentVersion();

                // The same release was installed already and the running version did
                // not move, so installing it again would change nothing. Reinstalling
                // on every check is what produced an update that appeared to succeed,
                // restarted onto the old build, and offered itself again forever.
                if (report.UpdateAvailable &&
                    string.Equals(ReadAppliedTag(), release.Tag, StringComparison.OrdinalIgnoreCase))
                {
                    report.UpdateAvailable = false;
                    report.InstallStalled = true;
                    report.Message =
                        "'" + release.Tag + "' KURULDU AMA SÜRÜM DEĞİŞMEDİ. Otomat hâlâ " +
                        report.CurrentVersion + " çalışıyor, yani dosyalar kurulum klasörüne " +
                        "yazılamıyor. Tekrar kurmak bir işe yaramaz.\n\n" + DescribeInstallFailure();
                    return Store(report, null);
                }

                report.Message = report.UpdateAvailable
                    ? "Yeni bir sürüm yayınlanmış. Kurmak için ŞİMDİ GÜNCELLE düğmesini kullanabilir " +
                      "veya gece 04:00'teki otomatik kurulumu bekleyebilirsiniz."
                    : "Bu otomat en güncel sürümü çalıştırıyor.";
                return Store(report, report.UpdateAvailable ? release : null);
            }
            catch (Exception ex)
            {
                report.Reachable = false;
                report.Message = ex.GetType().Name + ": " + ex.Message;

                // Windows 7 keeps TLS 1.2 switched off by default and GitHub accepts
                // nothing older, so this is the failure a kiosk hits first. The raw
                // WebException says nothing about that, and a technician standing at
                // the machine has no way to guess it.
                if (ex is System.Net.WebException)
                {
                    report.Message += "\n\nİnternet çalışıyor ama güvenli bağlantı kurulamıyor olabilir. " +
                        "Otomatta 5-TLS-Duzelt.bat dosyasını yönetici olarak çalıştırın ve makineyi " +
                        "yeniden başlatın.";
                }
                return Store(report, null);
            }
        }

        private UpdateStatusReport Store(UpdateStatusReport report, GitHubRelease? pending)
        {
            lock (_reportLock)
            {
                _report = report;
                _pendingRelease = pending;
            }
            return report;
        }

        private static Version CurrentVersion()
        {
            return Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0);
        }

        /// <summary>
        /// Installs the release the last check found. Separate from the check so the
        /// restart is always something someone asked for, whether that is the operator
        /// pressing the button or the nightly schedule.
        /// </summary>
        internal void ApplyPending()
        {
            GitHubRelease? release;
            lock (_reportLock)
            {
                release = _pendingRelease;
            }

            if (release == null)
            {
                throw new InvalidOperationException("Bekleyen bir sürüm yok. Önce kontrol çalıştırın.");
            }
            Apply(release);
        }

        private void CheckAndApply()
        {
            UpdateStatusReport report = CheckNow();
            if (!report.UpdateAvailable)
            {
                _log("Update check: " + report.Message);
                return;
            }

            GitHubRelease? release;
            lock (_reportLock)
            {
                release = _pendingRelease;
            }
            if (release == null)
            {
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

            Apply(release);
        }

        private void Apply(GitHubRelease release)
        {
            var client = new GitHubReleaseClient(_settings.UpdateRepositoryOwner, _settings.UpdateRepositoryName);

            string staging = Path.Combine(Path.GetTempPath(), "izban-kiosk-update");
            staging = PrepareEmptyDirectory(staging);

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

            try
            {
                _beforeRestart();
            }
            catch (Exception ex)
            {
                // Never abandon an update that is already staged; the incoming build
                // clears a leftover bridge itself, this only spares it the detour.
                _log("Pre-restart cleanup failed: " + ex.Message);
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = script,
                // Absolute paths throughout, so the script has no need to sit inside
                // the folder it is about to have deleted on the next run.
                WorkingDirectory = installRoot,
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

        /// <summary>
        /// Returns an empty staging folder, falling back to a fresh name when the
        /// usual one cannot be cleared.
        ///
        /// Something else holding a single file - a leftover shell, an antivirus
        /// scanner, an operator with the folder open - used to abort the whole update.
        /// Working around it costs one temporary folder and keeps the kiosk updating.
        /// </summary>
        private static string PrepareEmptyDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
                Directory.CreateDirectory(path);
                return path;
            }
            catch (Exception)
            {
                string alternate = path + "-" + DateTime.Now.ToString("HHmmss");
                Directory.CreateDirectory(alternate);
                return alternate;
            }
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

        /// <summary>
        /// Quotes what the installer script actually reported, when it managed to
        /// leave a copy behind. A technician standing at a stuck kiosk otherwise has
        /// no way to tell "access denied" from "path does not exist".
        /// </summary>
        private static string DescribeInstallFailure()
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, InstallFailureFileName);
            try
            {
                if (!File.Exists(path))
                {
                    return "Kurulum klasörünün yazma iznini ve dolu olup olmadığını kontrol edin: " +
                        AppDomain.CurrentDomain.BaseDirectory;
                }

                string[] lines = File.ReadAllLines(path);
                int from = Math.Max(0, lines.Length - 6);
                var tail = new StringBuilder("Kurulum günlüğünün sonu (" + path + "):");
                for (int i = from; i < lines.Length; i++)
                {
                    tail.Append("\n  " + lines[i]);
                }
                return tail.ToString();
            }
            catch (Exception ex)
            {
                return "Kurulum günlüğü okunamadı: " + ex.Message;
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

            string log = Path.Combine(staging, InstallLogFileName);

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
            script.AppendLine("xcopy \"" + installRoot + "\\*\" \"" + backup + "\\\" /E /I /Y >\"" + log + "\" 2>&1");
            script.AppendLine("copy /Y \"" + installRoot + "\\" + KioskHardwareSettings.FileName + "\" \"" + staging + "\\settings.bak\" >>\"" + log + "\" 2>&1");
            script.AppendLine("xcopy \"" + payload + "\\*\" \"" + installRoot + "\\\" /E /I /Y >>\"" + log + "\" 2>&1");

            // The copy is the whole update. Letting it fail quietly is what turned a
            // broken install into an endless nightly download: the kiosk restarted on
            // the old build, saw the new release again, and repeated forever with
            // nothing on screen to say why.
            script.AppendLine("if errorlevel 1 (");
            script.AppendLine("  echo KOPYALAMA BASARISIZ >>\"" + log + "\"");
            script.AppendLine("  copy /Y \"" + log + "\" \"" + installRoot + "\\" + InstallFailureFileName + "\" >nul 2>&1");
            script.AppendLine(")");

            script.AppendLine("copy /Y \"" + staging + "\\settings.bak\" \"" + installRoot + "\\" + KioskHardwareSettings.FileName + "\" >>\"" + log + "\" 2>&1");
            // /D is what keeps the next update working. Without it the relaunched
            // kiosk inherits this script's working directory - the staging folder in
            // %TEMP% - and a process holding a directory open is enough to stop it
            // being deleted. The following update then fails on "being used by another
            // process" until somebody closes and reopens the application by hand.
            script.AppendLine("start \"\" /D \"" + installRoot + "\" \"" + installRoot + "\\" + exeName + "\"");
            script.AppendLine("endlocal");

            // cmd.exe reads a .cmd in the console's OEM code page, not the ANSI one
            // Encoding.Default gives. On a Turkish Windows those are 857 and 1254, so
            // any non-ASCII character in the install path - "Masaüstü" is the one that
            // actually happens - arrived mangled, xcopy wrote to a path that did not
            // exist, and the old build was relaunched as though nothing was wrong.
            File.WriteAllText(scriptPath, script.ToString(), OemEncoding());
            return scriptPath;
        }

        private static Encoding OemEncoding()
        {
            try
            {
                return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
            }
            catch (Exception)
            {
                return Encoding.Default;
            }
        }
    }
}
