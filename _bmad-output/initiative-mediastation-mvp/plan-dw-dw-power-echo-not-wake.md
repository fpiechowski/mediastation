---
title: 'DW-50: display-state registration echo must not be reported as a wake'
type: 'bugfix'
ticket: ''
created: '2026-10-30'
status: 'built'
baseline_revision: '392ec40ea96d9b559f48d7bfe2dcb5489badc1b6'
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
      Display-state signals seen 2.7 to 9.8 s after the app started were reported as winning systemResumed, and their cause is unknown, so they may be false wakes that this fix does not stop.
    evidence: |-
      Across 511 logged starts on UM760, 4 had such a signal: 2026-10-26 13:29:28 (+9.76 s), 19:36:21 (+2.70 s), 20:18:36 (+7.26 s) and 2026-10-27 09:45:06 (+3.30 s). Each produced a systemResumed and a new snapshot. The log does not record the display state or any preceding off, so a real display-on cannot be told apart from a repeated on. When the echo lands inside the registration window, as in the 3 measured starts, such a signal still reports. Settled by logging Data[0] for each display-state notification over a few starts, including starts where the screen flickers, and checking whether an off precedes each on.
    location: >-
      src/MediaStation.App/Platform/System/PowerSignalRouter.cs
    severity: medium (unverified)
---

<intent-contract>

## Intent

**Problem:** `PowerSignalRouter` closes its echo window in `CompleteRegistration`, so a `GUID_CONSOLE_DISPLAY_STATE` registration echo delivered after `Start` returns is reported as a winning `systemResumed`. A false wake re-focuses the shell, minimizes a foreground app, renews sessions, and blocks a real wake for 5 s through `WakeSignalGate`.

**Approach:** The router expects exactly one echo per successful registration. The window closes when the echo is seen, or after a bounded wait if it never comes, so a real wake is never swallowed indefinitely.

## Boundaries & Constraints

**Always:** Time reaches the router only through `TimeProvider`, on the monotonic clock. Only a well-formed display-state notification (matching GUID, DWORD `DataLength`, full buffer) counts as the echo, in any state (on, off, dimmed). The three-source mapping stays: `PBT_APMRESUMEAUTOMATIC` always reports, `PowerModeChanged` only logs. The log contract stays: no new `msg`, no change to `powerSignal`, `systemResumed` or `powerSignalsRegistered`.

**Never:** No swallow-the-first-notification without a time bound: on UM760 real wakes arrive only as `GUID_CONSOLE_DISPLAY_STATE`, so a missing echo would eat the first wake. No test-only production code. No change to `WakeSignalGate`, `PowerSignalReporter` or the Orchestrator. No edit of the deferred-work ledger.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Late echo | Registered, then `on` before the wait ends | Nothing logged or posted | Consumed |
| Late echo, off or dimmed | Registered, then `off`/`dimmed`, then `on` | First consumed; `on` logs and posts one resume | No error expected |
| Echo inside the window (real app) | `on` between Begin and Complete, then `on` after Complete | First dropped; second reports with no wait | No error expected |
| Repeat inside the window | Two `on` between Begin and Complete | Both dropped, as today | No error expected |
| Wake after the echo | Echo consumed, then `on` at once | Logs and posts one resume | No error expected |
| No echo, inside the wait | Registered, no echo, `on` at just under the wait | Treated as the echo, dropped | Accepted loss, see Design Notes |
| No echo, wait over | Registered, no echo, `on` at exactly the wait | Logs and posts one resume | No error expected |
| Registration failed | Complete with `displayState: false`, then `on` | Logs and posts one resume, no echo expected | No error expected |
| Malformed or foreign notification | Other GUID, wrong `DataLength` or short buffer during the wait, then the echo | Ignored and does not consume; the echo is still dropped | No error expected |
| Other sources during the wait | `PBT_APMRESUMEAUTOMATIC` or `PowerModeChanged` before the echo | Report as today; do not consume; the echo is still dropped | No error expected |

</intent-contract>

## Code Map

Measured on UM760 at baseline with temporary logging in the shim (reverted): in 3 real-app starts the echo (`data0=1`) arrived **inside** the window, about 3 ms before `CompleteRegistration`. `Registration_echo_is_delivered_through_the_hook_after_start` **fails when run alone** and passes in a full run. The inference: the first `SystemEvents` subscription waits on an STA thread, which pumps messages, so the echo lands in the window; once `SystemEvents` is already initialized nothing pumps and the echo arrives after `Start`. In 511 logged starts no pattern of startup echo wins appears (4 display-state signals within 15 s, at +2.7 to +9.8 s, unexplained). So the real app is protected today by that accidental pump, and this fix removes the dependency on it.

