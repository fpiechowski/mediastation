---
title: 'Story 6.8: Refactor sweep (BrowserHost, zamknięta lista ID)'
type: 'refactor'
ticket: '8'
created: '2026-10-29'
status: done
baseline_revision: 'ec50bad70c38ed16422a2ba577f50fd386333983'
route: 'full'
route_source: 'auto'
risk: 'medium'
review: 'thorough'
review_source: 'pinned'
lenses_ran: [blind-hunter, edge-case-hunter, verification-gap, intent-alignment]
review_loop_iteration: 0
followup_review_recommended: false
context: []
warnings: [oversized]
deferred:
  - summary: >-
      DevHarnessHostPort still posts ProcessExited on every Stop even after its exit watcher already reported the exit, so a dev-harness restart can still hit the DW-3 outcome (a second ProcessExited failing the restarted app).
    evidence: |-
      Pre-existing, not caused by this change. StopApp handling in DevHarnessHostPort.cs (~L141) posts ProcessExited unconditionally, and its watcher (~L432) posts its own. The dev harness is opt-in (MEDIASTATION_DEV_HARNESS=1), and the accepted epic-5 checks use it, so 6.8 left it unchanged. Fix by mirroring the BrowserHost rule: one exit report per process, with Stop posting only CommandCompleted after a watcher report.
    location: >-
      src/MediaStation.App/Hosts/DevHarness/DevHarnessHostPort.cs
    severity: low
---

<intent-contract>

## Intent

**Problem:** Stories 6.2–6.7 left deferred ledger items and epic-note deferrals for this sweep. Two are real defects with no fix yet: DW-3 (a duplicate `ProcessExited` can fail a restarted app) and DW-4 (a window attached after the `Starting` timeout stays on top, unmuted, over the shell). The rest need a recorded verdict: done earlier, false, or not this sweep's.

**Approach:** The closed list below was fixed at the start of the work (UM760, 2026-10-29, HEAD `ec50bad`) and is not extended. Fix DW-3 in BrowserHost and DW-4 in the Orchestrator, each minimally and pinned by tests. Give every other item evidence (test name, commit, smoke or probe output) or a reason for rejection. BrowserHost behaviour in the specified 6.2–6.7 paths stays the same: all existing tests and the five BrowserHost smokes pass unchanged.

## Boundaries & Constraints

**Closed list (ID → planned verdict):**

| ID | Verdict | Evidence / reason |
|----|---------|-------------------|
| DW-3 | **fix** (BrowserHost) | see Always |
| DW-4 | **fix** (Orchestrator) | see Always |
| DW-39 | resolved in 6.4 | `browserVersion`/`browserVersionChanged` are logged before `appLaunch` (synchronous `JsonLinesLog`). Tests `Version_change_is_logged_before_launch_even_when_cdp_then_never_answers` and the log-order test; errors smoke A |
| DW-53 | resolved in 6.4 | `StopSessionAsync` waits ≤5 s for the root after `KillTree`. Tests `Stop_waits_for_the_root_to_exit_after_killing_the_tree`, `Restart_checks_profile_and_port_only_after_the_old_root_has_exited`; restart smoke P2/P5 (old root gone, no `launcherExited`) |
| DW-54 | resolved in 6.6 | `PageVersionRejected` → `Failed`, `pageVersionRejectedHandled`; tests in `BrowserHostTests` (AD-7/DW-54 block) and `OrchestratorFailureTests` |
| DW-55 | resolved (settle condition met) | The ledger's alternative "or a main-frame navigation": the adopt smoke drops the channel (`RenewSession`), runs the same `TryReconnectOnceAsync`/`InstallAsync` path, gets `cdpReconnected`, and F5 then gives a new `pageHello` on the new session. The "existing document" variant has no trigger: the runtime sends only `hello`, and drivers belong to epic-netflix-dpad |
| DW-52 | false (probe) | UM760 probe, Edge 156.0.4314.50, this plan's exact launch args: 5 fresh profiles, 2 profiles seeded with 3 tabs then killed, 2 seeded and closed gracefully with `restore_on_startup=1`. Every kiosk start had exactly one `page` target, `about:blank`, immediately and 2–4 s later. No code change |
| DW-57 | false (probe) | UM760 probe: kiosk Edge, `CloseMainWindow` (WM_CLOSE = Alt+F4), 3 runs. After 6 s no root and no process with the profile remains, so a window-less root does not occur. No code change |
| DW-56 | rejected: not a sweep fix | Recovery needs `Page.reload`/`Runtime.evaluate`, or a frame-tree check (new CDP method). All are outside AD-1, so it is a product/architecture decision (bmad-architecture). Stays open |
| DW-36 | partly resolved, rest rejected | Web tiles got the real host (`CompositionRoot.CreateWebHost` → `BrowserHost`), which is the ledger's second resolution. Moonlight/Native still use `MissingHostPort` until their host epics. A `HostMissing` `ErrorCode` is an AD-4 contract change, outside a sweep |
| DW-40 | rejected: needs hardware | Multiple monitors (UM760 has one output), a resolution change on the TV, and TV off are operator hardware states. The OSK overlay is epic-osk's first story, and behaviour after display-off belongs to WindowManager. Listed for the user's whole-epic acceptance on UM760 |
| DW-25 | rejected: not triggered | The trigger is "before epic-osk or epic-browserhost extends InputHub/InputPolicy". `git diff --stat cd6a046..HEAD -- src/MediaStation.App/Platform/Input src/MediaStation.Core/Input` is empty, so it stays with epic-osk |
| E-F6 (`id` in the envelope) | resolved in 6.6 | `PageEnvelope.Id`; `PageProtocol.Parse` tests |
| E-F18a (`service` kebab-case) | resolved in 6.2 | `Web_service_must_be_kebab_case_because_it_is_a_path_segment` |
| E-F18b (timeout upper bounds) | resolved in 6.2/6.3 | `cdpConnectTimeout < startingTimeout ≤ 600`; `cdpReconnectTimeout ≤ 600` (`Timeout_above_600_seconds_is_rejected` cases) |

