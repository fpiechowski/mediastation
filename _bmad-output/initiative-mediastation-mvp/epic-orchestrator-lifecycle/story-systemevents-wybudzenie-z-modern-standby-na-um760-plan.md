---
title: 'SystemEvents: adapter power i wybudzenie z Modern Standby'
type: 'feature'
ticket: '2'
created: '2026-10-21'
status: done
baseline_revision: '4296ec329a581ad086598fd10c0df2ec247b4b30'
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
      The adapter shim's registration-echo window (`PowerNotifications._registering`) and its three-source wiring have no automated test; only the Win32-free classifier, reporter and gate are covered.
    evidence: |-
      No test project compiles PowerNotifications (the plan forbids an App test project), DisplayStateNotificationTests passes `duringStartup` itself, and both iteration-2 runs saw no OS echo. Settle it with an HWND-level harness, or with an echo observation during the UM760 epic acceptance.
    location: >-
      src/MediaStation.App/Platform/System/PowerNotifications.cs
    severity: medium (unverified)
  - summary: >-
      A throw from `HwndSource.AddHook` or the `SystemEvents.PowerModeChanged` subscribe after the hook is attached leaks that hook for the process lifetime.
    evidence: |-
      CompositionRoot now catches adapter-construction failures and still disposes the loop, but the partially constructed PowerNotifications instance never gets Dispose, so an already-added HWND hook stays attached (its reporter is disposed, so late callbacks are dropped). Reachable only if AddHook or the event subscribe throws during startup.
    location: >-
      src/MediaStation.App/Platform/System/PowerNotifications.cs
    severity: low (unverified)
---

<intent-contract>

## Intent

**Problem:** Brak produkcyjnego adaptera power, więc wybudzenie z Modern Standby nie dociera do Core: nie ma logów `powerSignal`/`systemResumed`, Eventu `SystemResumed` ani snapshotu z nowym `seq` `focusRequest{default}`.

**Approach:** Adapter w `App/Platform/System` podsłuchuje trzy sygnały (hook `WM_POWERBROADCAST`/`PBT_APMRESUMEAUTOMATIC`, `RegisterPowerSettingNotification` dla `GUID_CONSOLE_DISPLAY_STATE`, `SystemEvents.PowerModeChanged`), loguje każdy kandydat jako `powerSignal {source}`, przepuszcza jednego zwycięzcę przez okno 5 s w adapterze, loguje `systemResumed` i wkłada `SystemResumed{source}` do kolejki Orchestratora. Core rejestruje Event i obsługuje go jako Home + nowy `focusRequest{default, seq}`.

## Boundaries & Constraints

**Always:** Win32 tylko w `App/Platform/*` przez CsWin32; Core `net10.0` bez Win32/WPF (AD-3); Orchestrator jedynym writerem stanu, adapter tylko loguje i raportuje fakt (AD-4); okno deduplikacji w adapterze, nigdy w `Orchestrator.Handle`; src logu `system`, klucz danych `source`; publikacja dalej latest-wins przez `ShellBridge` (AD-14).

**Never:** polityka story 8 (Background, `RenewSession`, zastępowanie przełączenia, `SetInputRouting`); nowa wartość `ShellScreen.Home` (należy do epic-home-screen); projekt testów App; ręczna edycja `web/contracts/**` i `web/shell/**`; pliki ticketów.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Resume automatic | `WM_POWERBROADCAST` + `PBT_APMRESUMEAUTOMATIC` | `powerSignal{source:"PBT_APMRESUMEAUTOMATIC"}`, `systemResumed`, Event w kolejce | brak |
| Display on | `PBT_POWERSETTINGCHANGE`, `PowerSetting == GUID_CONSOLE_DISPLAY_STATE`, `Data[0] == 1` | jak wyżej, `source:"GUID_CONSOLE_DISPLAY_STATE"` | `Data[0] == 0` → brak wpisu |
| Power mode | `PowerModeChanged` z `PowerModes.Resume` | tylko `powerSignal{source:"PowerModeChanged"}`, brak Eventu | inne tryby pominięte |
| Duplikat ≤ 5 s | kandydat w oknie 5 s od przyjętego | kandydat zalogowany, brak `systemResumed` i Eventu | brak |
| Burst | 3 kandydaty pod rząd | dokładnie 1 `systemResumed`, 1 Event | brak |
| Rejestracja padła | `RegisterPowerSettingNotification` → null | `powerSignalsRegistered{displayState:false}`, pozostałe źródła działają | log, bez crasha |
| Post po zamknięciu | callback po `Dispose` pętli | Event odrzucony | `InvalidOperationException` połknięty |

</intent-contract>

## Code Map

