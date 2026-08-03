using System;
using System.Data;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using IzbanKiosk.Domain;
using IzbanKiosk.Application.Repositories;

namespace IzbanKiosk.Infrastructure.Repositories
{
    public class SqliteReceiptRepository : IReceiptRepository
    {
        private readonly DbConnectionFactory _dbConnectionFactory;

        public SqliteReceiptRepository(DbConnectionFactory dbConnectionFactory)
        {
            _dbConnectionFactory = dbConnectionFactory;
        }

        public async Task<ReceiptRecord?> GetByTransactionIdAsync(string transactionId)
        {
            using var connection = await _dbConnectionFactory.CreateAndOpenConnectionAsync();

            string sql = "SELECT * FROM ReceiptRecords WHERE TransactionId = @TransactionId;";
            using var command = new SqliteCommand(sql, connection);
            command.Parameters.AddWithValue("@TransactionId", transactionId);

            using var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                return MapReaderToReceipt(reader);
            }

            return null;
        }

        public async Task SaveAsync(ReceiptRecord record)
        {
            using var connection = await _dbConnectionFactory.CreateAndOpenConnectionAsync();
            using var dbTransaction = connection.BeginTransaction();

            try
            {
                // Check if record exists
                string checkSql = "SELECT RowVersion FROM ReceiptRecords WHERE ReceiptId = @ReceiptId;";
                using var checkCmd = new SqliteCommand(checkSql, connection, dbTransaction);
                checkCmd.Parameters.AddWithValue("@ReceiptId", record.ReceiptId);

                var existingRowVersionObj = await checkCmd.ExecuteScalarAsync();

                if (existingRowVersionObj == null)
                {
                    // INSERT
                    string insertSql = @"
                        INSERT INTO ReceiptRecords (
                            ReceiptId, TransactionId, Decision, Status, RequestedAtUtc,
                            PrintStartedAtUtc, PrintedAtUtc, PrinterJobReference, ErrorCode, ErrorMessage,
                            RetryCount, RowVersion, CreatedAtUtc, LastModifiedAtUtc
                        ) VALUES (
                            @ReceiptId, @TransactionId, @Decision, @Status, @RequestedAtUtc,
                            @PrintStartedAtUtc, @PrintedAtUtc, @PrinterJobReference, @ErrorCode, @ErrorMessage,
                            @RetryCount, @RowVersion, @CreatedAtUtc, @LastModifiedAtUtc
                        );";

                    using var insertCmd = new SqliteCommand(insertSql, connection, dbTransaction);
                    MapRecordToParameters(insertCmd, record);
                    await insertCmd.ExecuteNonQueryAsync();
                }
                else
                {
                    // UPDATE with optimistic concurrency
                    int currentDbVersion = Convert.ToInt32(existingRowVersionObj);
                    if (currentDbVersion != record.RowVersion)
                    {
                        throw new DbUpdateConcurrencyException("Receipt record concurrency mismatch.");
                    }

                    int newVersion = record.RowVersion + 1;

                    string updateSql = @"
                        UPDATE ReceiptRecords SET
                            TransactionId = @TransactionId,
                            Decision = @Decision,
                            Status = @Status,
                            RequestedAtUtc = @RequestedAtUtc,
                            PrintStartedAtUtc = @PrintStartedAtUtc,
                            PrintedAtUtc = @PrintedAtUtc,
                            PrinterJobReference = @PrinterJobReference,
                            ErrorCode = @ErrorCode,
                            ErrorMessage = @ErrorMessage,
                            RetryCount = @RetryCount,
                            RowVersion = @NewRowVersion,
                            LastModifiedAtUtc = @LastModifiedAtUtc
                        WHERE ReceiptId = @ReceiptId AND RowVersion = @ExpectedRowVersion;";

                    using var updateCmd = new SqliteCommand(updateSql, connection, dbTransaction);
                    MapRecordToParameters(updateCmd, record);
                    updateCmd.Parameters.AddWithValue("@NewRowVersion", newVersion);
                    updateCmd.Parameters.AddWithValue("@ExpectedRowVersion", record.RowVersion);

                    int rowsAffected = await updateCmd.ExecuteNonQueryAsync();
                    if (rowsAffected == 0)
                    {
                        throw new DbUpdateConcurrencyException("Concurrency check failed for ReceiptRecord update.");
                    }

                    record.IncrementRowVersion();
                }

                await dbTransaction.CommitAsync();
            }
            catch
            {
                await dbTransaction.RollbackAsync();
                throw;
            }
        }

