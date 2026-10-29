---
title: 'Story 6.7: J4: restart aplikacji Failed jednym OK'
type: 'feature'
ticket: '7'
created: '2026-10-29'
status: done
baseline_revision: '2a7f81e25d95ea184c0f01060731b44febe29556'
route: 'full'
route_source: 'auto'
risk: 'high'
review: 'thorough'
review_source: 'pinned'
lenses_ran: [blind-hunter, edge-case-hunter, verification-gap, intent-alignment]
review_loop_iteration: 0
followup_review_recommended: false
context: []
warnings: [oversized]
deferred: []
---

<intent-contract>

## Intent

**Problem:** The J4 path is already in place, but no test or live run has ever proved it with the real BrowserHost:
- the shell's AppMenu sends `restartApp` (`focus.ts`), the gate maps it to `RestartApp`, and the Orchestrator runs it as `Stopping` → `Starting` (`Orchestrator.Stop.cs`);
- no test or live run checks that BrowserHost's Stop and Start actually bring a `Failed` browser tile back to the foreground in one pass;
- nothing proves that a second failure just lands in `Failed` again.

On top of that, the log has no entry for app state changes, so the order `Failed → Stopping → Starting → Foreground` can be asserted in a live run only by guessing from side effects.

**Approach:**
- Add one generic log entry to the Orchestrator loop: `appStateChanged { appId, from, to, cause }`.
- Prove J4 with the real BrowserHost and the real Orchestrator in `dotnet test`: restart, the order of states, a failed restart and no escalation.
- Add a repeatable live smoke, `tools/check-browserhost-restart.ps1`. It drives OK → AppMenu → OK on the published build. A temporary `v:2` patch of the published `page\runtime.js` makes the tile fail deterministically while its process stays alive (the 6.6 `PageVersionRejected` path).

## Boundaries & Constraints

**Always:**
- **`appStateChanged` (`OrchestratorLoop`):**
  - The loop keeps the last known state per `appId`. After every `Handle` whose result has a snapshot, it logs `info orchestrator appStateChanged { appId, from, to, cause }` for each app whose state differs from the last known one.
  - `cause` is `message.GetType().Name`, for example `RestartApp`, `ProcessExited`, `AppWindowAttached` or `PageVersionRejected`.
  - States use the same serialization as `cdpLostHandled.state` (for example `failed`).
  - The first time an `appId` appears, its state is recorded silently, so a cold start does not log one entry per tile.
  - Entries are written before `_dispatch(result.Commands)`, so `starting` precedes the host's `appLaunch`.
  - A result without a snapshot logs nothing.
- **States and copy stay as they are:**
  - AppMenu on `Failed` opens with focus on „Uruchom ponownie”, and „Zamknij” is the second item (already in `web/shell`).
  - The Stopping tile reads „Zamykam…”, as in DESIGN.md.
  - Starting reads „Uruchamiam…” / „Chwilka…”.
- **No escalation:**
  - A `Failed` app always accepts `RestartApp` and `CloseApp`, however many restarts failed before.
  - There is no counter, no extra state and no extra snapshot field.
- **Code rules:**
  - Comments are in Polish, matching the surrounding files.
  - Port methods never block or throw (AD-4).
  - New pure App code stays linked into `MediaStation.Core.Tests`.

**Never:**
- Changes to Orchestrator state logic, `transitionId`, contracts, `ErrorCode`, BrowserHost start/stop/probe/reconnect behaviour, or `web/shell`. This story wires and proves; it does not redesign. If a test exposes a real defect in these, fix it minimally and record it in Implementation Notes.
- DW-3 (a duplicate `ProcessExited` from a `WatchExit` race) and DW-4 (a late `AppWindowAttached` after `Failed`). Both are owned by the sweep, story 6.8.
- Long-press OK in the smoke.
- Touching `profiles\netflix` or leaving the published `page\runtime.js` patched. The smoke restores it in `finally` and checks the hash.
- Editing ticket files or `deferred-work.md`.

## I/O & Edge-Case Matrix

