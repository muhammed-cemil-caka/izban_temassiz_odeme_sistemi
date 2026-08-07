using System.IO;
using IzbanKiosk.LegacyHardwareBridge.Configuration;

namespace IzbanKiosk.Tests;

/// <summary>
/// Covers the choice a first-time installation makes on the operator's behalf.
///
/// The failure these guard against is silent: a kiosk pointed at the wrong queue
/// accepts every receipt, reports success and produces no paper, and nobody
/// notices until a passenger complains. Refusing to choose is always allowed;
/// choosing wrongly is not.
/// </summary>
public class KioskAutoConfigurationPolicyTests
{
    private static PrinterCandidate Printer(string name, string driver = "", string port = "USB001")
        => new PrinterCandidate(name, driver, port);

    [Fact]
    public void ConfiguredPrinterThatIsInstalledIsKept()
    {
        var printers = new List<PrinterCandidate>
        {
            Printer("Trentino Printer Driver 56mm"),
            Printer("EPSON TM-T20II Receipt")
        };

        bool resolved = KioskAutoConfigurationPolicy.TryPickPrinter(
            printers, "EPSON TM-T20II Receipt", out string picked, out _);

        Assert.True(resolved);
        Assert.Equal("EPSON TM-T20II Receipt", picked);
    }

    [Fact]
    public void ConfiguredPrinterMatchesCaseInsensitivelyButKeepsTheSpoolerSpelling()
    {
        var printers = new List<PrinterCandidate> { Printer("Trentino Printer Driver 56mm") };

        bool resolved = KioskAutoConfigurationPolicy.TryPickPrinter(
            printers, "trentino printer driver 56MM", out string picked, out _);

        Assert.True(resolved);
        Assert.Equal("Trentino Printer Driver 56mm", picked);
    }

    [Fact]
    public void VirtualPrintersAreNeverChosen()
    {
        var printers = new List<PrinterCandidate>
        {
            Printer("Microsoft Print to PDF", port: "PORTPROMPT:"),
            Printer("Microsoft XPS Document Writer", port: "XPSPort:"),
            Printer("Fax", port: "SHRFAX:"),
            Printer("POS-58 Thermal", port: "USB001")
        };

        bool resolved = KioskAutoConfigurationPolicy.TryPickPrinter(
            printers, string.Empty, out string picked, out _);

        Assert.True(resolved);
        Assert.Equal("POS-58 Thermal", picked);
    }

    [Fact]
    public void OnlyVirtualPrintersMeansNoChoiceAndAnActionableReason()
    {
        var printers = new List<PrinterCandidate>
        {
            Printer("Microsoft Print to PDF", port: "PORTPROMPT:"),
            Printer("Fax", port: "SHRFAX:")
        };

        bool resolved = KioskAutoConfigurationPolicy.TryPickPrinter(
            printers, string.Empty, out string picked, out string reason);

        Assert.False(resolved);
        Assert.Equal(string.Empty, picked);
        Assert.Contains("sanal", reason);
    }

    [Fact]
    public void ASinglePhysicalPrinterIsChosenEvenWithAnUnrecognisedName()
    {
        var printers = new List<PrinterCandidate>
        {
            Printer("Microsoft Print to PDF", port: "PORTPROMPT:"),
            Printer("KP-201", driver: "KP Series", port: "USB002")
        };

        bool resolved = KioskAutoConfigurationPolicy.TryPickPrinter(
            printers, string.Empty, out string picked, out _);

        Assert.True(resolved);
        Assert.Equal("KP-201", picked);
    }

    [Fact]
    public void TwoPhysicalPrintersWithNoThermalMarkerAreLeftToTheOperator()
    {
        var printers = new List<PrinterCandidate>
        {
            Printer("KP-201", port: "USB002"),
            Printer("HP LaserJet 1020", port: "USB003")
        };

        bool resolved = KioskAutoConfigurationPolicy.TryPickPrinter(
            printers, string.Empty, out string picked, out string reason);

        Assert.False(resolved);
        Assert.Equal(string.Empty, picked);
        Assert.Contains("Birden fazla aday", reason);
        Assert.Contains("KP-201", reason);
        Assert.Contains("HP LaserJet 1020", reason);
    }

    [Fact]
    public void AThermalMarkerBreaksTheTieAgainstAnOfficePrinter()
    {
        var printers = new List<PrinterCandidate>
        {
            Printer("HP LaserJet 1020", port: "USB003"),
            Printer("Trentino Printer Driver 56mm", port: "USB001")
        };

        bool resolved = KioskAutoConfigurationPolicy.TryPickPrinter(
            printers, string.Empty, out string picked, out _);

        Assert.True(resolved);
        Assert.Equal("Trentino Printer Driver 56mm", picked);
    }

    [Fact]
    public void TwoThermalCandidatesAreLeftToTheOperator()
    {
        // The duplicate-queue case: a re-enumerated USB printer leaves several
        // identically shaped queues and only one of them reaches the device.
        var printers = new List<PrinterCandidate>
        {
            Printer("POS-58 Thermal", port: "USB001"),
            Printer("POS-58 Thermal (Copy 1)", port: "USB004")
        };

        bool resolved = KioskAutoConfigurationPolicy.TryPickPrinter(
            printers, string.Empty, out _, out string reason);

        Assert.False(resolved);
        Assert.Contains("Birden fazla aday", reason);
    }

    [Fact]
    public void AConfiguredNameThatIsNotInstalledDoesNotBlockDetection()
    {
        // The package ships with a placeholder name. It must not pin the kiosk to a
        // queue that does not exist on this machine.
        var printers = new List<PrinterCandidate> { Printer("POS-58 Thermal", port: "USB001") };

        bool resolved = KioskAutoConfigurationPolicy.TryPickPrinter(
            printers, "Trentino Printer Driver 56mm", out string picked, out _);

        Assert.True(resolved);
        Assert.Equal("POS-58 Thermal", picked);
    }

