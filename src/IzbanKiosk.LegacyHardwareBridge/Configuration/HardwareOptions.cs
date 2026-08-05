using System;

namespace IzbanKiosk.LegacyHardwareBridge.Configuration
{
    public class HardwareOptions
    {
        public string NfcComPort { get; set; } = "COM4";
        public string PrinterName { get; set; } = string.Empty;
        
        // Balance verification options
        public int BalanceScale { get; set; } = 100;
        // The deployed AUSKiosk stores monetary values in kurus (for example the
        // MaxCardBalance=125000 setting represents 1,250.00 TRY). ReadOffCard.balance
        // therefore maps directly to minor units and is rendered by dividing by 100.
        public bool BalanceScaleVerified { get; set; } = true;
        public string Currency { get; set; } = "TRY";

        // Loaded only from IZBAN_HMAC_SECRET at process startup. Never provide a source-code fallback.
        public string HmacKeyBase64 { get; set; } = string.Empty;
    }
}
