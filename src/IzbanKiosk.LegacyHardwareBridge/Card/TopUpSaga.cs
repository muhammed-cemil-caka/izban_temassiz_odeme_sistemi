using System;
using System.Collections.Generic;
using IzbanKiosk.LegacyHardware.Contracts;
using IzbanKiosk.LegacyHardwareBridge.Pos;

namespace IzbanKiosk.LegacyHardwareBridge.Card
{
    /// <summary>
    /// Sequences a top-up: take the money, put the value on the card, prove it landed,
    /// and undo the payment if it did not.
    ///
    /// The kiosk this replaces charged the passenger and then wrote to the card with no
    /// link between the two. When the write failed, or the card was pulled a moment too
    /// early, the money was gone and nothing was reversed - the first critical finding
    /// in the audit of the existing system. Everything here exists to make that outcome
    /// impossible, or where it cannot be prevented, impossible to miss.
    ///
    /// Three rules shape the order of operations:
    ///
    /// Nobody is charged for value this kiosk cannot deliver. Write authorisation and
    /// the payment terminal are both checked before the card is touched, so a machine
    /// missing either refuses service instead of taking money it cannot honour.
    ///
    /// An undetermined outcome is never resolved by guessing. A payment or a load that
    /// cannot say what happened ends the transaction at
    /// <see cref="TopUpOutcome.NeedsReconciliation"/>, which is a state a person
    /// settles - not a failure the kiosk retries and not a success it reports.
    ///
    /// The card is the authority on its own balance. A load counts as done only when
    /// the card reads back the expected figure; arithmetic on the amount charged is
    /// not evidence that value arrived.
    ///
    /// Deliberately free of Windows and vendor dependencies so the whole state machine
    /// can be tested against fakes, which is the only way these paths get exercised -
    /// a failed load followed by a failed reversal is not something anyone can stage on
    /// a real kiosk.
    /// </summary>
    public sealed class TopUpSaga
    {
        private readonly IPosTerminal _pos;
        private readonly ICardLoader _loader;
        private readonly ICardBalanceReader _reader;
        private readonly Action<string, object> _journal;
        private readonly Dictionary<string, TopUpResponse> _completed =
            new Dictionary<string, TopUpResponse>(StringComparer.OrdinalIgnoreCase);

        public TopUpSaga(
            IPosTerminal pos, ICardLoader loader, ICardBalanceReader reader, Action<string, object> journal)
        {
            _pos = pos;
            _loader = loader;
            _reader = reader;
            _journal = journal ?? delegate { };
        }

