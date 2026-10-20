using System.IO;
using System.Security;
using System.Security.AccessControl;
using System.Text;
using MediaStation.Core.Logging;

namespace MediaStation.App.Logging;

/// <summary>
/// Dopisuje linie logu do <c>&lt;logsDir&gt;\&lt;fileName&gt;</c>. Plik otwierany per zapis (bez trzymanego uchwytu), z
/// <see cref="FileShare.ReadWrite"/>, więc log da się czytać na żywo. Prawo tylko do <see cref="FileSystemRights.AppendData"/>
/// (bez <c>WriteData</c>) sprawia, że Windows dopisuje każdy <c>WriteFile</c> na bieżący koniec pliku atomowo, niezależnie
/// od pozycji strumienia, więc procesy nie gubią ani nie przeplatają linii. Błąd zapisu pomija wpis i nigdy nie przerywa aplikacji.
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
            using var stream = FileSystemAclExtensions.Create(
                new FileInfo(Path.Combine(_logsDir, fileName)),
                FileMode.Append,
                FileSystemRights.AppendData,
                FileShare.ReadWrite,
                bufferSize: 1,
                FileOptions.None,
                fileSecurity: null);
            stream.Write(bytes, 0, bytes.Length);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            // Brak uprawnień, blokada pliku, pełny dysk: wpis pominięty.
        }
    }
}
