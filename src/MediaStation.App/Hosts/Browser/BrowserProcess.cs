using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MediaStation.App.Platform.Window;
using MediaStation.Core.Config;

namespace MediaStation.App.Hosts.Browser;

/// <summary>Produkcyjne adaptery seamów <see cref="BrowserHost"/>: proces, okno w drzewie procesów, pliki i ścieżka przeglądarki.</summary>
internal static class BrowserPlatform
{
    /// <summary>Plik wykonywalny Edge/Chrome z kandydatów <see cref="BrowserLaunch.ExecutableCandidates"/> albo <c>null</c>.</summary>
    public static string? ResolveBrowser(BrowserKind browser) =>
        BrowserLaunch.ResolveExecutable(
            BrowserLaunch.ExecutableCandidates(
                browser,
                Environment.GetEnvironmentVariable("ProgramFiles"),
                Environment.GetEnvironmentVariable("ProgramFiles(x86)"),
                Environment.GetEnvironmentVariable("LocalAppData")),
            File.Exists);

    /// <summary>Treść pliku albo <c>null</c>, gdy pliku nie ma lub nie da się go odczytać.</summary>
    public static string? ReadFile(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Żywy proces o danym PID dla adopcji (story 6.5) albo <c>null</c>, gdy go nie ma, już się zakończył albo nie da się
    /// go obsłużyć (brak dostępu).
    /// </summary>
    public static IBrowserProcess? OpenProcess(int pid)
    {
        Process? process = null;
        try
        {
            process = Process.GetProcessById(pid);
            if (process.HasExited)
            {
                process.Dispose();
                return null;
            }

            return new ProcessBrowserProcess(process);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
        {
            process?.Dispose();
            return null;
        }
    }

    /// <summary>Główne okno przeglądarki (<c>Chrome_WidgetWin_1</c>, widoczne, bez właściciela) w drzewie procesu <paramref name="rootPid"/>.</summary>
    public static Func<int, WindowInfo?> WindowFinder(IWindowApi windows)
    {
        ArgumentNullException.ThrowIfNull(windows);
        return rootPid =>
        {
            var tree = WindowTreePicker.Descendants(rootPid, Win32ProcessSnapshot.ParentMap());
            return WindowTreePicker.Pick(windows.EnumerateTopLevel(), tree, WindowTreePicker.BrowserWindowClass);
        };
    }
}

/// <summary>Start przeglądarki przez <see cref="Process.Start(ProcessStartInfo)"/> z listą argumentów (bez powłoki).</summary>
internal sealed class ProcessBrowserLauncher : IBrowserProcessLauncher
{
    public IBrowserProcess? Start(string executable, IReadOnlyList<string> arguments)
    {
        var info = new ProcessStartInfo(executable) { UseShellExecute = false };

        // Odziedziczone `__COMPAT_LAYER` (np. DetectorsAppHealth) każe Edge uruchomić się ponownie bez warstwy
        // zgodności: uruchomiony korzeń kończy się od razu, a host zgłosiłby fałszywe wyjście (sonda UM760, story 6.4).
        info.Environment.Remove("__COMPAT_LAYER");
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        return Process.Start(info) is { } process ? new ProcessBrowserProcess(process) : null;
    }
}

/// <summary><see cref="IBrowserProcess"/> na <see cref="Process"/>; operacje na zakończonym procesie nie rzucają.</summary>
internal sealed class ProcessBrowserProcess(Process process) : IBrowserProcess
{
    public int Id { get; } = process.Id;

    public bool HasExited
    {
        get
        {
            try
            {
                return process.HasExited;
            }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
            {
                return true;
            }
        }
    }

    public int? ExitCode
    {
        get
        {
            try
            {
                return process.HasExited ? process.ExitCode : null;
            }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
            {
                return null;
            }
        }
    }

    public Task WaitForExitAsync(CancellationToken cancellationToken) => process.WaitForExitAsync(cancellationToken);

    public void CloseMainWindow()
    {
        try
        {
            process.CloseMainWindow();
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            // Proces już zakończony albo bez okna: zostaje zabicie drzewa.
        }
    }

    public void KillTree()
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            // Proces już zakończony: stop jest idempotentny.
        }
    }
}
