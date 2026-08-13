using System;
using System.Globalization;
using System.IO;
using System.Text;
using Newtonsoft.Json;

namespace IzbanKiosk.Terminal
{
    /// <summary>
    /// Append-only local record of what the kiosk did, one JSON object per line under
    /// <c>Journal\kiosk-yyyy-MM-dd.jsonl</c>.
    ///
    /// Deliberately file-based rather than SQLite: the Windows 7 image carries no
    /// x86 SQLite native library, and a write-filtered embedded disk copes far better
    /// with appended text than with a database file it may roll back mid-write.
    ///
    /// Only pseudonymous identifiers are written. The İzmirim Kart number and the NFC
    /// UID are shown on screen and printed on the passenger's slip, but never stored.
    /// </summary>
    internal sealed class KioskJournal
    {
        private readonly object _sync = new object();
        private readonly string _directory;

        /// <summary>
        /// Days of journal kept on the machine. A kiosk runs for years unattended and
        /// nobody visits to sweep up; one file a day left forever eventually fills the
        /// disk of a Windows Embedded image that has very little to spare, and a full
        /// disk stops the kiosk far more thoroughly than a missing old log ever could.
        /// A year is well past any reconciliation window and still bounded.
        /// </summary>
        private const int RetentionDays = 365;

        private bool _pruned;

        internal KioskJournal()
        {
            _directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Journal");
        }

        /// <summary>
        /// Deletes journals past the retention window. Runs once per process rather
        /// than on every write: a kiosk restarts often enough, and scanning the folder
        /// on each card read would be work for nothing.
        /// </summary>
        private void PruneOnce()
        {
            if (_pruned)
            {
                return;
            }
            _pruned = true;

            try
            {
                DateTime cutoff = DateTime.Now.AddDays(-RetentionDays);
                foreach (string file in Directory.GetFiles(_directory, "kiosk-*.jsonl"))
                {
                    if (File.GetLastWriteTime(file) < cutoff)
                    {
                        File.Delete(file);
                    }
                }
            }
            catch (Exception)
            {
                // Housekeeping must never cost the kiosk a journal entry; the write
                // that follows is the part that matters.
            }
        }

        internal string LastErrorMessage { get; private set; } = string.Empty;

        internal void Record(string eventName, object payload)
        {
            lock (_sync)
            {
                try
                {
                    if (!Directory.Exists(_directory))
                    {
                        Directory.CreateDirectory(_directory);
                    }

                    PruneOnce();

                    string path = Path.Combine(
                        _directory,
                        "kiosk-" + DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".jsonl");

                    string line = JsonConvert.SerializeObject(new
                    {
                        atUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
                        atLocal = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                        eventName,
                        payload
                    });

                    File.AppendAllText(path, line + Environment.NewLine, Encoding.UTF8);
                    LastErrorMessage = string.Empty;
                }
                catch (Exception ex)
                {
                    // A journal that cannot be written must never take the kiosk down;
                    // the passenger-facing flow does not depend on it.
                    LastErrorMessage = ex.GetType().Name + ": " + ex.Message;
                }
            }
        }
    }
}
