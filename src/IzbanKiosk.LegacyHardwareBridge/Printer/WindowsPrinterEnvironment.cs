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
        public uint Attributes;
        public int QueuedJobCount;
        public List<string> JobStates = new List<string>();
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
            foreach (WindowsPrinterInfo info in ListInstalledPrinterDetails())
            {
                names.Add(info.Name);
            }
            return names;
        }

        /// <summary>
        /// Every installed queue with the port it prints to and how many jobs are
        /// waiting on it.
        ///
        /// The port matters more than the name. A USB thermal printer that has been
        /// re-enumerated leaves behind a trail of duplicate queues - "(Copy 1)",
        /// "(Copy 2)" and so on - and only the queue bound to the port the device is
        /// actually on will ever produce paper. The others accept jobs and pile them
        /// up forever, which looks exactly like a dead printer.
        /// </summary>
        public static List<WindowsPrinterInfo> ListInstalledPrinterDetails()
        {
            var printers = new List<WindowsPrinterInfo>();

            uint bytesNeeded;
            uint printersReturned;
            EnumPrinters(PrinterEnumLocal | PrinterEnumConnections, null, 2, IntPtr.Zero, 0, out bytesNeeded, out printersReturned);
            if (bytesNeeded == 0)
            {
                return printers;
            }

            IntPtr buffer = Marshal.AllocHGlobal((int)bytesNeeded);
            try
            {
                if (!EnumPrinters(PrinterEnumLocal | PrinterEnumConnections, null, 2, buffer, bytesNeeded, out bytesNeeded, out printersReturned))
                {
                    return printers;
                }

                int entrySize = Marshal.SizeOf(typeof(PRINTER_INFO_2));
                for (int i = 0; i < printersReturned; i++)
                {
                    IntPtr entry = new IntPtr(buffer.ToInt64() + (i * entrySize));
                    var info = (PRINTER_INFO_2)Marshal.PtrToStructure(entry, typeof(PRINTER_INFO_2));
                    if (string.IsNullOrEmpty(info.pPrinterName))
                    {
                        continue;
                    }

                    printers.Add(new WindowsPrinterInfo
                    {
                        Name = info.pPrinterName,
                        DriverName = info.pDriverName ?? string.Empty,
                        PortName = info.pPortName ?? string.Empty,
                        Status = info.Status,
                        Attributes = info.Attributes,
                        QueuedJobCount = (int)info.cJobs
                    });
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }

            return printers;
        }

        /// <summary>
        /// Cancels every job waiting on the named queue.
        ///
        /// Only ever called from an explicit operator action. Stuck jobs on a kiosk
        /// thermal printer are receipts that will never be produced, and leaving them
        /// blocks every later receipt; but discarding someone's queued work must stay
        /// a deliberate decision, never an automatic recovery.
        /// </summary>
        public static bool TryPurgeQueue(string printerName, out int purgedJobCount, out string error)
        {
            purgedJobCount = 0;
            error = string.Empty;

            WindowsPrinterInfo info;
            int readError;
            if (TryReadPrinterInfo(printerName, out info, out readError))
            {
                purgedJobCount = info.QueuedJobCount;
            }

            var defaults = new PRINTER_DEFAULTS { DesiredAccess = PrinterAllAccess };
            IntPtr printerHandle;
            if (!OpenPrinterWithDefaults(printerName, out printerHandle, ref defaults))
            {
                error = "Queue '" + printerName + "' could not be opened for administration. Win32 error=" +
                    Marshal.GetLastWin32Error() + ". The kiosk user may lack the Manage Documents right.";
                return false;
            }

            try
            {
                if (!SetPrinter(printerHandle, 0, IntPtr.Zero, PrinterControlPurge))
                {
                    error = "Windows refused to purge queue '" + printerName + "'. Win32 error=" +
                        Marshal.GetLastWin32Error() + ".";
                    return false;
                }
                return true;
            }
            finally
            {
                ClosePrinter(printerHandle);
            }
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
                    info.Attributes = info2.Attributes;
                    info.QueuedJobCount = (int)info2.cJobs;
                    info.JobStates = ReadJobStates(printerHandle, info.QueuedJobCount);
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

            WindowsPrinterInfo info;
            int readError;
            if (!TryReadPrinterInfo(printerName, out info, out readError))
            {
                error = "Thermal printer '" + printerName + "' could not be opened by this Windows user. Win32 error=" + readError +
                    ". Current Windows default printer: '" + current + "'.";
                return false;
            }

            // SetDefaultPrinter is the modern API, but it is not the one that decides
            // where our receipts go and it is not always willing: it refuses some
            // perfectly openable queues with ERROR_INVALID_PRINTER_NAME (1801). Its
            // failure is recorded and then ignored, because the authoritative channel
            // for this kiosk is the next call.
            int setDefaultError = 0;
            if (!string.Equals(current, printerName, StringComparison.OrdinalIgnoreCase) && !SetDefaultPrinter(printerName))
            {
                setDefaultError = Marshal.GetLastWin32Error();
            }

            // This is the one that matters. KioskPrint.dll is Delphi/VCL and resolves
            // its target with GetProfileStringA on [windows] device, so writing that
            // value is not a fallback for SetDefaultPrinter - it is the direct route to
            // the only setting the vendor library actually reads.
            WriteProfileString("windows", "device", info.Name + "," + info.DriverName + "," + info.PortName);
            BroadcastSettingChange();

            string profileDevice = GetProfileDeviceName();
            if (string.Equals(profileDevice, printerName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            error = "Receipt routing could not be pointed at '" + printerName + "'. [windows] device is now '" + profileDevice +
                "' and the Windows default printer is '" + GetDefaultPrinterName() + "'.";
            if (setDefaultError != 0)
            {
                error += " SetDefaultPrinter Win32 error=" + setDefaultError + ".";
            }
            return false;
        }

        /// <summary>
        /// Per-job state for the queue.
        ///
        /// Printer-level status hides the two faults that matter most here: a queue
        /// set to work offline reports nothing at all, and a job the driver has given
        /// up on carries its own error bit while the printer still looks healthy.
        /// </summary>
        private static List<string> ReadJobStates(IntPtr printerHandle, int jobCount)
        {
            var states = new List<string>();
            if (jobCount <= 0)
            {
                return states;
            }

            uint bytesNeeded;
            uint jobsReturned;
            EnumJobs(printerHandle, 0, (uint)jobCount, 1, IntPtr.Zero, 0, out bytesNeeded, out jobsReturned);
            if (bytesNeeded == 0)
            {
                return states;
            }

            IntPtr buffer = Marshal.AllocHGlobal((int)bytesNeeded);
            try
            {
                if (!EnumJobs(printerHandle, 0, (uint)jobCount, 1, buffer, bytesNeeded, out bytesNeeded, out jobsReturned))
                {
                    return states;
                }

                int entrySize = Marshal.SizeOf(typeof(JOB_INFO_1));
                for (int i = 0; i < jobsReturned; i++)
                {
                    IntPtr entry = new IntPtr(buffer.ToInt64() + (i * entrySize));
                    var job = (JOB_INFO_1)Marshal.PtrToStructure(entry, typeof(JOB_INFO_1));
                    states.Add(DescribeJobStatus(job.Status));
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }

            return states;
        }

        private static string DescribeJobStatus(uint status)
        {
            var parts = new List<string>();
            if ((status & 0x00000001) != 0) parts.Add("PAUSED");
            if ((status & 0x00000002) != 0) parts.Add("ERROR");
            if ((status & 0x00000004) != 0) parts.Add("DELETING");
            if ((status & 0x00000008) != 0) parts.Add("SPOOLING");
            if ((status & 0x00000010) != 0) parts.Add("PRINTING");
            if ((status & 0x00000020) != 0) parts.Add("OFFLINE");
            if ((status & 0x00000040) != 0) parts.Add("PAPEROUT");
            if ((status & 0x00000080) != 0) parts.Add("PRINTED");
            if ((status & 0x00000100) != 0) parts.Add("DELETED");
            if ((status & 0x00000200) != 0) parts.Add("BLOCKED_DEVQ");
            if ((status & 0x00000400) != 0) parts.Add("USER_INTERVENTION");
            if ((status & 0x00000800) != 0) parts.Add("RESTART");
            if ((status & 0x00001000) != 0) parts.Add("COMPLETE");
            if ((status & 0x00002000) != 0) parts.Add("RETAINED");
            return parts.Count == 0 ? "QUEUED(" + status + ")" : string.Join("+", parts.ToArray());
        }

        public static bool IsWorkOffline(uint attributes)
        {
            return (attributes & PrinterAttributeWorkOffline) != 0;
        }

        /// <summary>
        /// Clears the "Use Printer Offline" flag.
        ///
        /// That setting is the quietest way for a Windows queue to swallow every job:
        /// no status bit, no Win32 error, jobs simply accumulate and no paper is ever
        /// produced. Only the Attributes field reveals it, and only an explicit write
        /// clears it.
        /// </summary>
        public static bool TryClearWorkOffline(string printerName, out string error)
        {
            error = string.Empty;

            var defaults = new PRINTER_DEFAULTS { DesiredAccess = PrinterAllAccess };
            IntPtr printerHandle;
            if (!OpenPrinterWithDefaults(printerName, out printerHandle, ref defaults))
            {
                error = "Queue '" + printerName + "' could not be opened for administration. Win32 error=" +
                    Marshal.GetLastWin32Error() + ".";
                return false;
            }

            try
            {
                int bytesNeeded;
                GetPrinter(printerHandle, 2, IntPtr.Zero, 0, out bytesNeeded);
                if (bytesNeeded <= 0)
                {
                    error = "Queue '" + printerName + "' settings could not be read. Win32 error=" + Marshal.GetLastWin32Error() + ".";
                    return false;
                }

                IntPtr buffer = Marshal.AllocHGlobal(bytesNeeded);
                try
                {
                    if (!GetPrinter(printerHandle, 2, buffer, bytesNeeded, out bytesNeeded))
                    {
                        error = "Queue '" + printerName + "' settings could not be read. Win32 error=" + Marshal.GetLastWin32Error() + ".";
                        return false;
                    }

                    // Edit the two fields in the raw buffer. Round-tripping through a
                    // managed struct would rewrite the string pointers that PRINTER_INFO_2
                    // carries into this same block.
                    int attributesOffset = (int)Marshal.OffsetOf(typeof(PRINTER_INFO_2), "Attributes");
                    var attributes = (uint)Marshal.ReadInt32(buffer, attributesOffset);
                    Marshal.WriteInt32(buffer, attributesOffset, (int)(attributes & ~PrinterAttributeWorkOffline));

                    // A NULL security descriptor tells SetPrinter to leave permissions alone.
                    Marshal.WriteIntPtr(buffer, (int)Marshal.OffsetOf(typeof(PRINTER_INFO_2), "pSecurityDescriptor"), IntPtr.Zero);

                    if (!SetPrinter(printerHandle, 2, buffer, 0))
                    {
                        error = "Windows refused to bring queue '" + printerName + "' back online. Win32 error=" +
                            Marshal.GetLastWin32Error() + ".";
                        return false;
                    }
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
        /// The printer name from the win.ini mapped <c>[windows] device</c> entry: the
        /// exact value KioskPrint.dll reads to decide where a receipt goes. It can
        /// differ from <see cref="GetDefaultPrinterName"/>, and when it does, this one
        /// is what produces - or fails to produce - paper.
        /// </summary>
        public static string GetProfileDeviceName()
        {
            var buffer = new StringBuilder(512);
            int length = GetProfileString("windows", "device", string.Empty, buffer, buffer.Capacity);
            if (length <= 0)
            {
                return string.Empty;
            }

            string value = buffer.ToString();
            int separator = value.IndexOf(',');
            return separator >= 0 ? value.Substring(0, separator).Trim() : value.Trim();
        }

        /// <summary>
        /// Tells other processes that the default printer moved.
        ///
        /// This must not block. A synchronous broadcast waits up to its timeout for
        /// <em>every</em> top-level window on the machine, and on a kiosk that is also
        /// running the legacy AUSKiosk shell that stalls the hardware process for
        /// seconds while it holds the single named-pipe instance - long enough for the
        /// operator's own diagnostics request to time out before it can connect.
        /// SendNotifyMessage returns immediately for windows owned by other processes,
        /// which is all we need: our own profile cache was already updated by the
        /// WriteProfileString call above.
        /// </summary>
        private static void BroadcastSettingChange()
        {
            SendNotifyMessage(HwndBroadcast, WmSettingChange, IntPtr.Zero, "windows");
        }

        #region Win32

        private const uint PrinterEnumLocal = 0x00000002;
        private const uint PrinterEnumConnections = 0x00000004;

        private const uint PrinterControlPurge = 3;
        private const uint PrinterAttributeWorkOffline = 0x00000400;
        private const uint PrinterAllAccess = 0x000F000C;

        private static readonly IntPtr HwndBroadcast = new IntPtr(0xFFFF);
        private const uint WmSettingChange = 0x001A;

        [DllImport("winspool.drv", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool OpenPrinter(string pPrinterName, out IntPtr phPrinter, IntPtr pDefault);

        [DllImport("winspool.drv", SetLastError = true)]
        private static extern bool ClosePrinter(IntPtr hPrinter);

        [DllImport("winspool.drv", EntryPoint = "OpenPrinter", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool OpenPrinterWithDefaults(string pPrinterName, out IntPtr phPrinter, ref PRINTER_DEFAULTS pDefault);

        [DllImport("winspool.drv", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool SetPrinter(IntPtr hPrinter, uint dwLevel, IntPtr pPrinter, uint dwCommand);

        [DllImport("winspool.drv", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool EnumJobs(IntPtr hPrinter, uint firstJob, uint noJobs, uint level,
            IntPtr pJob, uint cbBuf, out uint pcbNeeded, out uint pcReturned);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct JOB_INFO_1
        {
            public uint JobId;
            public string pPrinterName;
            public string pMachineName;
            public string pUserName;
            public string pDocument;
            public string pDatatype;
            public string pStatus;
            public uint Status;
            public uint Priority;
            public uint Position;
            public uint TotalPages;
            public uint PagesPrinted;
            public SYSTEMTIME Submitted;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SYSTEMTIME
        {
            public ushort Year, Month, DayOfWeek, Day, Hour, Minute, Second, Milliseconds;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PRINTER_DEFAULTS
        {
            public IntPtr pDatatype;
            public IntPtr pDevMode;
            public uint DesiredAccess;
        }

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

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern int GetProfileString(string lpAppName, string lpKeyName, string lpDefault, StringBuilder lpReturnedString, int nSize);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool SendNotifyMessage(IntPtr hWnd, uint msg, IntPtr wParam, string lParam);

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
