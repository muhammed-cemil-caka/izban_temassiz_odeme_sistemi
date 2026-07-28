using System;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace IzbanKioskApp.Core
{
    public class DatabaseService
    {
        private const string DbPath = "Data Source=kiosk_transactions.db";

        private static async Task<SqliteConnection> GetConnectionAsync()
        {
            var connection = new SqliteConnection(DbPath);
            await connection.OpenAsync();
            
            // WAL Modu (Write-Ahead Logging) ve Eşzamanlılık Ayarları
            using var pragmaCmd = new SqliteCommand(
                "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;", 
                connection);
            await pragmaCmd.ExecuteNonQueryAsync();
            
            return connection;
        }

        public static async Task InitializeDatabaseAsync()
        {
            try
            {
                using var connection = await GetConnectionAsync();

                // Eskiden kalma INTEGER PRIMARY KEY içeren tablo varsa, yeni şemaya (TEXT GUID) temiz geçiş sağlamak için tabloyu yeniden oluştur
                bool dropTableNeeded = false;
                var checkCmdText = "PRAGMA table_info(Transactions);";
                using (var checkCmd = new SqliteCommand(checkCmdText, connection))
                {
                    using (var reader = await checkCmd.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            var colName = reader["name"]?.ToString();
                            var colType = reader["type"]?.ToString();
                            if (colName == "Id" && colType == "INTEGER")
                            {
                                dropTableNeeded = true;
                                break;
                            }
                        }
                    }
                }

                if (dropTableNeeded)
                {
                    Console.WriteLine("[DATABASE] Eski şema (INTEGER PRIMARY KEY) tespit edildi. Tablo dönüştürülüyor...");
                    using var dropCmd = new SqliteCommand("DROP TABLE Transactions;", connection);
                    await dropCmd.ExecuteNonQueryAsync();
                }

                var tableCommand = @"
                    CREATE TABLE IF NOT EXISTS Transactions (
                        Id TEXT PRIMARY KEY,
                        CardUid TEXT NOT NULL,
                        Amount DECIMAL NOT NULL,
                        ApprovalCode TEXT NOT NULL,
                        Status TEXT NOT NULL,
                        CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP
                    );";

                using var command = new SqliteCommand(tableCommand, connection);
                await command.ExecuteNonQueryAsync();
                Console.WriteLine("[DATABASE] Yerel SQLite veritabanı hazır (WAL Modu aktif).");

                // Arka plan temizlik servisini başlat
                StartPurgeService();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DATABASE ERROR] Başlatılamadı: {ex.Message}");
            }
        }

        public static async Task LogTransactionAsync(string cardUid, decimal amount, string approvalCode, string status)
        {
            try
            {
                // KVKK & PCI-DSS Güvenlik: Kart Uid'sine maskeleme uygula
                string maskedCardUid = MaskCardUid(cardUid);

                using var connection = await GetConnectionAsync();

                var insertCommand = @"
                    INSERT INTO Transactions (Id, CardUid, Amount, ApprovalCode, Status) 
                    VALUES (@id, @cardUid, @amount, @approvalCode, @status);";

                using var command = new SqliteCommand(insertCommand, connection);
                command.Parameters.Add("@id", SqliteType.Text).Value = Guid.NewGuid().ToString();
                command.Parameters.Add("@cardUid", SqliteType.Text).Value = maskedCardUid;
                command.Parameters.Add("@amount", SqliteType.Text).Value = amount.ToString(System.Globalization.CultureInfo.InvariantCulture);
                command.Parameters.Add("@approvalCode", SqliteType.Text).Value = approvalCode;
                command.Parameters.Add("@status", SqliteType.Text).Value = status;

                await command.ExecuteNonQueryAsync();
                Console.WriteLine($"[DATABASE LOG] İşlem kaydedildi: {maskedCardUid} | {amount} TL | Kod: {approvalCode}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DATABASE HATA] Log yazılamadı: {ex.Message}");
                throw;
            }
        }

        public static async Task PurgeOldTransactionsAsync()
        {
            try
            {
                using var connection = await GetConnectionAsync();

                // 30 günden eski olan başarılı işlem loglarını temizle (KVKK ve Depolama Yönetimi)
                var deleteQuery = "DELETE FROM Transactions WHERE CreatedAt < datetime('now', '-30 days') AND Status = 'SUCCESS';";
                using var command = new SqliteCommand(deleteQuery, connection);
                int rowsDeleted = await command.ExecuteNonQueryAsync();
                
                if (rowsDeleted > 0)
                {
                    Console.WriteLine($"[DATABASE SERVICE] Otomatik Rotasyon: 30 günden eski {rowsDeleted} adet başarılı işlem temizlendi.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DATABASE PURGE ERROR] Temizlik çalıştırılamadı: {ex.Message}");
            }
        }

        private static void StartPurgeService()
        {
            // Arka planda çalışan temizlik servisi (24 saatte bir çalışır)
            Task.Run(async () =>
            {
                while (true)
                {
                    await PurgeOldTransactionsAsync();
                    await Task.Delay(TimeSpan.FromHours(24));
                }
            });
        }

        public static string MaskCardUid(string cardUid)
        {
            if (string.IsNullOrEmpty(cardUid)) return "UNKNOWN-CARD";
            if (cardUid.Length <= 8) return cardUid; // Zaten kısa/simüle (Örn: 35-IZM-9921)
            // KVKK & PCI-DSS gereğince donanımdan gelen ham kart UID'sini maskele
            return cardUid.Substring(0, 4) + "••••" + cardUid.Substring(cardUid.Length - 4);
        }
    }
}