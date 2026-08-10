using IzbanKiosk.Terminal;

namespace IzbanKiosk.Tests;

/// <summary>
/// Covers how a kiosk decides which terminal it is to the card scheme.
///
/// The package ships these keys present and set to zero. Treating only an absent key
/// as "needs filling" left every freshly installed kiosk claiming to be terminal
/// zero, which the loader refuses - card loading would have been silently dead on
/// exactly the machines nobody had tested yet.
/// </summary>
public class TerminalIdentityTests
{
    private static KioskHardwareSettings WithKiosk(string number, int no = 0, long uid = 0)
        => new KioskHardwareSettings { KioskNumber = number, TerminalNo = no, TerminalUid = uid };

    [Fact]
    public void ZeroIsTreatedAsUnsetAndFilledFromTheKioskNumber()
    {
        KioskHardwareSettings settings = WithKiosk("51591");

        Assert.True(settings.ResolveTerminalIdentity());
        Assert.Equal(51591, settings.TerminalNo);
        Assert.Equal(51591L, settings.TerminalUid);
    }

    [Fact]
    public void AnExistingIdentityIsNeverOverwritten()
    {
        KioskHardwareSettings settings = WithKiosk("51591", no: 22, uid: 999);

        Assert.False(settings.ResolveTerminalIdentity());
        Assert.Equal(22, settings.TerminalNo);
        Assert.Equal(999L, settings.TerminalUid);
    }

    [Fact]
    public void OnlyTheMissingHalfIsFilled()
    {
        KioskHardwareSettings settings = WithKiosk("51591", no: 22, uid: 0);

        Assert.True(settings.ResolveTerminalIdentity());
        Assert.Equal(22, settings.TerminalNo);
        Assert.Equal(51591L, settings.TerminalUid);
    }

    [Fact]
    public void AKioskWithNoNumberIsLeftUnconfiguredRatherThanGuessed()
    {
        // The loader refuses on zero, which is the right outcome: writing to the
        // scheme under an identity nobody chose is worse than not writing.
        KioskHardwareSettings settings = WithKiosk(string.Empty);

        Assert.False(settings.ResolveTerminalIdentity());
        Assert.Equal(0, settings.TerminalNo);
        Assert.Equal(0L, settings.TerminalUid);
    }

    [Fact]
    public void AKioskNumberTooLargeForTheVendorFieldDoesNotWrapAround()
    {
        // termNo is a ushort in the vendor call. A number above 65535 silently
        // truncated would identify the kiosk as some other terminal entirely.
        KioskHardwareSettings settings = WithKiosk("70000");

        settings.ResolveTerminalIdentity();

        Assert.Equal(0, settings.TerminalNo);
        Assert.Equal(70000L, settings.TerminalUid);
    }
}
