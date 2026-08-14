using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;

namespace IzbanKiosk.Terminal.Timekeeping
{
    /// <summary>
    /// What the kiosk currently believes about its own clock, for the technician panel
    /// and for the updater's failure message.
    /// </summary>
    internal sealed class KioskClockStatus
    {
        internal ClockVerdict Verdict = ClockVerdict.Unverified;

        /// <summary>Local time as the machine reports it, at the moment of the check.</summary>
        internal string LocalNow = string.Empty;

        /// <summary>Windows time zone in force, which is what puts a wrong hour on a receipt.</summary>
        internal string TimeZone = string.Empty;

        /// <summary>Which reference answered, empty when none did.</summary>
        internal string Source = string.Empty;

        /// <summary>How far the clock was out before any correction.</summary>
        internal TimeSpan Offset = TimeSpan.Zero;

        internal bool CorrectionApplied;

        /// <summary>
        /// A reference was reached and disagreed, but Windows refused the write. Kept
        /// apart from "no reference" because the remedy is different: this machine
        /// needs the kiosk run as administrator, not a network.
        /// </summary>
        internal bool CorrectionRefused;

        internal string RefusalDetail = string.Empty;

        /// <summary>Earliest instant the clock could honestly report, for display.</summary>
        internal string FloorLocal = string.Empty;

        /// <summary>Turkish, technician-facing.</summary>
        internal string Message = string.Empty;

        internal bool Checked;
    }

    /// <summary>
    /// Keeps the kiosk's clock honest, and says so plainly when it cannot.
    ///
    /// The kiosk needs the right date for two things a passenger never sees: the
    /// certificate check that every update download depends on, and the timestamp on a
    /// printed receipt. Neither complains in a way anyone notices - a kiosk with a
    /// clock stuck in 2010 serves passengers all day and simply never updates again.
    ///
    /// Three references, in descending order of trust: NTP, the Date header of a plain
    /// HTTP response, and the machine's own history. The middle one exists because a
    /// station network that blocks UDP 123 may still allow port 80, and an
    /// unauthenticated header is a poor clock but a far better one than a dead CMOS
    /// battery. The last one needs no network at all, which is the case this has to
    /// survive: a field kiosk that reaches nothing must still be able to tell that its
    /// clock is wrong, and must not mistake its own isolation for a fault.
    /// </summary>
    internal sealed class KioskClock
    {
        private const string FloorFileName = "clock-floor.txt";
        private const int NtpPort = 123;
        private const int NtpTimeoutMs = 3000;
        private const int HttpTimeoutMs = 5000;

        /// <summary>
        /// Read over plain HTTP precisely because the clock may be too wrong for TLS.
        /// The redirect to HTTPS is not followed; its 301 carries the Date header, which
        /// is all this needs, and the kiosk talks to no host it was not already talking to.
        /// </summary>
        private const string HttpDateUrl = "http://github.com/";

        /// <summary>
        /// Re-checked through the day rather than at start-up alone. A kiosk that runs
        /// for months without a restart is exactly the one whose clock drifts, and the
        /// machine most likely to have a dying battery is the one already reporting a
        /// wrong date.
        /// </summary>
        private static readonly TimeSpan ResyncInterval = TimeSpan.FromHours(6);

        private readonly KioskHardwareSettings _settings;
        private readonly Action<string> _log;
        private readonly object _sync = new object();
        private readonly ManualResetEvent _stop = new ManualResetEvent(false);
        private KioskClockStatus _status = new KioskClockStatus();
        private Thread? _worker;

        internal KioskClock(KioskHardwareSettings settings, Action<string> log)
        {
            _settings = settings;
            _log = log;
        }

        internal KioskClockStatus LastStatus()
        {
            lock (_sync)
            {
                return _status;
            }
        }

        /// <summary>
        /// Returns a checked status, doing the work only if nobody has yet. Used by the
        /// updater, which needs a verdict but must not pay for a network round trip on
        /// every poll.
        /// </summary>
        internal KioskClockStatus EnsureSynchronised()
        {
            lock (_sync)
            {
                if (_status.Checked)
                {
                    return _status;
                }
            }
            return Synchronise();
        }

