---
title: 'Pointer: hook myszy i ClipCursor w osk'
type: 'feature'
ticket: '8'
created: '2026-10-22'
status: done
baseline_revision: '0aa88e7e235acb4d1be04142e6d907a6ae7577c9'
route: 'full'
route_source: 'auto'
risk: 'medium'
review: 'quick'
review_source: 'pinned'
lenses_ran: ['quick']
review_loop_iteration: 0
followup_review_recommended: false
context: []
warnings: ['oversized']
deferred:
  - summary: >-
      Przestrzeń współrzędnych clipu (`GetWindowRect`, piksele fizyczne) i zdarzeń hooka (`MSLLHOOKSTRUCT.pt`) może
      się rozejść, gdy aplikacja nie jest per-monitor DPI aware, a okno OSK stoi na monitorze o innym skalowaniu niż
      systemowe — wtedy clip i `Contains` liczyłyby w innych jednostkach.
    evidence: |-
      Plan (Implementation Notes) twierdzi „WPF w .NET jest per-monitor DPI aware”, ale `src/MediaStation.App/MediaStation.App.csproj`
      nie deklaruje żadnego manifestu DPI, a domyślny WPF raportuje `SYSTEM_AWARE`. ALDER pracuje przy 200% i ma dwa
      monitory (1920×1080 oraz 1080×1920), więc na monitorze o skali innej niż systemowa prostokąt clipu i punkt hooka
      mogą być w różnych przestrzeniach. Rozstrzyga pomiar `GetProcessDpiAwareness` na opublikowanym exe oraz przebieg
      z OSK na monitorze o innej skali (porównanie prostokąta z `GetWindowRect` z punktem z hooka) — do odbioru epiku,
      razem z epic-osk, które pierwsze wyśle `clipHwnd`.
    location: >-
      src/MediaStation.App/Platform/Input/InputHub.cs:570-601
    severity: medium (unverified)
  - summary: >-
      AD-8 nadal opisuje `SetInputRouting{ mode, appType, pointer, epoch }`, choć kontrakt ma teraz opcjonalne
      `clipHwnd`; dokumentacja architektury jest nieaktualna do czasu epic-osk.
    evidence: |-
      `architecture-mediastation.md:194` nie wymienia `clipHwnd`, a `set-input-routing.schema.json` i testy kontraktu
      pinują klucz `clipHwnd` w każdej kopercie. Epika już to przewidziała: `epic-input.md:61` każe dopisać `clipHwnd`
      do AD-8 (`bmad-architecture`) przed epic-osk, więc luka jest śledzona, ale nie ma wykonawcy w tym przebiegu.
    location: >-
      _bmad-output/initiative-mediastation-mvp/architecture-mediastation/architecture-mediastation.md:194
    severity: low
---

<intent-contract>

## Intent

**Problem:** InputHub nie ma hooka myszy, więc polityka nie zna zdarzeń wskaźnika i `pointer` z lokalnej kopii routingu nie ma żadnego skutku: w `shell` i `app` + `web` mysz i żyroskop sterują pulpitem, a w `osk` kursor wychodzi poza klawiaturę i klika w aplikację pod nią.

**Approach:** Wątek Input dostaje hook `WH_MOUSE_LL` na tym samym oknie message-only; `InputPolicy` dostaje `OnPointer`, który według `pointer` z kopii routingu połyka ruch, kliknięcia i kółko (`blocked`) albo je przepuszcza (`allowed`). `SetInputRouting` dostaje ostatni opcjonalny `long? ClipHwnd = null`, a InputHub przy `osk` z `clipHwnd` ogranicza kursor do prostokąta okna (`ClipCursor`), połyka kliknięcia poza nim i zwalnia ograniczenie po każdej zmianie wiersza routingu oraz w `Dispose`.

## Boundaries & Constraints

**Always:** decyzja z jednego odczytu `InputRoutingState.Current` na zdarzenie; `blocked` połyka ruch, kliknięcia i kółko; `allowed` przepuszcza wszystko poza kliknięciami poza prostokątem clipu w `osk`; zdarzenie z `InjectionMarker` (własne `SendInput`) zawsze przepuszczone bez połknięcia; wskaźnik nigdy nie daje Actions; clip wyłącznie przy `Mode == Osk` i niezerowym `ClipHwnd`; zwolnienie clipu przy każdej zmianie wiersza, przy porażce `GetWindowRect`/`ClipCursor` i w `Dispose`; callback hooka tylko woła politykę i wraca (AD-8: `LowLevelHooksTimeout`); błędy jako `inputHubError {operation}` bez kodów wejścia (AD-13); Win32 wyłącznie przez CsWin32 i `NativeMethods.txt` (AD-3); czas przez `TimeProvider` (AD-14).

