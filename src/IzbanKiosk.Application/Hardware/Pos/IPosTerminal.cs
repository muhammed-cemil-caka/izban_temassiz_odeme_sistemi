using System;
using System.Threading;
using System.Threading.Tasks;
using IzbanKiosk.Domain;

namespace IzbanKiosk.Application.Hardware.Pos
{
    public interface IPosTerminal
    {
        PosCapabilities Capabilities { get; }
        Task InitializeAsync(CancellationToken cancellationToken);
        Task<bool> ConnectAsync(CancellationToken cancellationToken);
        Task<bool> HealthCheckAsync(CancellationToken cancellationToken);
        Task<PosTransactionResult> StartSaleAsync(TransactionId transactionId, string idempotencyKey, Money amount, TimeSpan timeout, CancellationToken cancellationToken, Guid correlationId);
        Task<PosTransactionResult> PreAuthorizeAsync(TransactionId transactionId, string idempotencyKey, Money amount, TimeSpan timeout, CancellationToken cancellationToken, Guid correlationId);
        Task<PosTransactionResult> CaptureAsync(TransactionId transactionId, string idempotencyKey, Money amount, string preAuthReference, TimeSpan timeout, CancellationToken cancellationToken, Guid correlationId);
        Task<PosTransactionResult> QueryTransactionAsync(TransactionId transactionId, string vendorReference, CancellationToken cancellationToken, Guid correlationId);
        Task<PosTransactionResult> GetLastTransactionAsync(CancellationToken cancellationToken);
        Task<bool> CancelAsync(TransactionId transactionId, CancellationToken cancellationToken);
        Task<PosTransactionResult> VoidAsync(TransactionId transactionId, string vendorReference, CancellationToken cancellationToken, Guid correlationId);
        Task<PosTransactionResult> ReversalAsync(TransactionId transactionId, string vendorReference, Money amount, CancellationToken cancellationToken, Guid correlationId);
        Task<string> GetBatchSummaryAsync(CancellationToken cancellationToken);
    }
}
