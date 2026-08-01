using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IzbanKiosk.Domain;
using IzbanKiosk.Application.Repositories;
using IzbanKiosk.Application.Hardware.Pos;
using IzbanKiosk.Application.Hardware.Nfc;
using IzbanKiosk.Application.Hardware.Balance;
using Microsoft.Extensions.Logging;

namespace IzbanKiosk.Application.Services
{
    public class RecoveryService
    {
        private readonly ITransactionRepository _txRepository;
        private readonly IPosTerminal _posTerminal;
        private readonly INfcReader _nfcReader;
        private readonly IAuthoritativeBalanceProvider _balanceProvider;
        private readonly ILogger<RecoveryService> _logger;

        public bool IsRecovering { get; private set; }

        public RecoveryService(
            ITransactionRepository txRepository,
            IPosTerminal posTerminal,
            INfcReader nfcReader,
            IAuthoritativeBalanceProvider balanceProvider,
            ILogger<RecoveryService> logger)
        {
            _txRepository = txRepository;
            _posTerminal = posTerminal;
            _nfcReader = nfcReader;
            _balanceProvider = balanceProvider;
            _logger = logger;
        }

        public async Task<bool> ProcessRecoveryAsync(CancellationToken cancellationToken)
        {
            if (IsRecovering) return false;
            IsRecovering = true;

            try
            {
                _logger.LogInformation("Recovery process started.");
                List<KioskTransaction> pendingTransactions = await _txRepository.GetPendingTransactionsAsync();

                if (pendingTransactions.Count == 0)
                {
                    _logger.LogInformation("No pending transactions found for recovery.");
                    return true;
                }

                _logger.LogWarning("Found {Count} pending transactions to recover.", pendingTransactions.Count);

                foreach (var tx in pendingTransactions)
                {
                    try
                    {
                        await RecoverTransactionAsync(tx, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to recover transaction {TxId}.", tx.Id);
                        tx.MarkManualReview($"Recovery execution failed: {ex.Message}");
                        await _txRepository.SaveAsync(tx, "RecoveryError");
                    }
                }

                return true;
            }
            finally
            {
                IsRecovering = false;
                _logger.LogInformation("Recovery process completed.");
            }
        }

        private async Task RecoverTransactionAsync(KioskTransaction tx, CancellationToken cancellationToken)
        {
            _logger.LogInformation("Recovering transaction {TxId} in state {State}.", tx.Id, tx.State);

            switch (tx.State)
            {
                case KioskTransactionState.PaymentPending:
                case KioskTransactionState.PaymentOutcomeUnknown:
                    await RecoverPaymentStateAsync(tx, cancellationToken);
                    break;

                case KioskTransactionState.PaymentApproved:
                case KioskTransactionState.PreAuthorized:
                    // POS is approved, but load has not started. Trigger POS void/reversal.
                    await TriggerReversalAsync(tx, cancellationToken);
                    break;

                case KioskTransactionState.LoadPending:
                case KioskTransactionState.LoadOutcomeUnknown:
                    await RecoverLoadStateAsync(tx, cancellationToken);
                    break;

                case KioskTransactionState.LoadVerificationPending:
                    await VerifyBakiyeAsync(tx, cancellationToken);
                    break;

                case KioskTransactionState.ReversalPending:
                    await ExecuteReversalWaitAsync(tx, cancellationToken);
                    break;

                case KioskTransactionState.ReversalFailed:
                    await HandleReversalFailedAsync(tx, cancellationToken);
                    break;

                default:
                    // For states like CardDetected, CardValidated, BalanceQueryPending, BalanceVerified, AmountSelected:
                    // Since no financial transaction started yet, we can safely transition to Failed.
                    tx.TransitionTo(KioskTransactionState.Failed, "Abandoned during recovery.");
                    await _txRepository.SaveAsync(tx, "RecoveryAbandoned");
                    break;
            }
        }

        private async Task RecoverPaymentStateAsync(KioskTransaction tx, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(tx.PosVendorReference))
            {
                // Can't query without reference. Must fail-closed / ManualReview
                tx.MarkManualReview("Missing POS reference for pending transaction query.");
                await _txRepository.SaveAsync(tx, "RecoveryMissingRef");
                return;
            }

            _logger.LogInformation("Querying POS for tx {TxId} with ref {Ref}.", tx.Id, tx.PosVendorReference);
            var queryResult = await _posTerminal.QueryTransactionAsync(tx.Id, tx.PosVendorReference, cancellationToken, Guid.NewGuid());

            if (queryResult.Success)
            {
                _logger.LogInformation("POS query returned SUCCESS for {TxId}.", tx.Id);
                tx.RegisterPaymentDetails(tx.PosVendorReference, queryResult.ApprovalCode, queryResult.ResponseCode);
                tx.TransitionTo(KioskTransactionState.PaymentApproved);
                await _txRepository.SaveAsync(tx, "RecoveryPaymentApproved");

                // Immediately trigger card load since payment was approved
                await RecoverTransactionAsync(tx, cancellationToken);
            }
            else if (queryResult.ErrorCode == "DECLINED" || queryResult.ResponseCode == "51")
            {
                _logger.LogInformation("POS query returned DECLINED for {TxId}.", tx.Id);
                tx.TransitionTo(KioskTransactionState.PaymentDeclined, queryResult.ErrorMessage);
                tx.TransitionTo(KioskTransactionState.Failed, "Payment was declined.");
                await _txRepository.SaveAsync(tx, "RecoveryPaymentDeclined");
            }
            else if (queryResult.ErrorCode == "TIMEOUT" || queryResult.ErrorCode == "COMM_ERROR")
            {
                // Can't resolve definitely. Transition to ManualReview
                tx.MarkManualReview("POS query failed unresolved: " + queryResult.ErrorMessage);
                await _txRepository.SaveAsync(tx, "RecoveryQueryUnresolved");
            }
            else
            {
                // Unhandled error code
                tx.TransitionTo(KioskTransactionState.Failed, "Payment cancelled or failed.");
                await _txRepository.SaveAsync(tx, "RecoveryPaymentFailed");
            }
        }

