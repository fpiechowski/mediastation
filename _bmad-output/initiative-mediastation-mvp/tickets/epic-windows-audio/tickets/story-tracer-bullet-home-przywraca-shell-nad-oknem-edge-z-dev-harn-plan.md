---
title: 'Tracer bullet: Home przywraca shell nad oknem Edge z dev harnessu'
type: 'feature'
ticket: '1'
created: '2026-10-26'
status: done
baseline_revision: 'd2fbb94a0d0ba16646645b0a28da0c638d6e7177'
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
deferred: []
---

<intent-contract>

## Intent

**Problem:** `LogWindowManagerPort` tylko loguje, a hosty to `MissingHostPort`, więc Home nie zmienia żadnego okna i nie da się sprawdzić CAP-4 na prawdziwym oknie przeglądarki.

**Approach:** Dev harness (`MEDIASTATION_DEV_HARNESS=1`) uruchamia Edge/Chrome dla kafelków `web` i raportuje `AppWindowAttached`/`ProcessExited`; prawdziwy WindowManager w `Platform/Window` (własny wątek z pętlą komunikatów) wykonuje `ActivateWindow`, `MinimizeWindow`, `ShowShell` z odblokowaniem foreground i odpowiada `CommandCompleted`/`CommandFailed`. Decyzje leżą poza typami Win32 i mają testy przez linkowane źródła.

## Boundaries & Constraints

**Always:** Win32 tylko w `Platform/*` przez CsWin32 (nazwy dopisane do `Platform/System/NativeMethods.txt`); wykonawca nie rzuca przez port (AD-4); `MinimizeWindow` nigdy `SW_HIDE`; operacje na oknie shella przez `ShowWindowAsync` i asynchroniczne `SetWindowPos`; odblokowanie: symulowany Alt przez `KeyInjector` (znacznik `dwExtraInfo`, helper 3.7), fallback `AttachThreadInput`; komenda okienna kończy się `CommandCompleted` albo `CommandFailed` i wpisem `windowCommand{ type, cmdId, hwnd, result }`; nowe `ErrorCode`: `WindowNotFound`, `ForegroundDenied`, schematy i typy TS odświeżone przez build (AD-16); bez zmiennej env obie ścieżki zostają jak dotąd (`LogWindowManagerPort` nie jest już używany w trybie głównym, hosty `MissingHostPort`, OK daje `startWithoutHost`).

**Never:** harness sam nie wynosi okna (AD-14/15); bez portu CDP i bez ścieżki `Platform/Audio`; bez zmiany `Orchestrator` poza logiem porażki komendy okienna (stan bez zmian); bez `bmad-architecture`; bez ruszania `check-home-s10.ps1` (story 5.5), `Moonlight`/`native` hostów.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Minimize | `MinimizeWindow{hwnd}` żywe okno | `ShowWindowAsync(SW_MINIMIZE)`/`SW_SHOWMINNOACTIVE`, nigdy `SW_HIDE`; `CommandCompleted`; okno `IsIconic` i `IsWindowVisible` | — |
| Martwy hwnd | `hwnd` nieznany/zniszczony (`IsWindow` false) | `CommandFailed{WindowNotFound}`, wpis `result: WindowNotFound` | Orchestrator loguje, stan bez zmian |
| Activate zminimalizowanego | `ActivateWindow{hwnd}` iconic | restore, potem foreground z odblokowaniem; `CommandCompleted` | — |
| Foreground odmówiony | `SetForegroundWindow` fałszywe po Alt i po `AttachThreadInput` | `CommandFailed{ForegroundDenied}` | log, bez zmiany stanu |
| ShowShell | uchwyt shella wpisany | `ShowWindowAsync` + asynchroniczne `SetWindowPos`, foreground z odblokowaniem | brak uchwytu shella → `WindowNotFound` |
| Wybór okna drzewa | kandydaci: z właścicielem, niewidoczne, widoczne ownerless `Chrome_WidgetWin_1` | wybrane tylko widoczne, bez właściciela, klasa `Chrome_WidgetWin_1`, z procesu z drzewa `rootPid` | brak → harness czeka do limitu, potem `CommandFailed{Unexpected}` |
| Harness wyłączony | brak `MEDIASTATION_DEV_HARNESS=1` | `startWithoutHost` jak dotąd | — |

