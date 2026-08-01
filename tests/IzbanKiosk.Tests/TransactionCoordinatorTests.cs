using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using IzbanKiosk.Domain;
using IzbanKiosk.Application.Services;
using IzbanKiosk.Application.Repositories;
using IzbanKiosk.Application.Hardware.Pos;
using IzbanKiosk.Application.Hardware.Nfc;
using IzbanKiosk.Application.Hardware.Balance;

namespace IzbanKiosk.Tests
{
    public class TransactionCoordinatorTests
    {
        private readonly InMemoryTransactionRepository _txRepository;
        private readonly FakePosTerminal _posTerminal;
        private readonly FakeNfcReader _nfcReader;
        private readonly FakeBalanceProvider _balanceProvider;
        private readonly TransactionCoordinator _sut; // System Under Test

        public TransactionCoordinatorTests()
        {
            _txRepository = new InMemoryTransactionRepository();
            _posTerminal = new FakePosTerminal();
            _nfcReader = new FakeNfcReader();
            _balanceProvider = new FakeBalanceProvider();
            
            var recoveryLogger = NullLogger<RecoveryService>.Instance;
            var recoveryService = new RecoveryService(
                _txRepository,
                _posTerminal,
                _nfcReader,
                _balanceProvider,
                recoveryLogger
            );

            _sut = new TransactionCoordinator(
                _txRepository,
                _posTerminal,
                _nfcReader,
                _balanceProvider,
                recoveryService,
                NullLogger<TransactionCoordinator>.Instance
            );
        }

        // ==================== TRANSACTION COORDINATOR SAGA TESTS ====================

        [Fact]
        public async Task ProcessTransaction_WhenSucceeds_ShouldCompleteSuccessfully()
        {
            // Arrange
            string idempotencyKey = "happy-path-key";
            Money amount = new Money(5000); // 50 TL
            _nfcReader.SetSimulatedCard("35-IZM-9921", 1000); // balance 10.00 TL

            // Act
            var result = await _sut.ProcessTransactionAsync(idempotencyKey, amount, TimeSpan.FromSeconds(5), CancellationToken.None);

            // Assert
            Assert.Equal(KioskTransactionState.Completed, result.State);
            Assert.Null(result.ErrorMessage);

            // Verify db transaction logged correctly
            var savedTx = await _txRepository.GetByIdAsync(result.Id);
            Assert.NotNull(savedTx);
            Assert.Equal(KioskTransactionState.Completed, savedTx.State);
            Assert.Equal(amount.AmountMinor, savedTx.Amount!.AmountMinor);
            Assert.Equal(savedTx.PosApprovalCode, _posTerminal.ApprovalCode);
        }

        [Fact]
        public async Task ProcessTransaction_WhenCardValidationFails_ShouldMarkFailedAndFailSaga()
        {
            // Arrange
            string idempotencyKey = "card-fail-key";
            Money amount = new Money(2000);
            _nfcReader.NextValidateResult = false; // card is blacklisted or SAM check fails

            // Act
            var result = await _sut.ProcessTransactionAsync(idempotencyKey, amount, TimeSpan.FromSeconds(5), CancellationToken.None);

            // Assert
            Assert.Equal(KioskTransactionState.Failed, result.State);
            Assert.Contains("Card integrity validation failed", result.ErrorMessage);

            var savedTx = await _txRepository.GetByIdAsync(result.Id);
            Assert.Equal(KioskTransactionState.Failed, savedTx!.State);
        }

        [Fact]
        public async Task ProcessTransaction_WhenNfcReaderTimesOutWaitingForCard_ShouldFailSafeClosed()
        {
            // Arrange
            string idempotencyKey = "card-timeout-key";
            Money amount = new Money(3000);
            _nfcReader.NextWaitCardResult = "Timeout";

            // Act
            var result = await _sut.ProcessTransactionAsync(idempotencyKey, amount, TimeSpan.FromSeconds(1), CancellationToken.None);

            // Assert
            Assert.Equal(KioskTransactionState.Failed, result.State);
            Assert.Contains("Card tap timed out", result.ErrorMessage);
        }

        [Fact]
        public async Task ProcessTransaction_WhenAuthoritativeBalanceThrowsException_ShouldFailSagaBeforeCharging()
        {
            // Arrange
            string idempotencyKey = "balance-fail-key";
            Money amount = new Money(4000);
            _balanceProvider.ThrowExceptionOnGet = true;

            // Act
            var result = await _sut.ProcessTransactionAsync(idempotencyKey, amount, TimeSpan.FromSeconds(5), CancellationToken.None);

            // Assert
            Assert.Equal(KioskTransactionState.Failed, result.State);
            Assert.Contains("Failed to retrieve authoritative balance", result.ErrorMessage);

            // Verify no POS payment was attempted
            Assert.False(_posTerminal.WasChargeAttempted);
        }

