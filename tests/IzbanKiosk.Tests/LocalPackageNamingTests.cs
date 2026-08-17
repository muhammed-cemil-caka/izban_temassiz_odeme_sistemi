using IzbanKiosk.Terminal.Update;

namespace IzbanKiosk.Tests;

/// <summary>
/// Which files a closed-network kiosk will pick up off a USB stick. The dangerous
/// case cannot be caught by testing on a kiosk: the setup archive differs from the
/// release archive by one word in the file name and by 120 MB of .NET installer.
/// </summary>
public class LocalPackageNamingTests
{
    [Theory]
    [InlineData("IZBAN-Kiosk-v1.0.31.zip", "1.0.31")]
    [InlineData("IZBAN-Kiosk-v1.0.4.zip", "1.0.4")]
    [InlineData("izban-kiosk-v2.1.0.zip", "2.1.0")]
    public void Reads_the_version_out_of_a_release_package(string fileName, string expected)
    {
        Assert.Equal(Version.Parse(expected), LocalPackageNaming.ReadVersion(fileName));
    }

    [Fact]
    public void The_usb_setup_archive_is_never_an_update()
    {
        // It carries ndp48-x86-x64-allos-enu.exe at its root. Copied over an
        // installation it would drop a 120 MB installer into the kiosk's own folder,
        // and the only symptom would be a disk quietly filling on machines nobody
        // watches.
        Assert.Null(LocalPackageNaming.ReadVersion("IZBAN-Kiosk-v1.0.31-KURULUM.zip"));
        Assert.Null(LocalPackageNaming.ReadVersion("izban-kiosk-v1.0.31-kurulum.zip"));
    }

    [Theory]
    [InlineData("holiday-photos.zip")]
    [InlineData("AUSKiosk-5.2.0.4.zip")]
    [InlineData("IZBAN-Kiosk-v1.0.31.txt")]
    [InlineData("IZBAN-Kiosk-final.zip")]
    [InlineData("")]
    public void Anything_that_is_not_a_kiosk_package_is_refused(string fileName)
    {
        // A kiosk must not install the first .zip somebody happens to leave on a stick.
        Assert.Null(LocalPackageNaming.ReadVersion(fileName));
    }

    [Fact]
    public void Tags_match_the_form_the_applied_tag_guard_expects()
    {
        // The guard that stops a failed install repeating forever compares tags as
        // strings, so a local package has to produce the same shape GitHub does.
        Assert.Equal("v1.0.31", LocalPackageNaming.TagFor(new Version(1, 0, 31)));
        Assert.Equal("v1.2.0", LocalPackageNaming.TagFor(new Version(1, 2)));
    }
}
