using System;
using System.Threading.Tasks;
using IzbanKiosk.Application.Hardware.Balance;

namespace IzbanKiosk.Hardware.Balance.Simulator
{
    public class MockBalanceProvider : IAuthoritativeBalanceProvider
    {
        private long _simulatedBalanceMinor = 6250; // 62.50 TL

        public Task InitializeAsync()
        {
            return Task.CompletedTask;
        }

        public Task<bool> HealthCheckAsync()
        {
            return Task.FromResult(true);
        }

        public Task<BalanceResult> GetAuthoritativeBalanceAsync(string cardRef)
        {
            return Task.FromResult(new BalanceResult(
                isAuthoritative: true,
                isVerified: true,
                balanceMinor: _simulatedBalanceMinor,
                timestampUtc: DateTime.UtcNow,
                isStale: false
            ));
        }

        public Task<bool> VerifyBalanceAsync(string cardRef, long expectedBalanceMinor)
        {
            return Task.FromResult(true);
        }

        public Task<BalanceResult> RefreshBalanceAsync(string cardRef)
        {
            return GetAuthoritativeBalanceAsync(cardRef);
        }

        // Helper for simulator to modify the balance
        public void SetSimulatedBalance(long balanceMinor)
        {
            _simulatedBalanceMinor = balanceMinor;
        }
    }
}
