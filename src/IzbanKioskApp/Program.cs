using Avalonia;
using System;

namespace IzbanKioskApp
{
    internal class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            // Avalonia Masaüstü Pencere Uygulamasını Başlat
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }

        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .LogToTrace();
    }
}