**Always:**
- **DW-3 rule:** the host posts `ProcessExited` at most once per launched or adopted process.
  - **Exit watcher:** `WatchExit` claims the exit atomically: under the session lock, mark stopping and set an exit-reported flag only when the session is not already stopping. That replaces the separate `Stopping` check and `MarkStopping()` call. Only the winner logs `processExited` and posts.
  - **Recording the claim:** when the watcher removes the still-current session from `_sessions`, it records the `appId` in a host set (`_exitsReported`, guarded by `_gate`). Registering any new session for that `appId` (start, around L310, or adoption, around L536) clears the entry.
  - **Stop:** `StopCoreAsync` takes the session and removes the set entry under `_gate`. After `StopSessionAsync` it also reads the session's flag. If either says the exit was already reported, it logs `info browser exitAlreadyReported { appId, cmdId }` and posts only `CommandCompleted`. Otherwise it posts `ProcessExited` + `CommandCompleted` as today, which keeps `Stop_without_a_session_still_reports_exit_and_completion`.
- **DW-4 rule (Orchestrator):** decided here, because AD-14/15 forbid hosts to minimize windows.
  - **Flag:** a Starting timeout (`OnTimerElapsed`, start branch → `EnterFailed`) sets a per-app flag that a late window may still come. The flag is cleared on `OnStop` from `Failed`, on `ProcessExited` in `Failed`, and when it is consumed.
  - **Late attach:** `AppWindowAttached` in `Failed` with the flag records `RootPid` (not `Hwnd`: `Failed` keeps no window, as in `EnterFailed`). It emits `MuteProcessTree(rootPid)`, `MinimizeWindow(hwnd)`, and then `ShowShell` only when no other app is active, the same rule as `OnAdopt`. The state stays `Failed`, the snapshot is `null`, and `transitionId`/focus do not change.
  - **Without the flag:** an attach in `Failed` is ignored, as now (`Failed_ignores_inputs_without_commands_or_snapshot` stays).
- Update the `OnAppWindowAttached` summary, which still says "zostaje w DW-4", and the `EnterFailed`/`OnTimerElapsed` comments where they state the rule. Comments are in Polish, matching the files.
- Port methods never block or throw (AD-4). No contract, `ErrorCode`, schema or `web/` change.

**Never:**
- New items, or items outside the list. Scope pushed from another story becomes a new entry: for example the dev-harness web-path retirement from the 6.2 Design Notes.
- Any refactor not tied to a list ID.
- `DevHarnessHostPort` changes: it is dev-only, and the accepted epic-5 checks use it.
- New CDP methods, or AD-1/AD-4 amendments.
- Editing ticket files or `deferred-work.md`: the PO closes ledger entries.
- Touching `profiles\netflix` outside the existing smokes.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| DW-3 watcher first | process exits on its own; the watcher posts `ProcessExited`; then `StopApp` | the host posts only `CommandCompleted` for the stop and logs `exitAlreadyReported` | — |
| DW-3 restart race (real Orchestrator + BrowserHost) | app `Background`; the process exits; `RestartApp` is handled **before** the watcher's `ProcessExited` reaches the Orchestrator | the watcher's fact closes the stop, then one `StartApp`, the new root and `Foreground`. Never `Failed`, `Launcher.Starts == 2` | — |
| DW-3 normal stop | live session, `StopApp` | unchanged: `ProcessExited`, `CommandCompleted` | — |
| DW-3 never started | `StopApp` with no session and no reported exit | unchanged: `ProcessExited`, `CommandCompleted` | — |
| DW-3 new start clears | exit reported, then a new `StartApp` and a later Stop of the live session | that Stop posts `ProcessExited` + `CommandCompleted` | — |
| DW-4 late window | `LaunchApp`, start timeout → `Failed`, then `AppWindowAttached(h, p)` | `MuteProcessTree(p)`, `MinimizeWindow(h)`, `ShowShell`; state `Failed`; snapshot `null` | — |
| DW-4 other app active | as above, but another app is `Foreground`/`Starting` | `MuteProcessTree`, `MinimizeWindow`, no `ShowShell` | — |
| DW-4 after compensation | `RestartApp` | `UnmuteProcessTree(p)`, `StopApp`. The stop result gives `ForgetProcessTree(p)` + `StartApp` | — |
| DW-4 single use | a second attach, or an attach after `ProcessExited` in `Failed` | ignored (no commands, no snapshot) | — |
| DW-4 real host | BrowserHost (fakes) whose window appears after the Orchestrator's `TimerElapsed` | compensation as above; the later `CommandCompleted`/`CommandFailed` of the start is ignored; Restart kills the tree and relaunches | — |

</intent-contract>

## Code Map

- `src/MediaStation.App/Hosts/Browser/BrowserHost.cs`:
  - `WatchExit` (L1366–1396): the non-atomic `if (session.Stopping) return; var cdp = session.MarkStopping();`.
  - `StopCoreAsync` (L1398–1420): posts `ProcessExited` + `CommandCompleted` unconditionally.
  - `StopSessionAsync` (L1426): calls `MarkStopping()` first; keep it.
  - `Unregister` (L1468).
  - Session registration in `StartCoreAsync` (L310) and adoption in `ProbeCoreAsync` (L536).
  - `Session` (L1524+): `_gate`, `_stopping`, `MarkStopping`, `TryMarkVersionRejected` as the locking pattern for a new `TryMarkExited(out CdpSession?)` and `ExitReported`.
  - Update the class summary line about Stop.
- `src/MediaStation.Core/Orchestrator/Orchestrator.Attach.cs` `OnAppWindowAttached` (L22–72; the `Failed` branch goes before the `!= Starting` return) and `OnAdopt` (L82–98) for the command shape and the `AnotherAppIsActive` rule.
- `Orchestrator.Timers.cs` `OnTimerElapsed` L26–31 sets the flag. `Orchestrator.Failures.cs` `OnProcessExited` L56–61 (Failed branch) clears it, and `EnterFailed` (L97) stays as it is. `Orchestrator.Stop.cs` `OnStop` L48 clears it. `Orchestrator.cs` `AppState` (~L220–243) gets the documented flag.
- Read-only: `Orchestrator.cs` `Handle` (L109) + `WithManagedWindows` (no `Hwnd`, so no `SetManagedWindows` change); `ForgetRule` (L171).
- Tests:
  - `tests/MediaStation.Core.Tests/BrowserHostTests.cs`: `Harness` (`Posted`, `WaitPosted`, `WaitLog`, `Time`, `Process.Exit()`, `Launcher.Starts`), `Stop_without_a_session_still_reports_exit_and_completion` (L685, must stay), `Process_exit_not_caused_by_stop_reports_process_exited` (L696), and `J4Pump` (L3544). Its orchestrator comes from `OrchestratorTestSupport.Started()`; for DW-4, build one with `new Settings { StartingTimeout = 5 }` and feed `TimerElapsed(timerId)` from the `ScheduleTimer` command by hand. The window-after-delay seam is used by `Cdp_deadline_counts_from_launch_including_the_window_search`.
  - `OrchestratorAttachTests.cs` (theory L191–225), `OrchestratorFailureTests.cs` (L308–336), `OrchestratorTimerTests.cs`, `OrchestratorTestSupport.cs` (`Started`, `Foreground`, `StateOf`, `TwoApps`).
