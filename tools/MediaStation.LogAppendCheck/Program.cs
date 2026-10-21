using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using MediaStation.App.Logging;

const int WriterCount = 2;
const int LinesPerWriter = 5000;
const int PadLength = 150;
const int TimeoutSeconds = 30;
const string LogFile = "append-check.jsonl";
const string GoFile = "go";
const string DoneFile = "done";
const string HungEnvVar = "MEDIASTATION_LOGCHECK_HUNG";

if (args is ["writer", var writerDir, var writerIdText, var lineCountText, .. var writerRest])
{
    var writerId = int.Parse(writerIdText, CultureInfo.InvariantCulture);
    var lineCount = int.Parse(lineCountText, CultureInfo.InvariantCulture);
    var hang = writerRest.Contains("--hang", StringComparer.Ordinal)
        || string.Equals(Environment.GetEnvironmentVariable(HungEnvVar), "1", StringComparison.Ordinal);
    return RunWriter(writerDir, writerId, lineCount, hang);
}

if (args is ["reader", var readerDir, var donePath])
{
    return RunReader(readerDir, donePath);
}

var dir = Path.Combine(Path.GetTempPath(), "mediastation-log-append-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(dir);
var exitCode = RunCheck(dir);
if (exitCode == 0)
{
    try
    {
        Directory.Delete(dir, recursive: true);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
        Console.Error.WriteLine($"FAIL: nie udało się usunąć katalogu: {ex.Message}");
        Console.Error.WriteLine($"Katalog: {dir}");
        return 1;
    }

    return 0;
}

Console.Error.WriteLine($"Katalog: {dir}");
return exitCode;

static int RunCheck(string dir)
{
    var children = new List<Process>();
    var ok = false;
    var hang = string.Equals(Environment.GetEnvironmentVariable(HungEnvVar), "1", StringComparison.Ordinal);
    try
    {
        var writers = new List<Process>();
        for (var writerId = 1; writerId <= WriterCount; writerId++)
        {
            var writerArguments = new List<string>
            {
                "writer",
                dir,
                writerId.ToString(CultureInfo.InvariantCulture),
                LinesPerWriter.ToString(CultureInfo.InvariantCulture),
            };
            if (hang && writerId == 1)
            {
                writerArguments.Add("--hang");
            }

            writers.Add(StartSelf(writerArguments.ToArray()));
        }

        children.AddRange(writers);
        var reader = StartSelf("reader", dir, Path.Combine(dir, DoneFile));
        children.Add(reader);

        var clock = Stopwatch.StartNew();
        if (!WaitForReady(dir, clock))
        {
            throw new CheckFailure($"writerzy nie wystartowali w ciągu {TimeoutSeconds} s.");
        }

        File.WriteAllText(Path.Combine(dir, GoFile), string.Empty);
        if (!WaitForAll(writers, Remaining(clock)))
        {
            throw new CheckFailure($"writerzy nie zakończyli pracy w ciągu {TimeoutSeconds} s.");
        }

        foreach (var writer in writers)
        {
            if (writer.ExitCode != 0)
            {
                throw new CheckFailure($"writer zakończył się kodem {writer.ExitCode}.");
            }
        }

        File.WriteAllText(Path.Combine(dir, DoneFile), string.Empty);
        if (!WaitForAll([reader], Remaining(clock)))
        {
            throw new CheckFailure($"reader nie zakończył pracy w ciągu {TimeoutSeconds} s.");
        }

        if (reader.ExitCode != 0)
        {
            throw new CheckFailure($"reader zakończył się kodem {reader.ExitCode}.");
        }

        CheckSidecars(dir);
        CheckLog(dir);
        Console.WriteLine("OK: writerzy wystartowali wspólnie, reader czytał na żywo, a plik logu ma pełny kształt.");
        ok = true;
        return 0;
    }
    catch (Exception ex) when (ex is CheckFailure or IOException or UnauthorizedAccessException or FormatException or JsonException)
    {
        Console.Error.WriteLine($"FAIL: {ex.Message}");
        return 1;
    }
    finally
    {
        if (!ok)
        {
            KillAll(children);
        }
    }
}

static bool WaitForReady(string dir, Stopwatch clock)
{
    while (clock.Elapsed < TimeSpan.FromSeconds(TimeoutSeconds))
    {
        var allReady = true;
        for (var writerId = 1; writerId <= WriterCount; writerId++)
        {
            if (!File.Exists(Path.Combine(dir, $"ready-{writerId}")))
            {
                allReady = false;
                break;
            }
        }

        if (allReady)
        {
            return true;
        }

        Thread.Sleep(5);
    }

    return false;
}

static bool WaitForAll(IReadOnlyList<Process> processes, TimeSpan timeout)
{
    if (timeout <= TimeSpan.Zero)
    {
        return processes.All(static process => process.HasExited);
    }

    using var cancellation = new CancellationTokenSource(timeout);
    try
    {
        Task.WhenAll(processes.Select(process => process.WaitForExitAsync(cancellation.Token))).GetAwaiter().GetResult();
        return true;
    }
    catch (OperationCanceledException)
    {
        return false;
    }
}

static TimeSpan Remaining(Stopwatch clock)
{
    var remaining = TimeSpan.FromSeconds(TimeoutSeconds) - clock.Elapsed;
    return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
}

static void KillAll(IEnumerable<Process> processes)
{
    foreach (var process in processes)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
        }
        catch (Exception ex) when (ex is AggregateException or InvalidOperationException or Win32Exception or NotSupportedException)
        {
        }
    }
}

