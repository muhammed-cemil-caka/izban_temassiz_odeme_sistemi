using System.Collections.Generic;
using System.Threading.Tasks;
using IzbanKiosk.Domain;

namespace IzbanKiosk.Application.Repositories
{
    public interface ITransactionRepository
    {
        Task<KioskTransaction?> GetByIdAsync(TransactionId id);
        Task SaveAsync(KioskTransaction tx, string? eventReason = null);
        Task<List<KioskTransaction>> GetPendingTransactionsAsync();
        Task<KioskTransaction?> GetByIdempotencyKeyAsync(string idempotencyKey);
        Task<List<KioskTransaction>> GetTransactionsByDateAsync(DateTime date);
    }
}
