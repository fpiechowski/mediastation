using MediaStation.App.Config;
using MediaStation.Core.Config;
using MediaStation.Core.Contracts;

namespace MediaStation.Core.Tests;

public class AppIconsTests
{
    private static readonly AppEntry[] Apps =
    [
        new WebApp("netflix", AppCategory.Video, "Netflix", "netflix.png", "netflix", BrowserKind.Edge, 9222),
        new NativeApp("plex", AppCategory.Game, "Plex", "plex.png", "C:/Plex/Plex.exe"),
    ];

    [Fact]
    public void Returns_only_entries_whose_icon_file_is_absent_in_config_order()
    {
        var present = new HashSet<string> { Path.Combine("icons", "netflix.png") };

        var missing = AppIcons.FindMissing(Apps, "icons", present.Contains);

        Assert.Equal(["plex"], missing.Select(a => a.Id));
    }

    [Fact]
    public void All_present_gives_empty_list()
    {
        Assert.Empty(AppIcons.FindMissing(Apps, "icons", _ => true));
    }

    [Fact]
    public void All_absent_lists_every_entry()
    {
        Assert.Equal(["netflix", "plex"], AppIcons.FindMissing(Apps, "icons", _ => false).Select(a => a.Id));
    }
}
