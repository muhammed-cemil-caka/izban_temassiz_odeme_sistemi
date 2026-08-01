using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Microsoft.Extensions.Logging.Abstractions;

using IzbanKiosk.Domain;
using IzbanKiosk.Application.Services;
using IzbanKiosk.Application.Repositories;
using IzbanKiosk.Application.Hardware.Pos;
using IzbanKiosk.Application.Hardware.Balance;
using IzbanKiosk.Management.Contracts;

namespace IzbanKiosk.Tests
{
    public class ReconciliationServiceTests
    {
        private readonly InMemoryTransactionRepository _txRepository;
        private readonly FakePosTerminal _posTerminal;
        private readonly FakeBalanceProvider _balanceProvider;
        private readonly ReconciliationService _sut;

        public ReconciliationServiceTests()
        {
            _txRepository = new InMemoryTransactionRepository();
            _posTerminal = new FakePosTerminal();
            _balanceProvider = new FakeBalanceProvider();
            _sut = new ReconciliationService(
                _txRepository,
                _posTerminal,
                _balanceProvider,
                NullLogger<ReconciliationService>.Instance
            );
        }

        [Fact]
        public async Task ReconcileDaily_WhenTotalsAligned_ShouldReturnSuccessMatch()
        {
            // Arrange
            var date = DateTime.UtcNow.Date;
            var tx1 = new KioskTransaction(new TransactionId(Guid.NewGuid()), "k1");
            tx1.LoadProperties(
                KioskTransactionState.Completed, 
                CardReference.Create("card1"), 
                new Money(3000), 
                "POS-1", 
                "LOAD-1",
                "AUTH-1", 
                "00", 
                null, 
                0, 
                1000, 
                4000 // loaded 30.00 TL successfully
            );
            await _txRepository.SaveAsync(tx1);

            var tx2 = new KioskTransaction(new TransactionId(Guid.NewGuid()), "k2");
            tx2.LoadProperties(
                KioskTransactionState.Completed, 
                CardReference.Create("card2"), 
                new Money(2000), 
                "POS-2", 
                "LOAD-2",
                "AUTH-2", 
                "00", 
                null, 
                0, 
                500, 
                2500 // loaded 20.00 TL successfully
            );
            await _txRepository.SaveAsync(tx2);

            // Total local completed = 50.00 TL (5000 minor units)
            _posTerminal.BatchSummary = "BATCH-CLOSE-SUCCESS - Total: 50.00 TRY";

            // Act
            var report = await _sut.ReconcileDailyAsync("KIOSK-01", date, CancellationToken.None);

            // Assert
            Assert.True(report.IsMatched);
            Assert.Equal(5000, report.CalculatedLedgerSumMinor);
            Assert.Equal(5000, report.PosReportSumMinor);
            Assert.Equal(5000, report.CardReportSumMinor);
            Assert.Null(report.DiscrepancyReason);
        }

        [Fact]
        public async Task ReconcileDaily_WhenPosMismatch_ShouldReportFailureAndReason()
        {
            // Arrange
            var date = DateTime.UtcNow.Date;
            var tx1 = new KioskTransaction(new TransactionId(Guid.NewGuid()), "k1");
            tx1.LoadProperties(
                KioskTransactionState.Completed, 
                CardReference.Create("card1"), 
                new Money(3000), 
                "POS-1", 
                "LOAD-1",
                "AUTH-1", 
                "00", 
                null, 
                0, 
                1000, 
                4000
            );
            await _txRepository.SaveAsync(tx1);

            // POS terminal has recorded 40.00 TRY instead of 30.00 TRY
            _posTerminal.BatchSummary = "BATCH-CLOSE-SUCCESS - Total: 40.00 TRY";

            // Act
            var report = await _sut.ReconcileDailyAsync("KIOSK-01", date, CancellationToken.None);

            // Assert
            Assert.False(report.IsMatched);
            Assert.Equal(3000, report.CalculatedLedgerSumMinor);
            Assert.Equal(4000, report.PosReportSumMinor);
            Assert.Contains("POS mismatch", report.DiscrepancyReason);
        }

        [Fact]
        public async Task ReconcileDaily_WhenSamLoadMismatch_ShouldReportFailureAndReason()
        {
            // Arrange
            var date = DateTime.UtcNow.Date;
            var tx1 = new KioskTransaction(new TransactionId(Guid.NewGuid()), "k1");
            
            // Transaction marked Completed, but the card balance change doesn't match the loaded amount (SIMULATES hardware write error/tampering)
            tx1.LoadProperties(
                KioskTransactionState.Completed, 
                CardReference.Create("card1"), 
                new Money(3000), 
                "POS-1", 
                "LOAD-1",
                "AUTH-1", 
                "00", 
                null, 
                0, 
                1000, 
                1000 // Card balance stayed 1000 (no increment)
            );
            await _txRepository.SaveAsync(tx1);

            _posTerminal.BatchSummary = "BATCH-CLOSE-SUCCESS - Total: 30.00 TRY";

            // Act
            var report = await _sut.ReconcileDailyAsync("KIOSK-01", date, CancellationToken.None);

            // Assert
            Assert.False(report.IsMatched);
            Assert.Equal(3000, report.CalculatedLedgerSumMinor);
            Assert.Equal(3000, report.PosReportSumMinor);
            Assert.Equal(0, report.CardReportSumMinor); // 0 verified SAM load
            Assert.Contains("SAM Load mismatch", report.DiscrepancyReason);
        }

