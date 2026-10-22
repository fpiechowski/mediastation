using MediaStation.App;

namespace MediaStation.Core.Tests;

/// <summary>
/// Kolejność zwalniania adapterów przy wyjściu (DW-5) bez composition rootu i bez WPF/Win32: każdy adapter jest
/// zwalniany, awaria jednego nie zatrzymuje reszty, a na zewnątrz leci pierwszy wyjątek.
/// </summary>
public class TeardownSequenceTests
{
    [Fact]
    public void Dispose_releases_every_adapter_in_teardown_order()
    {
        var order = new List<string>();
        var sequence = Sequence(order);

        sequence.Dispose();

        Assert.Equal(
            ["inputHub", "actionPump", "keyInjection", "notifications", "reporter", "timers", "loop"],
            order);
    }

    [Fact]
    public void Dispose_keeps_releasing_after_a_failure_and_rethrows_the_same_exception()
    {
        var order = new List<string>();
        var failure = new InvalidOperationException("actionPump padł");
        var sequence = Sequence(order, failed: ("actionPump", failure));

        var thrown = Assert.Throws<InvalidOperationException>(() => sequence.Dispose());

        // Ta sama instancja wyjątku, a nie opakowanie; reszta adapterów i tak zwolniona.
        Assert.Same(failure, thrown);
        Assert.Equal(
            ["inputHub", "actionPump", "keyInjection", "notifications", "reporter", "timers", "loop"],
            order);
    }

    [Fact]
    public void Dispose_keeps_releasing_and_rethrows_the_first_exception()
    {
        var order = new List<string>();
        var first = new InvalidOperationException("inputHub padł");
        var second = new InvalidOperationException("timers padł");
        var sequence = Sequence(order, failed: ("inputHub", first), alsoFailed: ("timers", second));

        var thrown = Assert.Throws<InvalidOperationException>(() => sequence.Dispose());

        // Dawna siatka `try/finally` przepuściłaby ostatni wyjątek; macierz Intent wymaga pierwszego.
        Assert.Same(first, thrown);
        Assert.Equal(
            ["inputHub", "actionPump", "keyInjection", "notifications", "reporter", "timers", "loop"],
            order);
    }

    [Fact]
    public void Dispose_tolerates_null_optional_adapters()
    {
        var order = new List<string>();
        var sequence = new TeardownSequence(
            inputHub: null,
            actionPump: null,
            keyInjection: new Recorder("keyInjection", order),
            notifications: null,
            reporter: null,
            timers: new Recorder("timers", order),
            loop: new Recorder("loop", order));

        sequence.Dispose();

        Assert.Equal(["keyInjection", "timers", "loop"], order);
    }

    private static TeardownSequence Sequence(
        List<string> order,
        (string Name, Exception Failure)? failed = null,
        (string Name, Exception Failure)? alsoFailed = null) =>
        new(
            inputHub: new Recorder("inputHub", order, Failure("inputHub", failed, alsoFailed)),
            actionPump: new Recorder("actionPump", order, Failure("actionPump", failed, alsoFailed)),
            keyInjection: new Recorder("keyInjection", order, Failure("keyInjection", failed, alsoFailed)),
            notifications: new Recorder("notifications", order, Failure("notifications", failed, alsoFailed)),
            reporter: new Recorder("reporter", order, Failure("reporter", failed, alsoFailed)),
            timers: new Recorder("timers", order, Failure("timers", failed, alsoFailed)),
            loop: new Recorder("loop", order, Failure("loop", failed, alsoFailed)));

    private static Exception? Failure(
        string name,
        (string Name, Exception Failure)? failed,
        (string Name, Exception Failure)? alsoFailed) =>
        failed is { } one && one.Name == name ? one.Failure
        : alsoFailed is { } two && two.Name == name ? two.Failure
        : null;

    /// <summary>Adapter-zapis: nazwa ląduje w kolejności, a skonfigurowana awaria leci z <see cref="Dispose"/>.</summary>
    private sealed class Recorder(string name, List<string> order, Exception? failure = null) : IDisposable
    {
        public void Dispose()
        {
            order.Add(name);
            if (failure is not null)
            {
                throw failure;
            }
        }
    }
}