- Smokes (regression, unchanged): `tools/check-browserhost-{tracer,reconnect,errors,adopt,restart}.ps1`.
- Probe scripts used for DW-52/DW-57 (planning evidence, not committed). They live in the scratchpad: `C:\Users\UYTKOW~1\AppData\Local\Temp\claude\C--Users-U-ytkownik-mediastation\d322643f-ec0b-45ac-8d2c-9fdc21bae62e\scratchpad\probe-targets.ps1` and `probe-targets2.ps1`.

## Tasks & Acceptance

**Execution:**
- [x] `src/MediaStation.App/Hosts/Browser/BrowserHost.cs`: the DW-3 rule (`TryMarkExited`, `ExitReported`, `_exitsReported`, the Stop branch, the `exitAlreadyReported` log, cleared on registration). Lets a host report one exit per process.
- [x] `src/MediaStation.Core/Orchestrator/Orchestrator.{cs,Attach.cs,Timers.cs,Failures.cs,Stop.cs}`: the DW-4 flag and compensation, plus the summaries. Hides a late window that would otherwise sit unmuted over the shell.
- [x] `tests/MediaStation.Core.Tests/BrowserHostTests.cs`: the DW-3 rows (watcher first, restart race on `J4Pump`, new start clears) and the DW-4 real-host row. Each new behaviour test must fail with its fix reverted (mutation check, recorded in Implementation Notes).
- [x] `tests/MediaStation.Core.Tests/OrchestratorAttachTests.cs` (or `OrchestratorTimerTests.cs`): the DW-4 Orchestrator rows (late window, other app active, restart after compensation, single use, after `ProcessExited`).
- [x] Re-run the planning probes and copy their JSON lines to Implementation Notes. Fill a "Sweep ledger" table in Implementation Notes: one row per list ID, with the verdict and the concrete evidence (test names that pass now, commits `9aaa811`/`4a7262f`/`6319f01`/`ec04642`/`040fcaf`/`b261608` where a story resolved it, smoke evidence folders).

**Acceptance Criteria:**
- Given the closed list, when the sweep ends, then every ID in it has a ledger row with evidence of execution or a rejection reason, and no other change exists in the diff.
- Given `dotnet build MediaStation.slnx -c Release`, `dotnet test --solution MediaStation.slnx -c Release --no-build` and `npm test` in `web/`, when they run, then they pass. The pre-existing skip is the only one, and no existing BrowserHost test changes.
- Given a published Release build on the current machine, when the tracer, reconnect, errors, adopt and restart smokes run, then each exits 0. The restart smoke still logs `stopping>starting:ProcessExited` in P2 and P5.

## Implementation Notes

**Machine:** ran on UM760 (`MEDIASTATION` / `F7BSI` / `EliteMini Series`), 2026-10-29, baseline HEAD `ec50bad`. Edge `156.0.4314.50`.

**DW-3 (BrowserHost):**
- `Session.TryMarkExited(out CdpSession?)` sets `_stopping` and `_exitReported` under the session `_gate`, but only when the session is not already stopping. `Session.ExitReported` reads the flag.
- `WatchExit` uses `TryMarkExited` in place of the old `Stopping` check plus `MarkStopping()`. When it removes the still-current session from `_sessions`, it adds the `appId` to `_exitsReported` under the host `_gate`.
- Registering a new session clears the set entry, both in a start (`StartCoreAsync`) and in an adoption (`ProbeCoreAsync`).
- `StopCoreAsync` takes the session and removes the set entry under `_gate`. After `StopSessionAsync` it ORs in `session.ExitReported`. If the exit was already reported, it logs `info browser exitAlreadyReported {appId, cmdId}` and posts only `CommandCompleted`. Otherwise it posts `ProcessExited` + `CommandCompleted` as before.
- The class summary now states the rule.

**DW-4 (Orchestrator):**
- New `AppState.LateWindowExpected`. `OnTimerElapsed` sets it only in the start branch, after `EnterFailed`.
- `OnAppWindowAttached` handles `Failed` before the `!= Starting` return: with the flag it calls the new `OnLateWindow`, otherwise it returns `None`.
- `OnLateWindow` clears the flag and records only `RootPid`. It emits `MuteProcessTree(rootPid)`, `MinimizeWindow(hwnd)`, and `ShowShell` only when `!AnotherAppIsActive`. It returns a `null` snapshot and leaves state, `transitionId` and focus alone.
- The flag is cleared in `OnProcessExited` (Failed branch) and in `OnStop` when the stop goes ahead (from `Background` or `Failed`).
- Updated summaries: `OnAppWindowAttached` (the "zostaje w DW-4" wording is gone), `OnTimerElapsed`, `EnterFailed`, plus the new `OnLateWindow` and the field doc.
- No contract, `ErrorCode`, schema or `web/` change.

**Tests added:**
- `BrowserHostTests`:
  - `Stop_after_the_watcher_reported_the_exit_posts_only_completion` (DW-3, watcher first)
  - `Stop_of_a_new_session_after_a_reported_exit_reports_its_exit_again` (DW-3, a new start clears the entry)
  - `Restart_handled_before_the_watchers_exit_reaches_the_orchestrator_brings_a_new_root_to_the_foreground` (DW-3 restart race on `J4Pump`: Background → exit → `RestartApp` before the fact → states `starting, foreground, background, stopping, starting, foreground`, `Launcher.Starts == 2`)
  - `Window_after_the_orchestrators_start_timeout_is_hidden_and_restart_relaunches` (DW-4 real host, `StartingTimeout = 5`, hand-fed `TimerElapsed`)
  - `J4Pump` gained an optional `CoreOrchestrator` constructor parameter. No existing BrowserHost test was changed.
- `OrchestratorAttachTests`:
  - `Late_window_after_start_timeout_is_muted_minimized_and_hidden_behind_the_shell`
  - `Late_window_while_another_app_is_active_does_not_show_the_shell` (`foreground` / `starting`)
  - `Restart_after_late_window_unmutes_before_stop_and_forgets_the_tree_after_it`
  - `Late_window_compensation_is_single_use` (`second` / `afterProcessExited` / `afterClose`)

