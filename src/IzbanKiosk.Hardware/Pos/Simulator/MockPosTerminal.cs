using System;
using System.Threading;
using System.Threading.Tasks;
using IzbanKiosk.Domain;
using IzbanKiosk.Application.Hardware.Pos;

namespace IzbanKiosk.Hardware.Pos.Simulator
{
    public class MockPosTerminal : IPosTerminal
    {
        public PosCapabilities Capabilities => new(
            supportsSale: true,
            supportsPreAuthorization: true,
            supportsCapture: true,
            supportsQueryByReference: true,
            supportsGetLastTransaction: true,
            supportsCancel: true,
            supportsVoid: true,
            supportsReversal: true,
            supportsBatchClose: true,
            supportsIdempotencyReference: true
        );

        // Simulator controls
        public string NextOperationResult { get; set; } = "Approved"; // "Approved", "Declined", "Timeout", "Cancelled"
        public string? NextErrorCode { get; set; }
        public string? NextErrorMessage { get; set; }

        public Task InitializeAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task<bool> ConnectAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(true);
        }

        public Task<bool> HealthCheckAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(true);
        }

        public async Task<PosTransactionResult> StartSaleAsync(
            TransactionId transactionId, 
            string idempotencyKey, 
            Money amount, 
            TimeSpan timeout, 
            CancellationToken cancellationToken, 
            Guid correlationId)
        {
            await Task.Delay(100, cancellationToken); // Simulate IO delay

            if (NextOperationResult == "Timeout")
            {
                await Task.Delay((int)timeout.TotalMilliseconds + 50, cancellationToken);
                return PosTransactionResult.Failed("TIMEOUT", "POS Terminal response timed out.");
            }

            if (NextOperationResult == "Declined")
            {
                return new PosTransactionResult(
                    success: false,
                    approvalCode: null,
                    vendorReference: "MOCK-TX-" + Guid.NewGuid().ToString().Substring(0, 8),
                    responseCode: "51",
                    errorCode: NextErrorCode ?? "DECLINED",
                    errorMessage: NextErrorMessage ?? "Insufficient funds.",
                    amountMinor: amount.AmountMinor
                );
            }

            if (NextOperationResult == "Cancelled")
            {
                return PosTransactionResult.Failed("CANCELLED", "User cancelled transaction at terminal.");
            }

            // Default Approved
            return new PosTransactionResult(
                success: true,
                approvalCode: "123456",
                vendorReference: "MOCK-TX-" + Guid.NewGuid().ToString().Substring(0, 8),
                responseCode: "00",
                amountMinor: amount.AmountMinor
            );
        }

        public async Task<PosTransactionResult> PreAuthorizeAsync(
            TransactionId transactionId, 
            string idempotencyKey, 
            Money amount, 
            TimeSpan timeout, 
            CancellationToken cancellationToken, 
            Guid correlationId)
        {
            await Task.Delay(100, cancellationToken);
            if (NextOperationResult == "Approved")
            {
                return new PosTransactionResult(
                    success: true,
                    approvalCode: "AUTH-12",
                    vendorReference: "MOCK-PA-" + Guid.NewGuid().ToString().Substring(0, 8),
                    responseCode: "00",
                    amountMinor: amount.AmountMinor
                );
            }
            return PosTransactionResult.Failed("DECLINED", "PreAuth Declined");
        }

        public async Task<PosTransactionResult> CaptureAsync(
            TransactionId transactionId, 
            string idempotencyKey, 
            Money amount, 
            string preAuthReference, 
            TimeSpan timeout, 
            CancellationToken cancellationToken, 
            Guid correlationId)
        {
            await Task.Delay(100, cancellationToken);
            return new PosTransactionResult(
                success: true,
                approvalCode: "CAP-12",
                vendorReference: "MOCK-CAP-" + Guid.NewGuid().ToString().Substring(0, 8),
                responseCode: "00",
                amountMinor: amount.AmountMinor
            );
        }

        public Task<PosTransactionResult> QueryTransactionAsync(
            TransactionId transactionId, 
            string vendorReference, 
            CancellationToken cancellationToken, 
            Guid correlationId)
        {
            // Simulate that we know the outcome of this mock tx
            return Task.FromResult(new PosTransactionResult(
                success: true,
                approvalCode: "123456",
                vendorReference: vendorReference,
                responseCode: "00"
            ));
        }

        public Task<PosTransactionResult> GetLastTransactionAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(new PosTransactionResult(
                success: true,
                approvalCode: "123456",
                vendorReference: "MOCK-LAST",
                responseCode: "00"
            ));
        }

        public Task<bool> CancelAsync(TransactionId transactionId, CancellationToken cancellationToken)
        {
            return Task.FromResult(true);
        }

        public Task<PosTransactionResult> VoidAsync(
            TransactionId transactionId, 
            string vendorReference, 
            CancellationToken cancellationToken, 
            Guid correlationId)
        {
            return Task.FromResult(new PosTransactionResult(
                success: true,
                approvalCode: "VOID-OK",
                vendorReference: vendorReference,
                responseCode: "400"
            ));
        }

        public Task<PosTransactionResult> ReversalAsync(
            TransactionId transactionId, 
            string vendorReference, 
            Money amount, 
            CancellationToken cancellationToken, 
            Guid correlationId)
        {
            return Task.FromResult(new PosTransactionResult(
                success: true,
                approvalCode: "REV-OK",
                vendorReference: vendorReference,
                responseCode: "400"
            ));
        }

        public Task<string> GetBatchSummaryAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult("BATCH-CLOSE-SUCCESS - Total: 100.00 TRY");
        }
    }
}
