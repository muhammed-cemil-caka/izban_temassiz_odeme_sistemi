namespace IzbanKiosk.Domain
{
    public enum ReceiptStatus
    {
        Offered,
        Declined,
        TimedOut,
        Requested,
        Printing,
        Printed,
        PrintOutcomeUnknown,
        PaperOut,
        Failed
    }
}
