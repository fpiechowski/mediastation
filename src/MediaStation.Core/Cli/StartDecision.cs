using System;
using System.Collections.Generic;
using MediaStation.Core.Logging;

namespace MediaStation.Core.Cli;

/// <summary>
/// Czysta decyzja startu (AD-3): argumenty → tryb, mutex sprawdzany dopiero po poprawnym parse, a komunikaty
/// i kody wyjścia odpowiadają wierszom macierzy I/O. App tylko wykonuje decyzję: loguje i kończy proces.
/// </summary>
public abstract record StartDecision
{
    private StartDecision()
    {
    }

    /// <summary>Tryb główny: config, snapshot i okno shella.</summary>
    public sealed record Run : StartDecision;

    /// <summary>Zakończenie procesu z wpisem logu <paramref name="Msg"/> i kodem <paramref name="Code"/>.</summary>
    public sealed record Exit(
        ExitCode Code,
        LogLevel Level,
        string Msg,
        IReadOnlyDictionary<string, object?> Data) : StartDecision;

    /// <summary>
    /// Rozstrzyga start bez I/O: <paramref name="tryAcquireInstance"/> (mutex w App) jest wołane wyłącznie po
    /// poprawnym parse, więc złe argumenty nie blokują się na działającej instancji.
    /// </summary>
    public static StartDecision Decide(IReadOnlyList<string> args, Func<bool> tryAcquireInstance)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(tryAcquireInstance);

        var parsed = CliArgs.Parse(args);
        if (parsed is CliParseResult.Invalid invalid)
        {
            return new Exit(
                ExitCode.InvalidArguments,
                LogLevel.Error,
                "invalidArguments",
                new Dictionary<string, object?> { ["reason"] = invalid.Reason });
        }

        var mode = ((CliParseResult.Parsed)parsed).Mode;
        if (!tryAcquireInstance())
        {
            return new Exit(
                ExitCode.AlreadyRunning,
                LogLevel.Warn,
                "instanceRejected",
                new Dictionary<string, object?> { ["mode"] = mode.LogName });
        }

        if (mode is CliMode.Run)
        {
            return new Run();
        }

        var data = new Dictionary<string, object?> { ["mode"] = mode.LogName };
        if (mode is CliMode.Smoke smoke)
        {
            data["appId"] = smoke.AppId;
        }

        return new Exit(ExitCode.Unsupported, LogLevel.Warn, "modeUnsupported", data);
    }
}
