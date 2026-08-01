using System;
using System.Threading;
using System.Threading.Tasks;
using IzbanKiosk.Domain;

namespace IzbanKiosk.Application.Hardware.Nfc
{
    public interface INfcReader
    {
        Task InitializeAsync(CancellationToken cancellationToken);
        Task<bool> ConnectAsync(CancellationToken cancellationToken);
        Task<bool> HealthCheckAsync(CancellationToken cancellationToken);
        
        Task<CardReference?> WaitForCardAsync(
            TransactionId transactionId, 
            TimeSpan timeout, 
            CancellationToken cancellationToken);
            
        Task<bool> ValidateCardAsync(
            TransactionId transactionId, 
            CardReference cardRef, 
            CancellationToken cancellationToken);
            
        Task<CardSnapshot> ReadCardSnapshotAsync(
            TransactionId transactionId, 
            CardReference cardRef, 
            CancellationToken cancellationToken);
            
        Task<bool> LoadAmountAsync(
            TransactionId transactionId, 
            string idempotencyKey, 
            CardReference cardRef, 
            Money amount, 
            CancellationToken cancellationToken, 
            Guid correlationId);
            
        Task<bool> QueryLoadTransactionAsync(
            TransactionId transactionId, 
            string loadVendorReference, 
            CancellationToken cancellationToken, 
            Guid correlationId);
            
        Task<bool> VerifyLoadAsync(
            TransactionId transactionId, 
            CardReference cardRef, 
            Money amount, 
            CancellationToken cancellationToken);
            
        Task<long> ReadVerifiedBalanceAsync(
            TransactionId transactionId, 
            CardReference cardRef, 
            CancellationToken cancellationToken);
            
        Task WaitForCardRemovalAsync(
            TransactionId transactionId, 
            CardReference cardRef, 
            TimeSpan timeout, 
            CancellationToken cancellationToken);
    }
}
