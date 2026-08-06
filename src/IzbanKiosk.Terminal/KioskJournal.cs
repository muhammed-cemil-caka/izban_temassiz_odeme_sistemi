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

        internal KioskJournal()
        {
            _directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Journal");
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
