---
title: 'Tracer bullet: pilot na donglu daje Actions z wątku Input'
type: 'feature'
ticket: '2'
created: '2026-10-22'
status: done
baseline_revision: '18a13048ced675b0b61be6e0e0589068b390303b'
route: 'full'
route_source: 'auto'
risk: 'high'
review: 'thorough'
review_source: 'pinned'
lenses_ran: ['blind-hunter', 'edge-case-hunter', 'verification-gap', 'intent-alignment']
review_loop_iteration: 0
followup_review_recommended: false
context: []
warnings: ['oversized']
deferred:
  - summary: >-
      Kolejność zwalniania adapterów w composition root (hub → pompa → power → timer → pętla) nie ma testu.
    evidence: |-
      Brak projektu testów App i seamu w `CompositionRoot`, więc zamiana kolejności `inputHub?.Dispose()` i
      `actionPump?.Dispose()` nie zapali żadnego testu; skutek to Action w locie bez wpisu w logu przy wyjściu.
      Ten sam dług jest już zapisany w retrospektywie epiki (composition root bez testów).
    location: >-
      src/MediaStation.App/CompositionRoot.cs:151
    severity: low
  - summary: >-
      `InputPolicy` nie resetuje stanu przy (re)rejestracji hooka, bo ten adapter rejestruje hook tylko raz.
    evidence: |-
      Story 1 odroczyła reset `_held`/`_pendingLong` do story, która rejestruje hook; InputHub nie ma ścieżki
      re-rejestracji ani watchdoga, więc reset nie ma wołającego. Skutek: utracony key-up (np. blokada sesji
      w trakcie trzymania klawisza) zamraża VK do końca procesu. Domknięcie wymaga watchdoga hooka.
    location: >-
      src/MediaStation.Core/Input/InputPolicy.cs:111
    severity: low
---

<intent-contract>

## Intent

**Problem:** Polityka wejścia z Core (story 1) nie ma wołającego: nikt nie rejestruje hooka `WH_KEYBOARD_LL`, Raw Input ani wątku Input, więc Home, pilot i klawiatura nie dają żadnej Action, a log nie ma ani jednego wpisu `action`. Bez adaptera nie da się sprawdzić na maszynie, czy połknięcie, korelacja `source` i próg `long` działają na prawdziwych zdarzeniach ani czy zawieszony UI nie zabiera Home.

**Approach:** `Platform/Input/InputHub` na własnym wątku z pętlą komunikatów i oknem message-only: hook LL i Raw Input (`RIDEV_INPUTSINK`, klawiatura i HID Consumer 0x0C) podają zdarzenia do `InputPolicy` w stałym trybie `shell`, callback decyduje bez czekania i wkłada Actions do `ActionQueue` z Core, a termin `long` wraca na wątek Input komunikatem `WM_TIMER`. Osobny wątek opróżnia kolejkę do portu `IActionSink`, a budowanie wpisów (`action`, `inputHubError`) zostaje w Core — App tylko podpina adapter w composition root.

## Boundaries & Constraints

**Always:** połknięcie, `kind` i `source` wyłącznie z `InputPolicy` (AD-8, bez drugiej decyzji w adapterze); callback hooka nie czeka, nie loguje i nie gubi Action (Home); własne `SendInput` z hooka nie jest odfiltrowywane po `LLKHF_INJECTED`; czas polityki przez wstrzyknięty `TimeProvider` (AD-14), a `time` hooka i `WM_INPUT` to tylko `tsMono`/korelacja; Win32 wyłącznie w `MediaStation.App/Platform` przez CsWin32 i `NativeMethods.txt` (AD-3); wpisy logu `action`/`inputHubError` buduje Core; błędy rejestracji nie niosą kodów klawiszy.

**Never:** `--calibrate-input` i logowanie surowych kodów (3), `repeat`, `repeatDelayMs`/`repeatIntervalMs` (4), lokalna kopia routingu i tryby `app`/`osk` (5), `SendKey`/`dwExtraInfo` (7), mysz/żyroskop/`ClipCursor` (8), XInput, zmiany w `Orchestrator`, `CommandRouter`, `MessageTypes.All`, `ContractJson` i `web/**`.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Zmapowany klawisz | Enter (`0x0D`→`ok`) w `shell`, down i up przed progiem | klawisz połknięty, dokładnie jeden wpis `action {ok, press, keyboard}` | brak błędu |
| Long-press | `0x0D` trzymany dłużej niż `timing.longPressMs` | jedno `action {ok, long}`; key-up bez Action | brak błędu |
| Autorepeat | drugi key-down bez key-up | jedno `action`, kolejne key-down bez Action | brak błędu |
| Pilot na donglu | raport HID Consumer 30 ms przed wstrzykniętym VK | `action` z `source: remote` | brak raportu → `keyboard` |
| Klawisz spoza mapy | litera `0x41` | brak wpisu i brak połknięcia | brak błędu |
| Zawinięcie `time` hooka | DWORD `time` przechodzi `0xFFFFFFFF` → 0 | czas monotoniczny rośnie dalej, okno 50 ms działa | brak błędu |
| Zamknięcie pompy | 3 Actions w kolejce, `Dispose` | wszystkie 3 dostarczone do sinka, wątek kończy się bez wiszenia | brak błędu |
| Sink dla sekwencji | Actions + raport błędu | tylko `action {kind, phase, source}` i jeden `inputHubError`; brak `vk`/`scan`/`usage` | `errorCode` pominięty, gdy go nie ma |

</intent-contract>

## Code Map