**Never:** Actions z myszy; ukrywanie kursora w kodzie (CSS shella i PageRuntime); wysyłanie `clipHwnd` z Orchestratora (epic-osk); zmiany `ContractJson`, `MessageTypes.All`, `Orchestrator.cs` i zachowania `InputCalibration` poza no-op `OnPointer`; zmiany polityki klawiatury, pada, Raw Input, timera i repeat; `[DllImport]`; nowe klucze w logu `inputRouting` (kształt z 5.8 pinuje test); hook myszy blokujący albo rzucający wyjątek do systemu.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| `shell` / `app` + `web` | ruch, klik, kółko, `pointer: blocked` | połknięte, zero Actions | brak błędu |
| `app` + `moonlight`/`native` | to samo, `pointer: allowed` | przepuszczone, zero Actions | brak błędu |
| `osk` + `clipHwnd` | klik wewnątrz prostokąta | przepuszczony | brak błędu |
| `osk` + `clipHwnd` | klik poza prostokątem | połknięty, zero Actions | brak błędu |
| `osk` + `clipHwnd` | ruch albo kółko | przepuszczone (kursor uwięziony clipem) | brak błędu |
| każdy wiersz | `dwExtraInfo` = znacznik MediaStation | przepuszczone bez połknięcia | brak błędu |
| `osk` + `clipHwnd` → `shell` | `SetInputRouting` z większym `epoch` | `PointerClip.WindowOf` = `null`, hub woła `ClipCursor(null)`, kliki przepuszczone | brak błędu |
| `osk` + `clipHwnd`, okno już nie istnieje | porażka `GetWindowRect` | clip zwolniony, kliki przepuszczone, jedno `inputHubError {operation: clipCursor}` | brak wyjątku |
| rejestracja hooka myszy | porażka `SetWindowsHookEx` | wątek żyje, klawiatura działa, `inputHubError {operation: mouseHook}`, zero połknięć myszy | degradacja |

</intent-contract>

## Code Map

- `src/MediaStation.Core/Input/PointerStroke.cs` — nowy: `PointerMessage { Move, ButtonDown, ButtonUp, Wheel }`, `PointerButton { Left, Right, Middle, XButton1, XButton2, None }`, `record PointerStroke(PointerMessage Message, PointerButton Button, int X, int Y, int WheelDelta, bool Injected, long TimeMs, long ExtraInfo = 0)` i `record ScreenRectangle(int Left, int Top, int Right, int Bottom)` z `Contains(x, y)` (prawa i dolna krawędź wyłączna).
- `src/MediaStation.Core/Input/InputRoutingState.cs:12,23,52` — `InputRouting` dostaje ostatni `long? ClipHwnd = null` (stare `new InputRouting(a,b,c,d)` kompilują się), `Apply` kopiuje `command.ClipHwnd`, nowy `event Action<InputRouting>? Changed` wołany po przyjętym `epoch` i poza `lock`; nowy `static class PointerClip` z `long? WindowOf(InputRouting routing)` = `ClipHwnd` tylko dla `Mode == Osk` i niezerowego uchwytu.
- `src/MediaStation.Core/Contracts/SetInputRouting.cs:32` — `SetInputRouting(..., long Epoch, long? ClipHwnd = null)`; bez `JsonIgnore`, więc `clipHwnd` jedzie jawnie jako `null`, spójnie z `appType`.
- `src/MediaStation.Core/Input/InputTarget.cs:12-36` — nowy `InputDecision OnPointer(PointerStroke pointer, ScreenRectangle? clip)`.
- `src/MediaStation.Core/Input/InputPolicy.cs:174-254,514-534` — `OnPointer`: znacznik → `PassDecision`; jedno `_routing.Current`; `blocked` → `new InputDecision(true, NoActions)`; `allowed` + `Osk` + `clip` + klik poza → połknięcie; inaczej `PassDecision`. Zero Actions, zero stanu trzymania.
- `src/MediaStation.Core/Input/InputCalibration.cs:104-147` — `OnPointer` = `PassDecision` (kalibracja nie połyka i nie klipuje).
- `src/MediaStation.Core/Input/InputHubError.cs:13-32` — `MouseHook = "mouseHook"`, `ClipCursor = "clipCursor"`.
- `src/MediaStation.App/Platform/Input/InputHub.cs` — ctor `(..., TimeProvider time, InputRoutingState routing)` wymagany (wzorzec 5.8); `InstallHook` (255-268) instaluje też `WH_MOUSE_LL` z delegatem w polu (jak `_hookProc`, 60-62), a jego porażka tylko raportuje i degraduje; `MouseHookCallback` (wzór 396-428) czyta `MSLLHOOKSTRUCT`, mapuje `WM_*` na `PointerStroke`, robi `SyncClip()`, woła `_target.OnPointer(stroke, _clipRect)`, `Enqueue` i `CallNextHookEx`; `SyncClip()` na wątku Input: `PointerClip.WindowOf` → `GetWindowRect` + `ClipCursor(in rect)` albo `ClipCursor(null)`, cache `_clipRect` i `_clipEpoch`; `WindowProc` (294-310) obsługuje `RoutingChangedMessage` (`WM_APP + 1`), a `Changed` (wątek Orchestratora) robi nieblokujące `PostMessage` na `_window`; `ReleaseNativeResources` (687-704) zwalnia clip i odhacza hook myszy.
- `src/MediaStation.App/Platform/System/NativeMethods.txt` — dopisać `MSLLHOOKSTRUCT`, `POINT`, `RECT`, `ClipCursor`, `GetWindowRect`, `PostMessageW`, `WM_MOUSEMOVE`, `WM_LBUTTONDOWN/UP`, `WM_RBUTTONDOWN/UP`, `WM_MBUTTONDOWN/UP`, `WM_XBUTTONDOWN/UP`, `WM_MOUSEWHEEL`, `WM_MOUSEHWHEEL` (nazwy typów, nie stałych — PInvoke004; `WINDOWS_HOOK_ID` już jest).
- `src/MediaStation.App/CompositionRoot.cs:303-305,141` — hub dostaje współdzielony `routing` w trybie głównym, a kalibracja świeży `new InputRoutingState()`.
- `tests/MediaStation.Core.Tests/{PointerPolicyTests,InputRoutingStateTests,InputHubRoutingPortTests,CoreMessageContractTests,InputCalibrationTests}.cs` — macierz, `Changed`, `ClipHwnd` w kopii, kontrakt i schemat, no-op kalibracji; `CoreMessageContractTests.cs:25-50,544-563` wymagają aktualizacji dokładnego JSON-a i `required`.
- Reuse: `InjectionMarker`, `RecordingLog`, `FakeTimeProvider`, `MediaStation.Core.Tests.csproj:26-43` (link tylko typów bez Win32 — `InputHub.cs` zostaje poza testami). Nie ruszać: `InputTimerDelay`, `ActionPump`, `RawInputBody`, `Orchestrator.cs`, `web/**`.

