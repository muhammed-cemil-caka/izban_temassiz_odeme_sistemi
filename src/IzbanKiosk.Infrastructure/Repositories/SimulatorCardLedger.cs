using System;
using System.Data;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using IzbanKiosk.Application.Hardware.Balance;

namespace IzbanKiosk.Infrastructure.Repositories
{
    public class SimulatorCardLedger : ISimulatorCardLedger
    {
        private readonly DbConnectionFactory _dbFactory;

        public SimulatorCardLedger(DbConnectionFactory dbFactory)
        {
            _dbFactory = dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
        }

        public async Task<SimulatorCardRecord> GetOrCreateCardAsync(string cardRef, long initialBalanceMinor = 6250)
        {
            if (string.IsNullOrWhiteSpace(cardRef))
            {
                throw new ArgumentException("CardReference key cannot be null or empty.", nameof(cardRef));
            }

            using var connection = await _dbFactory.CreateAndOpenConnectionAsync();
            
            const string selectSql = @"
                SELECT CardRef, BalanceMinor, Currency, CardTransactionCounter, LastLoadReference, UpdatedAtUtc, RowVersion 
                FROM SimulatorCards 
                WHERE CardRef = @CardRef LIMIT 1;";

            using (var selectCmd = new SqliteCommand(selectSql, connection))
            {
                selectCmd.Parameters.AddWithValue("@CardRef", cardRef);
                using var reader = await selectCmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    return new SimulatorCardRecord(
                        cardRef: reader.GetString(0),
                        balanceMinor: reader.GetInt64(1),
                        currency: reader.GetString(2),
                        cardTransactionCounter: reader.GetInt32(3),
                        lastLoadReference: reader.IsDBNull(4) ? null : reader.GetString(4),
                        updatedAtUtc: DateTime.Parse(reader.GetString(5)),
                        rowVersion: reader.GetInt32(6)
                    );
                }
            }

            // Insert new card
            const string insertSql = @"
                INSERT INTO SimulatorCards (CardRef, BalanceMinor, Currency, CardTransactionCounter, LastLoadReference, UpdatedAtUtc, RowVersion)
                VALUES (@CardRef, @BalanceMinor, 'TRY', 42, NULL, @UpdatedAtUtc, 1);";

            var nowStr = DateTime.UtcNow.ToString("O");
            using (var insertCmd = new SqliteCommand(insertSql, connection))
            {
                insertCmd.Parameters.AddWithValue("@CardRef", cardRef);
                insertCmd.Parameters.AddWithValue("@BalanceMinor", initialBalanceMinor);
                insertCmd.Parameters.AddWithValue("@UpdatedAtUtc", nowStr);
                try
                {
                    await insertCmd.ExecuteNonQueryAsync();
                }
                catch (SqliteException ex) when (ex.SqliteErrorCode == 19) // Constraint validation (already exists due to race condition)
                {
                    // Re-read
                    using var selectCmd = new SqliteCommand(selectSql, connection);
                    selectCmd.Parameters.AddWithValue("@CardRef", cardRef);
                    using var reader = await selectCmd.ExecuteReaderAsync();
                    if (await reader.ReadAsync())
                    {
                        return new SimulatorCardRecord(
                            cardRef: reader.GetString(0),
                            balanceMinor: reader.GetInt64(1),
                            currency: reader.GetString(2),
                            cardTransactionCounter: reader.GetInt32(3),
                            lastLoadReference: reader.IsDBNull(4) ? null : reader.GetString(4),
                            updatedAtUtc: DateTime.Parse(reader.GetString(5)),
                            rowVersion: reader.GetInt32(6)
                        );
                    }
                }
            }

            return new SimulatorCardRecord(cardRef, initialBalanceMinor, "TRY", 42, null, DateTime.UtcNow, 1);
        }

