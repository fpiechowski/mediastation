namespace MediaStation.Core.Cli;

/// <summary>
/// Kody wyjścia procesu <c>MediaStation.exe</c>: jeden słownik dla autora, smoke i przyszłego <c>--supervise</c>.
/// </summary>
/// <remarks>
/// Epiki dopisują kody swoich trybów (smoke dostanie <c>CdpUnavailable</c>, <c>PortInUse</c> i <c>ProfileInUse</c>).
/// <c>1</c> zostaje na ogólną awarię, więc odrzucenie da się odróżnić od crasha; .NET przy nieobsłużonym wyjątku
/// zwraca <c>0xE0434352</c>, a nie 1.
/// </remarks>
public enum ExitCode
{
    Ok = 0,

    /// <summary>Argumenty CLI niezgodne z <see cref="CliArgs"/>; proces kończy się przed configiem i mutexem.</summary>
    InvalidArguments = 2,

    /// <summary>Inna instancja MediaStation trzyma mutex; nowy proces nie otwiera okna.</summary>
    AlreadyRunning = 3,

    /// <summary>Tryb rozpoznany, ale niezaimplementowany (dziś tylko <c>--smoke</c>).</summary>
    Unsupported = 4,

    /// <summary>WebView2 albo pliki shella nie wystartowały; proces kończy się po komunikacie błędu.</summary>
    ShellStartFailed = 5,

    /// <summary>
    /// Config nie przeszedł walidacji w trybie bez ekranu błędu (<c>--calibrate-input</c>); proces kończy się
    /// po wpisach <c>configError</c>, bez logowania surowych kodów.
    /// </summary>
    ConfigInvalid = 6,

    /// <summary>
    /// InputHub trybu kalibracji nie wystartował albo nie domknął trybu w ograniczonym czasie; całą pracą tego
    /// trybu jest wejście, więc brak huba jest porażką, a nie degradacją jak w trybie głównym.
    /// </summary>
    InputHubFailed = 7,
}