        private ReceiptRecord MapReaderToReceipt(SqliteDataReader reader)
        {
            var receiptId = reader.GetString(reader.GetOrdinal("ReceiptId"));
            var transactionId = reader.GetString(reader.GetOrdinal("TransactionId"));
            var decision = reader.GetString(reader.GetOrdinal("Decision"));
            var status = (ReceiptStatus)Enum.Parse(typeof(ReceiptStatus), reader.GetString(reader.GetOrdinal("Status")));
            
            var reqAtOrdinal = reader.GetOrdinal("RequestedAtUtc");
            DateTime? requestedAtUtc = reader.IsDBNull(reqAtOrdinal) 
                ? (DateTime?)null 
                : DateTime.Parse(reader.GetString(reqAtOrdinal));

            var prStartOrdinal = reader.GetOrdinal("PrintStartedAtUtc");
            DateTime? printStartedAtUtc = reader.IsDBNull(prStartOrdinal) 
                ? (DateTime?)null 
                : DateTime.Parse(reader.GetString(prStartOrdinal));

            var prEndOrdinal = reader.GetOrdinal("PrintedAtUtc");
            DateTime? printedAtUtc = reader.IsDBNull(prEndOrdinal) 
                ? (DateTime?)null 
                : DateTime.Parse(reader.GetString(prEndOrdinal));

            var jobRefOrdinal = reader.GetOrdinal("PrinterJobReference");
            var printerJobReference = reader.IsDBNull(jobRefOrdinal) ? null : reader.GetString(jobRefOrdinal);

            var errCodeOrdinal = reader.GetOrdinal("ErrorCode");
            var errorCode = reader.IsDBNull(errCodeOrdinal) ? null : reader.GetString(errCodeOrdinal);

            var errMsgOrdinal = reader.GetOrdinal("ErrorMessage");
            var errorMessage = reader.IsDBNull(errMsgOrdinal) ? null : reader.GetString(errMsgOrdinal);

            var retryCount = reader.GetInt32(reader.GetOrdinal("RetryCount"));
            var rowVersion = reader.GetInt32(reader.GetOrdinal("RowVersion"));
            var createdAtUtc = DateTime.Parse(reader.GetString(reader.GetOrdinal("CreatedAtUtc")));
            var lastModifiedAtUtc = DateTime.Parse(reader.GetString(reader.GetOrdinal("LastModifiedAtUtc")));

            return new ReceiptRecord(
                receiptId, transactionId, decision, status, requestedAtUtc,
                printStartedAtUtc, printedAtUtc, printerJobReference, errorCode, errorMessage,
                retryCount, rowVersion, createdAtUtc, lastModifiedAtUtc
            );
        }

        private void MapRecordToParameters(SqliteCommand cmd, ReceiptRecord record)
        {
            cmd.Parameters.AddWithValue("@ReceiptId", record.ReceiptId);
            cmd.Parameters.AddWithValue("@TransactionId", record.TransactionId);
            cmd.Parameters.AddWithValue("@Decision", record.Decision);
            cmd.Parameters.AddWithValue("@Status", record.Status.ToString());
            cmd.Parameters.AddWithValue("@RequestedAtUtc", record.RequestedAtUtc?.ToString("o") ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@PrintStartedAtUtc", record.PrintStartedAtUtc?.ToString("o") ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@PrintedAtUtc", record.PrintedAtUtc?.ToString("o") ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@PrinterJobReference", record.PrinterJobReference ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@ErrorCode", record.ErrorCode ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@ErrorMessage", record.ErrorMessage ?? (object)DBNull.Value);
            cmd.Parameters.AddWithValue("@RetryCount", record.RetryCount);
            cmd.Parameters.AddWithValue("@RowVersion", record.RowVersion);
            cmd.Parameters.AddWithValue("@CreatedAtUtc", record.CreatedAtUtc.ToString("o"));
            cmd.Parameters.AddWithValue("@LastModifiedAtUtc", record.LastModifiedAtUtc.ToString("o"));
        }
    }
}