        // ==================== RECONCILIATION TEST MOCKS ====================

        private class InMemoryTransactionRepository : ITransactionRepository
        {
            private readonly List<KioskTransaction> _db = new();

            public Task<KioskTransaction?> GetByIdAsync(TransactionId id) => Task.FromResult(_db.FirstOrDefault(t => t.Id == id));
            public Task<KioskTransaction?> GetByIdempotencyKeyAsync(string key) => Task.FromResult(_db.FirstOrDefault(t => t.IdempotencyKey == key));
            public Task<List<KioskTransaction>> GetPendingTransactionsAsync() => Task.FromResult(_db.Where(t => t.State != KioskTransactionState.Completed && t.State != KioskTransactionState.Failed && t.State != KioskTransactionState.ManualReview).ToList());
            
            public Task<List<KioskTransaction>> GetTransactionsByDateAsync(DateTime date)
            {
                return Task.FromResult(_db.Where(t => t.CreatedAtUtc.Date == date.Date).ToList());
            }

            public Task SaveAsync(KioskTransaction tx, string? eventReason = null)
            {
                var idx = _db.FindIndex(t => t.Id == tx.Id);
                if (idx >= 0) _db[idx] = tx;
                else _db.Add(tx);
                return Task.CompletedTask;
            }
        }

        private class FakePosTerminal : IPosTerminal
        {
            public PosCapabilities Capabilities => new PosCapabilities(true, true, true);
            public string BatchSummary { get; set; } = "BATCH-CLOSE-SUCCESS - Total: 0.00 TRY";

            public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
            public Task<bool> ConnectAsync(CancellationToken cancellationToken) => Task.FromResult(true);
            public Task<bool> HealthCheckAsync(CancellationToken cancellationToken) => Task.FromResult(true);

            public Task<PosTransactionResult> StartSaleAsync(TransactionId transactionId, string idempotencyKey, Money amount, TimeSpan timeout, CancellationToken cancellationToken, Guid correlationId)
                => Task.FromResult(new PosTransactionResult(true, "A", "V", "00", null, null, amount.AmountMinor));

            public Task<PosTransactionResult> PreAuthorizeAsync(TransactionId transactionId, string idempotencyKey, Money amount, TimeSpan timeout, CancellationToken cancellationToken, Guid correlationId)
                => Task.FromResult(new PosTransactionResult(true, "A", "V", "00", null, null, amount.AmountMinor));

            public Task<PosTransactionResult> CaptureAsync(TransactionId transactionId, string idempotencyKey, Money amount, string preAuthReference, TimeSpan timeout, CancellationToken cancellationToken, Guid correlationId)
                => Task.FromResult(new PosTransactionResult(true, "A", "V", "00", null, null, amount.AmountMinor));

            public Task<PosTransactionResult> QueryTransactionAsync(TransactionId transactionId, string vendorReference, CancellationToken cancellationToken, Guid correlationId)
                => Task.FromResult(new PosTransactionResult(true, "A", "V", "00", null, null, 0));

            public Task<PosTransactionResult> GetLastTransactionAsync(CancellationToken cancellationToken)
                => Task.FromResult(new PosTransactionResult(true, "A", "V", "00", null, null, 0));

            public Task<bool> CancelAsync(TransactionId transactionId, CancellationToken cancellationToken) => Task.FromResult(true);

            public Task<PosTransactionResult> VoidAsync(TransactionId transactionId, string vendorReference, CancellationToken cancellationToken, Guid correlationId)
                => Task.FromResult(new PosTransactionResult(true, "A", "V", "00", null, null, 0));

            public Task<PosTransactionResult> ReversalAsync(TransactionId transactionId, string vendorReference, Money amount, CancellationToken cancellationToken, Guid correlationId)
                => Task.FromResult(new PosTransactionResult(true, "A", "V", "00", null, null, amount.AmountMinor));

            public Task<string> GetBatchSummaryAsync(CancellationToken cancellationToken) => Task.FromResult(BatchSummary);
        }

        private class FakeBalanceProvider : IAuthoritativeBalanceProvider
        {
            public Task InitializeAsync() => Task.CompletedTask;
            public Task<bool> HealthCheckAsync() => Task.FromResult(true);
            public Task<BalanceResult> GetAuthoritativeBalanceAsync(string cardRef) => Task.FromResult(BalanceResult.VerifiedAuthoritative(1000));
            public Task<bool> VerifyBalanceAsync(string cardRef, long expectedBalanceMinor) => Task.FromResult(true);
            public Task<BalanceResult> RefreshBalanceAsync(string cardRef) => Task.FromResult(BalanceResult.VerifiedAuthoritative(1000));
        }
    }
}