## Tasks & Acceptance

**Execution:**
- [x] `src/MediaStation.Core/Input/PointerStroke.cs` — typy zdarzenia wskaźnika i prostokąta ekranu — czysta decyzja w Core (AD-8).
- [x] `src/MediaStation.Core/Contracts/SetInputRouting.cs` — opcjonalny `ClipHwnd` na końcu — kontrakt bez łamania epiki 2.
- [x] `src/MediaStation.Core/Input/InputRoutingState.cs` — `ClipHwnd` w kopii, `Changed`, `PointerClip.WindowOf` — zwolnienie clipu po zmianie trybu.
- [x] `src/MediaStation.Core/Input/{InputTarget,InputPolicy,InputCalibration,InputHubError}.cs` — `OnPointer` w kontrakcie, reguła `pointer` i klik poza clipem, no-op kalibracji, nazwy operacji — decyzja o połknięciu.
- [x] `src/MediaStation.App/Platform/Input/InputHub.cs` i `Platform/System/NativeMethods.txt` — hook `WH_MOUSE_LL`, `ClipCursor`, powiadomienie o routingu, zwolnienia — egzekucja Win32.
- [x] `src/MediaStation.App/CompositionRoot.cs` — wspólny stan routingu w hubie — wpięcie bez `null`.
- [x] `tests/MediaStation.Core.Tests/*` — macierz I/O, `Changed`, kontrakt i schemat, kalibracja, adapter — `verify`.

**Acceptance Criteria:**
- Given kopia `shell` z `pointer: blocked`, when ruch, klik i kółko, then każde zdarzenie ma `Swallow=true` i zero Actions; po `Apply` na `osk` + `allowed` te same zdarzenia mają `Swallow=false`.
- Given kopia `osk` z `clipHwnd` i prostokąt `0,0,100,100`, when klik przy `(99,99)` i przy `(100,50)`, then pierwszy jest przepuszczony, drugi połknięty.
- Given kopia `osk` z `clipHwnd` i klik połknięty, when `SetInputRouting{shell, epoch+1}`, then `PointerClip.WindowOf` = `null` (hub zwalnia `ClipCursor`) i ten sam klik jest przepuszczony.
- Given `SetInputRouting` bez `clipHwnd`, when serializacja i deserializacja, then `clipHwnd` jest `null`, a koperta w starym kształcie (bez klucza) deserializuje się z `ClipHwnd = null`; log `inputRouting` zostaje bez nowego klucza.
- Given `MediaStation.exe` z `tools/samples/config`, when start i stop, then log ma `start` → `inputRouting` → `powerSignalsRegistered` → `snapshotPublished` → `stop`, zero `inputHubError` (także `mouseHook` i `clipCursor`), a po zamknięciu kursor i klikanie działają.

