using System;
using System.Threading;
using IzbanKioskApp.Core;
using IzbanKioskApp.UI;

namespace IzbanKioskApp
{
    internal class Program
    {
        static void Main(string[] args)
        {
            // 0. Yerel Veritabanını Başlat
            DatabaseService.InitializeDatabase();
            Thread.Sleep(1000);

            // 1. EKRAN: ANA EKRAN
            KioskScreenManager.RenderScreen(KioskState.Idle);
            Thread.Sleep(1500);

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
            Thread.Sleep(2000);
            string approvalCode = "PROV_" + new Random().Next(100000, 999999);

            // 4. EKRAN: KARTA BAKIYE YAZILIYOR
            KioskScreenManager.RenderScreen(KioskState.WritingCard);
            Thread.Sleep(1500);

            // 5. YEREL VERİTABANINA LOG YAZ (FAIL-SAFE / OFFLINE LOG)
            DatabaseService.LogTransaction(cardUid, selectedAmount, approvalCode, "SUCCESS");

            // 6. EKRAN: BAŞARILI
            decimal newBalance = currentBalance + selectedAmount;
            KioskScreenManager.RenderScreen(KioskState.Success, balance: newBalance);

            Console.WriteLine("\n[Sistem] 3 saniye sonra Ana Ekran'a dönülecek...");
            Thread.Sleep(3000);
        }
    }
}