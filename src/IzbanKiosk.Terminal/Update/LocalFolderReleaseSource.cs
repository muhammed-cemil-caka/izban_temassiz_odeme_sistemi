using System;
using System.Collections.Generic;
using System.IO;

namespace IzbanKiosk.Terminal.Update
{
    /// <summary>
    /// Finds update packages on this machine instead of on the internet: a USB stick an
    /// engineer carries between stations, or a folder on a local file share.
    ///
    /// This is what makes the update path work at all inside İZBAN's closed network,
    /// where GitHub is not reachable from any kiosk. Everything after the package is
    /// found is unchanged - the same idle wait, the same staging, the same swap script,
    /// the same restart - so a kiosk updated from a stick and one updated from GitHub
    /// end up in exactly the same state.
    ///
    /// Trust boundary, stated plainly: this accepts any package placed in the well-known
    /// folder. It authenticates nothing, and the checksum only catches corruption, not a
    /// deliberately substituted file. That is acceptable because it does not widen the
    /// boundary that already exists - somebody standing at the machine with a USB stick
    /// can already copy files over the installation by hand, which is exactly how field
    /// updates are distributed today. It must not be enabled on a kiosk that can reach
    /// GitHub, where the signed-over-TLS path is the better one.
    /// </summary>
    internal sealed class LocalFolderReleaseSource : IReleaseSource
    {
        private readonly string[] _configuredFolders;
        private readonly Action<string> _log;
        private string _lastFolderSearched = string.Empty;

        internal LocalFolderReleaseSource(string[] configuredFolders, Action<string> log)
        {
            _configuredFolders = configuredFolders;
            _log = log;
        }

        public string Describe()
        {
            if (_configuredFolders.Length > 0)
            {
                return "Yerel klasör: " + string.Join(", ", _configuredFolders);
            }
            return "USB / yerel sürücüler: \\" + LocalPackageNaming.FolderName;
        }

        /// <summary>
        /// Never. A stick forgotten in a drawer for a month would otherwise walk every
        /// kiosk it touched back to whatever version it happens to hold, and nobody
        /// would connect the downgrade to the stick.
        /// </summary>
        public bool SupportsRollback
        {
            get { return false; }
        }

        public GitHubRelease? GetLatestRelease()
        {
            GitHubRelease? best = null;
            var searched = new List<string>();

            foreach (string folder in CandidateFolders())
            {
                searched.Add(folder);
                foreach (string file in SafeListZips(folder))
                {
                    Version? version = LocalPackageNaming.ReadVersion(Path.GetFileName(file));
                    if (version == null)
                    {
                        continue;
                    }

                    if (best != null && best.Version != null &&
                        UpdateDecisionPolicy.Compare(version, best.Version) <= 0)
                    {
                        continue;
                    }

                    best = new GitHubRelease
                    {
                        Tag = LocalPackageNaming.TagFor(version),
                        Version = version,
                        PackageUrl = file,
                        PackageName = Path.GetFileName(file),
                        ChecksumUrl = File.Exists(file + ".sha256") ? file + ".sha256" : string.Empty
                    };
                }
            }

            _lastFolderSearched = searched.Count == 0
                ? string.Empty
                : string.Join(", ", searched.ToArray());
            return best;
        }

        /// <summary>Folders actually looked at during the last check, for the panel.</summary>
        internal string LastFolderSearched
        {
            get { return _lastFolderSearched; }
        }

        private IEnumerable<string> CandidateFolders()
        {
            foreach (string folder in _configuredFolders)
            {
                if (folder.Length > 0 && SafeDirectoryExists(folder))
                {
                    yield return folder;
                }
            }

            // Every drive that is ready, so a stick works whatever letter Windows gave
            // it. Which letter a USB device lands on is not something an engineer
            // visiting a station can control or should have to know.
            foreach (string root in SafeDriveRoots())
            {
                string folder = Path.Combine(root, LocalPackageNaming.FolderName);
                if (SafeDirectoryExists(folder))
                {
                    yield return folder;
                }
            }
        }

        private IEnumerable<string> SafeDriveRoots()
        {
            DriveInfo[] drives;
            try
            {
                drives = DriveInfo.GetDrives();
            }
            catch (Exception)
            {
                yield break;
            }

            foreach (DriveInfo drive in drives)
            {
                string root;
                try
                {
                    // IsReady is false for an empty card reader or a disconnected
                    // network drive, and touching one of those throws rather than
                    // returning nothing.
                    if (!drive.IsReady)
                    {
                        continue;
                    }
                    root = drive.RootDirectory.FullName;
                }
                catch (Exception)
                {
                    continue;
                }
                yield return root;
            }
        }

        private static bool SafeDirectoryExists(string path)
        {
            try
            {
                return Directory.Exists(path);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private string[] SafeListZips(string folder)
        {
            try
            {
                return Directory.GetFiles(folder, "*.zip");
            }
            catch (Exception ex)
            {
                _log("Update folder could not be read: " + folder + " (" + ex.Message + ")");
                return new string[0];
            }
        }

        public string DownloadPackage(GitHubRelease release, string targetDirectory)
        {
            if (!Directory.Exists(targetDirectory))
            {
                Directory.CreateDirectory(targetDirectory);
            }

            // Copied rather than opened in place: the install runs minutes later and a
            // stick pulled out in between would fail the update halfway through the
            // swap, which is the one moment a kiosk cannot survive.
            string packagePath = Path.Combine(targetDirectory, "update.zip");
            File.Copy(release.PackageUrl, packagePath, true);

            if (release.ChecksumUrl.Length > 0)
            {
                string expected = ReadChecksum(release.ChecksumUrl);
                string actual = GitHubReleaseClient.ComputeSha256(packagePath);
                if (expected.Length > 0 && !string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(packagePath);
                    throw new InvalidDataException(
                        "Paketin sağlama toplamı yanındaki .sha256 dosyasıyla uyuşmuyor: " +
                        release.PackageName + ". Kopyalama sırasında bozulmuş olabilir.");
                }
            }

            return packagePath;
        }

        private static string ReadChecksum(string path)
        {
            try
            {
                string text = File.ReadAllText(path).Trim();
                int space = text.IndexOf(' ');
                return space > 0 ? text.Substring(0, space) : text;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }
    }
}
