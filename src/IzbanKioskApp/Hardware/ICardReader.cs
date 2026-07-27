namespace IzbanKioskApp.Hardware
{
    public interface ICardReader
    {
        bool Connect(string portName);
        (string CardUid, decimal CurrentBalance) ReadCard();
        bool WriteBalance(string cardUid, decimal newBalance);
    }
}