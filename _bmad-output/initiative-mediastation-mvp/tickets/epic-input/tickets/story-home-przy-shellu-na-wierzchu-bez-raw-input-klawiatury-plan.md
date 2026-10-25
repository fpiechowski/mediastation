---
title: 'Home przy shellu na wierzchu bez Raw Input klawiatury'
type: 'bugfix' # feature | bugfix | refactor | chore
ticket: '12' # the entry id from the ticket tree, or the story file's stem when the entry has no id; empty outside it
created: '2026-10-24'
status: 'built' # draft | ready-for-dev | in-progress | in-review | built | done | blocked | dropped
baseline_revision: 'b291d0939fb61c50229438cbb19e317e6e25d4de'
route: 'full' # oneshot | full — set by step-02
route_source: 'auto' # pinned | auto — set with route by step-02
risk: 'high' # low | medium | high — set by step-02; a CI process can read it to decide how much review the change gets
review: 'quick' # none | quick | thorough — set by step-04
review_source: 'pinned' # pinned | auto — set with review by step-04
lenses_ran: [quick]
review_loop_iteration: 0
followup_review_recommended: false
context:
  - '{project-root}/AGENTS.md'
warnings: [oversized]
deferred: []
---

<intent-contract>

## Intent

**Problem:** Raw Input keyboard registration can silence the low-level keyboard hook while the shell is foreground. Loose HID correlation can also mislabel Home/Back or let one report authorize multiple presses, while calibration must still identify a remote button with a VK.

**Approach:** Make HID Consumer the only Raw Input registration in both normal operation and calibration, retain the LL VK hook as the Action trigger, and require a one-use, time-bounded match of remote signature plus the exact Home/Back usage–VK pair before assigning `source: remote`.

## Boundaries & Constraints

**Always:** Preserve one Action per press, VK-triggered Actions, existing key-up/long/repeat/SendInput and foreground rules, calibration's non-swallowing behavior, 60-second and physical-Esc exits, and the user's epic-level UM760 acceptance boundary. Ordinary runs require no experiment variable; `MS_INPUT_TRACE=1` is the only diagnostic override used for UM760 runs.

**Never:** Register Raw Input keyboard in a process with an active LL hook; use HID as an Action trigger; infer `remote` from VK, `input.json`, an unrelated HID usage, or a stale/reused report; add a permanently running helper process or change Actions/routing/epic status.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Normal shell | Default start, shell foreground, mapped VK | HID Consumer registered, keyboard Raw Input absent; one `keyEnter` down and one Action | Any hook/registration failure remains `inputHubError` |
| Confirmed Home/Back | Injected VK and matching remote signature, HID usage, VK pair within 50 ms | One `source: remote` Action; consume the report after the press | No match means keyboard source; Home over foreign window passes without Action |
| Invalid correlation | Missing, wrong device/page/usage/pair, outside window, or reused report | No `source: remote`; preserve normal foreground rules | No exception or extra Action |
| Calibration | Remote HID with or without paired VK plus ordinary keyboard | Remote-only `rawInput`; signature and `hasVk`, VK when present; no swallowing | Ignore ordinary keyboard; physical Esc or 60 seconds completes |
</intent-contract>

## Code Map

- `src/MediaStation.App/Platform/Input/InputHub.cs` — `Run`, `RegisterRawInput`, `OnRawInput`, and `HookCallback`; registration currently precedes the LL hook and includes both keyboard and HID Consumer. Make HID-only registration the fixed default for normal and calibration paths; preserve the hook and add a trace-only registration summary for runtime evidence.
- `src/MediaStation.App/Platform/Input/InputHubVariants.cs` and `src/MediaStation.App/CompositionRoot.cs` — spike 3.11 environment-controlled registration/gate variants and `inputHubVariant` logging. Retire experiment selectors from the shipped composition path; keep `MS_INPUT_TRACE` diagnostics.
- `src/MediaStation.Core/Input/InputPolicy.cs` — `OnHidConsumer`/`ResolveSource` retain the last report and currently allow broader attribution. Match remote signature, 50 ms window, HID Consumer `0x0223 → VK_BROWSER_HOME (0xAC)` or `0x0224 → VK_BROWSER_BACK (0xA6)`, then consume the report once. Other keys fall back to `keyboard`.
- `tests/MediaStation.Core.Tests/InputPolicyTests.cs` and `ForegroundGateTests.cs` — existing correlation and foreign-window behavior; update fixtures and cover wrong/reversed pairs, wrong usage/page/device, stale/future reports, one-report/two-press, Home swallow, and Back pass-through.
- `src/MediaStation.Core/Input/InputCalibration.cs` and `tests/MediaStation.Core.Tests/InputCalibrationTests.cs` — calibration already pairs HID and hook VK, filters ordinary keyboard, avoids swallowing, and has duration/Esc completion coverage. Preserve and make those cases explicit for no-VK buttons and both exits.
- `tools/spike-3-11.ps1` — existing synthetic foreground-run harness supports baseline/no-variant runs and captures per-process evidence; use it for three default-path launches with only `MS_INPUT_TRACE=1`.

