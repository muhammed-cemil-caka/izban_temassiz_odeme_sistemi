using System;
using System.IO;
using Newtonsoft.Json;

namespace IzbanKiosk.LegacyHardwareBridge.Configuration
{
    /// <summary>
    /// Deployment-owned hardware identity, shared with the Windows 7 kiosk shell.
    ///
    /// The shell passes <c>--port</c>/<c>--printer</c> when it launches the bridge,
    /// but the bridge is also started by hand for the documented diagnostic runs.
    /// Reading the same <c>KioskHardware.config.json</c> keeps those runs from
    /// falling back to "no printer configured", which used to make every
    /// <c>--printer-health</c> and <c>--print-test</c> invocation fail on a kiosk
    /// whose printer was perfectly healthy.
    /// </summary>
    public sealed class BridgeHardwareConfigFile
    {
        public const string FileName = "KioskHardware.config.json";

        public string NfcComPort { get; set; } = string.Empty;
        public string ThermalPrinterName { get; set; } = string.Empty;

        // Card write. Absent keys leave the refusing defaults in HardwareOptions
        // untouched, so an older settings file cannot switch loading on by accident.
        public bool CardWriteEnabled { get; set; }
        public int TerminalNo { get; set; }
        public long TerminalUid { get; set; }
        public int CompanyId { get; set; }
        public string CardWriteAmountUnit { get; set; } = string.Empty;
        public int TopupReferenceSeed { get; set; }

        public static bool TryLoad(out BridgeHardwareConfigFile settings, out string sourcePath, out string error)
        {
            settings = new BridgeHardwareConfigFile();
            sourcePath = string.Empty;
            error = string.Empty;

            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string[] candidates =
            {
                Path.Combine(baseDirectory, FileName),
                // Prepare-Win7HardwareTestPackage.ps1 puts the settings file at the
                // package root while the bridge itself lives in Bridge\.
                Path.GetFullPath(Path.Combine(baseDirectory, "..", FileName))
            };

            foreach (string candidate in candidates)
            {
                if (!File.Exists(candidate))
                {
                    continue;
                }

                try
                {
                    BridgeHardwareConfigFile? parsed = JsonConvert.DeserializeObject<BridgeHardwareConfigFile>(
                        File.ReadAllText(candidate));
                    if (parsed == null)
                    {
                        error = "Hardware settings file could not be parsed: " + candidate;
                        return false;
                    }

                    parsed.NfcComPort = (parsed.NfcComPort ?? string.Empty).Trim();
                    parsed.ThermalPrinterName = (parsed.ThermalPrinterName ?? string.Empty).Trim();
                    parsed.CardWriteAmountUnit = (parsed.CardWriteAmountUnit ?? string.Empty).Trim();
                    settings = parsed;
                    sourcePath = candidate;
                    return true;
                }
                catch (Exception ex)
                {
                    error = "Hardware settings file could not be read (" + candidate + "): " + ex.Message;
                    return false;
                }
            }

            error = FileName + " was not found next to the bridge executable or in its parent folder.";
            return false;
        }
    }
}
