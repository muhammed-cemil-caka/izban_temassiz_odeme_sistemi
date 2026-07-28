using System.Threading.Tasks;

namespace IzbanKioskApp.Hardware
{
    public interface INfcReaderService
    {
        Task<bool> ConnectAsync(string portName);
        Task<(string CardUid, decimal CurrentBalance)> ReadCardAsync();
        Task<bool> WriteBalanceAsync(string cardUid, decimal newBalance);
    }
}
