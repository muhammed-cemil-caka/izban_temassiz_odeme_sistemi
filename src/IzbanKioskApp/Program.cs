using System;
using System.Threading;

namespace IzbanKioskApp
{
    // --- DONANIM ARAYÜZLERİ VEYA SİMÜLATÖRLERİ ---
    public interface ICardReader
    {
        bool Connect(string portName);
        (string CardUid, decimal CurrentBalance) ReadCard();
        bool WriteBalance(string cardUid, decimal newBalance);
    }

    public interface IPosTerminal
    {
        bool Connect(string portOrIp);
        bool ProcessPayment(decimal amount, out string approvalCode);
    }

    public class MockCardReader : ICardReader
    {
        public bool Connect(string portName)
        {
            Console.WriteLine($"[DONANIM] İzmirim Kart okuyucuya bağlanıldı ({portName})...");
            return true;
        }

        public (string CardUid, decimal CurrentBalance) ReadCard()
        {
            Console.WriteLine("[DONANIM] İzmirim Kart bekleniyor...");
            Thread.Sleep(1000);
            return ("35-IZM-9921", 45.50m);
        }

        public bool WriteBalance(string cardUid, decimal newBalance)
        {
            Console.WriteLine($"[DONANIM] Kart UID: {cardUid} üzerine yeni bakiye yazılıyor ({newBalance} TL)...");
            Thread.Sleep(1200);
            return true;
        }
    }

    public class MockPosTerminal : IPosTerminal
    {
        public bool Connect(string portOrIp)
        {
            Console.WriteLine($"[DONANIM] POS Cihazına bağlanıldı ({portOrIp})...");
            return true;
        }

        public bool ProcessPayment(decimal amount, out string approvalCode)
        {
            Console.WriteLine($"[POS] {amount} TL tutarında temassız ödeme bekleniyor. Kartınızı POS'a yaklaştırın...");
            Thread.Sleep(2000);
            approvalCode = "PROV_" + new Random().Next(100000, 999999);
            Console.WriteLine($"[POS] Ödeme Onaylandı! Onay Kodu: {approvalCode}");
            return true;
        }
    }

    // --- UYGULAMA GİRİŞ NOKTASI ---
    internal class Program
    {
        static void Main(string[] args)
        {
            Console.Clear();
            Console.WriteLine("==================================================");
            Console.WriteLine("  İZBAN KIOSK BAKİYE YÜKLEME SİSTEMİ (PROTOTİP v1) ");
            Console.WriteLine("==================================================\n");

            ICardReader cardReader = new MockCardReader();
            IPosTerminal posTerminal = new MockPosTerminal();

            cardReader.Connect("COM3");
            posTerminal.Connect("192.168.1.100");

            Console.WriteLine("\n--- İŞLEM BAŞLIYOR ---");

            // 1. Kart Okuma
            var cardData = cardReader.ReadCard();
            Console.WriteLine($"\n[KART OKUNDU] Kart No: {cardData.CardUid} | Mevcut Bakiye: {cardData.CurrentBalance} TL");

            // 2. Tutar Seçimi
            Console.WriteLine("\nYüklemek istediğiniz tutarı seçin:");
            Console.WriteLine("1 - 20 TL | 2 - 50 TL | 3 - 100 TL | 4 - 200 TL");
            Console.Write("Seçim (1-4): ");
            string input = Console.ReadLine();
            decimal amount = input switch { "1" => 20m, "2" => 50m, "3" => 100m, "4" => 200m, _ => 50m };

            // 3. POS Ödemesi
            Console.WriteLine($"\n[ÖDEME] {amount} TL POS cihazına gönderiliyor...");
            bool isPaid = posTerminal.ProcessPayment(amount, out string provCode);

            if (isPaid)
            {
                // 4. Karta Bakiye Yazma
                decimal newTotal = cardData.CurrentBalance + amount;
                bool isWritten = cardReader.WriteBalance(cardData.CardUid, newTotal);

                if (isWritten)
                {
                    Console.WriteLine("\n==================================================");
                    Console.WriteLine($" BAŞARILI! İşlem Tamamlandı. Yeni Bakiye: {newTotal} TL");
                    Console.WriteLine("==================================================");
                }
                else
                {
                    Console.WriteLine("\n[HATA] Karta yazma başarısız! POS ödemesi iptal ediliyor (Auto-Void)...");
                }
            }
            else
            {
                Console.WriteLine("\n[HATA] Ödeme alınamadı!");
            }
        }
    }
}