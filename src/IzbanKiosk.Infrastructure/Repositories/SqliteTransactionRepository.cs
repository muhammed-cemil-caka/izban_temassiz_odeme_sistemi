using System;
using System.Collections.Generic;
using System.Data;
using System.Text.Json;
using System.Threading.Tasks;
using IzbanKiosk.Domain;
using IzbanKiosk.Application.Repositories;
using Microsoft.Data.Sqlite;

namespace IzbanKiosk.Infrastructure.Repositories
{
    public class SqliteTransactionRepository : ITransactionRepository
    {
        private readonly DbConnectionFactory _dbConnectionFactory;

        public SqliteTransactionRepository(DbConnectionFactory dbConnectionFactory)
        {
            _dbConnectionFactory = dbConnectionFactory;
        }

        public async Task<KioskTransaction?> GetByIdAsync(TransactionId id)
        {
            using var connection = await _dbConnectionFactory.CreateAndOpenConnectionAsync();

            string sql = "SELECT * FROM Transactions WHERE TransactionId = @TransactionId;";
            using var command = new SqliteCommand(sql, connection);
            command.Parameters.AddWithValue("@TransactionId", id.ToString());

            using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
            {
                return null;
            }

            string idempotencyKey = reader.GetString(reader.GetOrdinal("IdempotencyKey"));
            var tx = new KioskTransaction(id, idempotencyKey);
            MapReaderToTransaction(reader, tx);
            return tx;
        }

        public async Task<KioskTransaction?> GetByIdempotencyKeyAsync(string idempotencyKey)
        {
            using var connection = await _dbConnectionFactory.CreateAndOpenConnectionAsync();

            string sql = "SELECT * FROM Transactions WHERE IdempotencyKey = @IdempotencyKey;";
            using var command = new SqliteCommand(sql, connection);
            command.Parameters.AddWithValue("@IdempotencyKey", idempotencyKey);

            using var reader = await command.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
            {
                return null;
            }

            string txIdStr = reader.GetString(reader.GetOrdinal("TransactionId"));
            var id = new TransactionId(Guid.Parse(txIdStr));
            var tx = new KioskTransaction(id, idempotencyKey);
            MapReaderToTransaction(reader, tx);
            return tx;
        }

        public async Task<List<KioskTransaction>> GetPendingTransactionsAsync()
        {
            using var connection = await _dbConnectionFactory.CreateAndOpenConnectionAsync();

            string sql = "SELECT * FROM Transactions WHERE CurrentState NOT IN ('Completed', 'Failed', 'ManualReview');";
            using var command = new SqliteCommand(sql, connection);

            var list = new List<KioskTransaction>();
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                string txIdStr = reader.GetString(reader.GetOrdinal("TransactionId"));
                string idempotencyKey = reader.GetString(reader.GetOrdinal("IdempotencyKey"));
                var id = new TransactionId(Guid.Parse(txIdStr));
                var tx = new KioskTransaction(id, idempotencyKey);
                MapReaderToTransaction(reader, tx);
                list.Add(tx);
            }
            return list;
        }

        private void MapReaderToTransaction(SqliteDataReader reader, KioskTransaction tx)
        {
            string stateStr = reader.GetString(reader.GetOrdinal("CurrentState"));
            if (Enum.TryParse<KioskTransactionState>(stateStr, out var state))
            {
                var cardHashIdx = reader.GetOrdinal("CardRefHash");
                CardReference? cardRef = null;
                if (!reader.IsDBNull(cardHashIdx))
                {
                    string cardHash = reader.GetString(cardHashIdx);
                    string cardMasked = reader.GetString(reader.GetOrdinal("CardRefMasked"));
                    cardRef = CardReference.Restore(cardHash, cardMasked);
                }

                Money? amount = null;
                var amountIdx = reader.GetOrdinal("AmountMinor");
                if (!reader.IsDBNull(amountIdx))
                {
                    long amtMinor = reader.GetInt64(amountIdx);
                    string currency = reader.GetString(reader.GetOrdinal("Currency"));
                    amount = new Money(amtMinor, currency);
                }

                string? posRef = reader.IsDBNull(reader.GetOrdinal("PosVendorReference")) ? null : reader.GetString(reader.GetOrdinal("PosVendorReference"));
                string? loadRef = reader.IsDBNull(reader.GetOrdinal("LoadVendorReference")) ? null : reader.GetString(reader.GetOrdinal("LoadVendorReference"));
                string? approvalCode = reader.IsDBNull(reader.GetOrdinal("PosApprovalCode")) ? null : reader.GetString(reader.GetOrdinal("PosApprovalCode"));
                string? responseCode = reader.IsDBNull(reader.GetOrdinal("ResponseCode")) ? null : reader.GetString(reader.GetOrdinal("ResponseCode"));
                string? errorMsg = reader.IsDBNull(reader.GetOrdinal("ErrorMessage")) ? null : reader.GetString(reader.GetOrdinal("ErrorMessage"));
                int retryCount = reader.GetInt32(reader.GetOrdinal("RetryCount"));
                long prevBal = reader.GetInt64(reader.GetOrdinal("PreviousBalanceMinor"));
                long newBal = reader.GetInt64(reader.GetOrdinal("NewBalanceMinor"));

                tx.LoadProperties(state, cardRef, amount, posRef, loadRef, approvalCode, responseCode, errorMsg, retryCount, prevBal, newBal);
            }
        }

