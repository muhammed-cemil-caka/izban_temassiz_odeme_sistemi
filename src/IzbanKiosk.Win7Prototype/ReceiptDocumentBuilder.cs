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
        private const int LineWidth = 52;

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

            builder.AppendLine("[C]IZBAN - IZMIRIM KART");
            builder.AppendLine(english ? "[C]BALANCE ENQUIRY RECEIPT" : "[C]BAKIYE SORGULAMA FISI");
            builder.AppendLine(separator);
            AppendField(builder, english ? "Station" : "Istasyon", stationName);
            AppendField(builder, "Kiosk", kioskId);
            AppendField(builder, english ? "Date" : "Tarih", localTimestamp.ToString("dd.MM.yyyy HH:mm:ss", Turkish));
            builder.AppendLine(separator);
            AppendField(builder, english ? "Card No" : "Kart No", MaskCardNumber(snapshot.CardNumber));
            AppendField(builder, english ? "Card Type" : "Kart Tipi", FallbackDash(snapshot.CardType));
            AppendField(builder, english ? "Balance" : "Bakiye", FormatBalance(snapshot));
            AppendField(builder, english ? "Currency" : "Para Birimi", FallbackDash(snapshot.Currency));
            builder.AppendLine(separator);
            AppendField(builder, english ? "SAM Check" : "SAM Dogrulama", english ? "SUCCESSFUL" : "BASARILI");
            AppendField(builder, english ? "Reference" : "Islem Ref", ShortReference(snapshot.StoragePseudonym));
            builder.AppendLine(separator);
            builder.AppendLine(english
                ? "[C]This receipt is an enquiry only."
                : "[C]Bu fis yalnizca bakiye sorgulamasidir.");
            builder.AppendLine(english
                ? "[C]No payment was taken."
                : "[C]Herhangi bir tahsilat yapilmamistir.");
            builder.AppendLine("[C]IZBAN A.S. - 444 29 26");
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
            builder.AppendLine(Ascii(label).PadRight(18) + ": " + Ascii(value));
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

        private static string FallbackDash(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();
        }

        /// <summary>
        /// KioskPrint.dll marshals text as ANSI. The kiosk locale is not guaranteed to
        /// be Turkish, so fold the Turkish letters instead of risking question marks on
        /// the slip.
        /// </summary>
        private static string Ascii(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(value.Length);
            foreach (char character in value)
            {
                switch (character)
                {
                    case 'ç': builder.Append('c'); break;
                    case 'Ç': builder.Append('C'); break;
                    case 'ğ': builder.Append('g'); break;
                    case 'Ğ': builder.Append('G'); break;
                    case 'ı': builder.Append('i'); break;
                    case 'İ': builder.Append('I'); break;
                    case 'ö': builder.Append('o'); break;
                    case 'Ö': builder.Append('O'); break;
                    case 'ş': builder.Append('s'); break;
                    case 'Ş': builder.Append('S'); break;
                    case 'ü': builder.Append('u'); break;
                    case 'Ü': builder.Append('U'); break;
                    default: builder.Append(character); break;
                }
            }
            return builder.ToString();
        }
    }
}
