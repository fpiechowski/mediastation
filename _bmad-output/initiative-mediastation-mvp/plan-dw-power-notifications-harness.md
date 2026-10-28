---
title: 'DW-1: HWND harness for PowerNotifications'
type: 'chore'
ticket: 'bug-power-notifications-hwnd-harness'
created: '2026-10-28'
status: 'done'
baseline_revision: '8fb74607733d789194e35b958e158ddeae988c1f'
route: 'oneshot'
route_source: 'auto'
risk: 'low'
review: 'quick'
review_source: 'pinned'
lenses_ran:
  - quick
review_loop_iteration: 0
followup_review_recommended: false
context: []
warnings: []
deferred:
  - summary: >-
      PowerSignalRouter reports the GUID_CONSOLE_DISPLAY_STATE registration echo as a winning systemResumed at startup, because the echo arrives after the registration window closes.
    evidence: |-
      Observed by the new harness; Registration_echo_is_delivered_through_the_hook_after_start pins the behavior and Registration_echo_is_not_reported_as_a_wake (Skipped) holds the desired one. Pre-existing router/shim timing, not caused by the harness.
    location: >-
      src/MediaStation.App/Platform/System/PowerSignalRouter.cs
    severity: medium
---

<intent-contract>

## Intent

**Problem:** The PowerNotifications Win32/WPF plumbing (HWND hook, RegisterPowerSettingNotification, POWERBROADCAST_SETTING copy, SystemEvents subscription) had no automated test (DW-1).

**Approach:** New `tests/MediaStation.App.Tests` (net10.0-windows, WPF) links the shim sources and drives them through a real message-only `HwndSource`. `PowerSignalRouter` and the App project are untouched.

</intent-contract>

## Implementation Notes

Oneshot: a test-only addition. Machine: ran on UM760 (MEDIASTATION / F7BSI / EliteMini Series).

- Linked sources instead of a ProjectReference to App: App is a WinExe whose BuildWeb target builds Core.Tests and runs npm.
- Core.Tests csproj comment updated to point at the new harness; `MediaStation.slnx` lists the project.
- SystemEvents is driven through reflection (`s_systemEvents.OnPowerModeChanged(PBT_APMRESUMESUSPEND)`), since a real resume cannot be produced from a test. Coupled to .NET internals.
- **Finding (needs a decision, not fixed here):** the system delivers the registration echo of GUID_CONSOLE_DISPLAY_STATE from the message queue after `Start` returns, when `PowerSignalRouter`'s registration window is already closed. The echo is therefore reported as a winning `systemResumed` at startup. `Registration_echo_is_delivered_through_the_hook_after_start` pins the observed behavior; `Registration_echo_is_not_reported_as_a_wake` holds the desired behavior and is Skipped with the reason. The fix touches the router or shim timing, out of scope.
- Not covered: release of the power-setting registration handle on Dispose (not observable without OS query).

## Verification

- `dotnet build MediaStation.slnx` -- 0 warnings, 0 errors (UM760)
- `dotnet test --project tests/MediaStation.Core.Tests/...` -- 1244 passed
- `dotnet test --project tests/MediaStation.App.Tests/...` -- 14 passed, 1 skipped (the echo finding above)

## Review Triage Log

### 2026-10-28 — Review pass
- verdicts: 7 findings — high 0, medium 1, low 4, false 2, maybe-false 0
- findings:
  - `[medium]` `[defer]` Echo-pinning test keeps the suite green while the startup false wake exists — the defect is in the router/shim timing (pre-existing), already flagged in the plan and the skip reason; recorded in `deferred`.
  - `[low]` `[reject]` Fixed 300/500 ms settle pumps are timing-dependent — passed on UM760; polling would add complexity for a machine class not used here.
  - `[low]` `[reject]` Reflection into private `SystemEvents` members — coupling acknowledged in the plan; `Assert.NotNull` fails loudly on a runtime change.
  - `[low]` `[reject]` Dispose negative assertion lacks a positive control — the same reflection path is exercised positively by other tests; fix adds a branch for negligible gain.
  - `[low]` `[reject]` Possibly unused NativeMethods.txt entries — file mirrors App's list as the csproj comment says; extra generated stubs are harmless.
  - `[false]` `[reject]` Machine identification not recorded per AGENTS.md — the plan names UM760 (MEDIASTATION / F7BSI / EliteMini Series); re-running the command this pass returned exactly that.
  - `[false]` `[reject]` Review bookkeeping incomplete — it described the in-flight state of this pass; the fix would only edit the plan.

## Auto Run Result

- Summary: test-only harness driving the PowerNotifications shim through a real message-only HwndSource; follow-up quick review pass found no patchable defects.
- Files changed: `tests/MediaStation.App.Tests/*` (new harness project), `MediaStation.slnx` (lists it), Core.Tests csproj (comment).
- Review: patches applied 0; deferred 1 (startup echo reported as wake); rejected 6 with reasons in the triage log.
- Follow-up review recommended: false (no patches).
- Verification: identified machine as UM760 (MEDIASTATION / F7BSI / EliteMini Series); no code changed this pass, so earlier results stand (build clean, App.Tests 14 passed 1 skipped).
- Residual risks: timing-based settle pumps; reflection into SystemEvents internals; startup false wake awaiting a decision.
