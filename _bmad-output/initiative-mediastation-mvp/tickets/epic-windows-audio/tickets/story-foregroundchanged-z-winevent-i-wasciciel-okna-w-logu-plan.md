---
title: 'ForegroundChanged z WinEvent i właściciel okna w logu'
type: 'feature'
ticket: '4'
created: '2026-10-27'
status: done
baseline_revision: 'b955fc41644b81f1569c1fa04210efe9c8aed02c'
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

**Problem:** Nic nie raportuje `ForegroundChanged`: WindowManager nie ma hooka, a pętla Orchestratora loguje tylko surowy `hwnd`, więc log nie mówi, czy na pierwszym planie jest shell, aplikacja czy obce okno.

**Approach:** WindowManager rejestruje na swoim wątku `SetWinEventHook(EVENT_SYSTEM_FOREGROUND, WINEVENT_OUTOFCONTEXT)` i wysyła `ForegroundChanged{ hwnd }` z oknem `GA_ROOT`; Orchestrator rozpoznaje właściciela czystą funkcją w Core (shell; aplikacja po `hwnd`, potem po `rootPid`; obce) i oddaje go pętli, która loguje jeden wpis `foregroundChanged{ hwnd, owner, appId }`. Tryb, stan aplikacji, komendy i dźwięk bez zmian (AD-8).

## Boundaries & Constraints

**Always:** hook i jego callback żyją na wątku WindowManagera z pętlą komunikatów (AD-14); uchwyt shella i PID okna wchodzą do Core przez `IForegroundLookup` (Core nie zna Win32); `ForegroundChanged` zostaje `{ hwnd }` (AD-15); jedno zdarzenie to jeden wpis w logu; wykonawca nie rzuca (AD-4).

**Never:** zmiana trybu, stanu, snapshotu, komend lub dźwięku po `ForegroundChanged`; powrót do shella po Alt+Tab (to Home); zmiana `AudioRouter`.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Shell | `hwnd` == uchwyt shella | `owner: shell`, `appId: null` | — |
| Aplikacja po hwnd | `hwnd` == `Hwnd` aplikacji | `owner: app`, `appId` | — |
| Aplikacja po rootPid | inny `hwnd`, PID == `RootPid` | `owner: app`, `appId` | PID 0 nie pasuje |
| Obce | nieznany `hwnd` i PID | `owner: foreign` | — |
| Brak okna | `hwnd` 0 | `owner: foreign` | — |
| Brak lookupu | Orchestrator bez `IForegroundLookup` | `owner: foreign` | — |
| Wpływ na stan | dowolne `ForegroundChanged` | brak komend, snapshotu i Actions | — |

</intent-contract>

## Code Map

- `src/MediaStation.Core/Orchestrator/ForegroundOwnerResolver.cs` -- `ForegroundOwner`, `ForegroundObservation`, `IForegroundLookup`, czysta `Resolve`
- `src/MediaStation.Core/Orchestrator/Orchestrator.Foreground.cs` -- `OnForegroundChanged`; `Orchestrator.cs` -- ctor z opcjonalnym lookupem, `OrchestratorResult.Foreground`
- `src/MediaStation.App/Orchestrator/OrchestratorLoop.cs` -- jedyny wpis `foregroundChanged` z wyniku Orchestratora
- `src/MediaStation.App/Platform/Window/WinEventForegroundHook.cs`, `Win32WindowManagerPort.cs` -- hook na wątku WindowManagera; `Win32ForegroundLookup.cs` -- shell z `ManagedWindows.Shell`, PID z `GetWindowThreadProcessId`; `CompositionRoot.cs` -- wiring; `NativeMethods.txt` -- nazwy CsWin32
- Testy: `ForegroundOwnerTests.cs`, `OrchestratorLoopTests.cs`

## Tasks & Acceptance

**Execution:**
- [x] Core: resolver, `OnForegroundChanged`, `OrchestratorResult.Foreground`
- [x] App: hook WinEvent, lookup, wpis w pętli, wiring
- [x] Testy rozpoznania właściciela i braku wpływu na stan

**Acceptance Criteria:**
- Given każdy wiersz macierzy, when `ForegroundChanged`, then wynik ma właściciela z macierzy i brak komend/snapshotu (`dotnet test`).
- Given UM760 z dev harnessem, when Alt+Tab na Edge, Notatnik i Home, then log ma `foregroundChanged` z `owner` app/foreign/shell, a stan i dźwięk bez zmian.

## Implementation Notes

- Implementacja w głównej sesji (bez subagenta): zmiana mała i przekrojowa. Ran on UM760 (host MediaStation, board F7BSI).
- `ForegroundChanged` zostaje `{ hwnd }` (zgodnie z AD-15); PID okna i uchwyt shella dostarcza `IForegroundLookup`, bo Core ich nie zna. Zapytanie o PID idzie z wątku Orchestratora (`GetWindowThreadProcessId`), więc dla okna już zniszczonego daje 0 i rozpoznanie spada do `hwnd`.
- Hook nie zgłasza początkowego okna, tylko zmiany.
- Live (Release publish, `MEDIASTATION_DEV_HARNESS=1`, klawisze i Alt+Tab wstrzyknięte `keybd_event`): OK na `netflix` -> `appWindowAttached` + `foregroundChanged owner:app appId:netflix`; Home -> `MinimizeWindow`, `ShowShell`, `foregroundChanged owner:shell`; Alt+Tab na Edge -> `owner:app` bez żadnej komendy okna/audio; Notatnik -> `owner:foreign`. Przełącznik Alt+Tab to osobne okna systemowe (hwnd 65924, 67202, 65978), więc przed ostatnim wpisem `owner:app` pojawiają się wpisy `foreign` (każda zmiana okna to osobne zdarzenie, tak jak chce R3). Szczegóły w `Auto Run Result`.

