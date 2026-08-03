namespace IzbanKiosk.Application.Hardware.Receipt
{
    public enum ReceiptPrintOutcome
    {
        Success,
        Offline,
        PaperOut,
        PaperLow,
        CoverOpen,
        CutterError,
        Overheated,
        Busy,
        Timeout,
        HardwareError,
        OutcomeUnknown,
        Failed
    }
}
