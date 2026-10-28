---
title: 'DW-24/DW-27: calibrate-input hardening'
type: 'bugfix'
ticket: ''
created: '2026-10-28'
status: 'built'
baseline_revision: '0bf503541b69ff0beeb6b499d0f1559bde6c199e'
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
deferred: []
---

<intent-contract>

## Intent

**Problem:** In `--calibrate-input`, releasing any remote button clears every held usage, so a second held button is logged again (DW-24); and a remote whose VID/PID is not in `input.json` produces no log at all (DW-27).

**Approach:** Track held usages per VK→usage so a key-up releases only its own usage; report HID devices matching no configured signature once per VID/PID through a new `IRawInputSink.DeliverUnknownDevice`, logged as `rawInputUnknownDevice {signature}`. Unit tests for both.

</intent-contract>

## Implementation Notes

Oneshot: small, localized change in one class plus a sink method. Ran on UM760 (MEDIASTATION / F7BSI / EliteMini Series).

- `InputCalibration`: `_usageByVk` is filled by `TrackUsage` whenever a press knows both VK and usage (HID, raw keyboard, injected hook attach, flush). `Release(vk)` removes only that VK's usage. A key-up whose VK has no recorded usage no longer clears anything; `usage == 0` still releases all.
- `ReportUnknownDevice`: for HID reports and raw-keyboard events (VK != 0) not matching any configured signature (any source), delivers `VID_xxxx&PID_xxxx` once per mode; only the VID/PID reaches the log, never the device instance.
- `IRawInputSink` gained `DeliverUnknownDevice`; `LogRawInputSink` logs `rawInputUnknownDevice`. Only implementers: `LogRawInputSink` and the test fake.
- The ledger and the ticket files in `_bmad-output/backlog` are untouched (orchestrator records resolution). Saving a newly found signature into `input.json` is out of scope; discovery is by log, as the DW-27 decision states.

## Plan Change Log

## Review Triage Log

### 2026-10-28 — Review pass
- verdicts: 8 findings — high 0, medium 0, low 5, false 2, maybe-false 0 (one finding not graded: process note)
- findings:
  - `[low]` `[patch]` Unknown-device fallback logged the full device path including the instance id — added `WithoutInstance` (first two `#` segments) and a test with an ACPI path.
  - `[false]` `[reject]` "Release lost the AD-13 guard" — the old code also released on any key-up whose VK was in `_heldRawVks`/`_heldRemoteVks`; behavior for that case is unchanged. The `remote &&` guard was kept anyway so the old condition still gates the release.
  - `[low]` `[patch]` Stale `Release` doc comment — rewritten.
  - `[low]` `[patch]` `Flush` could re-add a VK→usage pair after the key-up — `TrackUsage` now records only while the VK is still held.
  - `[low]` `[reject]` `TrackUsage` overwrite when one VK pairs with two usages — not shown reachable (a held VK is deduped; usage 0 still clears everything); the fix would add branches.
  - `[false]` `[reject]` Unknown logging is broader than the intent (any unconfigured keyboard) — the intent says "log unknown HID devices when no configured signature matches"; a raw keyboard is one, and the wildcard `*` correctly suppresses it.
  - `[low]` `[reject]` Machine/evidence not recorded in the plan — recorded below.
  - `[low]` `[reject]` `dotnet test --project` questionable — it is the repository's own command form (see plan-dw-power-notifications-harness).

## Verification

**Commands:**
- `dotnet test --project tests/MediaStation.Core.Tests` -- expected: all pass

## Auto Run Result

Status: built

- Summary: DW-24 — held usages are tracked per VK→usage, so a key-up releases only its own usage and the other held remote buttons stay deduplicated. DW-27 — HID reports and raw-keyboard events from devices matching no configured signature are reported once per VID/PID as `rawInputUnknownDevice {signature}` (no instance id).
- Files changed:
  - `src/MediaStation.Core/Input/InputCalibration.cs` — `_usageByVk`, `TrackUsage`, scoped `Release`, `ReportUnknownDevice`.
  - `src/MediaStation.Core/Ports/Ports.cs` — `IRawInputSink.DeliverUnknownDevice`.
  - `src/MediaStation.Core/Input/LogRawInputSink.cs` — logs `rawInputUnknownDevice`.
  - `tests/MediaStation.Core.Tests/InputCalibrationTests.cs`, `LogRawInputSinkTests.cs` — 5 new tests (two held buttons, unknown VID/PID once, no-VID/PID path, configured never unknown, sink log).
- Review: 3 patches applied (all low), 5 rejected with reasons above; follow-up review not recommended.
- Verification (ran on UM760: MEDIASTATION / F7BSI / EliteMini Series): `dotnet build MediaStation.slnx` — 0 warnings, 0 errors; `dotnet test --project tests/MediaStation.Core.Tests` — 1249 passed, 0 failed.
- Residual risks: a press whose VK is learned only after its key-up leaves its usage held until the HID `usage == 0` release (real remotes send it). Whole-epic acceptance on UM760 with the physical remote remains the user's.
