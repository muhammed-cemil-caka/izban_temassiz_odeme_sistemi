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
            return Run(apply, false);
        }

        /// <param name="interactive">
        /// Puts a tie to the operator on the console instead of leaving it unresolved.
        /// Field kiosks ship without the diagnostics screen, so installation is the
        /// only moment anyone can answer; an unattended run must still never guess.
        /// </param>
        public static AutoConfigureOutcome Run(bool apply, bool interactive)
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
            List<PrinterCandidate> printerChoices;
            List<string> portChoices;

            outcome.PrinterOk = KioskAutoConfigurationPolicy.TryPickPrinter(
                printers, settings.ThermalPrinterName, out picked, out reason, out printerChoices);

            if (!outcome.PrinterOk && interactive && printerChoices.Count > 1)
            {
                var names = new List<string>();
                foreach (PrinterCandidate candidate in printerChoices)
                {
                    names.Add(candidate.Name + "   (port " + candidate.PortName + ")");
                }

                int chosen = AskOperator("Termal yazici hangisi?", names);
                if (chosen >= 0)
                {
                    picked = printerChoices[chosen].Name;
                    reason = "Operator secti: " + picked;
                    outcome.PrinterOk = true;
                }
            }

            outcome.PrinterName = picked;
            outcome.PrinterMessage = reason;
            if (outcome.PrinterOk && apply &&
                !string.Equals(picked, settings.ThermalPrinterName, StringComparison.Ordinal))
            {
                KioskAutoConfigurationPolicy.WriteStringSetting(sourcePath, "ThermalPrinterName", picked);
                outcome.PrinterChanged = true;
            }

            outcome.ComPortOk = KioskAutoConfigurationPolicy.TryPickComPort(
                serialPorts, printers, settings.NfcComPort, out picked, out reason, out portChoices);

            if (!outcome.ComPortOk && interactive && portChoices.Count > 1)
            {
                int chosen = AskOperator("NFC okuyucu hangi portta?", portChoices);
                if (chosen >= 0)
                {
                    picked = portChoices[chosen];
                    reason = "Operator secti: " + picked;
                    outcome.ComPortOk = true;
                }
            }

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

        /// <summary>
        /// Numbers the options and reads one back. Returns -1 when the operator
        /// declines or types anything else - skipping is always allowed, because a
        /// wrong queue is worse than an unconfigured one.
        /// </summary>
        private static int AskOperator(string question, List<string> options)
        {
            Console.WriteLine();
            Console.WriteLine("        " + question);
            for (int i = 0; i < options.Count; i++)
            {
                Console.WriteLine("          " + (i + 1) + ") " + options[i]);
            }
            Console.WriteLine("          0) Simdi secme");
            Console.Write("        Numara: ");

            string? answer = Console.ReadLine();
            int number;
            if (!int.TryParse((answer ?? string.Empty).Trim(), out number) ||
                number < 1 || number > options.Count)
            {
                Console.WriteLine("        Secim yapilmadi.");
                return -1;
            }
            return number - 1;
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
