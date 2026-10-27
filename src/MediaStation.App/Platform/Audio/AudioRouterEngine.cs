using System;
using System.Collections.Generic;
using System.Linq;
using MediaStation.Core.Contracts;
using MediaStation.Core.Logging;

namespace MediaStation.App.Platform.Audio;

/// <summary>Wynik jednego skanu sesji audio.</summary>
/// <param name="Unavailable">Usługa audio nie odpowiedziała (enumeracja rzuciła); reguła zostaje bez zmian.</param>
/// <param name="Unresolved">Jest sesja, której PID nie ma jeszcze w migawce procesów, więc nie wiadomo, do czyjego drzewa należy.</param>
/// <param name="SessionsPerRoot">Liczba sesji w drzewie każdego korzenia reguły.</param>
internal sealed record SweepResult(bool Unavailable, bool Unresolved, IReadOnlyDictionary<int, int> SessionsPerRoot);

/// <summary>
/// Silnik AudioRoutera (AD-15) bez wątków i bez COM: trzyma <see cref="AudioRule"/>, przypisuje sesje do drzew po
/// łańcuchu PID i trzyma ich wyciszenie zgodne z regułą w obie strony: przy każdym skanie czyta rzeczywisty stan
/// (<c>ISimpleAudioVolume</c> z zapasem w <c>IChannelAudioVolume</c>: wyciszona = wszystkie kanały 0) i poprawia
/// różnicę, także gdy ktoś zmienił sesję w mikserze. Koniec reguły odcisza każdą sesję przypisaną do drzewa.
/// Wołany wyłącznie z wątku AudioRoutera; nie rzuca.
/// </summary>
internal sealed class AudioRouterEngine : IDisposable
{
    /// <summary>Źródło wpisów logu AudioRoutera.</summary>
    public const string Src = "audio";

    private readonly Func<IAudioSessionBackend> _backendFactory;
    private readonly Func<IReadOnlyList<KeyValuePair<int, int>>> _processSnapshot;
    private readonly ILog _log;
    private readonly AudioRule _rule = new();
    private readonly Dictionary<string, Assigned> _assigned = new(StringComparer.Ordinal);
    private readonly HashSet<string> _reported = new(StringComparer.Ordinal);
    private IAudioSessionBackend? _backend;
    private bool _unavailableLogged;

    public AudioRouterEngine(
        Func<IAudioSessionBackend> backendFactory,
        Func<IReadOnlyList<KeyValuePair<int, int>>> processSnapshot,
        ILog log)
    {
        ArgumentNullException.ThrowIfNull(backendFactory);
        ArgumentNullException.ThrowIfNull(processSnapshot);
        ArgumentNullException.ThrowIfNull(log);

        _backendFactory = backendFactory;
        _processSnapshot = processSnapshot;
        _log = log;
    }

    /// <summary>Czy istnieje jakakolwiek reguła (także „odcisz”).</summary>
    public bool AnyRule => _rule.Roots.Count > 0;

    /// <summary>Czy silnik trzyma sesje przypisane do drzew (do odciszenia po końcu reguły, także gdy odciszenie nie powiodło się).</summary>
    public bool HasAssignedSessions => _assigned.Count > 0;

    /// <summary>Czy port ma planować okresowy skan: istnieje reguła albo przypisana sesja.</summary>
    public bool NeedsSweeping => AnyRule || HasAssignedSessions;

    /// <summary>Czy ostatni skan zostawił sesję bez przypisania (PID jeszcze nieznany w migawce procesów).</summary>
    public bool LastSweepUnresolved { get; private set; }

    /// <summary>
    /// Zapisuje regułę drzewa i od razu ją stosuje. Zwraca <c>null</c> dla sukcesu albo
    /// <see cref="ErrorCode.AudioUnavailable"/>; reguła zostaje zapisana także wtedy, więc kolejny skan ją zastosuje.
    /// </summary>
    public ErrorCode? Execute(AudioCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        int rootPid;
        bool muted;
        switch (command)
        {
            case MuteProcessTree mute:
                (rootPid, muted) = (mute.RootPid, true);
                break;
            case UnmuteProcessTree unmute:
                (rootPid, muted) = (unmute.RootPid, false);
                break;
            case ForgetProcessTree forget:
                return Forget(forget);
            default:
                return ErrorCode.Unexpected;
        }

        if (rootPid <= 0)
        {
            _log.Warn(Src, "audioRuleIgnored", new Dictionary<string, object?>
            {
                ["cmdId"] = command.CmdId,
                ["rootPid"] = rootPid,
            });
            return null;
        }

        _rule.Set(rootPid, muted);
        var result = Sweep();
        if (result.Unavailable)
        {
            return ErrorCode.AudioUnavailable;
        }

        _log.Info(Src, "audioRuleApplied", new Dictionary<string, object?>
        {
            ["rootPid"] = rootPid,
            ["muted"] = muted,
            ["sessions"] = result.SessionsPerRoot.GetValueOrDefault(rootPid),
        });
        return null;
    }

