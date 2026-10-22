using MediaStation.App.Platform.System;

namespace MediaStation.Core.Tests;

/// <summary>
/// Księgowość podłączeń shimu power (DW-2): podłączenie, które rzuci, zdejmuje to, co już podłączone,
/// w odwrotnej kolejności, żeby hook HWND nie został do końca procesu; zwolnienie jest idempotentne.
/// </summary>
public class PowerSourcesAttachmentTests
{
    [Fact]
    public void Attach_that_throws_releases_the_sources_attached_before_it()
    {
        var released = new List<string>();
        var attachments = new PowerSourcesAttachment();
        attachments.Attach(() => { }, () => released.Add("hook"));

        var thrown = Record.Exception(() => attachments.Attach(
            () => throw new InvalidOperationException("AddHook failed"),
            () => released.Add("displayState")));

        Assert.IsType<InvalidOperationException>(thrown);
        Assert.Equal(["hook"], released);
        Assert.Equal(0, attachments.Count);
    }

    [Fact]
    public void Dispose_releases_sources_in_reverse_order()
    {
        var released = new List<string>();
        var attachments = new PowerSourcesAttachment();
        attachments.Attach(() => { }, () => released.Add("hook"));
        attachments.Attach(() => { }, () => released.Add("displayState"));
        attachments.Attach(() => { }, () => released.Add("powerModeChanged"));

        attachments.Dispose();

        Assert.Equal(["powerModeChanged", "displayState", "hook"], released);
        Assert.Equal(0, attachments.Count);
    }

    [Fact]
    public void Dispose_twice_releases_each_source_once()
    {
        var released = new List<string>();
        var attachments = new PowerSourcesAttachment();
        attachments.Attach(() => { }, () => released.Add("hook"));

        attachments.Dispose();
        attachments.Dispose();

        Assert.Equal(["hook"], released);
    }

    [Fact]
    public void Attach_runs_the_source_before_it_is_released()
    {
        var order = new List<string>();
        var attachments = new PowerSourcesAttachment();

        attachments.Attach(() => order.Add("attach"), () => order.Add("release"));
        attachments.Dispose();

        Assert.Equal(["attach", "release"], order);
    }

    [Fact]
    public void Dispose_without_sources_releases_nothing()
    {
        var attachments = new PowerSourcesAttachment();

        attachments.Dispose();

        Assert.Equal(0, attachments.Count);
    }
}
