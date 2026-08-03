using System;
using System.IO;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Microsoft.Extensions.Logging.Abstractions;

using IzbanKiosk.Domain;
using IzbanKiosk.Application.Services;
using IzbanKiosk.Application.Repositories;
using IzbanKiosk.Application.Hardware.Nfc;
using IzbanKiosk.Application.Hardware.Balance;
using IzbanKiosk.Infrastructure;
using IzbanKiosk.Infrastructure.Repositories;
using IzbanKiosk.Hardware.Nfc.Simulator;
using IzbanKiosk.Hardware.Balance.Simulator;
using IzbanKiosk.Hardware.Pos.Simulator;

namespace IzbanKiosk.Tests
{
    public class SimulatorBalanceTests : IDisposable
    {
        private readonly string _tempDbPath;
        private readonly DbConnectionFactory _dbConnectionFactory;
        private readonly SimulatorCardLedger _ledger;
        private readonly MockNfcReader _nfcReader;
        private readonly MockBalanceProvider _balanceProvider;
        private readonly InMemoryTransactionRepository _txRepository;
        private readonly MockPosTerminal _posTerminal;
        private readonly RecoveryService _recoveryService;
        private readonly TransactionCoordinator _sut;

        public SimulatorBalanceTests()
        {
            _tempDbPath = Path.Combine(Path.GetTempPath(), $"izban_test_simulator_{Guid.NewGuid():N}.db");
            _dbConnectionFactory = new DbConnectionFactory(_tempDbPath);
            _dbConnectionFactory.InitializeDatabaseAsync().GetAwaiter().GetResult();

            _ledger = new SimulatorCardLedger(_dbConnectionFactory);
            _nfcReader = new MockNfcReader(_ledger);
            _balanceProvider = new MockBalanceProvider(_ledger);

            _txRepository = new InMemoryTransactionRepository();
            _posTerminal = new MockPosTerminal();

            _recoveryService = new RecoveryService(
                _txRepository,
                _posTerminal,
                _nfcReader,
                _balanceProvider,
                NullLogger<RecoveryService>.Instance
            );

            _sut = new TransactionCoordinator(
                _txRepository,
                _posTerminal,
                _nfcReader,
                _balanceProvider,
                _recoveryService,
                NullLogger<TransactionCoordinator>.Instance
            );
        }

        public void Dispose()
        {
            if (File.Exists(_tempDbPath))
            {
                try { File.Delete(_tempDbPath); } catch { }
            }
        }

        [Fact]
        public async Task CumulativeLoadPersistsForSameCard()
        {
            // Arrange
            string cardUid = "CARD-A";
            _nfcReader.SetSimulatedCardUid(cardUid);
            var cardRef = CardReference.Create(cardUid);

            // Act: 1st Load
            var card = await _ledger.GetOrCreateCardAsync(cardRef.Hash);
            Assert.Equal(6250, card.BalanceMinor);

            bool success = await _nfcReader.LoadAmountAsync(
                new TransactionId(Guid.NewGuid()),
                "load-ref-1",
                cardRef,
                new Money(40000), // 400.00 TL
                CancellationToken.None,
                Guid.NewGuid()
            );
            Assert.True(success);

            var verifiedBalance1 = await _nfcReader.ReadVerifiedBalanceAsync(new TransactionId(Guid.NewGuid()), cardRef, CancellationToken.None);
            Assert.Equal(46250, verifiedBalance1);

            // Act: 2nd Load
            success = await _nfcReader.LoadAmountAsync(
                new TransactionId(Guid.NewGuid()),
                "load-ref-2",
                cardRef,
                new Money(10000), // 100.00 TL
                CancellationToken.None,
                Guid.NewGuid()
            );
            Assert.True(success);

            var verifiedBalance2 = await _nfcReader.ReadVerifiedBalanceAsync(new TransactionId(Guid.NewGuid()), cardRef, CancellationToken.None);
            Assert.Equal(56250, verifiedBalance2);
        }

