using System;

namespace IzbanKiosk.Terminal.Update
{
    internal enum UpdateAction
    {
        None,

        /// <summary>A newer release is published. Installed only in the nightly window.</summary>
        Upgrade,

        /// <summary>
        /// The published release is older than what this kiosk runs, which happens when
        /// a bad release is deleted. Applied as soon as the terminal is idle.
        /// </summary>
        Rollback
    }

    /// <summary>
    /// Decides whether to install anything, and in which direction.
    ///
    /// Upgrades wait for the nightly hour: a new release reaching a hundred kiosks at
    /// once during service is a change nobody asked for at that moment. Withdrawals
    /// are the opposite. Deleting a release from GitHub is what an operator does when
    /// the version now running is misbehaving, and making them wait until 04:00 for
    /// every kiosk to come back means a whole day on a build already known to be bad.
    /// So a published version that is behind the running one is treated as a recall
    /// and applied at the next check.
    ///
    /// Pure and free of clocks and networks, because the version arithmetic here is
    /// the part that is easy to get quietly wrong and impossible to observe on a
    /// kiosk until every machine in the fleet is doing something unexpected.
    /// </summary>
    internal static class UpdateDecisionPolicy
    {
        /// <summary>
        /// Compares only major.minor.build.
        ///
        /// A tag reads as "1.0.27" and parses to a Version whose Revision is -1, while
        /// the assembly reports 1.0.27.0. Compared as they are, the published release
        /// is permanently "older" than the identical build running on the kiosk - which
        /// would make every machine in the fleet recall itself, every single check.
        /// </summary>
        internal static int Compare(Version left, Version right)
        {
            var l = new Version(left.Major, left.Minor, Math.Max(left.Build, 0));
            var r = new Version(right.Major, right.Minor, Math.Max(right.Build, 0));
            return l.CompareTo(r);
        }

        /// <param name="appliedTag">
        /// The last tag this kiosk installed. When it matches the tag on offer and the
        /// running version still has not moved, the install is not taking effect and
        /// repeating it every check would achieve nothing but traffic.
        /// </param>
        /// <param name="installImmediately">
        /// Skips the nightly window. True for a package found on a local drive: somebody
        /// is standing at the machine having just plugged a stick into it, and that is
        /// the instruction. Making them wait until 04:00 would mean the engineer leaves
        /// the station without knowing whether the update worked - which is the whole
        /// reason the visit happened.
        /// </param>
        internal static UpdateAction Decide(
            Version current,
            Version? latest,
            string latestTag,
            string appliedTag,
            int hourNow,
            int scheduledHour,
            bool rollbackEnabled,
            bool installImmediately = false)
        {
            if (latest == null)
            {
                return UpdateAction.None;
            }

            if (string.Equals(latestTag ?? string.Empty, appliedTag ?? string.Empty,
                    StringComparison.OrdinalIgnoreCase))
            {
                return UpdateAction.None;
            }

            int order = Compare(latest, current);
            if (order == 0)
            {
                return UpdateAction.None;
            }

            if (order < 0)
            {
                return rollbackEnabled ? UpdateAction.Rollback : UpdateAction.None;
            }

            return installImmediately || hourNow == scheduledHour
                ? UpdateAction.Upgrade
                : UpdateAction.None;
        }
    }
}
