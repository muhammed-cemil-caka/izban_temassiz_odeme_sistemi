using System;
using System.Threading;
using IzbanKiosk.LegacyHardware.Contracts;
using IzbanKiosk.LegacyHardwareBridge.Configuration;
using IzbanKiosk.LegacyHardwareBridge.Nfc;

namespace IzbanKiosk.LegacyHardwareBridge.Card
{
    /// <summary>
    /// Loads value onto an İzmirim Kart through the vendor library's <c>Topup</c> call.
    ///
    /// Stays switched off unless the deployment supplies every value the scheme needs
    /// to identify this terminal, and says which unit the vendor's amount is in. There
    /// is no default for any of them: a terminal identifying itself as number zero, or
    /// a kiosk that loads a hundred times the money it charged, are both worse than a
    /// kiosk that declines to top up.
    ///
    /// Switching this on is not the same as being allowed to use it. The write also
    /// needs a SAM whose keys permit it and written authorisation from the card scheme;
    /// this class only stops being the reason it fails.
    /// </summary>
    public sealed class LegacyCardLoader : ICardLoader
    {
        private readonly ILegacyNfcDevice _device;
        private readonly HardwareOptions _options;
        private string _lastError = string.Empty;
        private int _referenceCounter;

        public LegacyCardLoader(ILegacyNfcDevice device, HardwareOptions options)
        {
            _device = device;
            _options = options;
        }

        public bool IsAuthorised
        {
            get { return ConfigurationProblem().Length == 0; }
        }

        public string LastErrorMessage
        {
            get
            {
                string problem = ConfigurationProblem();
                return problem.Length > 0 ? problem : _lastError;
            }
        }

        /// <summary>
        /// Why loading is refused, or empty when the deployment has answered
        /// everything. Kept as one place so the reason reaches the kiosk screen
        /// instead of a generic denial.
        /// </summary>
        private string ConfigurationProblem()
        {
            if (!_options.CardWriteEnabled)
            {
                return "Karta yazma bu otomatta kapalı (CardWriteEnabled=false).";
            }
            if (_options.TerminalNo == 0 || _options.TerminalUid == 0 || _options.CompanyId == 0)
            {
                return "Terminal kimliği eksik: TerminalNo, TerminalUid ve CompanyId tanımlanmalı.";
            }
            if (!string.Equals(_options.CardWriteAmountUnit, "Minor", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(_options.CardWriteAmountUnit, "Major", StringComparison.OrdinalIgnoreCase))
            {
                return "CardWriteAmountUnit 'Minor' (kuruş) veya 'Major' (lira) olmalı; " +
                       "doğrulanmadan yükleme yapılmaz.";
            }
            return string.Empty;
        }

        public CardLoadResponse Load(CardLoadRequest request)
        {
            var refused = new CardLoadResponse
            {
                IsLoaded = false,
                BalanceAfterMinor = request == null ? 0 : request.BalanceBeforeMinor
            };

            string problem = ConfigurationProblem();
            if (problem.Length > 0)
            {
                refused.StatusMessage = problem;
                return refused;
            }
            if (request == null || request.AmountMinor <= 0)
            {
                refused.StatusMessage = "Geçersiz yükleme tutarı.";
                return refused;
            }

            long amount = request.AmountMinor;
            if (string.Equals(_options.CardWriteAmountUnit, "Major", StringComparison.OrdinalIgnoreCase))
            {
                if (amount % 100 != 0)
                {
                    // Rounding here would silently short-change somebody by up to a
                    // lira on every load.
                    refused.StatusMessage = "Tutar tam lira değil, 'Major' biriminde yüklenemez.";
                    return refused;
                }
                amount /= 100;
            }

            if (amount > uint.MaxValue)
            {
                refused.StatusMessage = "Tutar vendor kütüphanesinin sınırını aşıyor.";
                return refused;
            }

            int referenceNo = Interlocked.Increment(ref _referenceCounter);
            string error;
            bool ok = _device.TryTopup(
                _options.TerminalNo, _options.TerminalUid, _options.CompanyId,
                referenceNo, (uint)amount, out error);

            if (!ok)
            {
                _lastError = error;
                refused.StatusMessage = error;
                return refused;
            }

            return new CardLoadResponse
            {
                IsLoaded = true,
                // Deliberately the expected figure and nothing more. The card is asked
                // for its own balance separately, and that read - not this number - is
                // what decides whether the load counts.
                BalanceAfterMinor = request.BalanceBeforeMinor + request.AmountMinor,
                StatusMessage = "Vendor Topup çağrısı başarılı döndü."
            };
        }
    }
}
