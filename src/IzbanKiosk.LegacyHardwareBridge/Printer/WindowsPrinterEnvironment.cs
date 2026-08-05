using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace IzbanKiosk.LegacyHardwareBridge.Printer
{
    /// <summary>
    /// Windows spooler facts and routing used by the legacy thermal printer path.
    ///
    /// KioskPrint.dll exposes no printer-name parameter. It is a Delphi/VCL library
    /// that resolves the Windows default printer through the win.ini mapped
    /// <c>[windows] device=</c> value (its import table contains
    /// <c>GetProfileStringA</c>) and then keeps that printer for the lifetime of the
    /// hosting process. Everything the bridge can do to control where paper comes
    /// out therefore has to happen through the Windows default queue, and it has to
    /// happen before the first vendor call.
    /// </summary>
    public sealed class WindowsPrinterInfo
    {
        public string Name = string.Empty;
        public string DriverName = string.Empty;
        public string PortName = string.Empty;
        public uint Status;
    }

    public static class WindowsPrinterEnvironment
    {
        public static string GetDefaultPrinterName()
        {
            int length = 0;
            GetDefaultPrinter(null, ref length);
            if (length <= 0)
            {
                return string.Empty;
            }

            var buffer = new StringBuilder(length);
            return GetDefaultPrinter(buffer, ref length) ? buffer.ToString() : string.Empty;
        }

        /// <summary>
        /// Names of every printer this Windows session can print to, local queues
        /// and per-user connections alike.
        /// </summary>
        public static List<string> ListInstalledPrinters()
        {
            var names = new List<string>();

            uint bytesNeeded;
            uint printersReturned;
            EnumPrinters(PrinterEnumLocal | PrinterEnumConnections, null, 4, IntPtr.Zero, 0, out bytesNeeded, out printersReturned);
            if (bytesNeeded == 0)
            {
                return names;
            }

            IntPtr buffer = Marshal.AllocHGlobal((int)bytesNeeded);
            try
            {
                if (!EnumPrinters(PrinterEnumLocal | PrinterEnumConnections, null, 4, buffer, bytesNeeded, out bytesNeeded, out printersReturned))
                {
                    return names;
                }

                int entrySize = Marshal.SizeOf(typeof(PRINTER_INFO_4));
                for (int i = 0; i < printersReturned; i++)
                {
                    IntPtr entry = new IntPtr(buffer.ToInt64() + (i * entrySize));
                    var info = (PRINTER_INFO_4)Marshal.PtrToStructure(entry, typeof(PRINTER_INFO_4));
                    if (!string.IsNullOrEmpty(info.pPrinterName))
                    {
                        names.Add(info.pPrinterName);
                    }
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }

            return names;
        }

        /// <summary>
        /// Matches the deployment-configured printer name against the queues that
        /// actually exist. A configured name that is a driver name, or that carries
        /// a typo, is the most common reason a kiosk silently stops producing paper,
        /// so the failure message lists what is installed instead.
        /// </summary>
        public static bool TryResolveInstalledPrinter(string configuredName, out string resolvedName, out string error)
        {
            resolvedName = string.Empty;
            error = string.Empty;

            string wanted = (configuredName ?? string.Empty).Trim();
            if (wanted.Length == 0)
            {
                error = "No thermal printer is configured.";
                return false;
            }

            List<string> installed = ListInstalledPrinters();
            foreach (string candidate in installed)
            {
                if (string.Equals(candidate.Trim(), wanted, StringComparison.OrdinalIgnoreCase))
                {
                    resolvedName = candidate;
                    return true;
                }
            }

            error = "Configured thermal printer '" + wanted + "' is not installed in Windows. Installed printers: " +
                (installed.Count == 0 ? "[none]" : string.Join(" | ", installed.ToArray())) +
                ". Current Windows default printer: '" + GetDefaultPrinterName() + "'.";
            return false;
        }

        public static bool TryReadPrinterInfo(string printerName, out WindowsPrinterInfo info, out int win32Error)
        {
            info = new WindowsPrinterInfo { Name = printerName };
            win32Error = 0;

            IntPtr printerHandle;
            if (!OpenPrinter(printerName, out printerHandle, IntPtr.Zero))
            {
                win32Error = Marshal.GetLastWin32Error();
                return false;
            }

            try
            {
                int bytesNeeded;
                GetPrinter(printerHandle, 2, IntPtr.Zero, 0, out bytesNeeded);
                if (bytesNeeded <= 0)
                {
                    win32Error = Marshal.GetLastWin32Error();
                    return false;
                }

                IntPtr buffer = Marshal.AllocHGlobal(bytesNeeded);
                try
                {
                    if (!GetPrinter(printerHandle, 2, buffer, bytesNeeded, out bytesNeeded))
                    {
                        win32Error = Marshal.GetLastWin32Error();
                        return false;
                    }

                    var info2 = (PRINTER_INFO_2)Marshal.PtrToStructure(buffer, typeof(PRINTER_INFO_2));
                    info.Name = info2.pPrinterName ?? printerName;
                    info.DriverName = info2.pDriverName ?? string.Empty;
                    info.PortName = info2.pPortName ?? string.Empty;
                    info.Status = info2.Status;
                    return true;
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
            finally
            {
                ClosePrinter(printerHandle);
            }
        }

        /// <summary>
        /// Makes the named queue the Windows default and makes the change visible to
        /// a GDI/VCL consumer inside this process.
        ///
        /// <c>SetDefaultPrinter</c> alone is not enough. It updates the registry, but
        /// Win32 keeps a per-process cache of the win.ini mapped <c>[windows] device</c>
        /// value that KioskPrint.dll reads, and that cache is only refreshed by a
        /// WM_SETTINGCHANGE notification. The extra <c>WriteProfileString</c> write
        /// updates the cache directly and the broadcast notifies everything else,
        /// including the legacy AUSKiosk process if it happens to be running.
        /// </summary>
        public static bool TryMakeDefault(string printerName, out string error)
        {
            error = string.Empty;

            string current = GetDefaultPrinterName();
            bool alreadyDefault = string.Equals(current, printerName, StringComparison.OrdinalIgnoreCase);

            WindowsPrinterInfo info;
            int readError;
            if (!TryReadPrinterInfo(printerName, out info, out readError))
            {
                error = "Thermal printer '" + printerName + "' could not be opened by this Windows user. Win32 error=" + readError +
                    ". Current Windows default printer: '" + current + "'.";
                return false;
            }

            if (!alreadyDefault && !SetDefaultPrinter(printerName))
            {
                error = "Windows refused to select thermal printer '" + printerName + "' as the default queue. Win32 error=" +
                    Marshal.GetLastWin32Error() + ". Current Windows default printer: '" + current + "'.";
                return false;
            }

            // Delphi's TPrinter reads the default through GetProfileString('windows','device').
            // Write the same value so the profile cache of this process cannot serve the
            // previous default (a PDF/XPS queue on the Windows Embedded image) to the
            // vendor DLL.
            WriteProfileString("windows", "device", info.Name + "," + info.DriverName + "," + info.PortName);
            BroadcastSettingChange();

            string verified = GetDefaultPrinterName();
            if (!string.Equals(verified, printerName, StringComparison.OrdinalIgnoreCase))
            {
                error = "Windows default printer stayed '" + verified + "' after selecting thermal printer '" + printerName + "'.";
                return false;
            }

            return true;
        }

        private static void BroadcastSettingChange()
        {
            UIntPtr result;
            SendMessageTimeout(
                HwndBroadcast,
                WmSettingChange,
                IntPtr.Zero,
                "windows",
                SmtoAbortIfHung,
                1000,
                out result);
        }

        #region Win32

        private const uint PrinterEnumLocal = 0x00000002;
        private const uint PrinterEnumConnections = 0x00000004;

        private static readonly IntPtr HwndBroadcast = new IntPtr(0xFFFF);
        private const uint WmSettingChange = 0x001A;
        private const uint SmtoAbortIfHung = 0x0002;

        [DllImport("winspool.drv", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool OpenPrinter(string pPrinterName, out IntPtr phPrinter, IntPtr pDefault);

        [DllImport("winspool.drv", SetLastError = true)]
        private static extern bool ClosePrinter(IntPtr hPrinter);

        [DllImport("winspool.drv", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool GetPrinter(IntPtr hPrinter, uint dwLevel, IntPtr pPrinter, int cbBuf, out int pcbNeeded);

        [DllImport("winspool.drv", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool EnumPrinters(
            uint flags,
            string? name,
            uint level,
            IntPtr pPrinterEnum,
            uint cbBuf,
            out uint pcbNeeded,
            out uint pcReturned);

        [DllImport("winspool.drv", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool SetDefaultPrinter(string pszPrinter);

        [DllImport("winspool.drv", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool GetDefaultPrinter(StringBuilder? pszBuffer, ref int pcchBuffer);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool WriteProfileString(string lpszSection, string lpszKeyName, string lpszString);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SendMessageTimeout(
            IntPtr hWnd,
            uint msg,
            IntPtr wParam,
            string lParam,
            uint fuFlags,
            uint uTimeout,
            out UIntPtr lpdwResult);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct PRINTER_INFO_4
        {
            public string pPrinterName;
            public string pServerName;
            public uint Attributes;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct PRINTER_INFO_2
        {
            public string pServerName;
            public string pPrinterName;
            public string pShareName;
            public string pPortName;
            public string pDriverName;
            public string pComment;
            public string pLocation;
            public IntPtr pDevMode;
            public string pSepFile;
            public string pPrintProcessor;
            public string pDatatype;
            public string pParameters;
            public IntPtr pSecurityDescriptor;
            public uint Attributes;
            public uint Priority;
            public uint DefaultPriority;
            public uint StartTime;
            public uint UntilTime;
            public uint Status;
            public uint cJobs;
            public uint AveragePPM;
        }

        #endregion
    }
}