        internal void Start()
        {
            if (_worker != null)
            {
                return;
            }

            // On its own thread: the kiosk must reach the idle screen whether or not a
            // time server answers, and three unreachable servers cost ten seconds a
            // passenger would otherwise spend watching a splash.
            _worker = new Thread(Run) { IsBackground = true, Name = "IZBAN Kiosk Clock" };
            _worker.Start();
        }

        internal void Stop()
        {
            _stop.Set();
        }

        private void Run()
        {
            do
            {
                try
                {
                    Synchronise();
                }
                catch (Exception ex)
                {
                    // The clock is a supporting concern; failing to check it must never
                    // be the reason a kiosk stops serving passengers.
                    _log("Clock check failed: " + ex.GetType().Name + ": " + ex.Message);
                }
            }
            while (!_stop.WaitOne((int)ResyncInterval.TotalMilliseconds));
        }

        /// <summary>
        /// Compares the clock against whatever reference can be reached, corrects it if
        /// one disagrees, and records the reading so the next run has a floor to judge
        /// against even with no network at all.
        /// </summary>
        internal KioskClockStatus Synchronise()
        {
            var status = new KioskClockStatus { Checked = true };
            DateTime floorUtc = ReadFloorUtc();

            DateTime referenceUtc = DateTime.MinValue;
            string source = string.Empty;
            bool haveReference = false;
            if (_settings.ClockSyncEnabled)
            {
                haveReference = TryFindReference(out referenceUtc, out source);
            }

            if (haveReference)
            {
                status.Source = source;
                status.Offset = DateTime.UtcNow - referenceUtc;

                if (ClockPlausibilityPolicy.ShouldCorrect(DateTime.UtcNow, referenceUtc))
                {
                    string refusal;
                    if (TrySetSystemTimeUtc(referenceUtc, out refusal))
                    {
                        status.CorrectionApplied = true;
                        _log("Clock corrected from " + source + " by " +
                            DescribeOffset(status.Offset) + ".");
                    }
                    else
                    {
                        status.CorrectionRefused = true;
                        status.RefusalDetail = refusal;
                        _log("Clock is out by " + DescribeOffset(status.Offset) +
                            " but Windows refused the correction: " + refusal);
                    }
                }
            }

            // Judged after any correction, so a machine that was fixed this second
            // reports the clock it actually has rather than the one it arrived with.
            bool verified = haveReference && !status.CorrectionRefused;
            status.Verdict = ClockPlausibilityPolicy.Judge(DateTime.UtcNow, floorUtc, verified);
            status.LocalNow = DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.InvariantCulture);
            status.FloorLocal = floorUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);
            status.TimeZone = DescribeTimeZone();
            status.Message = BuildMessage(status);

            if (ClockPlausibilityPolicy.MayRaiseFloor(status.Verdict))
            {
                WriteFloorUtc(DateTime.UtcNow);
            }

