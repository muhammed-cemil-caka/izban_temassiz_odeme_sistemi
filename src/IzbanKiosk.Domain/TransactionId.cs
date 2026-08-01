using System;

namespace IzbanKiosk.Domain
{
    public record TransactionId
    {
        public Guid Value { get; }

        public TransactionId(Guid value)
        {
            if (value == Guid.Empty)
            {
                throw new ArgumentException("TransactionId cannot be empty.", nameof(value));
            }
            Value = value;
        }

        public static TransactionId NewId() => new(Guid.NewGuid());

        public override string ToString() => Value.ToString();
    }
}