        public async Task SaveAsync(KioskTransaction tx, string? eventReason = null)
        {
            using var connection = await _dbConnectionFactory.CreateAndOpenConnectionAsync();

            using var dbTransaction = connection.BeginTransaction();
            try
            {
                // Retrieve current version or check insert
                string checkSql = "SELECT RowVersion FROM Transactions WHERE TransactionId = @TransactionId;";
                using var checkCmd = new SqliteCommand(checkSql, connection, dbTransaction);
                checkCmd.Parameters.AddWithValue("@TransactionId", tx.Id.ToString());
                var currentVersionObj = await checkCmd.ExecuteScalarAsync();

                if (currentVersionObj == null)
                {
                    // INSERT
                    string insertSql = @"
                        INSERT INTO Transactions (
                            TransactionId, IdempotencyKey, CardRefHash, CardRefMasked, AmountMinor, Currency,
                            CurrentState, PosVendorReference, LoadVendorReference, PosApprovalCode, ResponseCode,
                            ErrorMessage, RetryCount, RowVersion, PreviousBalanceMinor, NewBalanceMinor,
                            CreatedAtUtc, LastModifiedAtUtc
                        ) VALUES (
                            @TransactionId, @IdempotencyKey, @CardRefHash, @CardRefMasked, @AmountMinor, @Currency,
                            @CurrentState, @PosVendorReference, @LoadVendorReference, @PosApprovalCode, @ResponseCode,
                            @ErrorMessage, @RetryCount, 1, @PreviousBalanceMinor, @NewBalanceMinor,
                            @CreatedAtUtc, @LastModifiedAtUtc
                        );";

                    using var insertCmd = new SqliteCommand(insertSql, connection, dbTransaction);
                    AddParameters(insertCmd, tx);
                    await insertCmd.ExecuteNonQueryAsync();
                }
                else
                {
                    // UPDATE with Optimistic Concurrency Check
                    long currentVersion = (long)currentVersionObj;
                    string updateSql = @"
                        UPDATE Transactions SET
                            CurrentState = @CurrentState,
                            PosVendorReference = @PosVendorReference,
                            LoadVendorReference = @LoadVendorReference,
                            PosApprovalCode = @PosApprovalCode,
                            ResponseCode = @ResponseCode,
                            ErrorMessage = @ErrorMessage,
                            RetryCount = @RetryCount,
                            RowVersion = RowVersion + 1,
                            PreviousBalanceMinor = @PreviousBalanceMinor,
                            NewBalanceMinor = @NewBalanceMinor,
                            LastModifiedAtUtc = @LastModifiedAtUtc
                        WHERE TransactionId = @TransactionId AND RowVersion = @ExpectedRowVersion;";

                    using var updateCmd = new SqliteCommand(updateSql, connection, dbTransaction);
                    AddParameters(updateCmd, tx);
                    updateCmd.Parameters.AddWithValue("@ExpectedRowVersion", currentVersion);

                    int rowsAffected = await updateCmd.ExecuteNonQueryAsync();
                    if (rowsAffected == 0)
                    {
                        throw new DbUpdateConcurrencyException("Optimistic concurrency violation: The transaction row version has changed or was deleted.");
                    }
                }

                // Add Append-only Event
                string eventSql = @"
                    INSERT INTO TransactionEvents (TransactionId, State, Timestamp, Reason)
                    VALUES (@TransactionId, @State, @Timestamp, @Reason);";
                using var eventCmd = new SqliteCommand(eventSql, connection, dbTransaction);
                eventCmd.Parameters.AddWithValue("@TransactionId", tx.Id.ToString());
                eventCmd.Parameters.AddWithValue("@State", tx.State.ToString());
                eventCmd.Parameters.AddWithValue("@Timestamp", DateTime.UtcNow.ToString("o"));
                eventCmd.Parameters.AddWithValue("@Reason", eventReason ?? (object)DBNull.Value);
                await eventCmd.ExecuteNonQueryAsync();

                // If terminal state, enqueue Outbox Alert message
                if (tx.State == KioskTransactionState.Completed || tx.State == KioskTransactionState.Failed || tx.State == KioskTransactionState.ManualReview)
                {
                    string outboxSql = @"
                        INSERT INTO OutboxMessages (MessageId, MessageType, Payload, CreatedAt)
                        VALUES (@MessageId, @MessageType, @Payload, @CreatedAt);";
                    using var outboxCmd = new SqliteCommand(outboxSql, connection, dbTransaction);
                    outboxCmd.Parameters.AddWithValue("@MessageId", Guid.NewGuid().ToString());
                    outboxCmd.Parameters.AddWithValue("@MessageType", "TransactionAlert");
                    
                    var payload = JsonSerializer.Serialize(new {
                        TransactionId = tx.Id.Value,
                        State = tx.State.ToString(),
                        Amount = tx.Amount?.AmountMinor ?? 0,
                        CardHash = tx.CardRef?.Hash,
                        Error = tx.ErrorMessage
                    });
                    outboxCmd.Parameters.AddWithValue("@Payload", payload);
                    outboxCmd.Parameters.AddWithValue("@CreatedAt", DateTime.UtcNow.ToString("o"));
                    await outboxCmd.ExecuteNonQueryAsync();
                }

                await dbTransaction.CommitAsync();
            }
            catch
            {
                await dbTransaction.RollbackAsync();
                throw;
            }
        }

