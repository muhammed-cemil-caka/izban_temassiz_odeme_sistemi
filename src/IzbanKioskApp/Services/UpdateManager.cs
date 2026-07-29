using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;

namespace IzbanKioskApp.Services
{
    public static class UpdateManager
    {
        private const string DefaultOwner = "muhammed-cemil-caka";
        private const string DefaultRepo = "izban_temassiz_odeme_sistemi";

        /// <summary>
        /// Kiosk açılışında 1 defaya mahsus kontrol eder ve ardından günlük 04:00 zamanlayıcı döngüsünü başlatır.
        /// </summary>
        public static async Task StartUpdateSchedulerAsync()
        {
            // İlk açılışta 1 defaya mahsus güncelleme kontrolü yap
            Console.WriteLine("[UPDATE] Kiosk açılışında ilk güncelleme denetimi gerçekleştiriliyor...");
            try
            {
                await CheckAndPerformUpdateAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[UPDATE FAIL] Açılış güncelleme denetimi başarısız: {ex.Message}");
            }

            // Günlük sabaha karşı 04:00 zamanlayıcı döngüsü
            while (true)
            {
                var now = DateTime.Now;
                var targetTime = new DateTime(now.Year, now.Month, now.Day, 4, 0, 0);
                
                // Eğer saat 04:00 geçmişse, bir sonraki günün 04:00'ünü bekle
                if (now >= targetTime)
                {
                    targetTime = targetTime.AddDays(1);
                }

                var delay = targetTime - now;
                Console.WriteLine($"[UPDATE] Bir sonraki otomatik güncelleme denetimi {targetTime:yyyy-MM-dd HH:mm:ss} tarihinde yapılacak. (Bekleme süresi: {delay.TotalHours:F2} saat)");
                
                await Task.Delay(delay);

                // Zamanlayıcı tetiklendiğinde kullanıcı işlem yapıyorsa (meşgulse) güncelleme 10 dakika ertelenir
                while (AppServices.IsUserActive)
                {
                    Console.WriteLine("[UPDATE] Kiosk meşgul (kullanıcı aktif işlem yapıyor). Güncelleme kontrolü 10 dakika ertelendi...");
                    await Task.Delay(TimeSpan.FromMinutes(10));
                }

                Console.WriteLine("[UPDATE] Zamanlanmış güncelleme denetimi başlatılıyor...");
                try
                {
                    await CheckAndPerformUpdateAsync();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[UPDATE FAIL] Zamanlanmış güncelleme denetimi başarısız: {ex.Message}");
                }
            }
        }

        public static async Task CheckAndPerformUpdateAsync(
            string owner = DefaultOwner, 
            string repo = DefaultRepo)
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

                Console.WriteLine($"[UPDATE] New version found: {githubVersion}. Current: {currentVersion}. Downloading update archive...");

                string downloadUrl = "";
                if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
                {
                    foreach (var asset in assets.EnumerateArray())
                    {
                        var name = asset.GetProperty("name").GetString() ?? "";
                        if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                        {
                            downloadUrl = asset.GetProperty("browser_download_url").GetString() ?? "";
                            break;
                        }
                    }
                }

                if (string.IsNullOrEmpty(downloadUrl))
                {
                    Console.WriteLine("[UPDATE] No zip asset found in the latest release. Auto-update canceled.");
                    return;
                }

                // C:\Temp dizinini oluştur ve dosyayı indir
                string targetDir = @"C:\Temp";
                if (!Directory.Exists(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                }

                var tempZipPath = Path.Combine(targetDir, "update.zip");

                using (var downloadResponse = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
                {
                    downloadResponse.EnsureSuccessStatusCode();
                    using (var fs = new FileStream(tempZipPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        await downloadResponse.Content.CopyToAsync(fs);
                    }
                }

                // İndirilen zip dosyasını doğrula
                if (!File.Exists(tempZipPath) || new FileInfo(tempZipPath).Length == 0)
                {
                    throw new Exception("Downloaded zip file is invalid or empty.");
                }

                Console.WriteLine("[UPDATE] Download complete. Spawning Updater.exe...");

                // Kiosk uygulamasıyla aynı dizinde duran Updater.exe uygulamasını başlat
                string appDir = Path.GetDirectoryName(currentProcessPath) ?? AppContext.BaseDirectory;
                string updaterPath = Path.Combine(appDir, "Updater.exe");

                if (!File.Exists(updaterPath))
                {
                    Console.WriteLine($"[UPDATE FAIL] Companion updater not found at: {updaterPath}");
                    return;
                }

                var psi = new ProcessStartInfo
                {
                    FileName = updaterPath,
                    UseShellExecute = true
                };

                Process.Start(psi);

                // Ana Avalonia uygulamasını temiz bir şekilde kapat
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
                Console.WriteLine($"[UPDATE FAIL] Auto-update failed: {ex.Message}");
            }
        }
    }
}