using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;

namespace IzbanKioskApp.Services
{
    public static class UpdateManager
    {
        private const string DefaultOwner = "muhammed-cemil-caka";
        private const string DefaultRepo = "izban_temassiz_odeme_sistemi";

        // Authoritative production ECDsa public key (P-256)
        private const string AuthoritativePublicKeyPem = 
            "-----BEGIN PUBLIC KEY-----\n" +
            "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE1aP3C9Jp9kM0jU2wW8vB8o2z8X/f\n" +
            "mQ8wX9y6g5zZ9zY0d5pM+M2P7z4qy9d7u4d14j7U1y6z2Vb5K6Jz+gX9Zw==\n" +
            "-----END PUBLIC KEY-----";

        public static async Task StartUpdateSchedulerAsync()
        {
            Console.WriteLine("[UPDATE] Kiosk startup: Initial update check...");
            try
            {
                await CheckAndPerformUpdateAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UPDATE FAIL] Initial update check failed: {ex.Message}");
            }

            while (true)
            {
                var now = DateTime.Now;
                var targetTime = new DateTime(now.Year, now.Month, now.Day, 4, 0, 0);
                if (now >= targetTime)
                {
                    targetTime = targetTime.AddDays(1);
                }

                var delay = targetTime - now;
                Console.WriteLine($"[UPDATE] Next update scheduler check scheduled at {targetTime:yyyy-MM-dd HH:mm:ss}.");
                await Task.Delay(delay);

                while (AppServices.IsUserActive)
                {
                    Console.WriteLine("[UPDATE] Kiosk is active. Delaying check for 10 minutes...");
                    await Task.Delay(TimeSpan.FromMinutes(10));
                }

                Console.WriteLine("[UPDATE] Starting scheduled update check...");
                try
                {
                    await CheckAndPerformUpdateAsync();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[UPDATE FAIL] Scheduled check failed: {ex.Message}");
                }
            }
        }

        public static async Task CheckAndPerformUpdateAsync(
            string owner = DefaultOwner, 
            string repo = DefaultRepo)
        {
            if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS())
            {
                Console.WriteLine("[UPDATE] Auto-update is disabled on this platform.");
                return;
            }

