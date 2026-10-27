---
title: 'DW-41: Home in shell mode re-asserts the shell'
type: 'bugfix'
ticket: ''
created: '2026-10-27'
status: 'built'
baseline_revision: 'ec1632f4cdaf071a46c90773cea5b5b8dd61cf7a'
route: 'oneshot'
route_source: 'auto'
risk: 'medium'
review: 'quick'
review_source: 'pinned'
lenses_ran: [quick]
review_loop_iteration: 0
followup_review_recommended: false
context: []
warnings: []
deferred:
  - summary: >-
      Home on a Foreground app with no known Hwnd still returns no commands (no ShowShell).
    evidence: |-
      Orchestrator.Home.cs Foreground branch returns OrchestratorResult.None when Hwnd is null; pre-existing and outside DW-41's intent (no Foreground app).
    location: >-
      src/MediaStation.Core/Orchestrator/Orchestrator.Home.cs:58
    severity: low
---

<intent-contract>

## Intent

**Problem:** Home pressed while the shell is already the mode (app window raised by Alt+Tab or script) sends no window commands, so the app stays on top (DW-41).

**Approach:** In OnHome, when no app is Foreground or Starting, emit ShowShell and, for Background apps with a known Hwnd, MinimizeWindow, without changing app state; add Orchestrator tests for Home in shell mode with a Background app and for no app at all, and check the epic-windows-audio -Restore scenario in the check script.

</intent-contract>

## Tasks & Acceptance

Oneshot (~40 lines, one function plus tests).

## Implementation Notes

- `src/MediaStation.Core/Orchestrator/Orchestrator.Home.cs` — OnHome fall-through emits `MinimizeWindow` per Background app with Hwnd, then `ShowShell`, with a snapshot; no state change, no routing.
- Tests: `OrchestratorHomeTests` (no app; Background app), `OrchestratorActionTests`, `OrchestratorFailureTests` (Failed app: Home now reasserts shell; separate Fact), `OrchestratorStopTests` (Stopping: Home now emits ShowShell, command ids shift).
- Existing tests that asserted "Home ignored" were updated by design of the decision.

## Plan Change Log

## Review Triage Log

### 2026-10-27 — Review pass
- verdicts: 6 findings — high 0, medium 0, low 2, false 3, maybe-false 0 (1 process note)
- findings:
  - `[false]` `[reject]` check script untouched — the script already encodes Home with a raised Background app; -SelfTest and -Restore were run and passed (see Auto Run Result).
  - `[false]` `[reject]` plan lacks machine/evidence — recorded below in Auto Run Result.
  - `[low]` `[defer]` Foreground app without Hwnd still gets no ShowShell — pre-existing, outside the intent.
  - `[low]` `[reject]` Failed/Stopping windows not minimized — intent limits minimize to Background; fix would add branches.
  - `[low]` `[reject]` idle Home bumps snapshot rev — same as the existing Starting branch and the plan asks for a snapshot.
  - `[false]` `[reject]` Starting branch refactor lacks a test — Home_while_app_is_starting_shows_shell_without_focus_routing_or_timer_cancel covers it and passes; behavior unchanged.

## Verification

**Commands:**
- `dotnet test` -- expected: 1191 passed
- `tools/check-home-s10.ps1 -SelfTest` and `-Restore -InjectHome -Targets edge,desktop`

## Auto Run Result

Machine: UM760 (host MEDIASTATION, board F7BSI, model EliteMini Series).

Status: built

Summary: Home in shell mode with no Foreground/Starting app now emits MinimizeWindow for each Background app with a known Hwnd, then ShowShell, with a snapshot and no state change.

Files: `src/MediaStation.Core/Orchestrator/Orchestrator.Home.cs` (fix); `tests/MediaStation.Core.Tests/Orchestrator{Home,Action,Failure,Stop}Tests.cs` (new tests for no app and Background app; old "Home ignored" expectations updated).

Review: patches 0, deferred 1 (Foreground without Hwnd), rejected 5 as logged above.

Follow-up review recommended: false.

Verification on UM760: `dotnet test` 1191/1191 passed; `check-home-s10.ps1 -SelfTest` passed; `-Restore -InjectHome -Targets edge,desktop` 4/4 for edge and 4/4 for desktop (presses 2-4 pass, each with MinimizeWindow and ShowShell completed). Moonlight and chrome targets not run. Residual risk: moonlight/chrome Restore not exercised.
