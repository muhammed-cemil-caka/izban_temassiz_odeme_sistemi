using System;
using System.Security.Cryptography;
using System.Text;
using System.Globalization;
using IzbanKiosk.Domain;
using IzbanKiosk.Application.Hardware.Receipt;

namespace IzbanKiosk.Application.Services
{
    public class ReceiptDocumentFactory
    {
        private static readonly CultureInfo TurkishCulture = new CultureInfo("tr-TR");

        public ReceiptDocument CreateReceipt(KioskTransaction tx, string stationName, string kioskId)
        {
            if (tx == null) throw new ArgumentNullException(nameof(tx));
            if (tx.State != KioskTransactionState.Completed)
            {
                throw new InvalidOperationException("Cannot create receipt for non-completed transaction.");
            }

            string maskedCard = tx.CardRef?.Masked ?? "UNKNOWN-CARD";
            decimal amountVal = tx.Amount?.ToDecimal() ?? 0m;
            decimal prevBalVal = tx.PreviousBalanceMinor / 100m;
            decimal newBalVal = tx.NewBalanceMinor / 100m;

            string loadedAmount = amountVal.ToString("C2", TurkishCulture);
            string previousBalance = prevBalVal.ToString("C2", TurkishCulture);
            string newBalance = newBalVal.ToString("C2", TurkishCulture);

            string txDateTime = tx.LastModifiedAtUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss", TurkishCulture);
            string maskedTxId = tx.Id.Value.ToString().Substring(0, 8) + "...";
            
            string maskedPosRef = string.IsNullOrEmpty(tx.PosVendorReference) 
                ? "N/A" 
                : (tx.PosVendorReference.Length > 6 
                    ? tx.PosVendorReference.Substring(0, 4) + "****" 
                    : tx.PosVendorReference);

            string maskedLoadRef = string.IsNullOrEmpty(tx.LoadVendorReference)
                ? "N/A"
                : (tx.LoadVendorReference.Length > 6
                    ? tx.LoadVendorReference.Substring(0, 4) + "****"
                    : tx.LoadVendorReference);

            string posApproval = tx.PosApprovalCode ?? "N/A";

            string receiptNumber = new Random().Next(100000, 999999).ToString();

            // Determinisitc hash from non-sensitive fields
            string rawContentString = $"{tx.Id.Value}|{amountVal}|{newBalVal}|{txDateTime}";
            string contentHash = ComputeSha256Hash(rawContentString);

            return new ReceiptDocument(
                title: "İZBAN / İZMİRİM KART",
                subTitle: "BAKİYE YÜKLEME BİLGİ MAKBUZU",
                stationName: stationName,
                kioskId: kioskId,
                receiptNumber: receiptNumber,
                transactionDateTime: txDateTime,
                maskedTransactionId: maskedTxId,
                maskedCardNumber: maskedCard,
                loadedAmount: loadedAmount,
                previousBalance: previousBalance,
                newBalance: newBalance,
                currency: "TRY",
                maskedPosReference: maskedPosRef,
                posApprovalCode: posApproval,
                maskedLoadVendorReference: maskedLoadRef,
                transactionResultText: "İşlem Sonucu: Başarılı",
                supportContact: "Destek Hattı: ALO 153",
                thankYouMessage: "İyi Yolculuklar Dileriz.",
                contentHash: contentHash
            );
        }

        private string ComputeSha256Hash(string rawData)
        {
            using (SHA256 sha256Hash = SHA256.Create())
            {
                byte[] bytes = sha256Hash.ComputeHash(Encoding.UTF8.GetBytes(rawData));
                StringBuilder builder = new StringBuilder();
                for (int i = 0; i < bytes.Length; i++)
                {
                    builder.Append(bytes[i].ToString("x2"));
                }
                return builder.ToString();
            }
        }
    }
}
