using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

namespace IzbanKioskApp.Services
{
    public static class UpdateManager
    {
        // Varsayılan GitHub Bilgileriniz eklendi (Gerektiğinde parametre göndererek de değiştirebilirsiniz)
        public static async Task CheckAndPerformUpdateAsync(
            string owner = "muhammed-cemil-caka", 
            string repo = "izban_temassiz_odeme_sistemi")
        {
            // Auto-update is only supported and executed on Windows platforms
            if (!OperatingSystem.IsWindows())
            {
                Console.WriteLine("[UPDATE] Auto-update is disabled on non-Windows platforms.");
                return;
            }

            string? currentProcessPath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(currentProcessPath))
            {
                Console.WriteLine("[UPDATE] Could not resolve current process path. Aborting update.");
                return;
            }

            try
            {
                using var client = new HttpClient();
                // GitHub API requires User-Agent header
                client.DefaultRequestHeaders.UserAgent.ParseAdd("IzbanKioskApp-Updater");
                client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github.v3+json");

                string apiUrl = $"https://api.github.com/repos/{owner}/{repo}/releases/latest";
                string jsonString = await client.GetStringAsync(apiUrl);

                using var doc = JsonDocument.Parse(jsonString);
                var root = doc.RootElement;

                if (!root.TryGetProperty("tag_name", out var tagProp))
                {
                    Console.WriteLine("[UPDATE] No tag_name found in the latest release response.");
                    return;
                }

                string tagName = tagProp.GetString() ?? "";
                string cleanTag = tagName.TrimStart('v', 'V');

                if (!Version.TryParse(cleanTag, out Version? githubVersion))
                {
                    Console.WriteLine($"[UPDATE] Could not parse release version '{tagName}'.");
                    return;
                }

                Version currentVersion = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0, 0);

                if (githubVersion <= currentVersion)
                {
                    Console.WriteLine($"[UPDATE] Kiosk is up-to-date. Current: {currentVersion}, Latest: {githubVersion}");
                    return;
                }

                Console.WriteLine($"[UPDATE] New version found: {githubVersion}. Current: {currentVersion}. Downloading update...");

                string downloadUrl = "";
                if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
                {
                    foreach (var asset in assets.EnumerateArray())
                    {
                        var name = asset.GetProperty("name").GetString() ?? "";
                        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                        {
                            downloadUrl = asset.GetProperty("browser_download_url").GetString() ?? "";
                            break;
                        }
                    }
                }

                if (string.IsNullOrEmpty(downloadUrl))
                {
                    Console.WriteLine("[UPDATE] No executable asset found in the latest release.");
                    return;
                }

                // Download the asset to temporary file
                var tempExePath = Path.Combine(Path.GetTempPath(), "IzbanKioskApp_new.exe");

                using (var downloadResponse = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
                {
                    downloadResponse.EnsureSuccessStatusCode();
                    using (var fs = new FileStream(tempExePath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        await downloadResponse.Content.CopyToAsync(fs);
                    }
                }

                // Verify the downloaded file
                if (!File.Exists(tempExePath) || new FileInfo(tempExePath).Length == 0)
                {
                    throw new Exception("Downloaded file is invalid or empty.");
                }

                Console.WriteLine("[UPDATE] Download complete. Spawning bat updater script...");

                // Create the updater.bat script in temporary folder
                var updaterBatPath = Path.Combine(Path.GetTempPath(), "updater.bat");
                var batchContent = $@"@echo off
timeout /t 2 /nobreak > nul
copy /y ""{tempExePath}"" ""{currentProcessPath}""
if errorlevel 1 goto error
start """" ""{currentProcessPath}""
del ""{tempExePath}""
(goto) 2>nul & del ""%~f0""
exit

:error
echo Update failed. Restarting current version...
start """" ""{currentProcessPath}""
exit";

                await File.WriteAllTextAsync(updaterBatPath, batchContent);

                // Run batch script and close Avalonia application cleanly
                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c \"{updaterBatPath}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };

                Process.Start(psi);
                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UPDATE FAIL] Auto-update failed: {ex.Message}");
            }
        }
    }
}