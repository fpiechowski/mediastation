using System;
using System.Collections.Generic;

namespace MediaStation.Core.Logging;

/// <summary>Poziom wpisu; w linii logu jako string camelCase (<c>info</c>, <c>warn</c>…).</summary>
public enum LogLevel
{
    Debug,
    Info,
    Warn,
    Error,
}

/// <summary>
/// Port logu wspólny dla wszystkich komponentów (AD-13). <paramref name="src"/> w <see cref="Write"/> to krótka
/// nazwa źródła lowercase (<c>app</c>, <c>shell</c>, później np. <c>page</c>). Implementacja nigdy nie rzuca z powodu zapisu.
/// </summary>
public interface ILog
{
    void Write(LogLevel level, string src, string msg, IReadOnlyDictionary<string, object?>? data = null, Exception? error = null);
}

public static class LogExtensions
{
    public static void Info(this ILog log, string src, string msg, IReadOnlyDictionary<string, object?>? data = null) =>
        log.Write(LogLevel.Info, src, msg, data);

    public static void Warn(this ILog log, string src, string msg, IReadOnlyDictionary<string, object?>? data = null, Exception? error = null) =>
        log.Write(LogLevel.Warn, src, msg, data, error);

    public static void Error(this ILog log, string src, string msg, Exception? error = null, IReadOnlyDictionary<string, object?>? data = null) =>
        log.Write(LogLevel.Error, src, msg, data, error);
}
