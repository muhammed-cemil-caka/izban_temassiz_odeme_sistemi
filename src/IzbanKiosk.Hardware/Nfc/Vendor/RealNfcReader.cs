using System;
using System.Threading;
using System.Threading.Tasks;
using IzbanKiosk.Domain;
using IzbanKiosk.Application.Hardware.Nfc;

namespace IzbanKiosk.Hardware.Nfc.Vendor
{
    public class RealNfcReader : INfcReader
    {
        public Task InitializeAsync(CancellationToken cancellationToken)
        {
            throw new HardwareNotConfiguredException("Real NFC Reader Hardware not configured. Missing Izmirim Card SDK.");
        }

        public Task<bool> ConnectAsync(CancellationToken cancellationToken)
        {
            throw new VendorSdkUnavailableException("Real NFC SDK binary dependencies are missing.");
        }

        public Task<bool> HealthCheckAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(false);
        }

        public Task<CardReference?> WaitForCardAsync(
            TransactionId transactionId, 
            TimeSpan timeout, 
            CancellationToken cancellationToken)
        {
            throw new HardwareNotConfiguredException("Real NFC terminal is not configured.");
        }

        public Task<bool> ValidateCardAsync(
            TransactionId transactionId, 
            CardReference cardRef, 
            CancellationToken cancellationToken)
        {
            throw new HardwareNotConfiguredException("Real NFC terminal is not configured.");
        }

        public Task<CardSnapshot> ReadCardSnapshotAsync(
            TransactionId transactionId, 
            CardReference cardRef, 
            CancellationToken cancellationToken)
        {
            throw new HardwareNotConfiguredException("Real NFC terminal is not configured.");
        }

        public Task<bool> LoadAmountAsync(
            TransactionId transactionId, 
            string idempotencyKey, 
            CardReference cardRef, 
            Money amount, 
            CancellationToken cancellationToken, 
            Guid correlationId)
        {
            throw new HardwareNotConfiguredException("Real NFC terminal is not configured.");
        }

        public Task<bool> QueryLoadTransactionAsync(
            TransactionId transactionId, 
            string loadVendorReference, 
            CancellationToken cancellationToken, 
            Guid correlationId)
        {
            throw new HardwareNotConfiguredException("Real NFC terminal is not configured.");
        }

        public Task<bool> VerifyLoadAsync(
            TransactionId transactionId, 
            CardReference cardRef, 
            Money amount, 
            CancellationToken cancellationToken)
        {
            throw new HardwareNotConfiguredException("Real NFC terminal is not configured.");
        }

        public Task<long> ReadVerifiedBalanceAsync(
            TransactionId transactionId, 
            CardReference cardRef, 
            CancellationToken cancellationToken)
        {
            throw new HardwareNotConfiguredException("Real NFC terminal is not configured.");
        }

        public Task WaitForCardRemovalAsync(
            TransactionId transactionId, 
            CardReference cardRef, 
            TimeSpan timeout, 
            CancellationToken cancellationToken)
        {
            throw new HardwareNotConfiguredException("Real NFC terminal is not configured.");
        }
    }
}