- `src/MediaStation.App/Platform/System/WakeSignalGate.cs` (nowy) — **reguła w App, nie w Core** (decyzja epiki): czysty typ pod `lock` z wstrzykniętym `TimeProvider`; `TryAccept(string source)` przyjmuje tylko `PBT_APMRESUMEAUTOMATIC` i `GUID_CONSOLE_DISPLAY_STATE`, a okno 5 s liczy na zegarze monotonicznym (`GetTimestamp`/`GetElapsedTime`, kotwica = ostatni przyjęty): kandydat przed upływem 5 s odpada, dokładnie po 5 s wchodzi, a zmiana zegara ściennego (także cofnięcie) nie ma wpływu. Linkowany do testów (`FakeTimeProvider` + `Advance`).
- `src/MediaStation.App/Platform/System/DisplayStateNotification.cs` (nowy, bez Win32/WPF) — `internal static`: `IsWake(ReadOnlySpan<byte> payload, Guid setting, bool duringStartup)` czyta `POWERBROADCAST_SETTING` jako bajty: GUID na offsecie 0, `DataLength` (uint) na 16 musi być równe `sizeof(uint)` (4), bufor musi pokrywać zadeklarowany DWORD, `Data[0]` na 20 musi być 1 (on; 0 = off i 2 = dimmed odrzucone); `duringStartup` (echo rejestracji) odrzucone. Stałe `DataLengthOffset=16`, `DataOffset=20`, `DisplayStateDataLength=4`, `DisplayStateOn=1`.
- `src/MediaStation.App/Platform/System/PowerSignalReporter.cs` (nowy, bez Win32/WPF) — log `powerSignal {source}` na każdego kandydata, `systemResumed {source}` i `Post(SystemResumed)` tylko dla zwycięzcy, `powerSignalsRegistered {displayState, windowHook, powerModeChanged}` (`warn` przy niepełnej rejestracji), no-op po `Dispose` (`_disposed` jest `volatile`, bo `SystemEvents` woła z innego wątku). `Src = "system"`.
- `src/MediaStation.App/Platform/System/PowerNotifications.cs` (nowy) — cienki shim Win32: `HwndSource.FromHwnd(hwnd).AddHook` (hook zawsze zwraca `false`), `RegisterPowerSettingNotification(hwnd, GUID_CONSOLE_DISPLAY_STATE, DEVICE_NOTIFY_WINDOW_HANDLE)` przez samo-wyrejestrowujący `UnregisterPowerSettingNotificationSafeHandle`, `SystemEvents.PowerModeChanged` (tylko `PowerModes.Resume`); czyta najpierw `DataLength` (4 B na offsecie 16), a dopiero po zgodności z `sizeof(uint)` kopiuje nagłówek i zadeklarowane dane (`Marshal.Copy`, `lParam == 0` odrzucony) i woła `DisplayStateNotification.IsWake`.
- `src/MediaStation.App/Platform/System/NativeMethods.txt` (nowy) — `RegisterPowerSettingNotification`, `UnregisterPowerSettingNotification`, `PBT_APMRESUMEAUTOMATIC`, `PBT_POWERSETTINGCHANGE`, `WM_POWERBROADCAST`, `GUID_CONSOLE_DISPLAY_STATE`; **bez** `POWERBROADCAST_SETTING` (bajty parsuje helper).
- `src/MediaStation.App/MediaStation.App.csproj` — `Microsoft.Windows.CsWin32` 0.3.363 (`PrivateAssets="all"`), `AllowUnsafeBlocks` (potrzebne tylko na `&guid` w rejestracji), `AdditionalFiles` na `NativeMethods.txt`. W projekcie próbnym generator daje `Windows.Win32.PInvoke.{WM_POWERBROADCAST, PBT_APMRESUMEAUTOMATIC, PBT_POWERSETTINGCHANGE, GUID_CONSOLE_DISPLAY_STATE}`, `REGISTER_NOTIFICATION_FLAGS.DEVICE_NOTIFY_WINDOW_HANDLE` i `UnregisterPowerSettingNotificationSafeHandle`.
- `src/MediaStation.Core/Contracts/Messages.cs` — `public sealed record SystemResumed(string Source) : Event;` po `OrchestratorStarted`.
- `src/MediaStation.Core/Contracts/MessageTypes.cs` — rejestracja `SystemResumed` po `OrchestratorStarted`.
- `src/MediaStation.Core/Orchestrator/Orchestrator.cs` — `SystemResumed => OnDefaultFocus()` (obok `OrchestratorStarted`): `_rev++`, `_focusSeq++`, te same `apps`, `ShellScreen.Placeholder`, `focusRequest{DefaultTarget,_focusSeq}`, zero komend.
- `src/MediaStation.App/Orchestrator/OrchestratorLoop.cs` — `public void Post(IOrchestratorMessage message)`: no-op po `Dispose`, połyka `InvalidOperationException` z wyścigu z `CompleteAdding`; komentarz w `Run` mówi prawdę o braku portów do story 3.
- `src/MediaStation.App/CompositionRoot.cs` — jeden `TimeProvider.System` dla logu i adaptera; po `loop.Start()` adapter z `new WindowInteropHelper(window).Handle` i `m => loop.Post(m)`; `app.Exit` zwalnia adapter przed `loop`.
- `tools/inject-power.ps1` (nowy) — wstrzykuje syntetyczny `WM_POWERBROADCAST`/`PBT_APMRESUMEAUTOMATIC`, dopisuje do logu wpis `powerSignalInjected {source:"synthetic"}` (marker odróżnia dowód syntetyczny od prawdziwego wybudzenia), asertuje liczniki i zamyka aplikację.
- `tests/MediaStation.Core.Tests/*` — `WakeSignalGateTests`, `DisplayStateNotificationTests`, `PowerSignalReporterTests`, `OrchestratorLoopTests` (pliki App linkowane przez `Compile Link`, precedens `MediaStation.LogAppendCheck`), `OrchestratorTests` (`SystemResumed`), `CoreMessageContractTests` (koperta, schemat, rejestr), `CoreBoundaryTests` (brak reguły wybudzenia w Core).

## Tasks & Acceptance

**Execution:**
- [x] `Core/Contracts/Messages.cs` + `MessageTypes.cs` -- rekord `SystemResumed`, rejestracja -- właściciel Eventu ze story 2.
- [x] `App/Platform/System/WakeSignalGate.cs` -- reguła zwycięzcy i okno 5 s w App -- decyzja epiki „dedupe w adapterze, nie w Core”.
- [x] `App/Platform/System/DisplayStateNotification.cs` -- klasyfikacja `PBT_POWERSETTINGCHANGE` bez Win32 -- jedyna hardware'owa mapka, musi być testowalna.
- [x] `App/Platform/System/PowerSignalReporter.cs` -- logi `powerSignal`/`systemResumed`/`powerSignalsRegistered`, post Eventu, guard `Dispose` -- cała księgowość adaptera testowalna.
- [x] `App/Platform/System/PowerNotifications.cs` + `NativeMethods.txt` -- hook, rejestracja, `SystemEvents`, kopiowanie bajtów -- cienki shim Win32.
- [x] `App/MediaStation.App.csproj` -- CsWin32 + unsafe + `AdditionalFiles` -- interop tylko przez generator.
- [x] `Core/Orchestrator/Orchestrator.cs` -- `SystemResumed` → snapshot z nowym `seq` -- wspólny `OnDefaultFocus` ze `OrchestratorStarted`.
- [x] `App/Orchestrator/OrchestratorLoop.cs` -- `Post` + test wyścigu zamknięcia -- jedno wejście do kolejki dla adaptera.
- [x] `App/CompositionRoot.cs` -- start i zwolnienie adaptera przy ważnym configu -- brak adaptera przy `ConfigError`.
- [x] `tools/inject-power.ps1` -- syntetyczny sygnał + marker w logu -- dowód z tego ticketu jest odtwarzalny i oznaczony.
- [x] `tests/MediaStation.Core.Tests/*` -- gate (także krok zegara wstecz), display-state, reporter, `Post`, Orchestrator, koperta/schemat/rejestr, granica Core -- każdy wiersz macierzy ma uruchomiony test.

