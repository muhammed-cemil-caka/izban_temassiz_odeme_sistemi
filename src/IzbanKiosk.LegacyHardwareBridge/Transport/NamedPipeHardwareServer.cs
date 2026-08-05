using System;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using IzbanKiosk.LegacyHardware.Contracts;
using IzbanKiosk.LegacyHardwareBridge.Configuration;
using IzbanKiosk.LegacyHardwareBridge.Diagnostics;
using IzbanKiosk.LegacyHardwareBridge.Nfc;
using IzbanKiosk.LegacyHardwareBridge.Printer;
using IzbanKiosk.LegacyHardwareBridge.Pos;
using Newtonsoft.Json;

namespace IzbanKiosk.LegacyHardwareBridge.Transport
{
    /// <summary>
    /// Synchronous, length-framed named-pipe server designed for the .NET Framework
    /// 4.0 Client Profile available on the legacy Windows 7 kiosk. All blocking I/O
    /// runs on a dedicated background thread, never on the WPF UI thread.
    /// </summary>
    public sealed class NamedPipeHardwareServer
    {
        private const string PipeName = "IzbanKiosk.LegacyHardware.v1";
        private const int MaxMessageSize = 64 * 1024;

        private readonly ILegacyNfcDevice _nfcDevice;
        private readonly ILegacyReceiptPrinter _printerDevice;
        private readonly IPosTerminal _posTerminal;
        private readonly HardwareOptions _options;
        private readonly ComPortOwnershipGuard _comGuard = new ComPortOwnershipGuard();
        private readonly object _lifecycleLock = new object();

        private volatile bool _isRunning;
        private Thread? _listenerThread;
        private NamedPipeServerStream? _activePipe;

        public NamedPipeHardwareServer(
            ILegacyNfcDevice nfcDevice,
            ILegacyReceiptPrinter printerDevice,
            IPosTerminal posTerminal,
            HardwareOptions options)
        {
            _nfcDevice = nfcDevice;
            _printerDevice = printerDevice;
            _posTerminal = posTerminal;
            _options = options;
        }

        public void Start()
        {
            lock (_lifecycleLock)
            {
                if (_isRunning)
                {
                    return;
                }

                _isRunning = true;
                _listenerThread = new Thread(ListenLoop)
                {
                    IsBackground = true,
                    Name = "IZBAN Legacy Hardware Pipe"
                };
                _listenerThread.Start();
            }

            Console.WriteLine("Named Pipe Server listening on: \\\\.\\pipe\\" + PipeName);
        }

        public void Stop()
        {
            lock (_lifecycleLock)
            {
                _isRunning = false;
                try
                {
                    if (_activePipe != null)
                    {
                        _activePipe.Dispose();
                    }
                }
                catch
                {
                    // Disposing the active pipe is only used to unblock WaitForConnection.
                }
            }

            if (_listenerThread != null && _listenerThread.IsAlive)
            {
                _listenerThread.Join(2000);
            }
        }

        private void ListenLoop()
        {
            while (_isRunning)
            {
                NamedPipeServerStream? pipe = null;
                try
                {
                    pipe = CreateSecuredPipe();
                    lock (_lifecycleLock)
                    {
                        _activePipe = pipe;
                    }

                    pipe.WaitForConnection();
                    if (!_isRunning)
                    {
                        break;
                    }

                    HandleConnection(pipe);
                }
                catch (ObjectDisposedException)
                {
                    if (_isRunning)
                    {
                        Console.WriteLine("[ERROR] Named Pipe was closed unexpectedly.");
                    }
                }
                catch (Exception ex)
                {
                    if (_isRunning)
                    {
                        Console.WriteLine("[ERROR] Named Pipe connection error: " + ex.Message);
                        Thread.Sleep(500);
                    }
                }
                finally
                {
                    lock (_lifecycleLock)
                    {
                        if (ReferenceEquals(_activePipe, pipe))
                        {
                            _activePipe = null;
                        }
                    }

                    if (pipe != null)
                    {
                        pipe.Dispose();
                    }
                }
            }
        }

        private static NamedPipeServerStream CreateSecuredPipe()
        {
            var pipeSecurity = new PipeSecurity();
            SecurityIdentifier? currentUser = WindowsIdentity.GetCurrent().User;
            if (currentUser == null)
            {
                throw new InvalidOperationException("Current Windows user SID could not be resolved.");
            }

            pipeSecurity.AddAccessRule(new PipeAccessRule(
                currentUser,
                PipeAccessRights.ReadWrite,
                AccessControlType.Allow));

            return new NamedPipeServerStream(
                PipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.None,
                MaxMessageSize,
                MaxMessageSize,
                pipeSecurity);
        }

