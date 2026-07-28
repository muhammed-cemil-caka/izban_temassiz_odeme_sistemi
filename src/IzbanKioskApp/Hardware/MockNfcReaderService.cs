using System;
using System.Threading.Tasks;

namespace IzbanKioskApp.Hardware
{
    public class MockNfcReaderService : INfcReaderService
    {
        private bool _connected;

        public Task<bool> ConnectAsync(string portName)
        {
            _connected = true;
            Console.WriteLine($"[MOCK NFC] Bağlantı sağlandı ({portName}).");
            return Task.FromResult(true);
        }

        public async Task<(string CardUid, decimal CurrentBalance)> ReadCardAsync()
        {
            if (!_connected)
            {
                throw new InvalidOperationException("NFC Reader is not connected.");
            }
            await Task.Delay(300); // Simulate NFC poll latency
            Console.WriteLine("[MOCK NFC] Kart başarıyla okundu.");
            return ("35-IZM-9921", 45.50m);
        }

        public async Task<bool> WriteBalanceAsync(string cardUid, decimal newBalance)
        {
            if (!_connected)
            {
                throw new InvalidOperationException("NFC Reader is not connected.");
            }
            await Task.Delay(400); // Simulate sector write latency
            Console.WriteLine($"[MOCK NFC] Sektör güncellendi: {cardUid} | Yeni Bakiye: {newBalance} TL");
            return true;
        }
    }
}
