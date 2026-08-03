namespace IzbanKiosk.Application.Hardware.Receipt
{
    public enum ReceiptPrinterStatusCode
    {
        Ready,
        Offline,
        PaperLow,
        PaperOut,
        CoverOpen,
        CutterError,
        Overheated,
        Busy,
        Unknown
    }
}