static void CheckSidecars(string dir)
{
    var starts = new DateTimeOffset[WriterCount];
    var ends = new DateTimeOffset[WriterCount];
    for (var writerId = 1; writerId <= WriterCount; writerId++)
    {
        starts[writerId - 1] = ReadTimestamp(Path.Combine(dir, $"start-{writerId}"));
        ends[writerId - 1] = ReadTimestamp(Path.Combine(dir, $"end-{writerId}"));
    }

    var window = starts.Max() - starts.Min();
    if (window > TimeSpan.FromSeconds(1))
    {
        throw new CheckFailure($"starty writerów różnią się o {window.TotalMilliseconds:F0} ms (limit 1000 ms).");
    }

    var overlapStart = starts.Max();
    var overlapEnd = ends.Min();
    if (overlapStart >= overlapEnd)
    {
        throw new CheckFailure("okresy zapisu writerów nie nachodzą na siebie.");
    }

    var readerFirst = ReadTimestamp(Path.Combine(dir, "reader-first"));
    var readerLast = ReadTimestamp(Path.Combine(dir, "reader-last"));
    var lastWriterEnd = ends.Max();
    if (readerFirst > lastWriterEnd)
    {
        throw new CheckFailure("reader przeczytał pierwszą linię po zakończeniu writerów.");
    }

    Console.WriteLine(
        $"OK: starty writerów w oknie {window.TotalMilliseconds:F0} ms, zapisy nachodzą na siebie o {(overlapEnd - overlapStart).TotalMilliseconds:F0} ms.");
    Console.WriteLine(
        $"OK: reader czytał na żywo (pierwsza linia {(lastWriterEnd - readerFirst).TotalMilliseconds:F0} ms przed końcem writerów, ostatnia {readerLast:O}).");
}

static DateTimeOffset ReadTimestamp(string path) =>
    DateTimeOffset.Parse(File.ReadAllText(path), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

static void CheckLog(string dir)
{
    var path = Path.Combine(dir, LogFile);
    if (!File.Exists(path))
    {
        throw new CheckFailure("writer nie utworzył pliku logu.");
    }

    var text = File.ReadAllText(path);
    if (!text.EndsWith('\n'))
    {
        throw new CheckFailure("plik logu nie kończy się znakiem nowej linii (linia częściowa).");
    }

    var lines = text.Split('\n');
    var lineCount = lines.Length - 1;
    var expected = WriterCount * LinesPerWriter;
    var seen = new HashSet<(int Writer, int Seq)>();
    var broken = 0;
    var duplicates = 0;
    for (var i = 0; i < lineCount; i++)
    {
        if (!TryParseLine(lines[i], out var writer, out var seq))
        {
            broken++;
            continue;
        }

        if (!seen.Add((writer, seq)))
        {
            duplicates++;
        }
    }

    if (lineCount != expected || broken != 0 || duplicates != 0 || seen.Count != expected)
    {
        throw new CheckFailure(
            $"linii {lineCount}/{expected}, uszkodzonych {broken}, zduplikowanych {duplicates}, unikalnych par {seen.Count}/{expected}.");
    }

    Console.WriteLine($"OK: {lineCount} linii, każda ma pola writer/seq/pad, a każda para (writer, seq) występuje dokładnie raz.");
}

static bool TryParseLine(string line, out int writer, out int seq)
{
    writer = 0;
    seq = 0;
    try
    {
        using var document = JsonDocument.Parse(line);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var fields = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in root.EnumerateObject())
        {
            if (property.Name is not ("writer" or "seq" or "pad") || !fields.Add(property.Name))
            {
                return false;
            }
        }

        if (fields.Count != 3
            || !root.TryGetProperty("writer", out var writerElement)
            || writerElement.ValueKind != JsonValueKind.Number
            || !writerElement.TryGetInt32(out writer)
            || !root.TryGetProperty("seq", out var seqElement)
            || seqElement.ValueKind != JsonValueKind.Number
            || !seqElement.TryGetInt32(out seq)
            || !root.TryGetProperty("pad", out var padElement)
            || padElement.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var pad = padElement.GetString();
        if (pad is null || pad.Length != PadLength || pad.Any(static c => c != 'x'))
        {
            return false;
        }

        return writer is >= 1 and <= WriterCount && seq is >= 1 and <= LinesPerWriter;
    }
    catch (JsonException)
    {
        return false;
    }
}

static int RunWriter(string dir, int writerId, int lineCount, bool hang)
{
    Directory.CreateDirectory(dir);
    File.WriteAllText(Path.Combine(dir, $"ready-{writerId}"), string.Empty);
    var clock = Stopwatch.StartNew();
    var go = Path.Combine(dir, GoFile);
    while (!File.Exists(go))
    {
        if (clock.Elapsed >= TimeSpan.FromSeconds(TimeoutSeconds))
        {
            Console.Error.WriteLine($"FAIL: writer {writerId} nie doczekał się sygnału startu.");
            return 1;
        }

        Thread.Sleep(5);
    }

    var sink = new FileLogSink(dir);
    var pad = new string('x', PadLength);
    File.WriteAllText(Path.Combine(dir, $"start-{writerId}"), Now());
    if (hang)
    {
        sink.Append(LogFile, Line(writerId, 1, pad));
        Thread.Sleep(Timeout.Infinite);
    }

    for (var seq = 1; seq <= lineCount; seq++)
    {
        sink.Append(LogFile, Line(writerId, seq, pad));
    }

    File.WriteAllText(Path.Combine(dir, $"end-{writerId}"), Now());
    return 0;
}

static string Line(int writer, int seq, string pad) =>
    $$"""{"writer":{{writer}},"seq":{{seq}},"pad":"{{pad}}"}""";

static string Now() => DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);

