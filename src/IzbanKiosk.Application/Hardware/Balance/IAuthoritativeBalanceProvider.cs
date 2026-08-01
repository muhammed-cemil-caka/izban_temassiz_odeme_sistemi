using System;
using System.Threading.Tasks;

namespace IzbanKiosk.Application.Hardware.Balance
{
    public interface IAuthoritativeBalanceProvider
    {
        Task InitializeAsync();
        Task<bool> HealthCheckAsync();
        Task<BalanceResult> GetAuthoritativeBalanceAsync(string cardRef);
        Task<bool> VerifyBalanceAsync(string cardRef, long expectedBalanceMinor);
        Task<BalanceResult> RefreshBalanceAsync(string cardRef);
    }

    public class BalanceProviderUnavailableException : Exception
    {
        public BalanceProviderUnavailableException(string message) : base(message) { }
    }
}
