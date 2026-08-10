using IzbanKiosk.LegacyHardware.Contracts;

namespace IzbanKiosk.LegacyHardwareBridge.Card
{
    /// <summary>
    /// The single seam an authorised İzmirim Kart load command plugs into.
    ///
    /// Kept separate from the reader on purpose. Reading a balance needs nothing but
    /// the card; writing one needs a write-capable SAM, its keys, and written
    /// authorisation from the card scheme. Those arrive as their own delivery, and
    /// until they do <see cref="NotAuthorisedCardLoader"/> refuses every load.
    ///
    /// Contract requirements the top-up flow depends on:
    /// <list type="bullet">
    /// <item>Loading must be idempotent on <see cref="CardLoadRequest.IdempotencyKey"/>.
    /// A repeat of the same key must not add value twice.</item>
    /// <item>A load whose outcome cannot be determined must report
    /// <c>IsLoaded = false</c> with a reason, never a guess. The caller reverses the
    /// payment on a reported failure, so claiming failure for a load that actually
    /// succeeded gives the passenger both the value and the refund.</item>
    /// <item><see cref="CardLoadResponse.BalanceAfterMinor"/> must be what the card
    /// itself reports after the write, not the expected arithmetic.</item>
    /// </list>
    /// </summary>
    public interface ICardLoader
    {
        /// <summary>
        /// False until a write-capable SAM and scheme authorisation are in place. The
        /// top-up flow checks this before it charges anyone: taking money the kiosk
        /// cannot turn into value is the one outcome worse than refusing service.
        /// </summary>
        bool IsAuthorised { get; }

        string LastErrorMessage { get; }

        CardLoadResponse Load(CardLoadRequest request);
    }

    /// <summary>
    /// Reads the balance the card itself holds. Used to establish the figure before a
    /// load and to verify the figure after one, so the kiosk never reports a top-up on
    /// arithmetic alone.
    /// </summary>
    public interface ICardBalanceReader
    {
        bool TryReadBalanceMinor(string storagePseudonym, out long balanceMinor, out string error);
    }
}