## Implementation Notes

**Stan:** zaimplementowane; poniżej decyzje, pliki i dowody z tego przebiegu (ALDER, Windows 11 Home).

**Decyzje:**
- `InputRouting` dostał `long? ClipHwnd = null` na końcu, więc wszystkie dotychczasowe `new InputRouting(a,b,c,d)` kompilują się bez zmian; `Apply` kopiuje `ClipHwnd`, a `Changed` jest wołany po przyjętym `epoch` i poza `lock` (zapis routingu nie może czekać na InputHub).
- `PointerClip.WindowOf` jest jedyną regułą „kiedy clip obowiązuje” (tylko `osk` i niezerowy uchwyt); dzięki temu zwolnienie po przejściu na `shell`/`app` jest decyzją w Core, a nie warunkiem w hubie.
- Decyzja o połknięciu jest w `InputPolicy.OnPointer`: `pointer: blocked` połyka ruch, kliknięcia i kółko, `allowed` połyka wyłącznie klik poza prostokątem clipu w `osk`, a zdarzenie ze znacznikiem MediaStation zawsze przechodzi. Wskaźnik nigdy nie daje Actions, więc nie dotyka `_held`, timerów ani repeat.
- `IInputTarget` dostał `OnPointer`, więc `InputCalibration` ma no-op `PassDecision`: kalibracja nie połyka, nie klipuje i nie loguje wskaźnika.
- `ClipCursorGate` (App, bez Win32, linkowany do testów jak `InputTimerDelay`) trzyma stan clipu i woła szwy `measure`/`apply`; `InputHub` dostarcza tylko `GetWindowRect`/`ClipCursor` i raport `inputHubError {operation: clipCursor}`. Dzięki temu zwolnienie clipu po zmianie trybu i porażka pomiaru/clipu są pokryte testami.
- Powiadomienie o routingu: `Changed` z wątku Orchestratora robi tylko `PostMessage` (`WM_APP + 1`) na okno Input; `SyncClip()` porównuje też `epoch` przy zdarzeniu myszy, więc zgubiony komunikat nie zostawia starego clipu.
- Porażka `SetWindowsHookEx(WH_MOUSE_LL)` degraduje (jak Raw Input): wpis `inputHubError {operation: mouseHook}`, klawiatura, pad i timer działają dalej, wątek nie staje.
- Log `inputRouting` zostaje bez nowego klucza (test z 5.8 pinuje kształt), a `SetInputRouting` serializuje `clipHwnd` jawnie jako `null`, spójnie z `appType`.

**Pliki:**
- Nowe: `src/MediaStation.Core/Input/PointerStroke.cs`, `src/MediaStation.App/Platform/Input/ClipCursorGate.cs`, `tests/MediaStation.Core.Tests/{PointerPolicyTests,ClipCursorGateTests}.cs`.
- Zmienione: `src/MediaStation.Core/Contracts/SetInputRouting.cs`, `src/MediaStation.Core/Input/{InputRoutingState,InputPolicy,InputTarget,InputCalibration,InputHubError}.cs`, `src/MediaStation.App/Platform/Input/InputHub.cs`, `src/MediaStation.App/Platform/System/NativeMethods.txt`, `src/MediaStation.App/CompositionRoot.cs`, `tests/MediaStation.Core.Tests/{CoreMessageContractTests,InputRoutingStateTests,InputHubRoutingPortTests,InputCalibrationTests,LogActionSinkTests}.cs` i `MediaStation.Core.Tests.csproj` (link `ClipCursorGate`).

**Zaskoczenia / pułapki:**
- `POINT` nie może być w `NativeMethods.txt` (CsWin32 `PInvoke003` każe użyć `System.Drawing.Point`), więc pozycja jest brana z `MSLLHOOKSTRUCT.pt`; `LLMHF_INJECTED` trzeba było dopisać ręcznie.
- Prostokąt z `GetWindowRect` jest w pikselach fizycznych tak jak `pt` z hooka (WPF w .NET jest per-monitor DPI aware); przy skalowaniu innym niż 100% warto to potwierdzić okiem — nie ma tego w automatach.
- `ClipCursor` jest wołany tylko przy zmianie `epoch`, więc przesunięcie okna OSK bez zmiany wiersza routingu nie przelicza prostokąta; okno OSK jest nieruchome w swoim trybie, a epic-osk może wysłać nowy `epoch`, jeśli to zmieni.

