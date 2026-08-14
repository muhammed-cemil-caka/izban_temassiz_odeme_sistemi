using System;

namespace IzbanKiosk.Terminal.Timekeeping
{
    internal enum ClockVerdict
    {
        /// <summary>Compared against an external time reference during this run.</summary>
        Trusted,

        /// <summary>
        /// No reference was reachable - the normal case on a closed station network -
        /// but nothing the kiosk knows about itself contradicts the clock.
        /// </summary>
        Unverified,

        /// <summary>
        /// The clock reads earlier than a moment this machine has already lived
        /// through, so it is wrong beyond argument.
        /// </summary>
        Behind,

        /// <summary>The clock is implausibly far ahead of anything this build could have seen.</summary>
        Ahead
    }

    /// <summary>
    /// Decides whether the machine's clock can be believed, and whether an external
    /// reference is worth acting on.
    ///
    /// A kiosk whose clock is wrong looks perfectly healthy: it reads cards, shows
    /// balances and prints receipts. What silently stops is the update path, because
    /// GitHub's certificate is judged against the system clock and a machine that
    /// thinks it is 2010 rejects a certificate issued in 2026. The failure surfaces as
    /// a bare TLS error that names neither the clock nor the date, so the kiosk simply
    /// stops receiving fixes for as long as it stands there.
    ///
    /// The offline half of this matters as much as the online half. A station kiosk may
    /// never reach a time server, and refusing to judge the clock without one would
    /// leave the whole check useless exactly where it is needed. So the fallback
    /// reference is the machine's own history: time cannot run backwards, and any
    /// reading earlier than a moment already recorded is wrong without needing anyone's
    /// permission to say so.
    ///
    /// Pure and free of clocks, sockets and files, because a wrong-clock kiosk is not
    /// something anyone can stage on demand.
    /// </summary>
    internal static class ClockPlausibilityPolicy
    {
        /// <summary>
        /// How far the clock may differ from a reference before it is worth setting.
        /// Below this the correction would cost more than the error: writing the system
        /// time is a privileged call, and a second either way changes nothing for
        /// certificates or receipts.
        /// </summary>
        internal static readonly TimeSpan CorrectionThreshold = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Slack before a clock that reads earlier than the recorded floor is called
        /// wrong. A legitimate correction can move the clock backwards by minutes, and
        /// the fault this guards against - a dead CMOS battery resetting the machine to
        /// its manufacturing year - misses by years, not by an hour.
        /// </summary>
        internal static readonly TimeSpan BehindTolerance = TimeSpan.FromHours(1);

        /// <summary>
        /// How far past the floor a clock may read before it is treated as wrong. Five
        /// years is longer than any gap between a build and the kiosk running it, and
        /// short enough to catch a clock that has jumped to 2099.
        /// </summary>
        internal static readonly TimeSpan FutureAllowance = TimeSpan.FromDays(5 * 365);

        /// <summary>
        /// The earliest instant the clock could honestly report: the later of when this
        /// installation appeared on the disk and the last time the kiosk recorded
        /// itself running.
        /// </summary>
        internal static DateTime Floor(DateTime installedUtc, DateTime lastSeenUtc)
        {
            return lastSeenUtc > installedUtc ? lastSeenUtc : installedUtc;
        }

        internal static ClockVerdict Judge(DateTime nowUtc, DateTime floorUtc, bool verifiedExternally)
        {
            if (verifiedExternally)
            {
                // A reference was reached and any difference has already been applied,
                // so the machine's own history has nothing left to add.
                return ClockVerdict.Trusted;
            }

            if (nowUtc < floorUtc - BehindTolerance)
            {
                return ClockVerdict.Behind;
            }

            if (nowUtc > floorUtc + FutureAllowance)
            {
                return ClockVerdict.Ahead;
            }

            return ClockVerdict.Unverified;
        }

        internal static bool ShouldCorrect(DateTime nowUtc, DateTime referenceUtc)
        {
            TimeSpan difference = nowUtc - referenceUtc;
            if (difference < TimeSpan.Zero)
            {
                difference = difference.Negate();
            }
            return difference > CorrectionThreshold;
        }

        /// <summary>
        /// Whether this reading may be written back as the new floor.
        ///
        /// A clock that has jumped years into the future must never be recorded, or it
        /// poisons the floor permanently: every later reading, including the correct
        /// one that follows a repair, would be judged <see cref="ClockVerdict.Behind"/>
        /// and the kiosk would spend the rest of its life reporting a fault it no
        /// longer has.
        /// </summary>
        internal static bool MayRaiseFloor(ClockVerdict verdict)
        {
            return verdict == ClockVerdict.Trusted || verdict == ClockVerdict.Unverified;
        }

        /// <summary>
        /// True when the clock is wrong in a way that makes reaching GitHub impossible,
        /// so the updater can say so instead of reporting a TLS error nobody can act on.
        /// </summary>
        internal static bool BreaksSecureConnections(ClockVerdict verdict)
        {
            return verdict == ClockVerdict.Behind || verdict == ClockVerdict.Ahead;
        }

        /// <summary>
        /// Rejects an answer that cannot be a real date, whatever it claims to be.
        /// The reference is unauthenticated - plain NTP, or an HTTP response header -
        /// so a garbled or hostile reply must not be allowed to set the very clock this
        /// exists to protect.
        /// </summary>
        internal static bool IsUsableReference(DateTime referenceUtc)
        {
            return referenceUtc.Year >= 2020 && referenceUtc.Year <= 2100;
        }
    }
}
