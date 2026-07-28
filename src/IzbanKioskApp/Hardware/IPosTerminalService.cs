using System.Threading.Tasks;

namespace IzbanKioskApp.Hardware
{
    public interface IPosTerminalService
    {
        Task<bool> ConnectAsync(string portOrIp);
        Task<(bool Success, string ApprovalCode, string ErrorMessage)> ProcessPaymentAsync(decimal amount);
    }
}
