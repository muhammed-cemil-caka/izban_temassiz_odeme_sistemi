using IzbanKiosk.Domain;

namespace IzbanKiosk.Application.Hardware.Nfc
{
    public record CardSnapshot
    {
        public CardReference CardRef { get; }
        public long BalanceMinor { get; }
        public int TransactionCounter { get; }
        public bool IsValid { get; }

        public CardSnapshot(CardReference cardRef, long balanceMinor, int transactionCounter, bool isValid)
        {
            CardRef = cardRef;
            BalanceMinor = balanceMinor;
            TransactionCounter = transactionCounter;
            IsValid = isValid;
        }
    }
}
