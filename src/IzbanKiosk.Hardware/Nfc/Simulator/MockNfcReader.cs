using System;
using System.Threading;
using System.Threading.Tasks;
using IzbanKiosk.Domain;
using IzbanKiosk.Application.Hardware.Nfc;

namespace IzbanKiosk.Hardware.Nfc.Simulator
{
    public class MockNfcReader : INfcReader
    {
        private long _cardBalanceMinor = 6250; // starts at 62.50 TRY
        private int _transactionCounter = 42;
        private string _cardUidSimulated = "35-IZM-9921";

        // Simulator controls
        public string NextLoadResult { get; set; } = "Success"; // "Success", "Failure", "Timeout", "OutcomeUnknown"
        public string NextWaitCardResult { get; set; } = "Detected"; // "Detected", "Timeout", "None"
        public bool NextValidateResult { get; set; } = true;

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

        public Task<CardSnapshot> ReadCardSnapshotAsync(
            TransactionId transactionId, 
            CardReference cardRef, 
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new CardSnapshot(
                cardRef: cardRef,
                balanceMinor: _cardBalanceMinor,
                transactionCounter: _transactionCounter,
                isValid: true
            ));
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
            _cardBalanceMinor += amount.AmountMinor;
            _transactionCounter++;
            return true;
        }

        public Task<bool> QueryLoadTransactionAsync(
            TransactionId transactionId, 
            string loadVendorReference, 
            CancellationToken cancellationToken, 
            Guid correlationId)
        {
            // If we are in outcome unknown but actually succeeded:
            return Task.FromResult(NextLoadResult == "Success" || NextLoadResult == "OutcomeUnknown");
        }

        public Task<bool> VerifyLoadAsync(
            TransactionId transactionId, 
            CardReference cardRef, 
            Money amount, 
            CancellationToken cancellationToken)
        {
            // Verify if load occurred
            return Task.FromResult(true);
        }

        public Task<long> ReadVerifiedBalanceAsync(
            TransactionId transactionId, 
            CardReference cardRef, 
            CancellationToken cancellationToken)
        {
            return Task.FromResult(_cardBalanceMinor);
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
        public void SetSimulatedCard(string cardUid, long balanceMinor, int transactionCounter = 42)
        {
            _cardUidSimulated = cardUid;
            _cardBalanceMinor = balanceMinor;
            _transactionCounter = transactionCounter;
        }
    }
}
