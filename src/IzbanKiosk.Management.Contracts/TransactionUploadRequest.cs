using System;
using System.Collections.Generic;

namespace IzbanKiosk.Management.Contracts
{
    public class TransactionUploadItem
    {
        public Guid TransactionId { get; set; }
        public string IdempotencyKey { get; set; } = string.Empty;
        public string CardUid { get; set; } = string.Empty;
        public long AmountMinor { get; set; }
        public string? PosVendorReference { get; set; }
        public string? LoadVendorReference { get; set; }
        public string State { get; set; } = string.Empty;
        public DateTime CreatedAtUtc { get; set; }
    }

    public class TransactionUploadRequest
    {
        public string KioskId { get; set; } = string.Empty;
        public List<TransactionUploadItem> Items { get; set; } = new();
    }
}
