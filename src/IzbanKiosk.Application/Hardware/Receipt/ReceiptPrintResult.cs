namespace IzbanKiosk.Application.Hardware.Receipt
{
    public class ReceiptPrintResult
    {
        public bool Success { get; }
        public ReceiptPrintOutcome Outcome { get; }
        public string PrinterJobReference { get; }
        public string VendorResponseCode { get; }
        public string ErrorCode { get; }
        public string ErrorMessage { get; }

        public ReceiptPrintResult(
            bool success,
            ReceiptPrintOutcome outcome,
            string printerJobReference,
            string vendorResponseCode,
            string errorCode,
            string errorMessage)
        {
            Success = success;
            Outcome = outcome;
            PrinterJobReference = printerJobReference;
            VendorResponseCode = vendorResponseCode;
            ErrorCode = errorCode;
            ErrorMessage = errorMessage;
        }

        public static ReceiptPrintResult Successful(string printerJobReference)
            => new ReceiptPrintResult(true, ReceiptPrintOutcome.Success, printerJobReference, "00", null, null);

        public static ReceiptPrintResult StatusFailed(ReceiptPrintOutcome outcome, string errorCode, string errorMessage)
            => new ReceiptPrintResult(false, outcome, null, null, errorCode, errorMessage);
    }
}
