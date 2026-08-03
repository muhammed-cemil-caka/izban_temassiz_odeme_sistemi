using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Microsoft.Extensions.Logging.Abstractions;

using IzbanKiosk.Domain;
using IzbanKiosk.Application.Services;
using IzbanKiosk.Application.Repositories;
using IzbanKiosk.Application.Hardware.Receipt;
using IzbanKiosk.Hardware.Receipt.Simulator;
using IzbanKiosk.Infrastructure;
using IzbanKiosk.Infrastructure.Repositories;

namespace IzbanKiosk.Tests
{
    public class ReceiptServiceTests : IDisposable
    {
        private readonly string _tempDbPath;
        private readonly DbConnectionFactory _dbConnectionFactory;
        private readonly SqliteReceiptRepository _receiptRepository;
        private readonly InMemoryTransactionRepository _transactionRepository;
        private readonly MockReceiptPrinter _receiptPrinter;
        private readonly ReceiptDocumentFactory _documentFactory;
        private readonly ReceiptService _sut;

        public ReceiptServiceTests()
        {
            _tempDbPath = Path.Combine(Path.GetTempPath(), $"izban_test_receipts_{Guid.NewGuid():N}.db");
            _dbConnectionFactory = new DbConnectionFactory(_tempDbPath);
            _dbConnectionFactory.InitializeDatabaseAsync().GetAwaiter().GetResult();

            _receiptRepository = new SqliteReceiptRepository(_dbConnectionFactory);
            _transactionRepository = new InMemoryTransactionRepository();
            
            var options = new ReceiptPrinterOptions
            {
                Enabled = true,
                Port = "COM3",
                BaudRate = 9600,
                Simulator = new SimulatorOptions
                {
                    WritePreviewFile = true,
                    PreviewDirectory = "SimulatedReceipts"
                }
            };
            
            _receiptPrinter = new MockReceiptPrinter();
            _documentFactory = new ReceiptDocumentFactory();
            _sut = new ReceiptService(
                _transactionRepository,
                _receiptRepository,
                _receiptPrinter,
                _documentFactory
            );
        }

        public void Dispose()
        {
            if (File.Exists(_tempDbPath))
            {
                try { File.Delete(_tempDbPath); } catch { }
            }
        }

        // ==================== DOMAIN: RECEIPTRECORD TESTS ====================

        [Fact]
        public void ReceiptRecord_Create_ShouldInitializeCorrectly()
        {
            // Act
            var record = new ReceiptRecord("tx-1234");

            // Assert
            Assert.NotNull(record.ReceiptId);
            Assert.Equal("tx-1234", record.TransactionId);
            Assert.Equal("Offered", record.Decision);
            Assert.Equal(ReceiptStatus.Offered, record.Status);
            Assert.Equal(0, record.RetryCount);
            Assert.Null(record.PrintedAtUtc);
        }

        [Fact]
        public void ReceiptRecord_StartPrint_ShouldTransitionCorrectly()
        {
            // Arrange
            var record = new ReceiptRecord("tx-1234");
            record.TransitionTo(ReceiptStatus.Requested);

            // Act
            record.TransitionTo(ReceiptStatus.Printing, jobRef: "job-1");

            // Assert
            Assert.Equal(ReceiptStatus.Printing, record.Status);
            Assert.Equal("job-1", record.PrinterJobReference);
            Assert.NotNull(record.PrintStartedAtUtc);
        }

        [Fact]
        public void ReceiptRecord_CompletePrint_ShouldTransitionCorrectly()
        {
            // Arrange
            var record = new ReceiptRecord("tx-1234");
            record.TransitionTo(ReceiptStatus.Requested);
            record.TransitionTo(ReceiptStatus.Printing, jobRef: "job-1");

            // Act
            record.TransitionTo(ReceiptStatus.Printed);

            // Assert
            Assert.Equal(ReceiptStatus.Printed, record.Status);
            Assert.NotNull(record.PrintedAtUtc);
        }

