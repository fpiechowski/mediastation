using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using MediaStation.Core.Config;
using MediaStation.Core.Input;
using MediaStation.Core.Logging;
using MediaStation.Core.Ports;
using ActionMessage = MediaStation.Core.Contracts.Action;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input;
using Windows.Win32.UI.Input.XboxController;
using Windows.Win32.UI.WindowsAndMessaging;

namespace MediaStation.App.Platform.Input;

/// <summary>
/// Wątek Input (AD-2, AD-8): własna pętla komunikatów i okno message-only, na nich hooki <c>WH_KEYBOARD_LL</c>
/// i <c>WH_MOUSE_LL</c>, Raw Input <c>RIDEV_INPUTSINK</c> tylko dla HID Consumer, polling XInput (indeks 0,
/// co 10 ms) oraz <c>WM_TIMER</c> terminu long-press albo końca kalibracji. W trybie <c>osk</c> z <c>clipHwnd</c>
/// hub więzi kursor w prostokącie okna klawiatury (<c>ClipCursor</c>) i zwalnia clip przy każdej zmianie wiersza
/// routingu oraz w <see cref="Dispose"/>. Adapter nie decyduje o niczym: połknięcie, <c>kind</c> i <c>source</c>
/// pochodzą wprost z <see cref="IInputTarget"/> (tryb główny dostaje <see cref="InputPolicy"/>,
/// <c>--calibrate-input</c> — <see cref="InputCalibration"/>), a callback hooka tylko wkłada Actions do
/// <see cref="ActionQueue"/> i uzbraja timer — nie czeka, nie loguje i nie gubi Action (także Home przy
/// zawieszonym UI).
/// Porażki rejestracji degradują: brak okna kończy wątek, brak Raw Input zostawia hooki (tylko <c>source</c>
/// spada do <c>keyboard</c>), brak hooka myszy wyłącza połknięcia wskaźnika, a brak hooka klawiatury kończy wątek
/// po wpisie <c>inputHubError</c>.
/// </summary>
internal sealed unsafe class InputHub : IDisposable
{
    private const string WindowClassName = "MediaStationInputHub";

    /// <summary>Strona usage HID Consumer rejestrowana w Raw Input (<c>usUsagePage = 0x0C</c>).</summary>
    private const int HidConsumerUsagePage = 0x0C;

    /// <summary>Identyfikator timera progu long-press (i końca kalibracji) w oknie Input.</summary>
    private static readonly nuint DeadlineTimerId = 1;

    /// <summary>Identyfikator timera pollingu pada XInput w oknie Input.</summary>
    private static readonly nuint GamepadTimerId = 2;

    /// <summary><c>WM_APP + 1</c>: powiadomienie z wątku Orchestratora, że wiersz routingu się zmienił.</summary>
    private const uint RoutingChangedMessage = 0x8000 + 1;

    /// <summary>Okres pollingu XInput; bez <c>timeBeginPeriod</c> ziarno zegara systemowego jest większe.</summary>
    private const uint GamepadPollMs = 10;

    /// <summary><c>ERROR_DEVICE_NOT_CONNECTED</c> zwracane przez XInput, gdy pad nie jest podłączony.</summary>
    private const uint ErrorDeviceNotConnected = (uint)WIN32_ERROR.ERROR_DEVICE_NOT_CONNECTED;

    /// <summary>Górna granica czekania na zakończenie wątku w <see cref="Dispose"/>; zawieszony wątek nie może zamrozić wyjścia.</summary>
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(10);

    private readonly IInputTarget _target;
    private readonly ActionQueue _queue;
    private readonly IInputHubErrorSink _errors;
    private readonly ILog? _registrationTraceLog;
    private readonly TimeProvider _time;
    private readonly InputRoutingState _routing;
    private readonly ClipCursorGate _clipGate;
    private readonly InputClock _clock = new();
    private readonly Dictionary<IntPtr, string> _devicePaths = [];
    private readonly List<GamepadButton> _gamepadButtons = [];
    private readonly ManualResetEventSlim _threadReady = new(initialState: false);
    private readonly Thread _thread;
    // Delegate w polach: GC nie może zebrać callbacków, dopóki hook i okno ich używają.
    private WNDPROC? _windowProc;
    private HOOKPROC? _hookProc;
    private HOOKPROC? _mouseHookProc;

    private HWND _window;
    private HHOOK _hook;
    private HHOOK _mouseHook;
    private byte[] _rawBuffer = new byte[256];
    private uint _threadId;
    private ushort _gamepadState;
    private bool _gamepadPolled;
    private bool _gamepadFaulted;
    private bool _started;
    private bool _disposed;
    private int? _clipErrorCode;

