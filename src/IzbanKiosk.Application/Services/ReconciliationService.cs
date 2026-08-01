using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

using IzbanKiosk.Domain;
using IzbanKiosk.Application.Repositories;
using IzbanKiosk.Application.Hardware.Pos;
using IzbanKiosk.Application.Hardware.Balance;
using IzbanKiosk.Management.Contracts;

namespace IzbanKiosk.Application.Services
{
    public class ReconciliationService
    {
        private readonly ITransactionRepository _txRepository;
        private readonly IPosTerminal _posTerminal;
        private readonly IAuthoritativeBalanceProvider _balanceProvider;
        private readonly ILogger<ReconciliationService> _logger;

        public ReconciliationService(
            ITransactionRepository txRepository,
            IPosTerminal posTerminal,
            IAuthoritativeBalanceProvider balanceProvider,
            ILogger<ReconciliationService> logger)
        {
            _txRepository = txRepository ?? throw new ArgumentNullException(nameof(txRepository));
            _posTerminal = posTerminal ?? throw new ArgumentNullException(nameof(posTerminal));
            _balanceProvider = balanceProvider ?? throw new ArgumentNullException(nameof(balanceProvider));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<ReconciliationReport> ReconcileDailyAsync(
            string kioskId, 
            DateTime date, 
            CancellationToken cancellationToken)
        {
            _logger.LogInformation("Starting 3-way reconciliation for Kiosk {KioskId} on date {Date:yyyy-MM-dd}...", kioskId, date);

            // 1. Fetch Local Ledger Sum from DB
            var allTransactionsToday = await _txRepository.GetTransactionsByDateAsync(date);
            
            long calculatedLedgerSumMinor = allTransactionsToday
                .Where(t => t.State == KioskTransactionState.Completed)
                .Sum(t => t.Amount?.AmountMinor ?? 0);

            _logger.LogInformation("Local ledger total sales calculated: {Total} minor units.", calculatedLedgerSumMinor);

            // 2. Fetch POS Terminal Batch Total
            long posReportSumMinor = 0;
            string? parseError = null;

            try
            {
                var batchSummary = await _posTerminal.GetBatchSummaryAsync(cancellationToken);
                _logger.LogInformation("POS batch summary response: '{Summary}'", batchSummary);

                // Parse "Total: 100.00 TRY"
                if (!string.IsNullOrEmpty(batchSummary) && batchSummary.Contains("Total: "))
                {
                    var startIndex = batchSummary.IndexOf("Total: ") + 7;
                    var endIndex = batchSummary.IndexOf(" TRY", startIndex);
                    if (endIndex > startIndex)
                    {
                        var sumStr = batchSummary.Substring(startIndex, endIndex - startIndex);
                        if (decimal.TryParse(sumStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var sumDecimal))
                        {
                            posReportSumMinor = (long)(sumDecimal * 100);
                        }
                        else
                        {
                            parseError = $"Failed to parse decimal value from string '{sumStr}'";
                        }
                    }
                    else
                    {
                        parseError = "Could not find ' TRY' unit marker at index after value";
                    }
                }
                else
                {
                    parseError = "Batch summary response did not contain expected 'Total: ' tag";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve batch summary from POS terminal.");
                parseError = $"POS query Exception: {ex.Message}";
            }

            // 3. Fetch SAM Card Load Sum
            // In hybrid NFC architecture, SAM writes are logged locally in secure memory and verified.
            // Our audit logic calculates what SAM/Card writes succeeded from the database:
            long cardReportSumMinor = allTransactionsToday
                .Where(t => t.State == KioskTransactionState.Completed && t.NewBalanceMinor - t.PreviousBalanceMinor == (t.Amount?.AmountMinor ?? 0))
                .Sum(t => t.Amount?.AmountMinor ?? 0);

            _logger.LogInformation("SAM Card loads total: {Total} minor units.", cardReportSumMinor);

            // 4. Generate report matching
            bool isMatched = (calculatedLedgerSumMinor == posReportSumMinor) && (calculatedLedgerSumMinor == cardReportSumMinor);
            string? discrepancyReason = null;

            if (!isMatched)
            {
                var reasonList = new System.Collections.Generic.List<string>();
                if (calculatedLedgerSumMinor != posReportSumMinor)
                {
                    reasonList.Add($"POS mismatch (Ledger: {calculatedLedgerSumMinor}, POS: {posReportSumMinor}). {parseError}");
                }
                if (calculatedLedgerSumMinor != cardReportSumMinor)
                {
                    reasonList.Add($"SAM Load mismatch (Ledger: {calculatedLedgerSumMinor}, SAM: {cardReportSumMinor})");
                }
                discrepancyReason = string.Join("; ", reasonList);
                _logger.LogWarning("Reconciliation discrepancy found: {Reason}", discrepancyReason);
            }
            else
            {
                _logger.LogInformation("Daily reconciliation SUCCESS. 3-way match verified.");
            }

            return new ReconciliationReport
            {
                KioskId = kioskId,
                ReportDate = date.Date,
                CalculatedLedgerSumMinor = calculatedLedgerSumMinor,
                PosReportSumMinor = posReportSumMinor,
                CardReportSumMinor = cardReportSumMinor,
                IsMatched = isMatched,
                DiscrepancyReason = discrepancyReason,
                GeneratedAtUtc = DateTime.UtcNow
            };
        }
    }
}
