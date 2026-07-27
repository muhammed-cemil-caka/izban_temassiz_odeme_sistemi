using System;
using System.Threading;

namespace IzbanKioskApp.UI
{
    public enum KioskState
    {
        Idle,           // Kart bekleniyor
        CardRead,       // Kart okundu, bakiye gösterildi
        AmountSelect,   // Tutar seçiliyor (20, 50, 100, 200 TL)
        PaymentPending, // POS cihazından onay bekleniyor
        WritingCard,    // Karta yeni bakiye yazılıyor
        Success,        // İşlem başarılı
        Error           // Hata durumu
    }

    public class KioskScreenManager
    {
        public static void RenderScreen(KioskState state, string message = "", decimal amount = 0, decimal balance = 0)
        {
            Console.Clear();
            Console.WriteLine("===============================================================");
            Console.WriteLine("                İZBAN KIOSK BAKİYE YÜKLEME                    ");
            Console.WriteLine("===============================================================\n");

            switch (state)
            {
                case KioskState.Idle:
                    Console.WriteLine(" [ EKRAN 1: ANA EKRAN ]");
                    Console.WriteLine(" +---------------------------------------------------------+");
                    Console.WriteLine(" |                                                         |");
                    Console.WriteLine(" |        LÜTFEN İZMİRİM KARTINIZI OKUTUNUZ                |");
                    Console.WriteLine(" |                     (( NFC ))                           |");
                    Console.WriteLine(" |                                                         |");
                    Console.WriteLine(" +---------------------------------------------------------+");
                    break;

                case KioskState.CardRead:
                case KioskState.AmountSelect:
                    Console.WriteLine($" [ EKRAN 2: TUTAR SEÇİMİ ]   Mevcut Bakiye: {balance:F2} TL");
                    Console.WriteLine(" +---------------------------------------------------------+");
                    Console.WriteLine(" | Lütfen yüklemek istediğiniz tutara dokununuz:           |");
                    Console.WriteLine(" |                                                         |");
                    Console.WriteLine(" |  [ 1 - 20 TL ]    [ 2 - 50 TL ]                        |");
                    Console.WriteLine(" |  [ 3 - 100 TL ]   [ 4 - 200 TL ]                       |");
                    Console.WriteLine(" |                                                         |");
                    Console.WriteLine(" +---------------------------------------------------------+");
                    break;

                case KioskState.PaymentPending:
                    Console.WriteLine($" [ EKRAN 3: ÖDEME BEKLENİYOR ]   Seçilen Tutar: {amount:F2} TL");
                    Console.WriteLine(" +---------------------------------------------------------+");
                    Console.WriteLine(" |                                                         |");
                    Console.WriteLine(" |   LÜTFEN KREDİ KARTINIZI POS CİHAZINA YAKLAŞTIRINIZ...  |");
                    Console.WriteLine(" |                  [ Temassız POS ]                       |");
                    Console.WriteLine(" |                                                         |");
                    Console.WriteLine(" +---------------------------------------------------------+");
                    break;

                case KioskState.WritingCard:
                    Console.WriteLine(" [ EKRAN 4: KARTA YAZILIYOR ]");
                    Console.WriteLine(" +---------------------------------------------------------+");
                    Console.WriteLine(" |                                                         |");
                    Console.WriteLine(" |   Ödeme Alındı! Bakiye Kartınıza Yazılıyor...           |");
                    Console.WriteLine(" |           *** LÜTFEN KARTINIZI AYIRMAYIN ***            |");
                    Console.WriteLine(" |                                                         |");
                    Console.WriteLine(" +---------------------------------------------------------+");
                    break;

                case KioskState.Success:
                    Console.WriteLine(" [ EKRAN 5: İŞLEM BAŞARILI ]");
                    Console.WriteLine(" +---------------------------------------------------------+");
                    Console.WriteLine($" |  Yükleme Tamamlandı!                                    |");
                    Console.WriteLine($" |  Yeni Bakiyeniz: {balance:F2} TL                           |");
                    Console.WriteLine(" |                                                         |");
                    Console.WriteLine(" |  İyi yolculuklar dileriz!                               |");
                    Console.WriteLine(" +---------------------------------------------------------+");
                    break;

                case KioskState.Error:
                    Console.WriteLine(" [ HATA EKRANI ]");
                    Console.WriteLine(" +---------------------------------------------------------+");
                    Console.WriteLine($" |  HATA: {message}                                        |");
                    Console.WriteLine(" |  İşlem tamamlanamadı. Lütfen görevliye başvurunuz.      |");
                    Console.WriteLine(" +---------------------------------------------------------+");
                    break;
            }
            Console.WriteLine("\n===============================================================");
        }
    }
}