- `src/MediaStation.Core/Ports/Ports.cs` — dodać `IActionSink` (`void Deliver(ActionMessage action)`, alias `using ActionMessage = MediaStation.Core.Contracts.Action;` jak w `Orchestrator.cs`) z kontraktem „wołany z wątku opróżniającego kolejkę, nie rzuca i nie blokuje” oraz `IInputHubErrorSink` (`void Report(InputHubError error)`). Dwa porty, bo story 5 podmienia tylko sink Actions, a błędy huba zawsze idą do logu.
- `src/MediaStation.Core/Input/InputHubError.cs` **(nowy)** — `sealed record InputHubError(string Operation, int? ErrorCode)` (bez kodów klawiszy z założenia) i `static class InputHubOperations` z `KeyboardHook = "keyboardHook"`, `RawInput = "rawInput"`, `MessageWindow = "messageWindow"`, `HookCallback = "hookCallback"`, `ActionSink = "actionSink"`.
- `src/MediaStation.Core/Input/LogActionSink.cs` **(nowy)** — `public sealed class LogActionSink : IActionSink, IInputHubErrorSink`, `public const string Src = "input"`; `Deliver` → `Info(Src, "action", {["kind"], ["phase"], ["source"]})` w tej kolejności kluczy i bez `tsMono`; `Report` → `Error(Src, "inputHubError", data: {operation[, errorCode]})`, klucz `errorCode` pomijany, gdy `null` (wzór `LogInputRoutingPort`).
- `src/MediaStation.App/Platform/Input/InputClock.cs` **(nowy, bez Win32)** — `internal sealed class InputClock` z `long Normalize(uint timeMs)`: akumuluje wielokrotności `2^32`, gdy czas cofa się o więcej niż `WrapThreshold = 0x8000_0000`, więc 32-bitowe `time` hooka i `GetMessageTime` dają monotoniczny `long` (notatka story 1).
- `src/MediaStation.App/Platform/Input/ActionPump.cs` **(nowy)** — `internal sealed class ActionPump : IDisposable`; ctor `(IActionSink sink, IInputHubErrorSink errors)`; `ActionQueue Queue { get; }` tworzona w ctor z `wake` = sygnał `AutoResetEvent`; wątek `action-pump` (background) opróżnia kolejkę przez `IActionSink.Deliver`, wyjątek sinka raportuje jako `InputHubError(InputHubOperations.ActionSink, null)` i żyje dalej; `Dispose` ustawia stop, budzi, `Join` i domyka resztę kolejki.
- `src/MediaStation.App/Platform/Input/InputHub.cs` **(nowy)** — `internal sealed class InputHub : IDisposable`; ctor `(InputPolicy policy, ActionQueue queue, IInputHubErrorSink errors, TimeProvider time)`; `Start()` startuje wątek `input`; na nim: `GetCurrentThreadId`, `RegisterClassEx`/`CreateWindowEx` z `HWND_MESSAGE`, `RegisterRawInputDevices` (0x01/0x06 i 0x0C/0x01, `RIDEV_INPUTSINK`), `SetWindowsHookEx(WH_KEYBOARD_LL)`, pętla `GetMessage`/`DispatchMessage`; `WndProc` obsługuje `WM_INPUT` (ścieżka urządzenia z `GetRawInputDeviceInfo`/`RIDI_DEVICENAME`, cache `IntPtr → string`, `GetMessageTime()` → `HidConsumerReport`/`RawKeyboardInput`) i `WM_TIMER` (→ `policy.OnTimer(time.GetUtcNow())`); hook: `KBDLLHOOKSTRUCT` → `KeyStroke(vk, transition, LLKHF_INJECTED, clock.Normalize(time))`, `policy.OnKey`, enqueue, `ArmTimer()`, `return decision.Swallow ? 1 : CallNextHookEx`; `ArmTimer` liczony z `policy.NextDeadline` (`SetTimer`/`KillTimer`, `max(1, …)` ms) i re-armowany po wczesnym `WM_TIMER`; `Dispose` → `PostThreadMessage(threadId, WM_QUIT)` + `Join`, `UnhookWindowsHookEx`, `DestroyWindow`.
- `src/MediaStation.App/Platform/System/NativeMethods.txt` — dopisać symbole inputu jako **nazwy enumów** CsWin32 (gołe stałe dają `PInvoke004` przy `TreatWarningsAsErrors`): `WINDOWS_HOOK_ID`, `KBDLLHOOKSTRUCT_FLAGS`, `RAWINPUTDEVICE_FLAGS`, `RID_DEVICE_INFO_TYPE`, `RAW_INPUT_DATA_COMMAND_FLAGS`, `RAW_INPUT_DEVICE_INFO_COMMAND`, `RegisterClassExW`, `CreateWindowExW`, `DefWindowProcW`, `GetMessageW`, `TranslateMessage`, `DispatchMessageW`, `DestroyWindow`, `PostThreadMessageW`, `GetCurrentThreadId`, `SetTimer`, `KillTimer`, `SetWindowsHookExW`, `UnhookWindowsHookEx`, `CallNextHookEx`, `GetMessageTime`, `GetModuleHandleW`, `RegisterRawInputDevices`, `GetRawInputData`, `GetRawInputDeviceInfoW`, `WNDCLASSEXW`, `WNDPROC`, `HOOKPROC`, `TIMERPROC`, `KBDLLHOOKSTRUCT`, `HWND_MESSAGE`, `RAWINPUTDEVICE`, `RAWINPUTHEADER`, `WM_INPUT`, `WM_TIMER`, `WM_KEYDOWN`, `WM_SYSKEYDOWN`, `WM_QUIT`. Jedna lista, bo `AdditionalFiles` wskazuje tylko ten plik.
- `src/MediaStation.App/Platform/Input/{InputTimerDelay,RawInputReport}.cs` **(nowe, bez Win32)** — `InputTimerDelay.Compute(deadline, now)` (termin → opóźnienie `SetTimer`: `null` zabija timer, inaczej ≥ 1 ms, w górę, clamp do `int.MaxValue`) i `RawInputReport.IsValid(bytesRead, headerSize)` (wynik `GetRawInputData` vs `uint.MaxValue` i rozmiar nagłówka) — jedyne arytmetyki adaptera, które da się pokryć testami bez Win32.
- `src/MediaStation.App/CompositionRoot.cs` — po `loop.Start()`, w osobnym `try`, zbudować `LogActionSink`, `ActionPump`, `InputPolicy(config.Input, time, InputMode.Shell)`, `InputHub` i wystartować pumpę oraz hub; porażkę startu logować jako `inputHubStartFailed` (wzór `powerNotificationsStartFailed`) i nie zabijać startu; w `app.Exit` zwalniać najpierw hub, potem pumpę, dalej istniejący łańcuch (nested `try/finally`, `?.`).
- `tests/MediaStation.Core.Tests/LogActionSinkTests.cs` **(nowy)** — sekwencja Actions + `Report` błędu: pojedyncze wpisy `action` z kluczami `kind/phase/source` (kolejność i camelCase przez `JsonLinesLog.Format`), `Info`/`Src` = `input`, brak `tsMono`, brak `vk`/`scan`/`usage` w całej linii, `inputHubError` z `operation` i bez `errorCode` przy `null`.
- `tests/MediaStation.Core.Tests/InputClockTests.cs` **(nowy)** + wpis `<Compile Include="..\..\src\MediaStation.App\Platform\Input\InputClock.cs" …>` w `MediaStation.Core.Tests.csproj` — wzorzec linkowania plików App bez Win32: zawinięcie `0xFFFFFFFF` → `0` daje rosnący `long`, cofnięcie o 1 ms nie dolicza `2^32`.
- `tests/MediaStation.Core.Tests/ActionPumpTests.cs` **(nowy)** + link `Platform/Input/ActionPump.cs` w csproj — fake `IActionSink` (lista + `ManualResetEventSlim`): 3 Actions dostarczone w kolejności, `Dispose` kończy wątek i domyka kolejkę, wyjątek sinka nie zabija wątku (kolejna Action dochodzi), raport `inputHubError` przez fake `IInputHubErrorSink`.
- Reuse: `ActionQueue` (story 1) bez zmian, `InputPolicy` bez zmian, `RecordingLog`, `JsonLinesLog.Format`, `LogInputRoutingPort.Src = "input"`. Nie ruszać: `MessageTypes.All`, `ContractJson`, `web/**`, `Orchestrator`, `TimerScheduler`.