        [Fact]
        public async Task ProcessTransaction_WhenPosTransactionIsDeclined_ShouldFailSafeAndNotWriteBalance()
        {
            // Arrange
            string idempotencyKey = "pos-declined-key";
            Money amount = new Money(5000);
            _posTerminal.NextProcessResult = new PosTransactionResult(false, null, "DECLINED", "Card declined by issuer");

            // Act
            var result = await _sut.ProcessTransactionAsync(idempotencyKey, amount, TimeSpan.FromSeconds(5), CancellationToken.None);

            // Assert
            Assert.Equal(KioskTransactionState.Failed, result.State);
            Assert.Contains("Payment declined at terminal", result.ErrorMessage);

            // Verify NFC load was not attempted
            Assert.False(_nfcReader.WasLoadAttempted);
        }

        [Fact]
        public async Task ProcessTransaction_WhenPosConnectionTimesOut_ShouldMarkPaymentOutcomeUnknownAndVoidPayment()
        {
            // Arrange
            string idempotencyKey = "pos-timeout-key";
            Money amount = new Money(4000);
            _posTerminal.ThrowTimeoutOnProcess = true;

            // Act
            var result = await _sut.ProcessTransactionAsync(idempotencyKey, amount, TimeSpan.FromSeconds(5), CancellationToken.None);

            // Assert
            // It will catch TimeoutException and mark the transaction as ManualReview in catch block
            Assert.Equal(KioskTransactionState.ManualReview, result.State);
        }

        [Fact]
        public async Task ProcessTransaction_WhenUserPullsCardEarlyDuringPayment_ShouldCancelFlowSafely()
        {
            // Arrange
            string idempotencyKey = "early-pull-key";
            Money amount = new Money(5000);
            _nfcReader.SetSimulatedCard("35-IZM-9921", 1500);

            var cts = new CancellationTokenSource();
            
            // We set POS charge process to cancel the CTS midway (simulating card removed early)
            _posTerminal.OnProcessAction = () =>
            {
                _nfcReader.NextWaitCardResult = "None"; // card removed
                cts.Cancel();
            };

            // Act
            var result = await _sut.ProcessTransactionAsync(idempotencyKey, amount, TimeSpan.FromSeconds(5), cts.Token);

            // Assert
            Assert.True(result.State == KioskTransactionState.Failed || result.State == KioskTransactionState.ManualReview);
        }

        [Fact]
        public async Task ProcessTransaction_WhenNfcLoadFailsAndReversalSucceeds_ShouldPerformSecureRollback()
        {
            // Arrange
            string idempotencyKey = "load-fail-reversal-ok-key";
            Money amount = new Money(3500);
            _nfcReader.NextLoadResult = "Failure"; // card write failed

            // Act
            var result = await _sut.ProcessTransactionAsync(idempotencyKey, amount, TimeSpan.FromSeconds(5), CancellationToken.None);

            // Assert
            Assert.Equal(KioskTransactionState.Failed, result.State);
            // Verify payment was cancelled/refunded (voided) on POS
            Assert.True(_posTerminal.WasReversalAttempted);
        }

        [Fact]
        public async Task ProcessTransaction_WhenNfcLoadTimesOutAndReversalFails_ShouldMarkManualReviewAndLock()
        {
            // Arrange
            string idempotencyKey = "load-timeout-reversal-fail-key";
            Money amount = new Money(6000);
            _nfcReader.NextLoadResult = "Timeout";  // NFC write timeout
            _posTerminal.NextVoidResult = new PosTransactionResult(false, null, "FAIL", "POS Host unreachable");

            // Act
            // Since it throws a SystemException inside LoadAmountAsync catch block, the outermost catch block will transition the transaction to ManualReview:
            var result = await _sut.ProcessTransactionAsync(idempotencyKey, amount, TimeSpan.FromSeconds(5), CancellationToken.None);

            // Assert
            Assert.Equal(KioskTransactionState.ManualReview, result.State);
        }

