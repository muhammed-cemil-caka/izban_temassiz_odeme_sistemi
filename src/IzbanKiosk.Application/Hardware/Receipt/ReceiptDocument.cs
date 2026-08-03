namespace IzbanKiosk.Application.Hardware.Receipt
{
    public class ReceiptDocument
    {
        public string Title { get; }
        public string SubTitle { get; }
        public string StationName { get; }
        public string KioskId { get; }
        public string ReceiptNumber { get; }
        public string TransactionDateTime { get; }
        public string MaskedTransactionId { get; }
        public string MaskedCardNumber { get; }
        public string LoadedAmount { get; }
        public string PreviousBalance { get; }
        public string NewBalance { get; }
        public string Currency { get; }
        public string MaskedPosReference { get; }
        public string PosApprovalCode { get; }
        public string MaskedLoadVendorReference { get; }
        public string TransactionResultText { get; }
        public string SupportContact { get; }
        public string ThankYouMessage { get; }
        public string ContentHash { get; }

        public ReceiptDocument(
            string title,
            string subTitle,
            string stationName,
            string kioskId,
            string receiptNumber,
            string transactionDateTime,
            string maskedTransactionId,
            string maskedCardNumber,
            string loadedAmount,
            string previousBalance,
            string newBalance,
            string currency,
            string maskedPosReference,
            string posApprovalCode,
            string maskedLoadVendorReference,
            string transactionResultText,
            string supportContact,
            string thankYouMessage,
            string contentHash)
        {
            Title = title;
            SubTitle = subTitle;
            StationName = stationName;
            KioskId = kioskId;
            ReceiptNumber = receiptNumber;
            TransactionDateTime = transactionDateTime;
            MaskedTransactionId = maskedTransactionId;
            MaskedCardNumber = maskedCardNumber;
            LoadedAmount = loadedAmount;
            PreviousBalance = previousBalance;
            NewBalance = newBalance;
            Currency = currency;
            MaskedPosReference = maskedPosReference;
            PosApprovalCode = posApprovalCode;
            MaskedLoadVendorReference = maskedLoadVendorReference;
            TransactionResultText = transactionResultText;
            SupportContact = supportContact;
            ThankYouMessage = thankYouMessage;
            ContentHash = contentHash;
        }
    }
}
