using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Themes.Fluent;
using System.Threading.Tasks;
using IzbanKioskApp.Hardware;

namespace IzbanKioskApp
{
    public static class AppServices
    {
        public static INfcReaderService NfcReader { get; set; } = null!;
        public static IPosTerminalService PosTerminal { get; set; } = null!;
        public static bool IsUserActive { get; set; } = false;
    }

    public class App : Application
    {
        // Kiosk Donanım Yapılandırması: True ise simülasyon sınıfları,
        // False ise sahada kullanılacak gerçek donanım/şebeke sınıfları kullanılır.
        public static bool UseMockHardware = true;

        public override void Initialize()
        {
            Styles.Add(new FluentTheme());

            // Dependency Injection (DI) - Servis Kayıtları
            if (UseMockHardware)
            {
                AppServices.NfcReader = new MockNfcReaderService();
                AppServices.PosTerminal = new MockPosTerminalService();
            }
            else
            {
                AppServices.NfcReader = new RealNfcReaderService();
                AppServices.PosTerminal = new RealPosTerminalService();
            }

            // Arka planda donanımsal bağlantıları asenkron olarak ayağa kaldır
            Task.Run(async () =>
            {
                try
                {
                    await AppServices.NfcReader.ConnectAsync("COM3");
                    await AppServices.PosTerminal.ConnectAsync("192.168.1.100:5000");
                }
                catch (System.Exception ex)
                {
                    Console.WriteLine($"[HARDWARE FAIL] Donanım bağlantı hatası: {ex.Message}");
                }
            });

            // Arka planda zamanlanmış güncelleme denetleyicisini çalıştır
            _ = Task.Run(async () =>
            {
                try
                {
                    await Services.UpdateManager.StartUpdateSchedulerAsync();
                }
                catch (System.Exception ex)
                {
                    Console.WriteLine($"[UPDATE FAIL] Güncelleme zamanlayıcısı başlatılamadı: {ex.Message}");
                }
            });
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = new MainWindow();
            }
            base.OnFrameworkInitializationCompleted();
        }
    }
}