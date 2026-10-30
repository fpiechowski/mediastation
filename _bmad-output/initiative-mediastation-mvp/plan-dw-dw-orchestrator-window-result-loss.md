---
title: 'DW-49: lost window-command result must not block foreground evaluation for good'
type: 'bugfix'
ticket: ''
created: '2026-10-30'
status: 'built'
baseline_revision: 'b4803b670c2beb74dd48b8f40bfc71d5bb9ec3d5'
route: 'full'
route_source: 'auto'
risk: 'medium'
review: 'thorough'
review_source: 'pinned'
lenses_ran:
  - blind-hunter
  - edge-case-hunter
  - verification-gap
  - intent-alignment
review_loop_iteration: 0
followup_review_recommended: false
context: []
warnings:
  - oversized
deferred:
  - summary: >-
      The startup ReportCurrent from the window-manager thread can be dropped, because the port starts before the loop is assigned to postsTo.
    evidence: |-
      CompositionRoot.cs:267 posts through `postsTo?.Post`, :269 starts the thread, :288 assigns `postsTo`, so a report before that line is discarded. Five smoke runs had no startup foregroundChanged, which contradicts the AD-15 startup report. Read from the code; no test confirms the ordering. Pre-existing, not caused by DW-49. Without that report the first evaluation waits for the next real foreground change.
    location: >-
      src/MediaStation.App/CompositionRoot.cs:269
    severity: medium
  - summary: >-
      Raising the shell with SetForegroundWindow changes the foreground without a foregroundChanged for the shell.
    evidence: |-
      Seen in three smoke runs. In the final one (pid 22872) GetForegroundWindow returned the shell after the raise, and foregroundGate at 15:17:18 reported the shell as managed, yet the log has no foregroundChanged for it: the only entries are the helper form at 15:17:15 and a foreign window at 15:17:25 after the form closed. Cause not investigated; the hook code was not changed. Settled by a repeatable run comparing GetForegroundWindow with the hook's entries for each raise, including a real Alt+Tab, since only SetForegroundWindow from a script was seen. Recovery in DW-49 depends on observations, so a missed event delays it.
    location: >-
      src/MediaStation.App/Platform/Window/WinEventForegroundHook.cs
    severity: medium (unverified)
---

<intent-contract>

## Intent

**Problem:** A window command whose result never arrives stays in `_pendingWindow` until the next `transitionId`. While it stays, `IsHardBlocked` holds and every settled `ForegroundChanged` is dropped, so Alt+Tab leaves mode and audio stale. A lost `ReportForeground` blocks the same way through the `_pendingWindow.Count > 0` branch of `OnSettled`.

**Approach:** Pin the block with a test, then bound it: when an evaluation is waiting on window results, a `TimerElapsed` deadline (`windowResultTimeout`) drops the pending set as lost, logs it, and resumes through the existing `ReportForeground` path. Executors stay as they are (AD-4).

## Boundaries & Constraints

**Always:** Time reaches Core only through `TimeProvider` and `TimerElapsed` (AD-14). A saved observation that was hard-blocked is never evaluated; recovery gets a fresh one from `ReportForeground` (AD-15). Recovery is bounded by construction: a lost report with nothing waiting arms no timer and sends no further report. The timer is armed only while an evaluation is waiting, so a normal Home or launch emits no extra commands.

**Never:** No change to executors or to the AD-4 rule that they always answer. No per-command retries of `ActivateWindow`, `MinimizeWindow` or `ShowShell`. No state rollback on loss. No `Thread.Sleep` or wall clock in tests. No edit of the deferred-work ledger.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Block is real | Result dropped, mismatching observation settles, repeatedly | No switch, no report, until the deadline | Pinned by test |
| Recovery | Deadline elapses with a non-report command pending | `WindowResultsLost` holds the cmdIds, one `ReportForeground`, settle timer cancelled; the fresh observation switches | Warn `windowResultLost` |
| Result in time | Last result arrives before the deadline | Deadline cancelled, nothing marked lost | No error expected |
| Report lost, observation kept | Only a report pending, deadline elapses | Settle timer restarted, no second report; the kept observation is evaluated | Warn `windowResultLost` |
| Report lost, nothing waiting | Only a report pending, no observation | No deadline timer, no commands | Next observation arms it |
| Hung executor | Recovery report also gets no answer | No further timer or report until a new observation | Bounded |
| Late result | Result of a dropped command after the deadline | No effect | Ignored |
| New transition | Home before the deadline | Old pending dropped, old deadline cancelled; stale `TimerElapsed` has no effect | No error expected |
| New window command | Added while the deadline runs | Deadline restarted, so young commands are not marked lost | No error expected |
| App in `Starting` at the deadline | Deadline elapses, app still starting | Pending dropped, no report until `Starting` ends, then one report | Existing path |

