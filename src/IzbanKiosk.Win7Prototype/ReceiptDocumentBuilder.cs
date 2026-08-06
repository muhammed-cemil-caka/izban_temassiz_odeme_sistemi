using System;
using System.Globalization;
using System.Text;
using IzbanKiosk.LegacyHardware.Contracts;

namespace IzbanKiosk.Win7Prototype
{
    /// <summary>
    /// Renders the plain-text receipt body that the bridge feeds to KioskPrint.dll.
    /// Lines prefixed with <c>[C]</c> are centred by the vendor library; every other
    /// line is printed left-aligned. Layout follows the deployed AUSKiosk slips so the
    /// output is familiar to passengers and station staff.
    /// </summary>
    internal static class ReceiptDocumentBuilder
    {
        /// <summary>
        /// Separator width in characters. The first physical slips printed at 52 and
        /// the rule reached the right edge of the 56 mm roll, so it is cut back to
        /// leave a visible margin on both sides.
        /// </summary>
        private const int LineWidth = 46;

        private const int LabelWidth = 15;

        private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

        internal static string BuildBalanceReceipt(
            CardSnapshotResponse snapshot,
            string stationName,
            string kioskId,
            DateTime localTimestamp,
            bool english)
        {
            var builder = new StringBuilder();
            string separator = new string('-', LineWidth);

            builder.AppendLine("[C]İZBAN - İZMİRİM KART");
            builder.AppendLine(english ? "[C]BALANCE ENQUIRY RECEIPT" : "[C]BAKİYE SORGULAMA FİŞİ");
            builder.AppendLine(separator);
            AppendField(builder, english ? "Station" : "İstasyon", stationName);
            AppendField(builder, "Kiosk", kioskId);
            AppendField(builder, english ? "Date" : "Tarih", localTimestamp.ToString("dd.MM.yyyy HH:mm:ss", Turkish));
            builder.AppendLine(separator);
            AppendField(builder, english ? "Card No" : "Kart No", MaskCardNumber(snapshot.CardNumber));

            // The vendor returns a bare type code. Printing "1" tells the passenger
            // nothing, and translating it into a fare name would be a guess that could
            // put the wrong entitlement on someone's receipt, so the line is only
            // printed when the reader supplies an actual name.
            string cardType = DescribeCardType(snapshot.CardType);
            if (cardType.Length > 0)
            {
                AppendField(builder, english ? "Card Type" : "Kart Tipi", cardType);
            }

            AppendField(builder, english ? "Balance" : "Bakiye", FormatBalance(snapshot));
            builder.AppendLine(separator);
            AppendField(builder, english ? "SAM Check" : "SAM Doğrulama", english ? "SUCCESSFUL" : "BAŞARILI");
            AppendField(builder, english ? "Reference" : "İşlem Ref", ShortReference(snapshot.StoragePseudonym));
            builder.AppendLine(separator);
            builder.AppendLine(english
                ? "[C]This receipt is an enquiry only."
                : "[C]Bu fiş yalnızca bakiye sorgulamasıdır.");
            builder.AppendLine(english
                ? "[C]No payment was taken."
                : "[C]Herhangi bir tahsilat yapılmamıştır.");
            builder.AppendLine("[C]İZBAN A.Ş. - 444 29 26");
            builder.AppendLine();
            builder.AppendLine();
            builder.AppendLine();

            return builder.ToString();
        }

        /// <summary>
        /// Idempotency key for a single card presentation. The bridge refuses to print
        /// the same key twice, so a double tap on the button cannot produce two slips.
        /// Uses the storage pseudonym rather than the card number: the raw identifier
        /// must never leave the screen.
        /// </summary>
        internal static string BuildIdempotencyKey(CardSnapshotResponse snapshot, DateTime localTimestamp)
        {
            string pseudonym = string.IsNullOrWhiteSpace(snapshot.StoragePseudonym)
                ? "UNKNOWN"
                : snapshot.StoragePseudonym;
            return "BAL_" + pseudonym + "_" + localTimestamp.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        }

        private static void AppendField(StringBuilder builder, string label, string value)
        {
            builder.AppendLine(label.PadRight(LabelWidth) + ": " + value);
        }

        private static string FormatBalance(CardSnapshotResponse snapshot)
        {
            if (snapshot.BalanceScale <= 0)
            {
                return "-";
            }
            decimal balance = snapshot.BalanceMinor / (decimal)snapshot.BalanceScale;
            return balance.ToString("N2", Turkish) + " TL";
        }

        private static string DescribeCardType(string cardType)
        {
            string value = (cardType ?? string.Empty).Trim();
            if (value.Length == 0)
            {
                return string.Empty;
            }

            foreach (char character in value)
            {
                if (!char.IsDigit(character))
                {
                    return value;
                }
            }

            // Digits only: a raw vendor code, not a fare name.
            return string.Empty;
        }

        private static string MaskCardNumber(string cardNumber)
        {
            string value = (cardNumber ?? string.Empty).Trim();
            if (value.Length <= 4)
            {
                return value.Length == 0 ? "-" : new string('*', value.Length);
            }
            return new string('*', value.Length - 4) + value.Substring(value.Length - 4);
        }

        private static string ShortReference(string pseudonym)
        {
            string value = (pseudonym ?? string.Empty).Trim();
            if (value.Length == 0)
            {
                return "-";
            }
            return value.Length <= 16 ? value : value.Substring(0, 16);
        }
    }
}
