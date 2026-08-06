using System;
using System.IO;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace IzbanKiosk.Win7Prototype
{
    /// <summary>
    /// Deployment-owned hardware settings.  They intentionally live next to the
    /// executable so the hardware identity is not hidden in source code or a
    /// Windows-user-specific setting.
    /// </summary>
    internal sealed class KioskHardwareSettings
    {
        private const string SettingsFileName = "KioskHardware.config.json";

        public string NfcComPort { get; set; } = string.Empty;
        public string ThermalPrinterName { get; set; } = string.Empty;

        // Printed on every passenger receipt, so it must come from the machine it is
        // deployed on rather than from source: the same package is installed on every
        // kiosk in the fleet.
        public string StationName { get; set; } = string.Empty;
        public string KioskNumber { get; set; } = string.Empty;

        /// <summary>
        /// Path to the legacy AUSKiosk <c>setup.ini</c>. Its <c>[SETUP] No=</c> line is
        /// the number the machine itself carries, and the legacy application reads the
        /// same value, so it is preferred over <see cref="KioskNumber"/> when present.
        /// Leave empty to search the usual install locations.
        /// </summary>
        public string LegacySetupIniPath { get; set; } = string.Empty;

        /// <summary>Where <see cref="KioskNumber"/> ended up coming from, for display.</summary>
        internal string KioskNumberSource { get; private set; } = string.Empty;

        /// <summary>
        /// True when station and kiosk number are both known, i.e. a receipt can carry
        /// a truthful identity. False does not stop the kiosk: card reading and balance
        /// display need no identity, so only receipt printing is withheld.
        /// </summary>
        internal bool IsIdentityComplete
        {
            get { return IdentityProblem.Length == 0; }
        }

        internal string IdentityProblem { get; private set; } = string.Empty;

        public static KioskHardwareSettings LoadFromApplicationDirectory()
        {
            string settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, SettingsFileName);
            if (!File.Exists(settingsPath))
            {
                throw new FileNotFoundException(
                    "Required hardware settings file is missing: " + SettingsFileName,
                    settingsPath);
            }

            string json = File.ReadAllText(settingsPath);
            KioskHardwareSettings? settings = JsonConvert.DeserializeObject<KioskHardwareSettings>(json);
            if (settings == null)
            {
                throw new InvalidDataException("Hardware settings file could not be parsed.");
            }

            settings.ResolveKioskNumber();
            settings.Validate();
            return settings;
        }

        /// <summary>
        /// Rewrites the settings file with a new thermal printer queue.
        ///
        /// Needed because the live queue can only be found by trying them: a USB
        /// printer that has been re-enumerated leaves several identically named
        /// queues on different ports, and nothing in Windows says which one the
        /// device is actually behind. Making that a two-tap operation on the kiosk
        /// beats hand-editing JSON on a machine with no keyboard.
        /// </summary>
        internal static void SaveThermalPrinterName(string printerName)
        {
            string trimmed = (printerName ?? string.Empty).Trim();
            if (trimmed.Length == 0 || trimmed.Length > 128 || trimmed.IndexOf('"') >= 0)
            {
                throw new InvalidDataException("ThermalPrinterName is missing or invalid.");
            }

            string settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, SettingsFileName);
            KioskHardwareSettings settings = LoadFromApplicationDirectory();
            settings.ThermalPrinterName = trimmed;

            File.WriteAllText(settingsPath, JsonConvert.SerializeObject(settings, Formatting.Indented));
        }

        /// <summary>
        /// Takes the kiosk number from the machine rather than from the copied
        /// configuration whenever the legacy setup.ini can be found.
        ///
        /// The whole package is copied from kiosk to kiosk, so a hand-edited number
        /// travels with it and silently prints another machine's identity on
        /// passengers' receipts. setup.ini stays behind on each machine, which makes
        /// it the safer source.
        /// </summary>
        private void ResolveKioskNumber()
        {
            KioskNumber = (KioskNumber ?? string.Empty).Trim();
            KioskNumberSource = KioskNumber.Length == 0 ? string.Empty : SettingsFileName;

            foreach (string candidate in GetSetupIniCandidates())
            {
                string detected;
                if (TryReadKioskNumberFromSetupIni(candidate, out detected))
                {
                    KioskNumber = detected;
                    KioskNumberSource = candidate;
                    return;
                }
            }
        }

        private System.Collections.Generic.IEnumerable<string> GetSetupIniCandidates()
        {
            string configured = (LegacySetupIniPath ?? string.Empty).Trim();
            if (configured.Length > 0)
            {
                yield return configured;
                // An explicitly configured path is a deliberate answer; do not silently
                // fall back to a guess if it is wrong.
                yield break;
            }

            yield return @"C:\AUSKiosk\setup.ini";
            yield return @"C:\Program Files\AUSKiosk\setup.ini";
            yield return @"D:\AUSKiosk\setup.ini";
            yield return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "setup.ini");
        }

        private static bool TryReadKioskNumberFromSetupIni(string path, out string kioskNumber)
        {
            kioskNumber = string.Empty;
            try
            {
                if (!File.Exists(path))
                {
                    return false;
                }

                foreach (string rawLine in File.ReadAllLines(path))
                {
                    string line = rawLine.Trim();
                    if (!line.StartsWith("No=", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string value = line.Substring(3).Trim();
                    if (Regex.IsMatch(value, "^[0-9]{1,10}$"))
                    {
                        kioskNumber = value;
                        return true;
                    }
                    return false;
                }
            }
            catch
            {
                // An unreadable legacy file must never stop the kiosk; the configured
                // value stays in force.
            }
            return false;
        }

        private void Validate()
        {
            NfcComPort = (NfcComPort ?? string.Empty).Trim();
            ThermalPrinterName = (ThermalPrinterName ?? string.Empty).Trim();
            StationName = (StationName ?? string.Empty).Trim();
            KioskNumber = (KioskNumber ?? string.Empty).Trim();

            if (!Regex.IsMatch(NfcComPort, "^COM[1-9][0-9]*$", RegexOptions.IgnoreCase))
            {
                throw new InvalidDataException("NfcComPort must be a Windows COM port such as COM4.");
            }

            if (ThermalPrinterName.Length == 0 || ThermalPrinterName.Length > 128 || ThermalPrinterName.IndexOf('"') >= 0)
            {
                throw new InvalidDataException("ThermalPrinterName is missing or invalid.");
            }

            // Station and kiosk number are deliberately NOT fatal. The reader and the
            // printer are what the kiosk cannot run without; an unknown identity only
            // means a receipt would carry the wrong or no origin, so the receipt is
            // withheld while the passenger-facing balance enquiry keeps working.
            IdentityProblem = string.Empty;

            if (StationName.Length == 0 || StationName.Length > 40)
            {
                IdentityProblem = "StationName is not set in " + SettingsFileName + ".";
                return;
            }

            if (!Regex.IsMatch(KioskNumber, "^[0-9]{1,10}$"))
            {
                IdentityProblem = "Kiosk number could not be determined. It is read from the legacy setup.ini " +
                    "([SETUP] No=) when that file is found, otherwise from KioskNumber in " + SettingsFileName + ".";
            }
        }
    }
}
