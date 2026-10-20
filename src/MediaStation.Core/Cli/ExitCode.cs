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

    /// <summary>Tryb rozpoznany, ale niezaimplementowany (dziś <c>--smoke</c> i <c>--calibrate-input</c>).</summary>
    Unsupported = 4,
}