</intent-contract>

## Code Map

- `src/MediaStation.Core/Orchestrator/Orchestrator.Foreground.cs` -- `IsHardBlocked`, `RestartSettleTimer`, `ApplyForegroundPolicy` (report emission at l.121 reuses `_reportNeeded`), `OnSettled`. Put the deadline logic here.
- `src/MediaStation.Core/Orchestrator/Orchestrator.cs` -- `OrchestratorResult` (add `WindowResultsLost`, default empty, like `Actions`), state fields l.73-80, ctor reads settings l.90, `ForegroundTimerOwner` const.
- `src/MediaStation.Core/Orchestrator/Orchestrator.Timers.cs` -- `OnTimerElapsed`: dispatch the deadline next to the settle check.
- `src/MediaStation.App/Orchestrator/OrchestratorLoop.cs` -- l.241 `foregroundTransitionFailed`: log `windowResultLost` the same way.
- `src/MediaStation.Core/Config/Settings.cs`, `ConfigValidation.cs` (`CheckSettings`), `tools/samples/config/settings.json` -- new setting, in whole seconds like `StoppingTimeout`. `web/contracts` is generated and untracked.
- `tests/MediaStation.Core.Tests/OrchestratorForegroundTests.cs` -- `TimerOf` must filter `Owner == "foreground"`, since a blocked `Observe` now also schedules the deadline. Nothing else there should need to change.
- `tests/MediaStation.Core.Tests/OrchestratorLoopTests.cs` -- l.390-433 is the log-assertion pattern; l.447-455 wires `TimerScheduler` + `FakeTimeProvider`.
- `tests/MediaStation.Core.Tests/ConfigValidationTests.cs` -- defaults l.127, explicit l.139/156, positive l.1052, 600 bounds l.1086-1111, schema field order l.1363.
- `_bmad-output/initiative-mediastation-mvp/architecture-mediastation/architecture-mediastation.md` -- AD-15 "Kiedy nie oceniać" l.410, settings l.317.
- Do not change: `Win32WindowManagerPort`, `WindowCommandExecutor`, `CommandRouter`, the `deferred-work.md` ledger.

## Tasks & Acceptance

**Execution:**
- [x] `tests/MediaStation.Core.Tests/OrchestratorForegroundTests.cs` -- first add a test that drops a result and asserts repeated settles have no effect and the next transition unblocks; run it on the baseline and record the outcome -- settles "prove or disprove" with evidence.
- [x] `src/MediaStation.Core/Config/Settings.cs`, `ConfigValidation.cs`, `tools/samples/config/settings.json` -- add `WindowResultTimeout` (s, default 5, after `ForegroundSettleMs`), positive and at most 600 -- same convention as other Orchestrator timeouts.
- [x] `Orchestrator.cs`, `Orchestrator.Foreground.cs`, `Orchestrator.Timers.cs` -- add owner `"windowResult"`, `_windowResultTimerId`, the deadline and `OnWindowResultTimeout`, per Design Notes -- the bounded recovery.
- [x] `src/MediaStation.App/Orchestrator/OrchestratorLoop.cs` -- Warn `windowResultLost` with `cmdIds` when the list is non-empty -- leaves a trace of an AD-4 violation.
- [x] `tests/MediaStation.Core.Tests/OrchestratorForegroundTests.cs` -- cover every matrix row; fix `TimerOf` -- regression evidence.
- [x] `tests/MediaStation.Core.Tests/OrchestratorLoopTests.cs` -- through the real loop with `TimerScheduler` and `FakeTimeProvider`: dispatcher drops `ShowShell`'s result, advancing the clock past the deadline gives `windowResultLost` and a dispatched `ReportForeground` -- outermost automated surface.
- [x] `tests/MediaStation.Core.Tests/ConfigValidationTests.cs` -- default, explicit value, positive, 600 bound, schema order.
- [x] `architecture-mediastation.md` -- AD-15 rule for a lost result and the setting at l.317 -- records the decision.