- `src/MediaStation.App/Platform/System/PowerSignalRouter.cs` -- the state machine; add the `TimeProvider` ctor parameter and the wait constant.
- `src/MediaStation.App/Platform/System/DisplayStateNotification.cs` -- extract `IsDisplayState(payload, setting)` from `IsWake`; keep `IsWake`'s signature and results.
- `src/MediaStation.App/Platform/System/PowerNotifications.cs` -- `Start(hwnd, reporter, time)`, passes `time` to the router; update the doc comments only as needed.
- `src/MediaStation.App/CompositionRoot.cs:317` -- pass the existing `time`.
- `tests/MediaStation.App.Tests/PowerNotificationsHarnessTests.cs` -- real HWND harness; `Fixture.Time` is the clock to pass; `Settle()` comment l.62-66 is out of date after the fix.
- `tests/MediaStation.Core.Tests/PowerSignalRouterTests.cs` -- `Create()` l.219 already has a `FakeTimeProvider`; the "after registration" tests call `Complete(displayState: true)` and then send a payload, which the echo logic would now consume.
- `tests/MediaStation.Core.Tests/DisplayStateNotificationTests.cs` -- literal-byte fixtures, the pattern for `IsDisplayState` tests.
- Do not change: `WakeSignalGate`, `PowerSignalReporter`, `tools/inject-power.ps1` (it injects `PBT_APMRESUMEAUTOMATIC`, unaffected).

Code comments in these files are in Polish; keep that.

## Tasks & Acceptance

**Execution:**
- [x] `src/MediaStation.App/Platform/System/DisplayStateNotification.cs` -- add `IsDisplayState`, have `IsWake` use it -- one definition of a well-formed notification.
- [x] `src/MediaStation.App/Platform/System/PowerSignalRouter.cs` -- implement Design Notes -- the fix.
- [x] `src/MediaStation.App/Platform/System/PowerNotifications.cs`, `src/MediaStation.App/CompositionRoot.cs` -- pass `TimeProvider` through -- the router needs the clock.
- [x] `tests/MediaStation.Core.Tests/PowerSignalRouterTests.cs` -- pass `time` in `Create()`; add helper `Registered(router)` (Begin, Complete with all true, one echo) and use it in the existing post-registration payload tests so they still exercise the post-window path; update `Display_state_after_registration_logs_and_posts_one_resume` the same way; add one test per matrix row, including the exact wait boundary -- keeps real wakes covered.
- [x] `tests/MediaStation.Core.Tests/DisplayStateNotificationTests.cs` -- `IsDisplayState` true for on, off and dimmed, false for other GUID, wrong length and short buffer.
- [x] `tests/MediaStation.App.Tests/PowerNotificationsHarnessTests.cs` -- pass `f.Time` to every `Start`; remove `Skip` from `Registration_echo_is_not_reported_as_a_wake`; delete `Registration_echo_is_delivered_through_the_hook_after_start`; add a private helper that subscribes and unsubscribes a no-op `SystemEvents.PowerModeChanged` handler before `Start`, so the late-echo path is deterministic in any test order; in the un-skipped test, also count `PBT_POWERSETTINGCHANGE` with an observer hook and assert at least 1, so the test cannot pass when no echo arrives; add `Real_display_wake_right_after_the_echo_wins` (same setup, pump, `SendSetting` on, no clock advance); update the `Settle` comment -- the outermost automated surface.

**Acceptance Criteria:**
- Given a real window with `SystemEvents` already initialized, when `PowerNotifications.Start` runs and the queue is pumped, then the hook sees the echo, the log has no `powerSignal` and nothing is posted, in any test order.
- Given that same window after the echo, when a display-on `POWERBROADCAST_SETTING` is sent, then one `systemResumed` with source `GUID_CONSOLE_DISPLAY_STATE` is logged and one `SystemResumed` is posted.
- Given the built app on the current machine, when it is started and closed, then its log has `powerSignalsRegistered` with all three true, no `GUID_CONSOLE_DISPLAY_STATE` `powerSignal` in that run, and exit code 0.
- Given the running app, when `tools/inject-power.ps1` runs, then it reports 3 `powerSignal` and 2 `systemResumed`, exit 0.

## Implementation Notes

Route full: about 135 changed lines, most in tests. Ran on UM760 (host `MEDIASTATION`, board `F7BSI`, model `EliteMini Series`). The final diff is larger: 378 insertions, 57 deletions, of which about 330 lines are tests.