</intent-contract>

## Code Map

- `src/MediaStation.Core/Ports/Ports.cs` -- `IWindowManagerPort`, `IAppHostPort` (bez zmian kontraktu)
- `src/MediaStation.Core/Contracts/Messages.cs` -- `ErrorCode` (dopisać `WindowNotFound`, `ForegroundDenied`), `AppWindowAttached`, `ProcessExited`, `CommandCompleted/Failed`
- `src/MediaStation.Core/Orchestrator/Orchestrator.Failures.cs` / `Orchestrator.Stop.cs` -- porażka i sukces komendy spoza korelacji nie zmieniają stanu; `OrchestratorLoop` loguje porażkę
- `src/MediaStation.App/Platform/Window/LogWindowManagerPort.cs` -- wzorzec logu `windowCommand` (zastępowany w `CompositionRoot`)
- `src/MediaStation.App/Platform/Input/KeyInjector.cs` -- `TrySendKey(0x12)` to symulowany Alt ze znacznikiem; `Win32ForegroundWindow.cs` -- wzorzec GA_ROOT
- `src/MediaStation.App/Hosts/MissingHostPort.cs` -- wzorzec portu hosta (post przez `postsTo`)
- `src/MediaStation.App/CompositionRoot.cs` -- `StartRun`: `hosts`, `new LogWindowManagerPort(log)`, `managedShell`/`shellHwnd` (po `window.Show()`); wzorzec env `MEDIASTATION_DEBUG_HANG_UI`
- `src/MediaStation.App/Platform/System/NativeMethods.txt` -- lista CsWin32 (dopisać: `SetForegroundWindow`, `ShowWindowAsync`, `SetWindowPos`, `IsIconic`, `IsWindowVisible`, `IsWindow`, `GetWindow`, `GetClassNameW`, `EnumWindows`, `AttachThreadInput`, `GetCurrentThreadId` itp.)
- `tests/MediaStation.Core.Tests/MediaStation.Core.Tests.csproj` -- linkowanie źródeł App do testów; `CoreMessageContractTests.cs`, `LogWindowManagerPortTests.cs`, `RecordingLog.cs`
- `web/contracts/{schema,generated}` -- odświeża build (`npm run contracts:generate`); nie edytować ręcznie
- `tools/samples/config/apps.json` -- sample z kafelkiem `web` (`browser: edge`)

## Tasks & Acceptance

**Execution:**
- [x] `src/MediaStation.Core/Contracts/Messages.cs` -- dopisać `WindowNotFound`, `ForegroundDenied` do `ErrorCode` z komentarzem -- AD-4/AD-16; dopasować testy kontraktu/schematu
- [x] `src/MediaStation.App/Platform/Window/` (nowe pliki bez Win32: plan komendy → operacje, wybór metody odblokowania, wybór okna drzewa procesów, `WindowCommandExecutor` za interfejsem `IWindowApi`) -- czysta logika decyzji -- testowalna przez linkowanie
- [x] `src/MediaStation.App/Platform/Window/Win32WindowApi.cs`, `Win32WindowManagerPort.cs` -- CsWin32; własny wątek z pętlą komunikatów i kolejką (`PostThreadMessage`), `SetShellHandle(hwnd)`, wyjątki łapane na granicy, wyniki do kolejki Orchestratora, wpis `windowCommand{type,cmdId,hwnd,result}` -- zastępuje `LogWindowManagerPort`
- [x] `src/MediaStation.App/Hosts/DevHarness/` -- `DevHarnessWebHostPort` (`StartApp`/`StopApp`/`ProbeExisting`/`RenewSession` wg opisu ticketu), czysta budowa linii poleceń i wybór okna testowalne; włączanie przez `MEDIASTATION_DEV_HARNESS=1` (stała nazwy obok `DebugHangUi`-podobnego parsera)
- [x] `src/MediaStation.App/CompositionRoot.cs`, `Platform/System/NativeMethods.txt` -- rejestracja WindowManagera, `SetShellHandle` po utworzeniu okna, harness dla `AppType.Web` tylko przy zmiennej; teardown zamyka wątek WindowManagera (`TeardownSequence`)
- [x] `tests/MediaStation.Core.Tests/*` + csproj -- testy: Minimize nigdy `SW_HIDE`; martwy/nieznany hwnd → `WindowNotFound`; odmowa po obu metodach → `ForegroundDenied`; wybór okna pomija okna z właścicielem i niewidoczne; linia poleceń harnessu; wybór przeglądarki