## Tasks & Acceptance

**Execution:**
- [x] `src/MediaStation.App/Platform/Input/InputHub.cs` — register HID Consumer only in normal and calibration modes; keep LL VK hook active and non-swallowing in calibration; emit trace-only active-registration summary — satisfy AD-8 and make the runtime condition verifiable.
- [x] `src/MediaStation.App/Platform/Input/InputHubVariants.cs`, `src/MediaStation.App/CompositionRoot.cs` — remove the obsolete experiment selectors from production startup and ensure no variant variable is needed — prevent the spike path from masking the default behavior.
- [x] `src/MediaStation.Core/Input/InputPolicy.cs` — enforce exact, time-bounded, one-use Home/Back correlation and keyboard fallback — prevent false remote attribution and reused evidence.
- [x] `tests/MediaStation.Core.Tests/InputPolicyTests.cs`, `tests/MediaStation.Core.Tests/ForegroundGateTests.cs`, `tests/MediaStation.Core.Tests/InputCalibrationTests.cs` — exercise the correlation matrix, foreign-window behavior, no-VK/filtering, non-swallowing, and both calibration exits — prove policy and calibration invariants.
- [x] `tools/spike-3-11.ps1` run evidence and this plan's Verification section — execute three baseline launches with synthetic Home, Back, and OK on UM760; record each PID/log and registration evidence — prove the default path at the app surface.

**Acceptance Criteria:**
- Given a default app start with the shell foreground and only `MS_INPUT_TRACE=1`, when synthetic mapped Home, Back, and OK are sent in each of three separate launches, then every press has exactly one LL `keyEnter` down and one expected Action, the registration trace shows HID Consumer without keyboard Raw Input, and no process logs `inputHubError`.
- Given an injected Home or Back VK, when a matching remote HID Consumer report has the correct signature, usage–VK pair, and falls within the 50 ms window, then exactly one Action has `source: remote`; Home is swallowed with an Action over managed or foreign windows, while Back over a foreign window passes without an Action.
- Given missing, wrong, out-of-window, or already-consumed HID evidence, when a Home/Back VK arrives, then it is not attributed to `remote`; ordinary mapped keys still produce one Action with `source: keyboard`, including mappings from a `remote` input row.
- Given calibration receives a remote button and ordinary keyboard input, when it completes by physical Esc or timeout, then only pilot entries are logged with signature and `hasVk` (and VK when available), keys are not swallowed, and no process registers keyboard Raw Input alongside its LL hook.

## Implementation Notes

## Plan Change Log

## Review Triage Log

### 2026-10-24 — Review pass
- verdicts: 2 findings — high 0, medium 1, low 0, false 1, maybe-false 0
- findings:
  - `[medium]` `[patch]` Future-dated Home/Back evidence could be reused by a later press — cleared pending evidence on the first Home/Back event regardless of timestamp ordering and extended `Consumer_report_after_the_key_is_ignored` to assert the later matching press stays `keyboard`; targeted regression test passed.
  - `[false]` `[reject]` The matrix conflicts with confirmed Home over a foreign window — the ticket requires confirmed remote Home to be swallowed with an Action over foreign windows; the matrix's error-handling clause begins with “No match” and describes the unconfirmed case, so the acceptance target is consistent.

## Design Notes

