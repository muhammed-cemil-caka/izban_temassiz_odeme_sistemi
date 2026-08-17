using IzbanKiosk.Terminal.Update;

namespace IzbanKiosk.Tests;

/// <summary>
/// Covers when a kiosk installs a release and in which direction.
///
/// The version arithmetic here decides what an entire fleet does unattended at four
/// in the morning, and a mistake is invisible until every machine has already acted
/// on it. The comparison case in particular has a trap: a tag parses with no
/// revision component while the assembly reports one.
/// </summary>
public class UpdateDecisionPolicyTests
{
    private const int Scheduled = 4;

    private static UpdateAction Decide(
        string current, string? latest, int hour = Scheduled,
        string latestTag = "v9", string appliedTag = "", bool rollback = true,
        bool immediate = false)
        => UpdateDecisionPolicy.Decide(
            new Version(current),
            latest == null ? null : new Version(latest),
            latestTag, appliedTag, hour, Scheduled, rollback, immediate);

    [Fact]
    public void ATagAndTheBuildItCameFromAreTheSameVersion()
    {
        // "1.0.27" parses with Revision -1; the assembly reports 1.0.27.0. Compared
        // naively the published release looks older than the identical build, and
        // every kiosk would recall itself on every check.
        Assert.Equal(0, UpdateDecisionPolicy.Compare(new Version("1.0.27"), new Version("1.0.27.0")));
        Assert.Equal(UpdateAction.None, Decide(current: "1.0.27.0", latest: "1.0.27"));
    }

    [Fact]
    public void ANewerReleaseInstallsOnlyInTheNightlyWindow()
    {
        Assert.Equal(UpdateAction.Upgrade, Decide(current: "1.0.26.0", latest: "1.0.27", hour: 4));
        Assert.Equal(UpdateAction.None, Decide(current: "1.0.26.0", latest: "1.0.27", hour: 13));
    }

    [Fact]
    public void APackageOnAUsbStickDoesNotWaitForTheNight()
    {
        // Somebody walked to the station and plugged it in; that is the instruction.
        // Waiting until 04:00 would send the engineer away without ever seeing whether
        // the update took, which is the only reason the visit happened.
        Assert.Equal(UpdateAction.Upgrade,
            Decide(current: "1.0.26.0", latest: "1.0.27", hour: 13, immediate: true));
    }

    [Fact]
    public void AnImmediateSourceStillRefusesAnythingButAnUpgrade()
    {
        // Skipping the nightly window must not also skip the guards. An equal version
        // changes nothing, and a stick holding an older package is not a recall.
        Assert.Equal(UpdateAction.None,
            Decide(current: "1.0.27.0", latest: "1.0.27", hour: 13, immediate: true));
        Assert.Equal(UpdateAction.None,
            Decide(current: "1.0.27.0", latest: "1.0.26", hour: 13, rollback: false, immediate: true));
        Assert.Equal(UpdateAction.None,
            Decide(current: "1.0.26.0", latest: "1.0.27", latestTag: "v1.0.27", appliedTag: "v1.0.27",
                   hour: 13, immediate: true));
    }

    [Fact]
    public void AWithdrawnReleaseRollsBackAtOnceWhateverTheHour()
    {
        // The operator deleted the bad release; waiting until 04:00 would leave the
        // fleet on a known-bad build all day.
        Assert.Equal(UpdateAction.Rollback, Decide(current: "1.0.27.0", latest: "1.0.26", hour: 13));
        Assert.Equal(UpdateAction.Rollback, Decide(current: "1.0.27.0", latest: "1.0.26", hour: 4));
    }

    [Fact]
    public void RollbackCanBeTurnedOff()
    {
        Assert.Equal(UpdateAction.None,
            Decide(current: "1.0.27.0", latest: "1.0.26", hour: 13, rollback: false));
    }

    [Fact]
    public void AReleaseAlreadyInstalledIsNotInstalledAgain()
    {
        // The version did not move after applying this tag, so the install is not
        // taking effect; repeating it every check would only produce traffic.
        Assert.Equal(UpdateAction.None,
            Decide(current: "1.0.26.0", latest: "1.0.27", latestTag: "v1.0.27", appliedTag: "v1.0.27"));
        Assert.Equal(UpdateAction.None,
            Decide(current: "1.0.27.0", latest: "1.0.26", latestTag: "v1.0.26", appliedTag: "v1.0.26", hour: 13));
    }

    [Fact]
    public void NothingPublishedMeansNothingToDo()
    {
        // Every release deleted, or the tag carried no version.
        Assert.Equal(UpdateAction.None, Decide(current: "1.0.27.0", latest: null, hour: 13));
    }

    [Fact]
    public void AMajorOrMinorStepIsHandledLikeAnyOther()
    {
        Assert.Equal(UpdateAction.Upgrade, Decide(current: "1.0.27.0", latest: "1.1.0", hour: 4));
        Assert.Equal(UpdateAction.Rollback, Decide(current: "2.0.0.0", latest: "1.9.9", hour: 13));
    }

    [Fact]
    public void TagComparisonIgnoresCase()
    {
        Assert.Equal(UpdateAction.None,
            Decide(current: "1.0.26.0", latest: "1.0.27", latestTag: "V1.0.27", appliedTag: "v1.0.27"));
    }
}