All rows run on the real `Orchestrator` + real `BrowserHost` (fakes for process, launcher and CDP), except the loop rows.

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| J4 happy | Foreground app fails (`CdpLost` or `PageVersionRejected`, root alive), then `RestartApp` | `UnmuteProcessTree` + `StopApp`. The host closes and kills the old tree, then posts `ProcessExited` then `CommandCompleted`. One `StartApp` follows, and a second launch gives a new pid. `AppWindowAttached(newHwnd, newPid)` + `ActivateWindow` lead to `Foreground`. States in order: `Failed, Stopping, Starting, Foreground`. The old root is gone, and `Launcher.Starts` is 2. | — |
| Stop facts pair | `ProcessExited`, then `CommandCompleted` for one restart stop | exactly one `StartApp`; the second fact yields no command and leaves `Starting` | — |
| Restart start refused | the profile is still held when the restart's preflight runs (`ProfileHeld`) | `CommandFailed(ProfileInUse)` → `Failed`, no new launch | — |
| Restarted process dies | the new process exits before attach | `ProcessExited` in `Starting` → `Failed` | — |
| Restarted app fails again | the new session gets `PageVersionRejected` (or `CdpLost`) after attach | `Foreground → Failed` with Mute, Minimize, ShowShell and `focusRequest` on the tile | — |
| No escalation | three failed restarts in a row | each `RestartApp` is accepted (Stopping), never `intentIgnored`; the state is plain `Failed` each time; a fourth restart that succeeds ends in `Foreground` | — |
| Close from Failed | `CloseApp` after a failed restart | `Stopping` → `NotRunning`, the tree is killed, nothing is launched | — |
| Loop log | the restart sequence through `OrchestratorLoop` | `appStateChanged` entries `failed→stopping (RestartApp)`, `stopping→starting`, `starting→foreground (AppWindowAttached)` | first snapshot and ignored intents log no entry |

</intent-contract>

## Code Map

- `src/MediaStation.App/Orchestrator/OrchestratorLoop.cs` `Run` (L137–244): add the state tracking and `appStateChanged` next to `cdpLostHandled`. `StateIn` (L119) shows how snapshot apps are read (`result.Snapshot.Apps[i].AppId/State`).
- Read-only, already correct:
  - `src/MediaStation.Core/Orchestrator/Orchestrator.Stop.cs` `OnStop`/`OnStopped`;
  - `Orchestrator.Failures.cs`: `ProcessExited` in Stopping closes the stop, in Starting it fails; `OnLiveFailure`;
  - `Orchestrator.Timers.cs`;
  - `src/MediaStation.App/Shell/ShellMessageGate.cs` L67–73;
  - `web/shell/src/focus.ts` L41–44 and L151–188 (`MENU_ITEMS`, OK on Failed → menu at item 0), `tileView.ts`.
- `src/MediaStation.App/Hosts/Browser/BrowserHost.cs`:
  - `StopCoreAsync` (~L1398) posts `ProcessExited` then `CommandCompleted` after `StopSessionAsync` (~L1426: CloseMainWindow 3 s, KillTree, wait for the root 5 s);
  - `StartCoreAsync` preflight (~L320) checks the profile and port once.
  - Expect no change.
- `tests/MediaStation.Core.Tests/BrowserHostTests.cs`:
  - `Harness` (~L3027): `Posted`, `WaitPosted`, `WaitLog`, `Time`, `ProfileHeld`, `Launcher.Starts/Processes` (pids 4243…, hwnd `0x1234 + pid - 4242`), `FakeProcess` (`Exit()`, `KillCalls`, `IgnoreClose`), `Transport`, `Eventually`;
  - feeding `h.Posted[i]` into a real Orchestrator: see ~L480, ~L1920, ~L2936;
  - `Restart_checks_profile_and_port_only_after_the_old_root_has_exited` (~L1380) is the closest existing test.
  - Add a small pump helper there: it dispatches `StartApp`/`StopApp` from a result to `h.Host.Start/Stop`, feeds newly posted messages back to `orchestrator.Handle`, and collects the state after each step.
- `tests/MediaStation.Core.Tests/OrchestratorTestSupport.cs`: `Started`, `Foreground`, `StateOf`, `AssertManaged`.
- `tests/MediaStation.Core.Tests/OrchestratorStopTests.cs` (~L120, ~L208): the home for the "stop facts pair" row.
- `tests/MediaStation.Core.Tests/OrchestratorLoopTests.cs`: the `cdpLostHandled`/`pageVersionRejectedHandled` log tests are the pattern for the loop rows.
- `tools/check-browserhost-adopt.ps1`: the template to copy from:
  - the throwaway tile `browserhost-check`/9237, the `apps.json` backup, the `$MediaStation` publish path (L46), and the profile guard (L80);
  - `Send-Key`, `Send-Ok`, `Wait-For`, `Get-RunEntries`, `Wait-Log`, `Assert-Levels`, `Save-Evidence`, `Get-ProfileRoots`, `Stop-Profile`, `Remove-TestProfile`, `Start-Run`, `Test-ShellForeground`.
  - Keys: Home is 0x24 extended, as in `check-browserhost-reconnect.ps1`; Down is 0x28 extended.