        private void HandleConnection(NamedPipeServerStream pipe)
        {
            var response = new BridgeResponse();

            try
            {
                string requestText = NamedPipeFraming.ReadMessage(pipe);
                if (string.IsNullOrEmpty(requestText))
                {
                    return;
                }

                BridgeRequest? request = JsonConvert.DeserializeObject<BridgeRequest>(requestText);
                if (request == null)
                {
                    response.Success = false;
                    response.Error = new BridgeError { Code = "ERR_BAD_REQUEST", Message = "Malformed request payload." };
                }
                else if (string.IsNullOrEmpty(request.RequestId))
                {
                    response.Success = false;
                    response.Error = new BridgeError { Code = "ERR_BAD_REQUEST", Message = "Missing Request ID." };
                }
                else if (!string.Equals(request.ProtocolVersion, "1.0", StringComparison.Ordinal))
                {
                    response.RequestId = request.RequestId;
                    response.Success = false;
                    response.Error = new BridgeError
                    {
                        Code = "ERR_UNSUPPORTED_VERSION",
                        Message = "Protocol version '" + request.ProtocolVersion + "' is not supported."
                    };
                }
                else
                {
                    response.RequestId = request.RequestId;
                    ProcessCommand(request, response);
                }
            }
            catch (Exception ex)
            {
                response.Success = false;
                response.Error = new BridgeError { Code = "ERR_EXCEPTION", Message = "Internal server processing error." };
                Console.WriteLine("[FATAL] Named Pipe processing error: " + ex.GetType().Name + ": " + ex.Message);
            }

            try
            {
                NamedPipeFraming.WriteMessage(pipe, JsonConvert.SerializeObject(response));
            }
            catch (Exception ex)
            {
                Console.WriteLine("[ERROR] Failed to write Named Pipe response: " + ex.Message);
            }
        }