    [Fact]
    public void NoPrintersInstalledIsReportedAsADriverProblem()
    {
        bool resolved = KioskAutoConfigurationPolicy.TryPickPrinter(
            new List<PrinterCandidate>(), string.Empty, out _, out string reason);

        Assert.False(resolved);
        Assert.Contains("surucusunu kurun", reason);
    }

    [Fact]
    public void ConfiguredComPortThatExistsIsKept()
    {
        bool resolved = KioskAutoConfigurationPolicy.TryPickComPort(
            new List<string> { "COM1", "COM4" }, new List<PrinterCandidate>(), "COM4",
            out string picked, out _);

        Assert.True(resolved);
        Assert.Equal("COM4", picked);
    }

    [Fact]
    public void TheOnlyFreeComPortIsChosenWhenTheConfiguredOneIsAbsent()
    {
        bool resolved = KioskAutoConfigurationPolicy.TryPickComPort(
            new List<string> { "COM3" }, new List<PrinterCandidate>(), "COM4",
            out string picked, out _);

        Assert.True(resolved);
        Assert.Equal("COM3", picked);
    }

    [Fact]
    public void APortThatAPrinterPrintsToIsNeverGivenToTheReader()
    {
        var printers = new List<PrinterCandidate> { Printer("POS-58 Thermal", port: "COM3") };

        bool resolved = KioskAutoConfigurationPolicy.TryPickComPort(
            new List<string> { "COM3", "COM5" }, printers, "COM4", out string picked, out _);

        Assert.True(resolved);
        Assert.Equal("COM5", picked);
    }

    [Fact]
    public void TwoFreeComPortsAreLeftToTheOperator()
    {
        bool resolved = KioskAutoConfigurationPolicy.TryPickComPort(
            new List<string> { "COM3", "COM5" }, new List<PrinterCandidate>(), "COM4",
            out string picked, out string reason);

        Assert.False(resolved);
        Assert.Equal(string.Empty, picked);
        Assert.Contains("Birden fazla seri port", reason);
    }

    [Fact]
    public void EveryPortBelongingToAPrinterMeansNoReaderPort()
    {
        var printers = new List<PrinterCandidate> { Printer("POS-58 Thermal", port: "COM3") };

        bool resolved = KioskAutoConfigurationPolicy.TryPickComPort(
            new List<string> { "COM3" }, printers, "COM4", out _, out string reason);

        Assert.False(resolved);
        Assert.Contains("Bos seri port yok", reason);
    }

    [Fact]
    public void NoSerialPortsAtAllKeepsTheConfiguredValue()
    {
        bool resolved = KioskAutoConfigurationPolicy.TryPickComPort(
            new List<string>(), new List<PrinterCandidate>(), "COM4", out string picked, out string reason);

        Assert.False(resolved);
        Assert.Equal(string.Empty, picked);
        Assert.Contains("COM4", reason);
    }

    [Fact]
    public void WritingAPrinterNameLeavesEveryOtherSettingAlone()
    {
        // The regression this exists for: a round-trip through the bridge's own
        // two-property model would silently delete the update settings and turn
        // automatic updates off on every kiosk it configured.
        const string json = """
        {
          "NfcComPort": "COM4",
          "ThermalPrinterName": "Trentino Printer Driver 56mm",
          "StationName": "",
          "KioskNumber": "",
          "LegacySetupIniPath": "",
          "UpdateEnabled": true,
          "UpdateRepositoryOwner": "muhammed-cemil-caka",
          "UpdateRepositoryName": "izban_temassiz_odeme_sistemi",
          "UpdateCheckHour": 4
        }
        """;

        string updated = KioskAutoConfigurationPolicy.ReplaceStringSetting(
            json, "ThermalPrinterName", "POS-58 Thermal", "test.json");

        Assert.Contains("\"ThermalPrinterName\": \"POS-58 Thermal\"", updated);
        Assert.Contains("\"UpdateEnabled\": true", updated);
        Assert.Contains("\"UpdateRepositoryOwner\": \"muhammed-cemil-caka\"", updated);
        Assert.Contains("\"UpdateCheckHour\": 4", updated);
        Assert.Contains("\"NfcComPort\": \"COM4\"", updated);
        Assert.DoesNotContain("Trentino", updated);
    }

    [Fact]
    public void WritingTheComPortDoesNotDisturbThePrinterName()
    {
        const string json = """
        {"NfcComPort": "COM4", "ThermalPrinterName": "POS-58 Thermal"}
        """;

        string updated = KioskAutoConfigurationPolicy.ReplaceStringSetting(
            json, "NfcComPort", "COM7", "test.json");

        Assert.Contains("\"NfcComPort\": \"COM7\"", updated);
        Assert.Contains("\"ThermalPrinterName\": \"POS-58 Thermal\"", updated);
    }

    [Fact]
    public void AMissingKeyIsRefusedRatherThanAppended()
    {
        const string json = """{"SomethingElse": "x"}""";

        Assert.Throws<InvalidDataException>(() =>
            KioskAutoConfigurationPolicy.ReplaceStringSetting(json, "NfcComPort", "COM7", "test.json"));
    }

    [Fact]
    public void AQuoteInAPrinterNameIsRefusedRatherThanBreakingTheFile()
    {
        const string json = """{"ThermalPrinterName": "x"}""";

        Assert.Throws<InvalidDataException>(() =>
            KioskAutoConfigurationPolicy.ReplaceStringSetting(
                json, "ThermalPrinterName", "Bad\"Name", "test.json"));
    }
}
