using System;
using System.Threading.Tasks;

namespace IzbanKiosk.Application.Hardware.Balance
{
    public class SimulatorCardRecord
    {
        public string CardRef { get; }
        public long BalanceMinor { get; }
        public string Currency { get; }
        public int CardTransactionCounter { get; }
        public string? LastLoadReference { get; }
        public DateTime UpdatedAtUtc { get; }
        public int RowVersion { get; }

        public SimulatorCardRecord(
            string cardRef, 
            long balanceMinor, 
            string currency, 
            int cardTransactionCounter, 
            string? lastLoadReference, 
            DateTime updatedAtUtc, 
            int rowVersion)
        {
            CardRef = cardRef ?? throw new ArgumentNullException(nameof(cardRef));
            BalanceMinor = balanceMinor;
            Currency = currency ?? "TRY";
            CardTransactionCounter = cardTransactionCounter;
            LastLoadReference = lastLoadReference;
            UpdatedAtUtc = updatedAtUtc;
            RowVersion = rowVersion;
        }
    }

    public interface ISimulatorCardLedger
    {
        Task<SimulatorCardRecord> GetOrCreateCardAsync(string cardRef, long initialBalanceMinor = 6250);
        Task<bool> UpdateBalanceAsync(
            string cardRef, 
            long expectedBalanceMinor, 
            long newBalanceMinor, 
            string? loadReference, 
            int transactionCounterIncrement);
        Task<bool> IsLoadReferenceProcessedAsync(string loadReference);
        Task ResetAllAsync();
    }
}
