using System;
using System.Collections.Generic;

namespace IzbanKiosk.Domain
{
    public class KioskTransaction
    {
        public TransactionId Id { get; }
        public string IdempotencyKey { get; }
        public CardReference? CardRef { get; private set; }
        public Money? Amount { get; private set; }
        public KioskTransactionState State { get; private set; } = KioskTransactionState.Created;
        
        public string? PosVendorReference { get; private set; }
        public string? LoadVendorReference { get; private set; }
        public string? PosApprovalCode { get; private set; }
        public string? ResponseCode { get; private set; }
        public string? ErrorMessage { get; private set; }
        public int RetryCount { get; private set; }
        
        public long PreviousBalanceMinor { get; private set; }
        public long NewBalanceMinor { get; private set; }

        public DateTime CreatedAtUtc { get; } = DateTime.UtcNow;
        public DateTime LastModifiedAtUtc { get; private set; } = DateTime.UtcNow;

        // Custom validation mapping of allowed transitions
        private static readonly Dictionary<KioskTransactionState, HashSet<KioskTransactionState>> AllowedTransitions = new()
        {
            { KioskTransactionState.Created, new() { KioskTransactionState.CardDetected, KioskTransactionState.Failed } },
            
            { KioskTransactionState.CardDetected, new() { KioskTransactionState.CardValidated, KioskTransactionState.Failed, KioskTransactionState.Created } },
            
            { KioskTransactionState.CardValidated, new() { KioskTransactionState.BalanceQueryPending, KioskTransactionState.Failed, KioskTransactionState.Created } },
            
            { KioskTransactionState.BalanceQueryPending, new() { KioskTransactionState.BalanceVerified, KioskTransactionState.BalanceQueryFailed, KioskTransactionState.Failed } },
            
            { KioskTransactionState.BalanceQueryFailed, new() { KioskTransactionState.CardValidated, KioskTransactionState.Failed, KioskTransactionState.Created } },
            
            { KioskTransactionState.BalanceVerified, new() { KioskTransactionState.AmountSelected, KioskTransactionState.Failed, KioskTransactionState.Created } },
            
            { KioskTransactionState.AmountSelected, new() { 
                KioskTransactionState.PaymentPending, 
                KioskTransactionState.PreAuthorizationPending, 
                KioskTransactionState.Failed, 
                KioskTransactionState.Created 
            } },
            
            // Sale flow
            { KioskTransactionState.PaymentPending, new() { 
                KioskTransactionState.PaymentApproved, 
                KioskTransactionState.PaymentDeclined, 
                KioskTransactionState.PaymentCancelled, 
                KioskTransactionState.PaymentOutcomeUnknown, 
                KioskTransactionState.Failed 
            } },
            { KioskTransactionState.PaymentOutcomeUnknown, new() { 
                KioskTransactionState.PaymentApproved, 
                KioskTransactionState.PaymentDeclined, 
                KioskTransactionState.PaymentCancelled,
                KioskTransactionState.ReversalPending,
                KioskTransactionState.ManualReview
            } },
            { KioskTransactionState.PaymentDeclined, new() { KioskTransactionState.Failed } },
            { KioskTransactionState.PaymentCancelled, new() { KioskTransactionState.Failed } },
            { KioskTransactionState.PaymentApproved, new() { KioskTransactionState.LoadPending, KioskTransactionState.ReversalPending, KioskTransactionState.Failed, KioskTransactionState.ManualReview } },

            // PreAuth / Capture flow
            { KioskTransactionState.PreAuthorizationPending, new() { 
                KioskTransactionState.PreAuthorized, 
                KioskTransactionState.PaymentDeclined, 
                KioskTransactionState.PaymentCancelled, 
                KioskTransactionState.PaymentOutcomeUnknown, 
                KioskTransactionState.Failed 
            } },
            { KioskTransactionState.PreAuthorized, new() { KioskTransactionState.LoadPending, KioskTransactionState.ReversalPending, KioskTransactionState.Failed } },
            
            // NFC Card Load
            { KioskTransactionState.LoadPending, new() { 
                KioskTransactionState.LoadVerificationPending, 
                KioskTransactionState.LoadVerificationFailed,
                KioskTransactionState.LoadOutcomeUnknown, 
                KioskTransactionState.ReversalPending, 
                KioskTransactionState.Failed 
            } },
            { KioskTransactionState.LoadOutcomeUnknown, new() { 
                KioskTransactionState.LoadVerificationPending, 
                KioskTransactionState.LoadVerificationFailed,
                KioskTransactionState.ReversalPending,
                KioskTransactionState.ManualReview
            } },
            { KioskTransactionState.LoadVerificationPending, new() { 
                KioskTransactionState.LoadVerified, 
                KioskTransactionState.LoadVerificationFailed,
                KioskTransactionState.ManualReview
            } },
            { KioskTransactionState.LoadVerified, new() { 
                KioskTransactionState.Completed, 
                KioskTransactionState.CapturePending, 
                KioskTransactionState.ManualReview 
            } },
            { KioskTransactionState.LoadVerificationFailed, new() { 
                KioskTransactionState.ReversalPending, 
                KioskTransactionState.ManualReview 
            } },

            // Capture (For PreAuth/Capture saga path)
            { KioskTransactionState.CapturePending, new() { 
                KioskTransactionState.Captured, 
                KioskTransactionState.CaptureFailed, 
                KioskTransactionState.CaptureOutcomeUnknown, 
                KioskTransactionState.ManualReview 
            } },
            { KioskTransactionState.CaptureOutcomeUnknown, new() { 
                KioskTransactionState.Captured, 
                KioskTransactionState.CaptureFailed, 
                KioskTransactionState.ManualReview 
            } },
            { KioskTransactionState.Captured, new() { KioskTransactionState.Completed } },
            { KioskTransactionState.CaptureFailed, new() { KioskTransactionState.ManualReview } },

            // Reversals
            { KioskTransactionState.ReversalPending, new() { KioskTransactionState.Reversed, KioskTransactionState.ReversalFailed } },
            { KioskTransactionState.Reversed, new() { KioskTransactionState.Failed } },
            { KioskTransactionState.ReversalFailed, new() { KioskTransactionState.ManualReview } },

            // Terminal configurations
            { KioskTransactionState.Completed, new() },
            { KioskTransactionState.Failed, new() },
            { KioskTransactionState.ManualReview, new() }
        };

