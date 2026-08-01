using System.Threading.Tasks;
using IzbanKiosk.Application.Hardware.Balance;

namespace IzbanKiosk.Hardware.Balance.BackendBased
{
    public class BackendBasedBalanceProvider : IAuthoritativeBalanceProvider
    {
        public Task InitializeAsync()
        {
            return Task.CompletedTask;
        }

        public Task<bool> HealthCheckAsync()
        {
            return Task.FromResult(false);
        }

        public Task<BalanceResult> GetAuthoritativeBalanceAsync(string cardRef)
        {
            throw new BalanceProviderUnavailableException("Backend SDK not loaded. Failed-closed.");
        }

        public Task<bool> VerifyBalanceAsync(string cardRef, long expectedBalanceMinor)
        {
            throw new BalanceProviderUnavailableException("Backend SDK not loaded. Failed-closed.");
        }

        public Task<BalanceResult> RefreshBalanceAsync(string cardRef)
        {
            throw new BalanceProviderUnavailableException("Backend SDK not loaded. Failed-closed.");
        }
    }
}
