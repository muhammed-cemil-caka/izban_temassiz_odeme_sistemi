using System;
using System.Threading;
using IzbanKioskApp.UI;

namespace IzbanKioskApp
{
    internal class Program
    {
        static void Main(string[] args)
        {
            // 1. EKRAN: ANA EKRAN (KART BEKLENİYOR)
            KioskScreenManager.RenderScreen(KioskState.Idle);
            Thread.Sleep(2000); // Kart okunma simülasyonu

            string cardUid = "35-IZM-9921";
            decimal currentBalance = 45.50m;

            // 2. EKRAN: TUTAR SEÇİMİ
            KioskScreenManager.RenderScreen(KioskState.AmountSelect, balance: currentBalance);
            Console.Write("\n[Dokunmatik Ekran Simülasyonu] Seçiminiz (1: 20TL, 2: 50TL, 3: 100TL, 4: 200TL): ");
            string input = Console.ReadLine();

            decimal selectedAmount = input switch
            {
                "1" => 20m,
                "2" => 50m,
                "3" => 100m,
                "4" => 200m,
                _ => 50m
            };

            // 3. EKRAN: POS ÖDEME BEKLENİYOR
            KioskScreenManager.RenderScreen(KioskState.PaymentPending, amount: selectedAmount);
            Thread.Sleep(2500); // POS ödeme simülasyonu

            // 4. EKRAN: KARTA BAKIYE YAZILIYOR
            KioskScreenManager.RenderScreen(KioskState.WritingCard);
            Thread.Sleep(2000); // NFC karta yazma simülasyonu

            // 5. EKRAN: BAŞARILI
            decimal newBalance = currentBalance + selectedAmount;
            KioskScreenManager.RenderScreen(KioskState.Success, balance: newBalance);

            Console.WriteLine("\n[Sistem] 5 saniye sonra Ana Ekran'a dönülecek...");
            Thread.Sleep(3000);
        }
    }
}