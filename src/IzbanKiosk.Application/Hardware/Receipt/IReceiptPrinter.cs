using System.Threading;
using System.Threading.Tasks;

namespace IzbanKiosk.Application.Hardware.Receipt
{
    public interface IReceiptPrinter
    {
        Task InitializeAsync(CancellationToken cancellationToken);
        Task ConnectAsync(CancellationToken cancellationToken);
        Task<ReceiptPrinterStatus> HealthCheckAsync(CancellationToken cancellationToken);
        Task<ReceiptPrinterStatus> GetStatusAsync(CancellationToken cancellationToken);
        Task<ReceiptPrintResult> PrintReceiptAsync(
            ReceiptDocument document,
            string receiptIdempotencyKey,
            CancellationToken cancellationToken);
        Task<ReceiptJobStatus> QueryPrintJobAsync(
            string printerJobReference,
            CancellationToken cancellationToken);
    }
}
