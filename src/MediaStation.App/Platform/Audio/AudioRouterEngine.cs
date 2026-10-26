using System;
using System.Collections.Generic;
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
/// łańcuchu PID, wycisza je przez <c>ISimpleAudioVolume</c> z zapasem w <c>IChannelAudioVolume</c> (wszystkie kanały
/// na 0, odciszenie przywraca zapamiętane poziomy) i odciska tylko to, co sam wyciszył. Wołany wyłącznie z wątku
/// AudioRoutera; nie rzuca.
/// </summary>
internal sealed class AudioRouterEngine : IDisposable
{
    /// <summary>Źródło wpisów logu AudioRoutera.</summary>
    public const string Src = "audio";

    private readonly Func<IAudioSessionBackend> _backendFactory;
    private readonly Func<IReadOnlyList<KeyValuePair<int, int>>> _processSnapshot;
    private readonly ILog _log;
    private readonly AudioRule _rule = new();
    private readonly Dictionary<string, Muted> _ledger = new(StringComparer.Ordinal);
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

    /// <summary>Czy jakiekolwiek drzewo ma regułę „wycisz”.</summary>
    public bool AnyMuted => _rule.AnyMuted;

    /// <summary>Czy silnik trzyma sesje, które sam wyciszył (do odciszenia, także gdy odciszenie nie powiodło się).</summary>
    public bool HasMutedSessions => _ledger.Count > 0;

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
    /// Skan sesji wszystkich aktywnych urządzeń: nowe sesje w wyciszonym drzewie są wyciszane, sesje wyciszone przez
    /// nas w odciszonym drzewie wracają do stanu sprzed wyciszenia. Nie rzuca.
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
                Release(session);
            }
        }
    }

    /// <summary>Odciszenie wszystkiego, co wyciszył silnik (wyjście z aplikacji nie zostawia przeglądarki bez dźwięku), i zwolnienie backendu.</summary>
    public void Dispose()
    {
        if (_ledger.Count > 0)
        {
            foreach (var root in _rule.Roots.ToArray())
            {
                _rule.Set(root, false);
            }

            Sweep();
        }

        DropBackend();
    }

    private SweepResult Apply(IReadOnlyList<IAudioSession> sessions)
    {
        var parents = ReadParents();

        // Korzeń martwego procesu nie obejmuje już niczego, a jego PID mógłby wrócić do obcego procesu. Wpisy księgi
        // zostają: pętla niżej odciszy ich sesje (reguły korzenia już nie ma), więc przeżywający potomek nie zostaje
        // wyciszony na zawsze.
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
                else if (session.Pid > 0 && !parents.ContainsKey(session.Pid) && _rule.AnyMuted)
                {
                    unresolved = true;
                }

                if (_ledger.TryGetValue(session.Key, out var entry))
                {
                    if (_rule.IsMuted(entry.Root) != true)
                    {
                        Restore(session, entry);
                        _ledger.Remove(session.Key);
                    }
                }
                else if (governing is { } muteRoot && _rule.IsMuted(muteRoot) == true)
                {
                    Mute(session, muteRoot);
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // Sesja mogła zniknąć między enumeracją a wywołaniem; jeden wpis na sesję, bez zalewu logu przy skanie co sekundę.
                ReportOnce(session, "audioSessionFailed", ex.GetType().Name, ex);
            }
        }

        foreach (var stale in _ledger.Keys.Where(key => !present.Contains(key)).ToArray())
        {
            _ledger.Remove(stale);
        }

        _reported.IntersectWith(present);
        return new SweepResult(false, unresolved, perRoot);
    }

    private void Mute(IAudioSession session, int root)
    {
        Muted entry;
        if (session.TrySetMute(true))
        {
            entry = new Muted(root, null);
        }
        else
        {
            var levels = session.TryGetChannelVolumes();
            if (levels is not { Length: > 0 } || !session.TrySetChannelVolumes(new float[levels.Length]))
            {
                ReportOnce(session, "audioSessionSkipped", "noVolumeInterface", error: null, root);
                return;
            }

            entry = new Muted(root, levels);
            _log.Info(Src, "audioChannelFallback", new Dictionary<string, object?>
            {
                ["rootPid"] = root,
                ["pid"] = session.Pid,
            });
        }

        _ledger[session.Key] = entry;
        _log.Info(Src, "audioSessionMuted", new Dictionary<string, object?>
        {
            ["rootPid"] = root,
            ["pid"] = session.Pid,
        });
    }

    private static void Restore(IAudioSession session, Muted entry)
    {
        if (entry.Levels is { } levels)
        {
            session.TrySetChannelVolumes(levels);
        }
        else
        {
            session.TrySetMute(false);
        }
    }

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

    private static void Release(IAudioSession session)
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

    /// <summary>Sesja wyciszona przez nas: korzeń reguły i poziomy kanałów (<c>null</c> = zwykłe wyciszenie).</summary>
    private sealed record Muted(int Root, float[]? Levels);
}
