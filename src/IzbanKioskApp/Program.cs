using Avalonia;
using System;
using IzbanKioskApp.Core;

namespace IzbanKioskApp
{
    internal class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            // SQLite Veritabanını Başlat
            DatabaseService.InitializeDatabaseAsync().GetAwaiter().GetResult();

            // Avalonia Masaüstü Pencere Uygulamasını Başlat
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }

        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .LogToTrace();
    }
}