using System;

namespace IzbanKiosk.Application.Hardware.Balance
{
    public record BalanceResult
    {
        public bool IsAuthoritative { get; }
        public bool IsVerified { get; }
        public long BalanceMinor { get; }
        public DateTime TimestampUtc { get; }
        public bool IsStale { get; }

        public BalanceResult(bool isAuthoritative, bool isVerified, long balanceMinor, DateTime timestampUtc, bool isStale = false)
        {
            IsAuthoritative = isAuthoritative;
            IsVerified = isVerified;
            BalanceMinor = balanceMinor;
            TimestampUtc = timestampUtc;
            IsStale = isStale;
        }

        public static BalanceResult Unverified(long balanceMinor)
        {
            return new BalanceResult(false, false, balanceMinor, DateTime.UtcNow, false);
        }

        public static BalanceResult VerifiedAuthoritative(long balanceMinor)
        {
            return new BalanceResult(true, true, balanceMinor, DateTime.UtcNow, false);
        }
    }
}