- The published `page\runtime.js` has the line `var ENVELOPE_VERSION = 1;`. Changing it to `2` makes every `hello` give `pageMessageRejected{reason:version}`, then `PageVersionRejected`, then `Failed`. BrowserHost reads the file at every start.

## Tasks & Acceptance

**Execution:**
- [x] `src/MediaStation.App/Orchestrator/OrchestratorLoop.cs`: `appStateChanged` per Always. Update the class or `Run` comment.
- [x] `tests/MediaStation.Core.Tests/OrchestratorLoopTests.cs`: the loop rows, covering the restart sequence entries with `cause`, a silent baseline, no entry for an ignored intent, and no entry for a result without a snapshot.
- [x] `tests/MediaStation.Core.Tests/OrchestratorStopTests.cs`: the stop-facts-pair row.
- [x] `tests/MediaStation.Core.Tests/BrowserHostTests.cs`: the pump helper, and every real-host row of the Matrix. Assert the exact state sequence and that the old `FakeProcess` was killed.
- [x] `tools/check-browserhost-restart.ps1` (new): the live J4 smoke. See the AC and Verification.

**Acceptance Criteria:**
- **Setup.** Given the published Release build, the harness off, the throwaway tile first on the Video tab, and the published `page\runtime.js` patched to `ENVELOPE_VERSION = 2`, when the smoke runs, then each phase below holds in the JSONL log of the run and on screen.
- **P1, failure on the first plan:**
  - OK on the tile gives `appLaunch` (root R1), `pageVersionRejectedHandled{state:failed}`, and `appStateChanged` `starting→foreground→failed`;
  - the shell is foreground within 5 s, and Edge is `IsIconic`.
- **P2, a restart that fails again:**
  - OK shows the AppMenu (screenshot), and a second OK gives `appStateChanged` `failed→stopping` (`cause: RestartApp`), `stopping→starting`, `starting→foreground`, `foreground→failed`, in this order;
  - a new root R2 ≠ R1, R1 is gone, and exactly one root remains;
  - there is no `intentIgnored`, and the shell is foreground.
- **P3, Zamknij:** OK, Down and OK give `failed→stopping` (`cause: CloseApp`) and `stopping→notRunning`, and no browser root of the profile remains.
- **P4:** OK on the `NotRunning` tile gives `Failed` again (as in P1), with root R3.
- **P5, the J4 culmination:**
  - The runtime is restored first, and its hash equals the backup.
  - OK then OK give `failed→stopping`, `stopping→starting` and `starting→foreground`, a new root R4 with R3 gone, and a `pageHello{webdriver:false}` after R4's `appLaunch`.
  - The foreground window belongs to R4's tree and is not iconic, and no `→failed` follows within 5 s.
- **P6:** Home gives `foreground→background`, the shell foreground, and Edge `IsIconic` and visible.
- **Exit and cleanup:** the smoke exits 0. Its `finally` restores `apps.json` and the runtime, kills the test profile's roots and deletes the profile. Only `appIconMissing` and the expected `pageMessageRejected` may appear as warn/error.
- **Tests:** given `dotnet build` + `dotnet test` of `MediaStation.slnx` and `npm test` in `web/`, then all pass, and the tracer, adopt and errors smokes still pass.

## Implementation Notes

