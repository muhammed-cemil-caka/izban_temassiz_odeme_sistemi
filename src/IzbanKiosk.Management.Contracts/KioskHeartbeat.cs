using System;
using System.Collections.Generic;

namespace IzbanKiosk.Management.Contracts
{
    public class KioskHeartbeat
    {
        public string KioskId { get; set; } = string.Empty;
        public Dictionary<string, string> HardwareStatus { get; set; } = new();
        public int PendingTransactionCount { get; set; }
        public DateTime? LastTransactionTime { get; set; }
        public string AppVersion { get; set; } = "1.0.0";
        public long DiskFreeSpaceBytes { get; set; }
        public DateTime SentAtUtc { get; set; } = DateTime.UtcNow;
    }
}
