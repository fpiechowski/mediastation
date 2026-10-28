---
title: "Dev harness Moonlight nie znajduje okna SDL_app po starcie"
type: 'bugfix'
ticket: 13
created: '2026-10-28'
status: done
baseline_revision: '57fad6ff65715975054b961aee67c548a623d749'
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
---

<intent-contract>

## Intent

**Problem:** A Moonlight tile started through the dev harness stays on „Ustanawianie połączenia”, and after 20 s the harness logs `windowTimeout` for `SDL_app`. Live evidence on UM760 (2026-10-28): the same `Moonlight.exe stream desktop Desktop ...` started by hand from PowerShell hangs identically, because `apps.json` has `host: "desktop"` and Moonlight logs `Processing new PC at "desktop:47989"` then `serverinfo request failed: HostNotFoundError`; the paired host is named `alder` (192.168.0.64), and `stream alder Desktop ...` started by hand opens the stream. The harness log cannot tell a handshake failure from a window-pick failure.

**Approach:** Fix the machine config (`host` → `alder`) and verify a MediaStation start reaches `appWindowAttached`. Make the failure stage explicit: at timeout classify `handshake` (only the Qt connecting window exists) vs `windowAttach`, and attach the interesting tail of the newest Moonlight log (`HostNotFoundError`, `failed`) to `appLaunchStage`, so a wrong host shows up in one log entry. Cover the delayed/missing window and restart scenarios with a testable wait loop.

## Boundaries & Constraints

**Always:** Keep the launch contract of ticket 12, `--display-mode borderless --mute-on-focus-loss`, the `SDL_app` pick and the ticket-8 Home-return behaviour. Never log values of variables with KEY, TOKEN, SECRET or PASSWORD in the name. Harness code never changes window visibility or foreground (R4). Keep the `devHarnessStartFailed reason=windowTimeout` value.

**Never:** Change `Platform/Window`, WindowManager or Orchestrator. Do not pre-resolve the host with DNS in code (Moonlight resolves paired names itself). Do not edit pairing or Sunshine settings.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Wrong host | tree windows: only visible Qt window, no `SDL_app` | stage `handshake` | log shows Moonlight log hints |
| Window pick fails | tree has `SDL_app` window that is not selectable (hidden/owned) | stage `windowAttach` | n/a |
| Delayed window | `SDL_app` appears after N polls, before timeout | wait returns that window | n/a |
| Missing window | no window until timeout, process alive | wait returns null (`windowTimeout`) | n/a |
| Process exits early | `hasExited` true | wait returns null | n/a |
| Restart after close | first wait timed out/stopped, second start finds window | second wait returns the window | n/a |
| Log hints | lines with Warning/Error/failed among noise | last 5 matching lines, in order | empty list when none |

</intent-contract>

## Code Map

- `src/MediaStation.App/Hosts/DevHarness/HarnessMoonlight.cs` -- pure Moonlight decisions; add `ClassifyStage`, `LogHints`, `LatestLogFile`.
- `src/MediaStation.App/Hosts/DevHarness/HarnessWait.cs` (new) -- injectable window wait loop extracted from `DevHarnessHostPort.WaitForWindow`.
- `src/MediaStation.App/Hosts/DevHarness/DevHarnessHostPort.cs` -- `WaitForWindow` (~387), `LogStage` (~420): use `HarnessWait`, add `failedStage` and `moonlightLogHints` to the `windowTimeout` entry.
- `tests/MediaStation.Core.Tests/MediaStation.Core.Tests.csproj` -- link `HarnessWait.cs` beside `HarnessMoonlight.cs` (line 59).
- `tests/MediaStation.Core.Tests/DevHarnessTests.cs` -- tests next to the Moonlight ones (~197-300).
- `%LOCALAPPDATA%\MediaStation\config\apps.json` -- machine config outside the repo: `moonlight-pc.host` `desktop` → `alder` (backup first).
- `tools/check-moonlight-return.ps1` -- reference only; the end-to-end check of the Home return (do not change).

## Tasks & Acceptance

**Execution:**
- [ ] `src/MediaStation.App/Hosts/DevHarness/HarnessMoonlight.cs` -- add `ClassifyStage(windows, tree, expectedClass)`, `LogHints(lines)` and `LatestLogFile(files)` -- pure stage and log diagnostics
- [ ] `src/MediaStation.App/Hosts/DevHarness/HarnessWait.cs` -- add `FindWindow` loop with injected find/hasExited/stopping/sleep/clock -- testable timeout, delay and restart
- [ ] `src/MediaStation.App/Hosts/DevHarness/DevHarnessHostPort.cs` -- use `HarnessWait`; on Moonlight timeout log `failedStage` and `moonlightLogHints` (newest `%TEMP%\Moonlight-*.log`)
- [ ] `tests/MediaStation.Core.Tests/*` -- tests for every matrix row; run full build and tests
- [ ] `%LOCALAPPDATA%\MediaStation\config\apps.json` -- set host `alder`, then start through MediaStation on this machine and confirm `appWindowAttached`, then OK → Home → OK (ticket 8)

**Acceptance Criteria:**
- Given reachable Sunshine and `moonlight-pc` with the paired host name, when the tile is started through MediaStation, then the stream window appears and the log has `appWindowAttached` with `class=SDL_app`, no `windowTimeout`.
- Given a start that does not reach the stream, when the timeout fires, then `appLaunchStage` states `failedStage` (`handshake` or `windowAttach`) plus the Moonlight log hints.
- Given the suite, when it runs, then the delayed/missing-window and restart tests and all ticket-8 and ticket-12 tests pass.