**Dowody (ten przebieg, ALDER, Windows 11 Home):**
- `dotnet build MediaStation.slnx -c Release` — Build succeeded, 0 ostrzeżeń, 0 błędów.
- `dotnet test MediaStation.slnx -c Release` — 828/828 passed, 0 failed, 0 skipped (baseline tego worktree 782, nie 711 z planu; +46 przypadków: macierz wskaźnika, `Changed`, `ClipHwnd`, bramka clipu, kontrakt/schemat, kalibracja, wpisy `mouseHook`/`clipCursor`).
- `dotnet run --project tools/MediaStation.SchemaExport -c Release` — `set-input-routing.schema.json` ma `clipHwnd: {type: [integer, null], default: null}` poza `required`; `git status` czysty (generowane katalogi są w `.gitignore`).
- Build rozwiązania uruchamia `BuildWeb` — `web/contracts/generated/set-input-routing.ts` ma `clipHwnd?: number | null`, `tsc` i Vite zielone.
- `dotnet publish src/MediaStation.App -c Release -r win-x64 --self-contained true` + `MediaStation.exe` z `tools/samples/config` (10 s, `CloseMainWindow`): kod 0, dokładnie pięć wpisów `start` → `inputRouting {cmdId: 1, mode: "shell", pointer: "blocked", epoch: 1}` → `powerSignalsRegistered` → `snapshotPublished {rev: 1}` → `stop {exitCode: 0}`; zero `inputHubError`, także `mouseHook` i `clipCursor`. Config użytkownika odtworzony bajt w bajt (SHA-256 `apps.json 325b57c9…`, `input.json 4f832612…`, `settings.json b5ddf4be…` przed i po identyczne), `config/invalid` na miejscu.

**Audyt macierzy I/O:** wiersze `shell`/`app` + `web` → `Blocked_row_swallows_movement_clicks_and_wheel_without_actions` i `Blocked_row_in_app_web_swallows_the_whole_pointer_matrix`; `app` + `moonlight`/`native` → `Allowed_app_row_passes_the_whole_pointer_matrix`; klik w prostokącie → `Click_inside_the_clip_rectangle_is_passed`; klik poza → `Click_outside_the_clip_rectangle_is_swallowed`; ruch i kółko w `osk` → `Movement_and_wheel_are_passed_even_outside_the_clip_rectangle`; znacznik → `MediaStation_injected_pointer_event_is_passed_even_in_a_blocked_row` i `Foreign_injection_without_the_marker_is_still_swallowed_in_a_blocked_row`; `osk` + `clipHwnd` → `shell` → `Switching_from_osk_clip_to_shell_releases_the_clip_rule` z `Shell_row_releases_the_previous_clip_once`; porażka `GetWindowRect` → `Measure_failure_releases_the_clip_and_reports_failure`; wpisy `clipCursor`/`mouseHook` → `Pointer_failure_logs_one_entry_with_only_the_operation_and_code`. Wszystkie powyższe przebiegły w `dotnet test` powyżej.

**Luki pokrycia (świadome):** samo wywołanie `SetWindowsHookEx(WH_MOUSE_LL)` i `GetWindowRect` jest Win32 i `InputHub.cs` celowo nie wchodzi do projektu testów (granica epiki), więc wiersza „rejestracja hooka myszy” nie da się zasymulować automatem: jego obserwowalny kontrakt (`inputHubError {operation: mouseHook}`, brak kodów wejścia) jest w teście, a sama degradacja to cztery linie gałęzi w `InstallHook` — do potwierdzenia przeglądem i odbiorem epiku. Fizyczne połykanie myszy w `shell` i `ClipCursor` w `osk` (brak nadawcy `clipHwnd` do czasu epic-osk) są N/A na tej maszynie i idą do odbioru epiku na UM760.

## Plan Change Log

## Review Triage Log