        [Fact]
        public async Task ProcessTransaction_WhenDuplicateRequest_ShouldEnforceIdempotencyAndReturnExisting()
        {
            // Arrange
            string idempotencyKey = "idempotent-key";
            Money amount = new Money(5000);
            _nfcReader.SetSimulatedCard("35-IZM-9921", 1000);

            // First run
            var result1 = await _sut.ProcessTransactionAsync(idempotencyKey, amount, TimeSpan.FromSeconds(5), CancellationToken.None);
            Assert.Equal(KioskTransactionState.Completed, result1.State);

            // Act - Second run with same idempotency key
            var result2 = await _sut.ProcessTransactionAsync(idempotencyKey, amount, TimeSpan.FromSeconds(5), CancellationToken.None);

            // Assert
            Assert.Equal(result1.Id, result2.Id);
            Assert.Equal(KioskTransactionState.Completed, result2.State);
        }

        // ==================== TEST HARDWARE MOCKS ====================

        private class InMemoryTransactionRepository : ITransactionRepository
        {
            private readonly Dictionary<TransactionId, KioskTransaction> _db = new();

            public Task<KioskTransaction?> GetByIdAsync(TransactionId id)
            {
                if (_db.TryGetValue(id, out var tx))
                {
                    return Task.FromResult<KioskTransaction?>(tx);
                }
                return Task.FromResult<KioskTransaction?>(null);
            }

            public Task<KioskTransaction?> GetByIdempotencyKeyAsync(string key)
            {
                var tx = _db.Values.FirstOrDefault(t => t.IdempotencyKey == key);
                return Task.FromResult<KioskTransaction?>(tx);
            }

            public Task<List<KioskTransaction>> GetPendingTransactionsAsync()
            {
                var list = _db.Values.Where(t => t.State != KioskTransactionState.Completed 
                                              && t.State != KioskTransactionState.Failed 
                                              && t.State != KioskTransactionState.ManualReview).ToList();
                return Task.FromResult(list);
            }

            public Task SaveAsync(KioskTransaction transaction, string? eventReason = null)
            {
                // Simple in-memory save/update
                _db[transaction.Id] = transaction;
                return Task.CompletedTask;
            }

            public Task<List<KioskTransaction>> GetTransactionsByDateAsync(DateTime date)
            {
                var list = _db.Values.Where(t => t.CreatedAtUtc.Date == date.Date).ToList();
                return Task.FromResult(list);
            }
        }

        private class FakePosTerminal : IPosTerminal
        {
            public PosCapabilities Capabilities => new PosCapabilities(true, true, true);
            public bool WasChargeAttempted { get; private set; }
            public bool WasReversalAttempted { get; private set; }
            public string ApprovalCode { get; } = "AUTH123";

            public PosTransactionResult NextProcessResult { get; set; } = new PosTransactionResult(true, "AUTH123", "OK", null);
            public PosTransactionResult NextVoidResult { get; set; } = new PosTransactionResult(true, "VOID123", "OK", null);
            
            public bool ThrowTimeoutOnProcess { get; set; }
            public Action? OnProcessAction { get; set; }

            public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
            public Task<bool> ConnectAsync(CancellationToken cancellationToken) => Task.FromResult(true);
            public Task<bool> HealthCheckAsync(CancellationToken cancellationToken) => Task.FromResult(true);

            public async Task<PosTransactionResult> StartSaleAsync(
                TransactionId transactionId, 
                string idempotencyKey, 
                Money amount, 
                TimeSpan timeout, 
                CancellationToken cancellationToken, 
                Guid correlationId)
            {
                WasChargeAttempted = true;
                OnProcessAction?.Invoke();

                if (cancellationToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException(cancellationToken);
                }

                if (ThrowTimeoutOnProcess)
                {
                    throw new TimeoutException("POS transaction timed out");
                }

                await Task.Delay(10, cancellationToken);
                return NextProcessResult;
            }

            public Task<PosTransactionResult> PreAuthorizeAsync(TransactionId transactionId, string idempotencyKey, Money amount, TimeSpan timeout, CancellationToken cancellationToken, Guid correlationId)
                => Task.FromResult(NextProcessResult);

            public Task<PosTransactionResult> CaptureAsync(TransactionId transactionId, string idempotencyKey, Money amount, string preAuthReference, TimeSpan timeout, CancellationToken cancellationToken, Guid correlationId)
                => Task.FromResult(NextProcessResult);

            public Task<PosTransactionResult> QueryTransactionAsync(TransactionId transactionId, string vendorReference, CancellationToken cancellationToken, Guid correlationId)
                => Task.FromResult(NextProcessResult);

            public Task<PosTransactionResult> GetLastTransactionAsync(CancellationToken cancellationToken)
                => Task.FromResult(NextProcessResult);

