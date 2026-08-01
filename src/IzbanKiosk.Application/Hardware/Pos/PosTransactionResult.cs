namespace IzbanKiosk.Application.Hardware.Pos
{
    public record PosTransactionResult
    {
        public bool Success { get; }
        public string? ApprovalCode { get; }
        public string? VendorReference { get; }
        public string? ResponseCode { get; }
        public string? ErrorCode { get; }
        public string? ErrorMessage { get; }
        public long AmountMinor { get; }

        public PosTransactionResult(
            bool success,
            string? approvalCode,
            string? vendorReference,
            string? responseCode = null,
            string? errorCode = null,
            string? errorMessage = null,
            long amountMinor = 0)
        {
            Success = success;
            ApprovalCode = approvalCode;
            VendorReference = vendorReference;
            ResponseCode = responseCode;
            ErrorCode = errorCode;
            ErrorMessage = errorMessage;
            AmountMinor = amountMinor;
        }

        public static PosTransactionResult Failed(string errorCode, string errorMessage)
        {
            return new PosTransactionResult(false, null, null, null, errorCode, errorMessage);
        }
    }
}
