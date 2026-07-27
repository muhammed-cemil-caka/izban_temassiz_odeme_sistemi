namespace IzbanKioskApp.Hardware
{
    public interface IPosTerminal
    {
        bool Connect(string portOrIp);
        bool ProcessPayment(decimal amount, out string approvalCode);
    }
}