            public Task<bool> CancelAsync(TransactionId transactionId, CancellationToken cancellationToken)
                => Task.FromResult(true);

            public Task<PosTransactionResult> VoidAsync(TransactionId transactionId, string vendorReference, CancellationToken cancellationToken, Guid correlationId)
            {
                WasReversalAttempted = true;
                return Task.FromResult(NextVoidResult);
            }

            public Task<PosTransactionResult> ReversalAsync(TransactionId transactionId, string vendorReference, Money amount, CancellationToken cancellationToken, Guid correlationId)
            {
                WasReversalAttempted = true;
                return Task.FromResult(NextVoidResult);
            }

            public Task<string> GetBatchSummaryAsync(CancellationToken cancellationToken)
                => Task.FromResult("BATCH SUMMARY");
        }

        private class FakeNfcReader : INfcReader
        {
            private long _balanceMinor;
            private string _cardUid = "35-IZM-9921";
            
            public bool WasLoadAttempted { get; private set; }

            public string NextWaitCardResult { get; set; } = "Detected";
            public string NextLoadResult { get; set; } = "Success";
            public bool NextValidateResult { get; set; } = true;

            public void SetSimulatedCard(string uid, long balanceMinor)
            {
                _cardUid = uid;
                _balanceMinor = balanceMinor;
            }

            public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
            public Task<bool> ConnectAsync(CancellationToken cancellationToken) => Task.FromResult(true);
            public Task<bool> HealthCheckAsync(CancellationToken cancellationToken) => Task.FromResult(true);

            public async Task<CardReference?> WaitForCardAsync(
                TransactionId transactionId, 
                TimeSpan timeout, 
                CancellationToken cancellationToken)
            {
                if (NextWaitCardResult == "Timeout" || NextWaitCardResult == "None")
                {
                    return null;
                }
                return CardReference.Create(_cardUid);
            }

            public Task<bool> ValidateCardAsync(
                TransactionId transactionId, 
                CardReference cardRef, 
                CancellationToken cancellationToken)
            {
                return Task.FromResult(NextValidateResult);
            }

            public Task<CardSnapshot> ReadCardSnapshotAsync(
                TransactionId transactionId, 
                CardReference cardRef, 
                CancellationToken cancellationToken)
            {
                return Task.FromResult(new CardSnapshot(cardRef, _balanceMinor, 1, true));
            }

            public async Task<bool> LoadAmountAsync(
                TransactionId transactionId, 
                string idempotencyKey, 
                CardReference cardRef, 
                Money amount, 
                CancellationToken cancellationToken, 
                Guid correlationId)
            {
                WasLoadAttempted = true;

                if (cancellationToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException(cancellationToken);
                }

                if (NextLoadResult == "Timeout") throw new TimeoutException("NFC write timed out");
                if (NextLoadResult == "Failure") return false;

                _balanceMinor += amount.AmountMinor;
                return true;
            }

            public Task<bool> QueryLoadTransactionAsync(
                TransactionId transactionId, 
                string loadVendorReference, 
                CancellationToken cancellationToken, 
                Guid correlationId) => Task.FromResult(true);

            public Task<bool> VerifyLoadAsync(
                TransactionId transactionId, 
                CardReference cardRef, 
                Money amount, 
                CancellationToken cancellationToken) => Task.FromResult(true);

            public Task<long> ReadVerifiedBalanceAsync(
                TransactionId transactionId, 
                CardReference cardRef, 
                CancellationToken cancellationToken) => Task.FromResult(_balanceMinor);

            public Task WaitForCardRemovalAsync(
                TransactionId transactionId, 
                CardReference cardRef, 
                TimeSpan timeout, 
                CancellationToken cancellationToken) => Task.CompletedTask;
        }

        private class FakeBalanceProvider : IAuthoritativeBalanceProvider
        {
            public bool ThrowExceptionOnGet { get; set; }

            public Task InitializeAsync() => Task.CompletedTask;
            public Task<bool> HealthCheckAsync() => Task.FromResult(true);

            public Task<BalanceResult> GetAuthoritativeBalanceAsync(string cardRef)
            {
                if (ThrowExceptionOnGet)
                {
                    throw new Exception("Backend service unavailable");
                }
                return Task.FromResult(BalanceResult.VerifiedAuthoritative(1000));
            }

            public Task<bool> VerifyBalanceAsync(string cardRef, long expectedBalanceMinor) => Task.FromResult(true);
            public Task<BalanceResult> RefreshBalanceAsync(string cardRef) => Task.FromResult(BalanceResult.VerifiedAuthoritative(1000));
        }
    }
}