**Acceptance Criteria:**
- Given martwy `hwnd`, when `MinimizeWindow`/`ActivateWindow`, then `CommandFailed{WindowNotFound}` i wpis `windowCommand ... result`.
- Given odmowę foreground po Alt i `AttachThreadInput`, when `ActivateWindow`, then `CommandFailed{ForegroundDenied}`.
- Given `MEDIASTATION_DEV_HARNESS=1` i sample `apps.json`, when OK na kafelku `web`, then okno Edge jest na pierwszym planie (log `appWindowAttached` z `rootPid`, `windowCommand ActivateWindow completed`), a cztery kolejne Home dają shell na pierwszym planie i okno Edge z `IsIconic` i `IsWindowVisible` prawdziwymi (`MinimizeWindow` i `ShowShell` `completed`).
- Given brak zmiennej, when OK na kafelku, then log `startWithoutHost` jak dotąd.

## Implementation Notes

- Ran on UM760 (host MediaStation, board F7BSI). Subagent implemented; new files in `Platform/Window/` (`IWindowApi`, `WindowCommandExecutor`, `WindowTreePicker`, `WindowInfo`, `Win32WindowApi`, `Win32WindowManagerPort`, `Win32ProcessSnapshot`) and `Hosts/DevHarness/` (`DevHarnessWebHostPort`, `HarnessBrowser`); `LogWindowManagerPort` kept but unused in main mode. `TeardownSequence` disposes the WindowManager.
- Live check on UM760 (Release publish, `MEDIASTATION_DEV_HARNESS=1`, keys injected by script): OK on `web` tile gave `appWindowAttached` (rootPid) and `windowCommand ActivateWindow completed`; 4 x Home gave shell foreground, Edge `IsIconic` and `IsWindowVisible` true, `MinimizeWindow` and `ShowShell` `completed` each time (4/4).

## Plan Change Log

## Review Triage Log

