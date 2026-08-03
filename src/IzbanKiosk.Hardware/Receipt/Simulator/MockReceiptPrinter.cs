using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using IzbanKiosk.Application.Hardware.Receipt;

namespace IzbanKiosk.Hardware.Receipt.Simulator
{
    public class MockReceiptPrinter : IReceiptPrinter
    {
        private int _printCount;
        private ReceiptDocument _lastPrintedDocument;
        private ReceiptPrinterStatusCode _configuredStatusCode = ReceiptPrinterStatusCode.Ready;
        private ReceiptPrintOutcome _configuredPrintOutcome = ReceiptPrintOutcome.Success;
        private bool _writePreviewFile;
        private string _previewDirectory = "SimulatedReceipts";

        public int PrintCount => _printCount;
        public ReceiptDocument LastPrintedDocument => _lastPrintedDocument;

        public void Configure(
            ReceiptPrinterStatusCode statusCode,
            ReceiptPrintOutcome printOutcome,
            bool writePreviewFile = false,
            string previewDirectory = "SimulatedReceipts")
        {
            _configuredStatusCode = statusCode;
            _configuredPrintOutcome = printOutcome;
            _writePreviewFile = writePreviewFile;
            _previewDirectory = previewDirectory;
        }

        public Task InitializeAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task ConnectAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task<ReceiptPrinterStatus> HealthCheckAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(new ReceiptPrinterStatus(_configuredStatusCode, $"Mock status: {_configuredStatusCode}"));
        }

        public Task<ReceiptPrinterStatus> GetStatusAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(new ReceiptPrinterStatus(_configuredStatusCode, $"Mock status: {_configuredStatusCode}"));
        }

        public async Task<ReceiptPrintResult> PrintReceiptAsync(
            ReceiptDocument document,
            string receiptIdempotencyKey,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _printCount);
            _lastPrintedDocument = document;

            if (_configuredPrintOutcome != ReceiptPrintOutcome.Success)
            {
                return ReceiptPrintResult.StatusFailed(
                    _configuredPrintOutcome,
                    "MOCK_ERROR_" + _configuredPrintOutcome.ToString().ToUpperInvariant(),
                    $"Print simulated outcome failed: {_configuredPrintOutcome}");
            }

            string jobRef = "mock-job-" + Guid.NewGuid().ToString().Substring(0, 8);

            if (_writePreviewFile)
            {
                try
                {
                    Directory.CreateDirectory(_previewDirectory);
                    string filePath = Path.Combine(_previewDirectory, $"receipt_{receiptIdempotencyKey.Replace(":", "_")}.txt");
                    
                    string content = $@"
========================================
           {document.Title}
        {document.SubTitle}
========================================
Tarih: {document.TransactionDateTime}
Kiosk ID: {document.KioskId}
Istasyon: {document.StationName}
Makbuz No: {document.ReceiptNumber}
Islem ID: {document.MaskedTransactionId}
========================================
Kart No: {document.MaskedCardNumber}
Yuklenen Tutar: {document.LoadedAmount}
Onceki Bakiye: {document.PreviousBalance}
Yeni Bakiye: {document.NewBalance}
Doviz: {document.Currency}
========================================
Referans: {document.MaskedPosReference}
Onay Kodu: {document.PosApprovalCode}
Load Ref: {document.MaskedLoadVendorReference}
----------------------------------------
{document.TransactionResultText}
{document.SupportContact}
{document.ThankYouMessage}
========================================
Hash: {document.ContentHash}
========================================
";
                    await File.WriteAllTextAsync(filePath, content, System.Text.Encoding.UTF8, cancellationToken);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[MOCK PRINTER ERROR] Failed to write preview file: {ex.Message}");
                }
            }

            return ReceiptPrintResult.Successful(jobRef);
        }

        public Task<ReceiptJobStatus> QueryPrintJobAsync(
            string printerJobReference,
            CancellationToken cancellationToken)
        {
            if (_configuredPrintOutcome == ReceiptPrintOutcome.Success)
            {
                return Task.FromResult(ReceiptJobStatus.FinishedSuccess);
            }
            return Task.FromResult(new ReceiptJobStatus(true, false, _configuredPrintOutcome, "Job failed in mock."));
        }
    }
}