        public TopUpResponse Execute(PosPaymentRequest request)
        {
            if (request == null || request.AmountMinor <= 0 || string.IsNullOrWhiteSpace(request.IdempotencyKey))
            {
                return Fail(string.Empty, TopUpOutcome.NeedsReconciliation,
                    "Geçersiz istek: tutar ve işlem anahtarı zorunludur.");
            }

            // A repeat of a key already settled returns the first answer. Without this a
            // double tap, or a kiosk retrying after a dropped pipe, charges twice.
            TopUpResponse? previous;
            if (_completed.TryGetValue(request.IdempotencyKey, out previous) && previous != null)
            {
                _journal("TopUpRepeatIgnored", new { key = request.IdempotencyKey, outcome = previous.Outcome });
                return previous;
            }

            // Both checks come before any money moves.
            if (!_loader.IsAuthorised)
            {
                return Settle(request, TopUpOutcome.NotAuthorised, false, 0, _loader.LastErrorMessage);
            }
            if (!_pos.IsConfigured)
            {
                return Settle(request, TopUpOutcome.PosNotConfigured, false, 0, _pos.LastErrorMessage);
            }

            long balanceBefore;
            string readError;
            if (!_reader.TryReadBalanceMinor(request.StoragePseudonym, out balanceBefore, out readError))
            {
                // Without a starting figure the read-back afterwards proves nothing, so
                // there is no safe way to take the money.
                return Settle(request, TopUpOutcome.NeedsReconciliation, false, 0,
                    "Yükleme öncesi bakiye okunamadı, tahsilat yapılmadı: " + readError);
            }

            // Written before the charge so a power cut between here and the card leaves
            // evidence that a transaction was in flight.
            _journal("TopUpIntent", new
            {
                key = request.IdempotencyKey,
                amountMinor = request.AmountMinor,
                pseudonym = request.StoragePseudonym,
                balanceBeforeMinor = balanceBefore
            });

            PosPaymentResponse payment = _pos.Charge(request);
            _journal("TopUpCharge", new
            {
                key = request.IdempotencyKey,
                outcome = payment.Outcome,
                approved = payment.IsApproved,
                reference = payment.MaskedPosReference
            });

            if (!payment.IsApproved)
            {
                // An undetermined charge may or may not have taken money. Loading the
                // card on it could give away value that was never paid for.
                bool undetermined = !string.Equals(payment.Outcome, "Declined", StringComparison.OrdinalIgnoreCase);
                return Settle(request,
                    undetermined ? TopUpOutcome.NeedsReconciliation : TopUpOutcome.Declined,
                    false, balanceBefore, payment.StatusMessage, payment);
            }

            CardLoadResponse load = _loader.Load(new CardLoadRequest
            {
                IdempotencyKey = request.IdempotencyKey,
                AmountMinor = request.AmountMinor,
                StoragePseudonym = request.StoragePseudonym,
                BalanceBeforeMinor = balanceBefore
            });
            _journal("TopUpLoad", new
            {
                key = request.IdempotencyKey,
                loaded = load.IsLoaded,
                balanceAfterMinor = load.BalanceAfterMinor
            });

            if (!load.IsLoaded)
            {
                return ReverseAfterFailedLoad(request, payment, balanceBefore, load.StatusMessage);
            }

            // The card, not the arithmetic, decides whether the value arrived.
            long balanceAfter;
            if (!_reader.TryReadBalanceMinor(request.StoragePseudonym, out balanceAfter, out readError))
            {
                return Settle(request, TopUpOutcome.NeedsReconciliation, false, load.BalanceAfterMinor,
                    "Yükleme yapıldı ama karttan doğrulama okunamadı: " + readError, payment);
            }

            if (balanceAfter != balanceBefore + request.AmountMinor)
            {
                // Not reversed on purpose. The card may genuinely hold the value while
                // the figures disagree for another reason; refunding here could hand
                // back money for value the passenger kept.
                _journal("TopUpReadbackMismatch", new
                {
                    key = request.IdempotencyKey,
                    expectedMinor = balanceBefore + request.AmountMinor,
                    actualMinor = balanceAfter
                });
                return Settle(request, TopUpOutcome.NeedsReconciliation, false, balanceAfter,
                    "Yükleme sonrası bakiye beklenenle uyuşmuyor. İşlem elle incelenmelidir.", payment);
            }

            TopUpResponse completed = Settle(request, TopUpOutcome.Completed, true, balanceAfter,
                "Yükleme tamamlandı ve karttan doğrulandı.", payment);
            completed.AmountMinor = request.AmountMinor;
            completed.ReferenceNo = load.ReferenceNo;
            return completed;
        }

        private TopUpResponse ReverseAfterFailedLoad(
            PosPaymentRequest request, PosPaymentResponse payment, long balanceBefore, string loadError)
        {
            PosReversalResponse reversal = _pos.Reverse(new PosReversalRequest
            {
                IdempotencyKey = request.IdempotencyKey,
                ApprovalCode = payment.ApprovalCode,
                AmountMinor = request.AmountMinor,
                Currency = request.Currency,
                Reason = "Karta yükleme başarısız: " + loadError
            });
            _journal("TopUpReversal", new
            {
                key = request.IdempotencyKey,
                outcome = reversal.Outcome,
                reversed = reversal.IsReversed
            });

            if (reversal.IsReversed)
            {
                return Settle(request, TopUpOutcome.RefundedAfterLoadFailure, false, balanceBefore,
                    "Karta yükleme yapılamadı, ödeme iade edildi. " + loadError, payment);
            }

            return Settle(request, TopUpOutcome.NeedsReconciliation, false, balanceBefore,
                "Karta yükleme yapılamadı ve iade doğrulanamadı. İşlem elle incelenmelidir. " +
                loadError + " / " + reversal.StatusMessage, payment);
        }

        private TopUpResponse Settle(
            PosPaymentRequest request, string outcome, bool completed, long balanceAfter, string message,
            PosPaymentResponse? payment = null)
        {
            var response = new TopUpResponse
            {
                RequestId = request.IdempotencyKey,
                Outcome = outcome,
                IsCompleted = completed,
                BalanceAfterMinor = balanceAfter,
                ApprovalCode = payment == null ? string.Empty : payment.ApprovalCode,
                MaskedPosReference = payment == null ? string.Empty : payment.MaskedPosReference,
                StatusMessage = message
            };

            // Refusals that never reached the payment terminal are not remembered: the
            // kiosk should serve the passenger normally once the machine is fixed,
            // rather than replay a refusal for a key they never spent anything on.
            if (outcome != TopUpOutcome.NotAuthorised && outcome != TopUpOutcome.PosNotConfigured)
            {
                _completed[request.IdempotencyKey] = response;
            }

            _journal("TopUpSettled", new { key = request.IdempotencyKey, outcome = outcome });
            return response;
        }

        private static TopUpResponse Fail(string key, string outcome, string message)
        {
            return new TopUpResponse
            {
                RequestId = key,
                Outcome = outcome,
                IsCompleted = false,
                StatusMessage = message
            };
        }
    }
}