- **Machine:** ran on UM760 (`MEDIASTATION` / `F7BSI` / `EliteMini Series`), 2026-10-29.
- **`appStateChanged`** (`OrchestratorLoop.LogStateChanges`): it uses a per-`appId` dictionary that only the loop thread touches. It runs after `pageVersionRejectedHandled` and before `foregroundChanged`/dispatch, only when the step has a snapshot. `from`/`to` are `AppLifecycleState` values, which the JSONL sink writes in camelCase like `cdpLostHandled.state`. No Core, contract, BrowserHost or `web/shell` changes. No defect was found.
- **Matrix row "Restarted process dies":** on the real BrowserHost, a new root that exits *before* its window never reaches `WatchExit`. The host reports it as `CommandFailed(Unexpected)` with `browserStartFailed {launcherExited}`, not `ProcessExited`, so `Starting → Failed` happens through `OnCommandFailed`. The test covers that case, plus the case of a death *after* the window (CDP connect pending), where the host posts `ProcessExited` (`Foreground → Failed`, `ForgetProcessTree`, `ShowShell`). The literal "`ProcessExited` in `Starting` → `Failed`" case is already covered on the pure Orchestrator (`OrchestratorFailureTests.Failure_in_starting_or_background_enters_failed_and_only_starting_cancels_timeout`).
- **Live cause of `stopping→starting`:** it is `ProcessExited`, because BrowserHost posts it before `CommandCompleted` and the stop completes on the first fact. The loop test feeds only `CommandCompleted`, so its cause there is `CommandCompleted`.
- **Smoke keys:** inside the open AppMenu, Down/OK are sent without `Set-Front` (no Alt tap between menu keys). The first OK in each phase uses `Send-Ok` as in the adopt smoke.
- **Evidence:**
  - `dotnet build MediaStation.slnx -c Release`: 0 warnings, 0 errors.
  - `dotnet test --solution MediaStation.slnx -c Release --no-build`: 1624 total, 1623 passed, 1 skipped (pre-existing DW-1 skip), 0 failed.
  - The 14 new tests ran 5× in a row with no failures.
  - `npm test` in `web/`: 175/175.
  - `dotnet publish src/MediaStation.App -c Release -r win-x64 --self-contained`: exit 0.
  - `tools/check-browserhost-restart.ps1`: PASS, exit 0. Evidence is in `%LOCALAPPDATA%\MediaStation\logs\browserhost-restart-evidence-20261029-201959`:
    - P1 R1=22744, `notRunning>starting:LaunchApp, starting>foreground:AppWindowAttached, foreground>failed:PageVersionRejected`.
    - P2 R2=23108 (R1 gone, one root), `failed>stopping:RestartApp, stopping>starting:ProcessExited, starting>foreground:AppWindowAttached, foreground>failed:PageVersionRejected`.
    - P3 `failed>stopping:CloseApp, stopping>notRunning:ProcessExited`, with no profile processes left.
    - P4 R3=5048.
    - P5: the restored runtime hash matches the backup. R4=7812, `pageHello`, Edge foreground in R4's tree, the window covers the monitor, and nothing failed within 5 s.
    - P6 `foreground>background:Action`, shell foreground, Edge iconic and visible.
    - Cleanup: runtime hash equal, `apps.json` restored.
    - The AppMenu screenshots show „Uruchom ponownie” focused with „Zamknij” below it.
  - Regression: `check-browserhost-tracer.ps1`, `check-browserhost-adopt.ps1` and `check-browserhost-errors.ps1` all PASS (exit 0).
- **UM760 whole-epic acceptance:** this ticket was verified on UM760 itself. The user's epic acceptance should replay J4 with the real remote: OK on a failed tile → „Uruchom ponownie” → the app returns, and „Zamknij” from the menu. In the log, check the `appStateChanged` sequence.

- **Orchestrator verification re-run (UM760, 2026-10-29):**
  - Build: 0 warnings, 0 errors.
  - `dotnet test`: 1624 total, 1623 passed, 1 skipped.
  - `npm test`: 175/175. Publish: exit 0.
  - `tools/check-browserhost-restart.ps1`: PASS, exit 0. Evidence: `%LOCALAPPDATA%\MediaStation\logs\browserhost-restart-evidence-20261029-202436`.
    - Roots: R1 21048, R2 12484, R3 22340, R4 21836.
    - `p2-appmenu.png` checked by eye: „Uruchom ponownie” is focused and „Zamknij” is below it.
    - `p5-foreground.png`: Netflix fills the monitor with no browser UI.
  - Regression smokes: tracer (`…-202523`), adopt (`…-202541`) and errors (`…-202558`) all PASS.