    public InputHub(
        IInputTarget target,
        ActionQueue queue,
        IInputHubErrorSink errors,
        TimeProvider time,
        InputRoutingState routing,
        ILog? registrationTraceLog = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(queue);
        ArgumentNullException.ThrowIfNull(errors);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(routing);

        _target = target;
        _queue = queue;
        _errors = errors;
        _time = time;
        _routing = routing;
        _registrationTraceLog = registrationTraceLog;
        _clipGate = new ClipCursorGate(MeasureClipWindow, ApplyClipCursor);
        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "input",
        };

        // Każda przyjęta zmiana wiersza routingu musi zwolnić albo przestawić clip kursora, nawet gdy wątek Input
        // stoi w GetMessage; handler tylko kolejkuje komunikat na wątku Input.
        _routing.Changed += OnRoutingChanged;
    }

    /// <summary>Startuje wątek Input; kolejne wywołanie jest błędem wołającego.</summary>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_started)
        {
            throw new InvalidOperationException("Wątek Input został już wystartowany.");
        }

        _thread.Start();
        _started = true;
    }

    /// <summary>
    /// Kończy pętlę komunikatów przez <c>WM_QUIT</c> i czeka na wątek; hook, timer i okno zwalnia wątek Input,
    /// bo tylko on może zniszczyć własne okno. Kolejne wywołanie jest no-opem.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _routing.Changed -= OnRoutingChanged;

        if (!_started)
        {
            _threadReady.Dispose();
            return;
        }

        // Wątek ustawia znacznik po utworzeniu kolejki komunikatów, więc WM_QUIT zawsze ma gdzie trafić;
        // przy wcześniejszej porażce wątek kończy się sam i Join wraca od razu. Oba czekania są ograniczone:
        // zawieszony wątek Input nie może zamrozić wyjścia aplikacji (zostaje po nim wpis w logu).
        if (!_threadReady.Wait(ShutdownTimeout))
        {
            Report(InputHubOperations.MessageWindow, null);
            return;
        }

        PInvoke.PostThreadMessage(_threadId, PInvoke.WM_QUIT, default, default);
        if (!_thread.Join(ShutdownTimeout))
        {
            Report(InputHubOperations.MessageWindow, null);
            return;
        }

        _threadReady.Dispose();
    }

    private void Run()
    {
        _threadId = PInvoke.GetCurrentThreadId();

        try
        {
            if (!CreateMessageWindow())
            {
                return;
            }

            // Od utworzenia okna kolejka komunikatów istnieje, więc Dispose może przerwać pętlę.
            _threadReady.Set();

            RegisterRawInput(_window);

            if (!InstallHook())
            {
                return;
            }

            // Termin startowy celu (koniec kalibracji albo brak long-press) uzbrajamy po hooku: od tej chwili
            // każdy WM_TIMER ma już prawo przyjść, a kalibracja kończy się także bez żadnego wejścia.
            ArmTimer();
            ArmGamepadPoll();

            PumpMessages();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Nieoczekiwany błąd setupu nie może wywrócić procesu ani zostawić pustego wątku z hookiem.
            Report(InputHubOperations.MessageWindow, null);
        }
        finally
        {
            // Porażka przed utworzeniem okna też musi zwolnić Dispose.
            _threadReady.Set();
            ReleaseNativeResources();
        }
    }

    private bool CreateMessageWindow()
    {
        _windowProc = WindowProc;
        var module = PInvoke.GetModuleHandle(default(PCWSTR));

        fixed (char* className = WindowClassName)
        {
            var windowClass = new WNDCLASSEXW
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
                lpfnWndProc = _windowProc,
                hInstance = module,
                lpszClassName = className,
            };

            if (PInvoke.RegisterClassEx(in windowClass) == 0)
            {
                Report(InputHubOperations.MessageWindow, Marshal.GetLastWin32Error());
                return false;
            }

            _window = PInvoke.CreateWindowEx(
                dwExStyle: 0,
                className,
                lpWindowName: className,
                dwStyle: 0,
                X: 0,
                Y: 0,
                nWidth: 0,
                nHeight: 0,
                hWndParent: HWND.HWND_MESSAGE,
                hMenu: default,
                hInstance: module,
                lpParam: null);
        }

        if (_window.IsNull)
        {
            Report(InputHubOperations.MessageWindow, Marshal.GetLastWin32Error());
            return false;
        }

        return true;
    }

    /// <summary>
    /// Rejestruje wyłącznie HID Consumer. Rejestracja Raw Input klawiatury wyciszała LL keyboard hook przy shellu
    /// na wierzchu, więc HookCallback pozostaje jedynym źródłem klawiaturowych Actions.
    /// </summary>
    private void RegisterRawInput(HWND target)
    {
        var consumer = new RAWINPUTDEVICE
        {
            usUsagePage = 0x0C,
            usUsage = 0x01,
            dwFlags = RAWINPUTDEVICE_FLAGS.RIDEV_INPUTSINK,
            hwndTarget = target,
        };

        ReadOnlySpan<RAWINPUTDEVICE> devices = [consumer];
        bool registered = PInvoke.RegisterRawInputDevices(devices, (uint)sizeof(RAWINPUTDEVICE));
        if (_registrationTraceLog is not null)
        {
            _registrationTraceLog.Info("input", "rawInputRegistration", new Dictionary<string, object?>
            {
                ["keyboard"] = false,
                ["hidConsumer"] = true,
                ["usagePage"] = "0x0C",
                ["usage"] = "0x01",
                ["success"] = registered,
            });
        }

        if (!registered)
        {
            Report(InputHubOperations.RawInput, Marshal.GetLastWin32Error());
        }
    }

    private bool InstallHook()
    {
        _hookProc = HookCallback;
        _mouseHookProc = MouseHookCallback;
        var module = PInvoke.GetModuleHandle(default(PCWSTR));

        _hook = PInvoke.SetWindowsHookEx(WINDOWS_HOOK_ID.WH_KEYBOARD_LL, _hookProc, module, dwThreadId: 0);
        if (_hook.IsNull)
        {
            Report(InputHubOperations.KeyboardHook, Marshal.GetLastWin32Error());
            return false;
        }

        // Porażka hooka myszy tylko degraduje: kliknięcia poza OSK nie są wtedy połykane, ale klawiatura,
        // pad i timer działają dalej (AD-8), a wątek Input nie może stanąć z powodu brakującego hooka.
        _mouseHook = PInvoke.SetWindowsHookEx(WINDOWS_HOOK_ID.WH_MOUSE_LL, _mouseHookProc, module, dwThreadId: 0);
        if (_mouseHook.IsNull)
        {
            Report(InputHubOperations.MouseHook, Marshal.GetLastWin32Error());
        }

        return true;
    }

    private void PumpMessages()
    {
        while (true)
        {
            var result = PInvoke.GetMessage(out var message, default, wMsgFilterMin: 0, wMsgFilterMax: 0);

            // GetMessage zwraca -1 przy błędzie: pętla na martwej kolejce kręciłaby się bez końca.
            if (result.Value < 0)
            {
                Report(InputHubOperations.MessageWindow, Marshal.GetLastWin32Error());
                return;
            }

            // WM_QUIT kończy pętlę, zanim pojawi się komunikat.
            if (!result)
            {
                return;
            }

            PInvoke.TranslateMessage(message);
            PInvoke.DispatchMessage(message);
        }
    }

    private LRESULT WindowProc(HWND hwnd, uint message, WPARAM wParam, LPARAM lParam)
    {
        if (message == PInvoke.WM_INPUT)
        {
            OnRawInput((HRAWINPUT)(nint)lParam);
        }
        else if (message == PInvoke.WM_TIMER && (nuint)wParam == DeadlineTimerId)
        {
            OnTimer();
        }
        else if (message == PInvoke.WM_TIMER && (nuint)wParam == GamepadTimerId)
        {
            PollGamepad();
        }
        else if (message == RoutingChangedMessage)
        {
            // Powiadomienie z wątku Orchestratora: clip przelicza wyłącznie wątek Input (jeden właściciel cache).
            SyncClip();
        }

        return PInvoke.DefWindowProc(hwnd, message, wParam, lParam);
    }

    /// <summary>
    /// Czyta <c>WM_INPUT</c>: ścieżkę urządzenia z cache, ciało raportu bez Win32 (<see cref="RawInputBody"/>) i czas
    /// w tej samej jednostce co hook, a potem oddaje celowi sam raport (Consumer) albo <c>WM_INPUT</c> klawiatury —
    /// decyzja o <c>source</c> jest w Core. Timer uzbrajamy także po <c>WM_INPUT</c>, bo kalibracja domyka wpis
    /// terminem <c>flushAt</c> liczonym od zdarzenia urządzenia.
    /// </summary>
    private void OnRawInput(HRAWINPUT raw)
    {
        try
        {
            var headerSize = (uint)sizeof(RAWINPUTHEADER);
            uint size = 0;
            var query = PInvoke.GetRawInputData(raw, RAW_INPUT_DATA_COMMAND_FLAGS.RID_INPUT, Span<byte>.Empty, ref size, headerSize);
            if (query == uint.MaxValue)
            {
                // Uszkodzona ścieżka Raw Input nie może zniknąć bez śladu w logu.
                Report(InputHubOperations.RawInput, Marshal.GetLastWin32Error());
                return;
            }

            if (size == 0)
            {
                return;
            }

            if (size > _rawBuffer.Length)
            {
                _rawBuffer = new byte[size];
            }

            var buffer = _rawBuffer.AsSpan();
            uint copied = (uint)buffer.Length;

            // GetRawInputData zwraca liczbę skopiowanych bajtów; pcbSize to rozmiar bufora na wejściu (API go nie
            // zmniejsza), więc wynik sprawdza helper, a nie porównanie z `copied`.
            var read = PInvoke.GetRawInputData(raw, RAW_INPUT_DATA_COMMAND_FLAGS.RID_INPUT, buffer, ref copied, headerSize);
            if (!RawInputReport.IsValid(read, headerSize))
            {
                // Kod Win32 ma sens tylko przy uint.MaxValue; krótszy odczyt to już uszkodzony raport.
                Report(InputHubOperations.RawInput, read == uint.MaxValue ? Marshal.GetLastWin32Error() : null);
                return;
            }

            var header = MemoryMarshal.Read<RAWINPUTHEADER>(buffer);
            var devicePath = DevicePath(header.hDevice);
            if (devicePath is null)
            {
                return;
            }

            var body = buffer.Slice((int)headerSize, (int)(read - headerSize));
            var timeMs = CurrentTimeMs();
            if (header.dwType == (uint)RID_DEVICE_INFO_TYPE.RIM_TYPEKEYBOARD)
            {
                if (!RawInputBody.TryReadKeyboard(body, out var virtualKey, out var scan, out var down))
                {
                    // Ciało niezgodne z RAWKEYBOARD: surowe wartości nieznane, ale polityka nadal dostaje
                    // urządzenie i czas, żeby korelacja source w trybie głównym nie znikła.
                    virtualKey = 0;
                    scan = 0;
                    down = false;
                }

                _target.OnRawKeyboard(new RawKeyboardInput(devicePath, timeMs, virtualKey, scan, down));
                ArmTimer();
            }
            else if (header.dwType == (uint)RID_DEVICE_INFO_TYPE.RIM_TYPEHID)
            {
                if (!RawInputBody.TryReadHidUsage(body, out var usage))
                {
                    usage = 0;
                }

                _target.OnHidConsumer(new HidConsumerReport(devicePath, timeMs, HidConsumerUsagePage, usage));
                ArmTimer();
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Zły raport nie może wywrócić pętli komunikatów ani zgubić kolejnych klawiszy.
            Report(InputHubOperations.RawInput, null);
        }
    }

    /// <summary>Callback hooka: jedyna decyzja z <see cref="IInputTarget"/> i natychmiastowy powrót do systemu.</summary>
    private LRESULT HookCallback(int code, WPARAM wParam, LPARAM lParam)
    {
        if (code >= 0)
        {
            try
            {
                var data = *(KBDLLHOOKSTRUCT*)(nint)lParam;
                var message = (uint)wParam;
                var down = message == PInvoke.WM_KEYDOWN || message == PInvoke.WM_SYSKEYDOWN;
                var stroke = new KeyStroke(
                    (int)data.vkCode,
                    down ? KeyTransition.Down : KeyTransition.Up,
                    (data.flags & KBDLLHOOKSTRUCT_FLAGS.LLKHF_INJECTED) != 0,
                    _clock.Normalize(data.time),
                    (int)data.scanCode,
                    unchecked((long)data.dwExtraInfo));

                var decision = _target.OnKey(stroke);
                Enqueue(decision.Actions);
                ArmTimer();

                return decision.Swallow ? (LRESULT)1 : PInvoke.CallNextHookEx(_hook, code, wParam, lParam);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // Hook nie może przerzucić wyjątku do systemu; klawisz przepuszczamy, żeby nie zawiesić klawiatury.
                Report(InputHubOperations.HookCallback, null);
            }
        }

        return PInvoke.CallNextHookEx(_hook, code, wParam, lParam);
    }

    /// <summary>
    /// Callback hooka myszy: przelicza clip (jeśli wiersz routingu się zmienił), oddaje zdarzenie polityce i wraca
    /// natychmiast do systemu (AD-8: <c>LowLevelHooksTimeout</c>). Wskaźnik nigdy nie daje Actions, a jego wyjątek
    /// nie może wrócić do systemu — zdarzenie wtedy przechodzi.
    /// </summary>
    private LRESULT MouseHookCallback(int code, WPARAM wParam, LPARAM lParam)
    {
        if (code >= 0)
        {
            try
            {
                if (PointerStrokeOf(*(MSLLHOOKSTRUCT*)(nint)lParam, (uint)wParam) is { } pointer)
                {
                    SyncClip();
                    var decision = _target.OnPointer(pointer, _clipGate.Current);
                    Enqueue(decision.Actions);

                    return decision.Swallow ? (LRESULT)1 : PInvoke.CallNextHookEx(_mouseHook, code, wParam, lParam);
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // Hook nie może przerzucić wyjątku do systemu; zdarzenie przepuszczamy, żeby nie zawiesić myszy.
                Report(InputHubOperations.HookCallback, null);
            }
        }

        return PInvoke.CallNextHookEx(_mouseHook, code, wParam, lParam);
    }

    /// <summary>
    /// Mapuje komunikat <c>WH_MOUSE_LL</c> i <c>MSLLHOOKSTRUCT</c> na zdarzenie wskaźnika w jednostkach Core;
    /// <c>null</c> dla komunikatu spoza zestawu hooka (przepuszczany bez decyzji). Czas jest w tej samej jednostce
    /// co <see cref="KeyStroke"/> (pole <c>time</c> hooka), a pozycja — w pikselach fizycznych, tak jak clip.
    /// </summary>
    private PointerStroke? PointerStrokeOf(MSLLHOOKSTRUCT data, uint message)
    {
        PointerMessage kind;
        var button = PointerButton.None;
        var wheelDelta = 0;

        switch (message)
        {
            case PInvoke.WM_MOUSEMOVE:
                kind = PointerMessage.Move;
                break;
            case PInvoke.WM_LBUTTONDOWN:
            case PInvoke.WM_LBUTTONUP:
                kind = message == PInvoke.WM_LBUTTONDOWN ? PointerMessage.ButtonDown : PointerMessage.ButtonUp;
                button = PointerButton.Left;
                break;
            case PInvoke.WM_RBUTTONDOWN:
            case PInvoke.WM_RBUTTONUP:
                kind = message == PInvoke.WM_RBUTTONDOWN ? PointerMessage.ButtonDown : PointerMessage.ButtonUp;
                button = PointerButton.Right;
                break;
            case PInvoke.WM_MBUTTONDOWN:
            case PInvoke.WM_MBUTTONUP:
                kind = message == PInvoke.WM_MBUTTONDOWN ? PointerMessage.ButtonDown : PointerMessage.ButtonUp;
                button = PointerButton.Middle;
                break;
            case PInvoke.WM_XBUTTONDOWN:
            case PInvoke.WM_XBUTTONUP:
                kind = message == PInvoke.WM_XBUTTONDOWN ? PointerMessage.ButtonDown : PointerMessage.ButtonUp;
                button = (data.mouseData >> 16) == 1 ? PointerButton.XButton1 : PointerButton.XButton2;
                break;
            case PInvoke.WM_MOUSEWHEEL:
            case PInvoke.WM_MOUSEHWHEEL:
                kind = PointerMessage.Wheel;
                wheelDelta = (short)(data.mouseData >> 16);
                break;
            default:
                return null;
        }

        return new PointerStroke(
            kind,
            button,
            data.pt.X,
            data.pt.Y,
            wheelDelta,
            (data.flags & PInvoke.LLMHF_INJECTED) != 0,
            _clock.Normalize(data.time),
            unchecked((long)data.dwExtraInfo));
    }

    /// <summary>
    /// Przelicza clip kursora z lokalnej kopii routingu (AD-8). Decyzję i stan trzyma <see cref="ClipCursorGate"/>
    /// bez Win32, a ten hub dostarcza tylko szwy <c>GetWindowRect</c> i <c>ClipCursor</c>; wątek Input jest jedynym
    /// właścicielem stanu. Porażka pomiaru albo clipu zwalnia ograniczenie i zostawia jeden wpis
    /// <c>inputHubError {operation: clipCursor}</c>, bo uwięzienie kursora w nieaktualnym prostokącie jest gorsze niż
    /// brak ograniczenia.
    /// </summary>
    private void SyncClip()
    {
        if (_clipGate.Apply(_routing.Current) == ClipOutcome.Failed)
        {
            Report(InputHubOperations.ClipCursor, _clipErrorCode);
        }
    }

    /// <summary>Szew <c>GetWindowRect</c>: prostokąt okna w pikselach fizycznych albo <c>null</c> w porażce.</summary>
    private ScreenRectangle? MeasureClipWindow(long handle)
    {
        if (!PInvoke.GetWindowRect((HWND)(nint)handle, out var rect))
        {
            _clipErrorCode = Marshal.GetLastWin32Error();
            return null;
        }

        return new ScreenRectangle(rect.left, rect.top, rect.right, rect.bottom);
    }

    /// <summary>Szew <c>ClipCursor</c>: prostokąt albo <c>null</c> do zwolnienia; <c>false</c> znaczy porażkę.</summary>
    private bool ApplyClipCursor(ScreenRectangle? rectangle)
    {
        var applied = rectangle is { } clip
            ? PInvoke.ClipCursor(new RECT
            {
                left = clip.Left,
                top = clip.Top,
                right = clip.Right,
                bottom = clip.Bottom,
            })
            : PInvoke.ClipCursor(null);

        if (applied)
        {
            return true;
        }

        _clipErrorCode = Marshal.GetLastWin32Error();
        return false;
    }

    /// <summary>
    /// Powiadomienie o nowym wierszu routingu z wątku Orchestratora: tylko nieblokujące <c>PostMessage</c>, bo zapis
    /// kopii nie może czekać na wątek Input. Komunikat wysłany przed utworzeniem okna po prostu nie ma dokąd trafić —
    /// pierwsze zdarzenie myszy i tak porówna <c>epoch</c>.
    /// </summary>
    private void OnRoutingChanged(InputRouting routing)
    {
        if (_window.IsNull)
        {
            return;
        }

        PInvoke.PostMessage(_window, RoutingChangedMessage, default, default);
    }

    /// <summary>
    /// Timer progu long-press albo końca kalibracji: <see cref="IInputTarget.OnTimer"/> sam sprawdza termin
    /// i nigdy nie połyka.
    /// </summary>
    private void OnTimer()
    {
        try
        {
            var decision = _target.OnTimer(_time.GetUtcNow());
            Enqueue(decision.Actions);
            ArmTimer();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Report(InputHubOperations.HookCallback, null);
        }
    }

    /// <summary>
    /// Polling XInput na wątku Input: migawka powstaje tylko przy zmianie stanu, więc powtarzany odczyt nic nie daje,
    /// a brak pada albo błąd XInput daje jedną migawkę oznaczoną jako niedostępna (zwolnienie trzymań bez Actions).
    /// Porażka inna niż brak urządzenia daje jeden wpis <c>inputHubError {operation: gamepad}</c> na przejście
    /// w błąd, nigdy wpis na poll.
    /// </summary>
    private void PollGamepad()
    {
        ushort buttons;
        try
        {
            if (!TryReadGamepad(out buttons))
            {
                MarkGamepadUnavailable();
                return;
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Zły odczyt pada nie może wywrócić pętli komunikatów ani zatrzymać klawiatury.
            if (!_gamepadFaulted)
            {
                _gamepadFaulted = true;
                Report(InputHubOperations.Gamepad, null);
            }

            MarkGamepadUnavailable();
            return;
        }

        if (_gamepadPolled && buttons == _gamepadState)
        {
            return;
        }

        _gamepadPolled = true;
        _gamepadState = buttons;
        BuildGamepadButtons(buttons);
        DeliverGamepad(Environment.TickCount64, _gamepadButtons);
    }

    /// <summary>
    /// Jeden odczyt XInput: sukces zwraca bitmaskę przycisków, a brak pada albo błąd — <c>false</c>.
    /// Porażka inna niż <c>ERROR_DEVICE_NOT_CONNECTED</c> daje jeden wpis na przejście w błąd.
    /// </summary>
    private bool TryReadGamepad(out ushort buttons)
    {
        buttons = 0;
        var result = PInvoke.XInputGetState(dwUserIndex: 0, out var state);
        if (result == (uint)WIN32_ERROR.ERROR_SUCCESS)
        {
            _gamepadFaulted = false;
            buttons = (ushort)state.Gamepad.wButtons;
            return true;
        }

        if (result == ErrorDeviceNotConnected)
        {
            // Odpięcie i ponowne podpięcie kończy poprzedni błąd, więc następny fault znów jest nowym przejściem.
            _gamepadFaulted = false;
            return false;
        }

        if (!_gamepadFaulted)
        {
            _gamepadFaulted = true;
            Report(InputHubOperations.Gamepad, (int)result);
        }

        return false;
    }

    /// <summary>
    /// Buduje migawkę przycisków z bitmaski <c>XINPUT_GAMEPAD</c> w kolejności <see cref="GamepadButton"/>; lista
    /// jest wielokrotnego użytku, bo <see cref="IInputTarget.OnGamepad"/> czyta ją synchronicznie.
    /// </summary>
    private void BuildGamepadButtons(ushort buttons)
    {
        _gamepadButtons.Clear();
        AddGamepadButton(buttons, XINPUT_GAMEPAD_BUTTON_FLAGS.XINPUT_GAMEPAD_DPAD_UP, GamepadButton.DpadUp);
        AddGamepadButton(buttons, XINPUT_GAMEPAD_BUTTON_FLAGS.XINPUT_GAMEPAD_DPAD_DOWN, GamepadButton.DpadDown);
        AddGamepadButton(buttons, XINPUT_GAMEPAD_BUTTON_FLAGS.XINPUT_GAMEPAD_DPAD_LEFT, GamepadButton.DpadLeft);
        AddGamepadButton(buttons, XINPUT_GAMEPAD_BUTTON_FLAGS.XINPUT_GAMEPAD_DPAD_RIGHT, GamepadButton.DpadRight);
        AddGamepadButton(buttons, XINPUT_GAMEPAD_BUTTON_FLAGS.XINPUT_GAMEPAD_START, GamepadButton.Start);
        AddGamepadButton(buttons, XINPUT_GAMEPAD_BUTTON_FLAGS.XINPUT_GAMEPAD_BACK, GamepadButton.Back);
        AddGamepadButton(buttons, XINPUT_GAMEPAD_BUTTON_FLAGS.XINPUT_GAMEPAD_LEFT_THUMB, GamepadButton.LeftThumb);
        AddGamepadButton(buttons, XINPUT_GAMEPAD_BUTTON_FLAGS.XINPUT_GAMEPAD_RIGHT_THUMB, GamepadButton.RightThumb);
        AddGamepadButton(buttons, XINPUT_GAMEPAD_BUTTON_FLAGS.XINPUT_GAMEPAD_LEFT_SHOULDER, GamepadButton.LeftShoulder);
        AddGamepadButton(buttons, XINPUT_GAMEPAD_BUTTON_FLAGS.XINPUT_GAMEPAD_RIGHT_SHOULDER, GamepadButton.RightShoulder);
        AddGamepadButton(buttons, XINPUT_GAMEPAD_BUTTON_FLAGS.XINPUT_GAMEPAD_A, GamepadButton.A);
        AddGamepadButton(buttons, XINPUT_GAMEPAD_BUTTON_FLAGS.XINPUT_GAMEPAD_B, GamepadButton.B);
        AddGamepadButton(buttons, XINPUT_GAMEPAD_BUTTON_FLAGS.XINPUT_GAMEPAD_X, GamepadButton.X);
        AddGamepadButton(buttons, XINPUT_GAMEPAD_BUTTON_FLAGS.XINPUT_GAMEPAD_Y, GamepadButton.Y);
    }

    private void AddGamepadButton(ushort buttons, XINPUT_GAMEPAD_BUTTON_FLAGS flag, GamepadButton button)
    {
        if (((XINPUT_GAMEPAD_BUTTON_FLAGS)buttons & flag) != 0)
        {
            _gamepadButtons.Add(button);
        }
    }

    /// <summary>
    /// Brak pada albo błąd XInput: jedna niedostępna migawka na przejście zwalnia trzymania pada bez Actions,
    /// a kolejne pollowanie nie wysyła nic, dopóki odczyt nie wróci.
    /// </summary>
    private void MarkGamepadUnavailable()
    {
        if (!_gamepadPolled)
        {
            return;
        }

        _gamepadPolled = false;
        _gamepadState = 0;
        DeliverGamepad(Environment.TickCount64, Array.Empty<GamepadButton>(), available: false);
    }

    /// <summary>
    /// Oddaje migawkę celowi i uzbraja timer terminu. Wyjątek celu nie może wrócić do <c>WndProc</c>; raport idzie
    /// jako <c>hookCallback</c>, bo <c>gamepad</c> jest zarezerwowane dla porażki odczytu XInput.
    /// </summary>
    private void DeliverGamepad(long timeMs, IReadOnlyList<GamepadButton> buttons, bool available = true)
    {
        try
        {
            var decision = _target.OnGamepad(new GamepadSnapshot(timeMs, buttons, available));
            Enqueue(decision.Actions);
            ArmTimer();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Report(InputHubOperations.HookCallback, null);
        }
    }

    private void Enqueue(IReadOnlyList<ActionMessage> actions)
    {
        for (var i = 0; i < actions.Count; i++)
        {
            _queue.Enqueue(actions[i]);
        }
    }

    /// <summary>
    /// Ustawia timer na <see cref="IInputTarget.NextDeadline"/> liczony na <see cref="TimeProvider"/>. Wczesny
    /// albo spóźniony <c>WM_TIMER</c> nic nie psuje: <see cref="IInputTarget.OnTimer"/> porównuje termin,
    /// a hub uzbraja timer ponownie, dopóki termin został.
    /// </summary>
    private void ArmTimer()
    {
        if (InputTimerDelay.Compute(_target.NextDeadline, _time.GetUtcNow()) is not { } elapsed)
        {
            PInvoke.KillTimer(_window, DeadlineTimerId);
            return;
        }

        // Ten sam identyfikator zastępuje poprzedni termin; brak timera zostawiłby `long` bez terminu.
        if (PInvoke.SetTimer(_window, DeadlineTimerId, elapsed, default) == 0)
        {
            Report(InputHubOperations.MessageWindow, Marshal.GetLastWin32Error());
        }
    }

    /// <summary>
    /// Uzbraja polling pada po starcie wątku. Porażka timera to <c>messageWindow</c>: degraduje tylko wejście z pada,
    /// a hook i kalibracja działają dalej.
    /// </summary>
    private void ArmGamepadPoll()
    {
        if (PInvoke.SetTimer(_window, GamepadTimerId, GamepadPollMs, default) == 0)
        {
            Report(InputHubOperations.MessageWindow, Marshal.GetLastWin32Error());
        }
    }

    /// <summary>Czas <c>WM_INPUT</c> w jednostce hooka (ms od startu systemu), po zdjęciu zawinięcia 32 bitów.</summary>
    private long CurrentTimeMs() => _clock.Normalize(unchecked((uint)PInvoke.GetMessageTime()));

    /// <summary>
    /// Ścieżka urządzenia Raw Input bez wielokrotnego <c>GetRawInputDeviceInfo</c>; cache po uchwycie.
    /// Brak uchwytu to wejście wstrzyknięte (np. własne <c>SendInput</c>) i nie jest porażką; realne urządzenie,
    /// którego ścieżki nie da się odczytać, daje <c>inputHubError {operation: rawInput}</c>, bo korelacja
    /// <c>source</c> spadłaby do <c>keyboard</c> bez śladu.
    /// </summary>
    private string? DevicePath(HANDLE device)
    {
        if (device.IsNull)
        {
            return null;
        }

        var key = (IntPtr)device;
        if (_devicePaths.TryGetValue(key, out var cached))
        {
            return cached;
        }

        // RIDI_DEVICENAME zwraca długość w znakach; pierwsze wywołanie z NULL tylko mierzy.
        uint chars = 0;
        var probe = PInvoke.GetRawInputDeviceInfo(device, RAW_INPUT_DEVICE_INFO_COMMAND.RIDI_DEVICENAME, null, &chars);
        if (probe == uint.MaxValue)
        {
            Report(InputHubOperations.RawInput, Marshal.GetLastWin32Error());
            return null;
        }

        if (chars == 0)
        {
            Report(InputHubOperations.RawInput, null);
            return null;
        }

        var buffer = new byte[((int)chars + 1) * 2];
        string? path;
        fixed (byte* name = buffer)
        {
            var count = chars + 1;
            if (PInvoke.GetRawInputDeviceInfo(device, RAW_INPUT_DEVICE_INFO_COMMAND.RIDI_DEVICENAME, name, &count) == uint.MaxValue)
            {
                Report(InputHubOperations.RawInput, Marshal.GetLastWin32Error());
                return null;
            }

            path = Marshal.PtrToStringUni((IntPtr)name);
        }

        if (string.IsNullOrEmpty(path))
        {
            Report(InputHubOperations.RawInput, null);
            return null;
        }

        _devicePaths[key] = path;
        return path;
    }

    /// <summary>Zwalnia hooki, clip kursora, timer i okno na wątku, który je utworzył.</summary>
    private void ReleaseNativeResources()
    {
        if (!_hook.IsNull)
        {
            PInvoke.UnhookWindowsHookEx(_hook);
            _hook = default;
        }

        if (!_mouseHook.IsNull)
        {
            PInvoke.UnhookWindowsHookEx(_mouseHook);
            _mouseHook = default;
        }

        // Bezwarunkowo, bo kursor nie może zostać uwięziony w prostokącie po wyjściu aplikacji, nawet gdy cache
        // zdążył się rozjechać z rzeczywistym clipem.
        if (!_clipGate.ForceRelease())
        {
            Report(InputHubOperations.ClipCursor, _clipErrorCode);
        }

        if (_window.IsNull)
        {
            return;
        }

        PInvoke.KillTimer(_window, DeadlineTimerId);
        PInvoke.KillTimer(_window, GamepadTimerId);
        PInvoke.DestroyWindow(_window);
        _window = default;
    }

    /// <summary>
    /// Raport porażki; wołany także z callbacków Win32 (hook, <c>WndProc</c>), więc wyjątek kanału błędów
    /// nie może wrócić do systemu ani wywrócić wątku Input.
    /// </summary>
    private void Report(string operation, int? errorCode)
    {
        try
        {
            _errors.Report(new InputHubError(operation, errorCode));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Kanał błędów nie jest częścią decyzji o wejściu: jego awaria nie może przerwać callbacku Win32.
        }
    }
}
