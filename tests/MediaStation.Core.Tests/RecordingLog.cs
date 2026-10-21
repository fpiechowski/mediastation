using MediaStation.Core.Logging;

namespace MediaStation.Core.Tests;

/// <summary>Jeden wpis przechwycony przez <see cref="RecordingLog"/>; danych nie kopiujemy, żeby testy widziały oryginał.</summary>
internal sealed record LogEntry(LogLevel Level, string Src, string Msg, IReadOnlyDictionary<string, object?>? Data);

/// <summary>
/// Log testowy: zbiera wpisy zamiast pisać pliki (AD-13 w testach warstwy App). Bezpieczny wątkowo,
/// bo pętla Orchestratora pisze z własnego wątku, a test czyta z wątku testu; odczyt zwraca kopię.
/// </summary>
internal sealed class RecordingLog : ILog
{
    private readonly object _gate = new();
    private readonly List<LogEntry> _entries = [];

    /// <summary>Kopia wpisów z chwili odczytu; kolejne zapisy nie zmieniają zwróconej kolekcji.</summary>
    public IReadOnlyList<LogEntry> Entries
    {
        get
        {
            lock (_gate)
            {
                return _entries.ToArray();
            }
        }
    }

    public string[] Messages() => Entries.Select(entry => entry.Msg).ToArray();

    public void Write(LogLevel level, string src, string msg, IReadOnlyDictionary<string, object?>? data = null, Exception? error = null)
    {
        lock (_gate)
        {
            _entries.Add(new LogEntry(level, src, msg, data));
        }
    }
}