## Tasks & Acceptance

**Execution:**
- [x] `src/MediaStation.Core/Ports/Ports.cs` + `src/MediaStation.Core/Input/{InputHubError,LogActionSink}.cs` — porty sinków i adapter tylko-log z wpisami `action`/`inputHubError` — AD-13 i `verify` ticketu.
- [x] `src/MediaStation.App/Platform/Input/InputClock.cs` + `ActionPump.cs` — monotoniczny czas z 32-bitowego `time` i wątek opróżniający kolejkę do `IActionSink` — kolejka z Core bez blokowania hooka.
- [x] `src/MediaStation.App/Platform/Input/InputHub.cs` + `Platform/System/NativeMethods.txt` — wątek Input z pętlą komunikatów, oknem message-only, hookiem LL, Raw Input `RIDEV_INPUTSINK` i `WM_TIMER` progu `long` — AD-2, AD-8.
- [x] `src/MediaStation.App/CompositionRoot.cs` — podpięcie huba, pompy i sinka oraz kolejność zwalniania na `Exit` — composition root z 2.1.
- [x] `tests/MediaStation.Core.Tests/{LogActionSinkTests,InputClockTests,ActionPumpTests}.cs` + linki w csproj — dowód `verify`, reguły zawinięcia czasu i domknięcia pompy.

**Acceptance Criteria:**
- Given opublikowana aplikacja z `tools/samples/config`, when Enter (`0x0D`→`ok`) zostanie naciśnięty i puszczony przed progiem, then log ma dokładnie jeden wpis `action {"kind":"ok","phase":"press","source":"keyboard"}` i nie ma w nim żadnego kodu VK, scan ani usage.
- Given ta sama aplikacja, when Enter będzie trzymany dłużej niż `timing.longPressMs`, then log ma dokładnie jeden wpis `action {kind: ok, phase: long}` i żadnego `press` dla tego naciśnięcia (termin `long` obsłużony na wątku Input).
- Given `LogActionSink` i sekwencja Actions, when każda Action zostanie dostarczona, then powstaje po jednym wpisie `action` z kluczami `kind/phase/source` (kolejność i camelCase) i bez `tsMono`, `vk`, `scan` i `usage`.
- Given `LogActionSink` i raport `InputHubError("rawInput", 87)`, when wpis powstanie, then log ma `inputHubError {operation: "rawInput", errorCode: 87}`, a raport bez kodu daje `inputHubError` bez klucza `errorCode` i bez kodów klawiszy.
- Given uruchomiona aplikacja, when proces kończy się normalnie, then oba wątki inputu kończą się, a log ma `stop {exitCode: 0}` po ostatnim wpisie `action` i zero `warn`/`error` przy poprawnym configu.
- Given build Release, when `dotnet publish … --self-contained` przechodzi, then nie ma ostrzeżeń, a `NativeMethods.txt` generuje potrzebne API bez ręcznych `DllImport`.

## Implementation Notes

