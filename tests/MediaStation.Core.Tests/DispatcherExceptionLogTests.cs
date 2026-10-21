using MediaStation.Core.Logging;

namespace MediaStation.Core.Tests;

public class DispatcherExceptionLogTests
{
    [Fact]
    public void Mark_then_consume_once_allows_exactly_one_log_entry()
    {
        var log = new DispatcherExceptionLog();
        var exception = new InvalidOperationException("boom");

        log.Mark(exception);

        Assert.True(log.TryConsume(exception));
        Assert.False(log.TryConsume(exception));
    }

    [Fact]
    public void Distinct_exceptions_are_independent()
    {
        var log = new DispatcherExceptionLog();
        var first = new InvalidOperationException("first");
        var second = new InvalidOperationException("second");

        log.Mark(first);
        log.Mark(second);

        Assert.True(log.TryConsume(first));
        Assert.True(log.TryConsume(second));
        Assert.False(log.TryConsume(first));
    }

    [Fact]
    public void Unmarked_exception_is_never_consumed()
    {
        var log = new DispatcherExceptionLog();

        Assert.False(log.TryConsume(new InvalidOperationException("unmarked")));
    }

    [Fact]
    public void Concurrent_duplicate_marks_of_the_same_exception_allow_one_consume()
    {
        var log = new DispatcherExceptionLog();
        var exception = new InvalidOperationException("race");

        Parallel.For(0, 64, _ => log.Mark(exception));

        Assert.True(log.TryConsume(exception));
        Assert.False(log.TryConsume(exception));
    }
}
