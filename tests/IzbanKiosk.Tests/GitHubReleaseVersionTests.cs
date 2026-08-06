using IzbanKiosk.Terminal.Update;

namespace IzbanKiosk.Tests;

public sealed class GitHubReleaseVersionTests
{
    [Theory]
    [InlineData("v1.4.0", "1.4.0")]
    [InlineData("1.4.0", "1.4.0")]
    [InlineData("R25", "25.0")]
    [InlineData("v2.0", "2.0")]
    [InlineData("release-3.1.2", "3.1.2")]
    public void ReleaseTags_AreReadAsVersions(string tag, string expected)
    {
        Assert.Equal(Version.Parse(expected), GitHubReleaseClient.ParseVersion(tag));
    }

    [Theory]
    [InlineData("")]
    [InlineData("latest")]
    [InlineData("stable")]
    public void TagsWithoutAVersion_AreRejected(string tag)
    {
        // The updater refuses to install these rather than guess an ordering: a wrong
        // guess could push a kiosk backwards onto an older build.
        Assert.Null(GitHubReleaseClient.ParseVersion(tag));
    }

    [Fact]
    public void NewerTagSortsAboveOlderTag()
    {
        Version? older = GitHubReleaseClient.ParseVersion("v1.9.0");
        Version? newer = GitHubReleaseClient.ParseVersion("v1.10.0");

        Assert.NotNull(older);
        Assert.NotNull(newer);
        Assert.True(newer > older, "1.10.0 must rank above 1.9.0.");
    }
}
