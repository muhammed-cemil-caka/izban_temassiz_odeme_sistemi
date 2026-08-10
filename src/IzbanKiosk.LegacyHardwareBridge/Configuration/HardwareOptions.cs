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

        // Card write. Every default here is the refusing one: an unconfigured kiosk
        // must decline to load rather than write with a made-up terminal identity or
        // guess which unit the vendor library wants the amount in.
        public bool CardWriteEnabled { get; set; } = false;
        public ushort TerminalNo { get; set; } = 0;
        public uint TerminalUid { get; set; } = 0;
        public byte CompanyId { get; set; } = 0;
        // "Minor" (kurus) or "Major" (lira). Blank until someone has verified which
        // one the deployed AUSKiosk passes to Topup; the top-up flow proves the choice
        // by reading the balance back after the write.
        public string CardWriteAmountUnit { get; set; } = string.Empty;

        // Loaded only from IZBAN_HMAC_SECRET at process startup. Never provide a source-code fallback.
        public string HmacKeyBase64 { get; set; } = string.Empty;
    }
}
