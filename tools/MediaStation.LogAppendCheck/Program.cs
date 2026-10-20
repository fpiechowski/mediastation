// Sprawdza atomowy append FileLogSink między procesami: dwa procesy piszą po 5000 linii do jednego pliku,
// potem każda linia musi parsować się jako JSON, a każda para (writer, seq) wystąpić dokładnie raz.
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using MediaStation.App.Logging;

const int WriterCount = 2;
const int LinesPerWriter = 5000;
const string LogFile = "append-check.jsonl";

if (args is ["writer", var writerDir, var writerIdText, var lineCountText])
{
    var writerId = int.Parse(writerIdText, CultureInfo.InvariantCulture);
    var lineCount = int.Parse(lineCountText, CultureInfo.InvariantCulture);
    var sink = new FileLogSink(writerDir);
    var pad = new string('x', 150);

    for (var seq = 0; seq < lineCount; seq++)
    {
        sink.Append(LogFile, $$"""{"writer":{{writerId}},"seq":{{seq}},"pad":"{{pad}}"}""");
    }

    return 0;
}

var dir = Path.Combine(Path.GetTempPath(), "mediastation-log-append-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(dir);
try
{
    var writers = new List<Process>();
    for (var writer = 0; writer < WriterCount; writer++)
    {
        writers.Add(StartWriter(dir, writer));
    }

    foreach (var writer in writers)
    {
        writer.WaitForExit();
        if (writer.ExitCode != 0)
        {
            Console.Error.WriteLine($"FAIL: writer zakończył się kodem {writer.ExitCode}.");
            return 1;
        }
    }

    var path = Path.Combine(dir, LogFile);
    if (!File.Exists(path))
    {
        Console.Error.WriteLine("FAIL: writer nie utworzył pliku logu.");
        return 1;
    }

    var lines = File.ReadAllLines(path);
    var seen = new HashSet<(int Writer, int Seq)>();
    var broken = 0;
    foreach (var line in lines)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            var writer = document.RootElement.GetProperty("writer").GetInt32();
            var seq = document.RootElement.GetProperty("seq").GetInt32();
            if (writer < 0 || writer >= WriterCount || seq < 0 || seq >= LinesPerWriter)
            {
                broken++;
            }
            else if (!seen.Add((writer, seq)))
            {
                broken++;
            }
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            broken++;
        }
    }

    var expected = WriterCount * LinesPerWriter;
    if (lines.Length != expected || broken > 0 || seen.Count != expected)
    {
        Console.Error.WriteLine(
            $"FAIL: linii {lines.Length}/{expected}, uszkodzonych lub zdublowanych {broken}, unikalnych par {seen.Count}/{expected}.");
        return 1;
    }

    Console.WriteLine($"OK: {lines.Length} linii, każda parsuje się jako JSON, każda para (writer, seq) dokładnie raz.");
    return 0;
}
finally
{
    try
    {
        Directory.Delete(dir, recursive: true);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
    }
}

static Process StartWriter(string dir, int writer)
{
    var processPath = Environment.ProcessPath
        ?? throw new InvalidOperationException("Brak Environment.ProcessPath.");
    var startInfo = new ProcessStartInfo
    {
        FileName = processPath,
        UseShellExecute = false,
    };

    if (string.Equals(Path.GetFileNameWithoutExtension(processPath), "dotnet", StringComparison.OrdinalIgnoreCase))
    {
        startInfo.ArgumentList.Add(Assembly.GetEntryAssembly()!.Location);
    }

    startInfo.ArgumentList.Add("writer");
    startInfo.ArgumentList.Add(dir);
    startInfo.ArgumentList.Add(writer.ToString(CultureInfo.InvariantCulture));
    startInfo.ArgumentList.Add(LinesPerWriter.ToString(CultureInfo.InvariantCulture));
    return Process.Start(startInfo) ?? throw new InvalidOperationException("Nie udało się uruchomić procesu writer.");
}