        public KioskTransaction(TransactionId id, string idempotencyKey)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            IdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) 
                ? throw new ArgumentException("IdempotencyKey is required.", nameof(idempotencyKey)) 
                : idempotencyKey;
        }

        public void TransitionTo(KioskTransactionState newState, string? reason = null)
        {
            if (State == KioskTransactionState.Completed || State == KioskTransactionState.Failed || State == KioskTransactionState.ManualReview)
            {
                throw new InvalidOperationException($"Cannot transition from terminal state {State} to {newState}.");
            }

            if (!AllowedTransitions.TryGetValue(State, out var allowed) || !allowed.Contains(newState))
            {
                throw new InvalidOperationException($"Transition from {State} to {newState} is not allowed.");
            }

            State = newState;
            LastModifiedAtUtc = DateTime.UtcNow;
            if (reason != null)
            {
                ErrorMessage = reason;
            }
        }

        public void SetCard(CardReference cardRef, long previousBalanceMinor)
        {
            CardRef = cardRef ?? throw new ArgumentNullException(nameof(cardRef));
            PreviousBalanceMinor = previousBalanceMinor;
        }

        public void SetAmount(Money amount)
        {
            Amount = amount ?? throw new ArgumentNullException(nameof(amount));
        }

        public void RegisterPaymentDetails(string? posRef, string? approvalCode, string? responseCode = null)
        {
            PosVendorReference = posRef;
            PosApprovalCode = approvalCode;
            ResponseCode = responseCode;
        }

        public void RegisterLoadDetails(string? loadRef, long newBalanceMinor)
        {
            LoadVendorReference = loadRef;
            NewBalanceMinor = newBalanceMinor;
        }

        public void IncrementRetry()
        {
            RetryCount++;
        }

        public void MarkManualReview(string reason)
        {
            ErrorMessage = reason;
            // Force transit to ManualReview from any state except terminal if business calls for it
            if (State != KioskTransactionState.Completed && State != KioskTransactionState.Failed)
            {
                State = KioskTransactionState.ManualReview;
                LastModifiedAtUtc = DateTime.UtcNow;
            }
        }

        public void LoadProperties(
            KioskTransactionState state,
            CardReference? cardRef,
            Money? amount,
            string? posRef,
            string? loadRef,
            string? approvalCode,
            string? responseCode,
            string? errorMsg,
            int retryCount,
            long prevBal,
            long newBal)
        {
            State = state;
            CardRef = cardRef;
            Amount = amount;
            PosVendorReference = posRef;
            LoadVendorReference = loadRef;
            PosApprovalCode = approvalCode;
            ResponseCode = responseCode;
            ErrorMessage = errorMsg;
            RetryCount = retryCount;
            PreviousBalanceMinor = prevBal;
            NewBalanceMinor = newBal;
        }
    }
}
