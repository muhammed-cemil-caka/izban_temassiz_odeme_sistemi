using System;
using System.Threading;

namespace IzbanKiosk.LegacyHardwareBridge.Transport
{
    public class BridgeSingleInstanceGuard : IDisposable
    {
        private const string MUTEX_NAME = "Global\\IzbanKiosk.LegacyHardwareBridge.Mutex";
        private Mutex? _mutex;
        private bool _hasOwnership;

        public bool TryAcquire(out string errorMessage)
        {
            errorMessage = string.Empty;
            try
            {
                // Create mutex which is visible globally across all sessions (Global\)
                _mutex = new Mutex(true, MUTEX_NAME, out _hasOwnership);
                if (!_hasOwnership)
                {
                    errorMessage = "Another instance of IzbanKiosk.LegacyHardwareBridge.exe is already running on this machine.";
                    return false;
                }
            }
            catch (Exception ex)
            {
                errorMessage = $"Failed to check single instance mutex: {ex.Message}";
                return false;
            }
            return true;
        }

        public void Dispose()
        {
            if (_mutex != null)
            {
                if (_hasOwnership)
                {
                    try
                    {
                        _mutex.ReleaseMutex();
                    }
                    catch { }
                }
                _mutex.Dispose();
                _mutex = null;
            }
        }
    }
}