        [Fact]
        public async Task ReloadingApplicationReadsPersistedSimulatorBalance()
        {
            // Arrange
            string cardUid = "CARD-PERSIST";
            _nfcReader.SetSimulatedCardUid(cardUid);
            var cardRef = CardReference.Create(cardUid);

            // Load 400.00 TL
            await _nfcReader.LoadAmountAsync(
                new TransactionId(Guid.NewGuid()),
                "persist-load-1",
                cardRef,
                new Money(40000),
                CancellationToken.None,
                Guid.NewGuid()
            );

            // Act: Simulate app restart by constructing new ledger & reader instances
            var newLedger = new SimulatorCardLedger(_dbConnectionFactory);
            var newNfc = new MockNfcReader(newLedger);

            var balance = await newNfc.ReadVerifiedBalanceAsync(new TransactionId(Guid.NewGuid()), cardRef, CancellationToken.None);

            // Assert
            Assert.Equal(46250, balance);
        }

        [Fact]
        public async Task DifferentCardsHaveIndependentBalances()
        {
            // Arrange
            string cardUidA = "CARD-A";
            string cardUidB = "CARD-B";

            var cardRefA = CardReference.Create(cardUidA);
            var cardRefB = CardReference.Create(cardUidB);

            // Act: Load CARD-A with 400.00 TL
            _nfcReader.SetSimulatedCardUid(cardUidA);
            await _nfcReader.LoadAmountAsync(
                new TransactionId(Guid.NewGuid()),
                "card-a-load",
                cardRefA,
                new Money(40000),
                CancellationToken.None,
                Guid.NewGuid()
            );

            // Assert: CARD-B should still have default balance (62.50 TL)
            _nfcReader.SetSimulatedCardUid(cardUidB);
            var cardB = await _ledger.GetOrCreateCardAsync(cardRefB.Hash);
            Assert.Equal(6250, cardB.BalanceMinor);

            // Assert: CARD-A retains its loaded balance
            var cardA = await _ledger.GetOrCreateCardAsync(cardRefA.Hash);
            Assert.Equal(46250, cardA.BalanceMinor);
        }

        [Fact]
        public async Task DuplicateLoadReferenceDoesNotLoadTwice()
        {
            // Arrange
            string cardUid = "CARD-IDEMPOTENT";
            _nfcReader.SetSimulatedCardUid(cardUid);
            var cardRef = CardReference.Create(cardUid);

            // Act: First Load (100.00 TL) with load-key-1
            bool success1 = await _nfcReader.LoadAmountAsync(
                new TransactionId(Guid.NewGuid()),
                "load-key-1",
                cardRef,
                new Money(10000),
                CancellationToken.None,
                Guid.NewGuid()
            );
            Assert.True(success1);

            // Second Load (100.00 TL) with the SAME load-key-1
            bool success2 = await _nfcReader.LoadAmountAsync(
                new TransactionId(Guid.NewGuid()),
                "load-key-1",
                cardRef,
                new Money(10000),
                CancellationToken.None,
                Guid.NewGuid()
            );
            Assert.True(success2);

            // Assert
            var balance = await _nfcReader.ReadVerifiedBalanceAsync(new TransactionId(Guid.NewGuid()), cardRef, CancellationToken.None);
            Assert.Equal(16250, balance); // 62.50 + 100.00. The second load must be ignored.
        }

        [Fact]
        public async Task SuccessScreenUsesVerifiedNewBalance()
        {
            // Arrange
            string cardUid = "CARD-FLOW";
            _nfcReader.SetSimulatedCardUid(cardUid);
            Money loadAmount = new Money(5000); // 50.00 TL

            // Act
            var result = await _sut.ProcessTransactionAsync(
                "tx-flow-key",
                loadAmount,
                TimeSpan.FromSeconds(5),
                CancellationToken.None
            );

            // Assert
            Assert.Equal(KioskTransactionState.Completed, result.State);
            Assert.Equal(11250, result.NewBalanceMinor); // 62.50 + 50.00 = 112.50
        }

