using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace IzbanKiosk.LegacyHardwareBridge.Configuration
{
    /// <summary>
    /// One printer queue as far as the choice is concerned.
    ///
    /// Deliberately not the spooler's own structure: the decision depends only on
    /// the name, the driver and the port, and keeping it that way lets the rules be
    /// tested off Windows, where no spooler exists.
    /// </summary>
    public sealed class PrinterCandidate
    {
        public string Name = string.Empty;
        public string DriverName = string.Empty;
        public string PortName = string.Empty;

        public PrinterCandidate()
        {
        }

        public PrinterCandidate(string name, string driverName, string portName)
        {
            Name = name ?? string.Empty;
            DriverName = driverName ?? string.Empty;
            PortName = portName ?? string.Empty;
        }
    }

    /// <summary>
    /// Decides which printer queue is the receipt printer and which serial port is
    /// the card reader, for a kiosk being installed for the first time.
    ///
    /// Both values used to be filled in by hand on site: read the queue name off a
    /// console listing, then type it into a JSON file on a machine that has no
    /// keyboard. That is the step installations actually failed on, because a name
    /// that is one character off produces a kiosk that looks healthy and silently
    /// never prints.
    ///
    /// The rule throughout is that a wrong answer is worse than no answer. When the
    /// evidence does not single out one device these methods report what they found
    /// and choose nothing, leaving the operator to pick on the SİSTEM TANILA screen.
    /// </summary>
    public static class KioskAutoConfigurationPolicy
    {
        /// <summary>
        /// Queues that exist on almost every Windows install and can never be the
        /// receipt printer. Picking one would send every receipt into a save dialog
        /// that nobody is standing in front of.
        /// </summary>
        private static readonly string[] VirtualPrinterMarkers =
        {
            "pdf", "xps", "fax", "onenote", "send to", "microsoft print",
            "adobe", "cutepdf", "foxit", "document writer", "snagit",
            "print to file", "generic / text"
        };

        /// <summary>
        /// Names and drivers seen on kiosk receipt printers. Vendor and paper-width
        /// markers both count: the Alsancak machine's queue is named after its driver
        /// ("Trentino ... 56mm") while others carry only a model number.
        /// </summary>
        private static readonly string[] ThermalMarkers =
        {
            "thermal", "receipt", "fis", "pos", "trentino",
            "58mm", "56mm", "80mm", "76mm",
            "bixolon", "epson tm", "tm-t", "tm-u", "star tsp", "citizen",
            "sam4s", "rongta", "xprinter", "gprinter", "zj-", "custom vkp",
            "seiko", "sewoo", "posiflex", "metapace", "birch"
        };

        /// <summary>
        /// Chooses the receipt queue. An already-configured name that is really
        /// installed always wins: an operator who set it deliberately, or an earlier
        /// run of this code, must not be second-guessed on every reinstall.
        /// </summary>
        public static bool TryPickPrinter(
            List<PrinterCandidate> printers, string configured, out string picked, out string reason)
        {
            List<PrinterCandidate> ignored;
            return TryPickPrinter(printers, configured, out picked, out reason, out ignored);
        }

        /// <param name="choices">
        /// The queues that were tied when no choice could be made, so a caller with a
        /// console can put them to the operator. Empty whenever the result is decided:
        /// field kiosks have no diagnostics screen, so an unresolved printer has to be
        /// settled during installation or not at all.
        /// </param>
        public static bool TryPickPrinter(
            List<PrinterCandidate> printers, string configured, out string picked, out string reason,
            out List<PrinterCandidate> choices)
        {
            picked = string.Empty;
            reason = string.Empty;
            choices = new List<PrinterCandidate>();

            if (printers == null || printers.Count == 0)
            {
                reason = "Windows'ta hic yazici kurulu degil. Once termal yazicinin surucusunu kurun.";
                return false;
            }

            string wanted = (configured ?? string.Empty).Trim();
            if (wanted.Length > 0)
            {
                foreach (PrinterCandidate printer in printers)
                {
                    if (string.Equals(printer.Name.Trim(), wanted, StringComparison.OrdinalIgnoreCase))
                    {
                        picked = printer.Name;
                        reason = "Yapilandirilmis yazici kurulu, degistirilmedi: " + printer.Name;
                        return true;
                    }
                }
            }

            var real = new List<PrinterCandidate>();
            foreach (PrinterCandidate printer in printers)
            {
                if (!IsVirtual(printer))
                {
                    real.Add(printer);
                }
            }

            if (real.Count == 0)
            {
                reason = "Kurulu yazicilarin hepsi sanal (PDF/XPS/Fax); termal yazici kurulu degil: " +
                    Join(printers);
                return false;
            }

            var thermal = new List<PrinterCandidate>();
            foreach (PrinterCandidate printer in real)
            {
                if (LooksThermal(printer))
                {
                    thermal.Add(printer);
                }
            }

            // A single physical queue is unambiguous whether or not its name happens
            // to contain a keyword this code knows: a kiosk has one printer.
            List<PrinterCandidate> candidates = thermal.Count > 0 ? thermal : real;
            if (candidates.Count == 1)
            {
                picked = candidates[0].Name;
                reason = "Secildi: " + picked + "  (port " + candidates[0].PortName + ")";
                return true;
            }

            choices = candidates;
            reason = "Birden fazla aday var, secim yapilmadi: " + Join(candidates);
            return false;
        }

        /// <summary>
        /// Chooses the reader's serial port.
        ///
        /// Ports that a print queue already prints to are removed first. A thermal
        /// printer on COM3 is otherwise indistinguishable from a card reader, and
        /// pointing the reader at the printer produces a kiosk that reads no cards.
        /// </summary>
        public static bool TryPickComPort(
            List<string> serialPorts, List<PrinterCandidate> printers, string configured,
            out string picked, out string reason)
        {
            List<string> ignored;
            return TryPickComPort(serialPorts, printers, configured, out picked, out reason, out ignored);
        }

        /// <param name="choices">
        /// The free ports that were tied, for the same reason as on the printer.
        /// </param>
        public static bool TryPickComPort(
            List<string> serialPorts, List<PrinterCandidate> printers, string configured,
            out string picked, out string reason, out List<string> choices)
        {
            picked = string.Empty;
            reason = string.Empty;
            choices = new List<string>();

            string wanted = (configured ?? string.Empty).Trim();
            if (serialPorts == null || serialPorts.Count == 0)
            {
                reason = "Windows hic seri port bildirmiyor. Okuyucu takili degilse veya surucusu " +
                    "yuklenmemisse boyle gorunur. Yapilandirilan port korundu: " +
                    (wanted.Length == 0 ? "[bos]" : wanted);
                return false;
            }

            foreach (string port in serialPorts)
            {
                if (string.Equals(port, wanted, StringComparison.OrdinalIgnoreCase))
                {
                    picked = port;
                    reason = "Yapilandirilmis port mevcut, degistirilmedi: " + port;
                    return true;
                }
            }

            var free = new List<string>();
            foreach (string port in serialPorts)
            {
                if (!IsUsedByAPrinter(port, printers))
                {
                    free.Add(port);
                }
            }

            if (free.Count == 1)
            {
                picked = free[0];
                reason = "Secildi: " + picked + "  (yapilandirilan " +
                    (wanted.Length == 0 ? "[bos]" : wanted) + " bu makinede yok)";
                return true;
            }

            choices = free;
            reason = free.Count == 0
                ? "Bos seri port yok; bulunan portlarin hepsi bir yazici kuyruguna bagli."
                : "Birden fazla seri port var, secim yapilmadi: " + string.Join(" | ", free.ToArray());
            return false;
        }

        private static bool IsUsedByAPrinter(string port, List<PrinterCandidate> printers)
        {
            if (printers == null)
            {
                return false;
            }

            foreach (PrinterCandidate printer in printers)
            {
                if (string.Equals(printer.PortName.Trim(), port, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsVirtual(PrinterCandidate printer)
        {
            string haystack = (printer.Name + " " + printer.DriverName + " " + printer.PortName).ToLowerInvariant();
            foreach (string marker in VirtualPrinterMarkers)
            {
                if (haystack.IndexOf(marker, StringComparison.Ordinal) >= 0)
                {
                    return true;
                }
            }

            // A queue whose port is a file or a nul device cannot produce paper even
            // when its name looks like a real device.
            string port = printer.PortName.Trim().ToLowerInvariant();
            return port.Length == 0
                || port == "nul:" || port == "nul"
                || port.StartsWith("file:", StringComparison.Ordinal);
        }

        private static bool LooksThermal(PrinterCandidate printer)
        {
            string haystack = (printer.Name + " " + printer.DriverName).ToLowerInvariant();
            foreach (string marker in ThermalMarkers)
            {
                if (haystack.IndexOf(marker, StringComparison.Ordinal) >= 0)
                {
                    return true;
                }
            }
            return false;
        }

        private static string Join(List<PrinterCandidate> printers)
        {
            var names = new List<string>();
            foreach (PrinterCandidate printer in printers)
            {
                names.Add("'" + printer.Name + "'");
            }
            return names.Count == 0 ? "[yok]" : string.Join(" | ", names.ToArray());
        }

        /// <summary>
        /// Replaces one string value in the settings file, in place.
        ///
        /// Deserialising and re-serialising would be shorter, but this file also
        /// carries the update settings, the kiosk number and the station name, and
        /// the bridge's own model has no properties for those. Round-tripping through
        /// it would drop them and switch the kiosk's automatic updates off as a side
        /// effect of choosing a printer.
        /// </summary>
        public static void WriteStringSetting(string path, string key, string value)
        {
            File.WriteAllText(path, ReplaceStringSetting(File.ReadAllText(path), key, value, path));
        }

        /// <summary>
        /// The text transformation behind <see cref="WriteStringSetting"/>, separated
        /// so it can be exercised without touching a disk.
        /// </summary>
        public static string ReplaceStringSetting(string json, string key, string value, string pathForError)
        {
            if (value == null || value.IndexOf('"') >= 0 || value.IndexOf('\\') >= 0)
            {
                throw new InvalidDataException(
                    "Value for " + key + " contains a character that cannot be written safely: " + value);
            }

            var pattern = new Regex("(\"" + Regex.Escape(key) + "\"\\s*:\\s*)\"[^\"]*\"");
            if (!pattern.IsMatch(json))
            {
                throw new InvalidDataException(
                    key + " was not found in " + pathForError + "; this is not the expected settings file.");
            }

            return pattern.Replace(json, "$1\"" + value + "\"", 1);
        }
    }
}