    /// <summary>
    /// Usuwa regułę korzenia po końcu procesu: kolejny skan odciszy sesje przypisane do drzewa, a nowy proces z tym
    /// samym PID nie dostanie wyciszenia. Brak reguły nie jest błędem.
    /// </summary>
    private ErrorCode? Forget(ForgetProcessTree command)
    {
        if (command.RootPid <= 0)
        {
            _log.Warn(Src, "audioRuleIgnored", new Dictionary<string, object?>
            {
                ["cmdId"] = command.CmdId,
                ["rootPid"] = command.RootPid,
            });
            return null;
        }

        _rule.Remove(command.RootPid);
        var result = Sweep();
        if (result.Unavailable)
        {
            return ErrorCode.AudioUnavailable;
        }

        _log.Info(Src, "audioRuleForgotten", new Dictionary<string, object?>
        {
            ["rootPid"] = command.RootPid,
        });
        return null;
    }

    /// <summary>
    /// Skan sesji wszystkich aktywnych urządzeń: sesje drzew z regułą wracają do stanu z reguły (także po zmianie
    /// w mikserze), a sesje drzew, których reguła się skończyła, są odciszane. Nie rzuca.
    /// </summary>
    public SweepResult Sweep()
    {
        var result = SweepCore();
        LastSweepUnresolved = result.Unresolved;
        return result;
    }

    private SweepResult SweepCore()
    {
        IReadOnlyList<IAudioSession> sessions;
        try
        {
            _backend ??= _backendFactory();
            sessions = _backend.Enumerate();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Backend trzyma uchwyty COM usługi, która mogła paść: następna próba tworzy go od nowa.
            DropBackend();
            if (!_unavailableLogged)
            {
                _unavailableLogged = true;
                _log.Warn(Src, "audioUnavailable", error: ex);
            }

            return new SweepResult(true, false, new Dictionary<int, int>());
        }

        _unavailableLogged = false;
        try
        {
            return Apply(sessions);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _log.Error(Src, "audioSweepFailed", ex);
            return new SweepResult(false, false, new Dictionary<int, int>());
        }
        finally
        {
            foreach (var session in sessions)
            {
                ReleaseHandle(session);
            }
        }
    }

    /// <summary>Koniec wszystkich reguł (wyjście z aplikacji nie zostawia przeglądarki bez dźwięku): odciszenie przypisanych sesji i zwolnienie backendu.</summary>
    public void Dispose()
    {
        if (_assigned.Count > 0)
        {
            foreach (var root in _rule.Roots.ToArray())
            {
                _rule.Remove(root);
            }

            Sweep();
        }

        DropBackend();
    }

