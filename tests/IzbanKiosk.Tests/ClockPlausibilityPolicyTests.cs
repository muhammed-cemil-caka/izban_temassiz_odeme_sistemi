using IzbanKiosk.Terminal.Timekeeping;

namespace IzbanKiosk.Tests;

/// <summary>
/// The clock rules, which nobody can stage on a real kiosk: reproducing them means
/// waiting for a CMOS battery to die.
/// </summary>
public class ClockPlausibilityPolicyTests
{
    private static readonly DateTime Installed = new(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Floor_takes_the_later_of_install_and_last_seen()
    {
        DateTime lastSeen = Installed.AddDays(30);

        Assert.Equal(lastSeen, ClockPlausibilityPolicy.Floor(Installed, lastSeen));
        Assert.Equal(Installed, ClockPlausibilityPolicy.Floor(Installed, Installed.AddDays(-5)));
    }

    [Fact]
    public void A_reference_makes_the_clock_trusted_whatever_the_floor_says()
    {
        // The floor is a fallback for having no reference; once one has answered and
        // any correction is applied, the machine's own history adds nothing.
        ClockVerdict verdict = ClockPlausibilityPolicy.Judge(
            new DateTime(2010, 1, 1, 0, 0, 0, DateTimeKind.Utc), Installed, verifiedExternally: true);

        Assert.Equal(ClockVerdict.Trusted, verdict);
    }

    [Fact]
    public void A_clock_reset_to_the_machines_manufacturing_year_is_caught_offline()
    {
        // The real fault: a dead battery drops the kiosk back to 2009 and every TLS
        // handshake fails on a certificate that is not yet valid.
        ClockVerdict verdict = ClockPlausibilityPolicy.Judge(
            new DateTime(2009, 6, 1, 0, 0, 0, DateTimeKind.Utc), Installed, verifiedExternally: false);

        Assert.Equal(ClockVerdict.Behind, verdict);
        Assert.True(ClockPlausibilityPolicy.BreaksSecureConnections(verdict));
    }

    [Fact]
    public void A_clock_a_few_minutes_behind_the_floor_is_not_called_wrong()
    {
        // A legitimate correction can move the clock backwards. Reporting a fault for
        // that would train the field team to ignore the one that matters.
        ClockVerdict verdict = ClockPlausibilityPolicy.Judge(
            Installed.AddMinutes(-4), Installed, verifiedExternally: false);

        Assert.Equal(ClockVerdict.Unverified, verdict);
        Assert.False(ClockPlausibilityPolicy.BreaksSecureConnections(verdict));
    }

    [Fact]
    public void An_unreachable_time_server_is_not_a_fault()
    {
        // The expected case on a closed station network: no reference, nothing wrong.
        ClockVerdict verdict = ClockPlausibilityPolicy.Judge(
            Installed.AddDays(10), Installed, verifiedExternally: false);

        Assert.Equal(ClockVerdict.Unverified, verdict);
        Assert.False(ClockPlausibilityPolicy.BreaksSecureConnections(verdict));
    }

    [Fact]
    public void A_clock_years_into_the_future_is_caught()
    {
        ClockVerdict verdict = ClockPlausibilityPolicy.Judge(
            Installed.AddYears(20), Installed, verifiedExternally: false);

        Assert.Equal(ClockVerdict.Ahead, verdict);
        Assert.True(ClockPlausibilityPolicy.BreaksSecureConnections(verdict));
    }

    [Fact]
    public void A_wrong_clock_may_never_become_the_floor()
    {
        // Otherwise one reading of 2099 poisons the floor for good: every later
        // reading, including the correct one after a repair, reads as Behind and the
        // kiosk reports a fault it no longer has.
        Assert.False(ClockPlausibilityPolicy.MayRaiseFloor(ClockVerdict.Ahead));
        Assert.False(ClockPlausibilityPolicy.MayRaiseFloor(ClockVerdict.Behind));
        Assert.True(ClockPlausibilityPolicy.MayRaiseFloor(ClockVerdict.Trusted));
        Assert.True(ClockPlausibilityPolicy.MayRaiseFloor(ClockVerdict.Unverified));
    }

    [Fact]
    public void Corrections_are_applied_in_both_directions_but_not_for_seconds()
    {
        DateTime reference = new(2026, 8, 14, 12, 0, 0, DateTimeKind.Utc);

        Assert.True(ClockPlausibilityPolicy.ShouldCorrect(reference.AddYears(-16), reference));
        Assert.True(ClockPlausibilityPolicy.ShouldCorrect(reference.AddHours(3), reference));
        Assert.False(ClockPlausibilityPolicy.ShouldCorrect(reference.AddSeconds(5), reference));
        Assert.False(ClockPlausibilityPolicy.ShouldCorrect(reference.AddSeconds(-5), reference));
    }

    [Fact]
    public void A_reference_that_cannot_be_a_real_date_is_rejected()
    {
        // NTP and an HTTP header are both unauthenticated, so a garbled or hostile
        // answer must not be allowed to set the clock this exists to protect.
        Assert.False(ClockPlausibilityPolicy.IsUsableReference(new DateTime(1900, 1, 1)));
        Assert.False(ClockPlausibilityPolicy.IsUsableReference(new DateTime(2145, 1, 1)));
        Assert.True(ClockPlausibilityPolicy.IsUsableReference(new DateTime(2026, 8, 14)));
    }
}
