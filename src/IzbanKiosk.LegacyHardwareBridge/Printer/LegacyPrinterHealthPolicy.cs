namespace IzbanKiosk.LegacyHardwareBridge.Printer
{
    /// <summary>
    /// Pure decision policy for the legacy thermal printer health check.
    /// Kept separate from P/Invoke so every fail-open/fail-closed branch can be
    /// covered by automated tests on non-Windows build machines.
    /// </summary>
    public static class LegacyPrinterHealthPolicy
    {
        /// <summary>
        /// The deployed AUSKiosk 5.2.0.4 stops promising receipts when more than three
        /// jobs are queued on the current printer (<c>kpf.GetSpoolerJobCount() &lt;= 3</c>
        /// in <c>pageMain.Page_Loaded</c>). A backlog above that is how a thermal
        /// printer that accepts jobs but produces no paper actually shows up.
        /// </summary>
        public const int MaxHealthyQueueBacklog = 3;

        public static LegacyPrinterHealthDecision Evaluate(
            string printerName,
            bool win32PrinterOpened,
            bool win32StatusRead,
            uint win32Status,
            int win32Error,
            bool vendorProbeCompleted,
            int vendorJobCount,
            string vendorError)
        {
            if (win32PrinterOpened && win32StatusRead)
            {
                if ((win32Status & PrinterStatusOffline) != 0)
                {
                    return LegacyPrinterHealthDecision.NotReady("Printer status is OFFLINE.", true);
                }

                if ((win32Status & (PrinterStatusPaperOut | PrinterStatusPaperProblem)) != 0)
                {
                    return LegacyPrinterHealthDecision.NotReady("Printer status is PAPER OUT.", true);
                }

                if ((win32Status & PrinterStatusDoorOpen) != 0)
                {
                    return LegacyPrinterHealthDecision.NotReady("Printer cover is OPEN.", true);
                }

                if (vendorProbeCompleted && vendorJobCount > MaxHealthyQueueBacklog)
                {
                    return LegacyPrinterHealthDecision.NotReady(
                        "Printer queue backlog is " + vendorJobCount + " jobs. The queue accepts documents but the device " +
                        "is not consuming them. Clear the queue and check paper, power and cable.",
                        true);
                }

                return LegacyPrinterHealthDecision.Ready("Printer is Ready through Windows spooler.");
            }

            string details = "OutcomeUnknown: Could not verify printer '" + printerName + "'.";
            if (win32Error != 0)
            {
                details += " OpenPrinter/GetPrinter Win32 error=" + win32Error + ".";
            }
            if (!string.IsNullOrEmpty(vendorError))
            {
                details += " KioskPrint probe: " + vendorError;
            }
            else if (vendorProbeCompleted)
            {
                details += " KioskPrint probe returned invalid job count=" + vendorJobCount + ".";
            }

            return LegacyPrinterHealthDecision.NotReady(details, win32PrinterOpened);
        }

        private const uint PrinterStatusPaperOut = 0x00000010;
        private const uint PrinterStatusPaperProblem = 0x00000040;
        private const uint PrinterStatusOffline = 0x00000080;
        private const uint PrinterStatusDoorOpen = 0x00400000;
    }

    public sealed class LegacyPrinterHealthDecision
    {
        private LegacyPrinterHealthDecision(bool isReady, bool isSpoolerRunning, string statusMessage)
        {
            IsReady = isReady;
            IsSpoolerRunning = isSpoolerRunning;
            StatusMessage = statusMessage;
        }

        public bool IsReady { get; private set; }
        public bool IsSpoolerRunning { get; private set; }
        public string StatusMessage { get; private set; }

        public static LegacyPrinterHealthDecision Ready(string statusMessage)
        {
            return new LegacyPrinterHealthDecision(true, true, statusMessage);
        }

        public static LegacyPrinterHealthDecision NotReady(string statusMessage, bool isSpoolerRunning)
        {
            return new LegacyPrinterHealthDecision(false, isSpoolerRunning, statusMessage);
        }
    }
}