**Acceptance Criteria:**
- Given a dropped window-command result and a mismatching window on top, when `windowResultTimeout` passes, then the log has one `windowResultLost` and a `ReportForeground` is dispatched, and its fresh observation switches the app.
- Given normal window results, when Home or a launch runs, then the dispatched commands match the baseline and no `windowResultLost` appears.
- Given `settings.json` without the field or with `windowResultTimeout` 0 or 601, when loaded, then the default of 5 applies or the existing config error is reported.
- Given the built app on the current machine, when the strongest real-process smoke runs, then a `foregroundChanged` entry appears, there is no `windowResultLost`, and no unexpected warn or error.

## Implementation Notes

Ran on UM760 (`powershell` identification: host `MEDIASTATION`, board `F7BSI`, model `EliteMini Series`).

**Baseline proof.** `Lost_window_result_blocks_every_settle_until_the_next_transition` passed against unmodified production code at `b4803b6`: three settles of a mismatching observation had no effect and only the next Home unblocked evaluation. The block is real. The test stays green after the fix, because it never reaches the deadline.

**Automated evidence.**
- `dotnet test tests/MediaStation.Core.Tests --filter "...OrchestratorForegroundTests|...OrchestratorLoopTests|...ConfigValidationTests"`: all pass.
- `dotnet build MediaStation.slnx -c Release`: exit 0, 0 warnings. `dotnet test MediaStation.slnx -c Release`: exit 0, 1658 total, 1657 passed, 1 skipped (`Registration_echo_is_not_reported_as_a_wake`, the existing DW-1 finding).
- Every matrix row has a test in `OrchestratorForegroundTests`. `Lost_show_shell_result_logs_window_result_lost_and_the_recovery_report_switches` runs the first acceptance criterion through the real loop, `TimerScheduler` and `FakeTimeProvider`, and also checks the JSONL form `"cmdIds":[n]`.

**Real-process smoke.** Built `MediaStation.exe` (Release, not published), real `%LOCALAPPDATA%\MediaStation\config`, whose `settings.json` has no `windowResultTimeout`, so the default of 5 applied. pid 21940, exit code 0, run from 2026-10-30T15:06:38Z. Result: 2 `foregroundChanged`, real Home gave `ShowShell` with `result: completed`, the run lasted over 7 s past Home, 0 `windowResultLost`, and the only warns were 3 `appIconMissing` from this machine's config. The screenshot showed the shell home screen.

The smoke needed a helper foreign window and `SetForegroundWindow`: an app started from a background process does not take the foreground, so the input gate passes Home through and no hook event arrives.

**Limits.**
- A real dropped result cannot be injected without test-only production code, so the drop itself is proven only through the real loop with `TimerScheduler`.
- Alt+Tab and the recovery switch were not run with physical input; they belong to the whole-epic acceptance on UM760.

**Behaviour to know.** The "no extra commands" guarantee holds when no observation arrives during the transition. When an echo arrives before the results, as it can with a real hook, the step also carries a `windowResult` `ScheduleTimer` and a later `CancelTimer`. No window command changes and nothing is marked lost (`Result_before_the_deadline_cancels_it_and_marks_nothing_lost`). A window command held longer than 5 s with an observation waiting counts as lost, including a slow but healthy one, and its late result is then ignored.

