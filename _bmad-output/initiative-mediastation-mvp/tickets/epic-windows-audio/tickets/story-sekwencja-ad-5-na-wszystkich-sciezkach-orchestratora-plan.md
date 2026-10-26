---
title: 'Sekwencja AD-5 na wszystkich ścieżkach Orchestratora'
type: 'feature'
ticket: '3'
created: '2026-10-26'
status: done
baseline_revision: '98b63af5b36ab5fe150893437a1ee44e86562f3b'
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

**Problem:** Mute działa tylko na Home i aktywacji. Wybudzenie, `Failed` z pierwszego planu, adopcja i okno spóźnione po Home w `Starting` minimalizują okno bez wyciszenia, a `CloseApp`, `RestartApp` i `ProcessExited` zostawiają regułę drzewa w AudioRouterze, więc nowy proces z tym samym PID byłby wyciszony.

**Approach:** Orchestrator wysyła `MuteProcessTree` przed `MinimizeWindow` na każdej ścieżce opuszczenia/ukrycia z żywym oknem i `ForgetProcessTree{ cmdId, rootPid }` po końcu procesu; AudioRouter dostaje komendę `ForgetProcessTree` (usuwa regułę korzenia i odcisza to, co wyciszył). Wynik komendy ze starego `transitionId` nadal nie zmienia stanu.

## Boundaries & Constraints

**Always:** kolejność AD-5: opuszczenie `MuteProcessTree` → `MinimizeWindow` (→ `ShowShell` → routing), aktywacja `ActivateWindow` → `UnmuteProcessTree` → routing; `ForgetProcessTree` to `AudioCommand` (trafia do `IAudioRouterPort` przez istniejący `CommandRouter`), idzie do `MessageTypes` i schematów (build odświeża `web/contracts`, bez ręcznej edycji, AD-16); `ForgetProcessTree` wysyłany dopiero po potwierdzonym końcu procesu (stop potwierdzony `CommandCompleted`/`ProcessExited`, `ProcessExited` w żywym cyklu), nigdy przed `StopApp`, żeby proces w trakcie zamykania pozostał wyciszony; reguła i `rootPid` pochodzą tylko ze stanu aplikacji (AD-15); `rootPid` aplikacji zerowany po wysłaniu `ForgetProcessTree`, nie przy `Hwnd = null`; wykonawca nie rzuca (AD-4); `CommandCompleted`/`CommandFailed` komendy okiennej lub audio nie zmienia stanu ani nie wysyła komend.

**Never:** bez kroku `pause` strony (epic-netflix-dpad); bez zmian `WindowManager`; bez `ForgetProcessTree` w `Starting` bez znanego `rootPid`; bez zmiany polityki `AppWindowAttached` po `Failed` (DW-4); bez `bmad-architecture`.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Home | `Foreground` | `[Mute, Minimize, ShowShell, routing]` (bez zmian) | — |
| Wybudzenie | `SystemResumed`, `Foreground` | `[Mute, Minimize, ShowShell, routing, RenewSession…]` | — |
| Failed z pierwszego planu, proces żyje | `CdpLost` albo `CommandFailed` startu w `Foreground` | `[Mute, Minimize, ShowShell, routing, (cancel)]`, `Failed`, `rootPid` zostaje | — |
| Failed, proces zginął | `ProcessExited` w `Foreground` | `[Forget, ShowShell, routing, (cancel)]` | `Hwnd` i `rootPid` zerowane |
| `ProcessExited`/`CdpLost` w tle, `Starting` po attach | `Background` | `ProcessExited`: `[Forget]`; `CdpLost`: bez komend | — |
| Start z `NotRunning` po attach | `Starting`, bieżący `transitionId` | `[Activate, Unmute, routing, (cancel)]` (bez zmian) | — |
| Powrót z `Background` | `LaunchApp` | `[Activate, Unmute, routing]` (bez zmian) | — |
| Adopcja | `AppWindowAttached` w `NotRunning` | `[Mute, Minimize, ShowShell?]`; `ShowShell` pominięty, gdy inna aplikacja aktywna | — |
| Okno spóźnione po Home | attach w `Starting`, nowsza decyzja | `[Mute, Minimize, (cancel)]`, `Background`, bez `Activate` | — |
| Close / Restart | stop potwierdzony (`CommandCompleted` lub `ProcessExited`) | `[Forget, …]`, restart: `[Forget, StartApp, timer, cancel]`; `rootPid = 0` | stop bez znanego `rootPid`: bez `Forget` |
| Wynik starego `transitionId` | `CommandCompleted`/`CommandFailed` okna/audio | brak komend, brak snapshotu, stan bez zmian | — |