- Zaimplementowano zgodnie z planem: porty `IActionSink`/`IInputHubErrorSink`, `InputHubError` + `InputHubOperations`, `LogActionSink` (Core); `InputClock`, `ActionPump`, `InputHub`, wpisy w `NativeMethods.txt` i podpięcie w `CompositionRoot` (App); `LogActionSinkTests`, `InputClockTests`, `ActionPumpTests` (574 testy, baseline 560).
- `NativeMethods.txt` wymaga **nazw enumów** CsWin32, nie stałych: `WINDOWS_HOOK_ID`, `KBDLLHOOKSTRUCT_FLAGS`, `RAWINPUTDEVICE_FLAGS`, `RID_DEVICE_INFO_TYPE`, `RAW_INPUT_DATA_COMMAND_FLAGS`, `RAW_INPUT_DEVICE_INFO_COMMAND` (gołe `WH_KEYBOARD_LL`/`RIDEV_INPUTSINK` dają `PInvoke004`, a `TreatWarningsAsErrors` łamie build). Dodano też `GetCurrentThreadId` i `HWND_MESSAGE`, których plan nie wymieniał, i usunięto nieużywane `PostQuitMessage`/`RAWKEYBOARD`.
- Zwalnianie Win32 (`UnhookWindowsHookEx`, `KillTimer`, `DestroyWindow`) siedzi w `finally` wątku Input, bo okno można zniszczyć tylko na jego wątku; `Dispose` wysyła `WM_QUIT` przez `PostThreadMessage` i czeka na `Join`. `_started` ustawiane po `Thread.Start()`, więc nieudany start nie zawiesza `Dispose`.
- `CompositionRoot` loguje porażkę startu huba jako `inputHubStartFailed` ze `src: input` (nie `system`), żeby filtr po źródle wejścia widział cały łańcuch.
- **Poprawka po instrumentacji (znaleziony błąd):** `GetRawInputData` zwraca liczbę skopiowanych bajtów, a `pcbSize` na wejściu to rozmiar bufora i API go nie zmniejsza — porównanie wyniku z `copied` dawało `inputHubError {operation: rawInput}` przy **każdym** `WM_INPUT` i gubiło korelację `source`. Teraz wynik jest porównywany z `uint.MaxValue` i `sizeof(RAWINPUTHEADER)`; przebieg po poprawce nie ma już fałszywych błędów (dowód: linie `dbgRead:40/256` z instrumentacji tymczasowej, potem czysty log).
- **Ograniczenie środowiska (nie kodu):** na ALDER `SendInput` z innego procesu **ani z wnętrza aplikacji** nie dociera do hooka `WH_KEYBOARD_LL` — `WM_INPUT` dochodzi, hook milczy. Potwierdzone też na spike'ie projektu (`spike/win/home-logger.ps1`: `RAW-KBD ... vk=0x0D`, zero linii `LL`) i tymczasowym wstrzyknięciem w procesie aplikacji (usunięte). Dlatego wpis `action` z prawdziwego naciśnięcia jest nieosiągalny automatem na tej maszynie; łańcuch polityka→kolejka→pompa→log dowodzą testy złożone, a hook/Raw Input — log `dbgHookInstalled`/parsowanie 40 B z przebiegu instrumentowanego; realne klawisze zostają w odbiorze epiku na UM760.
- Zawinięcie czasu: `InputClock` trzyma się progu `0x8000_0000`, więc krok wstecz o 1 ms nie dolicza `2^32` (pinowane testem).

## Plan Change Log

## Review Triage Log