**Mutation check** (each piece reverted alone, rebuilt, new tests run; all files restored afterwards):

| Mutation | Failing tests |
|---|---|
| M1: Stop ignores `exitReported` (always posts `ProcessExited`) | `Stop_after_the_watcher_reported_the_exit_posts_only_completion`, `Restart_handled_before_the_watchers_exit_…` |
| M2: start registration does not clear `_exitsReported` | `Stop_of_a_new_session_after_a_reported_exit_reports_its_exit_again` |
| M3: attach in `Failed` always ignored (whole DW-4 reverted) | `Window_after_the_orchestrators_start_timeout_…`, `Late_window_after_start_timeout_…`, `Late_window_while_another_app_is_active_…` (both), `Restart_after_late_window_…`, `Late_window_compensation_is_single_use(second)` (its precondition), `OrchestratorHomeTests.Attached_window_after_start_timeout_does_not_revive_the_tile` |
| M4: `OnLateWindow` does not clear the flag | `Late_window_compensation_is_single_use(second)` |
| M5: `ProcessExited` in `Failed` does not clear the flag | `Late_window_compensation_is_single_use(afterProcessExited)` |
| M6: `OnStop` does not clear the flag | `Late_window_compensation_is_single_use(afterClose)` |

The adoption-site clear (`ProbeCoreAsync`) has no dedicated test; it mirrors the start site that M2 pins.

