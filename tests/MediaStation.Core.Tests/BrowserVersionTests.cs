using MediaStation.App.Hosts.Browser;

namespace MediaStation.Core.Tests;

/// <summary>
/// Story 6.4 (AD-13): parsowanie wersji z <c>FileVersion</c> i <c>Last Version</c>, odczyt z <c>product</c>
/// <c>Browser.getVersion</c>, porównanie po <see cref="Version"/> (nie jako tekst) i zapis do logu.
/// </summary>
public class BrowserVersionTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  \r\n")]
    [InlineData("garbage")]
    [InlineData("1")]
    [InlineData("156.0.4314.99999999999")]
    [InlineData("156.-1.4314.50")]
    [InlineData("156.0.4314.50.1")]
    public void Unparsable_text_is_unknown(string? text)
    {
        Assert.Null(BrowserVersion.Parse(text));
        Assert.Equal("unknown", BrowserVersion.Format(BrowserVersion.Parse(text)));
    }

    [Theory]
    [InlineData("156.0.4314.50", "156.0.4314.50")]
    [InlineData("  156.0.4314.50\r\n", "156.0.4314.50")]
    [InlineData("156.0.4314.050", "156.0.4314.50")]
    [InlineData("156.0.4314", "156.0.4314.0")]
    [InlineData("156.0", "156.0.0.0")]
    public void Text_is_trimmed_and_normalised_to_four_parts(string text, string expected)
    {
        Assert.Equal(expected, BrowserVersion.Format(BrowserVersion.Parse(text)));
    }

    [Theory]
    [InlineData("156.0.4314.050", "156.0.4314.50")]
    [InlineData("156.0.4314", "156.0.4314.0")]
    [InlineData("156.0.4314.0", "156.0.4314")]
    public void Equal_versions_in_different_spelling_are_not_a_change(string a, string b)
    {
        Assert.False(BrowserVersion.Changed(BrowserVersion.Parse(a), BrowserVersion.Parse(b)));
    }

    [Fact]
    public void Change_needs_two_known_different_versions()
    {
        var older = BrowserVersion.Parse("155.0.4283.45");
        var newer = BrowserVersion.Parse("156.0.4314.50");

        Assert.True(BrowserVersion.Changed(older, newer));
        Assert.True(BrowserVersion.Changed(newer, older));
        Assert.False(BrowserVersion.Changed(newer, newer));
        Assert.False(BrowserVersion.Changed(null, newer));
        Assert.False(BrowserVersion.Changed(older, null));
        Assert.False(BrowserVersion.Changed(null, null));
    }

    [Fact]
    public void Numeric_comparison_is_not_textual()
    {
        // Tekstowo "156.0.4314.9" > "156.0.4314.10"; liczbowo to różne wersje, a "…09" i "…9" to ta sama.
        Assert.True(BrowserVersion.Changed(BrowserVersion.Parse("156.0.4314.9"), BrowserVersion.Parse("156.0.4314.10")));
        Assert.False(BrowserVersion.Changed(BrowserVersion.Parse("156.0.4314.09"), BrowserVersion.Parse("156.0.4314.9")));
    }

    [Theory]
    [InlineData("Edg/156.0.4314.50", "156.0.4314.50")]
    [InlineData("Chrome/141.0.7390.55", "141.0.7390.55")]
    [InlineData("HeadlessChrome/141.0.7390.55", "141.0.7390.55")]
    [InlineData("Mozilla/Edg/156.0.4314", "156.0.4314.0")]
    public void Product_version_is_the_text_after_the_last_slash(string product, string expected)
    {
        Assert.Equal(expected, BrowserVersion.Format(BrowserVersion.FromProduct(product)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Edge")]
    [InlineData("156.0.4314.50")]
    [InlineData("Edg/")]
    [InlineData("Edg/garbage")]
    public void Product_without_a_parsable_version_after_a_slash_is_unknown(string? product)
    {
        Assert.Null(BrowserVersion.FromProduct(product));
    }
}