        [Fact]
        public async Task BalanceMismatchCannotComplete()
        {
            // Arrange
            string cardUid = "CARD-MISMATCH";
            _nfcReader.SetSimulatedCardUid(cardUid);
            Money loadAmount = new Money(5000);

            var cardRef = CardReference.Create(cardUid);
            await _ledger.GetOrCreateCardAsync(cardRef.Hash);

            // Force verification mismatch
            _nfcReader.CustomVerifiedBalance = 99999L;

            // Act
            var result = await _sut.ProcessTransactionAsync(
                "tx-mismatch-key",
                loadAmount,
                TimeSpan.FromSeconds(5),
                CancellationToken.None
            );

            // Assert
            Assert.Equal(KioskTransactionState.Failed, result.State);
            Assert.Contains("Transaction load failed", result.ErrorMessage);
        }

        [Fact]
        public async Task CardDetectionDoesNotResetBalance()
        {
            // Arrange
            string cardUid = "CARD-TAP-TWICE";
            _nfcReader.SetSimulatedCardUid(cardUid);
            var cardRef = CardReference.Create(cardUid);

            // Act: 1st Load
            await _nfcReader.LoadAmountAsync(
                new TransactionId(Guid.NewGuid()),
                "load-tap-1",
                cardRef,
                new Money(20000), // 200.00 TL
                CancellationToken.None,
                Guid.NewGuid()
            );

            // Act: Detect again (simulating subsequent tap / read)
            var cardRef2 = await _nfcReader.WaitForCardAsync(new TransactionId(Guid.NewGuid()), TimeSpan.FromSeconds(5), CancellationToken.None);
            Assert.NotNull(cardRef2);
            Assert.Equal(cardRef.Hash, cardRef2.Hash);

            var snapshot = await _nfcReader.ReadCardSnapshotAsync(new TransactionId(Guid.NewGuid()), cardRef2, CancellationToken.None);

            // Assert
            Assert.Equal(26250, snapshot.BalanceMinor); // must not reset back to 6250!
        }

        private class InMemoryTransactionRepository : ITransactionRepository
        {
            private readonly ConcurrentDictionary<TransactionId, KioskTransaction> _store = new();

            public Task<KioskTransaction?> GetByIdAsync(TransactionId id)
            {
                _store.TryGetValue(id, out var tx);
                return Task.FromResult(tx);
            }

            public Task<KioskTransaction?> GetByIdempotencyKeyAsync(string key)
            {
                foreach (var tx in _store.Values)
                {
                    if (tx.IdempotencyKey == key)
                        return Task.FromResult<KioskTransaction?>(tx);
                }
                return Task.FromResult<KioskTransaction?>(null);
            }

            public Task SaveAsync(KioskTransaction transaction, string? eventReason = null)
            {
                _store[transaction.Id] = transaction;
                return Task.CompletedTask;
            }

            public Task<System.Collections.Generic.List<KioskTransaction>> GetPendingTransactionsAsync()
            {
                var list = new System.Collections.Generic.List<KioskTransaction>();
                foreach (var tx in _store.Values)
                {
                    if (tx.State != KioskTransactionState.Completed && 
                        tx.State != KioskTransactionState.Failed && 
                        tx.State != KioskTransactionState.ManualReview)
                    {
                        list.Add(tx);
                    }
                }
                return Task.FromResult(list);
            }

            public Task<System.Collections.Generic.List<KioskTransaction>> GetTransactionsByDateAsync(DateTime date)
            {
                var list = new System.Collections.Generic.List<KioskTransaction>();
                foreach (var tx in _store.Values)
                {
                    if (tx.CreatedAtUtc.Date == date.Date)
                    {
                        list.Add(tx);
                    }
                }
                return Task.FromResult(list);
            }
        }
    }
}
