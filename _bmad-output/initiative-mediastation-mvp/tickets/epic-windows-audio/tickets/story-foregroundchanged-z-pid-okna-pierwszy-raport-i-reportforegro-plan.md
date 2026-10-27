---
title: 'ForegroundChanged z PID okna, pierwszy raport i ReportForeground'
type: 'feature'
ticket: '11'
created: '2026-10-27'
status: done
baseline_revision: '25d3487b8777a43c12f3ad1d63bcfa93e0ae4757'
route: 'full'
route_source: 'auto'
risk: 'medium'
review: 'thorough'
review_source: 'pinned'
lenses_ran: ['blind-hunter', 'edge-case-hunter', 'verification-gap', 'intent-alignment']
review_loop_iteration: 0
followup_review_recommended: false
context: []
warnings: []
deferred: []
---

<intent-contract>

## Intent

**Problem:** `ForegroundChanged` niesie tylko `hwnd`, a PID okna czyta Orchestrator przez `IForegroundLookup.ProcessIdOf` (Win32 na jego wątku, wbrew AD-14/AD-15). Nikt nie raportuje okna na wierzchu po starcie haka, a Orchestrator nie ma jak poprosić o świeżą obserwację.

**Approach:** `ForegroundChanged{ hwnd, pid }`: WindowManager czyta PID w callbacku haka (GA_ROOT) i raz po starcie haka raportuje bieżące okno. Nowa komenda `ReportForeground{ cmdId }` daje świeże `ForegroundChanged` i `CommandCompleted`. Rozpoznanie właściciela bierze `pid` z kontraktu; wpis logu niesie `pid`. Orchestrator po `ForegroundChanged` nadal tylko loguje.

## Boundaries & Constraints

**Always:** Orchestrator nie woła Win32 (AD-14); odczyt PID i okna na wierzchu tylko na wątku WindowManagera; nowe typy w `MessageTypes` (schematy generowane, AD-16); wykonawca nie rzuca (AD-4); `MinimizeWindow` zostaje `SW_SHOWMINNOACTIVE`.

**Never:** zmiana trybu, stanu aplikacji, dźwięku ani komend po `ForegroundChanged`; polityka Alt+Tab, timer `foregroundSettleMs` i wysyłanie `ReportForeground` przez Orchestrator (story 9); aktywacja innego okna przy minimalizacji.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Shell | `ForegroundChanged(hwnd=shell, pid=any)` | owner `shell`, log z `pid` | — |
| App po hwnd | `hwnd` = `AppWindowAttached.hwnd` | owner `app`, `appId` | — |
| App po pid | `hwnd` obcy, `pid` = `rootPid` | owner `app` | — |
| Obce | `hwnd` obcy, `pid` spoza aplikacji | owner `foreign` | — |
| Brak okna | `hwnd` 0 | owner `foreign` | — |
| ReportForeground | okno na wierzchu (hwnd, pid) | jedno `ForegroundChanged(hwnd, pid)`, potem `CommandCompleted(cmdId)` | — |
| ReportForeground bez okna | `GetForegroundWindow` puste | `ForegroundChanged(0, 0)`, `CommandCompleted` | — |
| Start haka | WindowManager wystartował | jedno `ForegroundChanged` z bieżącym oknem | błąd rejestracji haka: log, bez rzucania |

</intent-contract>

## Code Map

- `src/MediaStation.Core/Contracts/Messages.cs:98` -- `ForegroundChanged(long Hwnd)` → dodać `int Pid`, doc.
- `src/MediaStation.Core/Contracts/AppCommands.cs:45` -- dodać `ReportForeground(long CmdId) : WindowCommand`; router i port już kierują `WindowCommand`.
- `src/MediaStation.Core/Contracts/MessageTypes.cs` -- dopisać `typeof(ReportForeground)` po `ShowShell`.
- `src/MediaStation.Core/Orchestrator/ForegroundOwnerResolver.cs` -- `ForegroundObservation` + `Pid`; z `IForegroundLookup` usunąć `ProcessIdOf`.
- `src/MediaStation.Core/Orchestrator/Orchestrator.Foreground.cs` -- użyć `changed.Pid`, zero Win32.
- `src/MediaStation.App/Orchestrator/OrchestratorLoop.cs:133` -- wpis `foregroundChanged` dostaje `pid`.
- `src/MediaStation.App/Platform/Window/IWindowApi.cs`, `Win32WindowApi.cs` -- `GetForeground()` zwraca okno GA_ROOT i jego PID; statyczny `Describe(HWND)` współdzielony z hakiem.
- `src/MediaStation.App/Platform/Window/WinEventForegroundHook.cs` -- `pid` w callbacku; `ReportCurrent()`.
- `src/MediaStation.App/Platform/Window/Win32WindowManagerPort.cs:176` -- po `foregroundHook.Start()` wywołać `ReportCurrent()`.
- `src/MediaStation.App/Platform/Window/WindowCommandExecutor.cs` -- obsługa `ReportForeground`: post `ForegroundChanged`, potem standardowy `CommandCompleted`.
- `src/MediaStation.App/Platform/Window/Win32ForegroundLookup.cs` -- zostaje tylko `ShellHwnd`.
- Testy: `CoreMessageContractTests.cs` (envelope, rejestr, schematy), `ForegroundOwnerTests.cs`, `OrchestratorLoopTests.cs`, `WindowCommandExecutorTests.cs` (`FakeWindowApi`), `LogWindowManagerPortTests.cs`.