### 2026-10-26 — Review pass
- verdicts: ~60 findings across 4 lenses (counts approximate: lenses overlap heavily) — high 0, medium 9, low 6, false 5+, maybe-false 0
- findings:
  - `[medium]` `[patch]` thread-death / dispose / not-started WindowManager leaves commands without reply (blind, edge, gap) — `_dead` state, CommandFailed(Unexpected), drain on exit, dropped count logged
  - `[false]` `[reject]` Execute/_ready race strands queued command (blind) — `_ready` is set before the first Drain and Execute enqueues before reading it, so no lost wake-up
  - `[medium]` `[patch]` harness flags omit autoplay, add --new-window, URL env ignored (intent) — exact ticket flags, `MEDIASTATION_HARNESS_URL`
  - `[medium]` `[patch]` StopApp kills immediately, no ProcessExited (intent) — CloseMainWindow, wait, kill tree, ProcessExited
  - `[medium]` `[patch]` ProbeExisting ignores live process by `--user-data-dir` (intent) — WMI command-line lookup, off loop thread
  - `[medium]` `[patch]` Stop mid-start, duplicate start orphaning, timeout removing foreign session, window-wait ignoring exit (blind, edge) — fixed in StartCore
  - `[medium]` `[patch]` still-iconic after restore reports completed (edge, gap) — Unexpected
  - `[low]` `[patch]` exception-path log lacks hwnd; test gaps (teardown slot, restore poll, ShowAsync false, dead shell hwnd, dead test line) — fixed
  - `[false]` `[reject]` contract/TS refresh missing from diff (blind, intent) — `web/contracts` is gitignored; regenerated files contain `windowNotFound`/`foregroundDenied`
  - `[low]` `[reject]` plan bookkeeping (checkboxes, NtQuery wording), app.Id path traversal, duplicate Id in dev-only harness, pid reuse, EnumWindows callback guards, verify foreground root after SetForegroundWindow, Alt-simulation retry when injection fails, hidden-window Show, Win32 adapters untested — dev-only or unreachable in everyday use and fix adds guards; Win32 adapters covered by the live run on UM760
  - `[low]` `[reject]` ShowShell handle fixed at creation (edge) — shell window is never recreated in this app

## Auto Run Result

Status: built. Ran on UM760 (host MediaStation, board F7BSI).

- Summary: real WindowManager (own thread, executor over `IWindowApi`, unlock direct -> Alt -> AttachThreadInput), new `ErrorCode`s `WindowNotFound`/`ForegroundDenied`, and dev harness web host behind `MEDIASTATION_DEV_HARNESS=1`.
- Files: `src/MediaStation.App/Platform/Window/*` (executor, picker, Win32 API/port/snapshot), `Hosts/DevHarness/*`, `Platform/System/ProcessCommandLines.cs`, `CompositionRoot.cs`, `TeardownSequence.cs`, `NativeMethods.txt`, `MediaStation.App.csproj` (System.Management), `Messages.cs`, tests.
- Review: patches applied for all medium findings above; nothing deferred; rejected rows listed in the log.
- Verification: `dotnet test` 1104/1104 passed; Release publish OK; live run on UM760 with harness and script-injected keys: OK on `web` tile -> `appWindowAttached` + `ActivateWindow completed`, 4 x Home -> shell foreground, Edge `IsIconic` and `IsWindowVisible` true, `MinimizeWindow`/`ShowShell` completed (4/4, twice, before and after patches). Without the variable behaviour is the unchanged `MissingHostPort` path (unit-tested, not re-run live).
- Residual risks: harness Probe/graceful Stop not exercised live; Win32 adapters have no unit tests; `ForegroundDenied` real-world frequency unmeasured. followup_review_recommended: false.

## Design Notes

Decyzje (mapowanie komendy na wywołania, kolejność odblokowania: bezpośrednie `SetForegroundWindow` → Alt+retry → `AttachThreadInput`+retry, wybór okna drzewa) są czystymi funkcjami nad małymi rekordami (`WindowInfo{hwnd, pid, className, visible, hasOwner}`), a `IWindowApi` to jedyna granica do Win32. Drzewo procesów harnessu: PID startu + potomkowie z `Win32_Process`/`NtQuery` (App, nie Core).

## Verification

**Commands:**
- `dotnet test` (całe rozwiązanie) -- expected: zielone, w tym nowe testy, build odświeża schematy/TS bez różnic poza `ErrorCode`
- `powershell -NoProfile -Command "$env:MEDIASTATION_DEV_HARNESS='1'; ..."` uruchomienie Release `MediaStation.exe` na UM760, log w `%LOCALAPPDATA%\MediaStation\logs` -- expected: asercje z AC

**Manual checks (if no CLI):**
- Bez fizycznego pilota: Home wstrzyknięty skryptem (np. `SendInput` Home albo `tools/check-home-s10.ps1` celem `edge`); brak sprzętu → wskazać dowód z logu.
