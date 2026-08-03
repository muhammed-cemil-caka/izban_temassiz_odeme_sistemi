namespace IzbanKiosk.Application.Hardware.Receipt
{
    public class ReceiptPrinterStatus
    {
        public ReceiptPrinterStatusCode Code { get; }
        public string Message { get; }

        public ReceiptPrinterStatus(ReceiptPrinterStatusCode code, string message)
        {
            Code = code;
            Message = message;
        }

        public static ReceiptPrinterStatus Ready => new ReceiptPrinterStatus(ReceiptPrinterStatusCode.Ready, "Yazıcı Hazır");
        public static ReceiptPrinterStatus Offline => new ReceiptPrinterStatus(ReceiptPrinterStatusCode.Offline, "Yazıcı Çevrimdışı");
    }
}