### 2026-10-22 — Review pass (thorough)
- verdicts: 48 findings — high 0, medium 3, low 44, false 1, maybe-false 0
- findings:
  - `[low]` `[patch]` Blind: `## Verification` (Commands) twierdzi, że przebieg z wstrzykniętym Enterem dał `action press`/`long`, a Implementation Notes i evidence mówią, że wstrzyknięty `SendInput` nie dociera do hooka i log nie ma żadnego `action` — poprawiono bullet Commands na wynik faktycznie zaobserwowany.
  - `[low]` `[patch]` Blind: AC5 wymaga `stop` po ostatnim `action`, ale `stop` jest rejestrowany w `Start` przed handlerem zwalniającym z `StartRun`, więc Actions domknięte przy wyjściu lądują po `stop` — rejestracja `stop` przeniesiona za `StartRun`, żeby łańcuch zwalniania wykonał się pierwszy.
  - `[low]` `[reject]` Blind: brak wpisu o sukcesie rejestracji hooka/Raw Input — intent mówi „jedyny wpis InputHub to `action`”, więc dodatkowy wpis jest poza intencją; brak błędu jest sygnałem sukcesu.
  - `[medium]` `[patch]` Blind: `DevicePath` nie sprawdza wyniku `GetRawInputDeviceInfo` i sonduje rozmiar niepustym wskaźnikiem, więc nazwa urządzenia może nigdy nie powstać (korelacja `source` zawsze `keyboard`) — sonda idzie z `null`, wyniki są sprawdzane, a porażka nazwy daje `inputHubError {rawInput}`; `hDevice == NULL` (wstrzyknięte wejście) zostaje ciche.
  - `[low]` `[reject]` Blind: cache ścieżek urządzeń nigdy nie jest unieważniany — wymaga `RIDEV_DEVNOTIFY` i realnego przepięcia dongla; dzisiejszy config ma jedno urządzenie, więc zjawisko jest nieosiągalne w tym zakresie.
  - `[low]` `[reject]` Blind: `operation` myli timer z `hookCallback` — XML doc `HookCallback` wprost obejmuje timer progu `long`, a `Run`-catch dotyczy ścieżek, które nie rzucają (bool zwracane).
  - `[low]` `[patch]` Blind: `GetMessage` == -1 traktowane jak sukces pętli — dodano sprawdzenie błędu, raport `messageWindow` i wyjście z pętli.
  - `[low]` `[patch]` Blind: `Dispose` czeka bez limitu (`_threadReady.Wait`, `Join`) — dodano ograniczone oczekiwanie i wpis `inputHubError` przy przekroczeniu.
  - `[low]` `[patch]` Blind: wyjątek z error sinka w `ActionPump.Deliver` zabija wątek pompy — raport błędu jest teraz osłonięty, a `_wake` nie jest zwalniane (późny `Set()` jest bezpieczny).
  - `[low]` `[patch]` Blind: kontrakt współbieżności `ActionPump` nieopisany — dopisany do XML doc (jedno `Dispose` z wątku UI, kolejność hub→pompa).
  - `[false]` `[reject]` Blind: `LogActionSink` „nie rzuca”, a nie ma `try/catch` — `ILog` ma kontrakt „implementacja nigdy nie rzuca z powodu zapisu”, a `JsonLinesLog` łapie błędy serializacji; brak wyjątku jest gwarantowany niżej.
  - `[low]` `[reject]` Blind: AC1/AC2 powtarzają wiersze macierzy — kosmetyka szablonu; AC pozostają powierzchnią logu i sądzone są przy przeglądzie.
  - `[low]` `[reject]` Blind: połknięcie nie ma dowodu poza polityką — hook nie jest wołany przez wstrzyknięte wejście na ALDER; realne połknięcie idzie do checków UM760 (residual risks).
  - `[low]` `[patch]` Blind: zadania w Execution wciąż `- [ ]` — odhaczone po zakończeniu implementacji.
  - `[low]` `[reject]` Blind: `## Plan Change Log` pusty mimo poprawki w trakcie implementacji — Change Log jest dla pętli przeglądu (plan nie był zmieniany), a poprawka jest w Implementation Notes.
  - `[low]` `[patch]` Blind: Code Map `NativeMethods.txt` wymienia `PostQuitMessage`/`RAWKEYBOARD`, których nie ma — zaktualizowany do nazw enumów faktycznie użytych.
  - `[low]` `[reject]` Blind: hook robi dwa wywołania jądra na zdarzenie i nie sprawdza `SetTimer` — `SetTimer` jest tani, budżet opóźnienia nie jest przedmiotem ticketu; nieudany `SetTimer` dostał raport (osobny wiersz).
  - `[low]` `[patch]` Blind/Verification gap: `Dispose_delivers_actions_left_in_the_queue` jest wyścigowy i nie dowodzi `Drain` — test zmieniony na deterministyczny (Actions w kolejce przed `Start`, potem `Dispose` → `Drain`).
  - `[low]` `[patch]` Blind: Design Notes czytają się tak, jakby brak hooka zostawiał działający Raw Input — dopisane, że bez hooka nie ma żadnej Action.
  - `[low]` `[reject]` Blind: klasa okna nigdy nie wyrejestrowana; drugi hub w procesie padnie — jeden hub na proces w tym epiku (kalibracja z story 3 zastępuje tryb główny); obejście należy do story, która doda drugi hub.
  - `[low]` `[reject]` Blind: `CompositionRoot` loguje przez `LogActionSink.Src` — jedno źródło `input` dla całego łańcucha wejścia; rozróżnia je `msg`.
  - `[low]` `[reject]` Blind: brak listy checków UM760 — konkretna lista weszła do Auto Run Result (Residual risks), bo to dokument odbioru epiku, nie plan story.
  - `[low]` `[reject]` Blind: macierz nie opisuje braku urządzeń, porażki `SetTimer`/`PostThreadMessage` i nieznanego klawisza przy `pendingLong` — to gałęzie adaptera opisane w Design Notes (degradacja) i w testach `InputPolicy`; macierz zostaje przy scenariuszach intencji.
  - `[low]` `[patch]` Edge: `GetMessage` == -1 traktowane jak sukces pętli migocze/wisi — ten sam patch co wiersz Blind o `GetMessage`.
  - `[low]` `[patch]` Edge: `PostThreadMessage(WM_QUIT)` bez sprawdzenia wyniku i `Join` bez limitu — ten sam patch co wiersz Blind o ograniczonym oczekiwaniu w `Dispose`.
  - `[low]` `[patch]` Edge: `Join` pompy bez limitu przy blokującym sinku — ten sam patch co wiersz Blind o ograniczonym oczekiwaniu (pompa).
  - `[low]` `[patch]` Edge: wyjątek z error sinka w `ActionPump.Deliver` uśmierca pompę — ten sam patch co wiersz Blind o osłoniętym raporcie błędu.
  - `[low]` `[patch]` Edge: `stop` przed `action` domkniętym przy wyjściu — ten sam patch co wiersz Blind o kolejności `stop`.
  - `[low]` `[patch]` Edge: wyjątek z error sinka w catch blokach `InputHub` przekroczyłby granicę natywnego callbacku — `Report` jest osłonięty.
  - `[low]` `[patch]` Edge: `SetTimer` == 0 pozostawia `long` bez terminu po cichu — dodany raport `messageWindow` z kodem.
  - `[low]` `[patch]` Edge: sonda rozmiaru `GetRawInputData` == `uint.MaxValue` wraca po cichu — dodany raport `rawInput` z kodem (ten sam patch co sprawdzanie odczytu).
  - `[low]` `[patch]` Edge: `DevicePath` null porzuca zdarzenie bez wpisu — nazwa urządzenia raportowana jako `rawInput`, a `hDevice == NULL` (wstrzyknięte) zostaje cichy.
  - `[low]` `[patch]` Edge: oba wyniki `GetRawInputDeviceInfo` nie sprawdzane, sonda z niepustym buforem — sonda z `null`, wyniki sprawdzane (ten sam patch co `DevicePath`).
  - `[low]` `[reject]` Edge: cache po uchwycie urządzenia — ten sam powód co wiersz Blind o unieważnianiu cache.
  - `[low]` `[reject]` Edge: klasa okna już zarejestrowana — ten sam powód co wiersz Blind o drugim hubie.
  - `[low]` `[reject]` Edge: wyjątek timera raportowany jako `hookCallback` — udokumentowane w `InputHubOperations.HookCallback`.
  - `[low]` `[patch]` Edge: producent zapisuje po `Dispose` pompy i trafia w zwolnione `_wake` — `_wake` nie jest już zwalniane, a raport błędu jest osłonięty.
  - `[medium]` `[patch]` Verification gap: podłączenie `InputHub` (hook→kolejka, `ArmTimer`→`WM_TIMER`→`OnTimer`) nie ma żadnego testu, więc usunięcie `Enqueue`/`ArmTimer`/gałęzi `WM_TIMER` przechodzi zielono — wydzielono Win32-free `InputTimerDelay` (termin→ms z minimum 1 ms i clampem) z testami oraz dodano test złożony polityka→`ActionQueue`→`ActionPump`→`LogActionSink` dla `press` i `long`.
  - `[medium]` `[patch]` Verification gap: poprawka walidacji `GetRawInputData` nie ma testu i mogłaby wrócić po cichu — wydzielono Win32-free walidator zgłoszenia (wynik vs `uint.MaxValue` i rozmiar nagłówka) z testami na 40/256, `uint.MaxValue` i za krótki odczyt.
  - `[low]` `[defer]` Verification gap: kolejność zwalniania w composition root nie jest testowana (brak projektu testów App, seam bez testu) — istniejący dług z retrospektywy epiki; wpis do `deferred`.
  - `[low]` `[patch]` Verification gap (other): `stop` przed zwolnieniem wejścia — ten sam patch co wiersz Blind o kolejności `stop`.
  - `[low]` `[patch]` Verification gap (other): plan twierdzi przebieg, którego nie było — ten sam patch co wiersz Blind o `## Verification`.
  - `[low]` `[defer]` Verification gap (other): stan `InputPolicy` nie jest resetowany, choć story 1 odroczyła reset do rejestracji hooka w story 2 — ten adapter rejestruje hook raz i nie ma ścieżki re-rejestracji, więc reset nie ma wołającego; wpis do `deferred` z lokalizacją.
  - `[low]` `[patch]` Intent: powierzchnia intencji (wątek Input, hook, `WM_TIMER`) jest wyżej niż testy — ten sam patch co wiersz Verification gap o `InputTimerDelay` i teście złożonym.
  - `[low]` `[reject]` Intent: „test Core” częściowo dotyczy plików App linkowanych do testów — to ustalony wzorzec projektu (brak projektu testów App); test sinka jest w Core i spełnia `verify` literalnie.
  - `[low]` `[patch]` Intent: poprawka Raw Input bez powierzchni regresji — ten sam patch co wiersz Verification gap o walidatorze.
  - `[low]` `[reject]` Intent: logowanie błędów szersze niż intent (`messageWindow`, `hookCallback`, `actionSink`, `inputHubStartFailed`) i niejednolite — dodatkowe operacje opisują ścieżki degradacji wymagane przez ticket („błędy rejestracji hooka i Raw Input”), a hook loguje wyłącznie na ścieżce błędu; niejednolitość sondy rozmiaru jest naprawiona osobnym patchem.
  - `[low]` `[patch]` Intent: artefakt jednocześnie twierdzi i odwołuje AC1/AC2 — ten sam patch co wiersz Blind o `## Verification`.