</intent-contract>

## Code Map

- `src/MediaStation.Core/Contracts/AppCommands.cs` -- dopisać `ForgetProcessTree(CmdId, RootPid) : AudioCommand`; `MessageTypes.cs` -- rejestr; `tests/.../CoreMessageContractTests.cs`, `CommandRouterTests.cs` -- jak `MuteProcessTree`
- `src/MediaStation.Core/Orchestrator/Orchestrator.cs` -- `AppState.RootPid` (komentarz: ważny dopóki nie wysłano `ForgetProcessTree`, niezależnie od `Hwnd`); helper `ForgetRule(app)` zwracający `ForgetProcessTree?` i zerujący `RootPid`
- `Orchestrator.Startup.cs` (`OnSystemResumed`) -- dodać `MuteProcessTree` przed `MinimizeWindow`
- `Orchestrator.Attach.cs` (`OnAdopt`, kompensacja w `OnAppWindowAttached`) -- `MuteProcessTree` przed `MinimizeWindow`; poprawić komentarze „bez MuteProcessTree (story 3)”
- `Orchestrator.Failures.cs` (`EnterFailed`, `OnProcessExited`, `OnCdpLost`, `OnCommandFailed`) -- parametr „proces żyje”; `Failed` z `Foreground` przy żywym procesie: Mute+Minimize przed `ShowShell` (hwnd i rootPid zapamiętane przed `Hwnd = null`); `ProcessExited` w `Foreground`/`Background`/`Starting` po attach: `ForgetProcessTree` (przed `ShowShell`)
- `Orchestrator.Stop.cs` (`OnStopped`) -- `ForgetProcessTree` pierwszy w obu gałęziach (zamknięcie i restart; przy restarcie przed `StartApp`); `OnStop` nie wysyła `Forget`
- `Orchestrator.Home.cs`, `Orchestrator.Launch.cs` -- już mają Mute/Unmute; tylko testy
- `src/MediaStation.App/Platform/Audio/AudioRule.cs` (`Remove`), `AudioRouterEngine.cs` (`Execute`: `ForgetProcessTree` → usuń regułę, `Sweep` odciszy wpisy księgi, log `audioRuleForgotten{ rootPid }`; `rootPid <= 0` jak dotąd); `Win32AudioRouterPort.cs` bez zmian (dispatch po `AudioCommand`)
- Testy: `OrchestratorHomeTests`, `OrchestratorLifecycleTests`, `OrchestratorAttachTests`, `OrchestratorFailureTests`, `OrchestratorStopTests`, `OrchestratorStartupTests`, `OrchestratorLoopTests` (komendy z `Mute` w oczekiwanych listach), `AudioRouterEngineTests`, `OrchestratorTestSupport.cs`
- Deferred ze story 2 (sesje wyciszone przez zabity MediaStation): rozstrzyga adopcja, bo `MuteProcessTree` po adopcji zapisuje już wyciszone sesje w księdze, a późniejszy `UnmuteProcessTree` je odcisza; opisać w Implementation Notes

## Tasks & Acceptance

