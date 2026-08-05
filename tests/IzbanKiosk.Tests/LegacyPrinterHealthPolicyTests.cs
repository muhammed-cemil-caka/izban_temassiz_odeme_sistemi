using IzbanKiosk.LegacyHardwareBridge.Printer;

namespace IzbanKiosk.Tests;

public sealed class LegacyPrinterHealthPolicyTests
{
    [Fact]
    public void WindowsSpoolerReady_ShouldBeReadyWithoutVendorFallback()
    {
        var result = LegacyPrinterHealthPolicy.Evaluate(
            "Trentino Printer Driver 56mm", true, true, 0, 0, false, -1, string.Empty);

        Assert.True(result.IsReady);
        Assert.True(result.IsSpoolerRunning);
        Assert.Contains("Windows spooler", result.StatusMessage);
    }

    [Fact]
    public void OpenPrinterFailure_WithSuccessfulLegacyVendorProbe_ShouldRemainFailClosed()
    {
        // The vendor probe only proves KioskPrint.dll loaded. It says nothing about the
        // named queue being reachable for this kiosk user, so it must not promise paper.
        var result = LegacyPrinterHealthPolicy.Evaluate(
            "Trentino Printer Driver 56mm", false, false, 0, 1801, true, 0, string.Empty);

        Assert.False(result.IsReady);
        Assert.Contains("Win32 error=1801", result.StatusMessage);
        Assert.Contains("job count=0", result.StatusMessage);
    }

    [Fact]
    public void SpoolerBacklogAboveLegacyThreshold_ShouldNotBeReady()
    {
        var result = LegacyPrinterHealthPolicy.Evaluate(
            "Trentino Printer Driver 56mm",
            true,
            true,
            0,
            0,
            true,
            LegacyPrinterHealthPolicy.MaxHealthyQueueBacklog + 1,
            string.Empty);

        Assert.False(result.IsReady);
        Assert.True(result.IsSpoolerRunning);
        Assert.Contains("backlog is 4 jobs", result.StatusMessage);
    }

    [Fact]
    public void SpoolerBacklogAtLegacyThreshold_ShouldStayReady()
    {
        var result = LegacyPrinterHealthPolicy.Evaluate(
            "Trentino Printer Driver 56mm",
            true,
            true,
            0,
            0,
            true,
            LegacyPrinterHealthPolicy.MaxHealthyQueueBacklog,
            string.Empty);

        Assert.True(result.IsReady);
    }

    [Fact]
    public void PhysicalFaultOutranksQueueBacklog()
    {
        var result = LegacyPrinterHealthPolicy.Evaluate(
            "Trentino Printer Driver 56mm", true, true, 0x00000010u, 0, true, 12, string.Empty);

        Assert.False(result.IsReady);
        Assert.Contains("PAPER OUT", result.StatusMessage);
    }

    [Theory]
    [InlineData(0x00000080u, "OFFLINE")]
    [InlineData(0x00000010u, "PAPER OUT")]
    [InlineData(0x00400000u, "OPEN")]
    public void AuthoritativeWindowsPhysicalFault_ShouldRemainFailClosed(uint status, string expected)
    {
        var result = LegacyPrinterHealthPolicy.Evaluate(
            "Trentino Printer Driver 56mm", true, true, status, 0, true, 0, string.Empty);

        Assert.False(result.IsReady);
        Assert.Contains(expected, result.StatusMessage);
    }

    [Fact]
    public void OpenPrinterAndVendorProbeFailure_ShouldRemainFailClosedWithDiagnostics()
    {
        var result = LegacyPrinterHealthPolicy.Evaluate(
            "Trentino Printer Driver 56mm", false, false, 0, 5, false, -1, "DllNotFoundException");

        Assert.False(result.IsReady);
        Assert.Contains("Win32 error=5", result.StatusMessage);
        Assert.Contains("DllNotFoundException", result.StatusMessage);
    }

    [Fact]
    public void NegativeVendorJobCount_ShouldNotBeTreatedAsReady()
    {
        var result = LegacyPrinterHealthPolicy.Evaluate(
            "Trentino Printer Driver 56mm", false, false, 0, 0, true, -1, string.Empty);

        Assert.False(result.IsReady);
        Assert.Contains("invalid job count=-1", result.StatusMessage);
    }
}