Beyond the task list, added in the router tests: a literal pin of `EchoWait` (the boundary tests use the constant, so a changed value would otherwise go unnoticed) and a test that a malformed or foreign notification inside the registration window is not counted as the echo (the "Always" rule; the matrix only names the wait). `Power_mode_resume_after_registration_only_logs_a_candidate` stays on `Complete` without an echo: it is not a payload test and still holds.

**Evidence, all on UM760, final Release build:**
- `dotnet build MediaStation.slnx -c Release`: exit 0, 0 warnings.
- `dotnet test MediaStation.slnx -c Release`: exit 0, 1681 total, 1681 passed, 0 failed, 0 skipped.
- `Registration_echo_is_not_reported_as_a_wake` alone: exit 0, 1 passed. `Real_display_wake_right_after_the_echo_wins` alone: exit 0, 1 passed. `PowerNotificationsHarnessTests` as a class, 5 runs in a row: 15 passed each.
- Mutation check (temporary, reverted): with `_echoWaitStart = null` in `CompleteRegistration`, `Registration_echo_is_not_reported_as_a_wake` fails with `["GUID_CONSOLE_DISPLAY_STATE"]` in `powerSignal`, both in a run of just the two new-path tests and in the class (`Real_display_wake_right_after_the_echo_wins` passes under this mutant, as it guards the opposite failure). So in this test the echo really does arrive after `Start` and the wait is what drops it.
- Real process, start and close (pid 3924, about 9 s alive): exit code 0, `stop.exitCode` 0, `powerSignalsRegistered` all three true, no `powerSignal` and no `systemResumed` in the run. Warnings: only three `appIconMissing` from the local config, no error.
- Real process with `tools/inject-power.ps1` (pid 16376): script exit 0, app exit code 0, 3 `powerSignal` and 2 `systemResumed`, all `PBT_APMRESUMEAUTOMATIC`. Same three `appIconMissing`, no error.

**Limits:**
- A real display wake cannot be produced unattended, so a real wake after this fix belongs to the UM760 whole-epic acceptance. `Real_display_wake_right_after_the_echo_wins` injects it with `SendMessage` through the real hook.
- The smoke proves no false wake on a real start, but that was also true before the fix (the accidental pump, see Code Map). The late-echo path is proven only by the harness and the mutation check. The accepted loss (no echo at all and a real wake within 5 s of start) is pinned by `Without_echo_on_just_under_the_wait_is_treated_as_the_echo`.

**After review patches (final tree, run by the orchestrating session on UM760).** These replace the 1681 count and the pids above, which predate the patches.
- `dotnet build MediaStation.slnx -c Release`: exit 0, 0 warnings.
- `dotnet test MediaStation.slnx -c Release --no-build`: exit 0, 1684 total, 1684 passed, 0 failed, 0 skipped.
- `Registration_echo_is_not_reported_as_a_wake` alone: exit 0, 1 passed.
- Mutation re-run on the patched tests, then restored: with `_echoWaitStart = null` in `CompleteRegistration`, `Registration_echo_is_not_reported_as_a_wake` fails on `Assert.Empty`; `Real_display_wake_right_after_the_echo_wins` passes. After restoring, the harness class passes 15 of 15.
- Real process, start and close (pid 24100): exit 0, `powerSignalsRegistered` all three true, no `powerSignal`, only the three `appIconMissing` warns.
- Real process with `tools/inject-power.ps1` (pid 19820): script exit 0, app exit 0, 3 `powerSignal` and 2 `systemResumed`, all `PBT_APMRESUMEAUTOMATIC`, only the three `appIconMissing` warns.
- No visual acceptance criterion applies, so no screenshot was taken.

## Plan Change Log

## Review Triage Log