### 2026-10-22 — Review pass
- verdicts: 8 findings — high 0, medium 0, low 7, false 0, maybe-false 1
- findings:
  - `[low]` `[reject]` Dwa odczyty `_routing.Current` na jedno zdarzenie myszy (raz w `SyncClip`, raz w `InputPolicy.OnPointer`) — sprawdzone: `MouseHookCallback` (`InputHub.cs:481-482`) woła `SyncClip()` i dopiero potem politykę, więc nowy wiersz przyjęty w tym oknie daje decyzję z nowego wiersza i prostokąt ze starego. Skutek to co najwyżej jeden klik przepuszczony w chwili otwarcia OSK; zdarzenie jest tak wąskie, że użytkownik go nie spotka, a najmniejsza naprawa (podanie wiersza do `OnPointer`) zmienia kontrakt `IInputTarget` — czyli więcej niż bezpośrednia poprawka. Odrzucone jako `low` wg reguły „nieprawdopodobne w codziennym użyciu i poprawka większa niż korekta”.
  - `[low]` `[reject]` `WH_MOUSE_LL` woła Win32 i może pisać do logu w callbacku (`GetWindowRect`/`ClipCursor`, a przy porażce `Report` → plik) — sprawdzone: te wywołania są w callbacku, ale tylko przy zmianie `epoch` i tylko przy porażce clipu; klawiaturowy hook loguje dziś tak samo w `catch` (`InputHub.cs:458-462`), a `LowLevelHooksTimeout` jest rzędu setek ms. Poprawka (przeniesienie synchronizacji clipu do komunikatu i przepuszczenie bieżącego zdarzenia) dodaje gałęzie i stan, więc odrzucone jako `low`.
  - `[low]` `[reject]` Zdegenerowany prostokąt z `GetWindowRect` (puste okno, okno zminimalizowane) jest przyjmowany jako clip, więc w `osk` każdy klik jest połykany — sprawdzone: `ClipCursorGate.Apply` (`ClipCursorGate.cs:65-76`) sprawdza tylko BOOL. W tym przebiegu nikt jeszcze nie wysyła `clipHwnd` (epic-osk), więc stan jest nieosiągalny; zabezpieczenie (odrzucenie pustego prostokąta) to nowa gałąź z testem ponad bezpośrednią korektę, a sprawdzenie należy do integracji epic-osk. Odrzucone jako `low`.
  - `[low]` `[reject]` `_clipErrorCode` może nadpisać kod porażki `GetWindowRect` kodem zwolnienia clipu — sprawdzone: `ApplyClipCursor` pisze `_clipErrorCode` tylko, gdy `ClipCursor` zawiedzie (`InputHub.cs:596-600`), więc do nadpisania potrzebna jest podwójna porażka (brak okna i nieudane zwolnienie), a wpis i tak dotyczy tej samej operacji `clipCursor`. Naprawa (drugie pole na kod) to dodatkowy stan bez dowodu osiągalności, więc odrzucone jako `low`.
  - `[maybe-false]` `[defer]` Prostokąt clipu i punkt hooka mogą być w różnych przestrzeniach DPI, bo aplikacja nie deklaruje per-monitor awareness — sprawdzone: `MediaStation.App.csproj` nie ma manifestu DPI, a plan twierdzi „per-monitor DPI aware”; przy 200% na ALDER i drugim monitorze o innej skali różnica jest możliwa. Nie da się tego rozstrzygnąć bez pomiaru `GetProcessDpiAwareness` na opublikowanym exe i przebiegu z OSK na monitorze o innej skali; zapisane w `deferred` (medium, unverified), do odbioru epiku razem z epic-osk.
  - `[low]` `[reject]` Mapowanie `WM_*`/`MSLLHOOKSTRUCT` na `PointerStroke` (`XButton`, znak kółka, znacznik) nie ma testu i nie jest wymienione w lukach pokrycia — sprawdzone: `PointerStrokeOf` (`InputHub.cs:503-552`) jest Win32 i `InputHub.cs` celowo nie wchodzi do projektu testów; nie ma dowodu błędnego mapowania, a naprawa (wydzielenie bez-Win32 szwu i testy) dodaje typ i powierzchnię ponad bezpośrednią korektę. Uzupełnienie listy luk jest edycją tego planu, więc odrzucone jako `low`.
  - `[low]` `[reject]` Plan przeczy sobie: „Luki pokrycia” oznaczają fizyczne połykanie myszy jako N/A na ALDER, a „Manual checks” mają je na ALDER; dodatkowo `Verification` mówi o baseline 711, a notatki o 782/828 — sprawdzone: oba zdania faktycznie są w planie. „Manual checks” to lista dla człowieka (automat nie ruszy fizyczną myszą), więc sprzeczność jest redakcyjna, a poprawka jest edycją tego planu — odrzucone wg reguły „nie poprawiaj planu tego przebiegu”.
  - `[low]` `[defer]` AD-8 nadal opisuje `SetInputRouting` bez `clipHwnd`, a kontrakt ma już opcjonalny klucz — sprawdzone: `architecture-mediastation.md:194` nie wymienia `clipHwnd`, a testy kontraktu pinują go w każdej kopercie. Luka jest znana i śledzona przez epikę (`epic-input.md:61` każe dopisać `clipHwnd` do AD-8 przed epic-osk), więc zapisana w `deferred` (low); poprawka jest edycją dokumentu architektury, nie tego planu.

## Design Notes