**Execution:**
- [ ] `AppCommands.cs`, `MessageTypes.cs`, testy kontraktu/routera -- `ForgetProcessTree`
- [ ] `Orchestrator*.cs` -- ścieżki z macierzy; `AppState.RootPid` i helper `ForgetRule`
- [ ] `AudioRule.cs`, `AudioRouterEngine.cs` -- obsługa `ForgetProcessTree` + testy (usuwa regułę, odcisza wpisy księgi, nowy proces z tym PID niewyciszony)
- [ ] testy Orchestratora -- kolejność komend dla każdej ścieżki z macierzy oraz „stary `transitionId`: `CommandCompleted`/`CommandFailed` okna i audio nie zmieniają stanu”

**Acceptance Criteria:**
- Given każda ścieżka z macierzy, when zdarzenie, then komendy mają zapisaną kolejność AD-5 (`dotnet test`).
- Given wynik komendy okiennej/audio ze starego `transitionId`, when `CommandCompleted` lub `CommandFailed`, then brak komend, snapshotu i zmiany stanu.
- Given UM760 z dev harnessem i żywym Edge, when restart MediaStation, then log ma adopcję (`appWindowAttached`), `audioRuleApplied muted:true`, `windowCommand MinimizeWindow completed`, a shell zostaje na pierwszym planie.

## Implementation Notes

- Zweryfikowano na UM760 (host MediaStation, plyta F7BSI): `dotnet test` zielone, w tym nowe testy ForgetProcessTree (kontrakt, router, silnik, Orchestrator) i test starego transitionId.
- Deferred ze story 2 (sesje wyciszone przez zabity MediaStation): rozstrzyga adopcja. `MuteProcessTree` po adopcji zapisuje juz wyciszone sesje w ksiedze silnika, wiec pozniejszy `UnmuteProcessTree` je odcisza.
- `ForgetProcessTree` wysylany tylko po potwierdzonym koncu procesu (OnStopped, ProcessExited w zywym cyklu); Failed przy zywym procesie zostawia regule i `RootPid`. Timeout Starting/Stopping wchodzi w Failed jak przy zywym procesie.
- Live na UM760 (Release publish, `MEDIASTATION_DEV_HARNESS=1`, klawisze wstrzyknięte skryptem): OK na kafelku `netflix` -> `appWindowAttached` (rootPid 6664) + `ActivateWindow completed` + `audioRuleApplied muted:false`; Home -> `MinimizeWindow`, `ShowShell` completed, `audioRuleApplied muted:true`; zabity tylko MediaStation i uruchomiony ponownie przy żywym Edge: `appWindowAttached` (adopcja, ten sam rootPid), `windowCommand MinimizeWindow completed`, `ShowShell completed`, `audioRuleApplied muted:true`; shell na pierwszym planie, okno Edge `IsIconic` true.
- Po review: `ProcessExited` w `Failed` z zachowaną regułą wysyła `ForgetProcessTree` (bez snapshotu); test silnika dla `Forget` przy niedostępnym audio.


## Plan Change Log

## Review Triage Log