## Tasks & Acceptance

**Execution:**
- [x] `Contracts/Messages.cs`, `AppCommands.cs`, `MessageTypes.cs` -- `pid` w `ForegroundChanged`, komenda `ReportForeground`, rejestr -- kontrakt i schematy
- [x] `Orchestrator/ForegroundOwnerResolver.cs`, `Orchestrator.Foreground.cs`, `OrchestratorLoop.cs` -- PID z kontraktu, `Pid` w obserwacji i logu -- AD-14
- [x] `Platform/Window/*` (api, hak, port, executor, lookup) -- PID w callbacku, pierwszy raport, `ReportForeground` -- AD-15
- [x] testy w `tests/MediaStation.Core.Tests` -- scenariusze z macierzy, envelope `{"hwnd":1234,"pid":..}`, `reportForeground`, schematy `foreground-changed` (`hwnd,pid`) i `report-foreground` (`cmdId`) -- weryfikacja

**Acceptance Criteria:**
- Given `ForegroundChanged` z `pid`, when Orchestrator rozpoznaje właściciela, then używa tego `pid` bez wywołań Win32 (shell, aplikacja po `hwnd`, po `rootPid`, obce, `hwnd` 0).
- Given `ReportForeground`, when WindowManager go wykona, then powstaje dokładnie jedno `ForegroundChanged` i `CommandCompleted` z tym `cmdId`, w tej kolejności.
- Given eksport schematów, when `dotnet test`, then schematy mają `pid` i komendę `ReportForeground`.
- Given dev harness na bieżącej maszynie, when start MediaStation, then log ma jeden `foregroundChanged` z `pid` shella, a Alt+Tab na Edge daje `owner:app` z `pid`; stan, tryb i dźwięk bez zmian.

## Implementation Notes

- Machine: ran on UM760 (host MediaStation, board F7BSI, model EliteMini Series).
- Done: `ForegroundChanged(Hwnd, Pid)`, `ReportForeground`, `ForegroundObservation.Pid`, `IForegroundLookup.ProcessIdOf` removed, `IWindowApi.GetForeground`, shared `Win32WindowApi.Describe`, `WinEventForegroundHook.ReportCurrent` after hook start, `pid` in the `foregroundChanged` log entry. Tests updated and added.
- Evidence (UM760): `dotnet build MediaStation.slnx` 0 warnings/errors; `dotnet test MediaStation.slnx` 1196/1196. Release publish of the working tree on top of `25d3487` (log entries carry `pid`, so the new code ran), `check-home-s10.ps1 -Restore -InjectHome -Targets edge -Presses 2` 2/2 PASS. Log: one `foregroundChanged` right after `start` (`hwnd` 131884, `pid` 18580, `owner:foreign`: the shell is shown without Activate, so the window on top at hook start was another one); Edge in front gives `foregroundChanged owner:app appId:restore-edge pid:18164` (= `rootPid`); shell gives `pid` 21928 (= the MediaStation process); only the harness's own `windowCommand`s appear, no extra commands.

## Plan Change Log

## Review Triage Log