            lock (_sync)
            {
                _status = status;
            }
            return status;
        }

        private bool TryFindReference(out DateTime referenceUtc, out string source)
        {
            foreach (string server in TimeServers())
            {
                if (TryQueryNtp(server, out referenceUtc))
                {
                    source = server + " (NTP)";
                    return true;
                }
            }

            if (TryQueryHttpDate(out referenceUtc))
            {
                source = "github.com (HTTP)";
                return true;
            }

            referenceUtc = DateTime.MinValue;
            source = string.Empty;
            return false;
        }

        private string[] TimeServers()
        {
            string configured = (_settings.ClockTimeServers ?? string.Empty).Trim();
            if (configured.Length == 0)
            {
                return new string[0];
            }

            string[] parts = configured.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                parts[i] = parts[i].Trim();
            }
            return parts;
        }

        /// <summary>
        /// Minimal SNTP client. Deliberately not the Windows Time service: w32time has
        /// to be configured, started and permitted to make a correction this large, and
        /// on a machine whose battery has died the correction is always this large.
        /// </summary>
        private static bool TryQueryNtp(string host, out DateTime utc)
        {
            utc = DateTime.MinValue;
            try
            {
                IPAddress[] addresses = Dns.GetHostAddresses(host);
                if (addresses.Length == 0)
                {
                    return false;
                }

                var request = new byte[48];
                request[0] = 0x1B; // no leap warning, version 3, client mode

                byte[] response;
                using (var client = new UdpClient())
                {
                    client.Client.SendTimeout = NtpTimeoutMs;
                    client.Client.ReceiveTimeout = NtpTimeoutMs;
                    client.Connect(new IPEndPoint(addresses[0], NtpPort));
                    client.Send(request, request.Length);

                    var remote = new IPEndPoint(IPAddress.Any, 0);
                    response = client.Receive(ref remote);
                }

                if (response.Length < 48 || response[1] == 0)
                {
                    // Stratum 0 is a kiss-of-death packet: an answer, but not a time.
                    return false;
                }

                DateTime parsed = ParseNtpTransmitTimestamp(response);
                if (!ClockPlausibilityPolicy.IsUsableReference(parsed))
                {
                    return false;
                }

                utc = parsed;
                return true;
            }
            catch (Exception)
            {
                // Unreachable, blocked or silent: not a fault, just no answer from this
                // one. A closed station network gives this for every server.
                return false;
            }
        }

        private static DateTime ParseNtpTransmitTimestamp(byte[] response)
        {
            ulong seconds = ((ulong)response[40] << 24) | ((ulong)response[41] << 16) |
                            ((ulong)response[42] << 8) | response[43];
            ulong fraction = ((ulong)response[44] << 24) | ((ulong)response[45] << 16) |
                             ((ulong)response[46] << 8) | response[47];

            // NTP counts seconds since 1900 in 32 bits, which runs out in February 2036.
            // A zero top bit means the counter has already wrapped into the next era.
            if ((seconds & 0x80000000UL) == 0)
            {
                seconds += 4294967296UL;
            }

            var epoch = new DateTime(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            return epoch.AddMilliseconds((double)(seconds * 1000UL + (fraction * 1000UL >> 32)));
        }

        /// <summary>
        /// Reads the clock out of an HTTP response header. Weaker than NTP - nothing
        /// authenticates it - and used only when no time server answers, because a
        /// second-accurate date from a web server beats a clock that is years out.
        /// </summary>
        private static bool TryQueryHttpDate(out DateTime utc)
        {
            utc = DateTime.MinValue;
            WebResponse? response = null;
            try
            {
                var request = (HttpWebRequest)WebRequest.Create(HttpDateUrl);
                request.Method = "HEAD";
                request.Timeout = HttpTimeoutMs;
                request.ReadWriteTimeout = HttpTimeoutMs;
                request.AllowAutoRedirect = false;
                request.UserAgent = "IZBAN-Kiosk";
                response = request.GetResponse();
            }
            catch (WebException ex)
            {
                // A refusal still carries a Date header, and a proxy answering 407 is a
                // perfectly good clock for this purpose.
                response = ex.Response;
            }
            catch (Exception)
            {
                return false;
            }

            try
            {
                if (response == null)
                {
                    return false;
                }

                string? header = response.Headers["Date"];
                if (string.IsNullOrEmpty(header))
                {
                    return false;
                }

                DateTime parsed;
                if (!DateTime.TryParse(header, CultureInfo.InvariantCulture,
                        DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out parsed))
                {
                    return false;
                }

                if (!ClockPlausibilityPolicy.IsUsableReference(parsed))
                {
                    return false;
                }

                utc = DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
            finally
            {
                if (response != null)
                {
                    response.Close();
                }
            }
        }

        private string FloorPath()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, FloorFileName);
        }

        /// <summary>
        /// The earliest instant the clock could honestly report. Falls back to when this
        /// installation was written to disk, which is a lower bound nobody has to
        /// maintain: the files cannot have been copied here after the current moment.
        /// </summary>
        private DateTime ReadFloorUtc()
        {
            DateTime lastSeen = DateTime.MinValue;
            try
            {
                string path = FloorPath();
                if (File.Exists(path))
                {
                    DateTime parsed;
                    if (DateTime.TryParse(File.ReadAllText(path).Trim(), CultureInfo.InvariantCulture,
                            DateTimeStyles.RoundtripKind, out parsed))
                    {
                        lastSeen = parsed.ToUniversalTime();
                    }
                }
            }
            catch (Exception)
            {
                // An unreadable floor only costs sensitivity, never correctness.
            }

            return ClockPlausibilityPolicy.Floor(InstallTimeUtc(), lastSeen);
        }

        private static DateTime InstallTimeUtc()
        {
            try
            {
                string location = Assembly.GetExecutingAssembly().Location;
                return location.Length > 0 ? File.GetLastWriteTimeUtc(location) : DateTime.MinValue;
            }
            catch (Exception)
            {
                return DateTime.MinValue;
            }
        }

        private void WriteFloorUtc(DateTime utc)
        {
            try
            {
                File.WriteAllText(FloorPath(), utc.ToString("o", CultureInfo.InvariantCulture));
            }
            catch (Exception)
            {
                // A read-only install folder loses the offline check, not the kiosk.
            }
        }

        private static string DescribeTimeZone()
        {
            try
            {
                TimeZoneInfo zone = TimeZoneInfo.Local;
                TimeSpan offset = zone.GetUtcOffset(DateTime.Now);
                return zone.StandardName + " (UTC" + (offset < TimeSpan.Zero ? "-" : "+") +
                    Math.Abs(offset.Hours).ToString("00", CultureInfo.InvariantCulture) + ":" +
                    Math.Abs(offset.Minutes).ToString("00", CultureInfo.InvariantCulture) + ")";
            }
            catch (Exception)
            {
                return "bilinmiyor";
            }
        }

        internal static string DescribeOffset(TimeSpan offset)
        {
            TimeSpan magnitude = offset < TimeSpan.Zero ? offset.Negate() : offset;
            string direction = offset < TimeSpan.Zero ? "geri" : "ileri";

            if (magnitude.TotalDays >= 1)
            {
                return ((long)magnitude.TotalDays).ToString(CultureInfo.InvariantCulture) + " gün " + direction;
            }
            if (magnitude.TotalHours >= 1)
            {
                return ((long)magnitude.TotalHours).ToString(CultureInfo.InvariantCulture) + " saat " + direction;
            }
            if (magnitude.TotalMinutes >= 1)
            {
                return ((long)magnitude.TotalMinutes).ToString(CultureInfo.InvariantCulture) + " dakika " + direction;
            }
            return ((long)magnitude.TotalSeconds).ToString(CultureInfo.InvariantCulture) + " saniye " + direction;
        }

        private static string BuildMessage(KioskClockStatus status)
        {
            if (status.CorrectionRefused)
            {
                return "Otomatın saati " + DescribeOffset(status.Offset) + " kaymış ve Windows " +
                    "düzeltmeyi kabul etmedi (" + status.RefusalDetail + "). Uygulamayı yönetici " +
                    "olarak çalıştırın veya saati elle düzeltin: 6-Saat-Duzelt.bat";
            }

            if (status.CorrectionApplied)
            {
                return "Saat " + status.Source + " kaynağından düzeltildi (" +
                    DescribeOffset(status.Offset) + " kaymıştı). Bu her açılışta " +
                    "tekrarlanıyorsa anakart pili bitmiştir.";
            }

            switch (status.Verdict)
            {
                case ClockVerdict.Trusted:
                    return "Saat doğru, " + status.Source + " ile karşılaştırıldı.";

                case ClockVerdict.Behind:
                    return "OTOMATIN SAATİ YANLIŞ. Makine " + status.LocalNow + " diyor, oysa en erken " +
                        status.FloorLocal + " olabilir. Bu hâliyle GitHub sertifikası geçersiz görünür " +
                        "ve otomat GÜNCELLEME ALAMAZ. Saati düzeltin: 6-Saat-Duzelt.bat. Her açılışta " +
                        "tekrar bozuluyorsa anakart pilini değiştirin.";

                case ClockVerdict.Ahead:
                    return "OTOMATIN SAATİ YANLIŞ: " + status.LocalNow + " ileri bir tarih. Sertifika " +
                        "doğrulaması bu yüzden başarısız olur ve otomat GÜNCELLEME ALAMAZ. Saati " +
                        "düzeltin: 6-Saat-Duzelt.bat";

                default:
                    return "Zaman sunucusuna ulaşılamadı; kapalı ağda bu normaldir. Saat makinenin " +
                        "kendi geçmişiyle tutarlı, bilinen bir sorun yok.";
            }
        }

        // ---------------------------------------------------------------------
        // Writing the system time. Needs SeSystemtimePrivilege, which an
        // administrator's token carries but leaves DISABLED - so calling
        // SetSystemTime without enabling it first fails with "a required privilege is
        // not held by the client" on a machine that plainly does hold it.
        // ---------------------------------------------------------------------

        private const int TokenAdjustPrivileges = 0x0020;
        private const int TokenQuery = 0x0008;
        private const int PrivilegeEnabled = 0x0002;
        private const int ErrorNotAllAssigned = 1300;
        private const string SystemTimePrivilege = "SeSystemtimePrivilege";

        [StructLayout(LayoutKind.Sequential)]
        private struct SystemTime
        {
            public ushort Year;
            public ushort Month;
            public ushort DayOfWeek;
            public ushort Day;
            public ushort Hour;
            public ushort Minute;
            public ushort Second;
            public ushort Milliseconds;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Luid
        {
            public uint LowPart;
            public int HighPart;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct TokenPrivileges
        {
            public uint PrivilegeCount;
            public Luid Luid;
            public uint Attributes;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetSystemTime(ref SystemTime time);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(IntPtr process, int access, out IntPtr token);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool LookupPrivilegeValue(string? system, string name, out Luid luid);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool AdjustTokenPrivileges(
            IntPtr token, bool disableAll, ref TokenPrivileges newState,
            int bufferLength, IntPtr previous, IntPtr returnLength);

        private static bool TrySetSystemTimeUtc(DateTime utc, out string refusal)
        {
            refusal = string.Empty;
            try
            {
                if (!TryEnableSystemTimePrivilege(out refusal))
                {
                    return false;
                }

                var time = new SystemTime
                {
                    Year = (ushort)utc.Year,
                    Month = (ushort)utc.Month,
                    DayOfWeek = (ushort)utc.DayOfWeek,
                    Day = (ushort)utc.Day,
                    Hour = (ushort)utc.Hour,
                    Minute = (ushort)utc.Minute,
                    Second = (ushort)utc.Second,
                    Milliseconds = (ushort)utc.Millisecond
                };

                if (SetSystemTime(ref time))
                {
                    return true;
                }

                refusal = "SetSystemTime hata " +
                    Marshal.GetLastWin32Error().ToString(CultureInfo.InvariantCulture);
                return false;
            }
            catch (Exception ex)
            {
                refusal = ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        private static bool TryEnableSystemTimePrivilege(out string refusal)
        {
            refusal = string.Empty;
            IntPtr token = IntPtr.Zero;
            try
            {
                if (!OpenProcessToken(GetCurrentProcess(), TokenAdjustPrivileges | TokenQuery, out token))
                {
                    refusal = "OpenProcessToken hata " +
                        Marshal.GetLastWin32Error().ToString(CultureInfo.InvariantCulture);
                    return false;
                }

                Luid luid;
                if (!LookupPrivilegeValue(null, SystemTimePrivilege, out luid))
                {
                    refusal = "LookupPrivilegeValue hata " +
                        Marshal.GetLastWin32Error().ToString(CultureInfo.InvariantCulture);
                    return false;
                }

                var privileges = new TokenPrivileges
                {
                    PrivilegeCount = 1,
                    Luid = luid,
                    Attributes = PrivilegeEnabled
                };

                // Reports success even when it changed nothing, so the last error is the
                // only thing that distinguishes "enabled" from "this account may not".
                AdjustTokenPrivileges(token, false, ref privileges, 0, IntPtr.Zero, IntPtr.Zero);
                int error = Marshal.GetLastWin32Error();
                if (error == ErrorNotAllAssigned)
                {
                    refusal = "hesapta saat değiştirme yetkisi yok";
                    return false;
                }
                if (error != 0)
                {
                    refusal = "AdjustTokenPrivileges hata " + error.ToString(CultureInfo.InvariantCulture);
                    return false;
                }
                return true;
            }
            finally
            {
                if (token != IntPtr.Zero)
                {
                    CloseHandle(token);
                }
            }
        }
    }
}
