using System;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;

namespace IzbanKiosk.LegacyHardwareBridge.Card
{
    /// <summary>
    /// Writes the top-up flow's record of what happened to a passenger's money.
    ///
    /// The saga notes its intent before the card is charged, so that a kiosk which
    /// loses power mid-transaction leaves evidence a transaction was in flight. That
    /// only holds if the note reaches a disk: routed to a no-op callback, as it was,
    /// the whole trail existed in comments and nowhere else.
    ///
    /// Writes into the same <c>Journal</c> folder the kiosk shell uses, one file per
    /// day, so a technician reconciling a disputed load has everything in one place
    /// rather than two halves in two processes.
    /// </summary>
    public sealed class BridgeJournal
    {
        private readonly object _sync = new object();
        private readonly string _directory;

        public BridgeJournal()
        {
            // The bridge lives in Bridge\ beneath the kiosk, so the shared journal is
            // one level up. Falling back to its own folder is better than losing the
            // record when the layout is not what we expect.
            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string shared = Path.GetFullPath(Path.Combine(baseDirectory, "..", "Journal"));
            string local = Path.Combine(baseDirectory, "Journal");
            _directory = Directory.Exists(Path.GetFullPath(Path.Combine(baseDirectory, ".."))) ? shared : local;
        }

        public void Record(string eventName, object payload)
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
                        source = "bridge",
                        eventName,
                        payload
                    });

                    File.AppendAllText(path, line + Environment.NewLine);
                }
                catch (Exception)
                {
                    // A journal that cannot be written must not abort a transaction
                    // that is already under way; the card and the payment matter more
                    // than the note about them.
                }
            }
        }
    }
}
