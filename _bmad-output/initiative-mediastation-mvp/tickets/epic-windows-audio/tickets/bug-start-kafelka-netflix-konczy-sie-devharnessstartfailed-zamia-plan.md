---
title: "Start kafelka Netflix kończy się DevHarnessStartFailed zamiast otwarciem Edge"
type: 'bugfix'
ticket: 14
created: '2026-10-28'
status: done
baseline_revision: 'e9a648b51162ea1d1ea62eaf3d86473ab58fa69a'
route: 'full'
route_source: 'auto'
risk: 'high'
review: 'thorough'
review_source: 'pinned'
lenses_ran: [blind-hunter, edge-case-hunter, verification-gap, intent-alignment]
review_loop_iteration: 0
followup_review_recommended: false
context: []
warnings: []
deferred: []
---

<intent-contract>

## Intent

**Problem:** OK on the Netflix tile ends with `devHarnessStartFailed reason=windowTimeout` ~160 ms later and no Edge window. Live on UM760 (2026-10-28): when an Edge instance already holds `profiles\netflix`, a second `msedge.exe --user-data-dir=...netflix ...` forwards the request to it and exits with code 0 after ~120 ms; the harness sees the launcher exit, gives up, and kills nothing. A fresh profile start works (window `about:blank — Edge`, launcher stays alive).

**Approach:** Treat a launcher that exits 0 as a possible handoff: keep waiting and look for the window in the other processes carrying the profile; adopt an already running profile instance (with a window) instead of launching again. Log the web launch (command, args, cwd, pid, exit code) and a `failedStage` on timeout so a handoff is distinguishable from a window-pick failure or a WindowManager error.

## Boundaries & Constraints

**Always:** Harness never changes window visibility or foreground (R4). Keep `devHarnessStartFailed reason=windowTimeout`. Moonlight behaviour (tickets 12, 13) unchanged. Non-zero launcher exit still fails fast.

**Never:** Change `Platform/Window`, WindowManager, Orchestrator, or the Edge arguments (`--kiosk` from 6.1). Do not kill an existing profile instance to force a fresh launch.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Fresh profile | no process with the profile | launch, window in launcher tree, `appWindowAttached` | n/a |
| Running instance | profile process with a window exists, no own session | adopt it, no new process, `appWindowAttached adopted=true` | n/a |
| Launcher handoff | launcher exits 0, window appears in another profile root | `appWindowAttached` with that root as `rootPid` | n/a |
| Launcher crash | launcher exits non-zero | fail fast, `failedStage=launcherExited` | `windowTimeout` |
| Handoff, no window | launcher exits 0, no window until timeout | `failedStage=launcherHandoff` | `windowTimeout` |
| Alive, no window | launcher alive, no window until timeout | `failedStage=windowAttach` | `windowTimeout` |

</intent-contract>

## Code Map

- `src/MediaStation.App/Hosts/DevHarness/DevHarnessHostPort.cs` -- `StartCore` (~214), `WaitForWindow` (~387), `LogStage` (~443): adoption before launch, handoff in the wait, web `appLaunch` and `failedStage`.
- `src/MediaStation.App/Hosts/DevHarness/HarnessLaunch.cs` -- pure decisions: `LauncherExitIsHandoff`, `WebFailedStage`.
- `tests/MediaStation.Core.Tests/DevHarnessTests.cs` -- tests next to `HarnessLaunch`/`HarnessWait` ones (~223, ~360).
- `HarnessWait.cs`, `HarnessBrowser.cs` -- reuse as is (`MatchingRoots` main-before-helper).

## Tasks & Acceptance

**Execution:**
- [ ] `HarnessLaunch.cs` -- add `LauncherExitIsHandoff(app, exitCode)` and `WebFailedStage(exited, exitCode)` -- pure
- [ ] `DevHarnessHostPort.cs` -- adopt a running profile window before launch (no previous own session); in the wait, after launcher exit 0 search other profile roots and repoint `session.Process`; log web `appLaunch` + `failedStage`/`exitCode`
- [ ] `DevHarnessTests.cs` -- tests for every matrix row that is pure, plus the handoff through `HarnessWait`
- [ ] Live on UM760: Release publish, `MEDIASTATION_DEV_HARNESS=1`, OK on Netflix with a clean profile and with a leftover instance, then Home + OK

**Acceptance Criteria:**
- Given a fresh publish and profile `netflix`, when OK is pressed, then Edge opens and the log has `appWindowAttached` for `netflix`, no `windowTimeout`.
- Given a profile instance already running, when OK is pressed, then no second browser process starts and `appWindowAttached` follows.
- Given a failed start, when the timeout fires, then `appLaunchStage` carries `failedStage`, pid, `exitCode`, and `appLaunch` the command, args and cwd.
- Given the suite, when it runs, then it passes with the new tests.

## Implementation Notes

Ran on UM760 (host MediaStation, board F7BSI, model EliteMini Series). Root cause reproduced live before any code: a second `msedge.exe --user-data-dir=profiles
etflix ...` while that profile is held forwards the request and exits with code 0 after ~118 ms (the failed entries came 160 ms after OK). Fix in `DevHarnessHostPort` (adopt a running profile instance with a window before launching; keep waiting after a launcher exit 0 and search the other profile roots; `appLaunch` and `failedStage`/`exitCode` for web) and pure decisions in `HarnessLaunch` (`LauncherExitIsHandoff`, `WebFailedStage`, `FindWindow` wiring). New `tools/check-web-start.ps1` is the live check. Review patches applied by the orchestrator (not the implementer subagent).

## Plan Change Log

