using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using IzbanKiosk.LegacyHardware.Contracts;
using IzbanKiosk.LegacyHardwareBridge.Interop;

namespace IzbanKiosk.LegacyHardwareBridge.Printer
{
    public class KioskPrintReceiptPrinter : ILegacyReceiptPrinter
    {
        private readonly ConcurrentDictionary<string, bool> _printedReceipts = new ConcurrentDictionary<string, bool>();
        private readonly object _sync = new object();

        private string _configuredPrinterName = string.Empty;
        private string _resolvedPrinterName = string.Empty;
        private bool _isInitialized;

        // KioskPrint.dll binds itself to the printer that is default the first time
        // any of its entry points runs, and keeps it until the process exits. Track
        // that moment so a later change of the Windows default can be reported
        // instead of silently producing paper on the wrong queue - or on none.
        private bool _vendorBound;
        private bool _requiresBridgeRestart;

        public string LastErrorMessage { get; private set; } = string.Empty;

        public bool Initialize(string printerName)
        {
            lock (_sync)
            {
                LastErrorMessage = string.Empty;
                _isInitialized = false;
                _requiresBridgeRestart = false;
                _configuredPrinterName = (printerName ?? string.Empty).Trim();
                _resolvedPrinterName = string.Empty;

                if (_configuredPrinterName.Length == 0)
                {
                    LastErrorMessage = "No thermal printer is configured. Set ThermalPrinterName in KioskHardware.config.json " +
                        "or start the bridge with --printer \"<Windows printer name>\".";
                    return false;
                }

                string resolvedName;
                string resolveError;
                if (!WindowsPrinterEnvironment.TryResolveInstalledPrinter(_configuredPrinterName, out resolvedName, out resolveError))
                {
                    LastErrorMessage = resolveError;
                    return false;
                }
                _resolvedPrinterName = resolvedName;

                // This must complete before the first KioskPrint.dll call of the process.
                // The vendor library always prints to the Windows default queue, so on a
                // Windows Embedded image whose default is a PDF/XPS/network printer the
                // receipt is consumed by that queue and no paper is ever produced.
                string routingError;
                if (!WindowsPrinterEnvironment.TryMakeDefault(_resolvedPrinterName, out routingError))
                {
                    LastErrorMessage = routingError;
                    return false;
                }

                PrinterHealthResponse health = HealthCheckCore();
                _isInitialized = health.IsReady;
                if (!_isInitialized)
                {
                    LastErrorMessage = health.StatusMessage;
                }
                return _isInitialized;
            }
        }

        public bool PrintReceipt(string text, string idempotencyKey)
        {
            lock (_sync)
            {
                LastErrorMessage = string.Empty;
                if (!_isInitialized)
                {
                    LastErrorMessage = "Printer has not completed initialization.";
                    return false;
                }

                // Enforce idempotency: if already printed, do not reprint
                if (_printedReceipts.ContainsKey(idempotencyKey))
                {
                    return true;
                }

                bool documentStarted = false;
                try
                {
                    // Never purge the shared spooler queue. A pending job may belong to
                    // another transaction or process. HealthCheckCore re-asserts the
                    // default-printer routing first, so a default that drifted while the
                    // kiosk was idle is corrected instead of swallowing the receipt.
                    PrinterHealthResponse health = HealthCheckCore();
                    if (!health.IsReady)
                    {
                        LastErrorMessage = health.StatusMessage;
                        return false;
                    }

                    // This call sequence matches the deployed AUSKiosk 5.2.0.4
                    // implementation: BeginDoc -> SetFont -> TextOut -> EndDoc.
                    KioskPrintNativeMethods.PrinterBeginDoc();
                    documentStarted = true;
                    _vendorBound = true;
                    KioskPrintNativeMethods.PrinterSetFont("Tahoma", 8);

                    string[] lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                    int yOffset = 5;
                    foreach (string line in lines)
                    {
                        if (line.StartsWith("[C]"))
                        {
                            KioskPrintNativeMethods.PrinterCenteredTextOut(yOffset, line.Substring(3), 10);
                        }
                        else
                        {
                            KioskPrintNativeMethods.PrinterTextOut(5, yOffset, line);
                        }
                        yOffset += 30;
                    }

                    KioskPrintNativeMethods.PrinterEndDoc();
                    documentStarted = false;

                    _printedReceipts[idempotencyKey] = true;
                    return true;
                }
                catch (Exception ex)
                {
                    LastErrorMessage = ex.GetType().Name + ": " + ex.Message;
                    if (documentStarted)
                    {
                        try
                        {
                            KioskPrintNativeMethods.PrinterEndDoc();
                        }
                        catch
                        {
                            // Preserve the original exception and never purge shared jobs.
                        }
                    }
                    return false;
                }
            }
        }

