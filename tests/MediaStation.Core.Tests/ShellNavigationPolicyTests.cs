using MediaStation.App.Shell;

namespace MediaStation.Core.Tests;

public class ShellNavigationPolicyTests
{
    private const string Release = "https://shell.mediastation.example/index.html";
    private const string Dev = "http://localhost:5173/";

    [Theory]
    [InlineData(Release, "https://shell.mediastation.example/index.html")]
    [InlineData(Release, "https://shell.mediastation.example/other?x=1#h")]
    [InlineData(Release, "HTTPS://SHELL.MEDIASTATION.EXAMPLE/")]
    [InlineData(Dev, "http://localhost:5173/")]
    [InlineData(Dev, "http://localhost:5173/src/main.tsx")]
    public void Shell_origin_is_allowed(string origin, string uri) =>
        Assert.True(new ShellNavigationPolicy(new Uri(origin)).IsAllowed(uri));

    [Theory]
    [InlineData(Release, "https://evil.example/")]
    [InlineData(Release, "https://shell.mediastation.example.evil.com/")]
    [InlineData(Release, "https://shell.mediastation.example:8443/")]
    [InlineData(Release, "http://shell.mediastation.example/")]
    [InlineData(Release, "")]
    [InlineData(Release, null)]
    [InlineData(Release, "not a uri")]
    [InlineData(Release, "/index.html")]
    [InlineData(Dev, "http://localhost:5174/")]
    [InlineData(Dev, "https://localhost:5173/")]
    [InlineData(Dev, "http://localhost.evil.com:5173/")]
    [InlineData(Dev, "https://evil.example/")]
    [InlineData(Dev, "")]
    [InlineData(Dev, null)]
    [InlineData(Dev, "not a uri")]
    public void Foreign_or_unparsable_uri_is_blocked(string origin, string? uri) =>
        Assert.False(new ShellNavigationPolicy(new Uri(origin)).IsAllowed(uri));

    [Fact]
    public void Null_origin_throws() =>
        Assert.Throws<ArgumentNullException>(() => new ShellNavigationPolicy(null!));

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public void Only_non_cancelled_failures_are_logged(bool isSuccess, bool canceled, bool expected) =>
        Assert.Equal(expected, ShellNavigationPolicy.ShouldLogFailure(isSuccess, canceled));
}