## Implementation Notes

Ran on UM760 (host MediaStation, board F7BSI, model EliteMini Series). Root cause found live before any code: `apps.json` `moonlight-pc.host` was `desktop`, which is not a paired Moonlight host. A hand start `stream desktop Desktop ...` logged `Processing new PC at "desktop:47989"` and `serverinfo request failed ... HostNotFoundError`, staying on the Qt connecting window exactly like the harness; `stream alder Desktop ...` (paired name, 192.168.0.64) streamed. Config fixed to `alder` (backup `apps.json.bak-ticket13`). Code adds `failedStage` (handshake/windowAttach) and `moonlightLogHints` to the timeout `appLaunchStage`, and an injectable wait loop (`HarnessWait`).

## Plan Change Log

## Review Triage Log

### 2026-10-28 — Review pass
- verdicts: 30 findings — high 0, medium 1, low 5, false 3, maybe-false 0 (remaining rows duplicate the claims below and share their verdicts)
- findings:
  - `[medium]` `[patch]` Timeout wiring (`failedStage`, hints) untested (verification-gap) — added pure `TimeoutDiagnostics` helper and a test.
  - `[low]` `[patch]` `EnumerateTopLevel` called twice, snapshots can disagree (blind, edge-case) — single snapshot reused.
  - `[low]` `[patch]` `ClassifyStage` doc says hidden/owned, code checks class only (blind, edge-case, intent) — doc corrected.
  - `[low]` `[patch]` Restart test name implies host-port restart (blind, verification-gap, edge-case) — renamed to state it is the stateless loop.
  - `[false]` `[reject]` Moonlight hints attached to non-Moonlight apps — the timeout stage log is guarded by `moonLaunch is not null`.
  - `[false]` `[reject]` `ClassifyStage` with null class mislabels — only called with `SDL_app` for Moonlight.
  - `[false]` `[reject]` Diff lacks launch contract/AC2 command logging (intent-alignment) — `appLaunch` from ticket 5.12 already logs command, args, cwd, env, pid.
  - `[low]` `[reject]` Stale Moonlight log picked when none written by this launch — Moonlight writes its log at start; guard adds complexity.
  - `[low]` `[reject]` Hints may contain KEY/TOKEN words, whole file read, crowding by later warnings — Moonlight failure logs are small, redaction rule concerns env variables; fix adds branching.
  - `[low]` `[reject]` Zero timeout / no final find after exit — behaviour unchanged from the previous loop; exited process is a failure anyway.
  - `[low]` `[reject]` No doc/ticket or config evidence in diff, AC1/AC4 live-only (verification-gap, intent) — live check recorded in Auto Run Result; config is outside the repo.
  - Remaining rows repeat the claims above.

## Auto Run Result

Status: built

- **Summary:** The hang was a wrong host name in `%LOCALAPPDATA%\MediaStation\configpps.json` (`desktop`, not a paired host), not the launch contract or window pick; a hand start with it hangs identically. Host set to `alder`. The timeout log now names the failed stage and carries Moonlight log hints, so a wrong host is visible in one entry.
- **Files changed:** `src/MediaStation.App/Hosts/DevHarness/HarnessMoonlight.cs` (`ClassifyStage`, `LogHints`, `LatestLogFile`, `TimeoutDiagnostics`); `HarnessWait.cs` (new, injectable wait loop); `DevHarnessHostPort.cs` (uses both, logs `failedStage`, `moonlightLogHints`); `tests/MediaStation.Core.Tests/DevHarnessTests.cs` and `.csproj` (tests, link). Outside repo: `apps.json` host.
- **Review:** 4 patches applied (1 medium, 3 low), none deferred, rest rejected with reasons in the triage log.
- **Follow-up review recommended:** false (patched: medium 1, low 3).
- **Verification (UM760):** `dotnet build MediaStation.slnx` 0 errors; `dotnet test MediaStation.slnx` 1273 passed, 1 skipped, 0 failed. Live: Release publish and `tools/check-moonlight-return.ps1 -MoonlightHost alder` gave `appWindowAttached class=SDL_app`, 4/4 Home/OK cycles (minimized and visible, restored on top), one RTSP handshake, no warn/error, no `processExited` (AC1, AC4).
- **Residual risks:** the host name in `apps.json` is machine config, so a renamed Sunshine host reproduces the symptom (now diagnosable via `failedStage=handshake` and hints). AC3's restart scenario is tested at the wait-loop level, not through the host port.

## Design Notes

Evidence (UM760, 2026-10-28): Moonlight log of a hand start with `desktop`: `Processing new PC at "desktop:47989"`, `serverinfo request failed ... HostNotFoundError`, window title stays `Moonlight` (Qt, no `SDL_app`). With `alder`: `alder - Moonlight` window, AV1 decode, stream running. The 5.12 contract (cwd, env) was therefore not the cause; the only difference that mattered is the host name in `apps.json`.

## Verification

**Commands:**
- `dotnet build MediaStation.slnx` -- expected: 0 errors
- `dotnet test MediaStation.slnx` -- expected: all pass

**Manual checks (if no CLI):**
- Start through MediaStation with `MEDIASTATION_DEV_HARNESS=1` and `MEDIASTATION_HARNESS_MOONLIGHT`; read `appLaunchStage` and `appWindowAttached` in `%LOCALAPPDATA%\MediaStation\logs`.
