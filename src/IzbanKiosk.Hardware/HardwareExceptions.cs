using System;

namespace IzbanKiosk.Hardware
{
    public class HardwareNotConfiguredException : Exception
    {
        public HardwareNotConfiguredException(string message) : base(message) { }
    }

    public class VendorSdkUnavailableException : Exception
    {
        public VendorSdkUnavailableException(string message) : base(message) { }
    }

    public class TerminalNotActivatedException : Exception
    {
        public TerminalNotActivatedException(string message) : base(message) { }
    }

    public class BalanceProviderUnavailableException : Exception
    {
        public BalanceProviderUnavailableException(string message) : base(message) { }
    }

    public class SamUnavailableException : Exception
    {
        public SamUnavailableException(string message) : base(message) { }
    }
    
    public class UnsupportedVendorCapabilityException : Exception
    {
        public UnsupportedVendorCapabilityException(string message) : base(message) { }
    }
}