        private void AddParameters(SqliteCommand cmd, KioskTransaction tx)
        {
            cmd.Parameters.AddWithValue("@TransactionId", tx.Id.ToString());
            cmd.Parameters.AddWithValue("@IdempotencyKey", tx.IdempotencyKey);
            cmd.Parameters.AddWithValue("@CardRefHash", tx.CardRef?.Hash ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@CardRefMasked", tx.CardRef?.Masked ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@AmountMinor", tx.Amount?.AmountMinor ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@Currency", tx.Amount?.Currency ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@CurrentState", tx.State.ToString());
            cmd.Parameters.AddWithValue("@PosVendorReference", tx.PosVendorReference ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@LoadVendorReference", tx.LoadVendorReference ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@PosApprovalCode", tx.PosApprovalCode ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@ResponseCode", tx.ResponseCode ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@ErrorMessage", tx.ErrorMessage ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@RetryCount", tx.RetryCount);
            cmd.Parameters.AddWithValue("@PreviousBalanceMinor", tx.PreviousBalanceMinor);
            cmd.Parameters.AddWithValue("@NewBalanceMinor", tx.NewBalanceMinor);
            cmd.Parameters.AddWithValue("@CreatedAtUtc", tx.CreatedAtUtc.ToString("o"));
            cmd.Parameters.AddWithValue("@LastModifiedAtUtc", tx.LastModifiedAtUtc.ToString("o"));
        }

        public async Task<List<KioskTransaction>> GetTransactionsByDateAsync(DateTime date)
        {
            using var connection = await _dbConnectionFactory.CreateAndOpenConnectionAsync();

            string sql = "SELECT * FROM Transactions;";
            using var command = new SqliteCommand(sql, connection);

            var list = new List<KioskTransaction>();
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                string txIdStr = reader.GetString(reader.GetOrdinal("TransactionId"));
                string idempotencyKey = reader.GetString(reader.GetOrdinal("IdempotencyKey"));
                var id = new TransactionId(Guid.Parse(txIdStr));
                var tx = new KioskTransaction(id, idempotencyKey);
                MapReaderToTransaction(reader, tx);

                if (tx.CreatedAtUtc.Date == date.Date)
                {
                    list.Add(tx);
                }
            }
            return list;
        }
    }

    public class DbUpdateConcurrencyException : Exception
    {
        public DbUpdateConcurrencyException(string message) : base(message) { }
    }
}