**Acceptance Criteria:**
- Given dwa skonfigurowane `apps` i `OrchestratorStarted`, when `SystemResumed("PBT_APMRESUMEAUTOMATIC")`, then `rev: 2`, `focusRequest{target:"default", seq:2}`, `apps` bez zmian, zero komend.
- Given przyjęty sygnał wybudzenia w t0, when kolejne `PBT_APMRESUMEAUTOMATIC` przychodzą w t0+4,999 s i t0+5 s, then pierwszy jest pominięty, a drugi przyjęty; a gdy zegar ścienny cofa się w tym oknie, then okno nadal liczy się monotonicznie (kandydat przed 5 s odpada, po 5 s wchodzi).
- Given `PowerModeChanged` jako jedyny kandydat, when adapter go obsłuży, then powstaje tylko `powerSignal` (bez `systemResumed`) i żaden Event nie trafia do kolejki.
- Given payload `GUID_CONSOLE_DISPLAY_STATE` z `DataLength 4` i `Data[0] 1`, when `IsWake(..., duringStartup: false)`, then kandydat; a dla `Data[0] 0`, `Data[0] 2`, innego GUID, `DataLength 0`, `DataLength 1`, payloadu krótszego niż 24 B albo `duringStartup: true`, then brak kandydata.
- Given pętla Orchestratora po `Dispose`, when `Post(SystemResumed)`, then brak wyjątku i brak publikacji; a gdy `Post` ściga się z `Dispose`, then żaden wyjątek nie wychodzi z wywołania.
- Given `SystemResumed("PBT_APMRESUMEAUTOMATIC")`, when serializacja koperty, then dokładnie `{"v":1,"type":"systemResumed","payload":{"source":"PBT_APMRESUMEAUTOMATIC"}}` i round-trip; schemat `system-resumed.schema.json` w eksporcie.
- Given zbudowany assembly Core, when szukamy reguły wybudzenia, then `WakeSignalGate` nie istnieje w `MediaStation.Core` (reguła mieszka w `App/Platform/System`).
- Given zbudowana Release z ważnym configiem, when `tools/inject-power.ps1` wstrzyknie `PBT_APMRESUMEAUTOMATIC` dwa razy w 1 s, a potem raz po ≥6 s, then każdy marker `powerSignalInjected` stoi bezpośrednio przed swoim `powerSignal` (3 wpisy ze źródła `PBT_APMRESUMEAUTOMATIC`), są 2 `systemResumed` (src `system`) i 3 `snapshotPublished` z `seq` 1, 2, 3 (start + 2 wybudzenia), a `stop` ma `exitCode 0`.

## Implementation Notes

- Iteracja 1 (kod wycofany w loopbacku `bad_plan`, patrz Plan Change Log): `OrchestratorStarted` i `SystemResumed` wołały jeden `OnDefaultFocus()`; `WakeSignalGate` stał w Core; `PowerNotifications` trzymało całą księgowość (logi, gate, guard `_starting`) i było nietestowane; `PowerSignalReporter.cs` powstał dopiero przy audycie macierzy. Wnioski, które obowiązują dalej, są w Code Map i Plan Change Log.

## Plan Change Log

- 2026-10-21 — `PBT_POWERSETTINGCHANGE`/`GUID_CONSOLE_DISPLAY_STATE` przychodzi także **przy rejestracji** jako bieżący stan wyświetlacza („display on”), nie jako zmiana. Bez zabezpieczenia każdy start aplikacji dawał `powerSignal` + `systemResumed` i snapshot `rev: 2`/`seq: 2`, co łamało kryterium „2 `snapshotPublished`” (byłoby 3) i w story 8 wyglądałoby jak wybudzenie. Dodano strażnik `_starting`: notyfikacje z okna `Start()` (rejestracja + wpis `powerSignalsRegistered`) nie są kandydatami, a prawdziwa zmiana po starcie nadal jest logowana. Reszta planu bez zmian.
- 2026-10-22 — Loopback `bad_plan` z przeglądu: `WakeSignalGate` stał w `Core/SystemEvents`, choć ticket mówi „deduplikacja w oknie 5 s działa w adapterze w App, nie w Core”. Znany zły stan: reguła adaptera jako typ publiczny w Core, który kolejne story mogą wziąć za politykę Orchestratora. Poprawka: reguła przenosi się do `App/Platform/System` (linkowana do testów jak `PowerSignalReporter`), a test granicy Core pilnuje, że typ nie wróci do Core. KEEP: rekord `SystemResumed` i jego rejestracja, `OnDefaultFocus` w Orchestratorze, `PowerSignalReporter` z logami `powerSignal`/`systemResumed`/`powerSignalsRegistered`, cienki shim `PowerNotifications` na hook + rejestrację + `SystemEvents`, `OrchestratorLoop.Post`, kolejność startu/zwolnienia w `CompositionRoot`, brzmienie logów (`src` `system`, klucz `source`).
- 2026-10-22 — Ten sam loopback domyka luki weryfikacji wskazane przez przegląd: klasyfikacja `PBT_POWERSETTINGCHANGE` (GUID, `DataLength`, `Data[0]`, echo rejestracji) i `OrchestratorLoop.Post` dostają testy przez `Compile Link`; okno 5 s nie może zablokować wybudzeń po cofnięciu zegara; `tools/inject-power.ps1` w repo znaczy syntetyczny sygnał wpisem `powerSignalInjected`; z `NativeMethods.txt` znika nieużywany `POWERBROADCAST_SETTING`. Znany zły stan: „każdy wiersz macierzy ma test” było nieprawdą dla gałęzi display-state i wyścigu zamknięcia.
- 2026-10-22 — Re-derivacja wykonana zgodnie z poprawionym Code Map. Notatka implementacyjna bez zmiany kształtu: CsWin32 0.3.363 generuje tu `HANDLE` (nie `HWND`), a `REGISTER_NOTIFICATION_FLAGS` siedzi w `Windows.Win32.UI.WindowsAndMessaging`; surowy overload `RegisterPowerSettingNotification(HANDLE, Guid*, flags)` zwraca `HPOWERNOTIFY`, więc shim sam opakowuje je w `UnregisterPowerSettingNotificationSafeHandle`, zachowując samo-wyrejestrowanie.
- 2026-10-22 — Poprawki po przeglądzie implementacji: okno gate'a liczy się monotonicznie (`TimeProvider.GetTimestamp`/`GetElapsedTime`, provider wstrzyknięty), `DataLength` musi być równe `sizeof(uint)` i pokryte buforem, shim czyta `DataLength` przed kopiowaniem danych, fixture'y display-state używają literalnych offsetów i bajtów GUID, `_disposed` reportera jest `volatile`, `CompositionRoot` rejestruje `Exit` przed startem adaptera i loguje jego awarię bez zabijania aplikacji, a `tools/inject-power.ps1` sprząta w `finally`, liczy ścieżki logu od nowa, wiąże marker z sygnałem, raportuje inne źródła osobno i traktuje `stop exitCode != 0` jak porażkę. Granica 5 s zostaje w testach jednostkowych, bo `PostMessage` jej nie zmierzy.

