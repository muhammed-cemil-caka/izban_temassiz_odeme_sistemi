using System;
using System.Collections.Generic;

namespace IzbanKiosk.LegacyHardware.Contracts
{
    public class BridgeRequest
    {
        public string RequestId { get; set; } = string.Empty;
        public string Command { get; set; } = string.Empty;
        public string ProtocolVersion { get; set; } = "1.0";
        public int TimeoutMs { get; set; } = 5000;
        public string PayloadJson { get; set; } = string.Empty;
    }

    public class BridgeResponse
    {
        public string RequestId { get; set; } = string.Empty;
        public bool Success { get; set; }
        public string PayloadJson { get; set; } = string.Empty;
        public BridgeError? Error { get; set; }
    }

    public class BridgeError
    {
        public string Code { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }

    public class HardwareHealthResponse
    {
        public NfcHealthResponse Nfc { get; set; } = new NfcHealthResponse();
        public PrinterHealthResponse Printer { get; set; } = new PrinterHealthResponse();
        public bool IsSystemHealthy => Nfc.IsReady && Printer.IsReady;
    }

    public class NfcHealthResponse
    {
        public bool IsReady { get; set; }
        public string StatusMessage { get; set; } = string.Empty;
        public string ComPort { get; set; } = string.Empty;
        public bool IsSamVerified { get; set; }
    }

    public class PrinterHealthResponse
    {
        public bool IsReady { get; set; }
        public string StatusMessage { get; set; } = string.Empty;
        public string PrinterName { get; set; } = string.Empty;
        public bool IsSpoolerRunning { get; set; }
    }

    /// <summary>
    /// Full picture of the thermal printer environment, rendered on the kiosk screen
    /// itself. A Windows Embedded kiosk has no console and no operator keyboard, so
    /// every fact needed to explain "no paper came out" has to reach the UI.
    /// </summary>
    public class PrinterDiagnosticsResponse
    {
        public string ConfiguredPrinterName { get; set; } = string.Empty;
        public string ResolvedPrinterName { get; set; } = string.Empty;
        public bool IsInstalled { get; set; }
        public List<string> InstalledPrinters { get; set; } = new List<string>();
        public List<InstalledPrinterInfo> InstalledPrinterDetails { get; set; } = new List<InstalledPrinterInfo>();
        public string DefaultPrinterBefore { get; set; } = string.Empty;
        public string DefaultPrinterAfter { get; set; } = string.Empty;
        public bool DefaultPrinterRoutingApplied { get; set; }
        public string DriverName { get; set; } = string.Empty;
        public string PortName { get; set; } = string.Empty;
        public bool SpoolerStatusRead { get; set; }
        public uint SpoolerStatusFlags { get; set; }
        public int Win32Error { get; set; }
        public bool VendorProbeCompleted { get; set; }
        public int VendorQueuedJobCount { get; set; } = -1;
        public string VendorProbeError { get; set; } = string.Empty;
        public bool IsReady { get; set; }
        public string StatusMessage { get; set; } = string.Empty;
    }

    /// <summary>
    /// One Windows print queue. The port is the field that matters when a USB printer
    /// has been re-enumerated into several duplicate queues: only the queue on the
    /// port the device currently occupies can produce paper.
    /// </summary>
    public class InstalledPrinterInfo
    {
        public string Name { get; set; } = string.Empty;
        public string PortName { get; set; } = string.Empty;
        public string DriverName { get; set; } = string.Empty;
        public uint StatusFlags { get; set; }
        public int QueuedJobCount { get; set; }
        public bool IsDefault { get; set; }
        public bool IsConfigured { get; set; }
    }

    public class PrinterPurgeResponse
    {
        public string PrinterName { get; set; } = string.Empty;
        public bool Purged { get; set; }
        public int PurgedJobCount { get; set; }
        public string StatusMessage { get; set; } = string.Empty;
    }

    public class CardDetectedResponse
    {
        public string RequestId { get; set; } = string.Empty;
        public string MaskedCardReference { get; set; } = string.Empty;
        public string StoragePseudonym { get; set; } = string.Empty;
        public string ObservedAtUtc { get; set; } = string.Empty;
    }

    public class CardSnapshotResponse
    {
        public string RequestId { get; set; } = string.Empty;
        // Public İzmirim Kart alias returned by the vendor ReadOffCard call.
        // This value is displayed only on the local kiosk screen and must not be logged.
        public string CardNumber { get; set; } = string.Empty;
        // Physical NFC UID returned by SelectCardNoRats.
        // This value is displayed only on the local kiosk screen and must not be persisted.
        public string CardUid { get; set; } = string.Empty;
        public string MaskedCardReference { get; set; } = string.Empty;
        public string StoragePseudonym { get; set; } = string.Empty;
        public string CardType { get; set; } = string.Empty;
        public string CardSubType { get; set; } = string.Empty;
        public long BalanceRaw { get; set; }
        public long BalanceMinor { get; set; }
        public int BalanceScale { get; set; }
        public bool IsBalanceScaleVerified { get; set; }
        public int CardTransactionCounter { get; set; }
        public bool IsCardValid { get; set; }
        public bool IsSamVerified { get; set; }
        public bool IsAuthoritative { get; set; }
        public bool IsVerified { get; set; }
        public bool IsStale { get; set; }
        public string Currency { get; set; } = "TRY";
        public int VendorResponseCode { get; set; }
        public string ErrorCode { get; set; } = string.Empty;
        public string ObservedAtUtc { get; set; } = string.Empty;
    }

    /// <summary>
    /// Payment request handed to the bank POS terminal. Carries no cardholder data:
    /// the bank SDK owns the card entirely, and the kiosk only learns the outcome.
    /// </summary>
    public class PosPaymentRequest
    {
        public string IdempotencyKey { get; set; } = string.Empty;
        /// <summary>Amount in minor units, e.g. 2000 for 20,00 TRY.</summary>
        public long AmountMinor { get; set; }
        public string Currency { get; set; } = "TRY";
        /// <summary>Pseudonymous card reference used to tie the payment to the load.</summary>
        public string StoragePseudonym { get; set; } = string.Empty;
    }

    /// <summary>
    /// Outcome of a POS charge. <c>Outcome</c> is one of <c>Approved</c>,
    /// <c>Declined</c>, <c>NotConfigured</c> or <c>OutcomeUnknown</c>. An unknown
    /// outcome must never be treated as either success or failure: the card must not
    /// be loaded and the payment must be reconciled before it is settled.
    /// </summary>
    public class PosPaymentResponse
    {
        public string RequestId { get; set; } = string.Empty;
        public string Outcome { get; set; } = "OutcomeUnknown";
        public bool IsApproved { get; set; }
        public string ApprovalCode { get; set; } = string.Empty;
        public string MaskedPosReference { get; set; } = string.Empty;
        public string StatusMessage { get; set; } = string.Empty;
    }

    public class CardRemovalResponse
    {
        public string RequestId { get; set; } = string.Empty;
        public bool IsRemoved { get; set; }
        public string ObservedAtUtc { get; set; } = string.Empty;
    }


    public class PrintReceiptRequest
    {
        public string Text { get; set; } = string.Empty;
        public string IdempotencyKey { get; set; } = string.Empty;
    }
}
