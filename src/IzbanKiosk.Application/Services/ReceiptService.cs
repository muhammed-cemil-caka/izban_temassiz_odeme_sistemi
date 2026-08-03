using System;
using System.Threading;
using System.Threading.Tasks;
using IzbanKiosk.Domain;
using IzbanKiosk.Application.Repositories;
using IzbanKiosk.Application.Hardware.Receipt;

namespace IzbanKiosk.Application.Services
{
    public class ReceiptService
    {
        private readonly ITransactionRepository _transactionRepository;
        private readonly IReceiptRepository _receiptRepository;
        private readonly IReceiptPrinter _receiptPrinter;
        private readonly ReceiptDocumentFactory _documentFactory;
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);

        public ReceiptService(
            ITransactionRepository transactionRepository,
            IReceiptRepository receiptRepository,
            IReceiptPrinter receiptPrinter,
            ReceiptDocumentFactory documentFactory)
        {
            _transactionRepository = transactionRepository;
            _receiptRepository = receiptRepository;
            _receiptPrinter = receiptPrinter;
            _documentFactory = documentFactory;
        }

        public async Task<ReceiptRecord> RecordDecisionAsync(string transactionId, string decision, CancellationToken cancellationToken)
        {
            if (!Enum.TryParse<ReceiptStatus>(decision, out var status))
            {
                throw new ArgumentException("Invalid decision status", nameof(decision));
            }

            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                var existing = await _receiptRepository.GetByTransactionIdAsync(transactionId);
                if (existing != null)
                {
                    return existing;
                }

                var tx = await _transactionRepository.GetByIdAsync(new TransactionId(Guid.Parse(transactionId)));
                if (tx == null)
                {
                    throw new InvalidOperationException("Transaction not found.");
                }
                if (tx.State != KioskTransactionState.Completed)
                {
                    throw new InvalidOperationException("Cannot record receipt decision for non-completed transaction.");
                }

                var record = new ReceiptRecord(transactionId);
                record.TransitionTo(status);
                await _receiptRepository.SaveAsync(record);
                return record;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task<ReceiptPrintResult> PrintReceiptAsync(string transactionId, string stationName, string kioskId, CancellationToken cancellationToken)
        {
            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                // 1. Verify transaction exists and is Completed
                var tx = await _transactionRepository.GetByIdAsync(new TransactionId(Guid.Parse(transactionId)));
                if (tx == null)
                {
                    return ReceiptPrintResult.StatusFailed(ReceiptPrintOutcome.Failed, "TX_NOT_FOUND", "Transaction not found.");
                }
                if (tx.State != KioskTransactionState.Completed)
                {
                    return ReceiptPrintResult.StatusFailed(ReceiptPrintOutcome.Failed, "TX_NOT_COMPLETED", "Cannot print receipt for non-completed transaction.");
                }

                // 2. Fetch or create ReceiptRecord
                var record = await _receiptRepository.GetByTransactionIdAsync(transactionId);
                if (record == null)
                {
                    record = new ReceiptRecord(transactionId);
                    await _receiptRepository.SaveAsync(record);
                }

                // Double print prevention
                if (record.Status == ReceiptStatus.Printed)
                {
                    return ReceiptPrintResult.Successful(record.PrinterJobReference);
                }
                if (record.Status == ReceiptStatus.Printing)
                {
                    return ReceiptPrintResult.StatusFailed(ReceiptPrintOutcome.Busy, "BUSY", "Receipt is already printing.");
                }
                if (record.Status == ReceiptStatus.Declined || record.Status == ReceiptStatus.TimedOut)
                {
                    return ReceiptPrintResult.StatusFailed(ReceiptPrintOutcome.Failed, "PREVIOUS_DECISION_NO", "User declined receipt option previously.");
                }

                // 3. Transition to Requested (if Offered)
                if (record.Status == ReceiptStatus.Offered)
                {
                    record.TransitionTo(ReceiptStatus.Requested);
                    await _receiptRepository.SaveAsync(record);
                }

                // 4. Transition to Printing
                record.TransitionTo(ReceiptStatus.Printing);
                await _receiptRepository.SaveAsync(record);

                // 5. Build Receipt Document
                var doc = _documentFactory.CreateReceipt(tx, stationName, kioskId);

                // 6. Print via Hardware
                string idempotencyKey = $"receipt:{transactionId}";
                var printResult = await _receiptPrinter.PrintReceiptAsync(doc, idempotencyKey, cancellationToken);

                // 7. Update Record status based on outcome
                if (printResult.Success)
                {
                    record.TransitionTo(ReceiptStatus.Printed, jobRef: printResult.PrinterJobReference);
                }
                else
                {
                    record.IncrementRetry();
                    switch (printResult.Outcome)
                    {
                        case ReceiptPrintOutcome.PaperOut:
                            record.TransitionTo(ReceiptStatus.PaperOut, printResult.ErrorCode, printResult.ErrorMessage);
                            break;
                        case ReceiptPrintOutcome.OutcomeUnknown:
                            record.TransitionTo(ReceiptStatus.PrintOutcomeUnknown, printResult.ErrorCode, printResult.ErrorMessage);
                            break;
                        default:
                            record.TransitionTo(ReceiptStatus.Failed, printResult.ErrorCode, printResult.ErrorMessage);
                            break;
                    }
                }

                await _receiptRepository.SaveAsync(record);
                return printResult;
            }
            catch (Exception ex)
            {
                return ReceiptPrintResult.StatusFailed(ReceiptPrintOutcome.Failed, "EXCEPTION", ex.Message);
            }
            finally
            {
                _semaphore.Release();
            }
        }
    }
}
