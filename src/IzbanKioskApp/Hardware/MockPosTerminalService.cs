using System;
using System.Threading.Tasks;

namespace IzbanKioskApp.Hardware
{
    public class MockPosTerminalService : IPosTerminalService
    {
        private bool _connected;

        public Task<bool> ConnectAsync(string portOrIp)
        {
            _connected = true;
            Console.WriteLine($"[MOCK POS] Bağlantı sağlandı ({portOrIp}).");
            return Task.FromResult(true);
        }

        public async Task<(bool Success, string ApprovalCode, string ErrorMessage)> ProcessPaymentAsync(decimal amount)
        {
            if (!_connected)
            {
                return (false, string.Empty, "POS Terminal not connected.");
            }

            // Simulate bank communication latency
            await Task.Delay(2500);

            // Mock success code
            string approvalCode = "PROV_" + Random.Shared.Next(100000, 999999);
            Console.WriteLine($"[MOCK POS] Ödeme Başarılı: {amount} TL | Kod: {approvalCode}");
            return (true, approvalCode, string.Empty);
        }
    }
}
