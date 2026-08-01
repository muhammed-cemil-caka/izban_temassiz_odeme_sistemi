using System;

namespace IzbanKiosk.Domain
{
    public record Money
    {
        public long AmountMinor { get; }
        public string Currency { get; }

        public Money(long amountMinor, string currency = "TRY")
        {
            if (amountMinor < 0)
            {
                throw new ArgumentException("Amount cannot be negative.", nameof(amountMinor));
            }
            if (string.IsNullOrWhiteSpace(currency))
            {
                throw new ArgumentException("Currency must be specified.", nameof(currency));
            }

            AmountMinor = amountMinor;
            Currency = currency.ToUpperInvariant();
        }

        public static Money FromDecimal(decimal amount, string currency = "TRY")
        {
            if (amount < 0)
            {
                throw new ArgumentException("Amount cannot be negative.", nameof(amount));
            }
            // Check overflow
            checked
            {
                long minor = (long)Math.Round(amount * 100, MidpointRounding.AwayFromZero);
                return new Money(minor, currency);
            }
        }

        public decimal ToDecimal()
        {
            return AmountMinor / 100m;
        }

        public Money Add(Money other)
        {
            ValidateCurrency(other);
            checked
            {
                return new Money(AmountMinor + other.AmountMinor, Currency);
            }
        }

        public Money Subtract(Money other)
        {
            ValidateCurrency(other);
            if (AmountMinor < other.AmountMinor)
            {
                throw new InvalidOperationException("Resulting amount would be negative.");
            }
            return new Money(AmountMinor - other.AmountMinor, Currency);
        }

        private void ValidateCurrency(Money other)
        {
            if (Currency != other.Currency)
            {
                throw new InvalidOperationException($"Currency mismatch: {Currency} vs {other.Currency}");
            }
        }

        public override string ToString()
        {
            return $"{ToDecimal():F2} {Currency}";
        }
    }
}
