using System.IO;
using System.Security;
using System.Text;
using MediaStation.Core.Logging;

namespace MediaStation.App.Logging;

/// <summary>
/// Dopisuje linie logu do <c>&lt;logsDir&gt;\&lt;fileName&gt;</c>. Plik otwierany per zapis (bez trzymanego uchwytu), z
/// <see cref="FileShare.ReadWrite"/>, więc log da się czytać na żywo. Błąd zapisu pomija wpis i nigdy nie przerywa aplikacji.
/// </summary>
public sealed class FileLogSink : ILogLineSink
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly string _logsDir;

    public FileLogSink(string logsDir)
    {
        _logsDir = logsDir;
    }

    public void Append(string fileName, string line)
    {
        try
        {
            Directory.CreateDirectory(_logsDir);
            var bytes = Utf8NoBom.GetBytes(line + "\n");
            using var stream = new FileStream(
                Path.Combine(_logsDir, fileName), FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            stream.Write(bytes, 0, bytes.Length);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            // Brak uprawnień, blokada pliku, pełny dysk: wpis pominięty.
        }
    }
}