### 2026-10-30 — Review pass
- verdicts: 21 findings — high 0, medium 2, low 15, false 2, maybe-false 2
- findings:
  - `[medium]` `[patch]` Blind: no test sends an off or dimmed echo inside the window — confirmed in the tests; added `Off_or_dimmed_echo_inside_the_window_means_the_next_on_wins_with_no_wait`.
  - `[low]` `[patch]` Blind: nothing pins that the wait counts from `CompleteRegistration` — added `Echo_wait_counts_from_complete_registration`.
  - `[low]` `[patch]` Blind: `Real_display_wake_right_after_the_echo_wins` injects without confirming the echo arrived, on a fixed pump — both tests now use `StartAndAwaitLateEcho`, which waits for the echo with a 5 s deadline.
  - `[low]` `[patch]` Blind: `IsRegistrationEcho` changes state inside `IsWake`'s arguments and feeds a parameter misnamed `duringStartup` — renamed to `isRegistrationEcho` and moved the call to its own line.
  - `[low]` `[reject]` Blind: a dropped notification leaves no log trace — true, but it only matters when diagnosing a lost wake in a no-echo start, which was never observed; the fix adds a log entry and branches.
  - `[low]` `[reject]` Blind: an echo handled after `EchoWait` brings the false wake back, and 5 s has no measured basis — true, and no worse than the baseline; it needs a UI thread blocked 5 s with no echo in the window, and it is listed under residual risks.
  - `[maybe-false]` `[defer]` Blind: 4 display-state signals at +2.7 to +9.8 s are unexplained and untracked — log context shows each gave a `systemResumed`, but not whether the display really went off first; settled by logging `Data[0]`, recorded in `deferred`.
  - `[low]` `[reject]` Blind: the UM760 check is not in an acceptance file, and the old file says the echo was not observed — that file records a past session, and the check is named in the handoff under Auto Run Result; this bundle has no epic file to extend.
  - `[low]` `[patch]` Blind: `PowerNotifications.cs:14` still says the router holds "okno echa" — reworded to "echo rejestracji i mapowanie źródeł".
  - `[low]` `[patch]` Blind: helper `Begin` also completes registration, and the in-window malformed test omits a wrong `DataLength` — renamed to `RegisteredAwaitingEcho` and added the case.
  - `[low]` `[reject]` Edge: a stalled UI thread can push the echo past `EchoWait` — same outcome as the matching Blind row; the suggested dispatcher start adds a new mechanism to the shim for a state not observed on the measured starts.
  - `[low]` `[patch]` Edge: the fixed 500 ms pump can fail on a loaded machine — same fix as the helper row, `StartAndAwaitLateEcho`.
  - `[medium]` `[patch]` Verification: in-window off or dimmed echo is not pinned as consuming the echo — same defect and same new theory as the first row.
  - `[false]` `[reject]` Intent A: the late path was not seen in the measured product starts — this is no defect: the intent explicitly names the echo delivered after `Start`, and the plan records that the real app was protected by an accidental pump.
  - `[low]` `[reject]` Intent B: the late path runs on a message-only window, not the shell's top-level HWND — true, but the router has no window dependency and the hook code is the same; a real wake is on the UM760 check.
  - `[low]` `[patch]` Intent C: the un-skipped test does not assert the echo is late — the helper now asserts a count of 0 right after `Start` returns.
  - `[low]` `[reject]` Intent D: the bound is exercised only on a fake clock — true and intended, since boundary tests on a wall clock would be slow and flaky; `PowerSignalRouterTests` covers both sides of the boundary.
  - `[low]` `[reject]` Intent E: a real wake within 5 s is lost if no echo ever comes, a narrower guarantee than before — true, but no start without an echo has been measured, startup has just published its own focus request, and avoiding it needs a second signal; pinned by a test and listed under residual risks.
  - `[low]` `[reject]` Intent F: no physical display wake was tested — it cannot be produced unattended; it is on the UM760 check.
  - `[maybe-false]` `[defer]` Intent G: the one-echo assumption leaves later notifications reported, including the 4 unexplained signals — same open question as the deferred row; the harness would fail if the OS sent a second echo within its 500 ms pump.
  - `[false]` `[reject]` Intent H: the diff reaches beyond `PowerSignalRouter.cs` — the other production edits only pass the clock and share the payload check, and `IsWake` results are unchanged, so no bad outcome follows.

## Design Notes

```csharp
public static readonly TimeSpan EchoWait = TimeSpan.FromSeconds(5);

BeginRegistration:    _registering = true; _echoSeen = false; _echoWaitStart = null;
CompleteRegistration: _registering = false;
                      _echoWaitStart = displayState && !_echoSeen ? _time.GetTimestamp() : null;
                      _reporter.ReportRegistration(...);

private bool IsRegistrationEcho(payload, setting)
{
    var displayState = DisplayStateNotification.IsDisplayState(payload, setting);
    if (_registering) { _echoSeen |= displayState; return true; }
    if (!displayState || _echoWaitStart is not { } start) return false;
    _echoWaitStart = null;
    return _time.GetElapsedTime(start) < EchoWait;
}

OnDisplayStateChanged: if (IsWake(payload, setting, IsRegistrationEcho(payload, setting))) Report(...)
```

All router state is touched only on the UI thread (the hook); `OnPowerModeResumed` reads none of it.