Hook myszy żyje na tym samym wątku i oknie co hook klawiatury, bo Windows wymaga pętli komunikatów w wątku, który go założył, a `Dispose` i tak zwalnia zasoby tylko na wątku Input. `ClipCursor` woła wątek Input, więc cache prostokąta (`_clipRect`) ma jednego właściciela; powiadomienie z wątku Orchestratora to tylko `PostMessage` (`WM_APP + 1`), a `SyncClip()` dodatkowo porównuje `epoch` przy zdarzeniu myszy, więc zgubiony komunikat nie zostawia starego clipu. Prostokąt z `GetWindowRect` jest w pikselach fizycznych, tak jak `MSLLHOOKSTRUCT.pt` (WPF w .NET jest per-monitor DPI aware); przy porażce `GetWindowRect` clip jest zwalniany, bo uwięzienie kursora w nieaktualnym prostokącie jest gorsze niż brak ograniczenia. Porażka hooka myszy degraduje (jak Raw Input): klawiatura i Home muszą działać dalej (AD-8), a połknięcia myszy po prostu nie ma.

## Verification

**Commands:**
- `dotnet build MediaStation.slnx -c Release` — expected: 0 ostrzeżeń, 0 błędów (CsWin32 generuje nowe symbole).
- `dotnet test MediaStation.slnx -c Release` — expected: wszystkie zielone (baseline 711 plus nowe przypadki macierzy, `Changed`, kontrakt, schemat, kalibracja).
- `dotnet run --project tools/MediaStation.SchemaExport -c Release` — expected: `set-input-routing.schema.json` z opcjonalnym `clipHwnd`; `git status` czysty (katalogi generowane są w `.gitignore`).
- `cd web && npm run build` — expected: contracts, typecheck i Vite zielone.
- `dotnet publish src/MediaStation.App -c Release -r win-x64 --self-contained true` i `MediaStation.exe` z `tools/samples/config` — expected: kod 0, pięć wpisów jak w AC, zero `inputHubError`; po wyjściu kursor i klikanie działają, a config użytkownika odtworzony bajt w bajt.

**Manual checks (hardware-only, not blocking):**
- Fizyczny ruch i klik na ALDER w trybie `shell` są połykane (brak wpisów w logu), a po zamknięciu aplikacji wracają — obecny hook LL realnie widzi fizyczną mysz.
- Clip w `osk` nie ma jeszcze wysyłającego `clipHwnd` (epic-osk): pokrywa go test decyzji i przegląd kodu, a sprawdzenie na UM760 należy do odbioru epiku.

## Auto Run Result

**Status:** built.

**Summary:** InputHub dostał hook `WH_MOUSE_LL` na wątku Input, a polityka decyzję `OnPointer` według `pointer` z lokalnej kopii routingu: `blocked` połyka ruch, kliknięcia i kółko, `allowed` przepuszcza wszystko poza kliknięciem poza prostokątem clipu w `osk`, a własne wstrzyknięcie MediaStation zawsze przechodzi. `SetInputRouting` ma nowe, ostatnie opcjonalne pole `clipHwnd` (stary kod epiki 2 kompiluje się i deserializuje bez zmian, `clipHwnd` jedzie jako jawny `null`), a InputHub przy `osk` z niezerowym uchwytem więzi kursor w prostokącie okna (`ClipCursor`) i zwalnia clip po każdej zmianie wiersza routingu oraz w `Dispose`; stan clipu trzyma bez-Win32 `ClipCursorGate`, więc zwolnienie po przejściu na `shell` i porażkę `GetWindowRect`/`ClipCursor` (`inputHubError {operation: clipCursor}`) pokrywają testy. Porażka rejestracji hooka myszy degraduje: wpis `inputHubError {operation: mouseHook}` i klawiatura/pad/timer działają dalej. Wskaźnik nigdy nie daje Actions, kalibracja ma no-op `OnPointer`, a log `inputRouting` zostaje bez nowego klucza.

