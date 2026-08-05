using System;
using System.IO;
using System.IO.Ports;
using System.Diagnostics;

namespace IzbanKiosk.LegacyHardwareBridge.Diagnostics
{
    public class ComPortOwnershipGuard
    {
        public bool IsPortAlreadyAcquiredByUs { get; set; } = false;

        public bool IsComPortAvailable(string portName, out string errorMessage)
        {
            errorMessage = string.Empty;

            // 1. Check if legacy AUSKiosk.exe is currently running
            Process[] processes = Process.GetProcessesByName("AUSKiosk");
            if (processes.Length > 0)
            {
                errorMessage = "Legacy application 'AUSKiosk.exe' is currently running and occupying hardware ports. Please close it.";
                return false;
            }

            // 2. Validate port format
            if (string.IsNullOrEmpty(portName) || !portName.StartsWith("COM", StringComparison.OrdinalIgnoreCase))
            {
                errorMessage = $"Invalid COM port name: '{portName}'";
                return false;
            }

            // If we have already initialized the device and opened it, we bypass the physical test open
            if (IsPortAlreadyAcquiredByUs)
            {
                return true;
            }

            // 3. Check if serial port exists
            string[] availablePorts = SerialPort.GetPortNames();
            bool portExists = false;
            foreach (var p in availablePorts)
            {
                if (string.Equals(p, portName, StringComparison.OrdinalIgnoreCase))
                {
                    portExists = true;
                    break;
                }
            }

            if (!portExists)
            {
                errorMessage = $"COM port '{portName}' does not exist on this machine. Available ports: '{string.Join(", ", availablePorts)}'";
                return false;
            }

            // 4. Test opening the port to verify if another process owns it
            try
            {
                using (var testPort = new SerialPort(portName))
                {
                    testPort.Open();
                    testPort.Close();
                }
            }
            catch (UnauthorizedAccessException)
            {
                errorMessage = $"COM Port '{portName}' is currently held occupied by another application.";
                return false;
            }
            catch (Exception ex)
            {
                errorMessage = $"COM Port '{portName}' test failed: {ex.Message}";
                return false;
            }

            return true;
        }
    }
}
