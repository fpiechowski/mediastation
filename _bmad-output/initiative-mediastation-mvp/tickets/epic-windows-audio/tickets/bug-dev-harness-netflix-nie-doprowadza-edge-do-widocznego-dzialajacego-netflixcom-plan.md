---
title: 'Dev harness Netflix nie doprowadza Edge do widocznego, działającego netflix.com'
type: 'bugfix'
ticket: 16
created: '2026-10-28'
status: done
baseline_revision: '3fb79877f7a5d09645c3ed99ce7752522695fb37'
route: 'full'
route_source: 'auto'
risk: 'high'
review: 'thorough'
review_source: 'pinned'
lenses_ran: [blind-hunter, edge-case-hunter, verification-gap]
review_loop_iteration: 0
followup_review_recommended: false
context: ['_bmad-output/initiative-mediastation-mvp/tickets/epic-windows-audio/tickets/bug-start-kafelka-netflix-konczy-sie-devharnessstartfailed-zamia-plan.md']
warnings: []
deferred: []
---

<intent-contract>

## Intent

**Problem:** A fresh acceptance run with `MEDIASTATION_HARNESS_URL` can attach an already running `profiles\\netflix` Edge window that still shows its previous page, so OK produces only an audio transition and no visible `netflix.com` material. The current harness treats profile adoption as complete without applying the configured URL and gives weak evidence when a browser launch or navigation does not become ready.

**Approach:** Make web profile reuse perform an explicit URL handoff through the same Edge executable and profile without starting a second browser root. Add a pure launch/diagnostic contract and stage logging that records URL, PID, command, arguments, working directory and a distinct navigation or window failure reason; cover fresh, adopted and failed paths with tests and a real-process smoke check.

## Boundaries & Constraints

**Always:** Keep `MEDIASTATION_DEV_HARNESS=1`, per-app `profiles\\<appId>`, `--kiosk`, existing WindowManager ownership, one `appWindowAttached` per app start, and the ticket 14 handoff behavior. Run verification on the identified machine UM760 (MEDIASTATION / F7BSI / EliteMini Series).

**Never:** Change WindowManager, Orchestrator, Moonlight behavior, CDP, or the product host contracts. Do not create a second browser root for a live profile, expose secrets in launch diagnostics, or claim page readiness from `audioRuleApplied` alone.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|---|---|---|---|
| Fresh profile | no matching Edge root; URL configured | one Edge root and visible `Chrome_WidgetWin_1`; one attach; launch log includes URL | timeout records `windowAttach` with full launch context |
| Live profile | matching root already owns a visible window | URL handoff is sent to that profile; same root is adopted; no second root; one attach | failed handoff records `navigation` and process context |
| Launcher handoff | Edge launcher exits 0 while profile root remains | wait/adopt existing root and keep one attach | distinguish `launcherHandoff` from `windowAttach` |
| Bad URL or unavailable navigation | process starts but configured navigation cannot be confirmed | do not report audio as readiness; retain window diagnostics and explicit stage | `devHarnessStartFailed` includes URL and reason |

</intent-contract>

## Code Map

- `src/MediaStation.App/Hosts/DevHarness/HarnessBrowser.cs` -- pure browser URL resolution and argument/launch contract; add navigation contract and safe diagnostic fields while preserving ticket 14 flags.
- `src/MediaStation.App/Hosts/DevHarness/DevHarnessHostPort.cs` -- `ProbeCore`, `StartCore`, adoption and timeout logging; perform URL handoff for a live profile, attach the existing root once, and classify failures.
- `src/MediaStation.App/Hosts/DevHarness/HarnessLaunch.cs` -- existing pure stage decisions reused by fresh and adopted launch paths; no behavior change required.
- `tests/MediaStation.Core.Tests/DevHarnessTests.cs` and `tests/MediaStation.Core.Tests/MediaStation.Core.Tests.csproj` -- link/test browser launch, live-profile reuse and failure-stage contracts.
- `tools/check-web-start.ps1` -- extend the real Edge smoke check to run with `MEDIASTATION_HARNESS_URL` and assert one visible attach, URL-bearing launch evidence, no second root and no unexpected warnings.

