namespace IzbanKiosk.Terminal.Update
{
    /// <summary>
    /// Where a kiosk gets its next version from.
    ///
    /// İZBAN's station network has no route to the internet, so GitHub cannot be the
    /// only answer. The whole install path either side of this - version comparison,
    /// waiting for the terminal to be idle, staging, the swap script, the restart, the
    /// applied-tag guard - is the part that took the longest to get right and is
    /// identical whichever way the package arrived. Only the two steps that differ are
    /// behind this interface.
    /// </summary>
    internal interface IReleaseSource
    {
        /// <summary>
        /// Short, technician-facing description of where this looks, for the
        /// diagnostics screen. A kiosk that is not updating must say where it was
        /// looking before anyone can work out why nothing arrived.
        /// </summary>
        string Describe();

        /// <summary>
        /// True when a version older than the running one should be installed. GitHub
        /// says yes - deleting a release is how an operator recalls a bad build - but a
        /// folder on a USB stick says no: a stick left in a drawer for a month would
        /// otherwise walk every kiosk it touched back to an old version, silently.
        /// </summary>
        bool SupportsRollback { get; }

        /// <summary>
        /// True when a newer package should be installed as soon as the terminal is
        /// free, instead of waiting for the nightly window.
        ///
        /// GitHub waits: a release reaching the whole fleet at once during service is a
        /// change nobody asked for at that moment. A USB stick is the opposite - it did
        /// not appear on its own, somebody walked to the machine and plugged it in, and
        /// they need to see the result before they leave the station.
        /// </summary>
        bool InstallsImmediately { get; }

        GitHubRelease? GetLatestRelease();

        /// <summary>Puts the package on local disk and returns its path.</summary>
        string DownloadPackage(GitHubRelease release, string targetDirectory);
    }
}
