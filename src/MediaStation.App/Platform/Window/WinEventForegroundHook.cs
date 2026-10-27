using System;
using System.Collections.Generic;
using MediaStation.Core.Contracts;
using MediaStation.Core.Logging;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Accessibility;
using Windows.Win32.UI.WindowsAndMessaging;

namespace MediaStation.App.Platform.Window;

/// <summary>
/// Hook <c>EVENT_SYSTEM_FOREGROUND</c> (AD-15): każda zmiana okna na pierwszym planie, także po komendzie samego
/// WindowManagera albo Alt+Tab, trafia do Orchestratora jako <see cref="ForegroundChanged"/> z oknem najwyższego
/// poziomu (<c>GA_ROOT</c>). Tryb <c>WINEVENT_OUTOFCONTEXT</c> dostarcza callback przez pętlę komunikatów wątku,
/// który zarejestrował hook, więc tworzy go i zwalnia wyłącznie wątek WindowManagera.
/// </summary>
internal sealed class WinEventForegroundHook : IDisposable
{
    private readonly ILog _log;
    private readonly Action<IOrchestratorMessage> _post;

    // Delegat musi żyć tak długo jak hook: system trzyma na niego wskaźnik.
    private readonly WINEVENTPROC _callback;
    private HWINEVENTHOOK _hook;

    public WinEventForegroundHook(ILog log, Action<IOrchestratorMessage> post)
    {
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(post);

        _log = log;
        _post = post;
        _callback = OnEvent;
    }

    /// <summary>Rejestruje hook na wątku wołającego; błąd rejestracji jest logowany, bez rzucania (AD-4).</summary>
    public void Start()
    {
        var hook = PInvoke.SetWinEventHook(
            PInvoke.EVENT_SYSTEM_FOREGROUND,
            PInvoke.EVENT_SYSTEM_FOREGROUND,
            HMODULE.Null,
            _callback,
            0,
            0,
            PInvoke.WINEVENT_OUTOFCONTEXT);
        if (hook.IsNull)
        {
            _log.Warn(WindowCommandExecutor.Src, "foregroundHookFailed");
            return;
        }

        _hook = hook;
    }

    public void Dispose()
    {
        if (!_hook.IsNull)
        {
            PInvoke.UnhookWinEvent(_hook);
            _hook = default;
        }
    }

    private unsafe void OnEvent(HWINEVENTHOOK hook, uint eventType, HWND hwnd, int idObject, int idChild, uint eventThread, uint eventTime)
    {
        try
        {
            if (eventType != PInvoke.EVENT_SYSTEM_FOREGROUND || hwnd.IsNull)
            {
                return;
            }

            var root = PInvoke.GetAncestor(hwnd, GET_ANCESTOR_FLAGS.GA_ROOT);
            if (root.IsNull)
            {
                root = hwnd;
            }

            _post(new ForegroundChanged((long)(nint)root.Value));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _log.Error(WindowCommandExecutor.Src, "foregroundHookCallbackFailed", ex);
        }
    }
}