The VK remains the sole Action trigger. HID Consumer is evidence for attribution and foreign-window Home policy only. A report is matched once and cleared on use or expiry, so a second key cannot inherit the first press's device identity. Calibration keeps the LL hook for injected VK pairing and physical Esc; its Raw Input registration is limited to HID Consumer.

## Verification

**Commands:**
- `dotnet test tests/MediaStation.Core.Tests -v q` — **PASS**, 899 passed, 0 failed, 0 skipped.
- `dotnet build src/MediaStation.App -c Release -v q` — **PASS**, 0 warnings and 0 errors.
- `powershell -NoProfile -ExecutionPolicy Bypass -File tools/spike-3-11.ps1 -Variants baseline,baseline,baseline -Presses 1 -StartDelaySeconds 3 -SettleSeconds 1` — **PASS** on the current Windows desktop (harness header: `MEDIASTATION`). Final result: `C:\Users\Użytkownik\AppData\Local\MediaStation\logs\spike-3-11-20261024-215710.md`; application log: `C:\Users\Użytkownik\AppData\Local\MediaStation\logs\mediastation-2026-10-24.jsonl`.

  Each process had one `rawInputRegistration` entry with `keyboard=false`, `hidConsumer=true`, `usagePage=0x0C`, `usage=0x01`, `success=true`, and zero `inputHubError` entries. For each PID, Home (`0x24`), Back (`0x1B`), and OK (`0x0D`) each produced exactly one `keyEnter` down and one expected Action; no Edge window appeared:

  | PID | Home down / Action | Back down / Action | OK down / Action |
  |---:|---:|---:|---:|
| 17468 | 1 / 1 | 1 / 1 | 1 / 1 |
| 14544 | 1 / 1 | 1 / 1 | 1 / 1 |
| 21736 | 1 / 1 | 1 / 1 | 1 / 1 |

The first post-patch harness attempt skipped the third process's OK because the target was no longer foreground; the complete command was repeated, and all three presses passed in each final process.

**Manual checks:** The harness brought the shell foreground and verified the foreground handle before each synthetic press. The registration summary confirms the active `RegisterRawInputDevices` configuration. Physical G20S Pro behavior, physical browser-window behavior, and S10 16/16 are N/A for this synthetic per-ticket run; include them in whole-epic UM760 acceptance via `fp-acceptance`.

## Auto Run Result

Status: built

Summary: Normal InputHub and calibration now register HID Consumer only; exact one-use Home/Back HID–VK correlation is required for `source: remote`. The ticket is built; whole-epic acceptance remains with the user on UM760.

Files changed:
- `src/MediaStation.App/CompositionRoot.cs` — remove spike experiment selection and pass trace-only registration logging to InputHub.
- `src/MediaStation.App/Platform/Input/InputHub.cs` — register HID Consumer only and report the active registration under `MS_INPUT_TRACE`.
- `src/MediaStation.App/Platform/Input/InputHubVariants.cs` — remove obsolete experiment selectors.
- `src/MediaStation.App/Platform/Input/AlwaysManagedProbe.cs` — remove the spike-only foreground bypass.
- `src/MediaStation.Core/Input/InputPolicy.cs` — enforce exact one-use Home/Back correlation and keyboard fallback.
- `tests/MediaStation.Core.Tests/InputPolicyTests.cs`, `ForegroundGateTests.cs`, `InputClockTests.cs`, `InputCalibrationTests.cs` — cover correlation, gating, clock rollover, calibration pairing, filtering, and exits.
- `tools/spike-3-11.ps1` — keep only the default MediaStation path and capture per-process registration evidence.
- This plan — record implementation, review triage, and current-machine verification.

Review findings: one medium patch applied; one false finding rejected with the no-match branch clarification above; none deferred. Patched counts: high 0, medium 1, low 0. Follow-up review recommendation: false.

Verification: 899/899 Core tests passed (0 skipped); Release build passed with 0 warnings and 0 errors; three final shell-foreground processes each produced one `keyEnter` down and one Action for Home, Back, and OK, showed `keyboard=false` and `hidConsumer=true`, and logged no `inputHubError`. One earlier harness attempt was rerun after the third process lost foreground before OK.

Residual risk: synthetic UM760 input does not establish physical G20S Pro behavior, browser handling of `VK_BROWSER_HOME`, or S10 16/16. Those checks remain in whole-epic UM760 acceptance via `fp-acceptance`.
