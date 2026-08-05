using System;
using IzbanKiosk.LegacyHardware.Contracts;

namespace IzbanKiosk.LegacyHardwareBridge.Printer
{
    public interface ILegacyReceiptPrinter
    {
        bool Initialize(string printerName);
        bool PrintReceipt(string text, string idempotencyKey);
        bool PrintTestReceipt();
        PrinterHealthResponse HealthCheck();
        PrinterDiagnosticsResponse Diagnose(string printerName);
        PrinterPurgeResponse PurgeQueue(string printerName);
        PrinterPurgeResponse ClearWorkOffline(string printerName);
        string LastErrorMessage { get; }
    }
}
