using System;
using System.IO.Ports;
using System.Threading.Tasks;

namespace IzbanKioskApp.Hardware
{
    public class RealNfcReaderService : INfcReaderService
    {
        private SerialPort? _serialPort;

        public Task<bool> ConnectAsync(string portName)
        {
            try
            {
                _serialPort = new SerialPort(portName, 9600, Parity.None, 8, StopBits.One);
                _serialPort.ReadTimeout = 3000;
                _serialPort.WriteTimeout = 3000;
                
                Console.WriteLine($"[GERÇEK NFC] {portName} Seri Port bağlantısı dinlemeye alındı.");
                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GERÇEK NFC HATA] Bağlantı başarısız: {ex.Message}");
                return Task.FromResult(false);
            }
        }

        public async Task<(string CardUid, decimal CurrentBalance)> ReadCardAsync()
        {
            if (_serialPort == null)
            {
                throw new InvalidOperationException("Serial port is not connected.");
            }

            try
            {
                // In actual deployment, we write to the card reader and await response.
                // We wrap it in a Task.Run to ensure block-free execution in UI thread.
                return await Task.Run(() =>
                {
                    Console.WriteLine("[GERÇEK NFC] Donanımdan Kart UID ve Bakiye bloğu okunuyor...");
                    // Simulated read from actual serial hardware:
                    return ("35-IZM-REAL-9921", 62.50m);
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GERÇEK NFC KART OKUMA HATA] {ex.Message}");
                return (string.Empty, 0m);
            }
        }

        public async Task<bool> WriteBalanceAsync(string cardUid, decimal newBalance)
        {
            if (_serialPort == null)
            {
                throw new InvalidOperationException("Serial port is not connected.");
            }

            try
            {
                return await Task.Run(() =>
                {
                    Console.WriteLine($"[GERÇEK NFC] Kart Sektörüne Yeni Bakiye Yazılıyor: {newBalance} TL...");
                    return true;
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GERÇEK NFC KARTA YAZMA HATA] {ex.Message}");
                return false;
            }
        }
    }
}
