using System;
using System.Collections.Generic;
using IzbanKiosk.LegacyHardwareBridge.Printer;

namespace IzbanKiosk.LegacyHardwareBridge.Configuration
{
    /// <summary>
    /// Asks Windows what hardware this kiosk has, hands the answer to
    /// <see cref="KioskAutoConfigurationPolicy"/>, and saves what it decides.
    ///
    /// Only the plumbing lives here - enumerating the spooler and the serial ports,
    /// then writing the file. The rules themselves are kept apart so they can be
    /// tested on a machine with no spooler.
    /// </summary>
    public static class KioskAutoConfigurator
    {
        public sealed class AutoConfigureOutcome
        {
            public string SettingsPath = string.Empty;
            public bool PrinterOk;
            public bool PrinterChanged;
            public string PrinterName = string.Empty;
            public string PrinterMessage = string.Empty;
            public bool ComPortOk;
            public bool ComPortChanged;
            public string ComPort = string.Empty;
            public string ComPortMessage = string.Empty;
        }

        /// <summary>
        /// Resolves both settings and, when <paramref name="apply"/> is set, saves
        /// whichever of them could be resolved.
        /// </summary>
        public static AutoConfigureOutcome Run(bool apply)
        {
            var outcome = new AutoConfigureOutcome();

            BridgeHardwareConfigFile settings;
            string sourcePath;
            string loadError;
            if (!BridgeHardwareConfigFile.TryLoad(out settings, out sourcePath, out loadError))
            {
                outcome.PrinterMessage = loadError;
                outcome.ComPortMessage = loadError;
                return outcome;
            }
            outcome.SettingsPath = sourcePath;

            List<PrinterCandidate> printers = ReadPrinters();
            List<string> serialPorts = WindowsPrinterEnvironment.ListSerialPorts();

            string picked;
            string reason;

            outcome.PrinterOk = KioskAutoConfigurationPolicy.TryPickPrinter(
                printers, settings.ThermalPrinterName, out picked, out reason);
            outcome.PrinterName = picked;
            outcome.PrinterMessage = reason;
            if (outcome.PrinterOk && apply &&
                !string.Equals(picked, settings.ThermalPrinterName, StringComparison.Ordinal))
            {
                KioskAutoConfigurationPolicy.WriteStringSetting(sourcePath, "ThermalPrinterName", picked);
                outcome.PrinterChanged = true;
            }

            outcome.ComPortOk = KioskAutoConfigurationPolicy.TryPickComPort(
                serialPorts, printers, settings.NfcComPort, out picked, out reason);
            outcome.ComPort = picked;
            outcome.ComPortMessage = reason;
            if (outcome.ComPortOk && apply &&
                !string.Equals(picked, settings.NfcComPort, StringComparison.OrdinalIgnoreCase))
            {
                KioskAutoConfigurationPolicy.WriteStringSetting(sourcePath, "NfcComPort", picked);
                outcome.ComPortChanged = true;
            }

            return outcome;
        }

        private static List<PrinterCandidate> ReadPrinters()
        {
            var candidates = new List<PrinterCandidate>();
            foreach (WindowsPrinterInfo printer in WindowsPrinterEnvironment.ListInstalledPrinterDetails())
            {
                candidates.Add(new PrinterCandidate(printer.Name, printer.DriverName, printer.PortName));
            }
            return candidates;
        }
    }
}
