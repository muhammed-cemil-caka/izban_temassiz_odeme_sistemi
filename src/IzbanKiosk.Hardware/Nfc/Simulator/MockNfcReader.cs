using System;
using System.Threading;
using System.Threading.Tasks;
using IzbanKiosk.Domain;
using IzbanKiosk.Application.Hardware.Nfc;
using IzbanKiosk.Application.Hardware.Balance;

namespace IzbanKiosk.Hardware.Nfc.Simulator
{
    public class MockNfcReader : INfcReader
    {
        private readonly ISimulatorCardLedger _ledger;
        private string _cardUidSimulated = "35-IZM-9921";

        // Simulator controls
        public string NextLoadResult { get; set; } = "Success"; // "Success", "Failure", "Timeout", "OutcomeUnknown"
        public string NextWaitCardResult { get; set; } = "Detected"; // "Detected", "Timeout", "None"
        public bool NextValidateResult { get; set; } = true;
        public long? CustomVerifiedBalance { get; set; }

        public MockNfcReader(ISimulatorCardLedger ledger)
        {
            _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
        }

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

        public async Task<CardReference?> WaitForCardAsync(
            TransactionId transactionId, 
            TimeSpan timeout, 
            CancellationToken cancellationToken)
        {
            await Task.Delay(200, cancellationToken); // Simulate card tap delay

            if (NextWaitCardResult == "Timeout")
            {
                await Task.Delay((int)timeout.TotalMilliseconds + 50, cancellationToken);
                return null;
            }

            if (NextWaitCardResult == "None")
            {
                return null;
            }

            return CardReference.Create(_cardUidSimulated);
        }

        public Task<bool> ValidateCardAsync(
            TransactionId transactionId, 
            CardReference cardRef, 
            CancellationToken cancellationToken)
        {
            return Task.FromResult(NextValidateResult);
        }

        public async Task<CardSnapshot> ReadCardSnapshotAsync(
            TransactionId transactionId, 
            CardReference cardRef, 
            CancellationToken cancellationToken)
        {
            var record = await _ledger.GetOrCreateCardAsync(cardRef.Hash);
            return new CardSnapshot(
                cardRef: cardRef,
                balanceMinor: record.BalanceMinor,
                transactionCounter: record.CardTransactionCounter,
                isValid: true
            );
        }

        public async Task<bool> LoadAmountAsync(
            TransactionId transactionId, 
            string idempotencyKey, 
            CardReference cardRef, 
            Money amount, 
            CancellationToken cancellationToken, 
            Guid correlationId)
        {
            await Task.Delay(150, cancellationToken); // Simulate card write delay

            if (NextLoadResult == "Timeout")
            {
                throw new TimeoutException("NFC write timed out.");
            }

            if (NextLoadResult == "Failure")
            {
                return false;
            }

            if (NextLoadResult == "OutcomeUnknown")
            {
                // Simulate disconnected response
                throw new TaskCanceledException("NFC connection broken during write.");
            }

            // Success
            var record = await _ledger.GetOrCreateCardAsync(cardRef.Hash);
            bool updated = await _ledger.UpdateBalanceAsync(
                cardRef.Hash,
                record.BalanceMinor,
                record.BalanceMinor + amount.AmountMinor,
                idempotencyKey,
                1
            );

            return updated;
        }

        public async Task<bool> QueryLoadTransactionAsync(
            TransactionId transactionId, 
            string loadVendorReference, 
            CancellationToken cancellationToken, 
            Guid correlationId)
        {
            return await _ledger.IsLoadReferenceProcessedAsync(loadVendorReference);
        }

        public async Task<bool> VerifyLoadAsync(
            TransactionId transactionId, 
            CardReference cardRef, 
            Money amount, 
            CancellationToken cancellationToken)
        {
            var record = await _ledger.GetOrCreateCardAsync(cardRef.Hash);
            return record.LastLoadReference != null;
        }

        public async Task<long> ReadVerifiedBalanceAsync(
            TransactionId transactionId, 
            CardReference cardRef, 
            CancellationToken cancellationToken)
        {
            if (CustomVerifiedBalance.HasValue)
            {
                return CustomVerifiedBalance.Value;
            }
            var record = await _ledger.GetOrCreateCardAsync(cardRef.Hash);
            return record.BalanceMinor;
        }

        public async Task WaitForCardRemovalAsync(
            TransactionId transactionId, 
            CardReference cardRef, 
            TimeSpan timeout, 
            CancellationToken cancellationToken)
        {
            await Task.Delay(100, cancellationToken); // Simulate card removed
        }

        // Helper to override simulated values
        public void SetSimulatedCardUid(string uid)
        {
            _cardUidSimulated = uid;
        }

        [Obsolete("Use SetSimulatedCardUid, balance is managed by the ledger")]
        public void SetSimulatedCard(string cardUid, long balanceMinor, int transactionCounter = 42)
        {
            _cardUidSimulated = cardUid;
        }
    }
}
