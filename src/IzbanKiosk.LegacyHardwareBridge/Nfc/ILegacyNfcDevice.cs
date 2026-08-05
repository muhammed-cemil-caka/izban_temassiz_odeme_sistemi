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
        void Shutdown();
    }
}
