---
title: 'Story 6.4: Wersja przeglądarki i rozróżnialne błędy startu'
type: 'feature'
ticket: '4'
created: '2026-10-29'
status: done
baseline_revision: '4a7262f4c2fd56b19eea9c0ba9f15523f7a7c1bf'
route: 'full'
route_source: 'auto'
risk: 'high'
review: 'thorough'
review_source: 'pinned'
lenses_ran: [blind-hunter, edge-case-hunter, verification-gap, intent-alignment]
review_loop_iteration: 0
followup_review_recommended: false
context:
  - '{project-root}/_bmad-output/initiative-mediastation-mvp/architecture-mediastation/architecture-mediastation.md'
warnings: [oversized]
deferred: []
---

<intent-contract>

## Intent

**Problem:** BrowserHost (6.2/6.3) never reads the browser version, so a silent Edge update goes unnoticed (AD-13). Its start errors are not told apart: a profile or port held by another process makes the launch hand off and fail as a generic `launcherExited`, and `cdpUnavailable` carries no version.

**Approach:** Before each launch: check the profile lock and the CDP port and answer `CommandFailed(ProfileInUse|PortInUse)` without launching; read `FileVersion` of the resolved exe and `profiles\<appId>\Last Version`, log both and log `browserVersionChanged` on a difference. After CDP connects, confirm the version with `Browser.getVersion`. Pin down and test the CdpUnavailable / timeout / ProcessExited distinction, and prove it live on the current machine with a new smoke script.

## Boundaries & Constraints

**Always:**
- Ordering in `StartCoreAsync`: unknown app → page assets → exe → register session and stop the previous one (existing) → **profile check → port check** → version read and log → launch. A check failure unregisters the session, logs `warn browser browserStartFailed { reason: profileInUse, profileDir }` or `{ reason: portInUse, cdpPort }` and posts `CommandFailed(ProfileInUse)` / `CommandFailed(PortInUse)`. Nothing is launched. When both are held, the answer is `ProfileInUse`.
- Version (AD-13): current = `FileVersion` of the exe used for the launch; previous = text of `<profileDir>\Last Version`, read through the existing `readFile` seam. Parse = trim, then `System.Version.TryParse`, with missing build/revision normalised to 0 for comparison. Missing, empty or unparsable file, or an unreadable exe version, = unknown (logged as `"unknown"`, no change entry). Log `info browser browserVersion { appId, version, previous }` on every launch, plus `info browser browserVersionChanged { appId, from, to }` when both versions are known and differ. Both entries come before `appLaunch` (written before the browser rewrites `Last Version`, so a crash after the launch cannot lose the change). Not on adoption (6.5).
- After `InstallAsync` and before `Page.navigate`, send `Browser.getVersion` under the CDP deadline token. Parse `product` (`Edg/156.0.4314.50`, `Chrome/…`) after the last `/`. `cdpConnected` gains `browserVersion` (that value or `"unknown"`). Both known and different → `warn browser browserVersionMismatch { appId, expected, actual }`. A `CdpException` reply or an unparsable product → `warn browser browserVersionUnconfirmed { appId, reason }`, and the start still completes. A timeout keeps the existing `setupFailed` path. Not sent on reconnect.
- `cdpUnavailable` gains `browserVersion` (the pre-start `FileVersion` or `"unknown"`).
- Distinction (AD-13), unchanged in behaviour and now pinned by tests. Process alive without CDP within `cdpConnectTimeout` → `AppWindowAttached` first, then `CommandFailed(CdpUnavailable)`, with no `ProcessExited` and no kill (the Orchestrator then mutes and minimizes: existing `Command_failed_for_start_after_attached_window_enters_failed_from_foreground`). Process exit during CDP wait → `ProcessExited`, then `CommandFailed(Unexpected)` + `cdpAbandoned`, with no `cdpUnavailable`. No window in 20 s → `CommandFailed(Unexpected)` with `windowTimeout`, and no `AppWindowAttached`.
- `StopSessionAsync` awaits the root's exit after `KillTree`, bounded at 5 s, so a restart's profile/port check sees the old browser gone (closes the core of DW-53).
- `ProcessBrowserLauncher` removes `__COMPAT_LAYER` from the child environment. Probe on UM760: with `__COMPAT_LAYER=DetectorsAppHealth` inherited, Edge relaunches itself (`--edge-skip-compat-layer-relaunch`), so the launched root exits at once and BrowserHost would report a false exit.
- Seams, so tests stay process-free: `Func<string, string?> readFileVersion`, `Func<string, bool> isProfileInUse`, `Func<int, bool> isPortInUse`, wired in `CompositionRoot.CreateWebHost`.
- Production probes stay free of WPF/Win32 types, linked into `MediaStation.Core.Tests` and tested on real resources:
  - `IsProfileInUse`: open `<profileDir>\lockfile` with `FileMode.Open, FileAccess.Write, FileShare.ReadWrite|Delete`. Missing file or folder → false. Sharing or lock violation (`0x80070020`/`0x80070021`) or `UnauthorizedAccessException` (delete pending while the browser exits) → true. Any other error → false. Probe: a running Edge gives `IOException 0x80070020`, and the file is gone after exit.
  - `IsPortInUse`: any `IPGlobalProperties.GetActiveTcpListeners()` endpoint on the port (any address), with errors → false.
  - `ReadFileVersion`: `FileVersionInfo` `Major.Minor.Build.Private`, with all-zero or errors → null.
