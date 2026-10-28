---
title: 'DW-42: Home on Foreground app without Hwnd shows shell'
type: 'bugfix'
ticket: ''
created: '2026-10-28'
status: 'built'
baseline_revision: '8eb72c1a09cb74684315d6d5498b20f490404966'
route: 'oneshot'
route_source: 'auto'
risk: 'low'
review: 'quick'
review_source: 'pinned'
lenses_ran: [quick]
review_loop_iteration: 0
followup_review_recommended: false
context: []
warnings: []
deferred: []
---

<intent-contract>

## Intent

**Problem:** Home on a `Foreground` app with no known Hwnd returns `OrchestratorResult.None` (Orchestrator.Home.cs), so no `ShowShell` is emitted and input stays routed to the app (DW-42).

**Approach:** In that case move the app to `Background`, emit `MuteProcessTree`, `ShowShell` and shell routing (no `MinimizeWindow`, there is no window to minimize), and cover it with Orchestrator tests.

</intent-contract>

## Implementation Notes

Oneshot: a small change in one branch plus tests. Machine: UM760 (MEDIASTATION / F7BSI / EliteMini Series). The Hwnd-less path reuses the normal Foreground path with the minimize step skipped, so state and routing stay consistent.

## Verification

**Commands:**
- `dotnet test tests/MediaStation.Core.Tests --filter OrchestratorHomeTests` -- expected: all pass

## Review Triage Log

### 2026-10-28 — Review pass
- verdicts: 5 findings — high 0, medium 0, low 3, false 1, maybe-false 0 (one note-only)
- findings:
  - `[low]` `patch` OnHome XML doc stale — doc line added about the hwnd-less case; fixed.
  - `[low]` `reject` ClearHwnd reflection brittle — Stop.cs/Failures.cs null the Hwnd only together with a state change away from Foreground, so the state is unreachable publicly; a sturdier hook would add production surface for a test.
  - `[low]` `reject` SetManagedWindows assertion pins a test-setup artifact — it is real Handle behavior when the managed set changes, and is commented in the test.
  - `[false]` `reject` plan lacks evidence — evidence is recorded below under Auto Run Result.
  - `[low]` `reject` Background without hwnd cannot be resumed — same behavior as the existing Decide path; Hwnd-less Foreground is unreachable in production.

## Auto Run Result

Status: built

- Summary: Home on a `Foreground` app with no Hwnd now sets it `Background` and emits MuteProcessTree, ShowShell and shell routing (MinimizeWindow skipped), instead of returning no commands (DW-42).
- Files changed: `src/MediaStation.Core/Orchestrator/Orchestrator.Home.cs` (Foreground branch + doc); `tests/MediaStation.Core.Tests/OrchestratorHomeTests.cs` (two new tests, reflection helper to create the hwnd-less state).
- Review: 1 patch applied (doc), 0 deferred, 4 rejected (see Triage Log). Follow-up review recommended: false.
- Verification: ran on UM760 (MEDIASTATION / F7BSI / EliteMini Series): `dotnet test tests/MediaStation.Core.Tests` → 1251 passed, 0 failed. UM760 hardware checks: not applicable per ticket; the state is unreachable through real input.
- Residual risks: none known; the hwnd-less state is reachable only via tests today.
