using System.Collections.Generic;
using System.Management;

namespace MediaStation.App.Platform.System;

/// <summary>Linie poleceń procesów przeglądarek przez WMI (<c>Win32_Process</c>), dla adopcji okna harnessu po restarcie.</summary>
internal static class ProcessCommandLines
{
    /// <summary>Pary <c>(pid, commandLine)</c> procesów <c>msedge.exe</c> i <c>chrome.exe</c>; wolne, wołać poza pętlą Orchestratora.</summary>
    public static IReadOnlyList<KeyValuePair<int, string>> Browsers()
    {
        var result = new List<KeyValuePair<int, string>>();
        using var searcher = new ManagementObjectSearcher(
            "SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name = 'msedge.exe' OR Name = 'chrome.exe'");
        using var rows = searcher.Get();
        foreach (var row in rows)
        {
            using (row)
            {
                if (row["CommandLine"] is string line && row["ProcessId"] is uint pid)
                {
                    result.Add(new KeyValuePair<int, string>((int)pid, line));
                }
            }
        }

        return result;
    }
}