            string? currentProcessPath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(currentProcessPath))
            {
                Console.WriteLine("[UPDATE] Could not resolve current process path. Update cancelled.");
                return;
            }

            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.UserAgent.ParseAdd("IzbanKioskApp-Updater");
                client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github.v3+json");

                string apiUrl = $"https://api.github.com/repos/{owner}/{repo}/releases/latest";
                string jsonString = await client.GetStringAsync(apiUrl);

                using var doc = JsonDocument.Parse(jsonString);
                var root = doc.RootElement;

                if (!root.TryGetProperty("tag_name", out var tagProp))
                {
                    Console.WriteLine("[UPDATE] No tag_name in latest release response.");
                    return;
                }

                string tagName = tagProp.GetString() ?? "";
                string cleanTag = tagName.TrimStart('v', 'V');

                if (!Version.TryParse(cleanTag, out Version? githubVersion))
                {
                    Console.WriteLine($"[UPDATE] Invalid version format '{tagName}'.");
                    return;
                }

                Version currentVersion = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0, 0);
                if (githubVersion <= currentVersion)
                {
                    Console.WriteLine($"[UPDATE] Kiosk is up-to-date. Current: {currentVersion}, Latest: {githubVersion}");
                    return;
                }

                Console.WriteLine($"[UPDATE] New version {githubVersion} found. Downloading assets...");

                string downloadUrl = "";
                string signatureUrl = "";

                if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
                {
                    foreach (var asset in assets.EnumerateArray())
                    {
                        var name = asset.GetProperty("name").GetString() ?? "";
                        if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                        {
                            downloadUrl = asset.GetProperty("browser_download_url").GetString() ?? "";
                        }
                        else if (name.EndsWith(".zip.sig", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".sig", StringComparison.OrdinalIgnoreCase))
                        {
                            signatureUrl = asset.GetProperty("browser_download_url").GetString() ?? "";
                        }
                    }
                }

                if (string.IsNullOrEmpty(downloadUrl))
                {
                    Console.WriteLine("[UPDATE] Missing update zip file in release. Update cancelled.");
                    return;
                }

                if (string.IsNullOrEmpty(signatureUrl))
                {
                    Console.WriteLine("[SECURITY WARN] Missing .sig output. Signatures are mandatory for production updates. Aborting.");
                    throw new CryptographicException("Production security violation: Release is missing digital signature catalog (.sig).");
                }

                string tempDir = Path.Combine(Path.GetTempPath(), "IzbanUpdateSandbox");
                if (!Directory.Exists(tempDir))
                {
                    Directory.CreateDirectory(tempDir);
                }

                var tempZipPath = Path.Combine(tempDir, "update.zip");
                var tempSigPath = Path.Combine(tempDir, "update.sig");

                // Download Zip
                using (var downloadResponse = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
                {
                    downloadResponse.EnsureSuccessStatusCode();
                    using var fs = new FileStream(tempZipPath, FileMode.Create, FileAccess.Write, FileShare.None);
                    await downloadResponse.Content.CopyToAsync(fs);
                }

                // Download Signature
                using (var sigResponse = await client.GetAsync(signatureUrl, HttpCompletionOption.ResponseHeadersRead))
                {
                    sigResponse.EnsureSuccessStatusCode();
                    using var fs = new FileStream(tempSigPath, FileMode.Create, FileAccess.Write, FileShare.None);
                    await sigResponse.Content.CopyToAsync(fs);
                }

                // SECURE STEP: Verify ECDsa Cryptographic Signature
                Console.WriteLine("[UPDATE] Verification: checking ECDsa production signature...");
                bool isSignatureValid = VerifySignature(tempZipPath, tempSigPath);
                
                if (!isSignatureValid)
                {
                    File.Delete(tempZipPath);
                    File.Delete(tempSigPath);
                    throw new CryptographicException("Security violation: ECDsa digital signature mismatch. Update is untrusted.");
                }

                Console.WriteLine("[UPDATE] Verification SUCCESS. Valid secure signature detected. Sandboxing files...");

                // Sandbox path traversal audit
                VerifyZipSandbox(tempZipPath, tempDir);

                Console.WriteLine("[UPDATE] Sandbox path verification complete. Spawning updater...");

                string appDir = Path.GetDirectoryName(currentProcessPath) ?? AppContext.BaseDirectory;
                string updaterName = OperatingSystem.IsWindows() ? "Updater.exe" : "updater";
                string updaterPath = Path.Combine(appDir, updaterName);

                if (!File.Exists(updaterPath))
                {
                    Console.WriteLine($"[UPDATE FAIL] Companion updater binary missing at: {updaterPath}");
                    return;
                }

                var psi = new ProcessStartInfo
                {
                    FileName = updaterPath,
                    UseShellExecute = true,
                    Arguments = $"\"{tempZipPath}\" \"{appDir}\""
                };

                Process.Start(psi);

                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                    {
                        desktop.Shutdown();
                    }
                    else
                    {
                        Environment.Exit(0);
                    }
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UPDATE FAIL] Security update check failed: {ex.Message}");
                throw;
            }
        }

        private static bool VerifySignature(string filePath, string signaturePath)
        {
            try
            {
                string encodedSig = File.ReadAllText(signaturePath).Trim();
                byte[] signatureBytes = Convert.FromBase64String(encodedSig);

                using var ecdsa = ECDsa.Create();
                ecdsa.ImportFromPem(AuthoritativePublicKeyPem);

                using var fileStream = File.OpenRead(filePath);
                return ecdsa.VerifyData(fileStream, signatureBytes, HashAlgorithmName.SHA256);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SECURITY ERROR] Failed signature verify routine: {ex.Message}");
                return false;
            }
        }

        private static void VerifyZipSandbox(string zipPath, string targetFolder)
        {
            using var archive = ZipFile.OpenRead(zipPath);
            string safeDir = Path.GetFullPath(targetFolder);

            foreach (var entry in archive.Entries)
            {
                string targetPath = Path.GetFullPath(Path.Combine(safeDir, entry.FullName));
                if (!targetPath.StartsWith(safeDir, StringComparison.OrdinalIgnoreCase))
                {
                    throw new UnauthorizedAccessException($"Path traversal violation detected on zip entry '{entry.FullName}'.");
                }
            }
        }
    }
}