### 2026-10-22 — Review pass (thorough, follow-up)
- verdicts: 29 findings — high 0, medium 0, low 27, false 2, maybe-false 0
- findings:
  - `[low]` `[reject]` Blind: `InputClock` współdzielony przez hook i `WM_INPUT` — spóźniony `WM_INPUT` tuż przed zawinięciem dolicza jeden stały offset `2^32`; offset jest jednakowy dla obu źródeł, więc po jednym zdarzeniu na ~49,7 dnia uptime korelacja 50 ms działa dalej; poprawka wymaga osobnych zegarów.
  - `[low]` `[reject]` Blind: brak `UnregisterClass` — `carried`: drugi hub w procesie poza zakresem epiku.
  - `[low]` `[reject]` Blind: `Report` w catch hooka robi I/O — `carried`: tylko ścieżka wyjątku, której kod nie produkuje (`Enqueue`/`ArmTimer` nie rzucają); `Report` jest osłonięty.
  - `[low]` `[reject]` Blind: `SetTimer`/`KillTimer` na każdy klawisz — `carried`: tani syscall, budżet opóźnienia poza ticketem.
  - `[low]` `[reject]` Blind: `Dispose` do ~30 s w najgorszym przypadku — każde czekanie jest ograniczone i zostawia wpis w logu; zawieszenie trzech komponentów naraz nie jest osiągalne w normalnym użyciu.
  - `[low]` `[reject]` Blind: błędnie przypisane `operation` — `carried`: `InputHubOperations` dokumentuje użycie; nowa operacja to rozszerzenie bez wołającego.
  - `[low]` `[reject]` Blind: `PostThreadMessage` do zakończonego wątku — `Dispose` działa przy zamknięciu procesu, ponowne użycie identyfikatora wątku w tym oknie jest nierealne, a `WM_QUIT` do wątku bez pętli jest nieszkodliwy.
  - `[low]` `[reject]` Blind: cache `_devicePaths` bez unieważniania — `carried`: wymaga `RIDEV_DEVNOTIFY` i przepięcia dongla.
  - `[low]` `[reject]` Blind: kolejność `stop` zależna od kolejności subskrypcji — `carried`: świadoma decyzja z poprzedniego przeglądu, opisana komentarzem przy `AttachStopLogging`.
  - `[false]` `[reject]` Blind: termin timera na zegarze ściennym, `time` na monotonicznym — polityka liczy terminy wyłącznie na `TimeProvider` (AD-14), a `tsMono` służy tylko korelacji; adapter nie porównuje obu zegarów.
  - `[low]` `[reject]` Blind: `InputHub` bez automatu — `carried`: ograniczenie ALDER (wstrzyknięty klawisz nie dociera do hooka) opisane w Implementation Notes.
  - `[low]` `[reject]` Blind: `HeaderSize = 24` w `RawInputReportTests` — App jest wyłącznie `win-x64`, test pinuje wartość produkcyjną na tej platformie.
  - `[low]` `[reject]` Blind: zduplikowane `Src` i wzajemny import `Ports`↔`Input` — jeden assembly, brak realnego cyklu; jedno źródło `input` dla łańcucha wejścia to decyzja z poprzedniego przeglądu.
  - `[low]` `[reject]` Blind: nakładające się testy i brakujące krawędzie — kosmetyka; kluczowe krawędzie (`Dispose` z kolejką, wyjątek sinka) mają testy.
  - `[low]` `[reject]` Edge: brak negatywnego cache przy porażce `DevicePath` — porażka odczytu ścieżki realnego urządzenia jest nieosiągalna na tej konfiguracji (jedno urządzenie); poprawka dodaje stan.
  - `[low]` `[reject]` Edge: `PostThreadMessage` do martwego wątku — ten sam powód co wiersz Blind o `PostThreadMessage`.
  - `[low]` `[reject]` Edge: wyjątek w `Enqueue`/`ArmTimer` po decyzji `Swallow` przepuszcza klawisz — `ActionQueue.Enqueue` to kolejka + budzik bez rzucania, a `ArmTimer` używa tylko wywołań Win32 zwracających kod; ścieżka nieosiągalna.
  - `[low]` `[reject]` Edge: cache bez `WM_INPUT_DEVICE_CHANGE` — `carried`.
  - `[false]` `[reject]` Edge: odczyt krótszy niż `header.dwSize` przyjmowany jako pełny — bufor jest dobierany sondą do pełnego `size`, a `GetRawInputData` kopiuje cały raport albo zwraca `uint.MaxValue`; skrócony odczyt przy poprawnym buforze nie występuje.
  - `[low]` `[reject]` Edge: Actions po `Drain` przy przekroczeniu limitu `InputHub.Dispose` — dotyczy tylko zawieszonego wątku Input (10 s); wpis `inputHubError` zostaje, a poprawka wymaga flagi zamknięcia w `ActionQueue` z Core.
  - `[low]` `[reject]` Edge: brak wpisu `stop`, gdy `StartRun` rzuci — wyjątek ląduje w `unhandledException`, proces kończy się awarią; dawne zachowanie nie było kontraktem ani testowane.
  - `[low]` `[reject]` Edge: `Shutdown(ShellStartFailed)` przed `AttachStopLogging` — start WebView2 jest asynchroniczny po powrocie z `Start`; nie udowodniono ścieżki synchronicznej.
  - `[low]` `[reject]` Edge: klasa okna już zarejestrowana — `carried`.
  - `[low]` `[reject]` Edge: plan twierdzi „nie loguje” w hooku, a catch woła `Report` — `carried`; wywołanie jest osłonięte i dotyczy tylko ścieżki wyjątku.
  - `[low]` `[reject]` Verification gap: kolejność zwalniania i `stop` w composition root bez testu — `carried`: ten sam dług co istniejący wpis `deferred` (brak projektu testów App).
  - `[low]` `[reject]` Verification gap: glue `InputHub` (mapowanie `Swallow`, re-arm) bez testu — `carried`: arytmetyka timera i walidacja Raw Input są wydzielone i przetestowane; reszta wymaga żywego hooka (ograniczenie ALDER).
  - `[low]` `[reject]` Verification gap (other): gałąź limitu `ActionPump.Dispose` bez testu — limit jest `static readonly`, test kosztowałby 10 s; poprawka dodaje wstrzykiwany parametr.
  - `[low]` `[reject]` Verification gap (other): `MessageWindow` jako `operation` przy limicie `Dispose` — nazewnictwo, nie luka weryfikacji; `carried` jak wiersz o błędnych `operation`.
  - `[low]` `[reject]` Intent: testy leżą na powierzchni polityka→sink, a oczekiwanie intencji na powierzchni hooka i Raw Input — `carried`: ograniczenie środowiska opisane w Implementation Notes i Verification; zgodnie z AGENTS.md realne klawisze to odbiór epiku na UM760, nie blokada ticketu.