    private SweepResult Apply(IReadOnlyList<IAudioSession> sessions)
    {
        var parents = ReadParents();

        // Korzeń martwego procesu nie obejmuje już niczego, a jego PID mógłby wrócić do obcego procesu. Sesje w rejestrze
        // zostają: pętla niżej odciszy je (reguły korzenia już nie ma), więc przeżywający potomek nie zostaje wyciszony na zawsze.
        _rule.PruneDead(parents);

        var present = new HashSet<string>(StringComparer.Ordinal);
        var perRoot = new Dictionary<int, int>();
        var unresolved = false;
        foreach (var session in sessions)
        {
            present.Add(session.Key);
            try
            {
                var governing = _rule.Governing(session.Pid, parents);
                if (governing is { } root)
                {
                    perRoot[root] = perRoot.GetValueOrDefault(root) + 1;
                }
                else if (session.Pid > 0 && !parents.ContainsKey(session.Pid) && AnyRule)
                {
                    unresolved = true;
                }

                _assigned.TryGetValue(session.Key, out var entry);
                if (governing is { } enforcedRoot && _rule.IsMuted(enforcedRoot) is { } desired)
                {
                    Enforce(session, enforcedRoot, desired, entry);
                }
                else if (entry is not null && _rule.IsMuted(entry.Root) is null)
                {
                    // Tylko koniec reguły korzenia; chwilowa luka w migawce procesów nie odcisza sesji pod regułą.
                    Unassign(session, entry);
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // Sesja mogła zniknąć między enumeracją a wywołaniem; jeden wpis na sesję, bez zalewu logu przy skanie co sekundę.
                ReportOnce(session, "audioSessionFailed", ex.GetType().Name, ex);
            }
        }

        foreach (var stale in _assigned.Keys.Where(key => !present.Contains(key)).ToArray())
        {
            _assigned.Remove(stale);
        }

        _reported.IntersectWith(present);
        return new SweepResult(false, unresolved, perRoot);
    }

    /// <summary>
    /// Doprowadza sesję do wartości reguły. Różnica względem rzeczywistego stanu u sesji, którą poprzedni skan zostawił
    /// zgodną z regułą, to zmiana z zewnątrz (jeden wpis <c>audioSessionOverridden</c> na sesję i wartość reguły);
    /// pierwsze przypisanie albo zmiana wartości reguły to zwykłe zastosowanie.
    /// </summary>
    private void Enforce(IAudioSession session, int root, bool desired, Assigned? entry)
    {
        var simple = session.TryGetMute();
        float[]? levels = null;
        bool actual;
        if (simple is { } mutedNow)
        {
            actual = mutedNow;
        }
        else
        {
            levels = session.TryGetChannelVolumes();
            if (levels is not { Length: > 0 })
            {
                ReportOnce(session, "audioSessionSkipped", "noVolumeInterface", error: null, root);
                return;
            }

            actual = levels.All(level => level == 0f);
        }

        var sameRule = entry is not null && entry.Value == desired;
        var overridden = sameRule && actual != desired;
        if (sameRule && !overridden)
        {
            entry!.Root = root;
            return;
        }

        var remembered = entry?.Levels;
        if (actual != desired)
        {
            if (simple is not null)
            {
                session.TrySetMute(desired);
            }
            else if (desired)
            {
                if (!session.TrySetChannelVolumes(new float[levels!.Length]))
                {
                    return;
                }

                // Poziomy zapamiętane od nowa także po podniesieniu kanałów z zewnątrz.
                remembered = levels;
                if (!overridden)
                {
                    _log.Info(Src, "audioChannelFallback", new Dictionary<string, object?>
                    {
                        ["rootPid"] = root,
                        ["pid"] = session.Pid,
                    });
                }
            }
            else
            {
                if (!session.TrySetChannelVolumes(RestoredLevels(remembered, levels!.Length)))
                {
                    return;
                }

                remembered = null;
            }
        }

        var reported = overridden && (entry?.OverrideReported ?? false);
        if (overridden && !reported)
        {
            _log.Info(Src, "audioSessionOverridden", new Dictionary<string, object?>
            {
                ["rootPid"] = root,
                ["pid"] = session.Pid,
                ["muted"] = desired,
            });
            reported = true;
        }
        else if (!overridden && desired && actual != desired)
        {
            _log.Info(Src, "audioSessionMuted", new Dictionary<string, object?>
            {
                ["rootPid"] = root,
                ["pid"] = session.Pid,
            });
        }

        _assigned[session.Key] = new Assigned(root, desired, desired ? remembered : null) { OverrideReported = reported };
    }

    /// <summary>
    /// Koniec reguły drzewa sesji: odciszenie (jeśli jest wyciszona, także przez kogoś innego) i wypisanie z rejestru.
    /// Przy porażce wyjątek zostawia sesję w rejestrze do następnego skanu.
    /// </summary>
    private void Unassign(IAudioSession session, Assigned entry)
    {
        var simple = session.TryGetMute();
        if (simple is { } muted)
        {
            if (muted)
            {
                session.TrySetMute(false);
            }
        }
        else if (session.TryGetChannelVolumes() is { Length: > 0 } levels && levels.All(level => level == 0f))
        {
            session.TrySetChannelVolumes(RestoredLevels(entry.Levels, levels.Length));
        }

        _assigned.Remove(session.Key);
    }

    /// <summary>Poziomy do przywrócenia: zapamiętane sprzed wyciszenia albo 1.0, gdy ich brak (np. po awarii poprzedniej instancji).</summary>
    private static float[] RestoredLevels(float[]? remembered, int count) =>
        remembered is { Length: > 0 } && remembered.Length == count ? remembered : Enumerable.Repeat(1f, count).ToArray();

    private IReadOnlyDictionary<int, int> ReadParents()
    {
        try
        {
            return ProcessTreeMatcher.ToParentMap(_processSnapshot());
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _log.Warn(Src, "audioProcessSnapshotFailed", error: ex);
            return new Dictionary<int, int>();
        }
    }

    private void ReportOnce(IAudioSession session, string msg, string reason, Exception? error, int? root = null)
    {
        if (!_reported.Add(session.Key))
        {
            return;
        }

        var data = new Dictionary<string, object?>
        {
            ["pid"] = session.Pid,
            ["reason"] = reason,
        };
        if (root is { } rootPid)
        {
            data["rootPid"] = rootPid;
        }

        _log.Warn(Src, msg, data, error);
    }

    private void DropBackend()
    {
        var backend = _backend;
        _backend = null;
        if (backend is null)
        {
            return;
        }

        try
        {
            backend.Dispose();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _log.Warn(Src, "audioBackendDisposeFailed", error: ex);
        }
    }

    private static void ReleaseHandle(IAudioSession session)
    {
        try
        {
            session.Dispose();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Zwolnienie uchwytu COM nie może zepsuć skanu.
            _ = ex;
        }
    }

    /// <summary>
    /// Sesja przypisana do drzewa z regułą: korzeń, ostatnia wymuszona wartość reguły, poziomy kanałów sprzed
    /// wyciszenia (tylko w pamięci; <c>null</c> przy zwykłym wyciszeniu) i znacznik zgłoszonego nadpisania.
    /// </summary>
    private sealed record Assigned(int Root, bool Value, float[]? Levels)
    {
        public int Root { get; set; } = Root;

        public bool OverrideReported { get; init; }
    }
}