        public bool PrintTestReceipt()
        {
            string testReceipt =
                "[C]IZBAN KIOSK TEST FISI\n" +
                "----------------------------------------------------\n" +
                "Tarih: " + DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss") + "\n" +
                "Kiosk: 00082\n" +
                "Termal yazici test cikisi\n" +
                "KioskPrint.dll dogrulamasi\n" +
                "----------------------------------------------------\n" +
                "[C]Fiziksel ciktiyi kontrol edin.\n\n\n\n\n";

            string testKey = "TEST_RECEIPT_" + Guid.NewGuid().ToString("N");
            return PrintReceipt(testReceipt, testKey);
        }

        public PrinterHealthResponse HealthCheck()
        {
            lock (_sync)
            {
                return HealthCheckCore();
            }
        }

        /// <summary>
        /// Everything the operator needs to see on the kiosk itself when receipts stop
        /// coming out: which queue is configured, which queues exist, which one Windows
        /// currently treats as the default, and what the spooler and the vendor library
        /// each report about the configured queue.
        /// </summary>
        public PrinterDiagnosticsResponse Diagnose(string printerName)
        {
            lock (_sync)
            {
                string configuredName = (printerName ?? string.Empty).Trim();
                string defaultBefore = WindowsPrinterEnvironment.GetDefaultPrinterName();
                var diagnostics = new PrinterDiagnosticsResponse
                {
                    ConfiguredPrinterName = configuredName,
                    DefaultPrinterBefore = defaultBefore
                };

                foreach (WindowsPrinterInfo queue in WindowsPrinterEnvironment.ListInstalledPrinterDetails())
                {
                    diagnostics.InstalledPrinters.Add(queue.Name);
                    diagnostics.InstalledPrinterDetails.Add(new InstalledPrinterInfo
                    {
                        Name = queue.Name,
                        PortName = queue.PortName,
                        DriverName = queue.DriverName,
                        StatusFlags = queue.Status,
                        Attributes = queue.Attributes,
                        IsWorkOffline = WindowsPrinterEnvironment.IsWorkOffline(queue.Attributes),
                        QueuedJobCount = queue.QueuedJobCount,
                        IsDefault = string.Equals(queue.Name, defaultBefore, StringComparison.OrdinalIgnoreCase),
                        IsConfigured = string.Equals(queue.Name, configuredName, StringComparison.OrdinalIgnoreCase)
                    });
                }

                if (diagnostics.ConfiguredPrinterName.Length == 0)
                {
                    diagnostics.StatusMessage = "No thermal printer is configured.";
                    return diagnostics;
                }

                string resolvedName;
                string resolveError;
                if (!WindowsPrinterEnvironment.TryResolveInstalledPrinter(
                        diagnostics.ConfiguredPrinterName, out resolvedName, out resolveError))
                {
                    diagnostics.StatusMessage = resolveError;
                    return diagnostics;
                }

                diagnostics.IsInstalled = true;
                diagnostics.ResolvedPrinterName = resolvedName;

                string routingError;
                diagnostics.DefaultPrinterRoutingApplied =
                    WindowsPrinterEnvironment.TryMakeDefault(resolvedName, out routingError);
                diagnostics.DefaultPrinterAfter = WindowsPrinterEnvironment.GetDefaultPrinterName();
                diagnostics.ReceiptRoutingDevice = WindowsPrinterEnvironment.GetProfileDeviceName();

                WindowsPrinterInfo info;
                int win32Error;
                diagnostics.SpoolerStatusRead = WindowsPrinterEnvironment.TryReadPrinterInfo(resolvedName, out info, out win32Error);
                diagnostics.Win32Error = win32Error;
                if (diagnostics.SpoolerStatusRead)
                {
                    diagnostics.DriverName = info.DriverName;
                    diagnostics.PortName = info.PortName;
                    diagnostics.SpoolerStatusFlags = info.Status;
                    diagnostics.SpoolerAttributes = info.Attributes;
                    diagnostics.IsWorkOffline = WindowsPrinterEnvironment.IsWorkOffline(info.Attributes);
                    diagnostics.QueuedJobStates = info.JobStates;
                }

                string vendorError;
                int jobCount;
                diagnostics.VendorProbeCompleted = TryProbeVendorJobCount(out jobCount, out vendorError);
                diagnostics.VendorQueuedJobCount = jobCount;
                diagnostics.VendorProbeError = vendorError;

                if (!diagnostics.DefaultPrinterRoutingApplied)
                {
                    diagnostics.StatusMessage = routingError;
                    return diagnostics;
                }

                LegacyPrinterHealthDecision decision = LegacyPrinterHealthPolicy.Evaluate(
                    resolvedName,
                    diagnostics.SpoolerStatusRead,
                    diagnostics.SpoolerStatusRead,
                    diagnostics.SpoolerStatusFlags,
                    diagnostics.Win32Error,
                    diagnostics.VendorProbeCompleted,
                    diagnostics.VendorQueuedJobCount,
                    diagnostics.VendorProbeError);

                diagnostics.IsReady = decision.IsReady;
                diagnostics.StatusMessage = decision.StatusMessage;
                return diagnostics;
            }
        }

