using System;
using System.Threading;
using System.Threading.Tasks;
using IzbanKiosk.Domain;
using IzbanKiosk.Application.Hardware.Pos;

namespace IzbanKiosk.Hardware.Pos.Vendor
{
    public class RealPosTerminal : IPosTerminal
    {
        public PosCapabilities Capabilities => new(
            supportsSale: true,
            supportsPreAuthorization: false,
            supportsCapture: false,
            supportsQueryByReference: false,
            supportsGetLastTransaction: false,
            supportsCancel: false,
            supportsVoid: false,
            supportsReversal: false,
            supportsBatchClose: false,
            supportsIdempotencyReference: false
        );

        public Task InitializeAsync(CancellationToken cancellationToken)
        {
            throw new HardwareNotConfiguredException("Real POS Terminal Hardware not configured. Missing Vendor SDK.");
        }

        public Task<bool> ConnectAsync(CancellationToken cancellationToken)
        {
            throw new VendorSdkUnavailableException("Real POS SDK binaries are unavailable.");
        }

        public Task<bool> HealthCheckAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(false);
        }

        public Task<PosTransactionResult> StartSaleAsync(
            TransactionId transactionId, 
            string idempotencyKey, 
            Money amount, 
            TimeSpan timeout, 
            CancellationToken cancellationToken, 
            Guid correlationId)
        {
            throw new HardwareNotConfiguredException("Real POS is not configured.");
        }

        public Task<PosTransactionResult> PreAuthorizeAsync(
            TransactionId transactionId, 
            string idempotencyKey, 
            Money amount, 
            TimeSpan timeout, 
            CancellationToken cancellationToken, 
            Guid correlationId)
        {
            throw new UnsupportedVendorCapabilityException("PreAuthorization is not supported on this model.");
        }

        public Task<PosTransactionResult> CaptureAsync(
            TransactionId transactionId, 
            string idempotencyKey, 
            Money amount, 
            string preAuthReference, 
            TimeSpan timeout, 
            CancellationToken cancellationToken, 
            Guid correlationId)
        {
            throw new UnsupportedVendorCapabilityException("Capture is not supported on this model.");
        }

        public Task<PosTransactionResult> QueryTransactionAsync(
            TransactionId transactionId, 
            string vendorReference, 
            CancellationToken cancellationToken, 
            Guid correlationId)
        {
            throw new HardwareNotConfiguredException("Real POS is not configured.");
        }

        public Task<PosTransactionResult> GetLastTransactionAsync(CancellationToken cancellationToken)
        {
            throw new HardwareNotConfiguredException("Real POS is not configured.");
        }

        public Task<bool> CancelAsync(TransactionId transactionId, CancellationToken cancellationToken)
        {
            throw new HardwareNotConfiguredException("Real POS is not configured.");
        }

        public Task<PosTransactionResult> VoidAsync(
            TransactionId transactionId, 
            string vendorReference, 
            CancellationToken cancellationToken, 
            Guid correlationId)
        {
            throw new HardwareNotConfiguredException("Real POS is not configured.");
        }

        public Task<PosTransactionResult> ReversalAsync(
            TransactionId transactionId, 
            string vendorReference, 
            Money amount, 
            CancellationToken cancellationToken, 
            Guid correlationId)
        {
            throw new HardwareNotConfiguredException("Real POS is not configured.");
        }

        public Task<string> GetBatchSummaryAsync(CancellationToken cancellationToken)
        {
            throw new HardwareNotConfiguredException("Real POS is not configured.");
        }
    }
}
