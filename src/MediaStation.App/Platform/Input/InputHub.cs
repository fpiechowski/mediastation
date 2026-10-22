using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using MediaStation.Core.Input;
using MediaStation.Core.Ports;
using ActionMessage = MediaStation.Core.Contracts.Action;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input;
using Windows.Win32.UI.WindowsAndMessaging;

namespace MediaStation.App.Platform.Input;

/// <summary>
/// Wątek Input (AD-2, AD-8): własna pętla komunikatów i okno message-only, na nich hook <c>WH_KEYBOARD_LL</c>,
/// Raw Input <c>RIDEV_INPUTSINK</c> (klawiatura i HID Consumer) oraz <c>WM_TIMER</c> terminu long-press albo
/// końca kalibracji. Adapter nie decyduje o niczym: połknięcie, <c>kind</c> i <c>source</c> pochodzą wprost z
/// <see cref="IInputTarget"/> (tryb główny dostaje <see cref="InputPolicy"/>, <c>--calibrate-input</c> —
/// <see cref="InputCalibration"/>), a callback hooka tylko wkłada Actions do <see cref="ActionQueue"/> i uzbraja
/// timer — nie czeka, nie loguje i nie gubi Action (także Home przy zawieszonym UI).
/// Porażki rejestracji degradują: brak okna kończy wątek, brak Raw Input zostawia hook (tylko <c>source</c>
/// spada do <c>keyboard</c>), brak hooka kończy wątek po wpisie <c>inputHubError</c>.
/// </summary>
internal sealed unsafe class InputHub : IDisposable
{
    private const string WindowClassName = "MediaStationInputHub";

    /// <summary>Strona usage HID Consumer rejestrowana w Raw Input (<c>usUsagePage = 0x0C</c>).</summary>
    private const int HidConsumerUsagePage = 0x0C;

    /// <summary>Identyfikator jedynego timera progu long-press w oknie Input.</summary>
    private static readonly nuint TimerId = 1;

    /// <summary>Górna granica czekania na zakończenie wątku w <see cref="Dispose"/>; zawieszony wątek nie może zamrozić wyjścia.</summary>
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(10);

    private readonly IInputTarget _target;
    private readonly ActionQueue _queue;
    private readonly IInputHubErrorSink _errors;
    private readonly TimeProvider _time;
    private readonly InputClock _clock = new();
    private readonly Dictionary<IntPtr, string> _devicePaths = [];
    private readonly ManualResetEventSlim _threadReady = new(initialState: false);
    private readonly Thread _thread;

    // Delegate w polach: GC nie może zebrać callbacków, dopóki hook i okno ich używają.
    private WNDPROC? _windowProc;
    private HOOKPROC? _hookProc;

    private HWND _window;
    private HHOOK _hook;
    private byte[] _rawBuffer = new byte[256];
    private uint _threadId;
    private bool _started;
    private bool _disposed;

    public InputHub(IInputTarget target, ActionQueue queue, IInputHubErrorSink errors, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(queue);
        ArgumentNullException.ThrowIfNull(errors);
        ArgumentNullException.ThrowIfNull(time);

        _target = target;
        _queue = queue;
        _errors = errors;
        _time = time;
        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "input",
        };
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

            RegisterRawInput();
            if (!InstallHook())
            {
                return;
            }

            // Termin startowy celu (koniec kalibracji albo brak long-press) uzbrajamy po hooku: od tej chwili
            // każdy WM_TIMER ma już prawo przyjść, a kalibracja kończy się także bez żadnego wejścia.
            ArmTimer();

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

    /// <summary>Rejestruje klawiaturę i HID Consumer; porażka zostawia hook, więc <c>source</c> spada do <c>keyboard</c>.</summary>
    private void RegisterRawInput()
    {
        ReadOnlySpan<RAWINPUTDEVICE> devices =
        [
            new RAWINPUTDEVICE
            {
                usUsagePage = 0x01,
                usUsage = 0x06,
                dwFlags = RAWINPUTDEVICE_FLAGS.RIDEV_INPUTSINK,
                hwndTarget = _window,
            },
            new RAWINPUTDEVICE
            {
                usUsagePage = 0x0C,
                usUsage = 0x01,
                dwFlags = RAWINPUTDEVICE_FLAGS.RIDEV_INPUTSINK,
                hwndTarget = _window,
            },
        ];

        if (!PInvoke.RegisterRawInputDevices(devices, (uint)sizeof(RAWINPUTDEVICE)))
        {
            Report(InputHubOperations.RawInput, Marshal.GetLastWin32Error());
        }
    }

    private bool InstallHook()
    {
        _hookProc = HookCallback;
        var module = PInvoke.GetModuleHandle(default(PCWSTR));

        _hook = PInvoke.SetWindowsHookEx(WINDOWS_HOOK_ID.WH_KEYBOARD_LL, _hookProc, module, dwThreadId: 0);
        if (_hook.IsNull)
        {
            Report(InputHubOperations.KeyboardHook, Marshal.GetLastWin32Error());
            return false;
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
        else if (message == PInvoke.WM_TIMER && (nuint)wParam == TimerId)
        {
            OnTimer();
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
                    (int)data.scanCode);

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
            PInvoke.KillTimer(_window, TimerId);
            return;
        }

        // Ten sam identyfikator zastępuje poprzedni termin; brak timera zostawiłby `long` bez terminu.
        if (PInvoke.SetTimer(_window, TimerId, elapsed, default) == 0)
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

    /// <summary>Zwalnia hook, timer i okno na wątku, który je utworzył.</summary>
    private void ReleaseNativeResources()
    {
        if (!_hook.IsNull)
        {
            PInvoke.UnhookWindowsHookEx(_hook);
            _hook = default;
        }

        if (_window.IsNull)
        {
            return;
        }

        PInvoke.KillTimer(_window, TimerId);
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
