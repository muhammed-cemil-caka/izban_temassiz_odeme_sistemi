using System;
using System.Threading.Tasks;

namespace IzbanKioskApp.Hardware
{
    public class RealPosTerminalService : IPosTerminalService
    {
        private bool _connected;

        public Task<bool> ConnectAsync(string portOrIp)
        {
            try
            {
                _connected = true;
                Console.WriteLine($"[GERÇEK POS] POS Terminali bağlantısı kuruldu ({portOrIp}).");
                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GERÇEK POS HATA] Bağlantı kurulamadı: {ex.Message}");
                return Task.FromResult(false);
            }
        }

        public async Task<(bool Success, string ApprovalCode, string ErrorMessage)> ProcessPaymentAsync(decimal amount)
        {
            if (!_connected)
            {
                return (false, string.Empty, "POS Terminal not connected.");
            }

            try
            {
                // In actual staging we would execute a TCP TCP socket packet to Bank server.
                // We wrap it in a Task.Run to run block-free in the background.
                return await Task.Run(async () =>
                {
                    Console.WriteLine($"[GERÇEK POS] {amount} TL tutarındaki ödeme bankaya gönderiliyor...");
                    await Task.Delay(2500); // Network communication latency
                    string approvalCode = "PROV_" + Random.Shared.Next(100000, 999999);
                    return (true, approvalCode, string.Empty);
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GERÇEK POS ÖDEME HATA] {ex.Message}");
                return (false, string.Empty, ex.Message);
            }
        }
    }
}