        private async Task RecoverLoadStateAsync(KioskTransaction tx, CancellationToken cancellationToken)
        {
            if (tx.CardRef == null)
            {
                tx.MarkManualReview("Missing CardReference on Load recovery.");
                await _txRepository.SaveAsync(tx, "RecoveryError");
                return;
            }

            _logger.LogInformation("Recovering NFC load state. Reading verified bakiye first for {CardRef}.", tx.CardRef.Masked);

            // Read the card snapshot to verify balance
            long cardBalance = 0;
            try
            {
                cardBalance = await _nfcReader.ReadVerifiedBalanceAsync(tx.Id, tx.CardRef, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not read verified bakiye during NFC load recovery.");
                // If we cannot verify, move to ManualReview rather than reversing blindly
                tx.MarkManualReview("NFC load recovery balance read failed: " + ex.Message);
                await _txRepository.SaveAsync(tx, "RecoveryReadBalanceFailed");
                return;
            }

            long expectedBalance = tx.PreviousBalanceMinor + (tx.Amount?.AmountMinor ?? 0);

            if (cardBalance >= expectedBalance)
            {
                _logger.LogInformation("NFC Load detected SUCCESS via bakiye check.");
                tx.RegisterLoadDetails(tx.LoadVendorReference, cardBalance);
                tx.TransitionTo(KioskTransactionState.LoadVerificationPending);
                await _txRepository.SaveAsync(tx, "RecoveryLoadVerified");
                
                await VerifyBakiyeAsync(tx, cancellationToken);
            }
            else
            {
                // If it definitely did not reach, did a load transaction fail? We query POS reversal.
                _logger.LogWarning("NFC Load failed bakiye check. Current: {Cur}, Expected: {Exp}.", cardBalance, expectedBalance);
                tx.TransitionTo(KioskTransactionState.LoadVerificationFailed, "Card balance does not match loaded expected total.");
                await _txRepository.SaveAsync(tx, "RecoveryLoadFailed");

                await TriggerReversalAsync(tx, cancellationToken);
            }
        }

        private async Task VerifyBakiyeAsync(KioskTransaction tx, CancellationToken cancellationToken)
        {
            if (tx.CardRef == null)
            {
                tx.MarkManualReview("Missing CardReference on verification recovery.");
                await _txRepository.SaveAsync(tx, "RecoveryError");
                return;
            }

            // Perform authoritative balance query
            var balanceResult = await _balanceProvider.GetAuthoritativeBalanceAsync(tx.CardRef.Hash);
            long expectedBalance = tx.PreviousBalanceMinor + (tx.Amount?.AmountMinor ?? 0);

            if (balanceResult.IsAuthoritative && balanceResult.IsVerified && !balanceResult.IsStale)
            {
                if (balanceResult.BalanceMinor == expectedBalance || balanceResult.BalanceMinor >= expectedBalance)
                {
                    tx.TransitionTo(KioskTransactionState.LoadVerified);
                    tx.TransitionTo(KioskTransactionState.Completed);
                    await _txRepository.SaveAsync(tx, "RecoveryCompleted");
                }
                else
                {
                    tx.TransitionTo(KioskTransactionState.LoadVerificationFailed, "Authoritative balance mismatch.");
                    await _txRepository.SaveAsync(tx, "RecoveryVerificationFailed");
                    await TriggerReversalAsync(tx, cancellationToken);
                }
            }
            else
            {
                tx.MarkManualReview("Authoritative balance provider is offline during verification recovery.");
                await _txRepository.SaveAsync(tx, "RecoveryVerificationUnresolved");
            }
        }

        private async Task TriggerReversalAsync(KioskTransaction tx, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(tx.PosVendorReference))
            {
                tx.MarkManualReview("Cannot trigger reversal: missing POS vendor reference.");
                await _txRepository.SaveAsync(tx, "RecoveryError");
                return;
            }

            tx.TransitionTo(KioskTransactionState.ReversalPending, "Reversing transaction.");
            await _txRepository.SaveAsync(tx, "RecoveryReversalPending");

            await ExecuteReversalWaitAsync(tx, cancellationToken);
        }