        private void ProcessCommand(BridgeRequest request, BridgeResponse response)
        {
            if (RequiresNfcPort(request.Command))
            {
                string comError;
                if (!_comGuard.IsComPortAvailable(_options.NfcComPort, out comError))
                {
                    response.Success = false;
                    response.Error = new BridgeError { Code = "ERR_COM_PORT_LOCKED", Message = comError };
                    return;
                }
            }

            switch (request.Command)
            {
                case "GetBridgeVersion":
                    response.Success = true;
                    response.PayloadJson = JsonConvert.SerializeObject(new
                    {
                        Version = "2.2.2-net40",
                        Framework = ".NET Framework 4.0 Client Profile (x86)"
                    });
                    break;

                case "Initialize":
                    bool nfcInitialized = _nfcDevice.Initialize();
                    if (nfcInitialized)
                    {
                        nfcInitialized = _nfcDevice.OpenComm(_options.NfcComPort);
                        if (nfcInitialized)
                        {
                            _comGuard.IsPortAlreadyAcquiredByUs = true;
                        }
                    }

                    bool printerInitialized = _printerDevice.Initialize(_options.PrinterName);
                    string printerInitError = printerInitialized ? string.Empty : _printerDevice.LastErrorMessage;
                    if (!printerInitialized)
                    {
                        // The printer never reached the client before: Success only ever
                        // reflected the NFC reader, so a kiosk with a working reader and a
                        // dead receipt printer reported a fully successful initialization.
                        Console.WriteLine("[ERROR] Thermal printer initialization failed: " + printerInitError);
                    }

                    response.Success = nfcInitialized;
                    if (!nfcInitialized)
                    {
                        response.Error = new BridgeError { Code = "ERR_INIT_FAILED", Message = "Failed to initialize NFC Reader." };
                    }
                    else
                    {
                        response.PayloadJson = JsonConvert.SerializeObject(new
                        {
                            NfcInitialized = true,
                            PrinterInitialized = printerInitialized,
                            PrinterName = _options.PrinterName,
                            PrinterError = printerInitError
                        });
                    }
                    break;

                case "HealthCheck":
                    var report = new HardwareHealthResponse();
                    bool connected = _nfcDevice.IsHardwareConnected();
                    bool samVerified = connected && _nfcDevice.ResetSam();
                    report.Nfc = CreateNfcHealth(connected, samVerified);
                    report.Printer = _printerDevice.HealthCheck();
                    response.Success = report.IsSystemHealthy;
                    response.PayloadJson = JsonConvert.SerializeObject(report);
                    break;

                case "NfcHealth":
                    bool readerConnected = _nfcDevice.IsHardwareConnected();
                    bool samReady = readerConnected && _nfcDevice.ResetSam();
                    NfcHealthResponse nfcHealth = CreateNfcHealth(readerConnected, samReady);
                    response.Success = nfcHealth.IsReady;
                    response.PayloadJson = JsonConvert.SerializeObject(nfcHealth);
                    break;

                case "PrinterHealth":
                    PrinterHealthResponse printerHealth = _printerDevice.HealthCheck();
                    response.Success = printerHealth.IsReady;
                    response.PayloadJson = JsonConvert.SerializeObject(printerHealth);
                    break;

                case "PrinterDiagnose":
                    PrinterDiagnosticsResponse diagnostics = _printerDevice.Diagnose(_options.PrinterName);
                    // The report is the payload whether or not the printer is usable;
                    // an unusable printer is exactly when the operator needs to read it.
                    response.Success = true;
                    response.PayloadJson = JsonConvert.SerializeObject(diagnostics);
                    break;

                case "PrinterPurgeQueue":
                    PrinterPurgeResponse purge = _printerDevice.PurgeQueue(_options.PrinterName);
                    response.Success = purge.Purged;
                    response.PayloadJson = JsonConvert.SerializeObject(purge);
                    if (!purge.Purged)
                    {
                        response.Error = new BridgeError { Code = "ERR_PRINTER_PURGE_FAILED", Message = purge.StatusMessage };
                    }
                    break;

                case "PrinterReinitialize":
                    // Lets the operator recover after correcting KioskHardware.config.json
                    // or fixing the queue, without restarting the kiosk.
                    bool reinitialized = _printerDevice.Initialize(_options.PrinterName);
                    response.Success = reinitialized;
                    if (!reinitialized)
                    {
                        response.Error = new BridgeError
                        {
                            Code = "ERR_PRINTER_INIT_FAILED",
                            Message = _printerDevice.LastErrorMessage
                        };
                    }
                    response.PayloadJson = JsonConvert.SerializeObject(_printerDevice.HealthCheck());
                    break;

                case "WaitForCard":
                    WaitForCard(request, response);
                    break;

                case "ReadCardSnapshot":
                    CardSnapshotResponse snapshot;
                    bool readOk = _nfcDevice.ReadCardSnapshot(request.RequestId, out snapshot);
                    response.Success = readOk;
                    if (readOk)
                    {
                        snapshot.BalanceScale = _options.BalanceScale;
                        snapshot.IsBalanceScaleVerified = _options.BalanceScaleVerified;
                        snapshot.Currency = _options.Currency;
                        snapshot.BalanceMinor = snapshot.BalanceRaw;
                        response.PayloadJson = JsonConvert.SerializeObject(snapshot);
                    }
                    else
                    {
                        response.Error = new BridgeError
                        {
                            Code = string.IsNullOrEmpty(snapshot.ErrorCode) ? "ERR_READ_FAILED" : snapshot.ErrorCode,
                            Message = "Card read query failed."
                        };
                    }
                    break;

                case "WaitForCardRemoval":
                    int removalTimeout = request.TimeoutMs > 0 ? Math.Min(request.TimeoutMs, 60000) : 5000;
                    bool removed = _nfcDevice.WaitForCardRemoval(TimeSpan.FromMilliseconds(removalTimeout));
                    response.Success = true;
                    response.PayloadJson = JsonConvert.SerializeObject(new CardRemovalResponse
                    {
                        RequestId = request.RequestId,
                        IsRemoved = removed,
                        ObservedAtUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
                    });
                    break;

                case "PrintReceipt":
                    PrintReceiptRequest? printRequest = JsonConvert.DeserializeObject<PrintReceiptRequest>(request.PayloadJson);
                    if (printRequest == null || string.IsNullOrEmpty(printRequest.Text) || string.IsNullOrEmpty(printRequest.IdempotencyKey))
                    {
                        response.Success = false;
                        response.Error = new BridgeError { Code = "ERR_BAD_PAYLOAD", Message = "PrintRequest requires Text and IdempotencyKey." };
                    }
                    else
                    {
                        response.Success = _printerDevice.PrintReceipt(printRequest.Text, printRequest.IdempotencyKey);
                        if (!response.Success)
                        {
                            response.Error = new BridgeError
                            {
                                Code = "ERR_PRINT_FAILED",
                                Message = "Thermal printer output operation failed. " + _printerDevice.LastErrorMessage
                            };
                        }
                    }
                    break;

                case "PrintTestReceipt":
                    response.Success = _printerDevice.PrintTestReceipt();
                    if (!response.Success)
                    {
                        response.Error = new BridgeError
                        {
                            Code = "ERR_PRINT_FAILED",
                            Message = "Printer failed to output test block. " + _printerDevice.LastErrorMessage
                        };
                    }
                    break;

                case "Shutdown":
                    _nfcDevice.Shutdown();
                    response.Success = true;
                    var shutdownThread = new Thread(new ThreadStart(delegate
                    {
                        Thread.Sleep(500);
                        Environment.Exit(0);
                    })) { IsBackground = true };
                    shutdownThread.Start();
                    break;

                case "PosPayment":
                    // The seam a certified bank POS SDK plugs into. Until one is
                    // registered this always refuses, so no passenger can be charged.
                    PosPaymentRequest? paymentRequest = string.IsNullOrEmpty(request.PayloadJson)
                        ? null
                        : JsonConvert.DeserializeObject<PosPaymentRequest>(request.PayloadJson);
                    if (paymentRequest == null || paymentRequest.AmountMinor <= 0 || string.IsNullOrEmpty(paymentRequest.IdempotencyKey))
                    {
                        response.Success = false;
                        response.Error = new BridgeError
                        {
                            Code = "ERR_BAD_PAYLOAD",
                            Message = "PosPayment requires AmountMinor and IdempotencyKey."
                        };
                        break;
                    }

                    PosPaymentResponse payment = _posTerminal.Charge(paymentRequest);
                    response.Success = payment.IsApproved;
                    response.PayloadJson = JsonConvert.SerializeObject(payment);
                    if (!payment.IsApproved)
                    {
                        response.Error = new BridgeError
                        {
                            Code = _posTerminal.IsConfigured ? "ERR_POS_DECLINED" : "ERR_POS_NOT_CONFIGURED",
                            Message = payment.StatusMessage
                        };
                    }
                    break;

                case "Topup":
                case "AutoTopup":
                case "TicketCharge":
                case "MfrWriteBlock":
                    response.Success = false;
                    response.Error = new BridgeError
                    {
                        Code = "ERR_ACCESS_DENIED",
                        Message = "Card write and charge operations stay blocked until an authorised " +
                                  "İzmirim Kart load command and a certified POS adapter are integrated."
                    };
                    break;

                default:
                    response.Success = false;
                    response.Error = new BridgeError
                    {
                        Code = "ERR_UNKNOWN_COMMAND",
                        Message = "Request command '" + request.Command + "' is not supported."
                    };
                    break;
            }
        }