        /// <summary>
        /// Clears the configured queue on explicit operator request and lets the
        /// printer be used again. A backlog blocks initialization by design, so
        /// without this the kiosk could not recover from stuck jobs without a
        /// Windows session on the machine.
        /// </summary>
        public PrinterPurgeResponse PurgeQueue(string printerName)
        {
            lock (_sync)
            {
                string configuredName = (printerName ?? string.Empty).Trim();
                var response = new PrinterPurgeResponse { PrinterName = configuredName };

                string resolvedName;
                string resolveError;
                if (!WindowsPrinterEnvironment.TryResolveInstalledPrinter(configuredName, out resolvedName, out resolveError))
                {
                    response.StatusMessage = resolveError;
                    return response;
                }

                response.PrinterName = resolvedName;

                int purgedJobCount;
                string purgeError;
                if (!WindowsPrinterEnvironment.TryPurgeQueue(resolvedName, out purgedJobCount, out purgeError))
                {
                    response.StatusMessage = purgeError;
                    return response;
                }

                response.Purged = true;
                response.PurgedJobCount = purgedJobCount;
                response.StatusMessage = purgedJobCount + " job(s) cancelled on '" + resolvedName + "'.";

                // A cleared queue may well be healthy again; re-run initialization so
                // the operator does not have to restart anything.
                Initialize(configuredName);
                return response;
            }
        }

        /// <summary>
        /// Brings the configured queue out of "Use Printer Offline" on operator
        /// request and re-runs initialization.
        /// </summary>
        public PrinterPurgeResponse ClearWorkOffline(string printerName)
        {
            lock (_sync)
            {
                string configuredName = (printerName ?? string.Empty).Trim();
                var response = new PrinterPurgeResponse { PrinterName = configuredName };

                string resolvedName;
                string resolveError;
                if (!WindowsPrinterEnvironment.TryResolveInstalledPrinter(configuredName, out resolvedName, out resolveError))
                {
                    response.StatusMessage = resolveError;
                    return response;
                }

                response.PrinterName = resolvedName;

                string clearError;
                if (!WindowsPrinterEnvironment.TryClearWorkOffline(resolvedName, out clearError))
                {
                    response.StatusMessage = clearError;
                    return response;
                }

                response.Purged = true;
                response.StatusMessage = "'" + resolvedName + "' is online again.";
                Initialize(configuredName);
                return response;
            }
        }

