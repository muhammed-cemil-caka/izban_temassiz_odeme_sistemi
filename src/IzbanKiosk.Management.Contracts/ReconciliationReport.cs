using System;

namespace IzbanKiosk.Management.Contracts
{
    public class ReconciliationReport
    {
        public string KioskId { get; set; } = string.Empty;
        public DateTime ReportDate { get; set; }
        public long CalculatedLedgerSumMinor { get; set; }
        public long PosReportSumMinor { get; set; }
        public long CardReportSumMinor { get; set; }
        public bool IsMatched { get; set; }
        public string? DiscrepancyReason { get; set; }
        public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;
    }
}