static int RunReader(string dir, string donePath)
{
    var path = Path.Combine(dir, LogFile);
    var pending = new List<byte>();
    var position = 0L;
    var lineCount = 0;
    var lastReadAt = default(DateTimeOffset);
    while (true)
    {
        var done = File.Exists(donePath);
        if (File.Exists(path))
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (position > stream.Length)
            {
                Console.Error.WriteLine("FAIL: reader zobaczył plik krótszy niż przeczytany fragment.");
                return 1;
            }

            stream.Seek(position, SeekOrigin.Begin);
            var buffer = new byte[16 * 1024];
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                position += read;
                for (var i = 0; i < read; i++)
                {
                    if (buffer[i] == (byte)'\n')
                    {
                        var line = Encoding.UTF8.GetString(pending.ToArray());
                        pending.Clear();
                        if (!TryParseLine(line, out _, out _))
                        {
                            Console.Error.WriteLine("FAIL: reader zobaczył uszkodzoną linię.");
                            return 1;
                        }

                        var readAt = DateTimeOffset.UtcNow;
                        if (lineCount == 0)
                        {
                            File.WriteAllText(
                                Path.Combine(dir, "reader-first"),
                                readAt.ToString("O", CultureInfo.InvariantCulture));
                        }

                        lastReadAt = readAt;
                        lineCount++;
                    }
                    else
                    {
                        pending.Add(buffer[i]);
                    }
                }
            }
        }

        if (done)
        {
            break;
        }

        Thread.Sleep(5);
    }

    if (lineCount > 0)
    {
        File.WriteAllText(
            Path.Combine(dir, "reader-last"),
            lastReadAt.ToString("O", CultureInfo.InvariantCulture));
    }

    if (pending.Count > 0)
    {
        Console.Error.WriteLine("FAIL: reader zobaczył linię częściową.");
        return 1;
    }

    var expected = WriterCount * LinesPerWriter;
    if (lineCount != expected)
    {
        Console.Error.WriteLine($"FAIL: reader zobaczył {lineCount}/{expected} linii.");
        return 1;
    }

    Console.WriteLine($"OK: reader przeczytał {lineCount} linii bez linii częściowej ani uszkodzonego JSON.");
    return 0;
}

static Process StartSelf(params string[] arguments)
{
    var processPath = Environment.ProcessPath
        ?? throw new CheckFailure("Brak Environment.ProcessPath.");
    var startInfo = new ProcessStartInfo
    {
        FileName = processPath,
        UseShellExecute = false,
    };
    startInfo.Environment.Remove(HungEnvVar);

    if (string.Equals(Path.GetFileNameWithoutExtension(processPath), "dotnet", StringComparison.OrdinalIgnoreCase))
    {
        startInfo.ArgumentList.Add(Assembly.GetEntryAssembly()!.Location);
    }

    foreach (var argument in arguments)
    {
        startInfo.ArgumentList.Add(argument);
    }

    return Process.Start(startInfo) ?? throw new CheckFailure("Nie udało się uruchomić procesu.");
}

internal sealed class CheckFailure(string message) : Exception(message)
{
}