## Design Notes

Dwa zegary rozdzielone jak w story 1: `time` hooka (`KBDLLHOOKSTRUCT.time`) i `GetMessageTime()` są w tej samej jednostce (ms od startu systemu), więc korelacja 50 ms działa bez przeliczania, a `InputClock` tylko zdejmuje 32-bitowe zawinięcie. Próg `long` liczony jest na `TimeProvider` (`NextDeadline`), więc `SetTimer` dostaje opóźnienie względem `time.GetUtcNow()`, a spóźniony lub wczesny `WM_TIMER` nic nie psuje: `OnTimer` sam sprawdza termin, a hub re-armuje timer, gdy termin został. Adapter nie decyduje o niczym — `Swallow` i Actions pochodzą wprost z `InputDecision`, więc zawieszony UI czy brak okna na wierzchu nie zmieniają decyzji.

Porażki rejestracji Win32 degradują, nie zabijają: brak okna message-only kończy wątek Input, brak Raw Input zostawia hook (tylko `source` spada do `keyboard`), brak hooka kończy wątek po wpisie `inputHubError`. Bez hooka nie ma jednak **żadnej** Action — Raw Input tylko koryguje `source`, a nie tworzy Actions. Każda porażka to jeden wpis `Error` z `operation`, bez kodów klawiszy.

## Verification

