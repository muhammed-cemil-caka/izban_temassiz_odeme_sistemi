namespace IzbanKiosk.Application.Hardware.Receipt
{
    public class ReceiptPrinterOptions
    {
        public bool Enabled { get; set; } = true;
        public string Interface { get; set; } = "VendorSdk";
        public string PrinterName { get; set; } = "";
        public string Port { get; set; } = "COM5";
        public int BaudRate { get; set; } = 9600;
        public int PaperWidthMm { get; set; } = 80;
        public string CodePage { get; set; } = "CP857";
        public bool CutAfterPrint { get; set; } = true;
        public int PrintTimeoutSeconds { get; set; } = 10;
        public int DecisionTimeoutSeconds { get; set; } = 20;
        public SimulatorOptions Simulator { get; set; } = new SimulatorOptions();
    }

    public class SimulatorOptions
    {
        public string NextResult { get; set; } = "Success";
        public bool WritePreviewFile { get; set; } = false;
        public string PreviewDirectory { get; set; } = "SimulatedReceipts";
    }
}
