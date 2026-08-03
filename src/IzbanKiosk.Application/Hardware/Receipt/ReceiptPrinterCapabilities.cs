namespace IzbanKiosk.Application.Hardware.Receipt
{
    public class ReceiptPrinterCapabilities
    {
        public int PaperWidthMm { get; }
        public bool SupportsCut { get; }
        public bool SupportsStatusQuery { get; }
        public bool SupportsPrintJobQuery { get; }
        public bool SupportsUtf8 { get; }
        public string[] SupportedCodePages { get; }

        public ReceiptPrinterCapabilities(
            int paperWidthMm,
            bool supportsCut,
            bool supportsStatusQuery,
            bool supportsPrintJobQuery,
            bool supportsUtf8,
            string[] supportedCodePages)
        {
            PaperWidthMm = paperWidthMm;
            SupportsCut = supportsCut;
            SupportsStatusQuery = supportsStatusQuery;
            SupportsPrintJobQuery = supportsPrintJobQuery;
            SupportsUtf8 = supportsUtf8;
            SupportedCodePages = supportedCodePages;
        }
    }
}