- Polish code comments matching the surrounding files; never throw across the port (AD-4).

**Never:** new `ErrorCode` values or Orchestrator/WindowManager/AudioRouter changes (start timeout stays the Orchestrator's `Starting` timer, AD-14); a Core Event for the version change (log only, AD-13); a smoke `--smoke` mode or exit codes (epic-deploy-resilience); ProbeExisting/RenewSession (6.5); `Runtime.enable`/`Runtime.evaluate`; touching `profiles\netflix` in any script; editing ticket files or `deferred-work.md`.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Version changed | `Last Version` `155.0.4283.45`, exe `156.0.4314.50` | `browserVersion`, `browserVersionChanged{from 155…,to 156…}` before `appLaunch` | — |
| Same version | both `156.0.4314.50` (or `…050` vs `…50`, `156.0.4314` vs `156.0.4314.0`) | `browserVersion` only | — |
| Unknown version | file missing / empty / `garbage` / exe version null | `"unknown"` in `browserVersion`, no change entry | start continues |
| Profile held | lockfile held | `browserStartFailed{profileInUse}`, `CommandFailed(ProfileInUse)`, launcher never called | — |
| Port held | listener on `cdpPort` | `browserStartFailed{portInUse}`, `CommandFailed(PortInUse)`, no launch | both held → ProfileInUse |
| CDP never answers | alive process, no CDP | `AppWindowAttached` → `cdpUnavailable{browserVersion}` → `CommandFailed(CdpUnavailable)` | no ProcessExited, no kill |
| getVersion mismatch / error | product `Edg/157…` / error reply | `browserVersionMismatch` / `browserVersionUnconfirmed`, `CommandCompleted` | start completes |
| Restart | previous tree exits only when killed | profile/port check runs after the old root exits | exit wait ≤ 5 s |

</intent-contract>

## Code Map

- `src/MediaStation.App/Hosts/Browser/BrowserHost.cs`:
  - `StartCoreAsync` (L189–304): insert the checks and the version read after the previous-session stop (L243–246) and before `_launcher.Start` (L252); extend `FailStart` (L854) with an `ErrorCode` parameter (default `Unexpected`);
  - `ConnectCdpAsync` (L306–383): `Browser.getVersion` between `InstallAsync` and `Page.navigate`; `cdpConnected` at L364;
  - `CdpFailed` (L701–728): add `browserVersion`, and drop the "6.4 doda" comment;
  - `StopSessionAsync` (L816–841): bounded exit wait after `KillTree`;
  - keep the pre-start `FileVersion` on `Session` for the `cdpUnavailable`/mismatch entries;
  - keep `InstallAsync` (reconnect reuses it) free of `getVersion`.
- `src/MediaStation.App/Hosts/Browser/BrowserProcess.cs`: `ProcessBrowserLauncher.Start`, to remove `__COMPAT_LAYER` from `info.Environment`; `BrowserPlatform` has the other production adapters.
- New `src/MediaStation.App/Hosts/Browser/BrowserVersion.cs` (pure parse/compare/format, product parse) and `BrowserPreflight.cs` (`IsProfileInUse`, `IsPortInUse`, `ReadFileVersion`, without Win32 window types); link both in `tests/MediaStation.Core.Tests/MediaStation.Core.Tests.csproj` like the other `Hosts\Browser` files.
- `src/MediaStation.App/CompositionRoot.cs` `CreateWebHost` (~L358–382): pass the new seams.
- `tests/MediaStation.Core.Tests/BrowserHostTests.cs`:
  - `FakeLauncher` L1757, `FakeProcess` L1792, `FakeTransport` L1852, and the host factory;
  - existing tests that assert the exact CDP method list or reply order must gain `Browser.getVersion` (after `Page.addScriptToEvaluateOnNewDocument`, before `Page.navigate`); the reconnect tests must not.
- `tests/MediaStation.Core.Tests/OrchestratorFailureTests.cs` L124–145: the existing CdpUnavailable → mute/minimize test. Reference it and do not duplicate it.
- `src/MediaStation.App/Orchestrator/OrchestratorLoop.cs` L130: `warn orchestrator commandFailed { errorCode }`, which the smoke asserts; check how the enum is serialised.
- `tools/check-browserhost-tracer.ps1`: template for the new smoke (`apps.json` backup/restore, `Send-Key`, `Get-RunEntries`, `Save-Evidence`, `Get-ProfileProcesses`, `Stop-Profile`, the `IsIconic` helpers).
- Probe facts (UM760, Edge 156.0.4314.50):
  - `"devtools":{"remote_debugging":{"allowed":false}}` in the profile's `Local State` keeps Edge running with a window and no CDP listener;
  - `Browser.getVersion` on the page socket returns `product: "Edg/156.0.4314.50"`;
  - `Last Version` = `156.0.4314.50` after a launch.

## Tasks & Acceptance

**Execution:**
- [x] `src/MediaStation.App/Hosts/Browser/BrowserVersion.cs`: `Parse`, `FromProduct`, `Changed`, `Format` (rules in Always), so the version logic is pure and unit-testable.
- [x] `src/MediaStation.App/Hosts/Browser/BrowserPreflight.cs`: the three production probes (rules in Always).
- [x] `src/MediaStation.App/Hosts/Browser/BrowserHost.cs`: new seams; preflight checks; version read and logs; `Browser.getVersion` confirmation; `cdpUnavailable.browserVersion`; bounded exit wait in Stop; class summary updated for 6.4.
- [x] `src/MediaStation.App/Hosts/Browser/BrowserProcess.cs`: strip `__COMPAT_LAYER`.
- [x] `src/MediaStation.App/CompositionRoot.cs`: wire the seams.
- [x] `tests/MediaStation.Core.Tests/BrowserVersionTests.cs`, `BrowserPreflightTests.cs`, and `BrowserHostTests.cs` (+ csproj links), covering:
  - every Matrix row;
  - parse cases: null, `""`, `"  \r\n"`, `garbage`, `1`, overflow, `156.0.4314.050`, the 3-part vs 4-part pair, `Edg/…`, `Chrome/…`, product without `/`;
  - log order (`browserVersion`/`browserVersionChanged` before `appLaunch`);
  - launcher not called on ProfileInUse/PortInUse, and the session unregistered (a later Start works);
  - getVersion error and mismatch still give `CommandCompleted`;
  - reconnect sends no `getVersion`;
  - the CdpUnavailable / exit-during-connect / window-timeout distinction asserted on the posted messages;
  - Stop waits for the root exit, and gives up after 5 s;
  - preflight on a real temp `lockfile` held with `FileShare.Read`, a missing folder, and a `TcpListener` on loopback port 0.
- [x] `tools/check-browserhost-errors.ps1`: repeatable smoke on the current machine. It uses a throwaway web tile `browserhost-check` (service `netflix`, `cdpPort` 9229, `profiles\browserhost-check` deleted before and after; never `profiles\netflix`) and removes `__COMPAT_LAYER` from its own environment. Each scenario starts MediaStation (harness off), injects OK and asserts, then closes MediaStation and the profile's browser tree. Scenarios:
  - A: `Last Version` = `155.0.4283.45`;
  - A2: rerun, so the version is the same;
  - B: `Last Version` = `garbage`;
  - C: a `TcpListener` on `127.0.0.1:9229`;
  - D: an Edge started by the script on the profile;
  - E: the `Local State` pref set to false.

  Evidence goes to `logs\browserhost-errors-evidence-<ts>` (`measurements.jsonl`, screenshots after A's start and after E). Exit 0 = PASS.

**Acceptance Criteria:**
- Given a published build and the test tile whose `Last Version` is `155.0.4283.45`, when OK is injected, then the log has `browserVersionChanged { from: 155.0.4283.45, to: <the exe's FileVersion> }` before `appLaunch`, `cdpConnected.browserVersion` equals `to`, there is no `browserVersionMismatch`, and `pageHello` arrives; on the rerun (A2) there is `browserVersion` with equal values and no change entry; with `garbage` (B) `previous` is `unknown` and there is no change entry.
- Given `127.0.0.1:9229` held by another listener, when OK is injected, then the log has `browserStartFailed { reason: portInUse }` and `commandFailed { errorCode: PortInUse }`, there is no `appLaunch` and no browser runs on the profile.
- Given an Edge already running on the test profile, when OK is injected, then the log has `ProfileInUse` and no `appLaunch`, and the set of browser processes on the profile is unchanged.
- Given remote debugging disabled in the test profile's `Local State`, when OK is injected, then the log has `appWindowAttached`, then (≥ `cdpConnectTimeout` after `appLaunch`) `cdpUnavailable` with `browserVersion` and `commandFailed { errorCode: CdpUnavailable }`, and no `processExited`. The Edge root is alive and its window is `IsIconic` and `IsWindowVisible`, the shell is foreground, and the log shows the mute of the Edge tree.
- Given `dotnet test` and `npm test`, then all tests pass; and `tools/check-browserhost-tracer.ps1` still passes (regression with `Browser.getVersion` in the setup).

## Implementation Notes

- Machine: planned on **UM760** (`MEDIASTATION`, board `F7BSI`, model `EliteMini Series`), Edge 156.0.4314.50, 2026-10-29.
- Implemented and verified on **UM760** (`MEDIASTATION`, `F7BSI`, `EliteMini Series`), 2026-10-29, baseline `4a7262f`, Edge 156.0.4314.50.
- Choices left open by the plan:
  - `browserVersionUnconfirmed.reason` is the `CdpException` message for an error reply and `unparsableProduct` for a missing or unparsable `product`; a `product` without `/` counts as unparsable.
  - A root that is still alive 5 s after `KillTree` in Stop gets `warn browser processKillTimedOut { appId, pid, timeoutSeconds }`, and Stop then completes as before.
  - `Version` values are logged normalised to four parts (`156.0.4314.050` → `156.0.4314.50`, `156.0.4314` → `156.0.4314.0`).
- Evidence (UM760):
  - `dotnet build MediaStation.slnx -c Release`: 0 warnings, 0 errors.
  - `dotnet test --solution MediaStation.slnx -c Release --no-build`: 1482 total, 1481 passed, 1 skipped (pre-existing DW-1 skip in `MediaStation.App.Tests`), 0 failed. `BrowserHostTests` + `BrowserVersionTests` + `BrowserPreflightTests` (173 tests) run 3× in a row, all green.
  - `npm test` in `web/`: 11 files, 123 tests passed.
  - `dotnet publish src/MediaStation.App -c Release -r win-x64 --self-contained`: exit 0.
  - `tools/check-browserhost-errors.ps1`: PASS twice (`logs\browserhost-errors-evidence-20261029-175016`, `…-175158`). Run 1:
    - A: `browserVersion{version 156.0.4314.50, previous 155.0.4283.45}` and `browserVersionChanged{from 155.0.4283.45, to 156.0.4314.50}` before `appLaunch`, `cdpConnected.browserVersion=156.0.4314.50`, `pageHello`, Edge covers the monitor (screenshot);
    - A2: `previous=156.0.4314.50`, no change entry;
    - B: `previous=unknown`, no change entry;
    - C: `browserStartFailed{portInUse, cdpPort 9229}`, `commandFailed{portInUse}`, no `appLaunch`, no profile process;
    - D: `browserStartFailed{profileInUse, profileDir}`, `commandFailed{profileInUse}`, the 9 profile PIDs unchanged;
    - E: `cdpUnavailable{notConnected, browserVersion 156.0.4314.50}` 15.000 s after `appLaunch`, `commandFailed{cdpUnavailable}`, no `processExited`, root alive, window IsIconic+IsWindowVisible, shell foreground, `audioRuleApplied{muted:true}` (screenshot).
  - `tools/check-browserhost-tracer.ps1`: PASS (`logs\browserhost-tracer-evidence-20261029-175135`), no new warn/error; the `netflix` run logged `browserVersion{156.0.4314.50, previous 156.0.4314.50}` and `cdpConnected.browserVersion=156.0.4314.50`.
- For the user's whole-epic acceptance on UM760: rerun `tools/check-browserhost-errors.ps1` after the next real Edge update. `browserVersionChanged` should appear on the `netflix` tile's first start.

- Review fixes, 2026-10-29 (UM760):
  - The smoke's test tile moved from `cdpPort` 9229 (the Node inspector default) to 9237. The suggested 9339 is outside the AD-11 range 9220–9239, so config validation would reject it.
  - The smoke now starts MediaStation with `__COMPAT_LAYER=DetectorsAppHealth`. A, A2 and B assert no `processExited` and a live root.
  - `errorCode` checks are case-sensitive, and E's timing tolerance is 0.5 s.
  - `LogBrowserVersion` returns the version, and `StartCoreAsync` assigns it to the session.
  - New tests cover the read-only lockfile case, `cdpUnavailable.browserVersion=unknown`, and the version change followed by `CdpUnavailable`.
  - Rerun of `tools/check-browserhost-errors.ps1`: PASS (`logs\browserhost-errors-evidence-20261029-180355`).

## Plan Change Log

## Review Triage Log

### 2026-10-29 — Review pass
- verdicts: 35 findings — high 0, medium 2, low 21, false 12, maybe-false 0
- findings:
  - `[low]` `[reject]` (blind-hunter) `ConfirmVersionAsync` also catches the `CdpException` of a closed session, so a socket drop during `Browser.getVersion` logs a spurious `browserVersionUnconfirmed` before `setupFailed` — real, but the window is one sub-millisecond CDP call, the end state (`CdpUnavailable`/`cdpAbandoned`) is unchanged, and `CdpException` does not tell an error reply from a closed session, so the fix adds a guard.
  - `[low]` `[reject]` (blind-hunter) Stop still posts `ProcessExited` when the root outlives the 5 s kill wait — the same post existed before this change without any wait; it needs `Kill(entireProcessTree)` on our own child to stall for 5 s, `processKillTimedOut` is logged, and the fix changes the Stop contract.
  - `[false]` `[reject]` (blind-hunter) A restart after a failed kill gives `profileInUse` that cannot be told from a foreign holder — `processKillTimedOut { appId, pid }` is logged right before it in the same Stop/Start sequence.
  - `[low]` `[reject]` (blind-hunter) The window-timeout/`launcherExited` branch kills without the bounded exit wait, so an immediate retry can see the dying lock — a retry needs AppMenu navigation (seconds), a killed tree is gone in well under a second, and `launcherExited` has no live root; the fix adds a wait to another branch.
  - `[low]` `[reject]` (blind-hunter) `IsProfileInUse` maps every `UnauthorizedAccessException` to in use (a read-only or ACL-denied lockfile blocks every start), with no HResult in the log — Chromium opens `lockfile` delete-on-close, so a stale read-only one needs a hand-copied profile; telling causes apart adds branches. The untested branch is patched under verification-gap.
  - `[medium]` `[patch]` (blind-hunter) The `__COMPAT_LAYER` strip has no automated evidence; the smoke removed the variable itself — same root cause as the verification-gap row: the smoke now starts MediaStation with `__COMPAT_LAYER=DetectorsAppHealth`, and A/A2/B assert no `processExited` and a live root. Mutation check: with the strip line removed, the smoke fails (`FAIL (5 problems)`, no `appWindowAttached` in A, A2, B, E).
  - `[low]` `[reject]` (blind-hunter) The live smoke proves only CdpUnavailable, not exit-during-connect or window timeout — the ticket's UM760 verify names Last Version, port/profile and CDP off only; those two paths are unchanged 6.2 code pinned by `Process_exit_while_connecting_…` and `No_window_within_20_seconds_…`; a live kill scenario adds harness code.
  - `[low]` `[patch]` (blind-hunter) `errorCode` checks are case-insensitive (`-ne`) — now `-cne` against the serialised `portInUse`, `profileInUse`, `cdpUnavailable`.
  - `[low]` `[patch]` (blind-hunter) Test port 9229 is the Node inspector default — moved to 9237 (9339 is outside the AD-11 range 9220–9239 that config validation enforces).
  - `[low]` `[reject]` (blind-hunter) Stop can take 8 s, unchecked against `stoppingTimeout` — 8 s only when the kill stalls; a normal Stop is at most 3 s plus the kill, the default `stoppingTimeout` is 45 s, and the fix adds a validation rule.
  - `[low]` `[patch]` (blind-hunter) `cdpUnavailable.browserVersion = "unknown"` untested — test `Cdp_unavailable_carries_unknown_version_when_the_exe_version_is_unreadable`.
  - `[low]` `[reject]` (blind-hunter) A throwing seam between registration and launch leaves a stale session — the production probes swallow every error and `app.Id` is validated kebab-case; `CommandFailed` puts the tile in `Failed` and the next Stop/Start clears the session; the fix adds a guard.
  - `[low]` `[patch]` (blind-hunter) `LogBrowserVersion` hides a session write — it now returns the `Version?` and `StartCoreAsync` assigns `session.BrowserVersion`.
  - `[low]` `[reject]` (edge-case-hunter) Spurious `browserVersionUnconfirmed` when the session closes during `getVersion` — same as the first blind-hunter row.
  - `[false]` `[reject]` (edge-case-hunter) `StopApp` during `StopSessionAsync(previous)` launches a browser after Stop answered — the Orchestrator never sends `StopApp` while the app is `Starting` (`Orchestrator.Stop.cs` `OnStop`); even then the new session is `Stopping`, `WaitForWindowAsync` returns at once and the tree is killed as `stoppedDuringStart`.
  - `[false]` `[reject]` (edge-case-hunter) Two quick restarts make the third start see its own old browser — the Orchestrator sends `StartApp` only from `NotRunning`/`Failed` and holds `Starting` until a result, so two starts of one app never overlap; a restart goes through `StopApp` → `CommandCompleted` first.
  - `[low]` `[reject]` (edge-case-hunter) The window-timeout branch kills without waiting — same as the blind-hunter row.
  - `[low]` `[reject]` (edge-case-hunter) Another process takes the profile/port between the check and the launch — a microsecond window on a dedicated machine; the fix re-runs the checks after a launcher exit (a new branch).
  - `[low]` `[reject]` (edge-case-hunter) Read-only or ACL-denied stale lockfile reads as in use — same as the blind-hunter row.
  - `[low]` `[reject]` (edge-case-hunter) Root alive after the kill wait still gets `ProcessExited` — same as the blind-hunter row.
  - `[low]` `[reject]` (edge-case-hunter) Smoke log lookup breaks on a daily log rollover mid-run — needs a run across UTC midnight; the run fails loudly, and the fix adds a multi-file search.
  - `[low]` `[patch]` (edge-case-hunter) Scenario E tolerance of 0.05 s is flaky against process-creation time — widened to 0.5 s (runs measured 14.997 s for a 15 s deadline).
  - `[medium]` `[patch]` (verification-gap) Nothing verifies that `ProcessBrowserLauncher` strips `__COMPAT_LAYER` — fixed with the blind-hunter row above (the smoke runs MediaStation with the variable; the mutation check fails the smoke).
  - `[low]` `[patch]` (verification-gap) `UnauthorizedAccessException` → in-use branch untested — test `Lockfile_that_refuses_write_access_means_the_profile_is_in_use` (read-only lockfile).
  - `[low]` `[patch]` (verification-gap) Smoke `errorCode` comparison is case-insensitive — fixed with the blind-hunter row (`-cne`).
  - `[false]` `[reject]` (intent-alignment) The log key is `browserVersionChanged`, not the spec's `BrowserVersionChanged` — the spec's PascalCase names the fact, as AD-13 does for `CdpUnavailable`, which 6.2 already logs as `cdpUnavailable`; every log key in the project is camelCase, and the PowerShell checks (`-eq`, `Select-String`) match either spelling.
  - `[false]` `[reject]` (intent-alignment) The Orchestrator `Starting` timeout is not exercised next to `CdpUnavailable` — `CdpUnavailable` is posted only after `AppWindowAttached`, which cancels the start timer (`Orchestrator.Attach.cs` L66; `OrchestratorAttachTests` asserts the `CancelTimer`), so the two cannot be confused; unchanged code.
  - `[false]` `[reject]` (intent-alignment) Timeout and `ProcessExited` share `ErrorCode.Unexpected` — AD-13 defines `CdpUnavailable`, `PortInUse`, `ProfileInUse` as codes and makes CdpUnavailable distinguishable from the start timeout (an Orchestrator timer) and from `ProcessExited` (an Event); the tests assert those event sequences.
  - `[false]` `[reject]` (intent-alignment) Mute/minimize are only shown by the Orchestrator test and the smoke — that is the AD-4 split (executors never mute on their own initiative); both surfaces are covered: `OrchestratorFailureTests` and scenario E (`audioRuleApplied{muted:true}`, `IsIconic`, `IsWindowVisible`).
  - `[low]` `[patch]` (intent-alignment) No test combines a changed `Last Version` with unavailable CDP — test `Version_change_is_logged_before_launch_even_when_cdp_then_never_answers`.
  - `[false]` `[reject]` (intent-alignment) No version entry when the preflight refuses the start — AD-13 reads the version "przed każdym startem przeglądarki"; a refused start launches nothing.
  - `[false]` `[reject]` (intent-alignment) The profile check is a lockfile probe, not a process scan — scenario D proves it against a real Edge on the profile (`profileInUse`, PIDs unchanged).
  - `[false]` `[reject]` (intent-alignment) A `getVersion` mismatch only warns — the "smoke nie przechodzi" rule belongs to `--smoke`, which the epic's Boundaries assign to epic-deploy-resilience.
  - `[false]` `[reject]` (intent-alignment) The UM760 evidence is only the plan's claim — re-run in this session on MEDIASTATION/F7BSI/EliteMini Series: errors smoke PASS (`browserhost-errors-evidence-20261029-175456`, `-180618`), tracer PASS, reconnect PASS.
  - `[false]` `[reject]` (intent-alignment) The Stop wait and the `__COMPAT_LAYER` strip are behaviour the intent does not name — both are needed for the intent's checks to hold: the restart preflight would see its own old browser, and an inherited compat layer makes the launched root exit (probe on UM760, mutation check).

## Design Notes

- The checks run after the previous session's stop, so a restart does not see its own old browser. Stop now waits for the root to exit, which makes that order safe.
- Lockfile instead of WMI command lines: a single open call, no process scan. Chromium holds `lockfile` (`GENERIC_WRITE`, share read, delete-on-close) for its whole life. Adoption (6.5) will need the process scan anyway, and it can reuse the check.
- `browserVersionChanged` is a log entry, not an Event (AD-13). It is written before the launch, which settles the spike's deferred "from==to after a crash" note.

## Verification

**Commands:**
- `dotnet build MediaStation.slnx -c Release`: expected 0 warnings and 0 errors.
- `dotnet test --solution MediaStation.slnx -c Release --no-build`: expected all to pass.
- `npm test` in `web/`: expected to pass.
- `dotnet publish src/MediaStation.App -c Release -r win-x64 --self-contained`: expected exit 0.
- `powershell -NoProfile -ExecutionPolicy Bypass -File tools/check-browserhost-errors.ps1`: expected exit 0, PASS for A, A2, B, C, D and E.
- `powershell -NoProfile -ExecutionPolicy Bypass -File tools/check-browserhost-tracer.ps1`: expected exit 0 (regression).

**Manual checks (if no CLI):**
- Screenshots: after A's start Edge covers the monitor; after E the shell is visible.

## Auto Run Result

Status: built (ran on **UM760**: `MEDIASTATION` / `F7BSI` / `EliteMini Series`, Edge 156.0.4314.50)

**Summary.**
- **Preflight:** before every launch, and after the previous session has stopped, BrowserHost checks whether the app's profile is held (Chromium `lockfile`) and whether its CDP port has a listener. A held profile gets `CommandFailed(ProfileInUse)` and a held port `CommandFailed(PortInUse)`, with nothing launched; when both are held the answer is `ProfileInUse`.
- **Version before launch:** it reads the exe's `FileVersion` and the profile's `Last Version` and logs `browserVersion`. When they differ it also logs `browserVersionChanged { from, to }`. Both entries come before `appLaunch`.
- **Version confirmation:** after CDP setup it confirms the version with `Browser.getVersion` and puts the result in `cdpConnected.browserVersion`. A different version gives a `browserVersionMismatch` warning and an unreadable answer `browserVersionUnconfirmed`; the start still completes. `cdpUnavailable` carries the version read before the launch.
- **Stop and launch:** Stop waits up to 5 s for the root to exit after the kill. The launcher removes `__COMPAT_LAYER` from Edge's environment; on this machine the inherited variable made Edge relaunch itself, which looked like an immediate exit.
- **Start errors:** the three outcomes CdpUnavailable, exit during the CDP connect, and window timeout are pinned by tests and shown live.

**Files changed.**
- `src/MediaStation.App/Hosts/Browser/BrowserHost.cs`: preflight checks, version read and log, `Browser.getVersion` confirmation, `browserVersion` on `cdpUnavailable` and `cdpConnected`, bounded exit wait in Stop, `FailStart` with an `ErrorCode`.
- `src/MediaStation.App/Hosts/Browser/BrowserVersion.cs` (new): pure parse (normalised to 4 parts), product parse, compare, format.
- `src/MediaStation.App/Hosts/Browser/BrowserPreflight.cs` (new): `IsProfileInUse` (lockfile), `IsPortInUse` (TCP listeners), `ReadFileVersion`.
- `src/MediaStation.App/Hosts/Browser/BrowserProcess.cs`: the launcher removes `__COMPAT_LAYER` from the child environment.
- `src/MediaStation.App/CompositionRoot.cs`: wires the three new seams.
- `tests/MediaStation.Core.Tests/BrowserHostTests.cs`, `BrowserVersionTests.cs`, `BrowserPreflightTests.cs`, csproj links: tests for:
  - every matrix row;
  - the parse cases;
  - log order;
  - preflight on real temp files and loopback sockets;
  - the Stop wait.
- `tools/check-browserhost-errors.ps1` (new): live smoke, scenarios A, A2, B, C, D and E, on the throwaway `browserhost-check` tile (port 9237).

**Review findings.** 35 findings: high 0, medium 2, low 21, false 12, maybe-false 0. No intent gap and no plan defect.
- **Patches (8 entries).**
  - Medium (1): the smoke never proved the `__COMPAT_LAYER` strip. MediaStation now starts with the variable, and a mutation run without the strip fails the smoke.
  - Low (7):
    - read-only lockfile test;
    - case-sensitive `errorCode` checks;
    - port 9229 → 9237;
    - test for `unknown` version on `cdpUnavailable`;
    - test for a version change followed by CDP off;
    - `LogBrowserVersion` returns the version;
    - E tolerance 0.5 s.
- **Deferred:** none.
- **Rejected, with reasons:**
  - spurious `browserVersionUnconfirmed` on a socket drop during `getVersion`, reported twice (low: sub-millisecond window, same end state, the fix adds a guard);
  - `ProcessExited` after a stalled kill, reported twice (low: the post predates this change, needs a 5 s kill stall, and the stall is logged);
  - restart after a failed kill indistinguishable from a foreign holder (false: `processKillTimedOut` comes before it);
  - no exit wait in the window-timeout branch, reported twice (low: a retry takes seconds, the fix adds a wait);
  - a read-only lockfile reads as in use, reported twice (low: needs a hand-copied profile);
  - the live smoke has no exit or timeout scenario (low: not in the ticket's UM760 verify, unchanged code with unit tests);
  - Stop can take up to 8 s against `stoppingTimeout` (low: only on a stalled kill, default 45 s);
  - stale session when a seam throws (low: the probes never throw);
  - `StopApp` during the previous-session wait, and two quick restarts (false: the Orchestrator never sends them while `Starting`);
  - race between the check and the launch (low: microsecond window);
  - log rollover mid-smoke (low: the run fails loudly);
  - intent-alignment, all false:
    - camelCase `browserVersionChanged`;
    - the Orchestrator start timer;
    - `Unexpected` for timeout and exit;
    - where mute and minimize are shown;
    - no version log on a refused start;
    - lockfile instead of a process scan;
    - a mismatch only warns;
    - evidence re-run in this session;
    - the Stop wait and the compat strip are needed behaviour.

**Follow-up review recommended:** false. This was the first pass. Patched counts: high 0, medium 1, low 7, so no high and fewer than two medium.

**Verification (UM760, 2026-10-29, after the patches).**
- `dotnet build MediaStation.slnx -c Release`: 0 errors (TreatWarningsAsErrors).
- `dotnet test --solution MediaStation.slnx -c Release --no-build`: 1485 total, 1484 passed, 1 skipped (pre-existing), 0 failed.
- `npm test` in `web/`: 11 files, 123 passed.
- `dotnet publish src/MediaStation.App -c Release -r win-x64 --self-contained`: exit 0.
- `tools/check-browserhost-errors.ps1`: exit 0, PASS, evidence in `%LOCALAPPDATA%\MediaStation\logs\browserhost-errors-evidence-20261029-180618`. The earlier pre-patch run `-175456` also passed.
  - A: `browserVersionChanged { from 155.0.4283.45, to 156.0.4314.50 }` before `appLaunch`, `cdpConnected.browserVersion = 156.0.4314.50`.
  - A2: same version, no change entry.
  - B: `previous: unknown`, no change entry.
  - C: `browserStartFailed { portInUse }`, `commandFailed { errorCode: portInUse }`, no launch.
  - D: `browserStartFailed { profileInUse }`, `commandFailed { errorCode: profileInUse }`, profile PIDs unchanged.
  - E: `cdpUnavailable { notConnected, browserVersion 156.0.4314.50 }` about 15 s after `appLaunch`, then `commandFailed { cdpUnavailable }`, with no `processExited`. The root is alive, its window is `IsIconic` and `IsWindowVisible`, the shell is foreground, and the Edge tree is muted (`audioRuleApplied { muted: true }`).
- Mutation check: with the `__COMPAT_LAYER` strip removed, the same smoke fails (exit 1, 5 problems). The source was then restored and republished.
- `tools/check-browserhost-tracer.ps1`: PASS (`-180722`); the Netflix tile logs `browserVersion` and confirms 156.0.4314.50.
- `tools/check-browserhost-reconnect.ps1`: PASS (`-175636`), a regression check for the Stop change.
- Not applicable: nothing; every check ran on UM760.

**Residual risks.**
- A real Edge update was not observed live; scenario A writes an old `Last Version` instead.
- The log key is camelCase `browserVersionChanged`, so a case-sensitive search for the spec's `BrowserVersionChanged` misses it.
- If the root survives the kill for more than 5 s, Stop still reports `ProcessExited`, and the next start then answers `ProfileInUse` (with `processKillTimedOut` logged before it).
- Whole-epic acceptance on UM760 (user, `fp-acceptance`): run `tools/check-browserhost-errors.ps1` and expect PASS for A to E. After the next real Edge update, the first Netflix start should log `browserVersionChanged`.