## Review Triage Log

### 2026-10-28 — Review pass
- verdicts: 40 findings (blind 9, edge-case 13, verification-gap 5, intent-alignment 4 readings/divergences, rest merged) — high 0, medium 2, low 6, false 5, maybe-false 0; rows below, duplicates share verdicts
- findings:
  - `[medium]` `[patch]` `Process.GetProcessById` unguarded in `WaitForWindow` (blind, edge-case, verification-gap) — guarded: `ArgumentException` returns no window.
  - `[medium]` `[patch]` New wait wiring untested, test only re-tested `HarnessWait` (blind, edge-case, verification-gap, intent) — wiring moved to `HarnessLaunch.FindWindow`; four tests (exit 0 finds other root, exit 1 stops at once, alive launcher never searches others, own tree preferred).
  - `[low]` `[patch]` Stop during handoff kills the exited launcher, not the repointed root (blind) — failure branch kills `session.Process ?? started`.
  - `[low]` `[reject]` Adoption/`WebFailedStage`/`appLaunch` log call sites untested (verification-gap, edge-case) — covered live by `tools/check-web-start.ps1`; extracting the dictionary builder adds complexity for diagnostics.
  - `[false]` `[reject]` Web timeout drops the window-tree evidence (edge-case) — only `TimeoutDiagnostics` (Moonlight hints) is skipped; `windows` is still logged.
  - `[false]` `[reject]` Adoption ignores the CDP port / service (blind, edge-case) — dev harness opens no CDP port (epic Decision 2026-10-26) and checks windows only.
  - `[false]` `[reject]` Stop kills the user's pre-existing instance (edge-case) — it is the harness-owned `profiles\<id>` profile, which Stop is meant to close.
  - `[false]` `[reject]` `LauncherFailed` vs `LauncherHandedOff` disagree on unreadable exit code (verification-gap) — both treat unreadable as failure/no handoff; now one `ExitCodeOf` null means alive.
  - `[false]` `[reject]` Intent: clean-profile failure not addressed (intent-alignment) — live clean pass passes; failure mechanism reproduced and fixed for a held profile.
  - `[low]` `[reject]` `_commandLines()` WMI scan every 100 ms after a launcher exit 0 (blind, edge-case) — bounded to 20 s on a rare path; throttle adds state.
  - `[low]` `[reject]` Exit 0 with no other root waits the full 20 s (blind) — `failedStage=launcherHandoff` explains it; unchanged timeout.
  - `[low]` `[reject]` Pre-launch adoption probe exception aborts start (edge-case) — outer catch already reports `devHarnessCommandFailed`; WMI failure is not reachable in normal use.
  - `[low]` `[reject]` Stage misclassified when launcher exits 0 exactly at the deadline (edge-case) — negligible.
  - `[low]` `[reject]` `appLaunch` quoting, `WebFailedStage` parameter shape, no `appLaunch` for adoption (blind, edge-case) — same format as the existing Moonlight entry; adoption logs `adopted=true`.
  - `[low]` `[reject]` Script: hard-coded Edge path, missing `apps.json`, force-kill of the harness profile, full-log re-read, keyboard injection (blind, edge-case) — same pattern as `tools/check-moonlight-return.ps1`, dev-only tool for this machine.

## Auto Run Result

Status: built

- **Summary:** OK on the Netflix tile failed because a second `msedge.exe` on a held profile exits with code 0 after ~120 ms and the harness treated the launcher exit as a failure. The harness now adopts a running profile instance (no second process), keeps waiting after a launcher exit 0 and searches the other profile roots, and logs the web launch plus `failedStage`/`exitCode` on timeout.
- **Files changed:** `src/MediaStation.App/Hosts/DevHarness/DevHarnessHostPort.cs` (adoption, handoff wait, logging); `HarnessLaunch.cs` (`LauncherExitIsHandoff`, `WebFailedStage`, `FindWindow`); `tests/MediaStation.Core.Tests/DevHarnessTests.cs` (7 tests); `tools/check-web-start.ps1` (new live check).
- **Review:** 2 medium and 1 low patch applied, none deferred; rejected findings and reasons in the triage log.
- **Follow-up review recommended:** false (patched: medium 2, low 1).
- **Verification (UM760):** `dotnet build MediaStation.slnx` 0 errors, 0 warnings; `dotnet test MediaStation.slnx` 1282 total, 1281 passed, 1 skipped, 0 failed. Live on a Release publish: `tools/check-web-start.ps1` PASS twice (before and after the review patches): clean profile gives one `appWindowAttached`; a profile instance started after MediaStation gives `appWindowAttached adopted=true` with the same `rootPid`; 2 Home/OK cycles each (minimized and visible, restored on top), no `processExited`, no warn/error.
- **Residual risks:** the exact earlier-session state that held the `netflix` profile at 14:03 (2026-10-28) is not known; the reproduced mechanism and the 160 ms signature match. The post-launch handoff search (launcher exit 0, window in another root) is covered by unit tests only, not live. Whole-epic S9 on UM760 is the user's acceptance.

## Design Notes

Evidence: `msedge.exe` relaunch on a held profile exited with code 0 after 118 ms; the failure log entries came 160 ms after OK (no 20 s wait), i.e. `process.HasExited` ended the wait.

## Verification

**Commands:**
- `dotnet build MediaStation.slnx` -- expected: 0 errors
- `dotnet test MediaStation.slnx` -- expected: all pass

**Manual checks (if no CLI):**
- Logs in `%LOCALAPPDATA%\MediaStation\logs`: `appLaunch`, `appWindowAttached` for `netflix`.
