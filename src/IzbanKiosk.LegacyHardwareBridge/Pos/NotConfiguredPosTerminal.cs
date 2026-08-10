using IzbanKiosk.LegacyHardware.Contracts;

namespace IzbanKiosk.LegacyHardwareBridge.Pos
{
    /// <summary>
    /// Fail-closed placeholder used until a certified bank POS SDK is integrated.
    /// It refuses every charge with an unambiguous reason instead of pretending an
    /// outcome, so no passenger can be charged and no card can be loaded by accident.
    /// </summary>
    public sealed class NotConfiguredPosTerminal : IPosTerminal
    {
        public const string NotConfiguredMessage =
            "No certified bank POS SDK is integrated in this hardware profile. " +
            "No payment was taken and no amount was written to the card.";

        public bool IsConfigured
        {
            get { return false; }
        }

        public string LastErrorMessage
        {
            get { return NotConfiguredMessage; }
        }

        public bool Initialize()
        {
            return false;
        }

        public PosPaymentResponse Charge(PosPaymentRequest request)
        {
            return new PosPaymentResponse
            {
                RequestId = request == null ? string.Empty : request.IdempotencyKey,
                Outcome = "NotConfigured",
                IsApproved = false,
                StatusMessage = NotConfiguredMessage
            };
        }

        /// <summary>
        /// Reports the reversal as done. Nothing was ever charged by this placeholder,
        /// so there is nothing outstanding - and saying otherwise would push a
        /// transaction that never took money into manual reconciliation.
        /// </summary>
        public PosReversalResponse Reverse(PosReversalRequest request)
        {
            return new PosReversalResponse
            {
                Outcome = "Reversed",
                IsReversed = true,
                StatusMessage = NotConfiguredMessage
            };
        }

        public void Shutdown()
        {
        }
    }
}