**Commands:**
- `dotnet test MediaStation.slnx -c Release` — zielone, w tym `LogActionSinkTests`, `InputClockTests`, `ActionPumpTests`, `InputTimerDelayTests`, `RawInputReportTests` oraz istniejące wiersze macierzy w `InputPolicyTests`, zero ostrzeżeń.
- `dotnet build MediaStation.slnx -c Release` — 0 ostrzeżeń; schematy bez zmian (`action` już zarejestrowany, nowe typy nie są kontraktami).
- `dotnet build src/MediaStation.App -c Debug` — 0 ostrzeżeń (kontrakty i typecheck web zielone).
- `dotnet publish src/MediaStation.App -c Release -r win-x64 --self-contained true` — sukces; uruchomienie z `tools/samples/config` daje `start` → `snapshotPublished` → `stop {exitCode: 0}`, zero `warn`/`error` i czyste zamknięcie obu wątków inputu. Na ALDER wstrzyknięty klawisz nie dociera do hooka LL, więc wpisu `action` z prawdziwego naciśnięcia ten przebieg nie daje (szczegóły w evidence poniżej); łańcuch polityka→kolejka→pompa→log pokrywają testy `LogActionSinkTests` i `ActionPumpTests`, a arytmetykę terminu `long` i walidację Raw Input — `InputTimerDelayTests` i `RawInputReportTests`.

**Manual checks (if no CLI):**
- Log po wstrzyknięciu klawisza: brak pól `vk`, `scan`, `usage` w liniach `action`; `src` = `input`.

**Current-machine evidence (2026-10-22, baseline `18a1304` + working tree, ALDER / B760 Pro RS/D4 / Windows 11 Home):**
- `dotnet test MediaStation.slnx -c Release` — 583/583 passed, 0 failed (baseline 560; +6 `LogActionSinkTests`, +4 `InputClockTests`, +5 `ActionPumpTests`, +5 `InputTimerDelayTests`, +3 `RawInputReportTests`). Wiersze macierzy przechodzą przez istniejące `InputPolicyTests` (m.in. `Autorepeat_is_swallowed_and_gives_no_action`, `Unknown_vk_is_not_swallowed_and_gives_no_action`, `Injected_home_after_consumer_report_30_ms_earlier_is_remote`) oraz nowe testy: `Mapped_enter_before/past_the_long_threshold_*` (polityka → sink) i `Policy_decisions_from_the_queue_produce_one_action_entry_for_short_and_long_enter` (polityka → `ActionQueue` → pompa → sink, `press` i `long`).
- `dotnet build MediaStation.slnx -c Release` i `dotnet build src/MediaStation.App -c Debug` — 0 ostrzeżeń, 0 błędów; schematy i TS bez zmian (`action` już zarejestrowany, nowe typy nie są kontraktami).
- `dotnet publish src/MediaStation.App -c Release -r win-x64 --self-contained true` + uruchomienie z `tools/samples/config` (config użytkownika podmieniony i przywrócony, porównany bajtowo): proces żył 10 s (pid 51740), log ma `start`, `inputRouting`, `powerSignalsRegistered`, `snapshotPublished` i `stop {exitCode: 0}` jako ostatni wpis, zero `warn`/`error`, a zamknięcie okna zakończyło oba wątki inputu bez zawieszenia (poprawka przeglądu przeniosła `stop` za teardown wejścia).
- Przebieg instrumentowany (tymczasowa lokalna instrumentacja, usunięta przed oddaniem): hook `WH_KEYBOARD_LL` zainstalowany na wątku Input (`dbgHookInstalled:null=False`, tid 43212), `WM_INPUT` parsowany poprawnie (`dbgSize: 40`, `dbgRead: 40`), a okno message-only i timer żyją w pętli komunikatów.
- Ograniczenie: wstrzyknięty `SendInput` (z innego procesu i z wnętrza aplikacji) nie dociera do hooka LL na tej maszynie — dowód z tego samego przebiegu (`WM_INPUT` bez żadnego wywołania hooka) i ze spike'a projektu (`spike/win/home-logger.ps1`: linie `RAW-KBD vk=0x0D` i zero `LL`). Zgodnie z regułą projektu realne naciśnięcie pilota/klawiatury jest checkiem na UM760 w odbiorze epiku, a nie powodem blokady ticketu.

## Auto Run Result

**Status:** built (follow-up review pass, thorough).

**Summary:** InputHub na wątku Input (okno message-only, `WH_KEYBOARD_LL`, Raw Input `RIDEV_INPUTSINK`, `WM_TIMER` progu `long`), `ActionPump` opróżniający `ActionQueue` do `IActionSink`, `LogActionSink` w Core (wpisy `action` i `inputHubError` bez kodów klawiszy), `InputClock`/`InputTimerDelay`/`RawInputReport` bez Win32 i podpięcie w `CompositionRoot`.

**Files changed:** porty i `LogActionSink`/`InputHubError` (Core); `InputHub`, `ActionPump`, `InputClock`, `InputTimerDelay`, `RawInputReport`, `NativeMethods.txt`, `CompositionRoot` (App); testy `LogActionSinkTests`, `InputClockTests`, `ActionPumpTests`, `InputTimerDelayTests`, `RawInputReportTests` (+ linki w csproj).

**Review findings (follow-up pass):** 29 findings (low 27, false 2). Patches applied: 0. Deferred: 0 nowych (istniejące wpisy `deferred` bez zmian). Rejected: wszystkie 29, każdy z powodem w `## Review Triage Log` (większość to `carried` z pierwszego przeglądu albo ścieżki nieosiągalne w tym zakresie).

**Follow-up review recommended:** false — ten przebieg nie zmienił kodu; patched high 0, medium 0.

**Verification:** `dotnet test MediaStation.slnx -c Release` — 583/583 passed, 0 failed, na kodzie bez zmian względem poprzedniego przebiegu. Build Release/Debug, publish self-contained i uruchomienie z `tools/samples/config` mają wyniki w `## Verification` (Current-machine evidence).

**Residual risks / checks for UM760 (whole-epic acceptance):** realne naciśnięcie Enter/Home na klawiaturze i pilocie daje dokładnie jeden `action` (na ALDER hook LL nie dostaje wstrzykniętych klawiszy); `long` po `timing.longPressMs`; `source: remote` dla raportu HID Consumer przed VK; połknięcie zmapowanych klawiszy w `shell`; Home działa przy zawieszonym UI; `stop` po ostatnim `action` przy zamknięciu.
