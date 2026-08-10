using IzbanKiosk.LegacyHardware.Contracts;

namespace IzbanKiosk.LegacyHardwareBridge.Card
{
    /// <summary>
    /// Fail-closed placeholder used until a write-capable SAM, its keys and scheme
    /// authorisation are delivered. It refuses every load with an unambiguous reason,
    /// which keeps the top-up flow from charging anyone: the flow checks
    /// <see cref="IsAuthorised"/> before it reaches the payment terminal.
    /// </summary>
    public sealed class NotAuthorisedCardLoader : ICardLoader
    {
        public const string NotAuthorisedMessage =
            "İzmirim Kart yazma yetkisi bu otomatta tanımlı değil. Karta yükleme yapılmadı " +
            "ve hiçbir tahsilat denenmedi.";

        public bool IsAuthorised
        {
            get { return false; }
        }

        public string LastErrorMessage
        {
            get { return NotAuthorisedMessage; }
        }

        public CardLoadResponse Load(CardLoadRequest request)
        {
            return new CardLoadResponse
            {
                IsLoaded = false,
                BalanceAfterMinor = request == null ? 0 : request.BalanceBeforeMinor,
                StatusMessage = NotAuthorisedMessage
            };
        }
    }
}
