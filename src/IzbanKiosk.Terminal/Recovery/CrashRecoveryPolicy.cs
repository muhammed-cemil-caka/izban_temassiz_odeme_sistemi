using System;
using System.Collections.Generic;
using System.Globalization;

namespace IzbanKiosk.Terminal.Recovery
{
    /// <summary>
    /// Decides what to do after the kiosk has crashed: start again, or stop trying and
    /// hand the machine back to a person.
    ///
    /// The distinction matters because the kiosk can be the Windows shell. A one-off
    /// crash should simply restart - a passenger sees a flicker and the machine keeps
    /// working. But a fault that reproduces every time turns restarting into a loop
    /// that leaves a station kiosk showing nothing at all, with no desktop and no way
    /// in short of visiting it. After enough crashes in a short window the machine is
    /// deliberately returned to the Windows desktop instead, because a kiosk somebody
    /// can log into is worth more than one that keeps relaunching into the same fault.
    ///
    /// Kept free of Windows and file-system dependencies so the counting rules can be
    /// tested; a crash loop is not something anyone can stage on a real kiosk.
    /// </summary>
    public static class CrashRecoveryPolicy
    {
        /// <summary>
        /// Crashes within <see cref="Window"/> that mean restarting is pointless.
        /// Three allows for two unlucky restarts before the machine is handed back.
        /// </summary>
        public const int CrashLimit = 3;

        public static readonly TimeSpan Window = TimeSpan.FromMinutes(10);

        /// <summary>
        /// Drops crashes older than the window. Without this a kiosk that crashed
        /// twice months apart would fall back to the desktop on its next single
        /// crash, for no reason anyone could see.
        /// </summary>
        public static List<DateTime> Recent(IEnumerable<DateTime> crashes, DateTime now)
        {
            var recent = new List<DateTime>();
            if (crashes == null)
            {
                return recent;
            }

            foreach (DateTime crash in crashes)
            {
                if (now - crash <= Window && crash <= now)
                {
                    recent.Add(crash);
                }
            }
            return recent;
        }

        /// <summary>
        /// True when the crash just recorded was one too many and the kiosk should
        /// give the machine back rather than relaunch into the same fault.
        /// </summary>
        public static bool ShouldStopRestarting(IEnumerable<DateTime> crashesIncludingThisOne, DateTime now)
        {
            return Recent(crashesIncludingThisOne, now).Count >= CrashLimit;
        }

        /// <summary>
        /// One crash time per line, oldest first, round-trippable and readable by a
        /// technician opening the file in Notepad.
        /// </summary>
        public static string Serialise(IEnumerable<DateTime> crashes)
        {
            var lines = new List<string>();
            foreach (DateTime crash in crashes)
            {
                lines.Add(crash.ToString("o", CultureInfo.InvariantCulture));
            }
            return string.Join(Environment.NewLine, lines.ToArray());
        }

        /// <summary>
        /// Unreadable lines are skipped rather than treated as a crash. A corrupt
        /// counter file must not be able to push a healthy kiosk into fallback.
        /// </summary>
        public static List<DateTime> Deserialise(string text)
        {
            var crashes = new List<DateTime>();
            if (string.IsNullOrEmpty(text))
            {
                return crashes;
            }

            foreach (string line in text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                DateTime parsed;
                if (DateTime.TryParse(line.Trim(), CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind, out parsed))
                {
                    crashes.Add(parsed);
                }
            }
            return crashes;
        }
    }
}
