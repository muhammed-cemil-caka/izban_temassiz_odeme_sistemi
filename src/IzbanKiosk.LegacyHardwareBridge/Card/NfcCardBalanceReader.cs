using IzbanKiosk.LegacyHardware.Contracts;
using IzbanKiosk.LegacyHardwareBridge.Nfc;

namespace IzbanKiosk.LegacyHardwareBridge.Card
{
    /// <summary>
    /// Reads the balance straight off the card for the top-up flow's before and after
    /// checks.
    ///
    /// Refuses while the vendor balance scale is unverified. That scale converts the
    /// raw figure the card returns into minor units, and an unverified one can be out
    /// by a factor of ten - which would make a read-back either pass a load that never
    /// landed or fail one that did. Displaying an uncertain balance is tolerable;
    /// settling money against it is not.
    /// </summary>
    public sealed class NfcCardBalanceReader : ICardBalanceReader
    {
        private readonly ILegacyNfcDevice _device;

        public NfcCardBalanceReader(ILegacyNfcDevice device)
        {
            _device = device;
        }

        public bool TryReadBalanceMinor(string storagePseudonym, out long balanceMinor, out string error)
        {
            balanceMinor = 0;
            error = string.Empty;

            CardSnapshotResponse snapshot;
            if (!_device.ReadCardSnapshot(storagePseudonym, out snapshot) || snapshot == null)
            {
                error = "Kart okunamadı.";
                return false;
            }

            if (!snapshot.IsCardValid)
            {
                error = "Kart geçerli değil.";
                return false;
            }

            if (!snapshot.IsBalanceScaleVerified)
            {
                error = "Bakiye ölçeği doğrulanmadığı için para işlemi yapılamaz.";
                return false;
            }

            balanceMinor = snapshot.BalanceMinor;
            return true;
        }
    }
}
