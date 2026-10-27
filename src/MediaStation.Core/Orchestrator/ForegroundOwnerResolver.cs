using System;
using System.Collections.Generic;

namespace MediaStation.Core.Orchestrator;

/// <summary>Do kogo należy okno pierwszego planu (AD-8, AD-15): shell, aplikacja zarządzana albo obce okno.</summary>
public enum ForegroundOwner
{
    Shell,
    App,
    Foreign,
}

/// <summary>Okno aplikacji znane Orchestratorowi: <c>hwnd</c> z <c>AppWindowAttached</c> i <c>rootPid</c> jej drzewa procesów.</summary>
public readonly record struct ForegroundApp(string AppId, long Hwnd, int RootPid);

/// <summary>Wynik rozpoznania właściciela okna: <see cref="AppId"/> jest wypełnione tylko dla <see cref="ForegroundOwner.App"/>.</summary>
public sealed record ForegroundObservation(long Hwnd, ForegroundOwner Owner, string? AppId);

/// <summary>Platformowe fakty potrzebne do rozpoznania okna: uchwyt shella (znany tylko App) i PID procesu okna.</summary>
public interface IForegroundLookup
{
    /// <summary>Uchwyt okna shella; <c>0</c>, gdy jeszcze nieznany.</summary>
    long ShellHwnd { get; }

    /// <summary>PID procesu okna; <c>0</c>, gdy okna już nie ma.</summary>
    int ProcessIdOf(long hwnd);
}

/// <summary>
/// Czysta funkcja rozpoznania właściciela okna pierwszego planu (AD-8): najpierw uchwyt (shell, okno aplikacji),
/// potem PID korzenia aplikacji, bo okna dialogowe i potomne aplikacji mają inne uchwyty tego samego procesu.
/// Uchwyt <c>0</c> (brak okna, secure desktop) jest obcy.
/// </summary>
public static class ForegroundOwnerResolver
{
    public static ForegroundObservation Resolve(long hwnd, int pid, long shellHwnd, IReadOnlyList<ForegroundApp> apps)
    {
        ArgumentNullException.ThrowIfNull(apps);

        if (hwnd == 0)
        {
            return new ForegroundObservation(hwnd, ForegroundOwner.Foreign, null);
        }

        if (hwnd == shellHwnd)
        {
            return new ForegroundObservation(hwnd, ForegroundOwner.Shell, null);
        }

        foreach (var app in apps)
        {
            if (app.Hwnd == hwnd)
            {
                return new ForegroundObservation(hwnd, ForegroundOwner.App, app.AppId);
            }
        }

        if (pid != 0)
        {
            foreach (var app in apps)
            {
                if (app.RootPid == pid)
                {
                    return new ForegroundObservation(hwnd, ForegroundOwner.App, app.AppId);
                }
            }
        }

        return new ForegroundObservation(hwnd, ForegroundOwner.Foreign, null);
    }
}
