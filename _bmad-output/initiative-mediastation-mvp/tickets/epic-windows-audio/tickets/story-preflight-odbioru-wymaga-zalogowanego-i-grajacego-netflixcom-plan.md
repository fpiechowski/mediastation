---
title: 'Preflight odbioru wymaga zalogowanego i grającego netflix.com'
type: 'feature'
ticket: 17
created: '2026-10-28'
status: done
baseline_revision: '8d5ed0e'
route: 'oneshot'
route_source: 'auto'
risk: 'high'
review: 'thorough'
review_source: 'pinned'
lenses_ran: ['blind-hunter', 'edge-case-hunter', 'verification-gap']
review_loop_iteration: 0
followup_review_recommended: false
context: []
warnings: []
deferred: []
---

<intent-contract>

## Intent

**Problem:** Audio acceptance S15-S27 can start after a process or audio session appears, even when the Netflix profile has no visible Netflix page or the operator has not confirmed audible playback.

**Approach:** Add a repeatable PowerShell preflight that requires the Edge process to use `profiles\\netflix`, matches the logged `appWindowAttached` window, verifies a visible window and Netflix URL/title evidence, and requires an explicit operator playback confirmation. Add a self-test matrix for ready and blocked states.

## Boundaries & Constraints

**Always:** Keep the gate specific to `netflix.com` and `profiles\\netflix`; report concrete reasons; treat `audioRuleApplied` alone as insufficient; record that the check ran on UM760.

**Never:** Substitute YouTube or `MEDIASTATION_HARNESS_URL`; change WindowManager, AudioRouter, Moonlight, or personal Edge profiles; mark S15-S27 complete from the preflight alone.

</intent-contract>

## Code Map

- `tools/check-netflix-preflight.ps1` -- process, window, JSONL, Netflix evidence, operator confirmation, and self-test implementation.
- `_bmad-output/initiative-mediastation-mvp/tickets/epic-windows-audio/epic-windows-audio-um760-acceptance.md` -- S15-S27 rows that remain gated until this preflight is ready.

## Tasks & Acceptance

**Execution:**
- [x] `tools/check-netflix-preflight.ps1` -- implement the Netflix readiness gate and explicit blocked reasons.
- [x] `tools/check-netflix-preflight.ps1 -SelfTest` -- cover valid state and incomplete state including audio-only evidence.
- [ ] Acceptance operator step -- sign in, start Netflix playback, and confirm visible and audible media on UM760 before S15-S27.

**Acceptance Criteria:**
- Given a matching Edge process, visible attached window, Netflix URL/title evidence, and confirmation, when the preflight runs, then JSON reports `ready: true` and exit code 0.
- Given a missing profile, missing attachment, invisible/non-Netflix window, or no confirmation, when the preflight runs, then JSON reports `ready: false` with concrete reasons and exit code 1.
- Given only `audioRuleApplied muted=false sessions=0`, when the preflight runs, then it remains not ready because no attached Netflix window and playback confirmation exist.
- Given the self-test command, when it runs, then ready and blocked fixtures are distinguished without launching Edge or changing user profiles.

## Implementation Notes

Implemented on UM760 (MediaStation / F7BSI / EliteMini Series). The preflight reads the newest MediaStation JSONL log, matches `appWindowAttached.data.hwnd/rootPid` to the Edge process using `profiles\\netflix`, checks OS visibility and title/URL evidence, and asks for playback confirmation unless `-PlaybackConfirmed` or `-NonInteractive` is supplied.

## Plan Change Log

- 2026-10-28: Reopened after prerequisite 5.16 reached `built` at commit `8d5ed0e`; replaced the stale dependency block with the implementation plan.

## Review Triage Log

### 2026-10-28 — Review pass
- verdicts: 10 findings — high 0, medium 3, low 0, false 0, maybe-false 7
- findings:
  - [medium] [patch] Attached HWND now matches any Edge process in the required profile and checks root PID separately.
  - [medium] [patch] Netflix title evidence is restricted to the attached HWND process; a wrong-window fixture was added.
  - [medium] [patch] URL matching now validates the parsed netflix.com host.
  - [patch] [patch] Playback confirmation uses anchored affirmative answers.
  - [patch] [patch] Invalid profile paths and CIM failures return structured blocked results.
  - [maybe-false] [defer] Filesystem-newest log selection is not correlated to an explicit run id.
  - [maybe-false] [defer] Separate --user-data-dir path syntax is not parsed; current harness emits equals form.
  - [maybe-false] [defer] Machine identity is emitted in result and recorded in the plan.
  - [maybe-false] [defer] Malformed attachment fields beyond self-test are deferred.
  - [maybe-false] [defer] Full Netflix login/playback remains a user hardware acceptance step.

## Design Notes

The preflight is deliberately separate from the harness start smoke check. Ticket 5.16 proves the browser can be launched or adopted; this gate proves the acceptance environment is the intended Netflix profile and page and includes the human audible-playback confirmation required by the ticket.

## Verification

**Commands:**
- `powershell -NoProfile -ExecutionPolicy Bypass -File tools/check-netflix-preflight.ps1 -SelfTest` -- expected: PASS and exit code 0.
- `dotnet test MediaStation.slnx` -- expected: existing suite passes with no regressions.
- `dotnet build MediaStation.slnx` -- expected: 0 errors and 0 warnings.

**Manual checks (UM760):**
- Sign in to Netflix in `profiles\\netflix`, start visible playback, run the preflight with `-PlaybackConfirmed`, and record the JSON result before S15-S27. A result other than `ready: true` keeps S15-S27 unready.


## Auto Run Result

Status: built

Summary: Added tools/check-netflix-preflight.ps1, a Netflix-specific readiness gate for S15-S27. It checks the required Edge profile, attached visible window, current Netflix title or URL evidence, and explicit audible-playback confirmation. SelfTest covers ready, audio-only blocked, and wrong-window evidence.

Verification on UM760 (MEDIASTATION / F7BSI / EliteMini Series):
- powershell -NoProfile -ExecutionPolicy Bypass -File tools/check-netflix-preflight.ps1 -SelfTest: PASS.
- dotnet test MediaStation.slnx --no-restore: 1292 passed, 1 skipped, 0 failed.
- dotnet build MediaStation.slnx --no-restore: 0 errors, 0 warnings.
- Non-interactive live check without a matching Netflix process: exit 1 with concrete reasons and computer MEDIASTATION.

Residual risk: final login and audible playback confirmation still require the user's whole-epic UM760 acceptance; this ticket does not mark S15-S27 complete.
