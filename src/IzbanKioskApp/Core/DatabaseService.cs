using System;
using Microsoft.Data.Sqlite;

namespace IzbanKioskApp.Core
{
    public class DatabaseService
    {
        private const string DbPath = "Data Source=kiosk_transactions.db";

        public static void InitializeDatabase()
        {
            using var connection = new SqliteConnection(DbPath);
            connection.Open();

            var tableCommand = @"
                CREATE TABLE IF NOT EXISTS Transactions (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    CardUid TEXT NOT NULL,
                    Amount DECIMAL NOT NULL,
                    ApprovalCode TEXT NOT NULL,
                    Status TEXT NOT NULL,
                    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP
                );";

            using var command = new SqliteCommand(tableCommand, connection);
            command.ExecuteNonQuery();
            Console.WriteLine("[DATABASE] Yerel SQLite veritabanı hazır (kiosk_transactions.db).");
        }

        public static void LogTransaction(string cardUid, decimal amount, string approvalCode, string status)
        {
            try
            {
                using var connection = new SqliteConnection(DbPath);
                connection.Open();

                var insertCommand = @"
                    INSERT INTO Transactions (CardUid, Amount, ApprovalCode, Status) 
                    VALUES (@cardUid, @amount, @approvalCode, @status);";

                using var command = new SqliteCommand(insertCommand, connection);
                command.Parameters.AddWithValue("@cardUid", cardUid);
                command.Parameters.AddWithValue("@amount", amount);
                command.Parameters.AddWithValue("@approvalCode", approvalCode);
                command.Parameters.AddWithValue("@status", status);

                command.ExecuteNonQuery();
                Console.WriteLine($"[DATABASE LOG] İşlem kaydedildi: {cardUid} | {amount} TL | Kod: {approvalCode}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DATABASE HATA] Log yazılamadı: {ex.Message}");
            }
        }
    }
}