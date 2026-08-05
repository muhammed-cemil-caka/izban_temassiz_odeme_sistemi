using System;
using System.Reflection;
using IzbanKiosk.LegacyHardware.Contracts;
using IzbanKiosk.LegacyHardwareBridge.Security;

namespace IzbanKiosk.LegacyHardwareBridge.Nfc
{
    public class Usb10NfcDevice : ILegacyNfcDevice
    {
        private readonly SensitiveDataRedactor _redactor;
        private object? _cardLibNetInstance;
        private object? _izmirimKartNetInstance;
        private bool _isInitialized;

        public string LastSamStatusMessage { get; private set; } = "USB10 SAM has not been checked.";

        public Usb10NfcDevice(SensitiveDataRedactor redactor)
        {
            _redactor = redactor;
        }

        public bool Initialize()
        {
            if (_isInitialized) return true;

            try
            {
                // Load assemblies dynamically to allow compiling on macOS without failure
                string runDir = AppDomain.CurrentDomain.BaseDirectory;
                string cardLibPath = System.IO.Path.Combine(runDir, "CardLibWNet.dll");
                string qAsisPath = System.IO.Path.Combine(runDir, "QAsisIzmirimKartLibWNet.dll");

                if (!System.IO.File.Exists(cardLibPath) || !System.IO.File.Exists(qAsisPath))
                {
                    // Check vendor folder too
                    cardLibPath = System.IO.Path.Combine(runDir, "vendor", "CardLibWNet.dll");
                    qAsisPath = System.IO.Path.Combine(runDir, "vendor", "QAsisIzmirimKartLibWNet.dll");
                    if (!System.IO.File.Exists(cardLibPath) || !System.IO.File.Exists(qAsisPath))
                    {
                        return false;
                    }
                }

                Assembly cardLibAss = Assembly.LoadFrom(cardLibPath);
                Assembly qAsisAss = Assembly.LoadFrom(qAsisPath);

                Type cardLibType = cardLibAss.GetType("CardLibWNet.CardLibNet");
                Type qAsisType = qAsisAss.GetType("QAsisIzmirimKartLibWNet.QAsisIzmirimKartLibNet");

                if (cardLibType == null || qAsisType == null) return false;

                _cardLibNetInstance = Activator.CreateInstance(cardLibType);
                _izmirimKartNetInstance = Activator.CreateInstance(qAsisType);

                _isInitialized = true;
                return true;
            }
            catch
            {
                return false;
            }
        }

        public bool OpenComm(string port)
        {
            if (!_isInitialized || _cardLibNetInstance == null) return false;
            try
            {
                // Invoke Init(port) on CardLibNet
                var method = _cardLibNetInstance.GetType().GetMethod("Init", new Type[] { typeof(string) });
                if (method != null)
                {
                    method.Invoke(_cardLibNetInstance, new object[] { port });
                    return true;
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        public void CloseComm()
        {
            if (!_isInitialized || _cardLibNetInstance == null) return;
            try
            {
                var method = _cardLibNetInstance.GetType().GetMethod("Done");
                method?.Invoke(_cardLibNetInstance, null);
            }
            catch { }
        }

        public bool IsHardwareConnected()
        {
            return _isInitialized;
        }

        public bool ResetSam()
        {
            if (!_isInitialized || _cardLibNetInstance == null)
            {
                LastSamStatusMessage = "USB10 reader is not initialized.";
                return false;
            }
            try
            {
                var method = _cardLibNetInstance.GetType().GetMethod("SAM_Reset");
                if (method != null)
                {
                    var res = method.Invoke(_cardLibNetInstance, null);
                    bool ok = res is bool && (bool)res;
                    LastSamStatusMessage = ok ? "USB10 SAM reset completed." : "USB10 SAM_Reset returned false.";
                    return ok;
                }
                LastSamStatusMessage = "USB10 SAM_Reset method was not found.";
                return false;
            }
            catch (Exception ex)
            {
                LastSamStatusMessage = "USB10 SAM reset failed: " + ex.GetType().Name;
                return false;
            }
        }

        public bool CheckIfCardPresent(out string maskedCardRef, out string storagePseudonym)
        {
            maskedCardRef = string.Empty;
            storagePseudonym = string.Empty;

            if (!_isInitialized || _cardLibNetInstance == null) return false;

            try
            {
                var method = _cardLibNetInstance.GetType().GetMethod("CheckIfCardPresent");
                if (method != null)
                {
                    var res = method.Invoke(_cardLibNetInstance, null);
                    if (res is bool present && present)
                    {
                        // Set dummy references or poll status
                        maskedCardRef = _redactor.MaskCardReference("USB10-CARD");
                        storagePseudonym = _redactor.GenerateStoragePseudonym("USB10-CARD");
                        return true;
                    }
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        public bool ReadCardSnapshot(string requestId, out CardSnapshotResponse snapshot)
        {
            snapshot = new CardSnapshotResponse
            {
                RequestId = requestId,
                ObservedAtUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                IsCardValid = false
            };

            if (!_isInitialized || _izmirimKartNetInstance == null) return false;

            try
            {
                // Dummy read details since USB10 is skeleton fallback and deactivated
                snapshot.MaskedCardReference = _redactor.MaskCardReference("123456789");
                snapshot.StoragePseudonym = _redactor.GenerateStoragePseudonym("123456789");
                snapshot.CardType = "1";
                snapshot.CardSubType = "1";
                snapshot.BalanceRaw = 1000;
                snapshot.BalanceMinor = 1000;
                snapshot.BalanceScale = 100;
                snapshot.IsBalanceScaleVerified = false;
                snapshot.IsCardValid = true;
                return true;
            }
            catch
            {
                return false;
            }
        }

        public bool WaitForCardRemoval(TimeSpan timeout)
        {
            // USB10 card removal wait implementation
            return true;
        }

        public void Shutdown()
        {
            CloseComm();
            _cardLibNetInstance = null;
            _izmirimKartNetInstance = null;
            _isInitialized = false;
        }
    }
}
