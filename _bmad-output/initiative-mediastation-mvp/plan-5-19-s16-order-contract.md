---
title: 'S16: rozstrzygnięcie kolejności mute i minimize'
type: 'bugfix'
ticket: '19'
created: '2026-10-29'
status: 'built'
baseline_revision: '8945757859a20e94b547720b6a2f1a209af28592'
route: 'oneshot'
route_source: 'auto'
risk: 'high'
review: 'thorough'
review_source: 'pinned'
lenses_ran: ['blind-hunter', 'edge-case-hunter', 'verification-gap', 'intent-alignment']
review_loop_iteration: 0
context: []
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** S16 wymusza w formalnym checku kolejność timestampów `audioRuleApplied` przed `MinimizeWindow`, choć R7/AD-5 ustala kolejność wysłania komend, a AudioRouter i WindowManager kończą pracę na niezależnych wątkach. Przebieg z poprawnymi stanami (`muted=true`, `sessions=2`, `MinimizeWindow completed`) został więc odrzucony przez zbyt silną asercję.

**Approach:** Zachować implementację i jej kontrakt dispatchu `MuteProcessTree` → `MinimizeWindow`; usunąć `--ordered` tylko z asercji S16, pozostawiając wszystkie warunki stanu i liczności. Dodać regresję, która odtwarza kolejność logów z 2026-10-29 i potwierdza, że check przechodzi bez osłabiania globalnego mechanizmu `--ordered`.

</frozen-after-approval>

## Implementation Notes

Plan uses the oneshot route because the scoped change is a documentation/check assertion plus one focused regression test; no production implementation change is needed.

Verification machine: UM760 (MEDIASTATION / F7BSI / EliteMini Series).

Changed the S16 acceptance assertion and added a checker regression fixture. The existing Core tests remain the evidence for dispatch order; no production code was changed.

## Review Triage Log

- `false` — generic-event correlation concerns are outside this ticket's existing acceptance-window contract; S16 retains every prior state/count predicate and the app-specific window is established by the scenario.
- `false` — the rationale now names the production ports and Orchestrator test paths that establish dispatch ordering.
- `false` — the focused checker regression intentionally tests parser semantics; existing Orchestrator tests cover `MuteProcessTree → MinimizeWindow` dispatch.
- `false` — `--ordered` uses deterministic log-entry order, and equal timestamps do not alter the checker behavior under test.
- `false` — timestamp date is fixture-local; the regression reproduces the reported inverted completion sequence, not a date-dependent condition.

## Verification

**Commands:**
- `dotnet test tests/MediaStation.Core.Tests/MediaStation.Core.Tests.csproj --no-restore` -- expected: all tests pass.
- `python -m unittest .agents/skills/fp-acceptance/scripts/tests/test_acceptance.py` -- expected: acceptance checker tests pass, including S16 regression (pytest is not installed on this machine).
- `powershell -NoProfile -Command "$env:COMPUTERNAME; (Get-CimInstance Win32_BaseBoard).Product; (Get-CimInstance Win32_ComputerSystem).Model"` -- record current machine before verification.

**Manual checks (if no CLI):**
- Inspect S16 acceptance assertion and confirm it retains `sessions != 0`, `muted=true`, and completed `MinimizeWindow`, while documenting that log completion order is not the R7 contract.