        private async Task ExecuteReversalWaitAsync(KioskTransaction tx, CancellationToken cancellationToken)
        {
            tx.IncrementRetry();
            
            _logger.LogInformation("Sending reversal command to POS for tx {TxId}.", tx.Id);
            var reversalResult = await _posTerminal.ReversalAsync(tx.Id, tx.PosVendorReference!, tx.Amount ?? new Money(0), cancellationToken, Guid.NewGuid());

            if (reversalResult.Success)
            {
                tx.TransitionTo(KioskTransactionState.Reversed);
                tx.TransitionTo(KioskTransactionState.Failed, "Transaction reversed successfully.");
                await _txRepository.SaveAsync(tx, "RecoveryReversalCompleted");
            }
            else
            {
                tx.TransitionTo(KioskTransactionState.ReversalFailed, reversalResult.ErrorMessage);
                await _txRepository.SaveAsync(tx, "RecoveryReversalFailed");
                
                await HandleReversalFailedAsync(tx, cancellationToken);
            }
        }

        private async Task HandleReversalFailedAsync(KioskTransaction tx, CancellationToken cancellationToken)
        {
            if (tx.RetryCount < 3)
            {
                // Retry with backoff delay
                int delaySec = (int)Math.Pow(2, tx.RetryCount);
                _logger.LogWarning("Reversal failed for tx {TxId}. Retrying in {Sec} seconds...", tx.Id, delaySec);
                await Task.Delay(delaySec * 1000, cancellationToken);
                await ExecuteReversalWaitAsync(tx, cancellationToken);
            }
            else
            {
                _logger.LogError("Reversal retries exhausted. Marking tx {TxId} as ManualReview.", tx.Id);
                tx.MarkManualReview("POS Reversal failed after maximum retry attempts. Operator intervention required.");
                await _txRepository.SaveAsync(tx, "RecoveryReversalExhausted");
            }
        }
    }
}
