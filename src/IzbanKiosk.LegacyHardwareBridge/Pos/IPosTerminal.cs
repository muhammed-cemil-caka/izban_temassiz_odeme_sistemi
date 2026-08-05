using IzbanKiosk.LegacyHardware.Contracts;

namespace IzbanKiosk.LegacyHardwareBridge.Pos
{
    /// <summary>
    /// The single seam a certified bank POS SDK plugs into.
    ///
    /// Implement this against the bank's x86 SDK, register the implementation in
    /// <c>Program.Main</c> in place of <see cref="NotConfiguredPosTerminal"/>, and the
    /// existing <c>PosPayment</c> pipe command starts working without any other change
    /// to the kiosk or the bridge.
    ///
    /// Contract requirements that the kiosk depends on:
    /// <list type="bullet">
    /// <item>Charging must be idempotent on <see cref="PosPaymentRequest.IdempotencyKey"/>.
    /// A repeated request for the same key must return the first outcome rather than
    /// charging the passenger twice.</item>
    /// <item>An outcome that cannot be determined must be reported as
    /// <c>OutcomeUnknown</c>, never as success and never as failure. The card must not
    /// be loaded on an unknown outcome.</item>
    /// <item>PAN, track data, CVV and any cardholder name must never leave the
    /// implementation. Only the masked reference and the approval code may be
    /// returned.</item>
    /// </list>
    /// </summary>
    public interface IPosTerminal
    {
        bool IsConfigured { get; }

        string LastErrorMessage { get; }

        bool Initialize();

        PosPaymentResponse Charge(PosPaymentRequest request);

        void Shutdown();
    }
}