        private static bool RequiresNfcPort(string command)
        {
            return string.Equals(command, "Initialize", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(command, "WaitForCard", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(command, "ReadCardSnapshot", StringComparison.OrdinalIgnoreCase);
        }

        private NfcHealthResponse CreateNfcHealth(bool connected, bool samVerified)
        {
            return new NfcHealthResponse
            {
                IsReady = connected && samVerified,
                ComPort = _options.NfcComPort,
                IsSamVerified = samVerified,
                StatusMessage = connected
                    ? (samVerified ? "NFC is ready. " + _nfcDevice.LastSamStatusMessage : _nfcDevice.LastSamStatusMessage)
                    : "NFC reader not connected."
            };
        }

        private void WaitForCard(BridgeRequest request, BridgeResponse response)
        {
            int waitMilliseconds = request.TimeoutMs > 0 ? Math.Min(request.TimeoutMs, 60000) : 5000;
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(waitMilliseconds);
            string maskedReference = string.Empty;
            string storagePseudonym = string.Empty;
            bool present;

            do
            {
                present = _nfcDevice.CheckIfCardPresent(out maskedReference, out storagePseudonym);
                if (present)
                {
                    response.Success = true;
                    response.PayloadJson = JsonConvert.SerializeObject(new CardDetectedResponse
                    {
                        RequestId = request.RequestId,
                        MaskedCardReference = maskedReference,
                        StoragePseudonym = storagePseudonym,
                        ObservedAtUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
                    });
                    return;
                }

                Thread.Sleep(150);
            }
            while (DateTime.UtcNow < deadline && _isRunning);

            response.Success = false;
            response.Error = new BridgeError { Code = "ERR_CARD_NOT_PRESENT", Message = "No card detected on reader." };
        }
    }
}
