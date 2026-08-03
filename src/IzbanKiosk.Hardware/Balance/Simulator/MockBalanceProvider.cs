using System;
using System.Threading.Tasks;
using IzbanKiosk.Application.Hardware.Balance;

namespace IzbanKiosk.Hardware.Balance.Simulator
{
    public class MockBalanceProvider : IAuthoritativeBalanceProvider
    {
        private readonly ISimulatorCardLedger _ledger;

        public MockBalanceProvider(ISimulatorCardLedger ledger)
        {
            _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
        }

        public Task InitializeAsync()
        {
            return Task.CompletedTask;
        }

        public Task<bool> HealthCheckAsync()
        {
            return Task.FromResult(true);
        }

        public async Task<BalanceResult> GetAuthoritativeBalanceAsync(string cardRef)
        {
            var record = await _ledger.GetOrCreateCardAsync(cardRef);
            return new BalanceResult(
                isAuthoritative: true,
                isVerified: true,
                balanceMinor: record.BalanceMinor,
                timestampUtc: DateTime.UtcNow,
                isStale: false
            );
        }

        public async Task<bool> VerifyBalanceAsync(string cardRef, long expectedBalanceMinor)
        {
            var record = await _ledger.GetOrCreateCardAsync(cardRef);
            return record.BalanceMinor == expectedBalanceMinor;
        }

        public Task<BalanceResult> RefreshBalanceAsync(string cardRef)
        {
            return GetAuthoritativeBalanceAsync(cardRef);
        }

        // Helper for simulator to modify the balance
        public void SetSimulatedBalance(long balanceMinor)
        {
            // Deprecated - balance is managed via ledger
        }
    }
}
