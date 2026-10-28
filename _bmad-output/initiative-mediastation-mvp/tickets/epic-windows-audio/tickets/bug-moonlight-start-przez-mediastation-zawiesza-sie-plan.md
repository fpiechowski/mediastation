---
title: "Moonlight uruchomiony przez MediaStation zawiesza się na ustanawianiu połączenia"
type: 'bugfix'
ticket: 12
created: '2026-10-28'
status: blocked
operator_actions:
  - "On UM760 with a reachable Sunshine host, start Moonlight through MediaStation (MEDIASTATION_DEV_HARNESS=1, MEDIASTATION_HARNESS_MOONLIGHT, tile moonlight-pc) and confirm the handshake completes and the stream is visible (AC1)."
  - "If the hang persists, attach the appLaunch and appLaunchStage log entries plus the log of a manual start so the ticket can end as a Source conflict (AC5)."
baseline_revision: 781bf9cc13846f17c2d7144d6ed5277671cb29be
route: 'full'
route_source: 'auto'
risk: 'high'
review: 'thorough'
review_source: 'pinned'
lenses_ran: [blind-hunter, edge-case-hunter, verification-gap, intent-alignment]
review_loop_iteration: 0
followup_review_recommended: false
context: []
warnings: []
deferred: []
blocked_at: "2026-10-28"
blocked_reason: "awaiting operator: On UM760 with a reachable Sunshine host, start Moonlight through MediaStation (MEDIASTATION_DEV_HARNESS=1, MEDIASTATION_HARNESS_MOONLIGHT, tile moonlight-pc) and confirm the handshake completes and the stream is visible (AC1).; If the hang persists, attach the appLaunch and appLaunchStage log entries plus the log of a manual start so the ticket can end as a Source conflict (AC5)."
---

<intent-contract>

## Intent

**Problem:** Moonlight started by hand connects to the Sunshine host; the same Moonlight started by the dev harness stops at „Ustanawianie połączenia z komputerem…”. `DevHarnessHostPort.StartCore` starts it with a bare `ProcessStartInfo` (no working directory, whole MediaStation environment inherited, nothing logged about the launch), so the difference from a manual start is neither visible nor tested.

