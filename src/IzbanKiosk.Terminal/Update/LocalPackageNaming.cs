using System;

namespace IzbanKiosk.Terminal.Update
{
    /// <summary>
    /// Reads a version out of an update package's file name, and refuses the files that
    /// must never be installed as an update.
    ///
    /// Pure, because the one that matters cannot be discovered by testing on a kiosk:
    /// the USB setup archive is named almost identically to the release archive and
    /// carries a 120 MB .NET installer at its root. Copied over an installation it would
    /// dump that installer into the kiosk's own folder, and the mistake would only
    /// surface as a disk filling up on machines nobody was watching.
    /// </summary>
    internal static class LocalPackageNaming
    {
        /// <summary>
        /// The folder an operator drops packages into. Deliberately specific: a kiosk
        /// must not install the first .zip somebody happens to leave on a stick.
        /// </summary>
        internal const string FolderName = "IZBAN-Kiosk-Guncelleme";

        private const string SetupArchiveMarker = "-KURULUM";

        /// <summary>
        /// Returns the version encoded in <paramref name="fileName"/>, or null when the
        /// file is not an installable kiosk package. Names follow what the release
        /// script produces: <c>IZBAN-Kiosk-v1.0.31.zip</c>.
        /// </summary>
        internal static Version? ReadVersion(string fileName)
        {
            string name = (fileName ?? string.Empty).Trim();
            if (name.Length == 0 || !name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            string stem = name.Substring(0, name.Length - 4);

            // The USB setup archive. Never an update: it carries the .NET installer.
            if (stem.IndexOf(SetupArchiveMarker, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return null;
            }

            if (!stem.StartsWith("IZBAN-Kiosk-", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return GitHubReleaseClient.ParseVersion(stem.Substring("IZBAN-Kiosk-".Length));
        }

        /// <summary>
        /// The tag a local package reports, so the applied-tag guard that stops a
        /// failed install repeating forever works the same way it does for GitHub.
        /// </summary>
        internal static string TagFor(Version version)
        {
            return "v" + version.Major + "." + version.Minor + "." + Math.Max(version.Build, 0);
        }
    }
}
