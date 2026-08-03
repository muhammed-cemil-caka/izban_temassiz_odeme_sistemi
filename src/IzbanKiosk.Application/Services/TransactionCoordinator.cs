using System;
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
    public class TransactionCoordinator
    {
        private readonly ITransactionRepository _txRepository;
        private readonly IPosTerminal _posTerminal;
        private readonly INfcReader _nfcReader;
        private readonly IAuthoritativeBalanceProvider _balanceProvider;
        private readonly RecoveryService _recoveryService;
        private readonly ILogger<TransactionCoordinator> _logger;

        public TransactionCoordinator(
            ITransactionRepository txRepository,
            IPosTerminal posTerminal,
            INfcReader nfcReader,
            IAuthoritativeBalanceProvider balanceProvider,
            RecoveryService recoveryService,
            ILogger<TransactionCoordinator> logger)
        {
            _txRepository = txRepository;
            _posTerminal = posTerminal;
            _nfcReader = nfcReader;
            _balanceProvider = balanceProvider;
            _recoveryService = recoveryService;
            _logger = logger;
        }

        public async Task<KioskTransaction> ProcessTransactionAsync(
            string idempotencyKey,
            Money chargeAmount,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            // Block new transactions if recovery is running
            if (_recoveryService.IsRecovering)
            {
                throw new InvalidOperationException("System recovery is currently in progress. New transactions are temporarily disabled.");
            }

            // 1. Idempotency Check
            var existingTx = await _txRepository.GetByIdempotencyKeyAsync(idempotencyKey);
            if (existingTx != null)
            {
                _logger.LogWarning("Duplicate transaction detected for idempotency key {Key}. State: {State}", idempotencyKey, existingTx.State);
                return existingTx;
            }

            var transactionId = new TransactionId(Guid.NewGuid());
            var tx = new KioskTransaction(transactionId, idempotencyKey);
            await _txRepository.SaveAsync(tx, "Created");

            try
            {
                // 2. Wait for Card Tap
                _logger.LogInformation("Waiting for card tap...");
                var cardRef = await _nfcReader.WaitForCardAsync(tx.Id, timeout, cancellationToken);
                if (cardRef == null)
                {
                    tx.TransitionTo(KioskTransactionState.Failed, "Card tap timed out.");
                    await _txRepository.SaveAsync(tx, "CardTapTimeout");
                    return tx;
                }

                tx.TransitionTo(KioskTransactionState.CardDetected);
                tx.LoadProperties(
                    tx.State,
                    cardRef,
                    chargeAmount,
                    tx.PosVendorReference,
                    tx.LoadVendorReference,
                    tx.PosApprovalCode,
                    tx.ResponseCode,
                    tx.ErrorMessage,
                    tx.RetryCount,
                    tx.PreviousBalanceMinor,
                    tx.NewBalanceMinor
                );
                await _txRepository.SaveAsync(tx, "CardDetecteded");

                // 3. Validate Card Authenticity
                var isValid = await _nfcReader.ValidateCardAsync(tx.Id, cardRef, cancellationToken);
                if (!isValid)
                {
                    tx.TransitionTo(KioskTransactionState.Failed, "Card integrity validation failed.");
                    await _txRepository.SaveAsync(tx, "CardValidationFailed");
                    return tx;
                }

                tx.TransitionTo(KioskTransactionState.CardValidated);
                await _txRepository.SaveAsync(tx, "CardValidated");

                // 4. Fetch Authoritative Balance
                tx.TransitionTo(KioskTransactionState.BalanceQueryPending);
                await _txRepository.SaveAsync(tx, "BalanceQueryPending");

                BalanceResult balanceResult;
                try
                {
                    balanceResult = await _balanceProvider.GetAuthoritativeBalanceAsync(cardRef.Hash);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to retrieve authoritative balance for card hash {Hash}", cardRef.Hash);
                    tx.TransitionTo(KioskTransactionState.Failed, $"Failed to retrieve authoritative balance: {ex.Message}");
                    await _txRepository.SaveAsync(tx, "BalanceQueryFailedException");
                    return tx;
                }

                if (!balanceResult.IsAuthoritative || !balanceResult.IsVerified)
                {
                    tx.TransitionTo(KioskTransactionState.Failed, "Could not determine authoritative balance safely.");
                    await _txRepository.SaveAsync(tx, "BalanceQueryFailed");
                    return tx;
                }

                tx.TransitionTo(KioskTransactionState.BalanceVerified);
                tx.LoadProperties(
                    tx.State,
                    tx.CardRef,
                    tx.Amount,
                    tx.PosVendorReference,
                    tx.LoadVendorReference,
                    tx.PosApprovalCode,
                    tx.ResponseCode,
                    tx.ErrorMessage,
                    tx.RetryCount,
                    balanceResult.BalanceMinor, // store current card balance before loading
                    balanceResult.BalanceMinor
                );
                await _txRepository.SaveAsync(tx, "BalanceVerified");

                // 5. Select Charging Amount and Call POS
                tx.TransitionTo(KioskTransactionState.AmountSelected);
                await _txRepository.SaveAsync(tx, "AmountSelected");

                Guid correlationId = Guid.NewGuid();
                tx.TransitionTo(KioskTransactionState.PaymentPending);
                await _txRepository.SaveAsync(tx, "PaymentPending");

                var posResult = await _posTerminal.StartSaleAsync(
                    tx.Id,
                    tx.IdempotencyKey,
                    chargeAmount,
                    TimeSpan.FromSeconds(30),
                    cancellationToken,
                    correlationId
                );

                if (!posResult.Success)
                {
                    tx.RegisterPaymentDetails(posResult.VendorReference, null, posResult.ResponseCode);
                    tx.TransitionTo(KioskTransactionState.PaymentDeclined, posResult.ErrorMessage);
                    tx.TransitionTo(KioskTransactionState.Failed, "Payment declined at terminal.");
                    await _txRepository.SaveAsync(tx, "PaymentFailed");
                    return tx;
                }

                // Payment succeeded
                tx.RegisterPaymentDetails(posResult.VendorReference, posResult.ApprovalCode, posResult.ResponseCode);
                tx.TransitionTo(KioskTransactionState.PaymentApproved);
                await _txRepository.SaveAsync(tx, "PaymentApproved");

                // 6. Write Load to Card
                tx.TransitionTo(KioskTransactionState.LoadPending);
                await _txRepository.SaveAsync(tx, "LoadPending");

                bool loadSuccess;
                try
                {
                    loadSuccess = await _nfcReader.LoadAmountAsync(
                        tx.Id,
                        tx.IdempotencyKey,
                        tx.CardRef!,
                        chargeAmount,
                        cancellationToken,
                        correlationId
                    );
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "NFC load write threw exception. Transitioning to LoadOutcomeUnknown for recovery.");
                    tx.TransitionTo(KioskTransactionState.LoadOutcomeUnknown, ex.Message);
                    await _txRepository.SaveAsync(tx, "LoadOutcomeException");
                    
                    // Trigger async recovery immediately
                    _ = Task.Run(() => _recoveryService.ProcessRecoveryAsync(CancellationToken.None), CancellationToken.None);
                    throw new SystemException("Load write outcome unknown. Kiosk is verifying the balance. Do not remove card.", ex);
                }

                if (!loadSuccess)
                {
                    tx.TransitionTo(KioskTransactionState.LoadVerificationFailed, "Card load writing declined.");
                    await _txRepository.SaveAsync(tx, "LoadFailed");

                    // Trigger reversal
                    await CompensateFailedLoadAsync(tx, cancellationToken);
                    return tx;
                }

                // 7. Verify Load
                long newBalance = tx.PreviousBalanceMinor + chargeAmount.AmountMinor;
                tx.RegisterLoadDetails("NFC-LOAD-OK", newBalance);
                tx.TransitionTo(KioskTransactionState.LoadVerificationPending);
                await _txRepository.SaveAsync(tx, "LoadVerificationPending");

                var verifiedBalance = await _nfcReader.ReadVerifiedBalanceAsync(tx.Id, tx.CardRef!, cancellationToken);
                if (verifiedBalance == newBalance)
                {
                    tx.TransitionTo(KioskTransactionState.LoadVerified);
                    tx.TransitionTo(KioskTransactionState.Completed);
                    await _txRepository.SaveAsync(tx, "Completed");
                }
                else
                {
                    tx.TransitionTo(KioskTransactionState.LoadVerificationFailed, $"Card read-back value {verifiedBalance} does not match expected {newBalance}");
                    await _txRepository.SaveAsync(tx, "VerificationMismatch");

                    await CompensateFailedLoadAsync(tx, cancellationToken);
                }
            }
            catch (Exception ex) when (tx.State != KioskTransactionState.Completed && tx.State != KioskTransactionState.Failed && tx.State != KioskTransactionState.ManualReview)
            {
                _logger.LogError(ex, "Exception in transaction coordinator Saga. Triggering recovery...");
                tx.MarkManualReview($"Saga exception: {ex.Message}");
                await _txRepository.SaveAsync(tx, "SagaException");
            }

            return tx;
        }

        private async Task CompensateFailedLoadAsync(KioskTransaction tx, CancellationToken cancellationToken)
        {
            tx.TransitionTo(KioskTransactionState.ReversalPending, "Refunding payment due to load failure.");
            await _txRepository.SaveAsync(tx, "ReversalPending");

            int retries = 0;
            while (retries < 3)
            {
                try
                {
                    tx.IncrementRetry();
                    var reversalResult = await _posTerminal.ReversalAsync(
                        tx.Id,
                        tx.PosVendorReference!,
                        tx.Amount!,
                        cancellationToken,
                        Guid.NewGuid()
                    );

                    if (reversalResult.Success)
                    {
                        tx.TransitionTo(KioskTransactionState.Reversed);
                        tx.TransitionTo(KioskTransactionState.Failed, "Transaction load failed, payment success reversed.");
                        await _txRepository.SaveAsync(tx, "ReversalSucceeded");
                        return;
                    }

                    _logger.LogWarning("Reversal attempt {Retry} failed for tx {TxId}.", tx.RetryCount, tx.Id);
                    tx.TransitionTo(KioskTransactionState.ReversalFailed, reversalResult.ErrorMessage);
                    await _txRepository.SaveAsync(tx, "ReversalFailedAttempt");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Reversal attempt throw error.");
                }

                retries++;
                if (retries < 3)
                {
                    int delay = (int)Math.Pow(2, retries);
                    await Task.Delay(delay * 1000, cancellationToken);
                }
            }

            tx.MarkManualReview("POS Reversal failed after 3 attempts.");
            await _txRepository.SaveAsync(tx, "ReversalExhausted");
        }
    }
}
