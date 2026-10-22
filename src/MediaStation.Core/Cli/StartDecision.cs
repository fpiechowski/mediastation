using System;
using System.Collections.Generic;
using MediaStation.Core.Config;
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

    /// <summary>
    /// Kalibracja wejścia (AD-13): sam InputHub bez okna i bez UI, log wyłącznie surowych kodów urządzenia
    /// <c>remote</c>, zamknięcie po 60 s albo Esc z klawiatury.
    /// </summary>
    public sealed record CalibrateInput : StartDecision;

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

        if (mode is CliMode.CalibrateInput)
        {
            return new CalibrateInput();
        }

        var data = new Dictionary<string, object?> { ["mode"] = mode.LogName };
        if (mode is CliMode.Smoke smoke)
        {
            data["appId"] = smoke.AppId;
        }

        return new Exit(ExitCode.Unsupported, LogLevel.Warn, "modeUnsupported", data);
    }

    /// <summary>
    /// Kod zakończenia trybu <c>--calibrate-input</c> po odczycie configu (AD-11): tryb nie ma ekranu błędu, więc
    /// każde naruszenie walidacji kończy proces kodem <see cref="ExitCode.ConfigInvalid"/> bez logowania kodów,
    /// a poprawny config daje <see cref="ExitCode.Ok"/>. Decyzja w Core (AD-3), App tylko ją wykonuje.
    /// </summary>
    public static ExitCode CalibrationExit(ConfigResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result.Config is null ? ExitCode.ConfigInvalid : ExitCode.Ok;
    }
}