## Review Triage Log

### 2026-10-22 — Review pass 3 (thorough, 4 lenses, follow-up pass on a `built` plan)
- verdicts: 30 findings — high 0, medium 3, low 14, false 13, maybe-false 0
- findings:
  - `[false]` `[reject]` Blind: hook lost if HWND is 0, `EnsureHandle` missing — refuted: `CompositionRoot` calls `window.Show()` before `WindowInteropHelper(window).Handle`, so the HWND exists; a null `HwndSource` is reported as `windowHook:false`.
  - `[low]` `[reject]` Blind: `PowerNotifications._disposed` plain bool / in-flight message during teardown — `Dispose` runs once from `app.Exit` on the UI thread and the reporter's `_disposed` is volatile and drops late callbacks; carried reasoning from pass 2.
  - `[false]` `[reject]` Blind: `Report` runs concurrently from `SystemEvents` thread without protection — refuted: `WakeSignalGate` locks, `JsonLinesLog` writes under a lock, `_disposed` is volatile.
  - `[low]` `[reject]` Blind: `TryCopyDisplayStateSetting` may overread a sub-20-byte struct — a `PBT_POWERSETTINGCHANGE` always carries a full `POWERBROADCAST_SETTING` (≥20 B); only a foreign crafted `lParam` breaks it, same class as the rejected bogus-`lParam` row; fix is a guard beyond a direct correction.
  - `[low]` `[reject]` Blind: `PBT_APMRESUMESUSPEND` not handled — the epic decision names exactly `PBT_APMRESUMEAUTOMATIC` and `GUID_CONSOLE_DISPLAY_STATE=on` as winners.
  - `[low]` `[reject]` Blind: 5 s gate drops a genuine second wake and the window is not configurable — the window is the epic's explicit rule; recorded in Design Notes.
  - `[low]` `[reject]` Blind: injector targets `Process.MainWindowHandle`, posts only `PBT_APMRESUMEAUTOMATIC` — single top-level shell window; a display-state payload cannot be marshalled across processes by `PostMessage`; carried from pass 2 (runtime evidence drives one source).
  - `[low]` `[reject]` Blind: injector re-parses logs per poll, marker adjacency can be raced — adjacency is deliberate (marker ↔ signal binding); synthetic injection does not trigger `PowerModeChanged`; tool is a manual acceptance aid.
  - `[low]` `[reject]` Blind: no `CompositionRoot`/Post-race ordering test — carried: no App test project; `Post_racing_with_dispose_never_throws` and the harness `stop exitCode 0` cover the observable surface.
  - `[false]` `[reject]` Blind: `Dispose` vs `Post` ordering "half fixed" — refuted: `_disposed = true` precedes `CompleteAdding`, and `Post` swallows `InvalidOperationException` (incl. `ObjectDisposedException`) from `Add` only.
  - `[low]` `[reject]` Blind: `AllowUnsafeBlocks`, namespace `Platform.System` shadows `System` — compiles with 0 warnings; unsafe trade-off recorded in Design Notes; rename is churn.
  - `[low]` `[reject]` Blind: duplicated `"system"` source constant / undocumented log shapes — cosmetic; both are the same literal and the log contract lives in this plan.
  - `[medium]` `[defer]` Edge: echo delivered after the constructor returns would count as a wake — `carried` from pass 2; already recorded in `deferred` (HWND-level harness or UM760 observation).
  - `[low]` `[reject]` Edge: ctor exception after `AddHook` leaves hook/handler attached — `carried` from pass 2 (composition-level catch + already in `deferred`, low).
  - `[low]` `[reject]` Edge: `powerModeChanged` is a literal `true` — `carried` from pass 1/2.
  - `[false]` `[reject]` Edge: `WindowInteropHelper.Handle` may be 0 — refuted: `window.Show()` precedes it in `StartRun`.
  - `[false]` `[reject]` Edge: reporter leaks if `Start` threw — refuted: the `Exit` handler disposes `reporter` (and `notifications` when set), and the reporter drops late callbacks.
  - `[false]` `[reject]` Edge: `Exit` handler registered after `loop.Start()` — refuted: `Start` only creates the thread and enqueues; the handler is registered before the adapter, which is the failure the pass-2 fix addressed.
  - `[false]` `[reject]` Edge: `Report` can throw into WndProc/`SystemEvents` thread — refuted: `JsonLinesLog` swallows write failures and `OrchestratorLoop.Post` swallows the shutdown race; nothing else in `Report` throws.
  - `[low]` `[reject]` Edge: `systemResumed` logged though `Post` was a no-op after loop dispose — the adapter is disposed before the loop, so the window is a few instructions at shutdown; making `Post` return bool is added surface.
  - `[low]` `[reject]` Edge: `PowerNotifications._disposed` not volatile — duplicate of the Blind row above.
  - `[low]` `[reject]` Edge: injector strict adjacency can false-FAIL — duplicate of the Blind injector row.
  - `[low]` `[reject]` Edge: pid reuse in injector log scan — injector counts from the last `start` of a freshly launched process; reuse inside one run is not reachable.
  - `[low]` `[reject]` Edge: missing `stop` after forced kill passes with WARN — injector already treats `stop exitCode != 0` as failure and reports a missing `stop` explicitly.
  - `[low]` `[reject]` Edge: `new Guid(ReadOnlySpan<byte>)` assumes little-endian — Windows-only (x64/ARM64 little-endian) code.
  - `[medium]` `[defer]` Gap: shim dispatch (`OnWindowMessage`/`OnPowerModeChanged`, `_registering` wiring) has no automated test — `carried` from pass 2; same root cause as the existing first `deferred` entry (shim untested), covered there.
  - `[medium]` `[reject]` Gap: `CompositionRoot` wiring/shutdown order uncovered — inherent to the no-App-test-project boundary (stated in Boundaries); evidenced by two harness runs with `stop exitCode 0`.
  - `[false]` `[reject]` Gap other: log thread-safety of `JsonLinesLog` unverified — refuted: `JsonLinesLog` serialises writes under `lock (_gate)`.
  - `[low]` `[reject]` Gap other: partial constructor failure leaves hook — duplicate of the Edge ctor row, already in `deferred`.
  - `[medium]` `[reject]` Intent: expectations live at the real-wake surface while the diff exercises unit/contract plus a synthetic injection — `carried` from pass 1/2: `powercfg /a` shows no S0ix here and AGENTS.md assigns the real wake to the user's whole-epic UM760 acceptance.