- **Matrix audit:** every row has a passing test.
  - J4 happy: `J4_restart_from_failed_kills_the_old_tree_and_brings_a_new_root_to_the_foreground` (cdpLost and pageVersionRejected).
  - Stop facts pair: `Restart_from_failed_with_both_stop_facts_starts_the_app_once_and_stays_starting`.
  - Restart start refused: `J4_restart_refused_by_a_held_profile_fails_again_without_a_new_launch`.
  - Restarted process dies: `ProcessExited` in `Starting` → `Failed` is covered by `OrchestratorFailureTests.Failure_in_starting_or_background_enters_failed_and_only_starting_cancels_timeout` (`starting`, `processExited`). With the real host, `J4_restarted_process_that_dies_fails_the_tile_without_another_launch` covers it: before the window it is `CommandFailed{launcherExited}`, and after the window it is `ProcessExited`.
  - Fails again: `J4_restarted_app_that_fails_again_hides_behind_the_shell_with_focus_on_its_tile`.
  - No escalation: `J4_three_failed_restarts_never_escalate_and_a_fourth_reaches_the_foreground`.
  - Close: `J4_close_after_a_failed_restart_kills_the_tree_and_launches_nothing`.
  - Loop log: the four `OrchestratorLoopTests` 6.7 tests.

## Plan Change Log

## Review Triage Log