**Files changed:**
- `src/MediaStation.Core/Input/PointerStroke.cs` — typy zdarzenia wskaźnika i `ScreenRectangle` z wyłącznymi prawą i dolną krawędzią.
- `src/MediaStation.Core/Contracts/SetInputRouting.cs` — opcjonalny `long? ClipHwnd = null` na końcu rekordu.
- `src/MediaStation.Core/Input/InputRoutingState.cs` — `ClipHwnd` w kopii, `Changed` po przyjętym `epoch` poza `lock`, `PointerClip.WindowOf`.
- `src/MediaStation.Core/Input/InputPolicy.cs` — `OnPointer` według `pointer`, jedno odczytanie kopii, znacznik wstrzyknięcia, zero Actions.
- `src/MediaStation.Core/Input/InputTarget.cs` — `OnPointer` w kontrakcie celu.
- `src/MediaStation.Core/Input/InputCalibration.cs` — no-op `OnPointer` (kalibracja nie połyka i nie klipuje).
- `src/MediaStation.Core/Input/InputHubError.cs` — operacje `mouseHook` i `clipCursor`.
- `src/MediaStation.App/Platform/Input/InputHub.cs` — hook `WH_MOUSE_LL`, mapowanie `MSLLHOOKSTRUCT`, `SyncClip`/`WM_APP + 1`, szwy `GetWindowRect`/`ClipCursor`, zwolnienia, wymagany `InputRoutingState`.
- `src/MediaStation.App/Platform/Input/ClipCursorGate.cs` — stan clipu bez Win32 ze szwami `measure`/`apply` (linkowany do testów).
- `src/MediaStation.App/Platform/System/NativeMethods.txt` — `ClipCursor`, `GetWindowRect`, `PostMessageW`, `MSLLHOOKSTRUCT`, `RECT`, `LLMHF_INJECTED` i stałe `WM_*` myszy.
- `src/MediaStation.App/CompositionRoot.cs` — współdzielony `InputRoutingState` w hubie trybu głównego, świeży w kalibracji.
- `tests/MediaStation.Core.Tests/{PointerPolicyTests,ClipCursorGateTests}.cs` — macierz wskaźnika i stan clipu (nowe).
- `tests/MediaStation.Core.Tests/{CoreMessageContractTests,InputRoutingStateTests,InputHubRoutingPortTests,InputCalibrationTests,LogActionSinkTests}.cs` i `MediaStation.Core.Tests.csproj` — kontrakt/schemat `clipHwnd`, `Changed`, kopia lokalna, no-op kalibracji, wpisy `mouseHook`/`clipCursor`, link `ClipCursorGate`.

**Review findings:** 8 findings (high 0, medium 0, low 7, maybe-false 1). Patches applied: 0. Deferred: 2 — przestrzeń współrzędnych DPI clipu vs punkt hooka (medium, unverified, do odbioru epiku z epic-osk) i nieaktualny opis `SetInputRouting` w AD-8 (low, śledzony przez `epic-input.md:61` przed epic-osk). Rejected: 6 `low` — dwa odczyty routingu na zdarzenie (mikroskopijne okno, naprawa zmienia kontrakt `IInputTarget`), Win32/log w callbacku hooka (tylko przy zmianie `epoch` i porażce, wzorzec z hooka klawiatury), zdegenerowany prostokąt clipu (nieosiągalny, dopóki nikt nie wysyła `clipHwnd`; guard to nowa gałąź), nadpisanie kodu błędu przy podwójnej porażce (dodatkowy stan bez dowodu), brak testu mapowania `WM_*` (naprawa dodaje szew i typ; uzupełnienie luk to edycja planu), redakcyjna sprzeczność planu o N/A na ALDER i baseline 711 vs 782 (poprawka jest edycją tego planu).

**Follow-up review recommended:** false — przebieg bez patchy (0), więc konwergencja; progi `high` i dwa `medium` nie zostały osiągnięte.

**Verification:** `dotnet build MediaStation.slnx -c Release` — 0 ostrzeżeń, 0 błędów; `dotnet test MediaStation.slnx -c Release` — 828/828 passed, 0 failed, 0 skipped; `dotnet run --project tools/MediaStation.SchemaExport -c Release` — `set-input-routing.schema.json` z opcjonalnym `clipHwnd` (`integer|null`, `default: null`, poza `required`), `git status` czysty; `cd web && npm run build` — contracts, typecheck i Vite zielone; `dotnet publish src/MediaStation.App -c Release -r win-x64 --self-contained true` + `MediaStation.exe` z `tools/samples/config` (10 s, `CloseMainWindow`) — kod 0, pięć wpisów `start` → `inputRouting {mode: "shell", pointer: "blocked", epoch: 1}` → `powerSignalsRegistered` → `snapshotPublished` → `stop`, zero `inputHubError`; config użytkownika odtworzony bajt w bajt (SHA-256 zgodne, `config/invalid` na miejscu).

**Residual risks:** fizyczne połykanie myszy w `shell`, `ClipCursor` w `osk` (brak nadawcy `clipHwnd` do czasu epic-osk) i DPI na monitorze o skali innej niż systemowa są niesprawdzone automatem — idą do odbioru epiku na UM760 i do pozycji `deferred`. Sama rejestracja `SetWindowsHookEx(WH_MOUSE_LL)` i degradacja przy jej porażce pozostają poza testami (granica epiki: `InputHub.cs` nie wchodzi do projektu testów); obserwowalny kontrakt błędu jest pokryty testem.
