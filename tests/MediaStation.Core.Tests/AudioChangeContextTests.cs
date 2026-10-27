using MediaStation.App.Platform.Audio;

namespace MediaStation.Core.Tests;

/// <summary>Własny kontekst zmian głośności i harmonogram skanu AudioRoutera.</summary>
public class AudioChangeContextTests
{
    [Fact]
    public void Own_context_does_not_request_a_sweep()
    {
        Assert.False(AudioChangeContext.ShouldSweep(AudioChangeContext.Own));
    }

    [Theory]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("11111111-2222-3333-4444-555555555555")]
    public void Foreign_context_requests_a_sweep(string context)
    {
        Assert.True(AudioChangeContext.ShouldSweep(Guid.Parse(context)));
    }

    [Fact]
    public void Missing_context_requests_a_sweep()
    {
        Assert.True(AudioChangeContext.ShouldSweep(null));
    }

    [Fact]
    public void Schedule_polls_while_a_rule_or_assigned_session_exists_and_idles_otherwise()
    {
        var schedule = new AudioSweepSchedule();

        Assert.Equal(AudioSweepSchedule.PollInterval, schedule.NextDelay(false, true));
        Assert.Null(schedule.NextDelay(false, false));
    }
}