### 2026-10-29 — Review pass
- verdicts: 25 findings — high 0, medium 0, low 15, false 10, maybe-false 0
- findings:
  - `[false]` `[reject]` (blind-hunter) The loop test feeds only `CommandCompleted` for the stop, not the host's `ProcessExited` + `CommandCompleted` pair, so a double `stopping→starting` entry could go unseen. — The second fact gives no snapshot (`Restart_from_failed_with_both_stop_facts_starts_the_app_once_and_stays_starting`: `completed.Snapshot` is null). A step without a snapshot logs nothing (`Ignored_intent_and_a_step_without_a_snapshot_log_no_state_change`). The live smoke's `Assert-Sequence` compares the exact sequence and saw exactly one `stopping>starting:ProcessExited` in P2 and P5.
  - `[low]` `[reject]` (blind-hunter) The Matrix row "Restarted process dies" is not met as written (`ProcessExited` in `Starting`), and the plan was not amended. — The behaviour is correct and the outcome holds (Failed, no relaunch). The literal row is covered by `OrchestratorFailureTests` (`starting`, `processExited`), and the real-host mechanism is recorded in Implementation Notes and the Matrix audit. The only fix would edit this build's plan.
  - `[false]` `[reject]` (blind-hunter) `lenses_ran` is set but the triage log is empty. — This pass writes the log; the lenses had just run.
  - `[low]` `[reject]` (blind-hunter) `cause` as a CLR type name loses detail (which Action, which timer, which error code). — The plan fixes `cause` as the message type. The Action that moves Foreground to Background is Home, and `commandFailed`/timer logs sit next to the entry. Richer causes need a per-message mapping (new branches) for a reader-only gain.
  - `[low]` `[reject]` (blind-hunter) `cause` is PascalCase while `from`/`to` are camelCase, and renaming a record would silently change the log. — A rename makes the smoke's `Assert-Cause` fail loudly, and the loop test pins the names. A mapping table adds a second source of names.
  - `[false]` `[reject]` (blind-hunter) `J4Pump` never fires timers, so a stale stop/start timer could fail the restarted tile unseen. — The Orchestrator cancels the stop timer in `OnStopped` and the start timer on attach. Stale `TimerElapsed` after a restart is covered by `OrchestratorTimerTests.Restart_after_start_timeout_schedules_new_timer_and_stale_elapsed_is_ignored` (L62) and `Restart_after_stop_timeout_schedules_new_timers_and_stale_elapsed_is_ignored` (L176).
  - `[low]` `[patch]` (blind-hunter) `AssertQuiet` waits only 200 ms of real time without advancing `FakeTimeProvider`, so late posts timed on the fake clock would go unseen. — Fixed: `AssertQuiet` advances `h.Time` by 60 s (past the 30 s reconnect, 15 s connect and 3 s/5 s stop waits) before the wait. All 8 J4 tests pass, and so does the full suite (1623/1624, 1 skipped).
  - `[false]` `[reject]` (blind-hunter) No test restarts from a `Failed` without a live process (ProfileInUse, launcherExited, start timeout), so one-OK repair is unproven there. — `BrowserHost.StopCoreAsync` posts `ProcessExited` + `CommandCompleted` whether or not a session exists, so `OnStopped` issues `StartApp`. Restart from a timeout-`Failed` without `RootPid` is covered in Core (`OrchestratorTimerTests` L62; `OrchestratorStopTests` "Bez RootPid … StopApp idzie pierwszy").
  - `[low]` `[reject]` (blind-hunter) Smoke phase marks can drift on a half-written last log line, and a UTC-midnight log rotation empties the queries. — Both give a false FAIL, never a false PASS. Both need rare timing (a partial line at the mark instant, or a run that crosses midnight), and the pattern is shared with the 6.2–6.5 smokes.
  - `[low]` `[reject]` (blind-hunter) The smoke's log polling re-reads the whole JSONL on every poll. — The whole run took about 40 s on UM760, and every timing assertion passed. Offset-based reading adds state to a dev tool.
  - `[low]` `[reject]` (blind-hunter) A leftover `runtime.js.restore-bak` from a hard-killed run would overwrite a later republish. — This needs both a hard kill that skips `finally` and a republish before the next run. A guard comparing backup and current file adds a branch for state no run has shown. `apps.json` handling is the existing smokes' pattern.
  - `[low]` `[reject]` (blind-hunter) The AppMenu checks are only by eye, and `Open-AppMenu` does not confirm the menu opened. — A menu that fails to open still fails the phase (no `RestartApp`/`CloseApp` cause). The shell has no menu log to assert on, and adding one is new surface. Menu focus and order are unit-tested in `web/shell` (`focus.test.ts`, `Placeholder.test.tsx`), and the screenshots were checked.
  - `[low]` `[reject]` (blind-hunter) `FailLive` hard-codes the 30 s reconnect deadline. — The harness config is fixed at 30 s, and `NeverBackScheduleMs` and the 6.3 tests assume the same. A config change would fail these tests loudly.
  - `[low]` `[reject]` (blind-hunter) Profile process matching uses a loose `-like` prefix, and the `' --type='` / `'--type='` filters are inconsistent. — No sibling `browserhost-check*` profile exists, and roots carry no `--type=` at all, so both filters agree. Same pattern as the adopt smoke (rejected in the 6.5 triage).
  - `[low]` `[reject]` (edge-case-hunter) A stale runtime backup can replace a fresh publish. — Same as the blind-hunter backup row: rare, and the guard adds a branch.
  - `[low]` `[reject]` (edge-case-hunter) A run crossing UTC midnight reads the wrong daily log. — Same as the blind-hunter drift row: rare, and it fails loudly.
  - `[low]` `[reject]` (edge-case-hunter) A `browserhost-check2` sibling profile would be matched and killed. — Same as the blind-hunter matching row: no such profile exists.
  - `[low]` `[reject]` (edge-case-hunter) A message posted before `Start()` and handled before `OrchestratorStarted` makes the first real transition silent. — The only effect is one missing log line, in a startup race. Seeding the baseline only from `OrchestratorStarted` adds a branch.
  - `[low]` `[patch]` (edge-case-hunter) `AssertQuiet` misses late duplicate facts posted on the fake clock. — Same root cause and fix as the blind-hunter `AssertQuiet` row.
  - `[false]` `[reject]` (intent-alignment) A reading where the story needed new integration code (R-A) is not implemented. — The shell, gate, Orchestrator and router wiring already existed (the auditor's own references). The intent's Verify is met by the tests and the live run, and the auditor notes the diff implements the proof reading (R-B).
  - `[false]` `[reject]` (intent-alignment) The shell surface (menu focus, item order, Starting copy, no escalation UI) has no new automated test. — The existing `web/shell` tests cover it (`focus.test.ts` AppMenu block, `Placeholder.test.tsx:71`, `tileView.test.ts`) and ran in `npm test` (175/175). `web/shell` is unchanged, the smoke drives the menu live, and the screenshots show it.
  - `[false]` `[reject]` (intent-alignment) `J4Pump` checks activation, mute and minimize only as emitted commands. — The live smoke proves the effects: Edge foreground in R4's tree and not iconic; shell foreground with Edge iconic after a failure; P6 iconic and visible.
  - `[false]` `[reject]` (intent-alignment) The live failure is a synthetic `v:2`, not a hang or a CDP loss. — It goes through the same `OnLiveFailure` path as `CdpLost` (Design Notes). `CdpLost` is covered on the real host with fakes, and live by the 6.3 reconnect smoke. The real-remote Netflix replay belongs to the user's whole-epic acceptance (AGENTS.md).
  - `[false]` `[reject]` (intent-alignment) Keys are injected with `keybd_event`, not the remote, and the tile is a throwaway one. — Per AGENTS.md, the per-ticket check is the strongest automated evidence on the current machine. The real remote and Netflix tile are in the UM760 epic acceptance.
  - `[false]` `[reject]` (intent-alignment) `appStateChanged` was not asked for by the ticket. — It is not a defect: it exists to assert the "kolejność stanów" the Verify line asks for, in a live log, and it changes no behaviour.

## Design Notes

- **Why a generic state log.** The ticket asks for the order of states. Without the log, a live run has to infer `Stopping` from an unmute and `Starting` from `appLaunch`. One entry per actual change, with its cause, also serves the user's acceptance on UM760 and later epics, and costs one dictionary in the loop.
- **Why `v:2` and not a CDP drop.** A dropped CDP connection takes `cdpReconnectTimeout` (30 s) to reach `Failed`, and it cannot make the *restarted* app fail on demand. A patched envelope version fails every start within seconds and keeps the process alive, which is J4's "Nie odpowiada" case. It goes through the same `OnLiveFailure` path as `CdpLost`. Restoring the file before P5 makes the repair deterministic.
- **Stopping copy.** J4 step 3 lists „Uruchamiam…” / „Chwilka…” for the Starting part. DESIGN.md gives Stopping „Zamykam…”, so it stays.

## Verification

**Commands:**
- `powershell -NoProfile -Command "$env:COMPUTERNAME; (Get-CimInstance Win32_BaseBoard).Product; (Get-CimInstance Win32_ComputerSystem).Model"`: record the machine (planned on UM760: `MEDIASTATION`/`F7BSI`/`EliteMini Series`).
- `dotnet build MediaStation.slnx -c Release`: 0 warnings, 0 errors.
- `dotnet test --solution MediaStation.slnx -c Release --no-build`: all pass (only the pre-existing skip).
- `npm test` in `web/`: passes.
- `dotnet publish src/MediaStation.App -c Release -r win-x64 --self-contained`: exit 0.
- `powershell -NoProfile -ExecutionPolicy Bypass -File tools/check-browserhost-restart.ps1`: exit 0. Evidence (screenshots after P1, the two AppMenus, P5 and P6, plus `measurements.jsonl` with the root PIDs and the `appStateChanged` sequence) goes under `%LOCALAPPDATA%\MediaStation\logs\browserhost-restart-evidence-<ts>`.
- Regression: `tools/check-browserhost-tracer.ps1`, `check-browserhost-adopt.ps1` and `check-browserhost-errors.ps1` all PASS.

**Manual checks (if no CLI):**
- AppMenu screenshots: „Uruchom ponownie” is focused and „Zamknij” is below it. The P5 screenshot shows Edge covering the monitor.

## Auto Run Result

Status: built (ran on **UM760**: `MEDIASTATION` / `F7BSI` / `EliteMini Series`)

**Summary.** J4 (a Failed tile repaired with one OK) is now proven end to end on the real BrowserHost. The wiring already existed: AppMenu → `restartApp` → `ShellMessageGate` → `RestartApp` → `StopApp`/`StartApp`. This story adds:
- **State log:** `OrchestratorLoop` logs `info orchestrator appStateChanged { appId, from, to, cause }` for every tile state change. There is no entry at a tile's first sighting or on a step without a snapshot, and the entry is written before dispatch. A live log can now show `failed → stopping → starting → foreground`.
- **Real-host tests:** the real Orchestrator and the real BrowserHost run in one test pump (`J4Pump`). They cover restart after `CdpLost` and after `PageVersionRejected`, the order of states, the old tree killed and a new root, a restart refused by a held profile, a restarted process that dies, a restarted app that fails again, three failed restarts followed by a fourth that succeeds, and Close after a failed restart.
- **Live smoke:** `tools/check-browserhost-restart.ps1` drives OK → AppMenu → OK on the published build. A temporary `ENVELOPE_VERSION = 2` patch of the published `page\runtime.js` makes the tile fail while its process stays alive.

**Files changed.**
- `src/MediaStation.App/Orchestrator/OrchestratorLoop.cs`: the `appStateChanged` entry.
- `tests/MediaStation.Core.Tests/OrchestratorLoopTests.cs`: 4 tests: the order with causes, before dispatch, a silent baseline, and no entry for an ignored intent or a step without a snapshot.
- `tests/MediaStation.Core.Tests/OrchestratorStopTests.cs`: both stop facts give exactly one restart.
- `tests/MediaStation.Core.Tests/BrowserHostTests.cs`: `J4Pump` and 8 J4 tests on the real Orchestrator and BrowserHost.
- `tools/check-browserhost-restart.ps1` (new): the live J4 smoke, P1–P6.

**Review findings.** 25 in total: high 0, medium 0, low 15, false 10, maybe-false 0.
- **Patched (1 entry, low):** `AssertQuiet` now advances the fake clock by 60 s before its quiet check. Raised by blind-hunter and edge-case-hunter.
- **Deferred:** none.
- **Rejected:** the reason for each is in the Review Triage Log.
  - **low:**
    - the Matrix row wording (the fix would edit this plan);
    - `cause` detail and casing;
    - smoke log-mark drift and midnight rotation (raised twice);
    - quadratic log polling;
    - a stale runtime backup (raised twice);
    - AppMenu opening checked only by eye;
    - the hard-coded 30 s;
    - loose profile matching (raised twice);
    - a pre-`Start()` message.
  - **false:**
    - the loop test fact pair;
    - the empty triage log;
    - stale timers;
    - a restart from a no-process Failed;
    - six intent-alignment divergences: integration code, shell tests, commands vs effects, the synthetic failure, keybd_event/throwaway tile, and the unrequested log.

**Follow-up review recommended:** false. This was a first pass. The only patched entry is low, so the patched counts are high 0, medium 0, low 1.

**Verification (UM760, 2026-10-29, after the patch).**
- `dotnet build MediaStation.slnx -c Release`: 0 warnings, 0 errors.
- `dotnet test --solution MediaStation.slnx -c Release --no-build`: 1624 total, 1623 passed, 1 skipped (the existing DW-1 skip), 0 failed.
- `npm test` in `web/`: 175 passed.
- `dotnet publish src/MediaStation.App -c Release -r win-x64 --self-contained`: exit 0.
- `tools/check-browserhost-restart.ps1`: PASS, exit 0, before the patch (`%LOCALAPPDATA%\MediaStation\logs\browserhost-restart-evidence-20261029-202436`) and after it (`…-203217`). On the first run:
  - P1 Failed, with the shell in front 22 ms later and Edge iconic.
  - P2: the restart failed again with the order `failed>stopping:RestartApp`, `stopping>starting:ProcessExited`, `starting>foreground:AppWindowAttached`, `foreground>failed`. The new root 12484 replaced 21048, and one root remained.
  - P3: Zamknij gave `notRunning`, with no profile processes left.
  - P4: Failed again.
  - P5: the runtime was restored (hash equal), and the restart reached `foreground` with root 21836, `pageHello`, Edge in front, and no further failure.
  - P6: Home gave `foreground>background`, the shell in front, and Edge iconic and visible.
- Screenshots, checked by eye:
  - `p2-appmenu.png`: „Uruchom ponownie” focused, „Zamknij” below it, the tile „Nie odpowiada”;
  - `p5-foreground.png`: Netflix covering the monitor with no browser UI.
- Regression smokes, PASS before and after the patch:
  - `check-browserhost-tracer.ps1` (`…-202523`, `…-203256`);
  - `check-browserhost-adopt.ps1` (`…-202541`, `…-203314`);
  - `check-browserhost-errors.ps1` (`…-202558`, `…-203332`).
- Matrix audit: every row has a passing test (see Implementation Notes).

**Residual risks.**
- **DW-3 race:** a `WatchExit` that races a restart's Stop can post a second `ProcessExited`, which would fail the new start. This is DW-3, owned by sweep 6.8. DW-4 stays with 6.8 too.
- **Synthetic live failure:** the live failure in this smoke is the `v:2` envelope. A real Netflix hang or CDP loss was not replayed through J4 live; a CDP loss reaches the same `OnLiveFailure` path and is covered in fakes.
- **Whole-epic acceptance on UM760** (user, `fp-acceptance`):
  - on the Netflix tile in `Failed` (for example after a CDP loss), one OK with the real remote should open AppMenu with focus on „Uruchom ponownie”;
  - a second OK should bring Netflix to the foreground;
  - the log should show `appStateChanged` `failed→stopping→starting→foreground`;
  - „Zamknij” from the same menu should close the app;
  - or run `tools/check-browserhost-restart.ps1`.