### 2026-10-26 — Review pass
- verdicts: 29 findings across 4 lenses (counts approximate: lenses overlap) — high 0, medium 0, low 2, false 5, maybe-false 0, rejected-low 22 (grouped below)
- findings:
  - `[low]` `[patch]` `ProcessExited` in `Failed` with a kept rule is ignored, rule and `RootPid` linger (edge) — `OnProcessExited` now sends `ForgetProcessTree` without snapshot; tests added, `Stop_result_after_stop_timeout_is_ignored` adjusted
  - `[low]` `[patch]` `Forget` with unavailable backend untested (gap, blind, edge) — ledger entries stay and the 1 s poll restores them once the rule is gone, so no leak; test `Forget_with_unavailable_backend_fails_but_drops_the_rule_for_the_next_sweep` pins it
  - `[false]` `[reject]` schema file missing from diff (blind, intent) — `web/contracts` is gitignored and regenerated by the build
  - `[false]` `[reject]` `RootPid` unset in late-attach compensation (blind, edge) — `app.RootPid = attached.RootPid` runs before the branch
  - `[false]` `[reject]` Forget failure leaves sessions muted forever (blind, edge) — `HasMutedSessions` keeps the poll running and `Restore` runs once the rule is gone
  - `[false]` `[reject]` Mute skipped for live Failed in Starting/Background (blind, edge, intent) — Background is already muted and minimized; Starting has no known window or rootPid
  - `[false]` `[reject]` stale-transition test proves nothing (blind) — it feeds ids of Home/Forget commands issued before newer decisions and asserts no commands, no snapshot, state unchanged
  - `[low]` `[reject]` `audioRuleForgotten` logged when no rule existed, `AudioRule.Remove` untested directly, hand-renumbered cmdIds, `Mute` with rootPid 0 logs `audioRuleIgnored` (blind, edge, gap) — cosmetic or unreachable (Foreground always has attach rootPid); fix adds branches
  - `[low]` `[reject]` Forget and `StartApp` go to different executors, PID could be reused first (blind, edge) — old root already dead and the engine prunes dead roots every sweep; reuse in that window is speculative
  - `[low]` `[reject]` timeouts treated as live process and keep the rule (intent) — descriptive; rule is removed by the later stop or `ProcessExited`
  - `carried` intent-lens readings A-E — descriptive, no defect claimed; live-harness clause was run on UM760 (see Implementation Notes)

## Design Notes

`ForgetProcessTree` po potwierdzonym końcu, nie przy intencji: wyciszona reguła do końca życia procesu chroni przed dźwiękiem z zamykanej przeglądarki; PID wraca do puli dopiero po śmierci procesu, a `Forget` idzie przed `StartApp` restartu. `Failed` przy żywym procesie zostawia regułę (proces dalej wyciszony); wyjście z `Failed` jest tylko przez stop, który ją usuwa.

## Verification

**Commands:**
- `dotnet test` -- expected: zielone, w tym nowe testy; build odświeża schematy/TS (`forget-process-tree`)
- Release publish, `MEDIASTATION_DEV_HARNESS=1`: OK na `web` (Edge), zabić tylko MediaStation, uruchomić ponownie -- expected: w logu `appWindowAttached`, `audioRuleApplied muted:true`, `windowCommand MinimizeWindow completed`, shell na pierwszym planie

## Auto Run Result

Status: built. Ran on UM760 (host MediaStation, board F7BSI).

- Summary: new `ForgetProcessTree` command (contract, schema, AudioRouter engine); AD-5 order on wake, adoption, late window after Home and `Failed` from foreground with a live process; `ForgetProcessTree` after a confirmed stop (Close/Restart, before `StartApp`) and on `ProcessExited`.
- Files: `Core/Contracts/AppCommands.cs`, `MessageTypes.cs`, `Core/Orchestrator/Orchestrator*.cs` (Attach, Failures, Startup, Stop, Timers, main), `App/Platform/Audio/AudioRule.cs`, `AudioRouterEngine.cs`, tests in `tests/MediaStation.Core.Tests`.
- Review: 4 lenses; patches: 2 low (Forget on `ProcessExited` in `Failed`, test of Forget with unavailable audio); nothing deferred; rejected rows with reasons in the triage log. Story 2's deferred item (sessions muted by a killed MediaStation) resolved by adoption `MuteProcessTree`.
- Follow-up review recommendation: false (patched: high 0, medium 0, low 2).
- Verification: `dotnet test` 1166/1166 on UM760; live run on UM760 with dev harness: restart of MediaStation with live Edge gave adoption, `audioRuleApplied muted:true`, `MinimizeWindow completed`, `ShowShell completed`, shell foreground, Edge iconic.
- Residual risks: Forget and `StartApp` are dispatched to different threads without ordering guarantee; Win32 port thread loop still has no unit test.
