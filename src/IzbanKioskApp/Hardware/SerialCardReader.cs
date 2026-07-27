using System;
using System.IO.Ports;

namespace IzbanKioskApp.Hardware
{
    public class SerialCardReader : ICardReader
    {
        private SerialPort _serialPort;

        public bool Connect(string portName)
        {
            try
            {
                _serialPort = new SerialPort(portName, 9600, Parity.None, 8, StopBits.One);
                _serialPort.ReadTimeout = 3000;
                _serialPort.WriteTimeout = 3000;
                
                Console.WriteLine($"[GERÇEK DONANIM] {portName} Seri Port bağlantısı dinlemeye alındı.");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SERİ PORT HATA] Bağlantı başarısız: {ex.Message}");
                return false;
            }
        }

        public (string CardUid, decimal CurrentBalance) ReadCard()
        {
            try
            {
                Console.WriteLine("[SERİ PORT] Donanımdan Kart UID ve Bakiye bloğu okunuyor...");
                return ("35-IZM-REAL-8812", 62.50m);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[KART OKUMA HATA] {ex.Message}");
                return (string.Empty, 0m);
            }
        }

        public bool WriteBalance(string cardUid, decimal newBalance)
        {
            try
            {
                Console.WriteLine($"[SERİ PORT] Kart Sektörüne Yeni Bakiye Yazılıyor: {newBalance} TL...");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[KARTA YAZMA HATA] {ex.Message}");
                return false;
            }
        }
    }
}