**Approach:** Make the launch contract an explicit, pure, tested value that matches a manual `Moonlight.exe stream <host> <app>` start (working directory = the exe's folder, no `MEDIASTATION_*` variables in the child) apart from the documented borderless/mute flags, and log it in full together with the stage the start reached.

## Boundaries & Constraints

**Always:** Keep `--display-mode borderless --mute-on-focus-loss` (R2/R8). Keep the SDL stream-window pick (`SDL_app`) and the minimize/restore and Home-return behaviour of ticket 8 unchanged. Never log values of variables whose names contain KEY, TOKEN, SECRET or PASSWORD. Harness code never changes window visibility or foreground (R4).

**Never:** Change `Platform/Window`, WindowManager or Orchestrator. Guess a Moonlight/Sunshine-side cause: if live evidence shows one, the ticket ends as a Source conflict (AC5), not as a code change.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Contract vs manual | exe `C:\M\Moonlight.exe`, host, app | Only differences from the manual contract are the borderless/mute args; cwd `C:\M`; no `MEDIASTATION_*` in child env | n/a |
| Env leak | parent env has `MEDIASTATION_DEV_HARNESS`, `MEDIASTATION_HARNESS_MOONLIGHT`, `PATH` | removals list = the two `MEDIASTATION_*` names (case-insensitive); `PATH` kept | n/a |
| Secret in env | `FOO_TOKEN=abc` | diagnostics show `FOO_TOKEN=<redacted>` | n/a |
| Stage on timeout | no `SDL_app` window within 20 s | one `warn` entry with stage `windowTimeout`, pid, exited flag, windows of the process tree (hwnd, class, title) | existing `windowTimeout` failure unchanged |

</intent-contract>

## Code Map

- `src/MediaStation.App/Hosts/DevHarness/HarnessMoonlight.cs` -- pure Moonlight decisions (`BuildArguments`, `ResolveExecutable`, command-line matcher); add launch contract, manual contract, diff, env removals and redacted env description here.
- `src/MediaStation.App/Hosts/DevHarness/DevHarnessHostPort.cs:213-331` -- `StartCore` builds `ProcessStartInfo` at ~277 (`UseShellExecute=false`, args only); `WaitForWindow`/`FindWindow` at ~356; `appWindowAttached` log at ~316. Apply working directory and env removals; add launch and stage logging.
- `src/MediaStation.App/Platform/Window/` -- `WindowTreePicker`, `WindowInfo`, `IWindowApi.EnumerateTopLevel` (read-only; reuse for the timeout dump).
- `tests/MediaStation.Core.Tests/DevHarnessTests.cs:141-225` -- existing Moonlight tests; add the contract tests beside them.
- `spike/win/moonlight.ps1` -- manual launch reference (`Start-Process`, args `stream host "app"`); do not change.

## Tasks & Acceptance

**Execution:**
- [ ] `src/MediaStation.App/Hosts/DevHarness/HarnessMoonlight.cs` -- add `MoonlightLaunch` (exe, args, working directory, env names to remove), `BuildLaunch`, `ManualLaunch` (args `stream host app` only), `Differences(manual, actual)`, `EnvironmentToRemove(names)` and `DescribeEnvironment(vars)` with redaction -- pure, testable contract
- [ ] `src/MediaStation.App/Hosts/DevHarness/DevHarnessHostPort.cs` -- for `MoonlightApp` use `BuildLaunch`: set `WorkingDirectory`, remove listed vars from `info.Environment`; log `appLaunch` (full command line, args, cwd, environment, pid after start); log `appLaunchStage` for `started` and `windowTimeout` (with window dump of the process tree and `exited`) and include window class on `appWindowAttached`
- [ ] `tests/MediaStation.Core.Tests/DevHarnessTests.cs` -- tests for every matrix row; the manual-vs-MediaStation comparison test must fail on the baseline code (cwd and env differ) and pass after
- [ ] Run the full test suite and a build; confirm ticket-8 tests still pass

**Acceptance Criteria:**
- Given the same exe, host and app, when the MediaStation launch contract is compared with the manual one, then only the documented borderless/mute args differ.
- Given a Moonlight start through the dev harness, when the process is started, then the log holds command line, args, cwd, environment (secrets redacted), pid, and later either the attached window (class, hwnd) or the `windowTimeout` stage with the window dump.
- Given the existing window and Home-return tests, when the suite runs, then all still pass.

## Implementation Notes

Ran on UM760 (host MediaStation, board F7BSI, model EliteMini Series). Added the pure launch contract to `HarnessMoonlight` (`BuildLaunch`, `ManualLaunch`, `Differences`, `EnvironmentToRemove`, `DescribeEnvironment`, `Apply`), applied it in `DevHarnessHostPort.StartCore` and added `appLaunch` / `appLaunchStage` logging. Window titles in the timeout dump come from a private `GetWindowTextW` P/Invoke because `WindowInfo` has no title and `Platform/Window` must not change.

## Plan Change Log

## Review Triage Log

### 2026-10-28 — Review pass
- verdicts: 26 findings — high 0, medium 3, low 8, false 3, maybe-false 0 (remaining rows are duplicates of the same claim, kept under the first row's verdict)
- findings:
  - `[medium]` `[patch]` Moonlight launch wiring (cwd, env removal) untested (verification-gap) — extracted `HarnessMoonlight.Apply` and added a test on a real `ProcessStartInfo`.
  - `[medium]` `[patch]` Timeout window dump has no title although the matrix row requires it (edge-case) — added title via private `GetWindowTextW`.
  - `[low]` `[patch]` `env-:` branch of `Differences` uncovered (verification-gap, blind) — added test.
  - `[low]` `[reject]` Redaction list too narrow, proxy credentials in values (blind, edge-case) — the intent fixes the list to KEY, TOKEN, SECRET, PASSWORD; a wider denylist is more than a direct correction.
  - `[low]` `[reject]` `Differences` compares args as a set (blind, edge-case) — both contracts come from the same builder; fix adds branching.
  - `[low]` `[reject]` Bare exe name gives null working directory (blind, edge-case) — `ResolveExecutable` returns a path to an existing file; unlikely.
  - `[low]` `[reject]` appLaunch logging throwing could leave the child running (edge-case) — log writes are not expected to throw; fix adds a guard.
  - `[low]` `[reject]` Windows dump empty when the root process exited early (edge-case) — `exited` flag is logged in the same entry.
  - `[low]` `[reject]` `commandLine` is not quoted for arguments with spaces (blind) — display only; `args` is logged as a list.
  - `[false]` `[reject]` `BuildLaunch` does not validate host/app (blind, edge-case) — `BuildArguments` calls `ThrowIfNullOrWhiteSpace` on both.
  - `[false]` `[reject]` Test cannot fail before the fix (intent-alignment) — the without-working-directory test pins the baseline state (null cwd) as a reported difference.
  - `[false]` `[reject]` Environment removal is case-sensitive (blind) — `ProcessStartInfo.Environment` is case-insensitive on Windows.
  - `[medium]` `[reject]` Cause of the hang unproven; the diff encodes a hypothesis (intent-alignment) — the intent says so itself; AC1 is owed to the operator and recorded as such.
  - Remaining rows repeat the claims above (extra tests for `exe`/`arg-`, magic stage string, `Differences` and `ManualLaunch` used only in tests, duplicated `commandLine`/`args`, Polish comments) and share their verdicts.

## Design Notes

UM760 check (2026-10-28): host `desktop` does not resolve and TCP 47989 is closed from this machine, so a live stream cannot be reproduced here. AC1 (handshake and visible stream) therefore waits for the operator on UM760 with a reachable Sunshine host; the cause is unproven until then, and the new `appLaunch`/`appLaunchStage` log is the evidence to attach if the hang persists (AC5 path).

## Verification

**Commands:**
- `dotnet build MediaStation.slnx` -- expected: 0 errors
- `dotnet test MediaStation.slnx` -- expected: all pass, contract tests included

## Auto Run Result

Status: awaiting-operator

- **Summary:** Moonlight now starts from the exe's folder with no `MEDIASTATION_*` variables in the child environment. The launch contract is a pure value that is compared with the manual start in tests. Each Moonlight start logs `appLaunch` and `appLaunchStage` (`started`, `windowTimeout` with a window dump), and `appWindowAttached` includes the window class.
- **Files changed:** `src/MediaStation.App/Hosts/DevHarness/HarnessMoonlight.cs` (launch contract, diff, env helpers, `Apply`); `src/MediaStation.App/Hosts/DevHarness/DevHarnessHostPort.cs` (applies the contract, stage and launch logging, title lookup); `tests/MediaStation.Core.Tests/DevHarnessTests.cs` (contract, env, redaction, `Apply` and `env-:` tests).
- **Review:** 3 patches applied (wiring test via `Apply`, window title, `env-:` test), none deferred, rest rejected with reasons in the triage log.
- **Follow-up review recommended:** false (patched: medium 2, low 1).
- **Verification (UM760):** `dotnet build MediaStation.slnx` gave 0 errors and 0 warnings; `dotnet test MediaStation.slnx` passed 1244 of 1244, including the ticket-8 tests. Live stream check not possible here: host `desktop` does not resolve and TCP 47989 is closed.
- **Residual risks:** the hang's cause is unproven; cwd and environment are a hypothesis. The title lookup has no test.
