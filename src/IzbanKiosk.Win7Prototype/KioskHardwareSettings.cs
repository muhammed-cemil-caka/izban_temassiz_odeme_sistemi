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

            if (StationName.Length == 0 || StationName.Length > 40)
            {
                throw new InvalidDataException("StationName is missing or invalid.");
            }

            if (!Regex.IsMatch(KioskNumber, "^[0-9]{1,10}$"))
            {
                throw new InvalidDataException("KioskNumber must be the digits printed on the kiosk, e.g. 51591.");
            }
        }
    }
}
