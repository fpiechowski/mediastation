using System;
using System.Globalization;

namespace MediaStation.Core.Logging;

/// <summary>Nazwa dziennego pliku logu po dacie UTC: <c>mediastation-yyyy-MM-dd.jsonl</c>.</summary>
public static class LogFileName
{
    public static string For(DateTimeOffset time) =>
        "mediastation-" + time.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".jsonl";
}
