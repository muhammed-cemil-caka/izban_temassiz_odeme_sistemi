using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace IzbanKiosk.Infrastructure
{
    public class DbConnectionFactory
    {
        private readonly string _connectionString;

        public DbConnectionFactory(string? customDbPath = null)
        {
            string dbPath;
            if (!string.IsNullOrEmpty(customDbPath))
            {
                dbPath = customDbPath;
            }
            else
            {
                string folder;
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    folder = @"C:\ProgramData\IzbanKiosk\Data";
                }
                else
                {
                    folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".izbankiosk", "data");
                }

                Directory.CreateDirectory(folder);
                dbPath = Path.Combine(folder, "kiosk_transactions.db");
            }

            _connectionString = $"Data Source={dbPath};Cache=Shared;";
        }

        public SqliteConnection CreateConnection()
        {
            return new SqliteConnection(_connectionString);
        }

        public async Task<SqliteConnection> CreateAndOpenConnectionAsync()
        {
            var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            using (var pragmaCmd = new SqliteCommand(
                "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;", 
                connection))
            {
                await pragmaCmd.ExecuteNonQueryAsync();
            }

            return connection;
        }

        public async Task InitializeDatabaseAsync()
        {
            using var connection = await CreateAndOpenConnectionAsync();
            using var transaction = connection.BeginTransaction();
            try
            {
                // 1. SchemaMigrations Table
                await ExecuteCommandAsync(connection, transaction, @"
                    CREATE TABLE IF NOT EXISTS SchemaMigrations (
                        Version INTEGER PRIMARY KEY,
                        AppliedAt TEXT NOT NULL
                    );");

                // 2. Transactions Table
                await ExecuteCommandAsync(connection, transaction, @"
                    CREATE TABLE IF NOT EXISTS Transactions (
                        TransactionId TEXT PRIMARY KEY,
                        IdempotencyKey TEXT NOT NULL UNIQUE,
                        CardRefHash TEXT,
                        CardRefMasked TEXT,
                        AmountMinor INTEGER,
                        Currency TEXT,
                        CurrentState TEXT NOT NULL,
                        PosVendorReference TEXT,
                        LoadVendorReference TEXT,
                        PosApprovalCode TEXT,
                        ResponseCode TEXT,
                        ErrorMessage TEXT,
                        RetryCount INTEGER DEFAULT 0,
                        RowVersion INTEGER DEFAULT 1,
                        PreviousBalanceMinor INTEGER,
                        NewBalanceMinor INTEGER,
                        CreatedAtUtc TEXT NOT NULL,
                        LastModifiedAtUtc TEXT NOT NULL
                    );");

                // 3. TransactionEvents Table (Append-only)
                await ExecuteCommandAsync(connection, transaction, @"
                    CREATE TABLE IF NOT EXISTS TransactionEvents (
                        EventId INTEGER PRIMARY KEY AUTOINCREMENT,
                        TransactionId TEXT NOT NULL,
                        State TEXT NOT NULL,
                        Timestamp TEXT NOT NULL,
                        Reason TEXT
                    );");

                // 4. BalanceSnapshots Table
                await ExecuteCommandAsync(connection, transaction, @"
                    CREATE TABLE IF NOT EXISTS BalanceSnapshots (
                        SnapshotId TEXT PRIMARY KEY,
                        CardRefHash TEXT NOT NULL,
                        BalanceMinor INTEGER NOT NULL,
                        Timestamp TEXT NOT NULL,
                        Source TEXT NOT NULL
                    );");

                // 5. OutboxMessages Table
                await ExecuteCommandAsync(connection, transaction, @"
                    CREATE TABLE IF NOT EXISTS OutboxMessages (
                        MessageId TEXT PRIMARY KEY,
                        MessageType TEXT NOT NULL,
                        Payload TEXT NOT NULL,
                        CreatedAt TEXT NOT NULL,
                        ProcessedAt TEXT
                    );");

                // 6. ReconciliationItems Table
                await ExecuteCommandAsync(connection, transaction, @"
                    CREATE TABLE IF NOT EXISTS ReconciliationItems (
                        TransactionId TEXT PRIMARY KEY,
                        LocalPaymentState TEXT,
                        PosPaymentState TEXT,
                        LocalLoadState TEXT,
                        IzmirimLoadState TEXT,
                        LocalAmountMinor INTEGER,
                        PosAmountMinor INTEGER,
                        IzmirimLoadAmountMinor INTEGER,
                        ReconciliationStatus TEXT NOT NULL,
                        MismatchReason TEXT,
                        LastCheckedAtUtc TEXT NOT NULL,
                        RetryCount INTEGER DEFAULT 0,
                        RequiresManualReview INTEGER DEFAULT 0
                    );");

                // 7. DeviceHealthEvents Table
                await ExecuteCommandAsync(connection, transaction, @"
                    CREATE TABLE IF NOT EXISTS DeviceHealthEvents (
                        EventId INTEGER PRIMARY KEY AUTOINCREMENT,
                        Component TEXT NOT NULL,
                        Status TEXT NOT NULL,
                        Timestamp TEXT NOT NULL
                    );");

                // 8. ReceiptRecords Table
                await ExecuteCommandAsync(connection, transaction, @"
                    CREATE TABLE IF NOT EXISTS ReceiptRecords (
                        ReceiptId TEXT PRIMARY KEY,
                        TransactionId TEXT NOT NULL UNIQUE,
                        Decision TEXT NOT NULL,
                        Status TEXT NOT NULL,
                        RequestedAtUtc TEXT,
                        PrintStartedAtUtc TEXT,
                        PrintedAtUtc TEXT,
                        PrinterJobReference TEXT,
                        ErrorCode TEXT,
                        ErrorMessage TEXT,
                        RetryCount INTEGER NOT NULL DEFAULT 0,
                        RowVersion INTEGER NOT NULL DEFAULT 1,
                        CreatedAtUtc TEXT NOT NULL,
                        LastModifiedAtUtc TEXT NOT NULL
                    );");
 
                // Insert initial schema version if empty
                var checkVersionCmd = new SqliteCommand("SELECT COUNT(*) FROM SchemaMigrations WHERE Version = 2;", connection, transaction);
                var count = (long)(await checkVersionCmd.ExecuteScalarAsync() ?? 0L);
                if (count == 0)
                {
                    await ExecuteCommandAsync(connection, transaction, @"
                        INSERT INTO SchemaMigrations (Version, AppliedAt)
                        VALUES (2, datetime('now'));");
                }

                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        private async Task ExecuteCommandAsync(SqliteConnection connection, SqliteTransaction transaction, string sql)
        {
            using var command = new SqliteCommand(sql, connection, transaction);
            await command.ExecuteNonQueryAsync();
        }
    }
}
