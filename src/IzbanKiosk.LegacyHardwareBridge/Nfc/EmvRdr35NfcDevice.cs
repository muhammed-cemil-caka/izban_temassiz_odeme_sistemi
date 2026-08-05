using System;
using System.Runtime.InteropServices;
using System.Threading;
using IzbanKiosk.LegacyHardware.Contracts;
using IzbanKiosk.LegacyHardwareBridge.Interop;
using IzbanKiosk.LegacyHardwareBridge.Security;

namespace IzbanKiosk.LegacyHardwareBridge.Nfc
{
    public class EmvRdr35NfcDevice : ILegacyNfcDevice
    {
        private static readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);
        private readonly SensitiveDataRedactor _redactor;
        private uint _hComp;
        private bool _isInitialized;
        private bool _isCommOpen;
        private bool _isSamReady;
        // ResetSamAv2 and ReadOffCard both consume the 13-byte AV2_LAYOUT session,
        // not the 62-byte AV2_LAYOUT_EXT reader-information structure.
        private IntPtr _av2Buffer = IntPtr.Zero;

        public string LastSamStatusMessage { get; private set; } = "SAM has not been checked.";

        public EmvRdr35NfcDevice(SensitiveDataRedactor redactor)
        {
            _redactor = redactor;
        }

        public bool Initialize()
        {
            _lock.Wait();
            try
            {
                if (_isInitialized) return true;

                _hComp = EmvRdr35NativeMethods.InitComp();
                if (_hComp == 0)
                {
                    return false;
                }

                _isInitialized = true;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
            finally
            {
                _lock.Release();
            }
        }

        public bool OpenComm(string port)
        {
            _lock.Wait();
            try
            {
                if (!_isInitialized) return false;
                if (_isCommOpen) return true;

                bool ok = EmvRdr35NativeMethods.CommOpen(_hComp, port);
                if (ok)
                {
                    _isCommOpen = true;
                }
                return ok;
            }
            catch (Exception)
            {
                return false;
            }
            finally
            {
                _lock.Release();
            }
        }

        public void CloseComm()
        {
            _lock.Wait();
            try
            {
                if (_isCommOpen)
                {
                    EmvRdr35NativeMethods.CommClose(_hComp);
                    _isCommOpen = false;
                    _isSamReady = false;
                }
            }
            catch { }
            finally
            {
                _lock.Release();
            }
        }

        public bool IsHardwareConnected()
        {
            _lock.Wait();
            try
            {
                if (!_isInitialized || !_isCommOpen) return false;
                return EmvRdr35NativeMethods.IsConnected(_hComp);
            }
            catch (Exception)
            {
                return false;
            }
            finally
            {
                _lock.Release();
            }
        }

        public bool ResetSam()
        {
            _lock.Wait();
            try
            {
                return ResetSamUnsafe();
            }
            catch (Exception ex)
            {
                _isSamReady = false;
                LastSamStatusMessage = "SAM initialization exception: " + ex.GetType().Name;
                return false;
            }
            finally
            {
                _lock.Release();
            }
        }

        /// <summary>
        /// Reproduces the proven AUSKiosk 5.2.0.4 vendor sequence:
        /// CommOpen -> GetReaderParam(AV2_LAYOUT_EXT/62) -> ResetSamAv2(AV2_LAYOUT/13).
        /// Caller must hold <see cref="_lock"/>.
        /// </summary>
        private bool ResetSamUnsafe()
        {
            if (!_isInitialized || !_isCommOpen)
            {
                _isSamReady = false;
                LastSamStatusMessage = "NFC reader communication is not open.";
                return false;
            }

            IntPtr readerParamBuffer = IntPtr.Zero;
            try
            {
                int readerParamSize = Marshal.SizeOf(typeof(EmvRdr35NativeMethods.AV2_LAYOUT_EXT));
                readerParamBuffer = Marshal.AllocHGlobal(readerParamSize);
                ZeroBuffer(readerParamBuffer, readerParamSize);

                byte rxGain = 0;
                byte minLevel = 0;
                bool readerParamOk = EmvRdr35NativeMethods.GetReaderParam(
                    _hComp,
                    ref rxGain,
                    ref minLevel,
                    readerParamBuffer);
                if (!readerParamOk)
                {
                    _isSamReady = false;
                    LastSamStatusMessage = "GetReaderParam returned false before SAM reset.";
                    return false;
                }

                var readerInfo = (EmvRdr35NativeMethods.AV2_LAYOUT_EXT)Marshal.PtrToStructure(
                    readerParamBuffer,
                    typeof(EmvRdr35NativeMethods.AV2_LAYOUT_EXT));

                int sessionSize = Marshal.SizeOf(typeof(EmvRdr35NativeMethods.AV2_LAYOUT));
                if (_av2Buffer == IntPtr.Zero)
                {
                    _av2Buffer = Marshal.AllocHGlobal(sessionSize);
                }
                ZeroBuffer(_av2Buffer, sessionSize);

                bool resetOk = EmvRdr35NativeMethods.ResetSamAv2(_hComp, _av2Buffer);
                if (!resetOk)
                {
                    _isSamReady = false;
                    LastSamStatusMessage = "ResetSamAv2 returned false for the 13-byte SAM session buffer.";
                    return false;
                }

                var session = (EmvRdr35NativeMethods.AV2_LAYOUT)Marshal.PtrToStructure(
                    _av2Buffer,
                    typeof(EmvRdr35NativeMethods.AV2_LAYOUT));
                if (session.Av2_Uid == null || session.Av2_Uid.Length != 10)
                {
                    _isSamReady = false;
                    LastSamStatusMessage = "ResetSamAv2 returned an invalid SAM session layout.";
                    return false;
                }

                _isSamReady = true;
                LastSamStatusMessage = string.Format(
                    "SAM session ready (reader init={0}, unlocked={1}, host mode={2}).",
                    readerInfo.bSamInitOK,
                    readerInfo.bSamUnlocked,
                    session.Av2HostMode);
                return true;
            }
            finally
            {
                if (readerParamBuffer != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(readerParamBuffer);
                }
            }
        }

        public bool CheckIfCardPresent(out string maskedCardRef, out string storagePseudonym)
        {
            maskedCardRef = string.Empty;
            storagePseudonym = string.Empty;

            _lock.Wait();
            IntPtr cardBuffer = IntPtr.Zero;
            try
            {
                if (!_isInitialized || !_isCommOpen) return false;

                int cardSize = Marshal.SizeOf(typeof(EmvRdr35NativeMethods.CARD_LAYOUT));
                cardBuffer = Marshal.AllocHGlobal(cardSize);

                byte[] zeros = new byte[cardSize];
                Marshal.Copy(zeros, 0, cardBuffer, cardSize);

                bool detected = EmvRdr35NativeMethods.SelectCardNoRats(_hComp, cardBuffer);
                if (!detected) return false;

                bool topupValid = EmvRdr35NativeMethods.IsOnTopupValidCard(cardBuffer);
                if (!topupValid) return false;

                bool valid = EmvRdr35NativeMethods.IsOffValidCard(cardBuffer);
                if (!valid) return false;

                var cardInfo = (EmvRdr35NativeMethods.CARD_LAYOUT)Marshal.PtrToStructure(cardBuffer, typeof(EmvRdr35NativeMethods.CARD_LAYOUT));
                
                if (!TryExtractUid(cardInfo, out string rawCardId)) return false;

                // Compute pseudonyms
                maskedCardRef = _redactor.MaskCardReference(rawCardId);
                storagePseudonym = _redactor.GenerateStoragePseudonym(rawCardId);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
            finally
            {
                if (cardBuffer != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(cardBuffer);
                }
                _lock.Release();
            }
        }

        public bool ReadCardSnapshot(string requestId, out CardSnapshotResponse snapshot)
        {
            snapshot = new CardSnapshotResponse
            {
                RequestId = requestId,
                ObservedAtUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                Currency = "TRY",
                IsCardValid = false,
                IsSamVerified = false
            };

            _lock.Wait();
            IntPtr cardBuffer = IntPtr.Zero;
            IntPtr infoBuffer = IntPtr.Zero;
            try
            {
                if (!_isInitialized || !_isCommOpen)
                {
                    snapshot.ErrorCode = "ERR_NOT_READY";
                    return false;
                }

                // 1. Select card
                int cardSize = Marshal.SizeOf(typeof(EmvRdr35NativeMethods.CARD_LAYOUT));
                cardBuffer = Marshal.AllocHGlobal(cardSize);
                byte[] zeros = new byte[cardSize];
                Marshal.Copy(zeros, 0, cardBuffer, cardSize);

                bool detected = EmvRdr35NativeMethods.SelectCardNoRats(_hComp, cardBuffer);
                if (!detected)
                {
                    snapshot.ErrorCode = "ERR_NO_CARD";
                    return false;
                }

                bool topupValid = EmvRdr35NativeMethods.IsOnTopupValidCard(cardBuffer);
                if (!topupValid)
                {
                    snapshot.ErrorCode = "ERR_NOT_TOPUP_CARD";
                    return false;
                }

                bool valid = EmvRdr35NativeMethods.IsOffValidCard(cardBuffer);
                if (!valid)
                {
                    snapshot.ErrorCode = "ERR_INVALID_CARD";
                    return false;
                }

                var cardLayout = (EmvRdr35NativeMethods.CARD_LAYOUT)Marshal.PtrToStructure(cardBuffer, typeof(EmvRdr35NativeMethods.CARD_LAYOUT));
                if (!TryExtractUid(cardLayout, out string rawCardId))
                {
                    snapshot.ErrorCode = "ERR_INVALID_UID";
                    return false;
                }

                // 2. Ensure the 13-byte SAM session created during health initialization
                // is available. ReadOffCard requires that exact AV2_LAYOUT buffer.
                if (!_isSamReady && !ResetSamUnsafe())
                {
                    snapshot.ErrorCode = "ERR_SAM_RESET_FAILED";
                    return false;
                }
                bool samVerified = _isSamReady;

                // 3. Read Off Card Info
                int infoSize = Marshal.SizeOf(typeof(EmvRdr35NativeMethods.TOffCardInf));
                infoBuffer = Marshal.AllocHGlobal(infoSize);
                byte[] infoZeros = new byte[infoSize];
                Marshal.Copy(infoZeros, 0, infoBuffer, infoSize);

                bool readOk = EmvRdr35NativeMethods.ReadOffCard(_hComp, _av2Buffer, infoBuffer);
                if (!readOk)
                {
                    snapshot.ErrorCode = "ERR_READ_FAILED";
                    return false;
                }

                var cardInfo = (EmvRdr35NativeMethods.TOffCardInf)Marshal.PtrToStructure(infoBuffer, typeof(EmvRdr35NativeMethods.TOffCardInf));

                // The legacy production application accepts an offline card snapshot only
                // when both vendor-owned identity fields are populated.
                if (cardInfo.alias == 0 || cardInfo.cardType == 0)
                {
                    snapshot.ErrorCode = "ERR_READ_INVALID_DATA";
                    return false;
                }

                snapshot.CardNumber = cardInfo.alias.ToString();
                snapshot.CardUid = rawCardId;
                snapshot.MaskedCardReference = _redactor.MaskCardReference(cardInfo.alias.ToString());
                snapshot.StoragePseudonym = _redactor.GenerateStoragePseudonym(cardInfo.alias.ToString());
                snapshot.CardType = cardInfo.cardType.ToString();
                snapshot.CardSubType = cardInfo.cardSubType.ToString();
                snapshot.BalanceRaw = cardInfo.balance;
                snapshot.BalanceScale = 100;
                snapshot.IsBalanceScaleVerified = true;
                snapshot.BalanceMinor = cardInfo.balance; 
                snapshot.CardTransactionCounter = cardInfo.cardTrnCounter;
                snapshot.IsCardValid = true;
                snapshot.IsSamVerified = samVerified;
                // ReadOffCard is accepted only after the installed kiosk SAM has been reset and
                // unlocked. At that instant the card-resident value is authoritative for this
                // read-only prototype; no local cache or simulator value is substituted.
                snapshot.IsAuthoritative = samVerified;
                snapshot.IsVerified = samVerified;
                snapshot.IsStale = false;
                snapshot.VendorResponseCode = 0;

                return true;
            }
            catch (Exception)
            {
                snapshot.ErrorCode = "ERR_EXCEPTION";
                // Do not send native exception details over the named pipe.
                return false;
            }
            finally
            {
                if (cardBuffer != IntPtr.Zero) Marshal.FreeHGlobal(cardBuffer);
                if (infoBuffer != IntPtr.Zero) Marshal.FreeHGlobal(infoBuffer);
                _lock.Release();
            }
        }

        private static void ZeroBuffer(IntPtr buffer, int size)
        {
            byte[] zeros = new byte[size];
            Marshal.Copy(zeros, 0, buffer, size);
        }

        public bool WaitForCardRemoval(TimeSpan timeout)
        {
            DateTime start = DateTime.UtcNow;
            while (DateTime.UtcNow - start < timeout)
            {
                string maskedCardRef;
                string storagePseudonym;
                
                // If checking returns false, the card is not present on reader.
                if (!CheckIfCardPresent(out maskedCardRef, out storagePseudonym))
                {
                    return true;
                }

                Thread.Sleep(200);
            }
            return false;
        }

        private static bool TryExtractUid(EmvRdr35NativeMethods.CARD_LAYOUT cardLayout, out string rawCardId)
        {
            rawCardId = string.Empty;

            // AUSKiosk 5.2.0.4 uses the UID captured at reader reset as the İzmirim
            // Mifare identifier. Keep the same precedence for field compatibility.
            int resetUidLength = cardLayout.CARD_UIDLEN_AT_RST;
            byte[] resetUid = cardLayout.CARD_UID_AT_RST;
            if (resetUidLength > 0 && resetUidLength <= 10 && resetUid != null && resetUid.Length >= resetUidLength)
            {
                rawCardId = BitConverter.ToString(resetUid, 0, resetUidLength).Replace("-", string.Empty).ToUpperInvariant();
                return true;
            }

            int getUidLength = cardLayout.CARD_UIDLEN_AT_GET_UID;
            byte[] getUid = cardLayout.CARD_UID_AT_GET_UID;
            if (getUidLength > 0 && getUidLength <= 10 && getUid != null && getUid.Length >= getUidLength)
            {
                rawCardId = BitConverter.ToString(getUid, 0, getUidLength).Replace("-", string.Empty).ToUpperInvariant();
                return true;
            }

            return false;
        }

        public void Shutdown()
        {
            CloseComm();
            _lock.Wait();
            try
            {
                if (_isInitialized)
                {
                    EmvRdr35NativeMethods.DoneComp(_hComp);
                    _isInitialized = false;
                    _isSamReady = false;
                }

                if (_av2Buffer != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(_av2Buffer);
                    _av2Buffer = IntPtr.Zero;
                }
            }
            catch { }
            finally
            {
                _lock.Release();
            }
        }
    }
}