## Tasks & Acceptance

**Execution:**
- [ ] `HarnessBrowser.cs` -- define a pure web launch/navigation value and URL-safe diagnostic projection; keep existing argument order and profile matching.
- [ ] `DevHarnessHostPort.cs` -- route both startup probe adoption and StartApp adoption through one URL handoff path, log `appLaunch`/`appLaunchStage` with PID, command, args, cwd, URL and reason, and preserve one attach and existing-process ownership.
- [x] `HarnessLaunch.cs` -- reuse existing fresh and launcher-handoff stage classification; navigation failures are emitted by the web handoff path.
- [ ] `DevHarnessTests.cs` -- add matrix tests for fresh arguments, live profile handoff, launcher exit 0, bad URL/failure stage and no second root.
- [ ] `tools/check-web-start.ps1` -- run the clean and leftover passes with a diagnostic URL and assert the observable contract.

**Acceptance Criteria:**
- Given a fresh publish with `MEDIASTATION_DEV_HARNESS=1` and `profiles\\netflix`, when Netflix is selected with a configured URL, then a visible Edge window is attached exactly once and the launch evidence contains the configured URL.
- Given a live Netflix profile with a visible window, when Netflix is selected, then the same root is reused, the URL handoff is attempted, no second browser root appears, and the attach event is preserved.
- Given a missing window, launcher handoff, bad URL or navigation failure, when start does not reach readiness, then the log names the stage and includes PID, command, arguments, working directory, URL and cause; audio success is not used as readiness.
- Given the test suite and web smoke check on UM760, when they run, then the new regression tests and existing WindowManager/Moonlight tests pass.

## Implementation Notes

- Ran on UM760 (MEDIASTATION / F7BSI / EliteMini Series). Machine identification command: `powershell -NoProfile -Command "$env:COMPUTERNAME; (Get-CimInstance Win32_BaseBoard).Product; (Get-CimInstance Win32_ComputerSystem).Model"`; result: `MEDIASTATION`, `F7BSI`, `EliteMini Series`. The observed S15 log had a startup-adopted Edge profile and only `audioRuleApplied muted=false sessions=0`; the fix sends the configured URL through the same profile before adoption, while preserving the existing root and attach event.
- Added `HarnessBrowser.WebLaunch` and safe URL diagnostics. `DevHarnessHostPort` performs the handoff during both `ProbeExisting` adoption and `StartApp` live-profile adoption, and timeout logs now carry the URL, command, arguments, working directory, PID and failure stage.
- Updated `tools/check-web-start.ps1` to exercise `MEDIASTATION_HARNESS_URL=https://www.netflix.com/`; the real-process run opened a visible Edge window, completed Home/OK cycles in clean and leftover passes, and left no harness profile process running.
- Review fixes hardened URL redaction for credentials and non-http schemes, included full handoff failure context, handled a null process start, and made the smoke assertion compare the sanitized URL. A second UM760 smoke run with `https://www.netflix.com/watch/123?token=redacted` passed with exit code 0.

## Plan Change Log

## Review Triage Log

