using System.Threading.Tasks;
using IzbanKiosk.Domain;

namespace IzbanKiosk.Application.Repositories
{
    public interface IReceiptRepository
    {
        Task<ReceiptRecord> GetByTransactionIdAsync(string transactionId);
        Task SaveAsync(ReceiptRecord record);
    }
}