        [Fact]
        public void ReceiptRecord_MarkFailed_ShouldTransitionCorrectly()
        {
            // Arrange
            var record = new ReceiptRecord("tx-1234");
            record.TransitionTo(ReceiptStatus.Requested);

            // Act
            record.TransitionTo(ReceiptStatus.Failed, errorCode: "ERR_PRN", errorMessage: "Printer disconnected");

            // Assert
            Assert.Equal(ReceiptStatus.Failed, record.Status);
            Assert.Equal("ERR_PRN", record.ErrorCode);
            Assert.Equal("Printer disconnected", record.ErrorMessage);
        }

        [Fact]
        public void ReceiptRecord_MarkOutOfPaper_ShouldTransitionCorrectly()
        {
            // Arrange
            var record = new ReceiptRecord("tx-1234");
            record.TransitionTo(ReceiptStatus.Requested);
            record.TransitionTo(ReceiptStatus.Printing, jobRef: "job-1");

            // Act
            record.TransitionTo(ReceiptStatus.PaperOut, errorCode: "ERR_PAPER", errorMessage: "Paper out error");

            // Assert
            Assert.Equal(ReceiptStatus.PaperOut, record.Status);
            Assert.Equal("ERR_PAPER", record.ErrorCode);
            Assert.Contains("paper", record.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ReceiptRecord_IncrementRetryCount_ShouldWork()
        {
            // Arrange
            var record = new ReceiptRecord("tx-1234");

            // Act
            record.IncrementRetry();

            // Assert
            Assert.Equal(1, record.RetryCount);
        }

        // ==================== DOMAIN/APPLICATION: RECEIPTDOCUMENTFACTORY TESTS ====================

        [Fact]
        public void ReceiptDocumentFactory_CreateReceiptText_Turkish_ShouldFormatCorrectly()
        {
            // Arrange
            var txId = new TransactionId(Guid.NewGuid());
            var tx = new KioskTransaction(txId, "happy-key-tr");
            var cardRef = CardReference.Create("35-IZM-9921");
            tx.LoadProperties(
                KioskTransactionState.Completed,
                cardRef,
                new Money(4500),
                "pos-ref-123",
                "load-ref-123",
                "APP-TURKISH",
                "00",
                null,
                0,
                1000,
                5500
            );

            // Act
            var doc = _documentFactory.CreateReceipt(tx, "Alsancak", "Kiosk-1");

            // Assert
            Assert.Contains("İZBAN / İZMİRİM KART", doc.Title);
            Assert.Contains("BAKİYE YÜKLEME BİLGİ MAKBUZU", doc.SubTitle);
            Assert.Contains("45,00", doc.LoadedAmount);
            Assert.Contains("₺", doc.LoadedAmount);
            Assert.Equal("35-I••••9921", doc.MaskedCardNumber);
            Assert.Equal("APP-TURKISH", doc.PosApprovalCode);
        }

        [Fact]
        public void ReceiptDocumentFactory_CreateReceiptText_ShouldIncludePOSApprovalCode()
        {
            // Arrange
            var txId = new TransactionId(Guid.NewGuid());
            var tx = new KioskTransaction(txId, "happy-key-code");
            var cardRef = CardReference.Create("35-IZM-9921");
            tx.LoadProperties(
                KioskTransactionState.Completed,
                cardRef,
                new Money(1000),
                "pos-ref-123",
                "load-ref-123",
                "123456",
                "00",
                null,
                0,
                500,
                1500
            );

            // Act
            var doc = _documentFactory.CreateReceipt(tx, "Karsiyaka", "Kiosk-2");

            // Assert
            Assert.Equal("123456", doc.PosApprovalCode);
        }

        // ==================== APPLICATION/HARDWARE: MOCKRECEIPTPRINTER TESTS ====================

        [Fact]
        public async Task MockReceiptPrinter_PrintAsync_WhenReady_ShouldSucceed()
        {
            // Act
            var doc = new ReceiptDocument("Title", "SubTitle", "Alsancak", "K-1", "123", "2026-08-03", "tx...", "card...", "TL 10", "TL 5", "TL 15", "TRY", "pos-ref", "approval", "load-ref", "OK", "Support", "Thanks", "hash");
            var result = await _receiptPrinter.PrintReceiptAsync(doc, "idemp-key-1", CancellationToken.None);

            // Assert
            Assert.True(result.Success);
            Assert.NotEmpty(result.PrinterJobReference);
            Assert.Null(result.ErrorMessage);
            Assert.Equal(doc, _receiptPrinter.LastPrintedDocument);
        }

        [Fact]
        public async Task MockReceiptPrinter_PrintAsync_WhenOffline_ShouldFail()
        {
            // Arrange
            _receiptPrinter.Configure(ReceiptPrinterStatusCode.Offline, ReceiptPrintOutcome.Failed);

            // Act
            var doc = new ReceiptDocument("Title", "SubTitle", "Alsancak", "K-1", "123", "2026-08-03", "tx...", "card...", "TL 10", "TL 5", "TL 15", "TRY", "pos-ref", "approval", "load-ref", "OK", "Support", "Thanks", "hash");
            var result = await _receiptPrinter.PrintReceiptAsync(doc, "idemp-key-2", CancellationToken.None);

            // Assert
            Assert.False(result.Success);
            Assert.Equal("MOCK_ERROR_FAILED", result.ErrorCode);
        }

        [Fact]
        public async Task MockReceiptPrinter_PrintAsync_WhenOutOfPaper_ShouldFail()
        {
            // Arrange
            _receiptPrinter.Configure(ReceiptPrinterStatusCode.PaperOut, ReceiptPrintOutcome.PaperOut);

            // Act
            var doc = new ReceiptDocument("Title", "SubTitle", "Alsancak", "K-1", "123", "2026-08-03", "tx...", "card...", "TL 10", "TL 5", "TL 15", "TRY", "pos-ref", "approval", "load-ref", "OK", "Support", "Thanks", "hash");
            var result = await _receiptPrinter.PrintReceiptAsync(doc, "idemp-key-3", CancellationToken.None);

            // Assert
            Assert.False(result.Success);
            Assert.Equal("MOCK_ERROR_PAPEROUT", result.ErrorCode);
        }

        [Fact]
        public async Task MockReceiptPrinter_HealthCheck_ShouldReflectCurrentState()
        {
            // Arrange
            _receiptPrinter.Configure(ReceiptPrinterStatusCode.Offline, ReceiptPrintOutcome.Failed);

            // Act
            var health = await _receiptPrinter.HealthCheckAsync(CancellationToken.None);

            // Assert
            Assert.Equal(ReceiptPrinterStatusCode.Offline, health.Code);
            Assert.Contains("Offline", health.Message);
        }

        // ==================== APPLICATION: RECEIPTSERVICE TESTS ====================

        [Fact]
        public async Task ReceiptService_PrintReceiptAsync_WhenPrinterReady_ShouldSucceed()
        {
            // Arrange
            var txGuid = Guid.NewGuid();
            var txId = new TransactionId(txGuid);
            var tx = new KioskTransaction(txId, "happy-srv-key");
            var cardRef = CardReference.Create("35-IZM-9921");
            tx.LoadProperties(
                KioskTransactionState.Completed,
                cardRef,
                new Money(10000),
                "pos-ref-99",
                "load-ref-99",
                "ONAY99",
                "00",
                null,
                0,
                2000,
                12000
            );
            await _transactionRepository.SaveAsync(tx);

            // Act
            var result = await _sut.PrintReceiptAsync(txGuid.ToString(), "Alsancak", "Kiosk-1", CancellationToken.None);

            // Assert
            Assert.True(result.Success);
            Assert.NotNull(result.PrinterJobReference);

            var record = await _receiptRepository.GetByTransactionIdAsync(txGuid.ToString());
            Assert.NotNull(record);
            Assert.Equal(ReceiptStatus.Printed, record.Status);
        }

        [Fact]
        public async Task ReceiptService_PrintReceiptAsync_WhenPrinterOffline_ShouldFailAndLog()
        {
            // Arrange
            _receiptPrinter.Configure(ReceiptPrinterStatusCode.Offline, ReceiptPrintOutcome.Failed);
            var txGuid = Guid.NewGuid();
            var txId = new TransactionId(txGuid);
            var tx = new KioskTransaction(txId, "happy-srv-key-2");
            var cardRef = CardReference.Create("35-IZM-9921");
            tx.LoadProperties(
                KioskTransactionState.Completed,
                cardRef,
                new Money(5000),
                "pos-ref-99",
                "load-ref-99",
                "ONAY99",
                "00",
                null,
                0,
                2000,
                7000
            );
            await _transactionRepository.SaveAsync(tx);

            // Act
            var result = await _sut.PrintReceiptAsync(txGuid.ToString(), "Alsancak", "Kiosk-1", CancellationToken.None);

            // Assert
            Assert.False(result.Success);
            Assert.Equal("MOCK_ERROR_FAILED", result.ErrorCode);

            var record = await _receiptRepository.GetByTransactionIdAsync(txGuid.ToString());
            Assert.NotNull(record);
            Assert.Equal(ReceiptStatus.Failed, record.Status);
            Assert.Equal("MOCK_ERROR_FAILED", record.ErrorCode);
        }

        [Fact]
        public async Task ReceiptService_PrintReceiptAsync_WhenOutOfPaper_ShouldFailAndLog()
        {
            // Arrange
            _receiptPrinter.Configure(ReceiptPrinterStatusCode.PaperOut, ReceiptPrintOutcome.PaperOut);
            var txGuid = Guid.NewGuid();
            var txId = new TransactionId(txGuid);
            var tx = new KioskTransaction(txId, "happy-srv-key-3");
            var cardRef = CardReference.Create("35-IZM-9921");
            tx.LoadProperties(
                KioskTransactionState.Completed,
                cardRef,
                new Money(5000),
                "pos-ref-99",
                "load-ref-99",
                "ONAY99",
                "00",
                null,
                0,
                2000,
                7000
            );
            await _transactionRepository.SaveAsync(tx);

            // Act
            var result = await _sut.PrintReceiptAsync(txGuid.ToString(), "Alsancak", "Kiosk-1", CancellationToken.None);

            // Assert
            Assert.False(result.Success);
            Assert.Equal("MOCK_ERROR_PAPEROUT", result.ErrorCode);

            var record = await _receiptRepository.GetByTransactionIdAsync(txGuid.ToString());
            Assert.NotNull(record);
            Assert.Equal(ReceiptStatus.PaperOut, record.Status);
        }

        [Fact]
        public async Task ReceiptService_RecordDecisionAsync_WhenUserDeclines_ShouldSaveDeclinedRecord()
        {
            // Arrange
            var txGuid = Guid.NewGuid();
            var txId = new TransactionId(txGuid);
            var tx = new KioskTransaction(txId, "happy-opt-key-1");
            var cardRef = CardReference.Create("35-IZM-9921");
            tx.LoadProperties(
                KioskTransactionState.Completed,
                cardRef,
                new Money(3000),
                "pos-ref-99",
                "load-ref-99",
                "ONAY99",
                "00",
                null,
                0,
                2000,
                5000
            );
            await _transactionRepository.SaveAsync(tx);

            // Act
            await _sut.RecordDecisionAsync(txGuid.ToString(), "Declined", CancellationToken.None);

            // Assert
            var record = await _receiptRepository.GetByTransactionIdAsync(txGuid.ToString());
            Assert.NotNull(record);
            Assert.Equal("Declined", record.Decision);
            Assert.Equal(ReceiptStatus.Declined, record.Status);
        }

        [Fact]
        public async Task ReceiptService_RecordDecisionAsync_WhenUserAccepts_ShouldSaveRequestedRecord()
        {
            // Arrange
            var txGuid = Guid.NewGuid();
            var txId = new TransactionId(txGuid);
            var tx = new KioskTransaction(txId, "happy-opt-key-2");
            var cardRef = CardReference.Create("35-IZM-9921");
            tx.LoadProperties(
                KioskTransactionState.Completed,
                cardRef,
                new Money(3000),
                "pos-ref-99",
                "load-ref-99",
                "ONAY99",
                "00",
                null,
                0,
                2000,
                5000
            );
            await _transactionRepository.SaveAsync(tx);

            // Act
            await _sut.RecordDecisionAsync(txGuid.ToString(), "Requested", CancellationToken.None);

            // Assert
            var record = await _receiptRepository.GetByTransactionIdAsync(txGuid.ToString());
            Assert.NotNull(record);
            Assert.Equal("Requested", record.Decision);
            Assert.Equal(ReceiptStatus.Requested, record.Status);
        }

        // ==================== INFRASTRUCTURE: SQLITERECEIPTREPOSITORY TESTS ====================

        [Fact]
        public async Task SqliteReceiptRepository_SaveAsync_ShouldInsertRow()
        {
            // Arrange
            var record = new ReceiptRecord("tx-db-1");

            // Act
            await _receiptRepository.SaveAsync(record);

            // Assert
            var loaded = await _receiptRepository.GetByTransactionIdAsync("tx-db-1");
            Assert.NotNull(loaded);
            Assert.Equal(record.ReceiptId, loaded.ReceiptId);
            Assert.Equal("Offered", loaded.Decision);
            Assert.Equal(ReceiptStatus.Offered, loaded.Status);
        }

        [Fact]
        public async Task SqliteReceiptRepository_GetByTransactionIdAsync_ShouldReturnCorrectRecord()
        {
            // Arrange
            var record1 = new ReceiptRecord("tx-db-2a");
            var record2 = new ReceiptRecord("tx-db-2b");
            await _receiptRepository.SaveAsync(record1);
            await _receiptRepository.SaveAsync(record2);

            // Act
            var loaded = await _receiptRepository.GetByTransactionIdAsync("tx-db-2b");

            // Assert
            Assert.NotNull(loaded);
            Assert.Equal(record2.ReceiptId, loaded.ReceiptId);
        }

        [Fact]
        public async Task SqliteReceiptRepository_SaveAsync_ShouldModifyRecord_WhenRowExists()
        {
            // Arrange
            var record = new ReceiptRecord("tx-db-3");
            await _receiptRepository.SaveAsync(record);

            // Act
            record.TransitionTo(ReceiptStatus.Requested);
            record.TransitionTo(ReceiptStatus.Printing, jobRef: "job-9");
            record.TransitionTo(ReceiptStatus.Printed);
            await _receiptRepository.SaveAsync(record);

            // Assert
            var loaded = await _receiptRepository.GetByTransactionIdAsync("tx-db-3");
            Assert.NotNull(loaded);
            Assert.Equal(ReceiptStatus.Printed, loaded.Status);
            Assert.Equal("job-9", loaded.PrinterJobReference);
            Assert.NotNull(loaded.PrintedAtUtc);
            Assert.Equal(2, loaded.RowVersion);
        }

        [Fact]
        public async Task SqliteReceiptRepository_ConcurrencyCheck_ShouldFailOnStaleVersion()
        {
            // Arrange
            var record = new ReceiptRecord("tx-db-5");
            await _receiptRepository.SaveAsync(record);

            // Load clone 1 and clone 2
            var clone1 = await _receiptRepository.GetByTransactionIdAsync("tx-db-5");
            var clone2 = await _receiptRepository.GetByTransactionIdAsync("tx-db-5");

            Assert.NotNull(clone1);
            Assert.NotNull(clone2);

            // Modify clone 1 and save
            clone1.TransitionTo(ReceiptStatus.Requested);
            await _receiptRepository.SaveAsync(clone1);

            // Modify clone 2 and attempt to save (concurrency conflict should occur)
            clone2.TransitionTo(ReceiptStatus.Requested);
            
            // Assert
            await Assert.ThrowsAnyAsync<Exception>(async () =>
            {
                await _receiptRepository.SaveAsync(clone2);
            });
        }

        // ==================== IN-MEMORY TRANSACTION REPOSITORY HELPER ====================

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
                _db[transaction.Id] = transaction;
                return Task.CompletedTask;
            }

            public Task<List<KioskTransaction>> GetTransactionsByDateAsync(DateTime date)
            {
                var list = _db.Values.Where(t => t.CreatedAtUtc.Date == date.Date).ToList();
                return Task.FromResult(list);
            }
        }
    }
}