**Found, not changed (out of scope, ledger not edited).**
- `Win32WindowManagerPort` starts at `CompositionRoot.cs:269`, but `postsTo` is assigned at `:288`. The startup `ReportCurrent` can be dropped by `postsTo?.Post`, which fits the four smoke runs with no startup `foregroundChanged` and contradicts the AD-15 startup report. Cause inferred from the code, not confirmed by a test.
- In two smoke runs, raising the shell with `SetForegroundWindow` changed the foreground (confirmed by `GetForegroundWindow` in the last one) with no `foregroundChanged` for the shell. The hook code was not touched here and the cause was not investigated.

**After review patches.** Final Release build re-smoked: pid 22872, exit code 0, 2 `foregroundChanged` (helper form, then a foreign window after the form closed; none for the raised shell and none at startup), `ShowShell` completed, 0 `windowResultLost`, only 3 `appIconMissing` warns, shell home screen on the screenshot. Three mutants of `OnWindowResultTimeout` (`Any` to `All`, dropped `_reportNeeded = true`, dropped `!_reportNeeded` guard) each failed exactly one new test, then the file was restored.

## Plan Change Log

## Review Triage Log

### 2026-10-30 — Review pass
- verdicts: 27 findings — high 0, medium 2, low 21, false 4, maybe-false 0
- findings:
  - `[low]` `[reject]` Blind: `windowResultLost` logs only `cmdIds`, not the command type — true, but the event needs an AD-4 violation and the fix changes a public result type and the pending map.
  - `[low]` `[reject]` Blind: a loss with no later observation, or swept by a new transition, leaves no log — true, but nothing is blocked in that state; logging it adds branches for a case with no effect.
  - `[low]` `[reject]` Blind: a stalled WindowManager thread is not recovered, since commands and the hook share it — true; no observation can arrive during a stall and queued work runs on resume, so it is a limit, recorded under residual risks.
  - `[low]` `[reject]` Blind: the deadline counts from arming, not command age — true and by design; the wait stays bounded at about 5.3 s, and per-command timestamps add state.
  - `[low]` `[reject]` Blind: one failed plus one lost result cancels the deadline with the command still pending — true, but the failure policy has already dropped the observation, and the next observation arms the deadline.
  - `[low]` `[patch]` Blind: a lost report with `_reportNeeded` set schedules a settle timer the policy cancels in the same batch — fixed with a `!_reportNeeded` guard and `Lost_report_with_a_report_request_and_a_kept_observation_schedules_no_settle_timer`.
  - `[low]` `[patch]` Blind: `AssertNoEffect` ignores `WindowResultsLost` — added to the helper, manual copies removed.
  - `[low]` `[reject]` Blind: the second criterion overstates "match the baseline" when an echo precedes the results — true for timer commands only, no window command changes; the fix is plan wording, and it is disclosed in Implementation Notes and residual risks.
  - `[medium]` `[defer]` Blind: the startup report race and the missed shell event are untracked — both pre-existing and outside this change; added to `deferred`.
  - `[low]` `[reject]` Blind: the real Alt+Tab recovery check has no owner, because the ticket has no epic — a drop cannot be injected in the real app; the check is named in the handoff under residual risks.
  - `[low]` `[reject]` Blind: `ticket: ''` does not link the backlog ticket — the fix edits this plan, and the bundle was not dispatched as a ticket from the tree.
  - `[low]` `[patch]` Blind: surrounding docs not fully updated — added the elapsed `windowResultTimeout` to the AD-15 list; `EXPERIENCE.md`, the `Settings.cs` range text and the existing `CdpReconnectTimeout` omission were rejected as cosmetic.
  - `[low]` `[reject]` Blind: no negative, non-integer or schema-type config tests for the new setting — it uses the same `int` property and validators the other timeouts already test.
  - `[low]` `[patch]` Blind: the nothing-waiting test ends on a bare `DeadlineOf` — now asserts no `ReportForeground` and the deadline's `Due`.
  - `[low]` `[patch]` Edge: same defect as the settle schedule and cancel in one batch — same guard and test.
  - `[low]` `[reject]` Edge: same claim as the "match the baseline" wording — same reason.
  - `[medium]` `[patch]` Verification: no test reaches the hard branch with `_reportNeeded` false — added `Deadline_with_a_lost_report_and_lost_commands_of_a_late_window_sends_a_fresh_report`; two mutants confirm it fails on the regressions.
  - `[low]` `[patch]` Verification: no test shows a non-default timeout is used — added `Deadline_uses_the_configured_window_result_timeout`.
  - `[low]` `[reject]` Intent: the fault is modelled at the Orchestrator boundary and a stub dispatcher, not a real executor — the ledger's open question is the Orchestrator's reaction; a real seam would need test-only production code, which the plan forbids.
  - `[false]` `[reject]` Intent: asserts behaviour, not `IsHardBlocked` directly — the predicate is private, and in the pin test no report is pending, so it is the only branch that can produce the observed result.
  - `[low]` `[reject]` Intent: the pin test's name says "until the next transition" though the deadline now bounds it — its doc comment says "bez upływu terminu", and the test still shows that behaviour with a frozen clock.
  - `[false]` `[reject]` Intent: recovery tests do not assert mode and audio commands — they end at `foregroundSwitch` from `Decide`, whose commands `Background_app_on_top_is_activated_without_activate_window` and the Alt+Tab test pin.
  - `[false]` `[reject]` Intent: a loss with no observation has no bound on the pending set — the intent bounds blocked evaluation, and nothing is blocked until an observation arrives, which arms the deadline; `Starting` has its own timeout.
  - `[low]` `[reject]` Intent: a healthy command slower than 5 s is treated as lost, and its late result ignored — true and intended by the plan; the executor's `windowCommand` entry still records it, the setting can be raised, and it is listed under residual risks.
  - `[false]` `[reject]` Intent: the setting, result field, log event and AD-15 text go beyond the intent — each is what a logged, configurable timeout needs, and AD-15 keeps Orchestrator timeouts in settings.
  - `[low]` `[reject]` Intent: same claim as the "match the baseline" wording — same reason.
  - `[low]` `[reject]` Intent: the backlog ticket is unchanged and unlinked — the invocation assigns ledger resolution to the orchestrator, and the decision is recorded in AD-15; the deferred part is covered by the `deferred` row above.