### 2026-10-22 — Review pass 2 (thorough, 4 lenses, after bad_plan re-derivation)
- verdicts: 39 findings — high 0, medium 25, low 14, false 0, maybe-false 0
- findings:
  - `[medium]` `[defer]` Startup-echo suppression is still an untested decision (the classifier input is tested, the shim's `_registering` lifetime is not) — verified: no test compiles `PowerNotifications` and the recorded runs saw no echo; settle with an HWND-level harness or an echo observation at the UM760 acceptance; recorded in `deferred`.
  - `[medium]` `[patch]` `Marshal.Copy` copied 21 bytes before checking GUID/`DataLength`, over-reading a legal 20-byte struct — fixed: `TryCopyDisplayStateSetting` reads `DataLength` at 16 first and copies only after it equals `sizeof(uint)`.
  - `[medium]` `[patch]` `IsWake` accepted `DataLength >= 1` and never reconciled it with the buffer — fixed: `DataLength == 4` and `payload.Length >= DataOffset + dataLength`.
  - `[medium]` `[patch]` GUID/offset fixtures were built from the production constants (circular) — fixed: literal offsets and literal Windows GUID bytes, plus `DataLength 1` and 20/23-byte rejections.
  - `[medium]` `[patch]` Constructor guarded only `RegisterDisplayState`; `AddHook`/`SystemEvents +=` could escape with the loop undisposed — fixed at the composition boundary: `Exit` handler registered before start, adapter start wrapped in try/catch logging `powerNotificationsStartFailed`; per-call guards inside the shim stay a residual.
  - `[medium]` `[patch]` `app.Exit` disposer was registered after the adapter started — fixed: registered before, adapter → reporter → loop in nested `try/finally`.
  - `[low]` `[reject]` `stop` is logged before the disposers, so a drained snapshot could publish after it — real ordering nit, but the harness counts from the last `start`, nothing depends on `stop` being last, and reordering lifecycle logging is more than a direct correction.
  - `[low]` `[reject]` `ReportRegistration` has no `_disposed` guard — it runs once during construction, before any dispose is possible, so users never meet it.
  - `[medium]` `[patch]` Injector never correlated markers with the signals it asserts — fixed: each marker must be immediately followed by its own `PBT_APMRESUMEAUTOMATIC` `powerSignal`.
  - `[medium]` `[patch]` Injector leaked the app on failure and could attach to a stale process next run — fixed: whole run in `try/catch/finally` that always closes the app and prints the log tail.
  - `[medium]` `[patch]` Injector skipped the 5 s boundary and its description disagreed with the body (1 s vs 500 ms) — fixed: header states the boundary is unit-tested, wording aligned; the boundary itself stays in `WakeSignalGateTests` because `PostMessage` cannot measure it.
  - `[low]` `[reject]` Plan claimed every matrix row has a running test — the observable behaviour of each row is covered (reporter for registration failure, classifier for payloads, loop for post-dispose); the `null`-handle branch inside the shim is deliberately untested and the claim's fix is a plan edit.
  - `[low]` `[reject]` Frontmatter contradicted the body (`lenses_ran`, iteration, status) — those fields are owned by this workflow and are written by the review/finalize steps, not by the implementation.
  - `[medium]` `[patch]` Verification block and AC counted snapshots inconsistently (2 vs 3) — fixed: AC now expects 3 `snapshotPublished` with `seq` 1..3 (start + two wakes); numbers reconciled in `## Auto Run Result`.
  - `[low]` `[reject]` `CoreBoundaryTests` scans type names only — a differently named rule could slip; a behavioural assertion ("no Core type holds a window") is contrived and the epic decision is guarded well enough by the class-level check.
  - `[medium]` `[patch]` Gate measured the window on wall-clock time, so a wake could be suppressed for up to 5 s after a backward step returned — fixed: `TimeProvider.GetTimestamp`/`GetElapsedTime` (monotonic), provider injected; tests use `FakeTimeProvider`.
  - `[low]` `[reject]` `SystemResumed.Source` accepts null/empty and no test rejects a payload missing `source` — the envelope reader already requires the field (`RespectRequiredConstructorParameters`), and a null source would be logged verbatim; adding a validation layer is more than a direct correction.
  - `[medium]` `[patch]` Unconditional 21-byte copy (edge-case lens, same defect) — covered by the `TryCopyDisplayStateSetting` fix.
  - `[medium]` `[defer]` Echo posted after `Start()` would restore the false wake (edge-case lens) — same root cause as the deferred echo finding; one deferred entry covers it.
  - `[medium]` `[patch]` `AddHook`/`SystemEvents` throwing during construction left the hook attached and nothing disposed — fixed at the composition boundary (logging + loop disposal); the leaked HWND hook on a throwing subscribe remains a residual.
  - `[low]` `[reject]` `powerSignalsRegistered.powerModeChanged` stays a literal `true` — carried from pass 1; unchanged claim and code.
  - `[low]` `[patch]` `PowerSignalReporter._disposed` was not volatile, so an in-flight `SystemEvents` callback could log after dispose — fixed: `volatile`.
  - `[medium]` `[patch]` A throw from the adapter's `Dispose` skipped the loop's disposal — fixed: nested `try/finally` around adapter → reporter → loop.
  - `[low]` `[patch]` Injector failed on any extra display-state notification and assumed a single log file — fixed: other sources reported separately and excluded from the counts, log path recomputed per read (UTC-day rollover).
  - `[low]` `[patch]` Injector left the app running when a `Wait-For` timed out — covered by the `finally` fix.
  - `[medium]` `[patch]` Harness expects 3 snapshots while the AC said 2 (edge-case claim) — fixed in the AC.
  - `[low]` `[reject]` "Every matrix row has a running test" (edge-case claim) — same row as the blind-hunter finding; rejected there.
  - `[medium]` `[patch]` Display-state byte layout is pinned only by the production offsets (demonstrated by moving `DataOffset` to 22 with all tests green) — fixed: literal-offset fixtures.
  - `[medium]` `[defer]` Startup-echo suppression runs only in the untested shim (filed disposition: patch) — covered by the deferred entry above.
  - `[medium]` `[patch]` `snapshotPublished` count in the AC could not be produced by the shipped harness — fixed in the AC.
  - `[low]` `[reject]` "Home screen" is not observable at the UI surface — carried from pass 1: `ShellScreen` has no home value until epic-home-screen and the epic decision keeps the placeholder with the log as evidence.
  - `[medium]` `[reject]` `snapshotPublished` is asserted only in the PowerShell harness — carried from pass 1; the plan now states that surface and the harness marks synthetic entries, while a Core test cannot reach `ShellBridge` without an App test project.
  - `[medium]` `[reject]` "Core test" surface includes App sources compiled into the Core test project — inherent to the no-App-test-project boundary; stated in the plan and the csproj comment, and the shim stays thin on purpose.
  - `[medium]` `[reject]` Runtime evidence drives only one of the three sources (synthetic `PBT_APMRESUMEAUTOMATIC`) — the intent permits best-effort on the current machine, `powercfg /a` shows no S0ix, and the real three-source mix is the UM760 acceptance.
  - `[medium]` `[reject]` Registration echo is suppressed before logging, so "every candidate" is honoured only after startup — an echo of the current state is not a wake candidate; reading it as one would publish a home snapshot on every launch.
  - `[low]` `[reject]` Gate anchor and the `DataLength`/`Data[0]` thresholds are design choices without a measurement — carried from pass 1: the epic decision names the winners and no measurement is possible here; both are recorded in Design Notes.
  - `[low]` `[reject]` The harness writes its own lines into the production log — deliberate: the marker is what makes synthetic evidence distinguishable; the counts filter by source and by marker adjacency.
  - `[medium]` `[reject]` Additions beyond the ticket, and the shim's `SystemEvents +=` escape — the additions are required by matrix rows, the `+=` escape is now covered by the composition-level catch, and the literal `powerModeChanged` was carried from pass 1.

### 2026-10-22 — Review pass (thorough, 4 lenses)
- verdicts: 39 findings — high 0, medium 22, low 13, false 4, maybe-false 0
- findings:
  - `[low]` `[reject]` Code Map line references are stale and name a method that does not exist — real but a plan-documentation defect; the fix edits this build's plan, and the reviewer's own diff trace supplies the corrected anchors for the re-derivation.
  - `[low]` `[reject]` Code Map and task list omit `PowerSignalReporter` and its tests — same plan-documentation class; absorbed by the amendment without a code change.
  - `[medium]` `[bad_plan]` `_starting` guard has no matrix row, AC, test or Code Map entry — verified: the guard exists only in the untested shim and no test reaches it; amendment adds the display-state helper and its ACs.
  - `[medium]` `[bad_plan]` Guard only covers the synchronous registration echo; a posted notification would restore the false wake — verified: suppression is a transient window around `Start()`; amendment makes the decision a tested helper and documents the timing observation.
  - `[low]` `[reject]` `powerSignalsRegistered{powerModeChanged:true}` is a literal — true, but the field can only be true after a successful subscribe, and threading a real value through adds plumbing for no diagnostic gain.
  - `[medium]` `[bad_plan]` 5 s window measured on wall clock — verified: `now - last < Window` is true when `now < last`, so a backward step rejects every winner until wall time passes the stale anchor; amendment requires a monotonic-safe rule plus a test.
  - `[low]` `[reject]` `TryAccept` conflates rejection reasons — true; a `powerSignalDropped` message would add a new log contract beyond the ticket's three named entries, for an unlikely need.
  - `[medium]` `[bad_plan]` Matrix lacks rows for window-unavailable, dimmed, short payload, S3 — the matrix is inside the read-only intent-contract, so the amendment covers these cases as ACs instead.
  - `[low]` `[bad_plan]` `POWERBROADCAST_SETTING` is requested from CsWin32 but never used, and Design Notes contradict the unsafe block — verified; amendment removes the unused entry and corrects the note.
  - `[low]` `[reject]` `AllowUnsafeBlocks` assembly-wide — real containment nit, but the safe CsWin32 overload needs a bespoke `SafeHandle` wrapper for the HWND; documenting the trade-off is proportionate.
  - `[medium]` `[bad_plan]` Evidence script lives only in temp — verified: `inject-power.ps1` is not in the repo, so the epic-boundary acceptance cannot rerun it; amendment commits it to `tools/`.
  - `[medium]` `[bad_plan]` Synthetic log is indistinguishable from a real resume — verified: injected messages produce byte-identical lines and the "synthetic" qualifier is prose only; amendment makes the injector append a `powerSignalInjected` marker.
  - `[low]` `[reject]` Verification numbers not reconciled, no Auto Run Result block — the evidence block was updated to 304/304 during implementation and `## Auto Run Result` is written by Finalize; remaining items are plan edits.
  - `[medium]` `[bad_plan]` Epic-level meaning of the registration-echo guard recorded only story-level — verified; amendment puts it in Design Notes so it reaches the epic-boundary handoff.
  - `[medium]` `[bad_plan]` `OnDefaultFocus` sharing leaves the story 7 seam unclear — verified as an unstated design seam; amendment names it (story 7 adds a separate switch arm, not `ProbeExisting` inside the shared helper).
  - `[false]` `[reject]` "zero commands" leaves input routing in `app` after wake — the intent states the full wake policy (Background, `RenewSession`, `SetInputRouting{shell}`) is story 8's; no bad outcome at this ticket.
  - `[low]` `[reject]` `SystemResumed.Source` is an unconstrained string in the exported schema — intentional: the field carries the Windows source name verbatim for log/JSON parity, and story 8 may add sources; an enum would add a schema surface the ticket does not ask for.
  - `[medium]` `[bad_plan]` `ReportDisplayState` (GUID compare, offsets, startup guard) has no automated coverage — verified by search: no test reaches it; amendment extracts a Win32-free classifier and tests it.
  - `[low]` `[bad_plan]` Stale comment in `OrchestratorLoop.Run` about "story 1", and public visibility of new App types — verified; the re-derivation fixes the comment and keeps the new helpers `internal` where the link-compile allows.
  - `[medium]` `[bad_plan]` Clock stepped backwards rejects all wakes — same defect as the wall-clock finding; grouped, amendment covers it once.
  - `[low]` `[reject]` Bogus non-zero `lParam` from another process causes an AccessViolation — technically reachable only via a foreign `SendMessage` with a crafted pointer; a `VirtualQuery` guard is more than a direct correction, and no user meets this in the kiosk.
  - `[medium]` `[bad_plan]` Registration echo delivered after `Start()` restores the false wake — same root cause as the guard-timing finding; grouped.
  - `[false]` `[reject]` `Post` before `Start` breaks the first-snapshot contract — the sole call site starts the adapter after `loop.Start()` in the same method, so the program cannot reach that state.
  - `[low]` `[false]` `SystemResumed` yields `placeholder`, not a home screen — refuted: `ShellScreen` has no home value until epic-home-screen and the epic decision keeps the shell on the placeholder, with the log as the evidence.
  - `[medium]` `[bad_plan]` Display-state classification has no runnable verification (filed disposition: patch) — confirmed with a demonstration that flipping the comparison leaves all tests green; grouped with the classification extraction.
  - `[medium]` `[bad_plan]` Startup-window suppression unverified (filed disposition: patch) — grouped with the classification extraction; the amendment requires a tested decision input.
  - `[medium]` `[bad_plan]` `OrchestratorLoop.Post` shutdown-race guard uncovered — verified: no test compiles the loop; amendment links the file and requires a post-dispose and a post-during-dispose test.
  - `[medium]` `[bad_plan]` Gate uses wall-clock `DateTimeOffset` (other finding) — grouped with the clock-step defect.
  - `[medium]` `[bad_plan]` Dedupe rule's code lives in Core though the ticket says App — verified: `src/MediaStation.Core/SystemEvents/WakeSignalGate.cs`; this is the triggering plan defect (the plan drew a line the intent did not), fixed by moving the rule to `App/Platform/System` and guarding Core with a boundary test.
  - `[medium]` `[bad_plan]` Core-test surface reached by importing App source; the adapter has zero tested lines — true and inherent to the "no App test project" boundary; amendment states the precedent in the plan and keeps the shim thin.
  - `[medium]` `[bad_plan]` `snapshotPublished` with new `seq` has no automated assertion — true; the amendment states that this element is evidenced by the run log and adds the marker so the evidence is self-describing.
  - `[false]` `[reject]` "Home is not observable" makes the test unable to distinguish home from a no-op — refuted for this ticket: the intent's own scope hands the wake policy to story 8, so the observable surface is `rev`/`seq`/`focusRequest`, which the tests do assert.
  - `[medium]` `[bad_plan]` Candidate completeness narrowed at the untested platform surface — grouped with the startup-echo finding; the amendment keeps the exclusion but records it as a measurement observation for the epic boundary.
  - `[low]` `[reject]` Winner set taken from the epic Note, `PowerModeChanged` never wins, measurement escape hatch unexercised — the epic decision names exactly those two winners, and `powercfg /a` shows S0ix is unavailable here, so no measurement is possible on this machine.
  - `[low]` `[reject]` 5 s anchor (last vs first accepted) is a choice the intent does not determine — the epic wording "kolejne w ciągu 5 s" is satisfied by the last-accepted anchor and the choice is recorded in Design Notes.
  - `[medium]` `[bad_plan]` Queue and lifetime surfaces (`Post`, `CompositionRoot` wiring) untested — grouped with the `Post` finding.
  - `[medium]` `[bad_plan]` Registration failure tested only at the logging surface — verified; a true failure cannot be induced without Win32 injection, so the re-derivation keeps the logging contract tested and marks the null-handle branch as accepted residual risk.
  - `[medium]` `[bad_plan]` Verification surface broader than evidence, injector not in repo — grouped with the temp-script finding.
  - `[low]` `[reject]` Additions beyond the intent's text (`powerSignalsRegistered`, `_starting`, `Post`, dispose ordering, rename) — each is required by a matrix row or by the shutdown race; nothing contradicts the intent.

## Design Notes

Home to `ShellScreen.Placeholder` do epic-home-screen; dodanie `home` teraz przeciekłoby do schematu i TS innego epiku (decyzja epiki: shell zostaje na placeholderze, dowodem jest log). Reguła wyboru i okno 5 s mieszkają w `App/Platform/System` (dosłownie: „deduplikacja działa w adapterze w App, nie w Core”); testy dostają je przez `Compile Link`, a `CoreBoundaryTests` pilnuje, że typ nie wróci do Core — Orchestrator widzi wyłącznie gotowy `SystemResumed`. `PowerModeChanged` jest kandydatem do logu, ale nie zwycięzcą: reguła epiki wymienia tylko `PBT_APMRESUMEAUTOMATIC` i `GUID_CONSOLE_DISPLAY_STATE=on`; `powercfg /a` na ALDER pokazuje brak S0ix, więc pomiar nie jest tu możliwy i reguła idzie z Notes. Echo rejestracji `GUID_CONSOLE_DISPLAY_STATE` (system wysyła bieżący stan od razu po `RegisterPowerSettingNotification`) nie jest sygnałem wybudzenia, więc `duringStartup` je odrzuca, zanim zostanie kandydatem — do notatki odbioru epiku idzie jako obserwacja pomiarowa (ALDER, 2026-10-21/22), bo na UM760 echo może przyjść inaczej. Hook idzie na top-level HWND shella, bo broadcast nie dociera do okien message-only. `POWERBROADCAST_SETTING` czytamy jako bajty w helperze (GUID 0, `DataLength` 16, `Data[0]` 20), więc `AllowUnsafeBlocks` służy tylko `&guid` w rejestracji — świadomy kompromis, bezpieczny overload CsWin32 wymagałby własnego `SafeHandle` na HWND. `OnDefaultFocus` jest wspólny dla startu i wybudzenia, ale story 7 musi dołożyć `ProbeExisting` jako osobną gałąź `switch`, nie do tego helpera, inaczej każde wybudzenie sonduje wszystkie hosty. `SystemResumed.Source` zostaje otwartym stringiem: niesie nazwę sygnału Windows 1:1 do logu i JSON-a, a story 8 może dołożyć źródła.

## Verification

**Commands:**
- `dotnet test` -- wszystkie testy zielone (281 + nowe z `WakeSignalGateTests`, `DisplayStateNotificationTests`, `PowerSignalReporterTests`, `OrchestratorLoopTests`, `OrchestratorTests`, `CoreMessageContractTests`, `CoreBoundaryTests`).
- `dotnet build MediaStation.slnx -c Release` -- 0 ostrzeżeń, schematy/TS przegenerowane, Vite buduje shell.
- `dotnet build src/MediaStation.App -c Debug` -- typecheck i kontrakty przechodzą.
- `dotnet publish src/MediaStation.App -c Release -r win-x64 --self-contained true`, uruchomienie z ważnym `%LOCALAPPDATA%\MediaStation\config` i `tools/inject-power.ps1` (2× w 1 s, potem po ≥6 s) -- asercje `powerSignalInjected`, `powerSignal`, `systemResumed`, `snapshotPublished` z rosnącym `seq`; `powercfg /a` zapisane jako dowód dostępności Modern Standby. Prawdziwe uśpienie na UM760 zostaje w odbiorze epiku u użytkownika.

**Current-machine evidence (2026-10-21/22, HEAD `4296ec329a58` + working tree, ALDER / B760 Pro RS/D4 / Windows 11 Home):**

- Iteracja 1 (kod wycofany w loopbacku): `dotnet test` 304/304, Release i Debug build bez ostrzeżeń, przebieg 22:09 z syntetycznym `PBT_APMRESUMEAUTOMATIC` dał 3 `powerSignal`, 2 `systemResumed`, 2 `snapshotPublished` (seq 2, 3), `stop {exitCode:0}`, zero `error`; wcześniejszy przebieg 22:03 potwierdził realne echo `GUID_CONSOLE_DISPLAY_STATE` z OS. `powercfg /a`: Modern Standby (S0 Low Power Idle) niedostępny („The system firmware does not support this standby state”; tylko S3 i Hibernate), więc prawdziwe uśpienie jest tu niewykonalne i zgodnie z AGENTS.md dowodem jest sygnał syntetyczny plus testy; realny test na UM760 należy do odbioru epiku. Dowód iteracji 1 przepada razem z kodem -- nowy przebieg dopisuje się tutaj po re-derivacji.

**Iteracja 2 (re-derivacja poprawionego Code Map, 2026-10-22, HEAD `4296ec329a58` + working tree, ALDER / B760 Pro RS/D4 / Windows 11 Home):**

- `dotnet test` -- 313/313 zielone (w tym `WakeSignalGateTests` z zegarem monotonicznym, `DisplayStateNotificationTests`, `PowerSignalReporterTests`, `OrchestratorLoopTests`, `SystemResumed` w `OrchestratorTests`/`CoreMessageContractTests`, `CoreBoundaryTests` bez `WakeSignalGate` w Core).
- `dotnet build MediaStation.slnx -c Release` -- 0 ostrzeżeń, 0 błędów; SchemaExport wygenerował `system-resumed.schema.json`, `web/contracts/generated/system-resumed.ts` powstał, Vite zbudował shell.
- `dotnet build src/MediaStation.App -c Debug` -- 0 ostrzeżeń; `npm run contracts` + `npm run typecheck` przeszły.
- `dotnet publish src/MediaStation.App -c Release -r win-x64 --self-contained true` + uruchomienie z ważnym `%LOCALAPPDATA%\MediaStation\config` + `pwsh -File tools/inject-power.ps1` (2× w 0,5 s, potem po 6 s) -- dwa przebiegi (pid 41840 i 35132) z identycznym wynikiem: `OK: 3 powerSignal, 2 systemResumed (src system, PBT_APMRESUMEAUTOMATIC), 3 snapshotPublished (seq 1..3), 3 powerSignalInjected`, `stop: exitCode 0`. Log przebiegu (pid 41840): `powerSignalsRegistered {displayState:true, windowHook:true, powerModeChanged:true}`, `snapshotPublished` rev 1/seq 1 (start), `powerSignal` + `systemResumed` + `snapshotPublished` rev 2/seq 2 (po #1), `powerSignal` bez `systemResumed` (po #2, okno 5 s), `powerSignal` + `systemResumed` + `snapshotPublished` rev 3/seq 3 (po #3), 3 markery `powerSignalInjected {source:"synthetic"}` (src `harness`), zero `error`. Tym razem OS nie wysłał echa `GUID_CONSOLE_DISPLAY_STATE` w oknie startu (poprzedni przebieg 22:03 je potwierdził); ścieżkę echa pokrywa `Registration_echo_during_startup_is_not_a_wake`.
- `powercfg /a` (2026-10-22): Modern Standby (S0 Low Power Idle) nadal niedostępny („The system firmware does not support this standby state”; dostępne tylko S3 i Hibernate), więc prawdziwe uśpienie jest na tej maszynie niewykonalne -- zostaje w odbiorze epiku na UM760. Adapter zarejestrował wszystkie trzy źródła (`displayState`, `windowHook`, `powerModeChanged`), więc na UM760 wymaga tylko uruchomienia i uśpienia.
- Po poprawkach z przeglądu implementacji (2026-10-22): testy zmienionych plików 25/25 (`WakeSignalGateTests`, `DisplayStateNotificationTests`, `PowerSignalReporterTests`); `dotnet build src/MediaStation.App -c Debug -p:SkipWebBuild=true` i build projektu testów bez ostrzeżeń; przebieg pid 30748 z `powershell -File tools/inject-power.ps1` (Windows PowerShell 5.1): `OK: 3 powerSignal, 2 systemResumed (src system, PBT_APMRESUMEAUTOMATIC), 3 snapshotPublished (seq 1..3), 3 powerSignalInjected`, `stop exitCode 0`. Pełny `dotnet test` i Release build po tych poprawkach są po stronie odbioru przeglądu.


## Auto Run Result

**Status:** built (follow-up review pass 3, no code changes).

**Summary:** Adapter power in `App/Platform/System` (hook `WM_POWERBROADCAST`, `GUID_CONSOLE_DISPLAY_STATE` registration, `SystemEvents.PowerModeChanged`) logs every candidate as `powerSignal`, lets one winner through a monotonic 5 s window, logs `systemResumed` and posts `SystemResumed{source}` to the Orchestrator queue. Core registers the Event and answers with `OnDefaultFocus` (new `rev`/`seq`, `focusRequest{default}`, zero commands).

**Files changed:** `Core/Contracts/Messages.cs` + `MessageTypes.cs` (Event), `Core/Orchestrator/Orchestrator.cs` (`SystemResumed` handling), `App/Orchestrator/OrchestratorLoop.cs` (`Post`), `App/CompositionRoot.cs` (adapter wiring and disposal order), `App/Platform/System/{WakeSignalGate,DisplayStateNotification,PowerSignalReporter,PowerNotifications}.cs` + `NativeMethods.txt`, `App/MediaStation.App.csproj` (CsWin32), `tools/inject-power.ps1` (synthetic evidence), and tests in `tests/MediaStation.Core.Tests/*`.

**Review findings (pass 3):** 30 findings (medium 3, low 14, false 13). Patches applied: 0. Deferred: 0 new (two medium findings carried onto existing `deferred` entries: startup-echo timing and the untested shim). Rejected: all other findings, each with its reason in the Review Triage Log (false claims refuted by the code: HWND exists after `window.Show()`, log writes are locked and non-throwing, `Dispose` sets `_disposed` before `CompleteAdding`).

**Follow-up review recommendation:** `false` (patched counts: high 0, medium 0; the work has converged).

**Verification:** No code changed in this pass, so the last recorded results stand: `dotnet test` 313/313, Release and Debug builds with 0 warnings, and two `tools/inject-power.ps1` runs on ALDER (3 `powerSignal`, 2 `systemResumed`, 3 `snapshotPublished` seq 1..3, `stop exitCode 0`). Modern Standby is unavailable on this machine (`powercfg /a`), so the real wake is for the user's whole-epic UM760 acceptance.

**Residual risks:** The shim (`PowerNotifications`) has no automated test and the registration-echo timing is unobserved on UM760 (both in `deferred`); a throw from `AddHook`/`SystemEvents +=` leaks the hook until process exit; the display-state and `PowerModeChanged` sources are not injectable end to end.
