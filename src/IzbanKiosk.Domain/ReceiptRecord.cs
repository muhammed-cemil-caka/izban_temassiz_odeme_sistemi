using System;

namespace IzbanKiosk.Domain
{
    public class ReceiptRecord
    {
        public string ReceiptId { get; private set; }
        public string TransactionId { get; private set; }
        public string Decision { get; private set; }
        public ReceiptStatus Status { get; private set; }
        public DateTime? RequestedAtUtc { get; private set; }
        public DateTime? PrintStartedAtUtc { get; private set; }
        public DateTime? PrintedAtUtc { get; private set; }
        public string PrinterJobReference { get; private set; }
        public string ErrorCode { get; private set; }
        public string ErrorMessage { get; private set; }
        public int RetryCount { get; private set; }
        public int RowVersion { get; private set; }
        public DateTime CreatedAtUtc { get; private set; }
        public DateTime LastModifiedAtUtc { get; private set; }

        public ReceiptRecord(string transactionId)
        {
            ReceiptId = Guid.NewGuid().ToString();
            TransactionId = transactionId;
            Decision = "Offered";
            Status = ReceiptStatus.Offered;
            RowVersion = 1;
            CreatedAtUtc = DateTime.UtcNow;
            LastModifiedAtUtc = DateTime.UtcNow;
        }

        // Constructor for database loading
        public ReceiptRecord(
            string receiptId,
            string transactionId,
            string decision,
            ReceiptStatus status,
            DateTime? requestedAtUtc,
            DateTime? printStartedAtUtc,
            DateTime? printedAtUtc,
            string printerJobReference,
            string errorCode,
            string errorMessage,
            int retryCount,
            int rowVersion,
            DateTime createdAtUtc,
            DateTime lastModifiedAtUtc)
        {
            ReceiptId = receiptId;
            TransactionId = transactionId;
            Decision = decision;
            Status = status;
            RequestedAtUtc = requestedAtUtc;
            PrintStartedAtUtc = printStartedAtUtc;
            PrintedAtUtc = printedAtUtc;
            PrinterJobReference = printerJobReference;
            ErrorCode = errorCode;
            ErrorMessage = errorMessage;
            RetryCount = retryCount;
            RowVersion = rowVersion;
            CreatedAtUtc = createdAtUtc;
            LastModifiedAtUtc = lastModifiedAtUtc;
        }

        public void TransitionTo(ReceiptStatus newStatus, string errorCode = null, string errorMessage = null, string jobRef = null)
        {
            if (!IsValidTransition(Status, newStatus))
            {
                throw new InvalidOperationException($"Invalid receipt status transition from {Status} to {newStatus}");
            }

            Status = newStatus;
            ErrorCode = errorCode;
            ErrorMessage = errorMessage;
            if (jobRef != null) PrinterJobReference = jobRef;

            if (newStatus == ReceiptStatus.Declined || newStatus == ReceiptStatus.TimedOut || newStatus == ReceiptStatus.Requested)
            {
                Decision = newStatus.ToString();
                RequestedAtUtc = DateTime.UtcNow;
            }
            else if (newStatus == ReceiptStatus.Printing)
            {
                PrintStartedAtUtc = DateTime.UtcNow;
            }
            else if (newStatus == ReceiptStatus.Printed)
            {
                PrintedAtUtc = DateTime.UtcNow;
            }

            LastModifiedAtUtc = DateTime.UtcNow;
        }

        public void IncrementRetry()
        {
            RetryCount++;
            LastModifiedAtUtc = DateTime.UtcNow;
        }

        public void IncrementRowVersion()
        {
            RowVersion++;
            LastModifiedAtUtc = DateTime.UtcNow;
        }

        public static bool IsValidTransition(ReceiptStatus current, ReceiptStatus next)
        {
            if (current == next) return true;

            return current switch
            {
                ReceiptStatus.Offered => next == ReceiptStatus.Declined ||
                                         next == ReceiptStatus.TimedOut ||
                                         next == ReceiptStatus.Requested,
                ReceiptStatus.Requested => next == ReceiptStatus.Printing || next == ReceiptStatus.Failed,
                ReceiptStatus.Printing => next == ReceiptStatus.Printed ||
                                         next == ReceiptStatus.PrintOutcomeUnknown ||
                                         next == ReceiptStatus.PaperOut ||
                                         next == ReceiptStatus.Failed,
                // Terminal states cannot transition further
                ReceiptStatus.Declined => false,
                ReceiptStatus.TimedOut => false,
                ReceiptStatus.Printed => false,
                ReceiptStatus.PrintOutcomeUnknown => false,
                ReceiptStatus.PaperOut => false,
                ReceiptStatus.Failed => false,
                _ => false
            };
        }
    }
}
