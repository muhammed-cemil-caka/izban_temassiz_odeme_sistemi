using System;
using System.IO;
using System.Threading;
using Newtonsoft.Json;
using IzbanKiosk.LegacyHardware.Contracts;
using IzbanKiosk.LegacyHardwareBridge.Card;
using IzbanKiosk.LegacyHardwareBridge.Configuration;
using IzbanKiosk.LegacyHardwareBridge.Diagnostics;
using IzbanKiosk.LegacyHardwareBridge.Nfc;
using IzbanKiosk.LegacyHardwareBridge.Printer;
using IzbanKiosk.LegacyHardwareBridge.Pos;
using IzbanKiosk.LegacyHardwareBridge.Security;
using IzbanKiosk.LegacyHardwareBridge.Transport;

namespace IzbanKiosk.LegacyHardwareBridge
{
    internal class Program
    {
        private static int Main(string[] args)
        {
            var options = new HardwareOptions();
            
            bool cliHealth = false;
            bool cliNfcHealth = false;
            bool cliPrinterHealth = false;
            bool cliReadCardOnce = false;
            bool cliPrintTest = false;
            bool cliListPrinters = false;
            bool cliPrinterDiagnose = false;
            bool cliAutoConfigure = false;
            bool cliInteractive = false;
            bool rejectedScaleOverride = false;
            bool comPortFromCommandLine = false;
            bool printerFromCommandLine = false;

            // 1. Process command line arguments
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], "--port", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    options.NfcComPort = args[++i];
                    comPortFromCommandLine = true;
                }
                else if (string.Equals(args[i], "--printer", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    options.PrinterName = args[++i];
                    printerFromCommandLine = true;
                }
                else if (string.Equals(args[i], "--list-printers", StringComparison.OrdinalIgnoreCase))
                {
                    cliListPrinters = true;
                }
                else if (string.Equals(args[i], "--printer-diagnose", StringComparison.OrdinalIgnoreCase))
                {
                    cliPrinterDiagnose = true;
                }
                else if (string.Equals(args[i], "--autoconfigure", StringComparison.OrdinalIgnoreCase))
                {
                    cliAutoConfigure = true;
                }
                else if (string.Equals(args[i], "--interactive", StringComparison.OrdinalIgnoreCase))
                {
                    cliInteractive = true;
                }
                else if (string.Equals(args[i], "--verify-scale", StringComparison.OrdinalIgnoreCase))
                {
                    // A command-line switch is not evidence that the vendor balance unit was
                    // physically verified against known cards.
                    rejectedScaleOverride = true;
                }
                else if (string.Equals(args[i], "--health", StringComparison.OrdinalIgnoreCase))
                {
                    cliHealth = true;
                }
                else if (string.Equals(args[i], "--nfc-health", StringComparison.OrdinalIgnoreCase))
                {
                    cliNfcHealth = true;
                }
                else if (string.Equals(args[i], "--printer-health", StringComparison.OrdinalIgnoreCase))
                {
                    cliPrinterHealth = true;
                }
                else if (string.Equals(args[i], "--read-card-once", StringComparison.OrdinalIgnoreCase))
                {
                    cliReadCardOnce = true;
                }
                else if (string.Equals(args[i], "--print-test", StringComparison.OrdinalIgnoreCase))
                {
                    cliPrintTest = true;
                }
            }

            if (rejectedScaleOverride)
            {
                Console.Error.WriteLine("[ERROR] --verify-scale is not accepted. Record physical comparison evidence before enabling a verified balance scale in deployment configuration.");
                return 4;
            }

            // Runs before anything reads the settings: it is what writes them. Needs
            // no HMAC secret and no vendor DLLs, because it only asks Windows what
            // hardware is present - it never opens the reader or the printer.
            if (cliAutoConfigure)
            {
                return RunAutoConfigureCommand(cliInteractive);
            }

            // 2. Fill in whatever the caller did not pass from the deployment-owned
            // hardware settings file. The Windows 7 kiosk shell always passes both
            // values; hand-run diagnostics usually pass neither.
            ApplyHardwareConfigFileDefaults(options, comPortFromCommandLine, printerFromCommandLine);

            // Printer-only commands never open the reader and never see card data, so
            // they must not require the card-pseudonymisation secret. Demanding it here
            // is what made on-site printer troubleshooting impossible.
            bool cardCommandRequested = cliHealth || cliNfcHealth || cliReadCardOnce;
            bool printerOnlyRun = !cardCommandRequested &&
                (cliListPrinters || cliPrinterDiagnose || cliPrinterHealth || cliPrintTest);

