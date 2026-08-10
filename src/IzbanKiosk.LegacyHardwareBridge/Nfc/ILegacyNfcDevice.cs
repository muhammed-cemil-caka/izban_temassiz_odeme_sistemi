using System;
using IzbanKiosk.LegacyHardware.Contracts;

namespace IzbanKiosk.LegacyHardwareBridge.Nfc
{
    public interface ILegacyNfcDevice
    {
        bool Initialize();
        bool OpenComm(string port);
        void CloseComm();
        bool IsHardwareConnected();
        bool ResetSam();
        string LastSamStatusMessage { get; }
        bool CheckIfCardPresent(out string maskedCardRef, out string storagePseudonym);
        bool ReadCardSnapshot(string requestId, out CardSnapshotResponse snapshot);
        bool WaitForCardRemoval(TimeSpan timeout);

        /// <summary>
        /// Writes value onto the card presently on the reader.
        ///
        /// <paramref name="amount"/> is passed to the vendor library exactly as given;
        /// choosing its unit belongs to the caller, which declares it in configuration
        /// and proves it by reading the balance back afterwards.
        /// </summary>
        bool TryTopup(
            ushort terminalNo, uint terminalUid, byte companyId, int referenceNo, uint amount, out string error);
        void Shutdown();
    }
}
