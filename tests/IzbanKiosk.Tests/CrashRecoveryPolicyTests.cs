using IzbanKiosk.Terminal.Recovery;

namespace IzbanKiosk.Tests;

/// <summary>
/// Covers when a crashed kiosk restarts and when it stops trying.
///
/// This decides whether a station machine comes back on its own or sits showing an
/// empty screen. Getting it wrong in either direction is bad: too eager and a
/// reproducible fault becomes a flicker loop nobody can interrupt; too cautious and
/// a single hiccup takes the kiosk out of service for the day. Neither can be
/// staged on real hardware.
/// </summary>
public class CrashRecoveryPolicyTests
{
    private static readonly DateTime Now = new DateTime(2026, 8, 12, 9, 0, 0);

    [Fact]
    public void ASingleCrashJustRestarts()
    {
        var crashes = new List<DateTime> { Now };

        Assert.False(CrashRecoveryPolicy.ShouldStopRestarting(crashes, Now));
    }

    [Fact]
    public void TwoCrashesStillRestart()
    {
        var crashes = new List<DateTime> { Now.AddMinutes(-2), Now };

        Assert.False(CrashRecoveryPolicy.ShouldStopRestarting(crashes, Now));
    }

    [Fact]
    public void ThreeCrashesInTheWindowHandTheMachineBack()
    {
        // The fault clearly reproduces; restarting again only loops.
        var crashes = new List<DateTime> { Now.AddMinutes(-5), Now.AddMinutes(-2), Now };

        Assert.True(CrashRecoveryPolicy.ShouldStopRestarting(crashes, Now));
    }

    [Fact]
    public void OldCrashesDoNotCountAgainstAHealthyKiosk()
    {
        // Two crashes months ago must not make today's single crash fatal.
        var crashes = new List<DateTime>
        {
            Now.AddDays(-60), Now.AddDays(-30), Now
        };

        Assert.False(CrashRecoveryPolicy.ShouldStopRestarting(crashes, Now));
        Assert.Single(CrashRecoveryPolicy.Recent(crashes, Now));
    }

    [Fact]
    public void CrashesJustOutsideTheWindowAreDropped()
    {
        var crashes = new List<DateTime>
        {
            Now.AddMinutes(-11), Now.AddMinutes(-10).AddSeconds(-1), Now
        };

        Assert.Single(CrashRecoveryPolicy.Recent(crashes, Now));
    }

    [Fact]
    public void TimesInTheFutureAreIgnored()
    {
        // A clock that jumped, or a file copied from another machine.
        var crashes = new List<DateTime> { Now.AddHours(1), Now };

        Assert.Single(CrashRecoveryPolicy.Recent(crashes, Now));
    }

    [Fact]
    public void HistorySurvivesARoundTrip()
    {
        var crashes = new List<DateTime> { Now.AddMinutes(-3), Now };

        List<DateTime> restored = CrashRecoveryPolicy.Deserialise(
            CrashRecoveryPolicy.Serialise(crashes));

        Assert.Equal(crashes, restored);
    }

    [Fact]
    public void ACorruptHistoryFileCannotForceFallback()
    {
        // Unreadable lines are skipped, not counted as crashes: a damaged counter
        // must never take a working kiosk out of service.
        List<DateTime> restored = CrashRecoveryPolicy.Deserialise("bozuk\n\n???\n" +
            Now.ToString("o"));

        Assert.Single(restored);
        Assert.False(CrashRecoveryPolicy.ShouldStopRestarting(restored, Now));
    }

    [Fact]
    public void AnEmptyOrMissingHistoryIsTreatedAsNoCrashes()
    {
        Assert.Empty(CrashRecoveryPolicy.Deserialise(string.Empty));
        Assert.Empty(CrashRecoveryPolicy.Recent(null, Now));
    }
}