### 2026-10-28 — Review pass
- verdicts: 14 findings — high 0, medium 6, low 7, false 1, maybe-false 0
- findings:
  - `[low]` `[patch]` Smoke check compared raw URL with sanitized log URL — assertion now strips query and fragment before comparison.
  - `[medium]` `[patch]` Diagnostic URL could expose URI credentials or data payloads — `SafeDiagnosticUrl` now removes user-info and redacts unsupported schemes; focused tests added.
  - `[low]` `[patch]` Handoff stage omitted launch context — URL, args, command line and working directory are now passed to `LogStage`.
  - `[low]` `[patch]` Null handoff process could look successful — null start now emits an explicit navigation failure stage with reason and context.
  - `[medium]` `[defer]` No host-level test directly exercises ProbeExisting and StartApp handoff wiring — pure contract and real-process smoke cover the behavior, but a fake-process host seam would improve regression isolation.
  - `[medium]` `[defer]` URL navigation readiness is not independently confirmed after Edge accepts the handoff — dev harness has no CDP and the remaining evidence is visible-window attachment plus launch result; whole-epic acceptance must confirm the displayed page/material.
  - `[false]` `[reject]` Multiple matching roots cause multiple handoffs — the adoption loop returns after the first visible matching window, and the handoff is now inside that guard.
  - `[low]` `[patch]` Handoff was attempted before confirming a visible window — moved the call after the window match so a failed adoption does not add an unnecessary launch.
  - `[low]` `[reject]` HarnessLaunch stage centralization was missing — existing `WebFailedStage` already owns fresh-launch classification; the plan now records reuse rather than an absent code change.
  - `[low]` `[patch]` Plan lacked exact machine command evidence — recorded the required identification command and UM760 result.
  - `[medium]` `[defer]` Removing either host handoff call would not be caught by unit tests — retained as a verification gap; the live smoke check passed both clean and leftover paths.
  - `[medium]` `[patch]` Safe URL redaction gap repeated by verification tracing — covered by the same sanitizer fix and tests.
  - `[medium]` `[defer]` Failed navigation after a successful launcher handoff is not observable as page readiness — retained for manual whole-epic acceptance because no CDP is in scope.
  - `[low]` `[patch]` Process-start failure context lacked cwd/PID — failure logging now includes cwd and a null PID explicitly.

## Design Notes

A live profile is already adopted by the startup probe before the user presses OK. Therefore the fix must make the URL handoff part of profile probing/adoption; changing only the `StartApp` path would leave the observed `audioRuleApplied muted=false sessions=0` reproduction unchanged.

## Verification

**Commands:**
- `dotnet test MediaStation.slnx --no-restore` -- passed: 1290 passed, 1 skipped, 0 failed.
- `dotnet test tests/MediaStation.Core.Tests/MediaStation.Core.Tests.csproj --no-restore --filter FullyQualifiedName~DevHarnessTests` -- passed: 69/69.
- `dotnet test MediaStation.slnx --no-restore` (after review fixes) -- passed: 1292 passed, 1 skipped, 0 failed.
- `dotnet build MediaStation.slnx --no-restore` -- passed: 0 warnings/errors; web contracts/typecheck completed.
- `dotnet publish src/MediaStation.App -c Release -r win-x64 --self-contained false --no-restore` -- passed: publish succeeded.
- `powershell -NoProfile -ExecutionPolicy Bypass -File tools/check-web-start.ps1 -Cycles 1 -DiagnosticUrl https://www.netflix.com/watch/123?token=redacted` -- passed on UM760 with exit code 0: clean and leftover Edge runs attached one visible window each, URL-bearing `appLaunch`, Home/OK cycles completed, no `devHarnessStartFailed`; harness profile was cleaned up.

**Manual checks (if no CLI):**
- Inspect the latest JSONL run for `appLaunch`, `appLaunchStage`, `appWindowAttached`, `devHarnessStartFailed`; confirm the attached window is visible and shows the configured URL.

## Auto Run Result

Status: built

Summary: the dev harness now sends `MEDIASTATION_HARNESS_URL` through an already running Edge profile before adoption, preserving one profile root and one `appWindowAttached` event. Launch and failure diagnostics include a safe URL, PID, command, arguments, working directory and stage; credential-bearing and unsupported URLs are redacted.

Files changed: `src/MediaStation.App/Hosts/DevHarness/HarnessBrowser.cs` (launch value and URL sanitization), `src/MediaStation.App/Hosts/DevHarness/DevHarnessHostPort.cs` (profile handoff and diagnostics), `tests/MediaStation.Core.Tests/DevHarnessTests.cs` (launch and redaction tests), `tools/check-web-start.ps1` (URL-aware smoke assertions), and this ticket plan.

Review findings: 8 patch findings applied, 4 medium findings deferred as verification/readiness gaps, 1 false finding rejected, and 1 low finding rejected or corrected in the plan. Follow-up review recommendation: false; no high findings or unresolved code defects remain.

Residual risks: the harness still cannot prove page content or playback without CDP; the user must confirm `netflix.com` and material readiness during whole-epic UM760 acceptance. Host-level fake-process coverage for the two handoff call sites remains a future verification improvement.

