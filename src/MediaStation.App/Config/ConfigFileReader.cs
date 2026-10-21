using System.IO;
using MediaStation.Core.Config;

namespace MediaStation.App.Config;

/// <summary>
/// Czyta pliki configu z dysku i zamienia wyjątki I/O na <see cref="ConfigFileState"/>; Core dostaje tylko treść albo stan.
/// </summary>
public static class ConfigFileReader
{
    public static IReadOnlyDictionary<string, ConfigFileState> ReadAll(string configDir, IEnumerable<string> fileNames)
    {
        var result = new Dictionary<string, ConfigFileState>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in fileNames)
        {
            result[name] = Read(Path.Combine(configDir, name));
        }

        return result;
    }

    public static ConfigFileState Read(string path)
    {
        try
        {
            return new ConfigFileState.Present(File.ReadAllText(path));
        }
        catch (Exception ex) when (ConfigFileState.FromReadError(ex) is { } state)
        {
            return state;
        }
    }
}