        public async Task<bool> UpdateBalanceAsync(
            string cardRef, 
            long expectedBalanceMinor, 
            long newBalanceMinor, 
            string? loadReference, 
            int transactionCounterIncrement)
        {
            if (string.IsNullOrWhiteSpace(cardRef))
            {
                throw new ArgumentException("CardReference key cannot be null or empty.", nameof(cardRef));
            }

            using var connection = await _dbFactory.CreateAndOpenConnectionAsync();
            using var transaction = connection.BeginTransaction();

            try
            {
                // 1. Get current card record to check RowVersion and idempotency
                const string selectSql = @"
                    SELECT BalanceMinor, CardTransactionCounter, LastLoadReference, RowVersion 
                    FROM SimulatorCards 
                    WHERE CardRef = @CardRef LIMIT 1;";

                long currentBalance = 0;
                int currentCounter = 0;
                string? lastLoadRef = null;
                int currentRowVersion = 0;
                bool found = false;

                using (var selectCmd = new SqliteCommand(selectSql, connection, transaction))
                {
                    selectCmd.Parameters.AddWithValue("@CardRef", cardRef);
                    using var reader = await selectCmd.ExecuteReaderAsync();
                    if (await reader.ReadAsync())
                    {
                        currentBalance = reader.GetInt64(0);
                        currentCounter = reader.GetInt32(1);
                        lastLoadRef = reader.IsDBNull(2) ? null : reader.GetString(2);
                        currentRowVersion = reader.GetInt32(3);
                        found = true;
                    }
                }

                if (!found)
                {
                    return false; // Card must be created first
                }

                // Idempotency: If this load reference is already recorded, return true directly (do not double add)
                if (loadReference != null && lastLoadRef == loadReference)
                {
                    await transaction.CommitAsync();
                    return true;
                }

                if (currentBalance != expectedBalanceMinor)
                {
                    // Concurrency mismatch on balance
                    return false;
                }

                // 2. Perform optimistic update
                const string updateSql = @"
                    UPDATE SimulatorCards 
                    SET BalanceMinor = @NewBalance, 
                        CardTransactionCounter = CardTransactionCounter + @CounterInc, 
                        LastLoadReference = @LoadRef, 
                        UpdatedAtUtc = @UpdatedAtUtc, 
                        RowVersion = RowVersion + 1 
                    WHERE CardRef = @CardRef AND RowVersion = @RowVersion;";

                var nowStr = DateTime.UtcNow.ToString("O");
                using (var updateCmd = new SqliteCommand(updateSql, connection, transaction))
                {
                    updateCmd.Parameters.AddWithValue("@NewBalance", newBalanceMinor);
                    updateCmd.Parameters.AddWithValue("@CounterInc", transactionCounterIncrement);
                    updateCmd.Parameters.AddWithValue("@LoadRef", (object?)loadReference ?? DBNull.Value);
                    updateCmd.Parameters.AddWithValue("@UpdatedAtUtc", nowStr);
                    updateCmd.Parameters.AddWithValue("@CardRef", cardRef);
                    updateCmd.Parameters.AddWithValue("@RowVersion", currentRowVersion);

                    int rowsAffected = await updateCmd.ExecuteNonQueryAsync();
                    if (rowsAffected == 0)
                    {
                        // Concurrency collision
                        return false;
                    }
                }

                await transaction.CommitAsync();
                return true;
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task<bool> IsLoadReferenceProcessedAsync(string loadReference)
        {
            if (string.IsNullOrWhiteSpace(loadReference))
            {
                return false;
            }

            using var connection = await _dbFactory.CreateAndOpenConnectionAsync();
            const string sql = "SELECT COUNT(*) FROM SimulatorCards WHERE LastLoadReference = @LoadRef LIMIT 1;";
            using var command = new SqliteCommand(sql, connection);
            command.Parameters.AddWithValue("@LoadRef", loadReference);
            var count = (long)(await command.ExecuteScalarAsync() ?? 0L);
            return count > 0;
        }

        public async Task ResetAllAsync()
        {
            using var connection = await _dbFactory.CreateAndOpenConnectionAsync();
            const string sql = "DELETE FROM SimulatorCards;";
            using var command = new SqliteCommand(sql, connection);
            await command.ExecuteNonQueryAsync();
        }
    }
}