## Design Notes

At the end of `ApplyForegroundPolicy` (`restart` = this step's `result.Commands` holds a `WindowCommand`):

```csharp
var waiting = _pendingWindow.Count > 0 && (_observation is not null || _reportNeeded);
if (_windowResultTimerId != 0 && (restart || !waiting)) { /* CancelTimer, zero the id */ }
if (_windowResultTimerId == 0 && waiting) { /* ScheduleTimer, owner "windowResult" */ }
```

`OnWindowResultTimeout`: zero the id; capture cmdIds sorted ascending; compute `hard` (any id not in `_reportCmds`) before clearing; clear both sets. If `hard`, set `_reportNeeded = true` and let the policy send the report, which also cancels the settle timer and drops the observation. Otherwise restart the settle timer when an observation is kept. Arming only while waiting is what keeps the common path command-free and makes retries bounded. A single timer per pending set suffices because any new command restarts it.

The timeout is a setting rather than a const because AD-15 and l.317 keep Orchestrator timeouts in `settings.json`.

## Verification

Ran on UM760 (host `MEDIASTATION`, board `F7BSI`, model `EliteMini Series`).

**Commands:**
- `dotnet test tests/MediaStation.Core.Tests --filter "OrchestratorForegroundTests|OrchestratorLoopTests|ConfigValidationTests"` -- expected: all pass.
- `dotnet build MediaStation.slnx -c Release` and `dotnet test MediaStation.slnx -c Release` -- expected: exit 0.

**Manual checks (if no CLI):**
- Real-process smoke: launch the built `MediaStation.exe` on this machine and assert on its JSONL log (`foregroundChanged` present, no `windowResultLost`, no unexpected warn or error), with a screenshot. A real dropped result cannot be injected without test-only production code, so the drop itself is proven through the real loop with `TimerScheduler`; record this limit.

**Evidence (session scratchpad, temporary):**
- Screenshot: `C:\Users\Użytkownik\AppData\Local\Temp\claude\C--Users-U-ytkownik-mediastation--bmad-loop-runs-20261030-155044-02b1-worktrees-dw-dw-orchestrator-window-result-loss\88402991-61d6-407a-ba9f-6da0f1a269fb\scratchpad\smoke-dw49.png`
- Log: `C:\Users\Użytkownik\AppData\Local\Temp\claude\C--Users-U-ytkownik-mediastation--bmad-loop-runs-20261030-155044-02b1-worktrees-dw-dw-orchestrator-window-result-loss\88402991-61d6-407a-ba9f-6da0f1a269fb\scratchpad\smoke-dw49.jsonl`

## Auto Run Result

Status: built

**Summary.** The block is real: a lost window-command result stopped every foreground evaluation until the next `transitionId`, and a lost `ReportForeground` did the same through `OnSettled`. Evaluation now waits at most `windowResultTimeout` (default 5 s). At the deadline the pending commands are dropped and logged as `windowResultLost`. A lost non-report command triggers one fresh `ReportForeground`; a lost report re-settles the kept observation. Executors and AD-4 are unchanged.

**Files changed.**
- `src/MediaStation.Core/Orchestrator/Orchestrator.Foreground.cs` -- deadline and `OnWindowResultTimeout`.
- `src/MediaStation.Core/Orchestrator/Orchestrator.cs` -- `WindowResultsLost`, timer state and timeout.
- `src/MediaStation.Core/Orchestrator/Orchestrator.Timers.cs` -- dispatches the deadline.
- `src/MediaStation.App/Orchestrator/OrchestratorLoop.cs` -- Warn `windowResultLost`.
- `src/MediaStation.Core/Config/Settings.cs`, `ConfigValidation.cs`, `tools/samples/config/settings.json` -- the setting, 1–600 s.
- `tests/MediaStation.Core.Tests/OrchestratorForegroundTests.cs`, `OrchestratorLoopTests.cs`, `ConfigValidationTests.cs` -- pin test, matrix coverage, real-loop test, config cases.
- `_bmad-output/initiative-mediastation-mvp/architecture-mediastation/architecture-mediastation.md` -- AD-15 rule and settings list.

**Review findings.** 27 findings. Patched 6 entries (1 medium, 5 low; 8 findings including the duplicate). Deferred 1 finding as 2 items in `deferred`. Rejected 18, each with its reason in the Review Triage Log.

**Follow-up review recommended: false.** This first pass patched 0 high and 1 medium entries.

**Verification (ran on UM760: host `MEDIASTATION`, board `F7BSI`, model `EliteMini Series`).**
- `dotnet build MediaStation.slnx -c Release`: exit 0, 0 warnings.
- `dotnet test MediaStation.slnx -c Release --no-build`: exit 0, 1661 total, 1660 passed, 0 failed, 1 skipped (`Registration_echo_is_not_reported_as_a_wake`, the existing DW-1 finding). This filter's three test classes are part of that run.
- Pin test passed on baseline production code, per the implementer's run at `b4803b6`; not re-run after that.
- Three mutants each failed one new test.
- Real-process smoke on the final Release build: exit 0, values under Implementation Notes.
- Matrix audit: every row has a test in `OrchestratorForegroundTests`, and all passed in the full run.

**Residual risks.**
- A real dropped result was never injected into the running app; the drop is proven through the real loop with `TimerScheduler` and a dropping dispatcher.
- Physical Alt+Tab was not run. Check on UM760: after Home, Alt+Tab to a running app, and confirm the remote and audio follow it within about 300 ms and the log has no `windowResultLost`. This ticket has no parent epic, so this check needs its own slot in your acceptance.
- During a stall of the window-manager thread the recovery report waits in the same queue and no hook events arrive, so recovery only completes when the thread resumes.
- A healthy window command slower than 5 s with an evaluation waiting is logged as lost and its late result is ignored.
- A hook echo arriving before the results adds one `windowResult` schedule and cancel to a normal Home; window commands do not change.
- The two `deferred` items limit recovery, because it depends on `ForegroundChanged` arriving.
