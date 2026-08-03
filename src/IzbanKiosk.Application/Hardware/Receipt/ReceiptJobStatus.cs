namespace IzbanKiosk.Application.Hardware.Receipt
{
    public class ReceiptJobStatus
    {
        public bool IsFinished { get; }
        public bool Success { get; }
        public ReceiptPrintOutcome Outcome { get; }
        public string ErrorMessage { get; }

        public ReceiptJobStatus(bool isFinished, bool success, ReceiptPrintOutcome outcome, string errorMessage = null)
        {
            IsFinished = isFinished;
            Success = success;
            Outcome = outcome;
            ErrorMessage = errorMessage;
        }

        public static ReceiptJobStatus FinishedSuccess => new ReceiptJobStatus(true, true, ReceiptPrintOutcome.Success);
        public static ReceiptJobStatus Pending => new ReceiptJobStatus(false, false, ReceiptPrintOutcome.Busy);
    }
}
