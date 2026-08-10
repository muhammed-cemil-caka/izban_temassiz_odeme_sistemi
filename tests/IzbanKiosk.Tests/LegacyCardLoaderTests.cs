using IzbanKiosk.LegacyHardware.Contracts;
using IzbanKiosk.LegacyHardwareBridge.Card;
using IzbanKiosk.LegacyHardwareBridge.Configuration;
using IzbanKiosk.LegacyHardwareBridge.Nfc;

namespace IzbanKiosk.Tests;

/// <summary>
/// Covers the gates in front of the one call that moves money onto a card.
///
/// Every one of these is a way a half-configured kiosk could write the wrong figure,
/// or write on behalf of a terminal the scheme does not recognise. The card cannot be
/// un-loaded afterwards, so the gates matter more than the write itself.
/// </summary>
public class LegacyCardLoaderTests
{
    private sealed class SpyDevice : ILegacyNfcDevice
    {
        public int TopupCalls;
        public uint LastAmount;
        public ushort LastTerminalNo;
        public bool Succeeds = true;

        public bool TryTopup(ushort terminalNo, uint terminalUid, byte companyId,
                             int referenceNo, uint amount, out string error)
        {
            TopupCalls++;
            LastAmount = amount;
            LastTerminalNo = terminalNo;
            error = Succeeds ? string.Empty : "vendor reddetti";
            return Succeeds;
        }

        public bool Initialize() => true;
        public bool OpenComm(string port) => true;
        public void CloseComm() { }
        public bool IsHardwareConnected() => true;
        public bool ResetSam() => true;
        public string LastSamStatusMessage => string.Empty;
        public bool CheckIfCardPresent(out string m, out string s) { m = ""; s = ""; return true; }
        public bool ReadCardSnapshot(string r, out CardSnapshotResponse s) { s = new CardSnapshotResponse(); return true; }
        public bool WaitForCardRemoval(TimeSpan t) => true;
        public void Shutdown() { }
    }

    private static HardwareOptions Configured() => new HardwareOptions
    {
        CardWriteEnabled = true,
        TerminalNo = 12,
        TerminalUid = 34567,
        CompanyId = 7,
        CardWriteAmountUnit = "Minor"
    };

    private static CardLoadRequest Request(long amountMinor = 2000) => new CardLoadRequest
    {
        IdempotencyKey = "k", AmountMinor = amountMinor, BalanceBeforeMinor = 5000
    };

    [Fact]
    public void WritingIsOffUntilTheDeploymentTurnsItOn()
    {
        var device = new SpyDevice();
        var loader = new LegacyCardLoader(device, new HardwareOptions());

        Assert.False(loader.IsAuthorised);
        Assert.False(loader.Load(Request()).IsLoaded);
        Assert.Equal(0, device.TopupCalls);
        Assert.Contains("CardWriteEnabled", loader.LastErrorMessage);
    }

    [Fact]
    public void AMissingTerminalIdentityRefusesRatherThanWritingAsTerminalZero()
    {
        var device = new SpyDevice();
        var options = Configured();
        options.TerminalNo = 0;
        var loader = new LegacyCardLoader(device, options);

        Assert.False(loader.IsAuthorised);
        Assert.False(loader.Load(Request()).IsLoaded);
        Assert.Equal(0, device.TopupCalls);
        Assert.Contains("Terminal kimliği", loader.LastErrorMessage);
    }

    [Fact]
    public void AnUndeclaredAmountUnitRefusesRatherThanGuessing()
    {
        // Guessing wrong here loads a hundred times too much or too little.
        var device = new SpyDevice();
        var options = Configured();
        options.CardWriteAmountUnit = "";
        var loader = new LegacyCardLoader(device, options);

        Assert.False(loader.IsAuthorised);
        Assert.Equal(0, device.TopupCalls);
        Assert.Contains("CardWriteAmountUnit", loader.LastErrorMessage);
    }

    [Fact]
    public void MinorUnitsArePassedThroughUnchanged()
    {
        var device = new SpyDevice();
        var loader = new LegacyCardLoader(device, Configured());

        Assert.True(loader.Load(Request(2000)).IsLoaded);
        Assert.Equal(2000u, device.LastAmount);
        Assert.Equal((ushort)12, device.LastTerminalNo);
    }

    [Fact]
    public void MajorUnitsAreConvertedAndNeverRounded()
    {
        var device = new SpyDevice();
        var options = Configured();
        options.CardWriteAmountUnit = "Major";
        var loader = new LegacyCardLoader(device, options);

        Assert.True(loader.Load(Request(2000)).IsLoaded);
        Assert.Equal(20u, device.LastAmount);

        // 20,50 TRY cannot be expressed in whole lira; rounding would quietly take
        // half a lira from somebody on every load.
        Assert.False(loader.Load(Request(2050)).IsLoaded);
        Assert.Equal(1, device.TopupCalls);
    }

    [Fact]
    public void AVendorFailureIsReportedAsNotLoaded()
    {
        var device = new SpyDevice { Succeeds = false };
        var loader = new LegacyCardLoader(device, Configured());

        CardLoadResponse response = loader.Load(Request());

        Assert.False(response.IsLoaded);
        Assert.Equal(5000, response.BalanceAfterMinor);
        Assert.Contains("vendor reddetti", response.StatusMessage);
    }

    [Fact]
    public void AZeroAmountNeverReachesTheCard()
    {
        var device = new SpyDevice();
        var loader = new LegacyCardLoader(device, Configured());

        Assert.False(loader.Load(Request(0)).IsLoaded);
        Assert.Equal(0, device.TopupCalls);
    }
}
