using System;
using System.Collections.Generic;
using System.Linq;
using MediaStation.Core.Config;
using MediaStation.Core.Contracts;
using ActionMessage = MediaStation.Core.Contracts.Action;

namespace MediaStation.Core.Orchestrator;

public sealed partial class Orchestrator
{
    /// <summary>
    /// Bramka foreground (AD-8): gdy zbiór okien z <c>Hwnd</c> różni się od ostatnio opublikowanego (attach, adopcja,
    /// zamknięcie, awaria), wynik dostaje na końcu <see cref="SetManagedWindows"/> z nowym <c>epoch</c> własnego licznika (niezależnego od routingu). Komenda idzie
    /// po routingu i aktywacji: okno bez wpisu w zbiorze jest dla hooka obce, więc klawisze przechodzą do aplikacji,
    /// nie giną. Jedno miejsce publikacji obejmuje każdą ścieżkę zmieniającą <c>Hwnd</c>.
    /// </summary>
    private OrchestratorResult WithManagedWindows(OrchestratorResult result)
    {
        var current = new List<ManagedWindow>();
        foreach (var app in _apps)
        {
            if (app.Hwnd is { } hwnd)
            {
                current.Add(new ManagedWindow(hwnd, app.RootPid));
            }
        }

        if (current.SequenceEqual(_publishedWindows))
        {
            return result;
        }

        _publishedWindows = current;
        _windowsChanged = true;
        var commands = new List<Command>(result.Commands)
        {
            new SetManagedWindows(NextCmdId(), null, [.. current], ++_managedEpoch),
        };
        return result with { Commands = commands };
    }
}