## Plan Change Log

## Review Triage Log

### 2026-10-27 — Review pass
- verdicts: 15 findings across 4 lenses (intent-alignment was descriptive, no defect claimed) — high 0, medium 0, low 3, false 3, maybe-false 0, rejected-low 9 (grouped below)
- findings:
  - `[low]` `[patch]` no-lookup doc says every window is foreign, yet an app `hwnd` still matches (edge) — constructor doc corrected; matrix row stays valid for unknown windows
  - `[low]` `[patch]` loop log mapping tested only for `foreign` (gap, blind) — test `Foreground_changed_log_names_shell_and_app_owners` added with a fake lookup
  - `[false]` `[reject]` `pid` uninitialized when `GetWindowThreadProcessId` fails (blind) — the ternary returns 0 on failure and never reads it; same pattern as `Win32ForegroundWindow`
  - `[false]` `[reject]` callback after `Dispose` posts to a dead loop (edge) — hook is unhooked on its own thread in `finally` before the port is marked dead, and `OrchestratorLoop.Post` is safe after `Dispose`
  - `[false]` `[reject]` stale `Hwnd`/`RootPid` of a stopped app mislabels a reused PID (edge) — `Hwnd` is null and `RootPid` zeroed after `ForgetProcessTree` (story 5.3)
  - `[low]` `[reject]` PID looked up on the Orchestrator thread can be stale (blind, edge, intent) — a destroyed window gives 0 and falls back to `hwnd`; carrying the PID needs a contract change that AD-15 (`ForegroundChanged{ hwnd }`) and the ticket exclude
  - `[low]` `[reject]` `rootPid` match ignores the process tree, apps sharing a process (blind) — the ticket specifies "po `rootPid` jak w bramce AD-8", the same rule as `ManagedWindows.Contains`
  - `[low]` `[reject]` shell with unset handle logs `foreign`; `hwnd` 0 test only (blind) — by design: the handle is set before the hook starts
  - `[low]` `[reject]` hook and lookup have no unit tests (blind, gap) — Win32 code with no seam; covered by the live run in Implementation Notes
  - `[low]` `[reject]` no `WINEVENT_SKIPOWNPROCESS`, Alt+Tab switcher windows add `foreign` entries (blind, intent) — the ticket wants own-command changes reported; each change is one entry
  - `[low]` `[reject]` per-event list allocation, magic hwnds in tests, `_ => "foreign"` default, one-shot hook failure warn, no initial foreground report, loop log depends on `Handle` not throwing (blind, edge) — cosmetic or speculative; fixes add branches
  - `[low]` `[reject]` acceptance/process wording, empty triage log, indirect state assertion (blind) — review had not run when the finding was filed; the state test asserts empty commands, snapshot and actions
  - `carried` intent readings A-E — descriptive; live-harness clause run on UM760 (see Implementation Notes)

## Verification

**Commands:**
- `dotnet test` -- expected: zielone, w tym `ForegroundOwnerTests`
- Release publish, `MEDIASTATION_DEV_HARNESS=1`: OK na `web`, Home, Alt+Tab na Edge i Notatnik -- expected: wpisy `foregroundChanged` z `owner`

## Auto Run Result

Status: built. Ran on UM760 (host MediaStation, board F7BSI; `COMPUTERNAME` output `MEDIASTATION`).

- Summary: `WinEventForegroundHook` on the WindowManager thread posts `ForegroundChanged{ hwnd }` (`GA_ROOT`) for every foreground change; the Orchestrator classifies the owner with the pure `ForegroundOwnerResolver` (shell, app by `hwnd` then `rootPid`, foreign) and the loop logs one `foregroundChanged{ hwnd, owner, appId }`; no commands, snapshot, state or sound change.
- Files: `Core/Orchestrator/ForegroundOwnerResolver.cs`, `Orchestrator.Foreground.cs`, `Orchestrator.cs`, `Core/Input/ManagedWindows.cs` (`Shell` getter), `Core/Contracts/Messages.cs` (doc), `App/Orchestrator/OrchestratorLoop.cs`, `App/Platform/Window/WinEventForegroundHook.cs`, `Win32ForegroundLookup.cs`, `Win32WindowManagerPort.cs`, `App/CompositionRoot.cs`, `NativeMethods.txt`, tests `ForegroundOwnerTests.cs`, `OrchestratorLoopTests.cs`.
- Review: 4 lenses; patches: 2 low (lookup doc, loop test for shell/app branches); nothing deferred; rejected rows with reasons in the triage log.
- Follow-up review recommendation: false (patched: high 0, medium 0, low 2).
- Verification: `dotnet test` 1176/1176 on UM760. Live on UM760 (Release publish, `MEDIASTATION_DEV_HARNESS=1`, keys and Alt+Tab injected): OK -> `owner:app appId:netflix`; Home -> `MinimizeWindow`, `ShowShell` completed, `owner:shell`; Alt+Tab to Edge -> `owner:app` with no window or audio command; Notepad -> `owner:foreign`. The Alt+Tab switcher windows log their own `foreign` entries before the final `owner:app`.
- Residual risks: the PID is read on the Orchestrator thread, so a window destroyed before dequeue falls back to `hwnd` matching; the hook and lookup have no unit test (Win32 only).