            if (printerOnlyRun)
            {
                if (cliListPrinters || cliPrinterDiagnose)
                {
                    int inspectionExitCode = RunPrinterInspectionCommand(cliListPrinters, cliPrinterDiagnose, options);
                    if (!cliPrinterHealth && !cliPrintTest)
                    {
                        return inspectionExitCode;
                    }
                }

                return RunCliCommand(false, false, cliPrinterHealth, false, cliPrintTest, options, new VendorDependencyValidator());
            }

            // 3. Load HMAC secret key from Environment
            string hmacKey = Environment.GetEnvironmentVariable("IZBAN_HMAC_SECRET");
            if (string.IsNullOrWhiteSpace(hmacKey))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("[ERROR] Missing required environment variable 'IZBAN_HMAC_SECRET'. HMAC key is mandatory.");
                Console.ResetColor();
                return 1;
            }
            options.HmacKeyBase64 = hmacKey;

            try
            {
                // Validate Base64 format and minimum key length before loading native code.
                _ = new SensitiveDataRedactor(options.HmacKeyBase64);
            }
            catch (ArgumentException ex)
            {
                Console.Error.WriteLine($"[ERROR] Invalid IZBAN_HMAC_SECRET: {ex.Message}");
                return 1;
            }

            // 4. Create Validator
            var validator = new VendorDependencyValidator();

            // Handle CLI Mode checks before doing anything else
            if (cliHealth || cliNfcHealth || cliPrinterHealth || cliReadCardOnce || cliPrintTest)
            {
                return RunCliCommand(cliHealth, cliNfcHealth, cliPrinterHealth, cliReadCardOnce, cliPrintTest, options, validator);
            }

            Console.WriteLine("----------------------------------------------------------------");
            Console.WriteLine("İzban Kiosk Legacy Hardware Bridge (.NET Framework 4.0 Client Profile x86)");
            Console.WriteLine($"COM Port: {options.NfcComPort}");
            Console.WriteLine($"Printer Name: {(string.IsNullOrEmpty(options.PrinterName) ? "[NOT CONFIGURED - receipts will fail]" : options.PrinterName)}");
            Console.WriteLine($"Balance Scale: {(options.BalanceScaleVerified ? "Verified (100)" : "Unverified (100)")}");
            Console.WriteLine("----------------------------------------------------------------");

            // Validate environment dependencies (OS, Process Arch, Whitelisted DLLs)
            if (!validator.Validate(out string validationMsg, out _))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[ERROR] Startup Validation Failed: {validationMsg}");
                Console.ResetColor();
                return 1;
            }

            Console.WriteLine("[INFO] Environment validation passed.");

            // Acquire Single Instance Mutex
            using (var mutexGuard = new BridgeSingleInstanceGuard())
            {
                if (!mutexGuard.TryAcquire(out string instanceError))
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"[ERROR] Instance Conflict: {instanceError}");
                    Console.ResetColor();
                    return 2;
                }

                var redactor = new SensitiveDataRedactor(options.HmacKeyBase64);
                ILegacyNfcDevice nfcDevice = new EmvRdr35NfcDevice(redactor);
                ILegacyReceiptPrinter printerDevice = new KioskPrintReceiptPrinter();

                // Replace this with the certified bank POS adapter once its SDK is
                // available. Nothing else in the bridge or the kiosk has to change.
                IPosTerminal posTerminal = new NotConfiguredPosTerminal();
                posTerminal.Initialize();

                // Start Named Pipe Server
                var server = new NamedPipeHardwareServer(nfcDevice, printerDevice, posTerminal, options);
                
                var exitEvent = new ManualResetEvent(false);
                Console.CancelKeyPress += (s, e) =>
                {
                    Console.WriteLine("[INFO] Shutdown signal received. Exiting...");
                    e.Cancel = true;
                    exitEvent.Set();
                };

                server.Start();
                Console.WriteLine("[INFO] Legacy hardware bridge running. Press Ctrl+C to terminate.");

                exitEvent.WaitOne();

                server.Stop();
                nfcDevice.Shutdown();
                posTerminal.Shutdown();
            }

            return 0;
        }

        private static int RunCliCommand(
            bool health, 
            bool nfcHealth, 
            bool printerHealth, 
            bool readCardOnce, 
            bool printTest, 
            HardwareOptions options, 
            VendorDependencyValidator validator)
        {
            if ((printerHealth || printTest) && !validator.Validate(out string printerValidationMessage, out _))
            {
                Console.WriteLine(JsonConvert.SerializeObject(new
                {
                    IsReady = false,
                    Error = "IntegrityCheckFailed",
                    Message = printerValidationMessage
                }));
                return 1;
            }

            if (health)
            {
                if (!validator.Validate(out string validationMsg, out _))
                {
                    Console.WriteLine(JsonConvert.SerializeObject(new { IsHealthy = false, Error = "IntegrityCheckFailed", Message = validationMsg }));
                    return 1;
                }

                var redactor = new SensitiveDataRedactor(options.HmacKeyBase64);
                var nfc = new EmvRdr35NfcDevice(redactor);
                var printer = new KioskPrintReceiptPrinter();

                bool nfcOk = nfc.Initialize() && nfc.OpenComm(options.NfcComPort);
                bool samOk = nfcOk && nfc.ResetSam();
                bool printerOk = printer.Initialize(options.PrinterName);

                var report = new
                {
                    IsHealthy = samOk && printerOk,
                    Nfc = new { Initialized = nfcOk, SamOk = samOk, ComPort = options.NfcComPort },
                    Printer = new { Initialized = printerOk, Name = options.PrinterName }
                };

                Console.WriteLine(JsonConvert.SerializeObject(report));
                nfc.Shutdown();
                return report.IsHealthy ? 0 : 2;
            }

            if (nfcHealth)
            {
                if (!validator.Validate(out string validationMsg, out _))
                {
                    Console.WriteLine(JsonConvert.SerializeObject(new { IsReady = false, Error = validationMsg }));
                    return 1;
                }

                var redactor = new SensitiveDataRedactor(options.HmacKeyBase64);
                var nfc = new EmvRdr35NfcDevice(redactor);
                bool nfcOk = nfc.Initialize() && nfc.OpenComm(options.NfcComPort);
                bool samOk = nfcOk && nfc.ResetSam();

                Console.WriteLine(JsonConvert.SerializeObject(new { IsReady = samOk, InitOk = nfcOk, SamOk = samOk }));
                nfc.Shutdown();
                return samOk ? 0 : 2;
            }

            if (printerHealth)
            {
                var printer = new KioskPrintReceiptPrinter();
                bool printerOk = printer.Initialize(options.PrinterName);
                Console.WriteLine(JsonConvert.SerializeObject(new
                {
                    IsReady = printerOk,
                    PrinterName = options.PrinterName,
                    Message = printerOk ? "Printer is ready." : printer.LastErrorMessage
                }));
                return printerOk ? 0 : 2;
            }

            if (readCardOnce)
            {
                if (!validator.Validate(out string validationMsg, out _))
                {
                    Console.WriteLine(JsonConvert.SerializeObject(new { Success = false, Error = validationMsg }));
                    return 1;
                }

                var redactor = new SensitiveDataRedactor(options.HmacKeyBase64);
                var nfc = new EmvRdr35NfcDevice(redactor);
                bool nfcOk = nfc.Initialize() && nfc.OpenComm(options.NfcComPort);
                if (!nfcOk)
                {
                    Console.WriteLine(JsonConvert.SerializeObject(new { Success = false, Error = "Failed to initialize/open reader communication." }));
                    return 2;
                }
                if (!nfc.ResetSam())
                {
                    Console.WriteLine(JsonConvert.SerializeObject(new { Success = false, Error = "Failed to verify SAM card." }));
                    nfc.Shutdown();
                    return 2;
                }

                DateTime deadline = DateTime.UtcNow.AddSeconds(15);
                while (DateTime.UtcNow < deadline)
                {
                    if (nfc.IsHardwareConnected())
                    {
                        CardSnapshotResponse data;
                        if (nfc.ReadCardSnapshot(Guid.NewGuid().ToString("N"), out data))
                        {
                            Console.WriteLine(JsonConvert.SerializeObject(new { Success = true, Card = data }));
                            nfc.Shutdown();
                            return 0;
                        }
                    }
                    Thread.Sleep(250);
                }

                Console.WriteLine(JsonConvert.SerializeObject(new { Success = false, Error = "Timeout waiting for card or read error." }));
                nfc.Shutdown();
                return 2;
            }

            if (printTest)
            {
                var printer = new KioskPrintReceiptPrinter();
                if (!printer.Initialize(options.PrinterName))
                {
                    Console.WriteLine(JsonConvert.SerializeObject(new
                    {
                        Success = false,
                        Stage = "Initialize",
                        PrinterName = options.PrinterName,
                        Message = printer.LastErrorMessage
                    }));
                    return 3;
                }

                if (!printer.PrintTestReceipt())
                {
                    Console.WriteLine(JsonConvert.SerializeObject(new
                    {
                        Success = false,
                        Stage = "PrintTestReceipt",
                        PrinterName = options.PrinterName,
                        Message = printer.LastErrorMessage
                    }));
                    return 3;
                }

                Console.WriteLine(JsonConvert.SerializeObject(new
                {
                    Success = true,
                    PrinterName = options.PrinterName,
                    Message = "Test receipt submitted. Confirm that paper was physically produced."
                }));
                return 0;
            }

            return 0;
        }

        private static void ApplyHardwareConfigFileDefaults(
            HardwareOptions options,
            bool comPortFromCommandLine,
            bool printerFromCommandLine)
        {
            // Deliberately no early return when both were passed on the command line.
            // The kiosk always passes --port and --printer, so skipping the file here
            // meant the bridge never saw any other setting: card writing stayed off on
            // every kiosk while the screen beside it showed the values as configured.
            BridgeHardwareConfigFile settings;
            string sourcePath;
            string error;
            if (!BridgeHardwareConfigFile.TryLoad(out settings, out sourcePath, out error))
            {
                if (!printerFromCommandLine)
                {
                    Console.Error.WriteLine("[WARN] " + error +
                        " Pass --printer \"<Windows printer name>\" or place the settings file next to the bridge.");
                }
                return;
            }

            if (!comPortFromCommandLine && settings.NfcComPort.Length > 0)
            {
                options.NfcComPort = settings.NfcComPort;
            }

            if (!printerFromCommandLine && settings.ThermalPrinterName.Length > 0)
            {
                options.PrinterName = settings.ThermalPrinterName;
                Console.Error.WriteLine("[INFO] Thermal printer name loaded from " + sourcePath + ".");
            }

            // Card write identity. Out-of-range values are dropped rather than
            // truncated: a terminal number silently wrapped to something else would
            // write to the scheme under an identity nobody chose.
            options.CardWriteEnabled = settings.CardWriteEnabled;
            if (settings.TerminalNo > 0 && settings.TerminalNo <= ushort.MaxValue)
            {
                options.TerminalNo = (ushort)settings.TerminalNo;
            }
            if (settings.TerminalUid > 0 && settings.TerminalUid <= uint.MaxValue)
            {
                options.TerminalUid = (uint)settings.TerminalUid;
            }
            if (settings.CompanyId > 0 && settings.CompanyId <= byte.MaxValue)
            {
                options.CompanyId = (byte)settings.CompanyId;
            }
            if (settings.CardWriteAmountUnit.Length > 0)
            {
                options.CardWriteAmountUnit = settings.CardWriteAmountUnit;
            }
            if (settings.TopupReferenceSeed > 0)
            {
                options.TopupReferenceSeed = settings.TopupReferenceSeed;
            }
        }

        /// <summary>
        /// Detects the thermal printer queue and the reader's serial port and saves
        /// them, so first-time installation needs no hand-edited JSON on a kiosk with
        /// no keyboard.
        ///
        /// The exit code tells the installer script which half is unresolved; the
        /// script prints that back to the operator before they leave the machine.
        /// </summary>
        private static int RunAutoConfigureCommand(bool interactive)
        {
            KioskAutoConfigurator.AutoConfigureOutcome outcome;
            try
            {
                outcome = KioskAutoConfigurator.Run(true, interactive);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("[HATA] Otomatik yapilandirma basarisiz: " + ex.Message);
                return 5;
            }

            Console.WriteLine("Ayar dosyasi : " +
                (outcome.SettingsPath.Length == 0 ? "[bulunamadi]" : outcome.SettingsPath));
            Console.WriteLine("Yazici       : " + (outcome.PrinterOk ? "[TAMAM]" : "[EKSIK]") + " " +
                outcome.PrinterMessage + (outcome.PrinterChanged ? "  (yazildi)" : string.Empty));
            Console.WriteLine("NFC portu    : " + (outcome.ComPortOk ? "[TAMAM]" : "[EKSIK]") + " " +
                outcome.ComPortMessage + (outcome.ComPortChanged ? "  (yazildi)" : string.Empty));

            if (outcome.PrinterOk && outcome.ComPortOk)
            {
                return 0;
            }
            if (!outcome.PrinterOk && !outcome.ComPortOk)
            {
                return 4;
            }
            return outcome.PrinterOk ? 3 : 2;
        }

        private static int RunPrinterInspectionCommand(bool listPrinters, bool diagnose, HardwareOptions options)
        {
            if (listPrinters)
            {
                Console.WriteLine(JsonConvert.SerializeObject(new
                {
                    ConfiguredPrinterName = options.PrinterName,
                    DefaultPrinterName = WindowsPrinterEnvironment.GetDefaultPrinterName(),
                    InstalledPrinters = WindowsPrinterEnvironment.ListInstalledPrinters()
                }, Formatting.Indented));

                if (!diagnose)
                {
                    return 0;
                }
            }

            PrinterDiagnosticsResponse report = new KioskPrintReceiptPrinter().Diagnose(options.PrinterName);
            Console.WriteLine(JsonConvert.SerializeObject(report, Formatting.Indented));
            return report.IsReady ? 0 : 2;
        }
    }
}