**Why 5 s.** It only matters when no echo has been seen. If the echo stalls past it (a blocked UI thread), the old false wake returns, which is harmless next to losing wakes. If no echo ever comes and a real display wake lands within 5 s of start, that wake is lost, but startup has just published its own focus request and fresh state. On UM760 the echo was seen in the window in all 3 measured starts, so no wait applies there.

## Verification

**Commands:**
- `dotnet build MediaStation.slnx -c Release` -- expected: exit 0, 0 warnings.
- `dotnet test MediaStation.slnx -c Release` -- expected: exit 0, 0 failed, 0 skipped.
- `dotnet test tests/MediaStation.App.Tests/MediaStation.App.Tests.csproj -c Release --filter "FullyQualifiedName~Registration_echo_is_not_reported_as_a_wake"` -- expected: passes alone, which proves the order independence.

**Manual checks (if no CLI):**
- Real-process smoke: start the built `MediaStation.exe`, close it, and assert the third acceptance criterion on its JSONL log; then start it again and run `tools/inject-power.ps1` for the fourth. Record pids, exit codes and any warn or error. A real display wake cannot be produced unattended, so a real wake after this fix belongs to the UM760 whole-epic acceptance; record this limit.

## Auto Run Result

Status: built

**Summary.** A `GUID_CONSOLE_DISPLAY_STATE` registration echo delivered after `Start` returns is no longer reported as a wake. The router now treats the first well-formed display-state notification after a successful registration as the echo, unless one was already seen in the window or 5 s have passed. Measurement changed the picture of the bug: in 3 instrumented real-app starts the echo arrived inside the window, so the real app was protected only by an inferred side effect of first `SystemEvents` initialization. This fix removes that dependency.

**Files changed.**
- `src/MediaStation.App/Platform/System/PowerSignalRouter.cs` -- echo tracking and `EchoWait`.
- `src/MediaStation.App/Platform/System/DisplayStateNotification.cs` -- `IsDisplayState`, parameter renamed to `isRegistrationEcho`.
- `src/MediaStation.App/Platform/System/PowerNotifications.cs`, `src/MediaStation.App/CompositionRoot.cs` -- pass `TimeProvider`; doc wording.
- `tests/MediaStation.App.Tests/PowerNotificationsHarnessTests.cs` -- echo test un-skipped, characterization test replaced by `Real_display_wake_right_after_the_echo_wins`, `StartAndAwaitLateEcho` added.
- `tests/MediaStation.Core.Tests/PowerSignalRouterTests.cs`, `DisplayStateNotificationTests.cs` -- matrix coverage and `IsDisplayState` cases.

**Review findings.** 21 findings. Patches applied: 6 entries covering 9 findings (1 medium entry, 5 low entries). Deferred: 1 entry covering 2 findings. Rejected: 10, each with its reason in the Review Triage Log: no log trace for a dropped notification; echo past `EchoWait` and the unmeasured 5 s; UM760 check not in an acceptance file; UI-thread stall; late path not seen in product starts (false); message-only window; fake-clock bound; wake lost when no echo comes; no physical wake; diff beyond the named file (false).

**Follow-up review recommended: false.** This first pass patched 0 high and 1 medium entries.

**Verification (ran on UM760: host `MEDIASTATION`, board `F7BSI`, model `EliteMini Series`).**
- `dotnet build MediaStation.slnx -c Release`: exit 0, 0 warnings.
- `dotnet test MediaStation.slnx -c Release --no-build`: exit 0, 1684 total, 1684 passed, 0 failed, 0 skipped.
- `Registration_echo_is_not_reported_as_a_wake` alone: passed. With the fix disabled it fails, and the fix was then restored.
- Real-process smoke: pid 24100 exit 0, `powerSignalsRegistered` all true, no `powerSignal`. `tools/inject-power.ps1` on pid 19820: exit 0, 3 `powerSignal`, 2 `systemResumed`. Only the three `appIconMissing` warns from this machine's config.
- Matrix audit: every row has a passing test in `PowerSignalRouterTests`.

**Residual risks.**
- The real-app smoke cannot show the fix working, because the real app also had no false wake before it. Only the harness and the mutation check prove the late-echo path.
- No physical display wake was run after this fix. Check on UM760: start the app, wait at least 10 s, sleep and wake the machine, and confirm exactly one `systemResumed` from `GUID_CONSOLE_DISPLAY_STATE`. Repeat with the TV off at app start, then on within a few seconds; that wake must also report. This bundle has no parent epic, so the check needs its own slot in your acceptance.
- If no echo ever arrives, a real display wake within 5 s of start is lost. If an echo is handled more than 5 s after registration, the false wake returns. Neither was observed.
- The 4 unexplained display-state signals remain open; see `deferred`.