using System;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;

namespace IzbanKiosk.Terminal.Update
{
    internal sealed class GitHubRelease
    {
        internal string Tag = string.Empty;
        internal Version? Version;
        internal string PackageUrl = string.Empty;
        internal string PackageName = string.Empty;
        internal string ChecksumUrl = string.Empty;
    }

    /// <summary>
    /// Reads the repository's latest release and fetches its package.
    ///
    /// Only the GitHub API over HTTPS is used, and only the release's own assets are
    /// downloaded: the kiosk installs code from this one source and nowhere else.
    ///
    /// Useless on a kiosk with no route to the internet, which is why it is one
    /// <see cref="IReleaseSource"/> among others rather than the only way in.
    /// </summary>
    internal sealed class GitHubReleaseClient : IReleaseSource
    {
        private const string UserAgent = "IZBAN-Kiosk-Updater";
        private const int RequestTimeoutMs = 60000;

        private readonly string _owner;
        private readonly string _repository;

        internal GitHubReleaseClient(string owner, string repository)
        {
            _owner = owner;
            _repository = repository;
            EnableModernTls();
        }

        public string Describe()
        {
            return "GitHub: " + _owner + "/" + _repository;
        }

        /// <summary>
        /// Deleting a release from GitHub is how an operator recalls a bad build, so a
        /// published version behind the running one is a deliberate instruction.
        /// </summary>
        public bool SupportsRollback
        {
            get { return true; }
        }

        /// <summary>
        /// No. A release published at midday must not restart a hundred kiosks in the
        /// middle of service; that is what the nightly window is for.
        /// </summary>
        public bool InstallsImmediately
        {
            get { return false; }
        }

        /// <summary>
        /// .NET Framework 4.0 negotiates SSL3/TLS1.0 by default and GitHub refuses
        /// both. The TLS 1.2 enum value did not exist until 4.5, so it is applied by
        /// its numeric value; this works whenever the machine has 4.5 or later
        /// installed, which is the documented requirement for updates.
        /// </summary>
        private static void EnableModernTls()
        {
            try
            {
                ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            }
            catch (NotSupportedException)
            {
                // Only .NET 4.0 is present: the check will fail later with a clear
                // network error rather than crashing here.
            }
        }

        public GitHubRelease? GetLatestRelease()
        {
            string url = "https://api.github.com/repos/" + _owner + "/" + _repository + "/releases/latest";
            string json = DownloadString(url);
            JObject payload = JObject.Parse(json);

            var release = new GitHubRelease
            {
                Tag = (string?)payload["tag_name"] ?? string.Empty
            };

            if (release.Tag.Length == 0)
            {
                return null;
            }

            release.Version = ParseVersion(release.Tag);

            JToken? assets = payload["assets"];
            if (assets != null)
            {
                foreach (JToken asset in assets)
                {
                    string name = (string?)asset["name"] ?? string.Empty;
                    string downloadUrl = (string?)asset["browser_download_url"] ?? string.Empty;
                    if (downloadUrl.Length == 0)
                    {
                        continue;
                    }

                    if (name.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase))
                    {
                        release.ChecksumUrl = downloadUrl;
                    }
                    else if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && release.PackageUrl.Length == 0)
                    {
                        release.PackageUrl = downloadUrl;
                        release.PackageName = name;
                    }
                }
            }

            return release.PackageUrl.Length == 0 ? null : release;
        }

        /// <summary>
        /// Accepts tags such as <c>v1.4.0</c>, <c>R24</c> or <c>1.4</c>. Returns null
        /// when no version can be read, and the caller then refuses to install:
        /// guessing an ordering between unparseable tags could downgrade a kiosk.
        /// </summary>
        internal static Version? ParseVersion(string tag)
        {
            var digits = new StringBuilder();
            foreach (char character in tag)
            {
                if (char.IsDigit(character) || character == '.')
                {
                    digits.Append(character);
                }
                else if (digits.Length > 0)
                {
                    break;
                }
            }

            string cleaned = digits.ToString().Trim('.');
            if (cleaned.Length == 0)
            {
                return null;
            }
            if (cleaned.IndexOf('.') < 0)
            {
                cleaned += ".0";
            }

            Version parsed;
            return Version.TryParse(cleaned, out parsed) ? parsed : null;
        }

        public string DownloadPackage(GitHubRelease release, string targetDirectory)
        {
            if (!Directory.Exists(targetDirectory))
            {
                Directory.CreateDirectory(targetDirectory);
            }

            string packagePath = Path.Combine(targetDirectory, "update.zip");
            DownloadFile(release.PackageUrl, packagePath);

            if (release.ChecksumUrl.Length > 0)
            {
                string expected = ExtractChecksum(DownloadString(release.ChecksumUrl));
                string actual = ComputeSha256(packagePath);
                if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(packagePath);
                    throw new InvalidDataException(
                        "Update package checksum does not match the published .sha256 asset.");
                }
            }

            return packagePath;
        }

        private static string ExtractChecksum(string checksumFileContent)
        {
            // Accepts both a bare hash and the "hash  filename" form sha256sum writes.
            string text = (checksumFileContent ?? string.Empty).Trim();
            int space = text.IndexOf(' ');
            return space > 0 ? text.Substring(0, space) : text;
        }

        internal static string ComputeSha256(string path)
        {
            using (var sha = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
            {
                byte[] hash = sha.ComputeHash(stream);
                var builder = new StringBuilder(hash.Length * 2);
                foreach (byte value in hash)
                {
                    builder.Append(value.ToString("x2"));
                }
                return builder.ToString();
            }
        }

        private static string DownloadString(string url)
        {
            HttpWebRequest request = CreateRequest(url);
            request.Accept = "application/vnd.github+json";
            using (WebResponse response = request.GetResponse())
            using (Stream stream = response.GetResponseStream())
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            {
                return reader.ReadToEnd();
            }
        }

        private static void DownloadFile(string url, string targetPath)
        {
            HttpWebRequest request = CreateRequest(url);
            request.Accept = "application/octet-stream";
            using (WebResponse response = request.GetResponse())
            using (Stream stream = response.GetResponseStream())
            using (FileStream file = File.Create(targetPath))
            {
                var buffer = new byte[81920];
                int read;
                while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    file.Write(buffer, 0, read);
                }
            }
        }

        private static HttpWebRequest CreateRequest(string url)
        {
            if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Updates are only fetched over HTTPS.");
            }

            var request = (HttpWebRequest)WebRequest.Create(url);
            request.UserAgent = UserAgent;
            request.Timeout = RequestTimeoutMs;
            request.ReadWriteTimeout = RequestTimeoutMs;
            return request;
        }
    }
}
