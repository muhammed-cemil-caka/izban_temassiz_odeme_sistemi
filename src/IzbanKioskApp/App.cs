using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Themes.Fluent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using IzbanKiosk.Domain;
using IzbanKiosk.Application.Services;
using IzbanKiosk.Application.Repositories;
using IzbanKiosk.Application.Hardware.Pos;
using IzbanKiosk.Application.Hardware.Nfc;
using IzbanKiosk.Application.Hardware.Balance;
using IzbanKiosk.Infrastructure;
using IzbanKiosk.Infrastructure.Repositories;
using IzbanKiosk.Hardware.Pos.Simulator;
using IzbanKiosk.Hardware.Pos.Vendor;
using IzbanKiosk.Hardware.Nfc.Simulator;
using IzbanKiosk.Hardware.Nfc.Vendor;
using IzbanKiosk.Hardware.Balance.Simulator;
using IzbanKiosk.Hardware.Balance.Hybrid;
using IzbanKioskApp.ViewModels;

namespace IzbanKioskApp
{
    public static class AppServices
    {
        public static bool IsUserActive { get; set; } = false;
    }

    public class App : Application
    {
        public static IHost AppHost { get; private set; } = null!;

        public override void Initialize()
        {
            Styles.Add(new FluentTheme());

            // Build the Microsoft.Extensions.Hosting AppHost
            AppHost = Host.CreateDefaultBuilder()
                .ConfigureAppConfiguration((context, builder) =>
                {
                    builder.SetBasePath(AppContext.BaseDirectory);
                    builder.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
                })
                .ConfigureServices((context, services) =>
                {
                    // Read UseMockHardware configuration (default: true)
                    bool useMock = context.Configuration.GetValue<bool>("UseMockHardware", true);

                    // Db connection factory & repository
                    string dbPath = context.Configuration.GetValue<string>("Database:Path") ?? "transactions.db";
                    string fullDbPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, dbPath);
                    var dbFactory = new DbConnectionFactory(fullDbPath);
                    services.AddSingleton(dbFactory);
                    services.AddSingleton<ITransactionRepository, SqliteTransactionRepository>();

                    // Hardware configurations
                    if (useMock)
                    {
                        services.AddSingleton<INfcReader, MockNfcReader>();
                        services.AddSingleton<IPosTerminal, MockPosTerminal>();
                        services.AddSingleton<IAuthoritativeBalanceProvider, MockBalanceProvider>();
                    }
                    else
                    {
                        services.AddSingleton<INfcReader, RealNfcReader>();
                        services.AddSingleton<IPosTerminal, RealPosTerminal>();
                        services.AddSingleton<IAuthoritativeBalanceProvider, HybridBalanceProvider>();
                    }

                    // Application Services
                    services.AddSingleton<TransactionCoordinator>();
                    services.AddSingleton<RecoveryService>();
                    services.AddSingleton<ReconciliationService>();

                    // ViewModel & View setup
                    services.AddTransient<MainWindowViewModel>();
                    services.AddTransient<MainWindow>();
                })
                .ConfigureLogging(logging =>
                {
                    logging.ClearProviders();
                    logging.AddConsole();
                    logging.SetMinimumLevel(LogLevel.Debug);
                })
                .Build();

            // Synchronously initialize database schema before starting anything else.
            // If schema cannot be initialized, throw a fatal exception to block app from launching.
            try
            {
                var dbFactory = AppHost.Services.GetRequiredService<DbConnectionFactory>();
                dbFactory.InitializeDatabaseAsync().GetAwaiter().GetResult();
                Console.WriteLine("[DB INIT] Database schema verified & initialized successfully.");
            }
            catch (System.Exception ex)
            {
                Console.WriteLine($"[FATAL DB ERROR] Veritabanı şeması oluşturulamadı: {ex.Message}");
                throw new InvalidOperationException("Veritabanı şeması oluşturulamadığı için finansal işlem motoru başlatılamıyor.", ex);
            }

            // Run Host & start hardware/recovery loops in backgound thread
            Task.Run(async () =>
            {
                await AppHost.StartAsync();

                try
                {
                    var nfcReader = AppHost.Services.GetRequiredService<INfcReader>();
                    var posTerminal = AppHost.Services.GetRequiredService<IPosTerminal>();

                    await nfcReader.ConnectAsync(CancellationToken.None);
                    await posTerminal.ConnectAsync(CancellationToken.None);
                }
                catch (System.Exception ex)
                {
                    Console.WriteLine($"[HARDWARE FAIL] Donanım bağlantı hatası: {ex.Message}");
                }

                try
                {
                    var recoveryService = AppHost.Services.GetRequiredService<RecoveryService>();
                    var logger = AppHost.Services.GetRequiredService<ILogger<RecoveryService>>();
                    while (true)
                    {
                        try
                        {
                            await recoveryService.ProcessRecoveryAsync(CancellationToken.None);
                        }
                        catch (Exception ex)
                        {
                            logger.LogError(ex, "Periodic recovery check failed.");
                        }
                        await Task.Delay(TimeSpan.FromSeconds(30));
                    }
                }
                catch (System.Exception ex)
                {
                    Console.WriteLine($"[RECOVERY FAIL] Kurtarma servisi başlatılamadı: {ex.Message}");
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
                desktop.MainWindow = AppHost.Services.GetRequiredService<MainWindow>();
            }
            base.OnFrameworkInitializationCompleted();
        }
    }
}