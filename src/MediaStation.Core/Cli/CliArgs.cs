using System;
using System.Collections.Generic;

namespace MediaStation.Core.Cli;

/// <summary>Tryb pracy wybrany z argumentów CLI; <see cref="LogName"/> trafia do wpisów logu.</summary>
public abstract record CliMode
{
    private CliMode()
    {
    }

    /// <summary>Nazwa trybu w logu: <c>run</c>, <c>smoke</c> albo <c>calibrateInput</c>.</summary>
    public abstract string LogName { get; }

    /// <summary>Zwykły start: config, snapshot i okno shella.</summary>
    public sealed record Run : CliMode
    {
        public override string LogName => "run";
    }

    /// <summary>Smoke test wskazanej aplikacji (AD-13); dziś rozpoznawany, ale nieobsługiwany.</summary>
    public sealed record Smoke(string AppId) : CliMode
    {
        public override string LogName => "smoke";
    }

    /// <summary>Kalibracja wejścia (AD-8); dziś rozpoznawana, ale nieobsługiwana.</summary>
    public sealed record CalibrateInput : CliMode
    {
        public override string LogName => "calibrateInput";
    }
}

/// <summary>Wynik <see cref="CliArgs.Parse"/>: rozpoznany tryb albo polski powód odrzucenia.</summary>
public abstract record CliParseResult
{
    private CliParseResult()
    {
    }

    public sealed record Parsed(CliMode Mode) : CliParseResult;

    public sealed record Invalid(string Reason) : CliParseResult;
}

/// <summary>
/// Czysty parser argumentów CLI (AD-3: bez I/O). Rozpoznaje dokładnie trzy kształty, z rozróżnianiem wielkości liter:
/// brak argumentów → <see cref="CliMode.Run"/>; <c>--calibrate-input</c> → <see cref="CliMode.CalibrateInput"/>;
/// <c>--smoke &lt;appId&gt;</c> → <see cref="CliMode.Smoke"/>. Każdy inny kształt to <see cref="CliParseResult.Invalid"/>
/// z polskim powodem; <c>appId</c> nie jest sprawdzany względem <c>apps.json</c>.
/// </summary>
public static class CliArgs
{
    public const string SmokeFlag = "--smoke";

    public const string CalibrateInputFlag = "--calibrate-input";

    private const string MissingAppIdReason = "brak appId po --smoke";
    private const string EmptyAppIdReason = "appId nie może być pusty";
    private const string AppIdStartsWithDashReason = "appId nie może zaczynać się od '-'";
    private const string DuplicateModeReason = "tryb podano więcej niż raz";

    public static CliParseResult Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Count == 0)
        {
            return new CliParseResult.Parsed(new CliMode.Run());
        }

        if (args[0] == CalibrateInputFlag)
        {
            return args.Count == 1
                ? new CliParseResult.Parsed(new CliMode.CalibrateInput())
                : new CliParseResult.Invalid(ExtraArgumentReason(args, 1));
        }

        if (args[0] == SmokeFlag)
        {
            if (args.Count == 1)
            {
                return new CliParseResult.Invalid(MissingAppIdReason);
            }

            var appId = args[1];
            if (appId is SmokeFlag or CalibrateInputFlag)
            {
                return new CliParseResult.Invalid(DuplicateModeReason);
            }

            if (string.IsNullOrEmpty(appId))
            {
                return new CliParseResult.Invalid(EmptyAppIdReason);
            }

            if (appId[0] == '-')
            {
                return new CliParseResult.Invalid(AppIdStartsWithDashReason);
            }

            return args.Count == 2
                ? new CliParseResult.Parsed(new CliMode.Smoke(appId))
                : new CliParseResult.Invalid(ExtraArgumentReason(args, 2));
        }

        return new CliParseResult.Invalid(UnknownArgumentReason(args[0]));
    }

    private static string ExtraArgumentReason(IReadOnlyList<string> args, int index) =>
        args[index] is SmokeFlag or CalibrateInputFlag
            ? DuplicateModeReason
            : $"nadmiarowy argument: {args[index]}";

    private static string UnknownArgumentReason(string arg) => $"nieznany argument: {arg}";
}