        private PrinterHealthResponse HealthCheckCore()
        {
            var response = new PrinterHealthResponse
            {
                PrinterName = _resolvedPrinterName.Length > 0 ? _resolvedPrinterName : _configuredPrinterName,
                IsReady = false
            };

            try
            {
                // Do not fall back to an arbitrary default queue. The deployment
                // configuration must name the physical thermal printer explicitly.
                if (_configuredPrinterName.Length == 0)
                {
                    response.StatusMessage = "No thermal printer is configured. Configure ThermalPrinterName before startup.";
                    return response;
                }

                if (_resolvedPrinterName.Length == 0)
                {
                    string resolvedName;
                    string resolveError;
                    if (!WindowsPrinterEnvironment.TryResolveInstalledPrinter(_configuredPrinterName, out resolvedName, out resolveError))
                    {
                        response.StatusMessage = resolveError;
                        return response;
                    }
                    _resolvedPrinterName = resolvedName;
                    response.PrinterName = resolvedName;
                }

                if (_requiresBridgeRestart)
                {
                    response.StatusMessage = BridgeRestartMessage(_resolvedPrinterName);
                    return response;
                }

                string routingError;
                if (!EnsureDefaultPrinterRouting(out routingError))
                {
                    response.StatusMessage = routingError;
                    return response;
                }

                WindowsPrinterInfo info;
                int win32Error;
                bool statusRead = WindowsPrinterEnvironment.TryReadPrinterInfo(_resolvedPrinterName, out info, out win32Error);

                // A vendor job-count probe alone is insufficient: it only proves that
                // KioskPrint.dll loaded, not that the named Windows queue can be opened
                // by this kiosk user. It does however expose a queue that is silently
                // piling jobs up, which is how the legacy AUSKiosk decided whether a
                // receipt could be promised to the passenger.
                if (statusRead && WindowsPrinterEnvironment.IsWorkOffline(info.Attributes))
                {
                    response.StatusMessage = "Queue '" + _resolvedPrinterName + "' is set to Use Printer Offline. " +
                        "Windows accepts jobs and never sends them to the device. Bring it back online.";
                    return response;
                }

                string vendorError;
                int vendorJobCount;
                bool vendorProbeCompleted = TryProbeVendorJobCount(out vendorJobCount, out vendorError);

                return ApplyDecision(response, LegacyPrinterHealthPolicy.Evaluate(
                    _resolvedPrinterName,
                    statusRead,
                    statusRead,
                    statusRead ? info.Status : 0u,
                    win32Error,
                    vendorProbeCompleted,
                    vendorJobCount,
                    vendorError));
            }
            catch (Exception ex)
            {
                response.StatusMessage = "OutcomeUnknown Error: " + ex.Message;
            }

            return response;
        }

        /// <summary>
        /// Re-asserts the Windows default printer on every health check and every
        /// receipt. The default is per-user state that a policy, a driver
        /// reinstallation, the "let Windows manage my default printer" behaviour or a
        /// write-filter rollback on the embedded image can move at any time; the
        /// previous implementation only set it once at startup and refused to print
        /// forever afterwards.
        /// </summary>
        private bool EnsureDefaultPrinterRouting(out string error)
        {
            error = string.Empty;

            string current = WindowsPrinterEnvironment.GetDefaultPrinterName();
            if (string.Equals(current, _resolvedPrinterName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            string routingError;
            if (!WindowsPrinterEnvironment.TryMakeDefault(_resolvedPrinterName, out routingError))
            {
                error = routingError;
                return false;
            }

            if (_vendorBound)
            {
                // The default was corrected, but KioskPrint.dll already captured the
                // previous one. Nothing in its API rebinds it, so refuse to print
                // rather than send a receipt to a queue we can no longer identify.
                _requiresBridgeRestart = true;
                error = BridgeRestartMessage(_resolvedPrinterName) +
                    " Windows default printer had drifted to '" + current + "'.";
                return false;
            }

            return true;
        }

        private static string BridgeRestartMessage(string printerName)
        {
            return "Windows default printer changed after KioskPrint.dll was bound in this process. " +
                "It has been restored to '" + printerName + "', but the legacy printing library keeps the queue it " +
                "captured at first use, so the hardware bridge must be restarted before receipts can be printed again.";
        }

        private static bool TryProbeVendorJobCount(out int jobCount, out string error)
        {
            jobCount = -1;
            error = string.Empty;
            try
            {
                jobCount = KioskPrintNativeMethods.GetSpoolerJobCount();
                return true;
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        private static PrinterHealthResponse ApplyDecision(
            PrinterHealthResponse response,
            LegacyPrinterHealthDecision decision)
        {
            response.IsReady = decision.IsReady;
            response.IsSpoolerRunning = decision.IsSpoolerRunning;
            response.StatusMessage = decision.StatusMessage;
            return response;
        }
    }
}