### 2026-10-27 — Review pass
- verdicts: 17 findings — high 0, medium 0, low 3, false 10, maybe-false 4
- findings:
  - `[false]` `[reject]` Evidence from the wrong build (blind) — the published log entries carry `pid`, which the baseline code cannot write; wording clarified in Implementation Notes.
  - `[false]` `[reject]` Machine not identified (blind) — identified at the start of the run with the AGENTS.md command: MEDIASTATION / F7BSI / EliteMini Series = UM760.
  - `[false]` `[reject]` `ReportForeground` path can throw (blind) — `Execute` wraps `Run` in try/catch and answers `CommandFailed(Unexpected)`.
  - `[low]` `[reject]` No automated test for `ReportCurrent`, hook start report or `Describe` (blind, verification-gap) — need a live window handle; covered by the recorded harness run; adding a Win32 seam is out of proportion.
  - `[false]` `[reject]` `Describe` untested for pid 0 with a non-zero hwnd (blind) — `ForegroundOwnerTests` has the case `hwnd` set, `pid` 0 (resolver skips pid matching).
  - `[false]` `[reject]` Hook vs report disagree on null foreground (blind, edge) — intended: the hook ignores events without a window (unchanged), `ReportForeground` must answer with the current state, so `(0, 0)`; matrix row "ReportForeground bez okna".
  - `[false]` `[reject]` Envelope version / missing `pid` handling and schemas not in the diff (blind) — schemas are generated artefacts (git-ignored); `CoreMessageContractTests` pin them.
  - `[low]` `[patch]` `ForegroundChanged` doc comment stale (blind) — added the hook-start and `ReportForeground` sources to the summary in `Messages.cs`.
  - `[maybe-false]` `[reject]` Plan file unfinished: checkboxes, logs (blind) — finalized in this step; edits to the plan are not a code finding.
  - `[maybe-false]` `[reject]` Window destroyed between event and `Describe` gives non-zero hwnd with pid 0 (edge) — the resolver still matches shell and app by hwnd; foreign otherwise; negligible.
  - `[maybe-false]` `[reject]` `ReportForeground` log `hwnd` 0 instead of null (edge) — cosmetic.
  - `[false]` `[reject]` `ReportCurrent` after a failed hook registration posts a lone event (edge) — the failure is logged (`foregroundHookFailed`) and one observation is still the requested startup state.
  - `[low]` `[reject]` No test for a throwing `GetForeground` (edge) — generic `Execute` catch is covered by existing failure tests.
  - `[maybe-false]` `[reject]` AC 4 vs evidence: first report is `owner:foreign`, not the shell (edge, intent-alignment) — the shell is shown without Activate (epic Assumption), so at hook start another window is on top; the report is a faithful observation. Recorded under residual risks.
  - `[false]` `[reject]` `LogWindowManagerPortTests.cs` not updated (edge) — the log-only port logs any `WindowCommand`; no change needed.
  - `[false]` `[reject]` UM760 label vs whole-epic acceptance (intent-alignment) — AGENTS.md: per-ticket evidence on the current machine, which is UM760.
  - `[false]` `[reject]` Verification-gap lens: no gaps (pre-verified); win32-only parts noted, see above.

## Design Notes

Hak i `ReportForeground` używają tej samej funkcji `Describe(HWND)` (GA_ROOT + `GetWindowThreadProcessId`), więc obserwacja ma jedną definicję. Pierwszy raport idzie z wątku WindowManagera tuż po `Start()` haka, przed pętlą komunikatów.

## Verification

**Commands:**
- `dotnet build MediaStation.slnx` -- expected: 0 błędów
- `dotnet test MediaStation.slnx` -- expected: wszystkie testy przechodzą

**Manual checks:**
- `MEDIASTATION_DEV_HARNESS=1`, start Release: log ma jeden `foregroundChanged` z `pid`, Alt+Tab na Edge daje `owner:app`; stan i dźwięk bez zmian (UM760).

## Auto Run Result

Status: built

- Summary: `ForegroundChanged{ hwnd, pid }` (WindowManager reads the PID in the hook callback), one startup report right after the hook starts, new `ReportForeground{ cmdId }` command (`ForegroundChanged` then `CommandCompleted`), owner resolution uses the contract `pid`, the `foregroundChanged` log entry carries `pid`. The Orchestrator still only logs.
- Files: `Core/Contracts/{Messages,AppCommands,MessageTypes}.cs` (contract, registry); `Core/Orchestrator/{ForegroundOwnerResolver,Orchestrator.Foreground}.cs` (pid from contract, `ProcessIdOf` removed); `App/Orchestrator/OrchestratorLoop.cs` (`pid` in log); `App/Platform/Window/{IWindowApi,Win32WindowApi,WinEventForegroundHook,Win32WindowManagerPort,WindowCommandExecutor,Win32ForegroundLookup}.cs` (shared `Describe`, startup report, `ReportForeground`); tests in `CoreMessageContractTests`, `ForegroundOwnerTests`, `OrchestratorLoopTests`, `OrchestratorFailureTests`, `WindowCommandExecutorTests`.
- Review: 17 findings, 1 patched (low: stale `ForegroundChanged` doc comment), 0 deferred, 16 rejected with reasons in the Review Triage Log.
- Follow-up review recommended: false (no high/medium patches).
- Verification (UM760): `dotnet build MediaStation.slnx` 0 warnings/errors; `dotnet test MediaStation.slnx` 1196/1196 (rerun after the doc patch); Release publish + `tools/check-home-s10.ps1 -Restore -InjectHome -Targets edge -Presses 2` 2/2 PASS with the log assertions in Implementation Notes (UM760 whole-epic acceptance stays with the user).
- Residual risks: the startup `foregroundChanged` reports whatever window is on top at hook start (seen: a foreign window, `pid` 18580), not necessarily the shell, because the shell is shown without Activate; story 9 must not assume the first observation is the shell. `Describe`, `ReportCurrent` and `GetForeground` touch Win32 and are covered only by the harness run, not by unit tests.