**Verification (UM760):**
- `dotnet build MediaStation.slnx -c Release`: 0 warnings, 0 errors.
- `dotnet test --solution MediaStation.slnx -c Release --no-build`: 1635 total, 1634 passed, 0 failed, 1 skipped (the pre-existing skip).
- `npm test` in `web/`: 14 files, 175 tests passed.
- `dotnet publish src/MediaStation.App -c Release -r win-x64 --self-contained`: exit 0.
- Smokes, all exit 0 (evidence under `%LOCALAPPDATA%\MediaStation\logs\`):
  - tracer: `browserhost-tracer-evidence-20261029-205723`. kiosk + loopback CDP, `pageHello` webdriver=false, again after F5, Home → shell foreground, Edge iconic + visible. Screenshots `after-start.png`, `after-home.png` checked.
  - reconnect: `browserhost-reconnect-evidence-20261029-205746`. `cdpLost` after 30.02 s (17 attempts) → `cdpLostHandled failed`, shell foreground before and after the limit.
  - errors: `browserhost-errors-evidence-20261029-205828`. A/A2/B version logged and confirmed, C `portInUse`, D `profileInUse`, E `cdpUnavailable` at 14.994 s without `ProcessExited`.
  - adopt: `browserhost-adopt-evidence-20261029-205936`. `probeExisting adopted` with no second launch, `renewSession renewing` → `cdpReconnected trigger=renewSession`, `pageHello` after F5.
  - restart: `browserhost-restart-evidence-20261029-205956`. P2 `[failed>stopping:RestartApp, stopping>starting:ProcessExited, starting>foreground:AppWindowAttached, foreground>failed:PageVersionRejected]`, P5 `[failed>stopping:RestartApp, stopping>starting:ProcessExited, starting>foreground:AppWindowAttached]`, pageHello True, roots `[19488]`. Screenshots `p2-appmenu.png` (AppMenu over the "Nie odpowiada" tile) and `p5-foreground.png` (Netflix foreground) checked.
- `git diff --stat cd6a046..HEAD -- src/MediaStation.App/Platform/Input src/MediaStation.Core/Input`: empty (DW-25).
- Live DW-4 late window: not applicable. With default timeouts the Edge window appears within about 0.5 s, so the late path is covered by the real-host fake test above.

**Probe re-run (UM760, Edge 156.0.4314.50, port 9238, scratchpad profiles, run with `pwsh`; under Windows PowerShell 5.1 `-NoProxy` fails silently and every query returns 0 pages):**

`probe-targets.ps1`:
```
{"label":"fresh1-immediate","pages":1,"firstPage":"about:blank","all":"page|about:blank ; service_worker|chrome-extension://ndcpkimcihhghdcddljkfmmjccdmcmof/background.rollup.js"}
{"label":"fresh1-after2s","pages":1,"firstPage":"about:blank","all":"page|about:blank ; service_worker|chrome-extension://ndcpkimcihhghdcddljkfmmjccdmcmof/background.rollup.js"}
{"label":"fresh2-immediate","pages":1,"firstPage":"about:blank","all":"page|about:blank ; service_worker|chrome-extension://ndcpkimcihhghdcddljkfmmjccdmcmof/background.rollup.js"}
{"label":"fresh2-after2s","pages":1,"firstPage":"about:blank","all":"page|about:blank ; service_worker|chrome-extension://ndcpkimcihhghdcddljkfmmjccdmcmof/background.rollup.js"}
{"label":"fresh3-immediate","pages":1,"firstPage":"about:blank","all":"page|about:blank ; service_worker|chrome-extension://ndcpkimcihhghdcddljkfmmjccdmcmof/background.rollup.js"}
{"label":"fresh3-after2s","pages":1,"firstPage":"about:blank","all":"page|about:blank ; service_worker|chrome-extension://ndcpkimcihhghdcddljkfmmjccdmcmof/background.rollup.js"}
{"label":"fresh4-immediate","pages":1,"firstPage":"about:blank","all":"page|about:blank"}
{"label":"fresh4-after2s","pages":1,"firstPage":"about:blank","all":"page|about:blank"}
{"label":"fresh5-immediate","pages":1,"firstPage":"about:blank","all":"page|about:blank"}
{"label":"fresh5-after2s","pages":1,"firstPage":"about:blank","all":"page|about:blank"}
{"label":"tabs-crash-seed","pages":1,"firstPage":"about:blank","all":"page|about:blank"}
{"label":"tabs-crash-kiosk-immediate","pages":1,"firstPage":"about:blank","all":"page|about:blank"}
{"label":"tabs-crash-kiosk-after3s","pages":1,"firstPage":"about:blank","all":"page|about:blank"}
{"label":"tabs-restore-kiosk-immediate","pages":1,"firstPage":"about:blank","all":"page|about:blank"}
{"label":"tabs-restore-kiosk-after3s","pages":1,"firstPage":"about:blank","all":"page|about:blank"}
{"label":"close1","rootsBefore":"9612","closeSent":true,"rootsAfter6s":"","anyProfileProcess":0}
{"label":"close2","rootsBefore":"19980","closeSent":true,"rootsAfter6s":"","anyProfileProcess":0}
{"label":"close3","rootsBefore":"4608","closeSent":true,"rootsAfter6s":"","anyProfileProcess":0}
```

`probe-targets2.ps1`:
```
{"label":"crash-seed","pages":3,"urls":"https://example.org/ , https://example.com/ , about:blank"}
{"label":"crash-kiosk1-immediate","pages":1,"urls":"about:blank"}
{"label":"crash-kiosk1-after4s","pages":1,"urls":"about:blank"}
{"label":"crash-kiosk2-immediate","pages":1,"urls":"about:blank"}
{"label":"crash-kiosk2-after4s","pages":1,"urls":"about:blank"}
{"label":"graceful-seed","pages":3,"urls":"https://example.org/ , https://example.com/ , about:blank"}
{"label":"graceful-kiosk1-immediate","pages":1,"urls":"about:blank"}
{"label":"graceful-kiosk1-after4s","pages":1,"urls":"about:blank"}
{"label":"graceful-kiosk2-immediate","pages":1,"urls":"about:blank"}
{"label":"graceful-kiosk2-after4s","pages":1,"urls":"about:blank"}
```

**Review fixes, round 1 (UM760):**
- **DW-3 ordering race.** Before this fix, the watcher claimed the exit and added the app to `_exitsReported`, but posted `ProcessExited` only after releasing `_gate`. A Stop running in that gap could post `CommandCompleted` before the watcher's fact.
  - Fix: `WatchExit` now does the claim (`TryMarkExited`), the unregister and set entry, the `processExited` log and `_post(ProcessExited)` in one `lock (_gate)` section. `cdp?.Dispose()` and `CancelStart()` stay after it, as before.
  - `StopCoreAsync` reads `session.ExitReported` under `_gate` after `StopSessionAsync`. Its first `_gate` section (`_sessions.Remove` + `_exitsReported.Remove`) is unchanged.
  - The log stays inside the lock, before the post, so the existing log-then-post order is kept. `Process_exit_not_caused_by_stop_reports_process_exited` waits for the post and then asserts the log line.
  - Lock order is always host `_gate` → session `_gate`; session methods never call back into the host.
  - The production `post` is `OrchestratorLoop.Post`, which calls `Add` on an unbounded `BlockingCollection` and swallows `InvalidOperationException`. It never blocks or throws, so posting under the lock is safe.
- **Regression test:** `BrowserHostTests.Stop_during_the_watchers_exit_post_completes_only_after_that_exit`. The Harness got a `BeforePost` hook. The test blocks the watcher's `ProcessExited` post, waits until the hook is entered, sends `StopApp(8)`, waits 200 ms and then releases the post. It asserts that `Posted` ends `ProcessExited, CommandCompleted(8)` and holds exactly one `ProcessExited`. Passed in 5 repeated runs.
- **Pin for "only the Starting timeout sets the flag":** `OrchestratorAttachTests.Attach_after_a_stopping_timeout_is_ignored` (`restart` false/true). From Background, `CloseApp`/`RestartApp` and then the stop `TimerElapsed` → `Failed`; a later `AppWindowAttached` gives no commands and no snapshot.
- **Mutation check:**

| Mutation | Failing tests |
|---|---|
| M7: watcher `_post(ProcessExited)` moved back outside the lock (the pre-fix ordering) | `Stop_during_the_watchers_exit_post_completes_only_after_that_exit` |
| M8: the `Stopping` timeout also sets `LateWindowExpected` | `Attach_after_a_stopping_timeout_is_ignored(restart: False)`, `(restart: True)` |

- **Tests run** (covering the edited files only): `BrowserHostTests`, `OrchestratorAttachTests`, `OrchestratorHomeTests`: 255 passed, 0 failed. Release build: 0 warnings.

**Sweep ledger:**

| ID | Verdict | Evidence |
|----|---------|----------|
| DW-3 | fixed (this story) | BrowserHost `TryMarkExited` / `_exitsReported` / `exitAlreadyReported`. Tests `Stop_after_the_watcher_reported_the_exit_posts_only_completion`, `Stop_of_a_new_session_after_a_reported_exit_reports_its_exit_again`, `Restart_handled_before_the_watchers_exit_reaches_the_orchestrator_brings_a_new_root_to_the_foreground`, `Stop_during_the_watchers_exit_post_completes_only_after_that_exit` (mutations M1, M2, M7). `Stop_without_a_session_still_reports_exit_and_completion` and `Process_exit_not_caused_by_stop_reports_process_exited` still pass. Restart smoke P2/P5 still `stopping>starting:ProcessExited` |
| DW-4 | fixed (this story) | Orchestrator `LateWindowExpected` / `OnLateWindow`. Tests in `OrchestratorAttachTests` (late window, other app active ×2, restart after compensation, single use ×3) `Attach_after_a_stopping_timeout_is_ignored` ×2, and `Window_after_the_orchestrators_start_timeout_is_hidden_and_restart_relaunches` (mutations M3–M6, M8). `Failed_ignores_inputs_without_commands_or_snapshot` still passes. Live check not applicable (window appears in about 0.5 s with default timeouts) |
| DW-39 | resolved in 6.4 (`6319f01`) | `Version_change_is_logged_before_launch_even_when_cdp_then_never_answers`, `Changed_version_is_logged_before_app_launch_and_confirmed_by_get_version`, `Same_version_logs_only_browser_version_before_app_launch` pass. Errors smoke A/A2/B PASS (`browserhost-errors-evidence-20261029-205828`) |
| DW-53 | resolved in 6.4 (`6319f01`) | `Stop_waits_for_the_root_to_exit_after_killing_the_tree`, `Restart_checks_profile_and_port_only_after_the_old_root_has_exited` pass. Restart smoke P2/P5: old root gone, a single root (`[7916]`, `[19488]`), no `launcherExited` (`browserhost-restart-evidence-20261029-205956`) |
| DW-54 | resolved in 6.6 (`040fcaf`) | `Other_major_version_twice_logs_twice_and_posts_page_version_rejected_once`, `Other_major_version_after_stop_posts_nothing` (BrowserHostTests AD-7/DW-54 block). `OrchestratorFailureTests` `pageVersionRejected` cases and `OrchestratorLoopTests.Ignored_page_version_rejected_logs_handled_with_a_null_state` pass. Restart smoke P1/P2/P4 `foreground>failed:PageVersionRejected` |
| DW-55 | resolved (settle condition met), 6.5 (`ec04642`) / 6.6 (`040fcaf`) | Adopt smoke (`browserhost-adopt-evidence-20261029-205936`): `renewSession renewing` → `cdpReconnected trigger=renewSession`, then F5 → `pageHello x1` on the new session. The "existing document" variant has no trigger (the runtime sends only `hello`; drivers belong to epic-netflix-dpad) |
| DW-52 | false (probe) | `probe-targets.ps1` fresh1–5 and `probe-targets2.ps1` crash/graceful (seeded with 3 pages, kiosk relaunched twice each): every kiosk start has exactly one `page` target, `about:blank`, both immediately and 2–4 s later. No code change |
| DW-57 | false (probe) | `probe-targets.ps1` close1–3: `CloseMainWindow` (WM_CLOSE) on the kiosk root; after 6 s `rootsAfter6s` is empty and `anyProfileProcess` is 0. No code change |
| DW-56 | rejected: not a sweep fix | Recovery needs `Page.reload`/`Runtime.evaluate` or a frame-tree check (new CDP method), outside AD-1. Product/architecture decision (bmad-architecture). Stays open |
| DW-36 | partly resolved (6.2, `9aaa811`), rest rejected | `CompositionRoot.CreateWebHost` gives web tiles the real `BrowserHost`. Moonlight/Native still use `MissingHostPort` until their host epics. A `HostMissing` `ErrorCode` is an AD-4 contract change, outside a sweep |
| DW-40 | rejected: needs hardware | Multiple monitors, a TV resolution change and TV off are operator hardware states (UM760 has one output). The OSK overlay is epic-osk, and display-off behaviour belongs to WindowManager. Listed for the whole-epic UM760 acceptance |
| DW-25 | rejected: not triggered | `git diff --stat cd6a046..HEAD -- src/MediaStation.App/Platform/Input src/MediaStation.Core/Input` is empty (re-run 2026-10-29). Stays with epic-osk |
| E-F6 | resolved in 6.6 (`040fcaf`) | `PageEnvelope.Id`. `Hello_envelope_v1_is_parsed`, `Bad_page_messages_are_rejected_with_a_reason`, `Other_integer_major_version_is_rejected_as_version_with_the_value` (`PageProtocol.Parse`) pass |
| E-F18a | resolved in 6.2 (`9aaa811`) | `ConfigValidationTests.Web_service_must_be_kebab_case_because_it_is_a_path_segment` passes |
| E-F18b | resolved in 6.2/6.3 (`9aaa811`/`4a7262f`) | `ConfigValidationTests.Timeout_above_600_seconds_is_rejected` cases pass (`cdpConnectTimeout < startingTimeout ≤ 600`, `cdpReconnectTimeout ≤ 600`) |

DW-53 is also exercised by the 6.7 J4 tests (`b261608`).

## Plan Change Log

- 2026-10-29 (UM760): `OrchestratorHomeTests.Attached_window_after_start_timeout_is_ignored` (story 4.2) asserted the exact behaviour DW-4 replaces: an attach after the start timeout gave no commands. The plan did not list it. It was renamed to `Attached_window_after_start_timeout_does_not_revive_the_tile`. Its intent is kept: no activation, unmute or routing, and a `null` snapshot. It now expects the DW-4 compensation (`MuteProcessTree`, `MinimizeWindow`, `ShowShell`). This is the only existing test changed, and it is not a BrowserHost test.

## Review Triage Log

### 2026-10-29 — Review pass
- verdicts: 21 findings — high 0, medium 2, low 7, false 12, maybe-false 0
- findings:
  - `[medium]` `[patch]` (blind-hunter) DW-3 ordering race: the watcher claims the exit and fills `_exitsReported` before it posts `ProcessExited`, so a Stop in that gap can post `CommandCompleted` first. The Orchestrator then restarts on `CommandCompleted`, and the late `ProcessExited` fails the new start. — Fixed: in `WatchExit`, the claim, the unregister/set entry, the log and the post now run in one `_gate` section, and `StopCoreAsync` reads `session.ExitReported` under `_gate`. Test `Stop_during_the_watchers_exit_post_completes_only_after_that_exit` (with the watcher's post held while Stop runs) fails on the old ordering (M7).
  - `[low]` `[reject]` (blind-hunter) The `session.ExitReported` branch (Stop removes the session before the watcher claims the exit) and the adoption-site clear have no test. — Reaching the first needs an interleaving inside `StopSessionAsync` that the fakes cannot force; the patch above makes it ordered by construction. The adoption clear is reachable only on startup probes (`OrchestratorStarted` once), when no entry exists yet. A seam for either adds test hooks for paths users will not hit.
  - `[false]` `[reject]` (blind-hunter) `LateWindowExpected` stays set after the host's own start ends without a window, so any later attach in `Failed` would be compensated. — No later `AppWindowAttached` is reachable while the app stays `Failed`: `LaunchApp` is ignored in `Failed` (`Failed_ignores_inputs_without_commands_or_snapshot("launch")`), a restart or close goes through `OnStop` (which clears the flag), and probes run only at startup from `NotRunning`.
  - `[low]` `[reject]` (blind-hunter) `OnLateWindow` with another app in front does not re-activate that app after minimizing the late window. — This needs a non-default `startingTimeout` < 20 s, plus a second app launched before the late window. `OnAdopt` uses the same rule, and story 5.9's `ForegroundChanged` policy re-evaluates input after a foreground change. The fix adds a branch and commands.
  - `[low]` `[reject]` (blind-hunter) The DW-4 compensation has no dedicated log entry. — The commands it sends are traced by their executors (`audioRuleApplied`, window commands). An Orchestrator-loop log entry adds detection code for diagnostics only.
  - `[low]` `[patch]` (blind-hunter) Nothing pins that only the `Starting` timeout sets the flag. — Added the theory `Attach_after_a_stopping_timeout_is_ignored` (`CloseApp`/`RestartApp`); mutation M8 (stop timeout also sets the flag) fails both cases.
  - `[low]` `[reject]` (blind-hunter) The "nothing else posted" checks use a 100 ms delay. — This is the same pattern as the existing `Stop_closes_cdp_window_and_tree_then_reports_exit_and_completion`. In these tests the watcher's fact is already posted before Stop, so nothing is left to post late, and the restart test uses `AssertQuiet`.
  - `[low]` `[reject]` (blind-hunter) The rewritten `OrchestratorHomeTests` test repeats the exact-sequence check and nearly duplicates the new attach test. — Cosmetic; its `DoesNotContain` line states the "does not revive" intent.
  - `[false]` `[reject]` (blind-hunter) The `_exitReported` comment ("set under `_gate`") contradicts its lock-free volatile read. — It is set only inside `TryMarkExited` under the session `_gate`; reading a volatile flag after `MarkStopping` (now under the host `_gate`) does not contradict that.
  - `[medium]` `[patch]` (edge-case-hunter) Late watcher post after Stop's `CommandCompleted`. — Same root cause and fix as the first row.
  - `[false]` `[reject]` (edge-case-hunter) Claim that no existing test changed, while `OrchestratorHomeTests` was rewritten. — The Plan Change Log records that change, and the AC limits "no change" to BrowserHost tests.
  - `[medium]` `[patch]` (verification-gap, other) The DW-3 race window between claim and post. — Same root cause and fix as the first row.
  - `[low]` `[defer]` (verification-gap, other) `DevHarnessHostPort` still posts `ProcessExited` on every Stop after its watcher reported the exit (DW-3 pattern). — Pre-existing, dev-only harness; the plan's Never keeps it unchanged for the accepted epic-5 checks. Added to `deferred`.
  - `[false]` `[reject]` (intent-alignment) Changing BrowserHost's fact stream contradicts "bez zmiany zachowania BrowserHost". — The ticket's references assign DW-3 to this sweep, and the ledger settles DW-3 only by a host guarantee, a behaviour change in the race path. The epic Note says the final sweep closes the technical debt, and 6.7 hands DW-3/DW-4 to 6.8. The specified paths are unchanged: all existing BrowserHost tests and five smokes pass, and the restart smoke still logs `stopping>starting:ProcessExited`.
  - `[false]` `[reject]` (intent-alignment) DW-4 makes a decision the ledger calls "osobna story". — The later decision record ("Decide with epic-browserhost", 2026-10-28), the epic Notes (DW-4 "z decyzją o Hwnd w Failed" assigned to this epic) and 6.7's Never all hand it to 6.8. AD-14/15 leave only the Orchestrator-side option.
  - `[false]` `[reject]` (intent-alignment) DW-3 dedupes per session/appId, not by pid/`rootPid`, and the two-facts test is not a loop test. — The ledger gives pid/`rootPid` as examples of "ProcessExited refers to the current process", and one report per session process meets that. The two-facts-for-one-stop test exists (`OrchestratorStopTests`, 6.7).
  - `[false]` `[reject]` (intent-alignment) Evidence for 13 IDs lives only in the untracked plan. — The plan is committed with the change at finalize.
  - `[false]` `[reject]` (intent-alignment) "Scope pushed from another story becomes a new entry", but no entry was created. — Build-auto never writes ticket files. The pushed item (dev-harness web retirement) is reported for the PO in the Auto Run Result.
  - `[false]` `[reject]` (intent-alignment) No structural refactor in the diff. — Refactor-type items had no trigger (DW-25: empty diff on Input), and the sweep may not add items outside the list.
  - `[false]` `[reject]` (intent-alignment) DW-4 is checked only at command level, with no live run. — A live late window needs a non-default timeout (the Edge window appears in ~0.5 s). The real-host fake test drives BrowserHost and the Orchestrator together, and the executors' minimize and mute are proven live by the tracer, errors and restart smokes.
  - `[false]` `[reject]` (intent-alignment) "Fixed at the start" is not demonstrable. — The plan with the closed list was written and set `ready-for-dev` before any code change, at baseline `ec50bad`.

## Design Notes

- **"No change to BrowserHost behaviour"** is read as no change in the specified 6.2–6.7 paths. DW-3's fix changes only the race path, where the watcher already reported the exit. There, the second `ProcessExited` is what made the restarted app fail, and `CommandCompleted` alone closes the stop (`OnCommandCompleted` → `OnStopped`).
- **Why a set and not just a session flag:** the watcher can unregister the session before Stop looks it up, and Stop then finds no session. The set covers that order; the flag covers the order where Stop removed the session first.
- **Why DW-4 lives in the Orchestrator:** a host cannot minimize (AD-14/15). With the defaults (`startingTimeout` 30 s, BrowserHost's window wait 20 s), BrowserHost never attaches late. It can with `startingTimeout` < 20 s, or with any other host. The compensation mirrors `OnAdopt` (AD-5 order: mute → minimize → shell), while `Failed` keeps its rule that it has no `Hwnd`.

## Verification

**Commands:**
- `powershell -NoProfile -Command "$env:COMPUTERNAME; (Get-CimInstance Win32_BaseBoard).Product; (Get-CimInstance Win32_ComputerSystem).Model"`: record the machine (planned on UM760: `MEDIASTATION`/`F7BSI`/`EliteMini Series`).
- `dotnet build MediaStation.slnx -c Release`: 0 warnings, 0 errors.
- `dotnet test --solution MediaStation.slnx -c Release --no-build`: all pass.
- `npm test` in `web/`: passes.
- `dotnet publish src/MediaStation.App -c Release -r win-x64 --self-contained`: exit 0.
- `powershell -NoProfile -ExecutionPolicy Bypass -File tools/check-browserhost-<name>.ps1` for `tracer`, `reconnect`, `errors`, `adopt` and `restart`: exit 0 each. Record the evidence folders and screenshots.
- `git diff --stat cd6a046..HEAD -- src/MediaStation.App/Platform/Input src/MediaStation.Core/Input`: empty (DW-25).
- The two probe scripts: the output matches the DW-52/DW-57 rows.

**Manual checks (if no CLI):**
- Smoke screenshots: tracer after start/Home; restart `p2-appmenu.png` and `p5-foreground.png`.
- A live DW-4 late window is not reproducible with default timeouts (the Edge window appears in about 0.5 s). Record it as not applicable and rely on the real-host fake test.

## Auto Run Result

Status: built (ran on **UM760**: `MEDIASTATION` / `F7BSI` / `EliteMini Series`, Edge 156.0.4314.50)

**Summary.** The closed list of 15 IDs was fixed at the start (baseline `ec50bad`). The Sweep ledger in Implementation Notes gives each ID evidence or a rejection reason.
- **Fixed in this story (2):**
  - **DW-3:** BrowserHost now posts `ProcessExited` at most once per process. The exit watcher claims the exit atomically and posts it inside the host lock. A Stop that comes after a reported exit logs `exitAlreadyReported` and posts only `CommandCompleted`.
  - **DW-4:** a window that attaches after the `Starting` timeout gets one compensation (mute → minimize → `ShowShell` when no other app is active). The tile stays `Failed`, and its `RootPid` is kept so a restart or close unmutes and forgets the tree.
- **Resolved earlier, with evidence:** DW-39, DW-53, DW-54, DW-55, E-F6, E-F18a, E-F18b.
- **False, by a UM760 probe:** DW-52 (a kiosk start always has exactly one `about:blank` page target, even on profiles with restored tabs) and DW-57 (closing the kiosk window leaves no profile process).
- **Rejected, with reasons:**
  - DW-56: needs an AD-1 or product decision.
  - DW-36: resolved for web by BrowserHost; Moonlight/Native wait for their hosts, and an `ErrorCode` change is an AD-4 contract change.
  - DW-40: needs operator hardware.
  - DW-25: not triggered (no Input change in this epic).

**Files changed.**
- `src/MediaStation.App/Hosts/Browser/BrowserHost.cs`: `Session.TryMarkExited`/`ExitReported`, the `_exitsReported` set, an atomic watcher claim and post, and the Stop branch with `exitAlreadyReported`.
- `src/MediaStation.Core/Orchestrator/Orchestrator.cs`: `AppState.LateWindowExpected`.
- `src/MediaStation.Core/Orchestrator/Orchestrator.Attach.cs`: the `Failed` branch and `OnLateWindow`.
- `src/MediaStation.Core/Orchestrator/Orchestrator.Timers.cs`, `Orchestrator.Failures.cs`, `Orchestrator.Stop.cs`: the flag is set by a `Starting` timeout and cleared by `ProcessExited` in `Failed` and by a stop; summaries updated.
- `tests/MediaStation.Core.Tests/BrowserHostTests.cs`:
  - DW-3: watcher first, a new start clears the entry, the restart race on `J4Pump`, and the held watcher post.
  - DW-4: on the real host.
  - Supporting changes: `J4Pump` takes an optional orchestrator, and the harness has a `BeforePost` hook.
- `tests/MediaStation.Core.Tests/OrchestratorAttachTests.cs`: the DW-4 Orchestrator rows and the stop-timeout theory.
- `tests/MediaStation.Core.Tests/OrchestratorHomeTests.cs`: a story 4.2 test that pinned the old DW-4 behaviour was renamed and updated (recorded in the Plan Change Log).

**Review findings.** 21 findings: high 0, medium 2, low 7, false 12, maybe-false 0.
- **Patched (2 entries):**
  - medium: the DW-3 claim → post ordering race, raised by blind-hunter, edge-case-hunter and verification-gap;
  - low: a test that the stop timeout does not set the flag.
- **Deferred (1):** `DevHarnessHostPort` still double-posts `ProcessExited` (pre-existing, dev-only, low).
- **Rejected:**
  - low:
    - the untested session-flag and adoption-clear branches;
    - re-activating a foreground app after the late minimize;
    - no DW-4 log entry;
    - the 100 ms quiet checks;
    - the duplicated Home test.
  - false:
    - the flag outliving a window-less start;
    - the `_exitReported` comment;
    - the claim that no test changed;
    - six intent-alignment readings: the BrowserHost behaviour clause, the DW-4 decision owner, dedupe by pid, evidence only in the plan, no new ticket entry, no refactor;
    - a command-level-only DW-4 check;
    - "fixed at the start" not demonstrable.
  - The reasons are in the triage log.

**Follow-up review recommended:** false. This was the first pass, and one medium and one low entry were patched (patched counts: high 0, medium 1, low 1). The patch closes the race by construction and is pinned by a test that fails on the old ordering (M7).

**Verification (UM760, 2026-10-29, after the patches).**
- `dotnet build MediaStation.slnx -c Release`: 0 warnings, 0 errors.
- `dotnet test --solution MediaStation.slnx -c Release --no-build`: 1638 total, 1637 passed, 1 skipped (the pre-existing DW-1 skip), 0 failed.
- `npm test` in `web/`: 175 passed.
- `dotnet publish src/MediaStation.App -c Release -r win-x64 --self-contained`: exit 0.
- Smokes, all exit 0, with evidence under `%LOCALAPPDATA%\MediaStation\logs\`:
  - tracer: `browserhost-tracer-evidence-20261029-212524`, screenshots `after-start.png` and `after-home.png`.
  - restart: `browserhost-restart-evidence-20261029-212544`. P2 and P5 still log `stopping>starting:ProcessExited`. Screenshots `p2-appmenu.png` and `p5-foreground.png`.
  - adopt: `-212623`.
  - reconnect: `-212641`.
  - errors: `-212726`.
- The same five smokes also passed before the patches (`…-211227` to `…-211427`), and in the implementer's own run (`…-2057xx`).
- `git diff --stat cd6a046..HEAD -- src/MediaStation.App/Platform/Input src/MediaStation.Core/Input`: empty.
- Probe scripts (DW-52/DW-57): output recorded in Implementation Notes.
- Not applicable:
  - A live DW-4 late window: with default timeouts the Edge window appears in about 0.5 s; covered by the real-host fake test.
  - A live DW-3 race: it cannot be forced on a real process; covered by the held-post test.
- Matrix audit: every Matrix row has a passing test; see the ledger and test lists in Implementation Notes.

**Residual risks and PO follow-ups.**
- **Close in `deferred-work.md`:** DW-3 and DW-4 (fixed); DW-39, DW-53, DW-54 and DW-55 (resolved); DW-52 and DW-57 (false).
- **Keep open with new owners:**
  - DW-56 → bmad-architecture (AD-1 decision);
  - DW-36 → the Moonlight/Native host epics;
  - DW-40 → the whole-epic UM760 acceptance and epic-osk;
  - DW-25 → epic-osk.
- **Epic Notes:** the deferred lines F6, F18/service and F18/timeouts are closed in code (E-F6, E-F18a, E-F18b), and the Notes should say so.
- **Scope pushed from 6.2:** the decision on retiring the dev harness's web path is a candidate new entry, not part of this sweep.
- **Whole-epic acceptance on UM760 (user, `fp-acceptance`), DW-40 checks:**
  - Kiosk Edge after a TV resolution change, and after turning the TV off and on: the window still covers the screen, and Home still brings the shell back.
  - With a second monitor connected, if one is available.
