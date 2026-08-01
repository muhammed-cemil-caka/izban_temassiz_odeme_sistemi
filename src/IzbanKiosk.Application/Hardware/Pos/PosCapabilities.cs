namespace IzbanKiosk.Application.Hardware.Pos
{
    public record PosCapabilities
    {
        public bool SupportsSale { get; }
        public bool SupportsPreAuthorization { get; }
        public bool SupportsCapture { get; }
        public bool SupportsQueryByReference { get; }
        public bool SupportsGetLastTransaction { get; }
        public bool SupportsCancel { get; }
        public bool SupportsVoid { get; }
        public bool SupportsReversal { get; }
        public bool SupportsBatchClose { get; }
        public bool SupportsIdempotencyReference { get; }

        public PosCapabilities(
            bool supportsSale = true,
            bool supportsPreAuthorization = false,
            bool supportsCapture = false,
            bool supportsQueryByReference = false,
            bool supportsGetLastTransaction = false,
            bool supportsCancel = false,
            bool supportsVoid = false,
            bool supportsReversal = false,
            bool supportsBatchClose = false,
            bool supportsIdempotencyReference = false)
        {
            SupportsSale = supportsSale;
            SupportsPreAuthorization = supportsPreAuthorization;
            SupportsCapture = supportsCapture;
            SupportsQueryByReference = supportsQueryByReference;
            SupportsGetLastTransaction = supportsGetLastTransaction;
            SupportsCancel = supportsCancel;
            SupportsVoid = supportsVoid;
            SupportsReversal = supportsReversal;
            SupportsBatchClose = supportsBatchClose;
            SupportsIdempotencyReference = supportsIdempotencyReference;
        }
    }
}
