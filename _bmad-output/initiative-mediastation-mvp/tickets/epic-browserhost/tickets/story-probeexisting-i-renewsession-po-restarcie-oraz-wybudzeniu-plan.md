---
title: 'Story 6.5: ProbeExisting i RenewSession po restarcie oraz wybudzeniu'
type: 'feature'
ticket: '5'
created: '2026-10-29'
status: done
baseline_revision: '6319f01ba974e4de084d665ed8d74df302989277'
route: 'full'
route_source: 'auto'
risk: 'high'
review: 'thorough'
review_source: 'pinned'
lenses_ran: [blind-hunter, edge-case-hunter, verification-gap, intent-alignment]
review_loop_iteration: 0
followup_review_recommended: false
context:
  - '{project-root}/_bmad-output/initiative-mediastation-mvp/tickets/epic-browserhost/tickets/story-binding-po-nawigacji-i-reconnect-cdp-plan.md'
warnings: [oversized]
deferred:
  - summary: >-
      A browser root that holds the app profile but has no browser window (for example Edge kept alive in background mode) is not adopted, so a later start fails with ProfileInUse, and Close or Restart cannot kill it because the host has no session.
    evidence: |-
      maybe-false. The probe logs `probeExisting {noWindow}` and registers nothing (BrowserHost.ProbeCoreAsync), and the 6.4 preflight then refuses every start while the lockfile is held. This existed before 6.5 (the 6.4 start path had no adoption at all). Whether a kiosk Edge on its own user-data-dir ever stays running without a window is not proven. To settle it on UM760: close the kiosk window by hand (Alt+F4) and check for a surviving msedge.exe root with --user-data-dir=<profile> and no --type=. If one survives, adopt it without a window (register the session, no AppWindowAttached) so Stop can kill the tree.
    location: >-
      src/MediaStation.App/Hosts/Browser/BrowserHost.cs (ProbeCoreAsync)
    severity: medium (unverified)
---

<intent-contract>

## Intent

**Problem:** `BrowserHost.Probe` and `BrowserHost.Renew` are silent no-ops (`BrowserHost.cs` L198–208). After a MediaStation restart, a live Edge on `profiles\<appId>` is never adopted, so the tile stays `NotRunning`. Selecting the tile then fails with `ProfileInUse`, and a restart from AppMenu cannot recover, because Stop has no session to kill. After a wake from sleep, nothing renews the CDP session or the binding (AD-10).

**Approach:**
- **Probe:** find the live browser root by its profile in the process command lines, report `AppWindowAttached` for its window without a second launch, then connect CDP on the app's port and install the binding, `Page.enable` and the init script. The adopted session then behaves like a started one: reconnect, Stop and Renew all work on it.
- **Renew:** drop the current CDP channel and run the existing 6.3 reconnect loop to the same target, so a failure ends in `CdpLost`.
- **Late facts:** a StartApp that races an in-flight probe waits for the probe and reuses the adopted session. Stale CDP traffic is ignored. Late Orchestrator facts are proven against the real Orchestrator, whose `transitionId` logic stays unchanged.

## Boundaries & Constraints

**Always:**
- **Probe match:** the root is a process whose command line has `--user-data-dir=<profiles\<appId>>` (the rules of `HarnessBrowser.CommandLineMatchesProfile`) and no ` --type=`. Roots are tried in order, and the first with a browser window (`_findWindow(pid)`) wins. No root, or no window, means silence: no Event, and a log entry only.
- **Shared matcher:** the matching moves to `BrowserLaunch` as pure helpers. `HarnessBrowser` keeps thin wrappers, so harness behaviour and tests stay unchanged. The new helper `BrowserLaunch.HasDebuggingPort(commandLine, cdpPort)` matches the exact `--remote-debugging-port=<port>` token.
- **Adoption order:** the adopted session is registered under `_gate` only when the app has no session; otherwise the probe stands down. Then `info browser appAdopted { appId, rootPid, hwnd, class }`, a post of `AppWindowAttached(appId, hwnd, rootPid)`, and `WatchExit`. The host never launches anything during a probe. The Orchestrator's adoption (`OnAdopt`: mute → minimize → ShowShell, AD-5) does the window and audio work.
- **CDP after adoption:**
  - **Assets:** the page assets (runtime.js and manifest) load through the same helper as Start.
  - **Connect:** CDP connects with `_connect(app.CdpPort, token)` under `cdpConnectTimeout`, linked with the session stop token, and only when the root's command line has the app's debugging port.
  - **Setup:** setup is `InstallAsync` only: no `Page.navigate`, no `Browser.getVersion` and no version read (AD-13: "nie przy adopcji").
  - **Success:** `info browser cdpConnected { appId, cdpPort, adopted: true }`, then the session becomes ready (it holds its init script) and gets `WatchCdp`, the same as after a start.
  - **Failure:** a missing or invalid asset, a port mismatch, a connect timeout or a setup error, while the session is not stopping and the root is alive. It gives `warn browser cdpUnavailable { appId, cdpPort, reason, adopted: true }` and one `CdpLost(appId)`. The session stays registered, so Close and Restart from AppMenu kill the orphan tree.
- **Probe log:** every probe ends with one `info browser probeExisting { appId, outcome, rootPid? }`. `outcome` is one of `adopted`, `notRunning`, `noWindow`, `sessionExists` or `unknownApp`.
- **Start racing a probe:** `StartCoreAsync` first awaits an in-flight probe of the same app; the probe task never faults. If the registered session is then an adopted session that is ready, has a current CDP channel and a live root, Start reuses it. It logs `info browser startReusedAdopted { appId, rootPid }` and posts `AppWindowAttached` (same hwnd and rootPid) and then `CommandCompleted`, with no preflight, no stop and no launch. In every other case it follows the 6.2/6.4 path.
- **Renew:** `Renew` → `Task.Run`, which never throws. It needs a registered, non-stopping, ready session that is not already reconnecting. It takes the current channel atomically (detach plus return), logs `info browser renewSession { appId, outcome: renewing }`, disposes the old channel and starts `ReconnectAsync` (6.3 backoff and `cdpReconnectTimeout`). Otherwise it only logs `renewSession` with the outcome `noSession`, `stopping`, `notReady`, `reconnecting` or `noCdp`. The closed old channel never starts a second loop, because `Detach` returns false.
- **Loop logs:** `cdpReconnected` and `cdpLost` gain `trigger` (`cdpClosed` or `renewSession`). `Session` gets `Reconnecting`, set before a loop starts and cleared when it ends.
- **Probe and Renew seams:** `Func<IReadOnlyList<KeyValuePair<int,string>>> commandLines` (production: `ProcessCommandLines.HarnessApps`, called off the Orchestrator thread) and `Func<int, IBrowserProcess?> openProcess`. In production, `openProcess` is `Process.GetProcessById` wrapped in `ProcessBrowserProcess`; it gives `null` on `ArgumentException`/`InvalidOperationException` or an exited process.
- **Code rules:** port methods never block or throw (AD-4). The host never shows, raises or minimizes a window (AD-14/15). Comments are in Polish. New pure code stays linked into `MediaStation.Core.Tests`.

**Never:**
- Orchestrator, WindowManager, AudioRouter, `ErrorCode` or contract changes, including `transitionId` logic.
- Killing or relaunching a live orphan during a probe.
- `Runtime.enable`, `Runtime.evaluate`, `Page.reload` or `Page.navigate` on adoption or renew.
- Picking a target other than `CdpConnector.ConnectAsync`'s first page target on adoption, or the stored endpoint on renew.
- Retrying a probe.
- DW-3/DW-4 or the envelope `id` (6.6).
- J4 restart wiring (6.7).
- Editing ticket files or `deferred-work.md`.
- Touching `profiles\netflix` in a script.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Adopt | root with the profile and port, window, CDP answers | `appAdopted`, `AppWindowAttached(hwnd, root)`, setup calls exactly `Runtime.addBinding`, `Page.enable`, `Page.addScriptToEvaluateOnNewDocument`, `cdpConnected{adopted}`, launcher never called | — |
| Nothing to adopt | no matching root / helper-only (`--type=`) / root without window | `probeExisting{notRunning|noWindow}`, nothing posted | — |
| Adopted, no CDP | port missing in the cmdline, or connect returns null within 15 s | `AppWindowAttached`, then `cdpUnavailable{adopted}` + one `CdpLost` at the deadline; session kept; a later Stop kills the tree | no `CdpLost` if stopped/exited first |
| Adopted then drop | socket of adopted session closes | 6.3 reconnect to the same endpoint (`trigger: cdpClosed`) | `CdpLost` after 30 s |
| Stop adopted | `StopApp` | CloseMainWindow → KillTree → `ProcessExited` + `CommandCompleted` | — |
| Start during probe | `StartApp` while the probe awaits CDP | Start waits; then `AppWindowAttached` (same hwnd/root) + `CommandCompleted`, launcher never called | adopted but not ready → 6.4 path |
| Renew healthy | ready session with current CDP | `renewSession{renewing}`, old channel disposed, new transport to the same endpoint gets the 3 setup calls, `cdpReconnected{trigger: renewSession}` | failure → `cdpLost{trigger: renewSession}` + `CdpLost` |
| Renew skipped | no session / stopping / start in progress / reconnecting | `renewSession{noSession|stopping|notReady|reconnecting}`, nothing sent or posted | — |
| Stale traffic after renew | old channel pushes `frameNavigated`/`hello` | ignored (no `pageHello`, nothing sent on the new channel) | — |
| Late facts (real Orchestrator) | host-posted adoption after LaunchApp of another app / after `SystemResumed`; Starting attach after Home | adoption → `Background`, only Mute+Minimize (no ShowShell while another app active), no ActivateWindow; old-transition attach → compensation, not Foreground | — |

</intent-contract>

## Code Map

- `src/MediaStation.App/Hosts/Browser/BrowserHost.cs`: the main change.
  - `Probe`/`Renew` (L198–208): replace them, and update the class summary (L41–57).
  - `StartCoreAsync` (L210–344): extract the page-asset load (L220–251, runtime + manifest + `BuildInitScript`) into a helper shared with the probe, and add the probe await and the adopted-session reuse before the `_sessions.Remove` (L257–263).
  - `ConnectCdpAsync` (L346–427): the template for the adoption connect. Reuse `CreateCdp`, `TryAttach`, `InstallAsync`, `WatchCdp`, and `CdpTargets.LoopbackWebSocket` for `Endpoint`.
  - `OnCdpClosed`/`ReconnectAsync` (L542–643): add the `trigger` parameter and the `Reconnecting` flag.
  - `WatchExit` (L884) and `StopSessionAsync` (L944): reuse them as they are for adopted sessions.
  - `Session` (L1042): add `Adopted`, `Ready` (with `InitScript`), `Hwnd`, `Reconnecting`, and an atomic `DetachCurrent()`.
- `src/MediaStation.App/Hosts/Browser/BrowserLaunch.cs` (linked into Core.Tests): add `CommandLineMatchesProfile`, `MatchingRoots` (moved from `HarnessBrowser.cs` L37–88) and `HasDebuggingPort`. `BuildArguments` already emits `--remote-debugging-port=<cdpPort>`.
- `src/MediaStation.App/Hosts/DevHarness/HarnessBrowser.cs`: turn the two moved methods into delegating wrappers. `DevHarnessTests` must stay green.
- `src/MediaStation.App/Hosts/Browser/BrowserProcess.cs`: add `BrowserPlatform.OpenProcess(int pid)`.
- `src/MediaStation.App/CompositionRoot.cs` `CreateWebHost` (L358–385): wire `ProcessCommandLines.HarnessApps` (`Platform/System/ProcessCommandLines.cs`, WMI) and `BrowserPlatform.OpenProcess`.
- Read-only, already tested: `src/MediaStation.Core/Orchestrator/Orchestrator.Attach.cs` (`OnAppWindowAttached`/`OnAdopt`), `Orchestrator.Startup.cs` (probes at start; `RenewSession` on wake only for web apps in Foreground/Background) and `Orchestrator.Failures.cs` `OnCdpLost` (L73: Background → Failed).
- `tests/MediaStation.Core.Tests/BrowserHostTests.cs`:
  - `Harness` (L2006): add the command-line and open-process seams, which default to no processes.
  - Replace `Probe_and_renew_are_silent` (L551) with the new behaviour; the empty-process case stays silent.
  - `FakeProcess`/`FakeTransport`/`Reconnects` are already there; `NeverBackScheduleMs` comes from 6.3.
  - Real-Orchestrator tests sit next to L1745 and use `OrchestratorTestSupport` (`Started`, `Foreground`, `Background`, `StateOf`, `TwoApps`).
- `tools/check-browserhost-errors.ps1`: the template for the new smoke. It has the throwaway tile `browserhost-check`/9237, the `apps.json` backup, `Start-Run`/`Stop-Run`, `Send-Ok`, `Get-ProfileProcesses`, `Stop-Profile`, `Remove-TestProfile`, the `IsIconic` checks and `Save-Evidence`. `tools/inject-power.ps1` L40–60 has the synthetic `WM_POWERBROADCAST`/`PBT_APMRESUMEAUTOMATIC` PostMessage. Copy the technique, not the script, because the script closes MediaStation.
- No job object ties Edge to MediaStation (grep: none), so `Stop-Process -Force` on MediaStation leaves Edge alive.

## Tasks & Acceptance

**Execution:**
- [x] `src/MediaStation.App/Hosts/Browser/BrowserLaunch.cs` + `HarnessBrowser.cs`: move the matcher, add `HasDebuggingPort`, and leave wrappers in the harness.
- [x] `src/MediaStation.App/Hosts/Browser/BrowserHost.cs`: probe, adoption connect, start/probe serialization with reuse, Renew, the `trigger` field and the `Session` fields, as specified in Always and the Matrix.
- [x] `src/MediaStation.App/Hosts/Browser/BrowserProcess.cs` + `CompositionRoot.cs`: `OpenProcess` and wiring.
- [x] `tests/MediaStation.Core.Tests/BrowserHostTests.cs` + new `tests/MediaStation.Core.Tests/BrowserProfileMatchTests.cs` (matcher), covering:
  - every Matrix row on fakes with `FakeTimeProvider`;
  - matcher cases: quoted and unquoted paths, case, trailing slash, `--type=` helper, a sibling profile prefix (`netflix2`), port token `9222` vs `92220`;
  - "no second process" (`Launcher.Starts` empty after probe + Start reuse);
  - renew attempt instants equal to 6.3's schedule, and the endpoint equal to the adopted one;
  - late facts fed into a real Orchestrator: (a) probe adoption → Background + Mute/Minimize/ShowShell and no ActivateWindow; (b) adoption after another app's LaunchApp → no ShowShell; (c) Start-during-probe → one Foreground with the adopted hwnd and the second `AppWindowAttached` ignored; (d) `SystemResumed` → `RenewSession` → host `renewSession` → a later `CdpLost` gives Failed; (e) a Starting attach after Home gets compensation.
- [x] `tools/check-browserhost-adopt.ps1`: a repeatable live smoke (see Verification).

**Acceptance Criteria:**
- **Adoption after a crash:** Given the published build, harness off, and the throwaway tile started (`pageHello`), when MediaStation is killed (`Stop-Process -Force`) and started again, then:
  - the profile's browser root PID set is unchanged: one root, and the same PID;
  - the new run logs `probeExisting{adopted}`, `appAdopted`, `cdpConnected{adopted:true}` and `audioRuleApplied{muted:true}` for that root, with no `appLaunch`;
  - the Edge window is `IsIconic` and `IsWindowVisible`;
  - the shell is foreground within 5 s.
- **Renew after a synthetic wake:** Given that adopted state, when a synthetic resume is posted, then the log has `systemResumed`, `renewSession{renewing}` and `cdpReconnected{trigger:renewSession}`, there is still one root, and the shell stays foreground.
- **The bridge works after renew:** Given the renewed session, when OK is injected on the tile and then F5 (or Ctrl+R) in the Edge window, then Edge is foreground and a new `pageHello` arrives from the renewed session.
- **Tests:** Given `dotnet test` and `npm test` in `web/`, then all tests pass, and the tracer, reconnect and errors smokes still pass.

## Implementation Notes

- Machine: ran on **UM760** (`MEDIASTATION`, board `F7BSI`, model `EliteMini Series`), 2026-10-29.
- **Probe.** `Probe` registers the probe task in `_probes` under `_gate` before it returns, so a `StartApp` dispatched after it always sees the probe. `ProbeCoreAsync` catches everything, which means the task never faults; an unexpected exception logs `error browser browserProbeFailed`. It first checks for an existing session (`sessionExists`, with no WMI scan), then goes through the command lines in order. A root is a line that matches the profile and has no ` --type=`. `_findWindow` runs first, and a root without a window sets `noWindow`. `_openProcess` runs next, and a root that vanished before it could be opened is skipped (the outcome stays `notRunning` if nothing else matched). `probeExisting` is logged in `finally`, so it is the last entry of the probe and comes after the adoption's `cdpConnected`/`cdpUnavailable`. `rootPid` is present only for `adopted`.
- **Adoption CDP failure timing.** A missing asset or a port mismatch fails at once, because there is nothing to wait for. A connect that returns nothing fails when the connect attempt ends. The production connector polls `/json/list` until the token expires, so in practice that is at the 15 s deadline (test: nothing at 14.9 s, `CdpLost` at 15 s). A failure after Stop or process exit logs only `info browser cdpAbandoned { appId, reason: stopped|processExited, adopted: true }`. The `cdpUnavailable` entry also carries `file`/`detail` when known.
- **Shared helpers.** The page-asset load is shared by Start and adoption (`LoadPageAssets`). The failure reasons and their order on Start are unchanged.
- **Start racing a probe.** `StartCoreAsync` awaits the probe and checks reuse right after the unknown-app check, before assets and the `_sessions.Remove`. Reuse needs an adopted, ready, non-stopping session with a current channel and a live root.
- **Ready sessions.** A started session becomes `Ready` just before `CommandCompleted`; an adopted one becomes `Ready` after `cdpConnected`.
- **Renew.** Outcomes are checked in the order `noSession`, `stopping`, `notReady`, `reconnecting`, `noCdp`. On `renewing`, it detaches the channel (`DetachCurrent`), sets `Reconnecting`, disposes the old channel, and takes the timestamp and deadline. Only then does it log `renewSession` and start the loop, so the backoff and the 30 s deadline count from the detach. `Reconnecting` is set by `OnCdpClosed` and Renew before the loop starts. On success it is cleared before `WatchCdp`, so a close of the new channel can start its own loop. On every other exit it is cleared in `finally`. The `stopping` outcome is reachable only in the instant between `MarkStopping` and `Unregister` on process exit, so no test can reach it deterministically. A port-method failure on Probe or Renew only logs `browserCommandFailed`; nothing is posted, since the Orchestrator does not correlate those commands.
- **Matcher.** `BrowserLaunch` gains `CommandLineMatchesProfile`/`MatchingRoots` (moved), `IsBrowserRoot`, `DebuggingPortArgument` and `HasDebuggingPort`. `HasDebuggingPort` needs the exact whitespace-delimited token, ignores quotes and ignores case in the flag name. `HarnessBrowser` delegates to them, and `DevHarnessTests` is unchanged and green.
- **OpenProcess.** `BrowserPlatform.OpenProcess` also returns `null` on `Win32Exception` (no access), besides `ArgumentException`/`InvalidOperationException`/exited.
- **Errors smoke, scenario D (`tools/check-browserhost-errors.ps1`).** D used to start the foreign Edge before MediaStation. Since 6.5 the startup probe adopts it (`portMismatch` → `CdpLost` → Failed), so OK gave no `commandFailed {profileInUse}` and D failed. D now starts the foreign Edge only after the probe has logged `probeExisting {notRunning}`, and it asserts that outcome. That keeps D on 6.4's start-path `ProfileInUse`. No other scenario changed.
- **Test harness.** `Harness.OrphanHwnd` is a `const`: a `static readonly` read in the outer class's static initializer gave 0 (circular type initialization).
- **Mutation checks** (done by the implementer, then reverted). Without the probe await in Start, both Start-during-probe tests fail. Without the `Reconnecting` guard in Renew, `Renew_during_a_reconnect_is_skipped_and_starts_no_second_loop` fails.

### Evidence (UM760, 2026-10-29)

- `dotnet build MediaStation.slnx -c Release`: 0 warnings, 0 errors.
- `dotnet test --solution MediaStation.slnx -c Release --no-build`: 1558 total, 1557 passed, 1 skipped (the pre-existing `PowerNotificationsHarnessTests` skip), 0 failed.
  - `BrowserHostTests`: 178 cases, five runs without a failure.
  - New `BrowserProfileMatchTests`: 36 cases.
  - The Matrix rows on fakes: Adopt, Nothing to adopt (6 cases), Adopted no CDP (port missing/`92220`/`9223`; never-answering connect at exactly 15 s; Stop/exit first), Adopted then drop (recover / `CdpLost` at 30 s), Stop adopted, Start during probe, Renew healthy (schedule 250/750 ms, same endpoint), Renew never back (17 attempts, `CdpLost` at 30 s), Renew skipped (`notReady`, `reconnecting`, `noCdp`, `noSession`), and stale traffic after renew.
  - Real-Orchestrator late facts: (a)–(e), plus adoption after `SystemResumed`.
- `npm test` in `web/`: 11 files, 123 tests passed.
- `dotnet publish src/MediaStation.App -c Release -r win-x64 --self-contained`: exit 0.
- `tools/check-browserhost-adopt.ps1`: PASS twice (`%LOCALAPPDATA%\MediaStation\logs\browserhost-adopt-evidence-20261029-183752`, `…-184222`). First run:
  - root 8148 was the only root before the kill, after the kill and after adoption;
  - the new run logged `appAdopted {rootPid 8148}`, `audioRuleApplied {8148, muted: true}`, `cdpConnected {adopted: true}` and `probeExisting {adopted, 8148}`, with no `appLaunch`;
  - the Edge window was IsIconic and IsWindowVisible, and the shell was foreground 45 ms after `appAdopted` was seen;
  - synthetic resume: `systemResumed {PBT_APMRESUMEAUTOMATIC}`, `renewSession {renewing}` and `cdpReconnected {attempts 1, elapsedMs 272, trigger renewSession}`, with one root and the shell foreground;
  - OK brought Edge to the foreground (hwnd 6030994, pid 8148), and F5 gave a new `pageHello` 0.6 s later;
  - no warn/error besides `appIconMissing`.
  - `after-adoption.png` (checked by eye) shows the shell on top with the tile "W tle · wstrzymane".
- Regression smokes:
  - `tools/check-browserhost-tracer.ps1`: PASS (`…\browserhost-tracer-evidence-20261029-183826`).
  - `tools/check-browserhost-reconnect.ps1`: PASS (`…-183838`).
  - `tools/check-browserhost-errors.ps1`: FAIL at D before the smoke change (`…-183919`, see the note above), then PASS after it (`…-184114`).

## Plan Change Log

## Review Triage Log

### 2026-10-29 — Review pass
- verdicts: 31 findings — high 0, medium 1, low 20, false 9, maybe-false 1
- findings:
  - `[false]` `[reject]` (blind-hunter) A Start that races a probe can run past the Orchestrator's `Starting` timeout. False: Start dispatched means the Orchestrator was NotRunning at LaunchApp, so the probe's `AppWindowAttached` lands in `Starting` and cancels the start timer (`Orchestrator.Attach.cs` `CancelStartTimeout`) before any long CDP wait.
  - `[low]` `[reject]` (blind-hunter) No test for a Start sent while the probe's CDP is pending and then fails. The path is real: the tile goes Failed, and the host still stops the orphan and launches a fresh browser whose attach is ignored. It needs both a CDP-dead orphan and a tile chosen inside the probe window (about 1 s for a port mismatch, up to 15 s for a hung CDP), and AppMenu Restart recovers it. Fixing it adds a branch that contradicts the contract's "every other case follows 6.2/6.4" rule. Kept as a residual risk.
  - `[false]` `[reject]` (blind-hunter) An unexpected exception after adoption leaves the tile without `CdpLost`. False: `CdpSession.SendAsync` maps disposal and socket errors to `CdpException` (`CdpSession.cs` L86, L106–109), and its only other throw is the AD-1 forbidden-method guard, which `InstallAsync` never hits.
  - `[low]` `[patch]` (blind-hunter) A failed probe (WMI throws) logs `probeExisting {notRunning}`. Fixed: the catch sets `outcome = "failed"`; new test `Failing_command_line_scan_logs_probe_failed_then_probe_existing_failed_and_posts_nothing`.
  - `[low]` `[reject]` (blind-hunter) `HasDebuggingPort` does not prove the CDP listener belongs to the root. This needs Edge to fail to bind its own port while a foreign process holds it, and the 6.4 preflight already refuses a start on a held port. An owner-PID check (`GetExtendedTcpTable`) adds a Win32 seam.
  - `[low]` `[reject]` (blind-hunter) Reuse sends the hwnd stored at probe time without looking it up again. Probe and Start are seconds apart, and the kiosk window does not change during the process's life (6.1). A fresh lookup plus fallback adds a branch.
  - `[low]` `[patch]` (blind-hunter) The profile matcher accepts a sub-path of the profile (`netflix\child`) and only a space as terminator. Fixed: a trailing `\`/`/` counts only before the end or whitespace, and any whitespace terminates; new negative and tab cases in `BrowserProfileMatchTests`, and `DevHarnessTests` stay green.
  - `[low]` `[reject]` (blind-hunter) Each probe runs its own WMI query. One query per web tile at startup, off the Orchestrator thread; with 1–2 web tiles this costs well under a second. Sharing a snapshot adds caching state.
  - `[low]` `[reject]` (blind-hunter) `_probes` keeps only the latest probe and is never cleared, and reuse is not limited to racing Starts. A second `ProbeExisting` for one app comes only from a second `OrchestratorStarted`, which production posts once. Outside the race, a StartApp only follows Stop (AppMenu), which removes the session first. A per-app chain adds state.
  - `[low]` `[reject]` (blind-hunter) The plan does not record the errors-smoke change, and its change and triage logs are empty. The fix edits this build's plan. The change is recorded in Implementation Notes, and this pass writes the triage log.
  - `[low]` `[reject]` (blind-hunter) The adopt smoke checks the tile's background state only by eye. The AC lists it as a manual check, and it was checked on both runs (`after-adoption.png`: "W tle · wstrzymane"). Asserting it from snapshots adds script code.
  - `[low]` `[reject]` (blind-hunter) The smoke's `-like` profile filter is looser than the matcher. The throwaway profile `browserhost-check` has no sibling profiles on the machine; tightening it means porting the matcher to PowerShell.
  - `[false]` `[reject]` (blind-hunter) The `RenewCore` final guard can leak a detached channel. False: `Ready` is `InitScript != null`, checked before `DetachCurrent`, and `app` is null only on the `session is null` branch, so the guard cannot be true with `old != null`. An unknown appId reported as `noSession` cannot happen, because the Orchestrator sends Renew only for configured web apps.
  - `[low]` `[reject]` (edge-case-hunter) Start racing a probe whose CDP fails leaves the Orchestrator Failed while the host launches an unmanaged browser. Same path as the blind-hunter row above: rare, recovered by AppMenu Restart, and the fix contradicts the contract. Residual risk.
  - `[low]` `[reject]` (edge-case-hunter) A second probe for the same app overwrites `_probes`. Same as the blind-hunter `_probes` row: unreachable in production.
  - `[low]` `[reject]` (edge-case-hunter) Start hangs forever if WMI stalls. The Orchestrator's `Starting` timeout (AD-14) fails the tile anyway, and WMI on UM760 returns in under a second (smoke runs). A wait timeout adds a constant and a branch.
  - `[maybe-false]` `[defer]` (edge-case-hunter) A live root with no window is never adopted, so starts fail with `ProfileInUse` and Close/Restart cannot kill it. This existed before 6.5 (the 6.4 path); whether kiosk Edge survives without a window is unproven. Added to `deferred` (medium, unverified) with the UM760 check that settles it.
  - `[low]` `[reject]` (edge-case-hunter) A PID can be reused between the WMI snapshot, `_findWindow` and `_openProcess`. The window is milliseconds, and the reused PID would also need a `Chrome_WidgetWin_1` window in its tree. A creation-time check adds WMI plumbing.
  - `[low]` `[reject]` (edge-case-hunter) The opened process handle leaks when a concurrent probe wins registration. Only reachable with two probes for one app, which production never sends.
  - `[low]` `[reject]` (edge-case-hunter) An exception after `DetachCurrent` leaves `Reconnecting` stuck. What can run there (`CdpSession.Dispose`, a CTS with a validated ≤600 s timeout, `Task.Run`) does not throw in practice; the same reasoning rejected 6.3's outer-catch `CdpLost`.
  - `[low]` `[patch]` (edge-case-hunter) A failed WMI scan logs `notRunning`. Same fix as the blind-hunter row.
  - `[low]` `[patch]` (edge-case-hunter) The matcher accepts nested profile paths. Same fix as the blind-hunter row.
  - `[low]` `[reject]` (edge-case-hunter) Claim: the Approach says a racing Start reuses the adopted session, but after a CDP failure it launches a second browser. The contract's Always rule states the "every other case → 6.2/6.4 path" exception; making the Approach line exact edits this build's plan.
  - `[low]` `[patch]` (verification-gap) No test covers Start on an adopted session that lost CDP (Ready, no channel). New test `Start_after_an_adopted_session_lost_cdp_stops_the_orphan_and_launches_instead_of_reusing`; it fails without `|| !session.HasChannel` (implementer mutation check).
  - `[medium]` `[patch]` (verification-gap) No test proves a successful reconnect clears `Reconnecting`, on which every later wake renew depends. New tests `Second_renew_after_a_successful_renew_renews_again` and `Renew_after_a_recovered_drop_renews_again`; both fail when the clear is deleted. The clear now runs just before the `cdpReconnected` log, so a renew issued right after that entry sees the flag cleared.
  - `[false]` `[reject]` (intent-alignment) The adoption gate is the profile (1a), not "CDP answers on cdpPort" (1b), so a foreign Edge at startup is adopted and then Failed instead of giving `ProfileInUse`. False as a defect: the ticket says "rozpoznaje żywy proces po profilu", AD-10's port condition is enforced before any CDP connect (`HasDebuggingPort`), and under 1b the orphan could never be killed from AppMenu. Profile-held-at-startup is covered by `Adopted_root_without_the_app_debugging_port_is_cdp_lost_and_stop_still_kills_its_tree`.
  - `[false]` `[reject]` (intent-alignment) `transitionId` is never asserted, and the old-transition test is a Start→Home race, not a restart or wake. `transitionId` is Orchestrator-internal (no contract field), so the tests assert what it causes. The wake case is `Adoption_after_a_wake_is_background_without_activation`, plus the existing `OrchestratorStartupTests` wake-during-Starting compensation.
  - `[false]` `[reject]` (intent-alignment) The verify line lists RenewSession in the restart scenario, but the smoke shows it only after a synthetic wake, and `appWindowAttached` only in the first run. AD-10 and `Orchestrator.Startup.cs` send RenewSession only on wake, and the smoke runs restart and wake in one sequence. The adopting run logs `appAdopted` for the posted `AppWindowAttached`, and the Orchestrator's mute and minimize of that root prove it was handled.
  - `[false]` `[reject]` (intent-alignment) "Jeden proces" is checked as one browser root, not one process. Edge always runs helper processes (`--type=`), so "one process" can only mean one browser instance: the same root PID before and after, and no launch.
  - `[false]` `[reject]` (intent-alignment) The UM760 evidence is not the user's whole-epic acceptance. Correct per `AGENTS.md`: this is current-machine evidence for the ticket, and the epic acceptance stays with the user (`fp-acceptance`).
  - `[false]` `[reject]` (intent-alignment) The diff makes its own decisions (adopt without CDP → Failed, race reuse, relaunch after a failed adoption). These are not defects: each derives from AD-13/AD-10 in the plan's Design Notes, and each has tests.

## Design Notes

- **Process scan, not lockfile.** The lockfile says only that the profile is held, not by whom. Adoption needs the root PID for `AppWindowAttached` and for later Mute/Stop (AD-15). The debugging-port check keeps the host off a foreign CDP listener: AD-10 asks for "przeglądarka z jej profilem na `cdpPort`".
- **No CDP means adopt and then fail.** AD-13 attaches a window without waiting for CDP, so a live browser without CDP is muted and minimized. Probe has no `CommandFailed`, and `CdpLost` is the AD-10 event for "CDP bez możliwości reconnectu". The tile turns `Failed`, and J4 Restart kills the orphan, because the session stays registered. Staying silent instead would make every start fail with `ProfileInUse` and could never be repaired.
- **Start waits for the probe.** Without that, a tile selected during the probe gets a fresh session that fails with `ProfileInUse`, and the orphan is never killed. Reusing the adopted session sends the same hwnd and root twice. The Orchestrator applies the first one (Starting → Foreground, or NotRunning → adoption) and ignores the second, so its state is the same in either order.
- **Renew = forced reconnect.** Reusing the 6.3 loop gives renew the same backoff, deadline, stale-session guards and `CdpLost` with no new timing code.

## Verification

**Commands:**
- `powershell -NoProfile -Command "$env:COMPUTERNAME; (Get-CimInstance Win32_BaseBoard).Product; (Get-CimInstance Win32_ComputerSystem).Model"`: record the machine (planned on UM760: `MEDIASTATION`/`F7BSI`/`EliteMini Series`).
- `dotnet build MediaStation.slnx -c Release`: 0 warnings, 0 errors.
- `dotnet test --solution MediaStation.slnx -c Release --no-build`: all pass (only the pre-existing skip).
- `npm test` in `web/`: passes.
- `dotnet publish src/MediaStation.App -c Release -r win-x64 --self-contained`: exit 0.
- `powershell -NoProfile -ExecutionPolicy Bypass -File tools/check-browserhost-adopt.ps1`: exit 0 means PASS.
  - Setup: the throwaway tile `browserhost-check` (port 9237, profile deleted before and after, `apps.json` restored).
  - Steps: start, OK, `pageHello`; Home; kill MediaStation; record the root PIDs; start again; assert the first AC; PostMessage a synthetic resume to the MediaStation window; assert the second AC; OK on the tile; F5; `pageHello`; close everything.
  - Evidence: screenshots after adoption and after renew, and `measurements.jsonl`, all under `logs\browserhost-adopt-evidence-<ts>`.
- Regression: `tools/check-browserhost-tracer.ps1`, `check-browserhost-reconnect.ps1` and `check-browserhost-errors.ps1` all PASS.

**Manual checks (if no CLI):**
- Screenshot after adoption: the shell is on top and the tile shows as running in the background.

## Auto Run Result

Status: built (ran on **UM760**: `MEDIASTATION` / `F7BSI` / `EliteMini Series`)

**Summary.**
- **Probe:** after a MediaStation restart, `ProbeExisting` finds the live browser root for the tile's profile in the process command lines (no ` --type=`) and needs a window. It reports `AppWindowAttached` without launching anything, and the Orchestrator's adoption mutes, minimizes and shows the shell (AD-5).
- **CDP on adoption:** the host then connects CDP on the app's port, only if the root was started with that port, and installs the binding, `Page.enable` and the init script, with no navigation and no version read. Without CDP it logs `cdpUnavailable {adopted}` and posts one `CdpLost`. The tile then goes Failed, and Close/Restart can kill the orphan, because the session stays registered.
- **Start racing a probe:** a StartApp that arrives during a probe waits for it and reuses a fully adopted session.
- **Renew:** `RenewSession` drops the current CDP channel and runs the 6.3 reconnect loop to the same target (`trigger: renewSession`). A failure ends in `CdpLost`.
- **Stale traffic:** traffic from a replaced channel is ignored.
- **Matcher:** the profile matcher moved from the dev harness to `BrowserLaunch`.

**Files changed.**
- `src/MediaStation.App/Hosts/Browser/BrowserHost.cs`: probe and adoption, adoption CDP, Start/probe serialization with reuse, Renew, `trigger` on the reconnect logs, new `Session` fields.
- `src/MediaStation.App/Hosts/Browser/BrowserLaunch.cs`: profile matcher (moved; it now rejects nested paths), `IsBrowserRoot`, `HasDebuggingPort`.
- `src/MediaStation.App/Hosts/DevHarness/HarnessBrowser.cs`: delegating wrappers.
- `src/MediaStation.App/Hosts/Browser/BrowserProcess.cs`: `BrowserPlatform.OpenProcess`.
- `src/MediaStation.App/CompositionRoot.cs`: wiring of the command-line scan (WMI) and `OpenProcess`.
- `tests/MediaStation.Core.Tests/BrowserHostTests.cs`: probe, adoption, renew and race tests, plus real-Orchestrator late-fact tests.
- `tests/MediaStation.Core.Tests/BrowserProfileMatchTests.cs` (new): matcher cases.
- `tools/check-browserhost-adopt.ps1` (new): live crash → adoption → synthetic wake → reload smoke.
- `tools/check-browserhost-errors.ps1`: scenario D now starts its foreign Edge after the startup probe, because a foreign Edge present at startup is adopted since 6.5.

**Review findings.** 31 in total: high 0, medium 1, low 20, false 9, maybe-false 1.
- **Patched (4 entries):**
  - medium (1): the `Reconnecting` flag after a successful reconnect is now tested, and the flag is cleared before the `cdpReconnected` log;
  - low (3): a failed probe logs `outcome: failed`; the matcher rejects nested profile paths and accepts any whitespace; a test covers Start after an adopted session lost CDP.
- **Deferred (1):** a live profile root without a window is not adopted, so starts give `ProfileInUse` and AppMenu cannot kill it (medium, unverified; existed before this change).
- **Rejected, with reasons (see the triage log):**
  - **Rare paths, fix would add branches (low):**
    - a Start that races a probe whose CDP fails, raised twice (residual risk below);
    - owner-PID proof for the CDP listener;
    - re-looking up the hwnd on reuse;
    - a WMI query per tile;
    - `_probes` overwrite, handle leak and PID reuse;
    - a WMI stall (covered by the `Starting` timeout);
    - `Reconnecting` stuck after an exception.
  - **Fix would edit this build's plan (low):** plan bookkeeping, and the Approach wording.
  - **Smoke-only (low):** the screenshot-only tile check and the loose `-like` filter.
  - **False:**
    - the `Starting` timeout overrun;
    - a non-`CdpException` after adoption;
    - the `RenewCore` guard;
    - the intent-alignment readings: profile gate, `transitionId`, RenewSession on restart, one process, UM760 acceptance, own decisions.

**Follow-up review recommended:** false. This was the first pass, with 1 medium and 0 high patched. Patched counts: high 0, medium 1, low 3.

**Verification (UM760, 2026-10-29, after the patches).**
- `dotnet build MediaStation.slnx -c Release`: 0 warnings, 0 errors.
- `dotnet test --solution MediaStation.slnx -c Release --no-build`: 1568 total, 1567 passed, 1 skipped (the existing `PowerNotificationsHarnessTests` skip), 0 failed. Before the patches: 1558/1557/1, and the BrowserHost and matcher classes passed 214/214.
- `npm test` in `web/`: 123 passed.
- `dotnet publish src/MediaStation.App -c Release -r win-x64 --self-contained`: exit 0.
- `tools/check-browserhost-adopt.ps1`: PASS before the patches (`%LOCALAPPDATA%\MediaStation\logs\browserhost-adopt-evidence-20261029-184438`) and after them (`…-185631`).
  - root 6480 survived the kill and was adopted with no `appLaunch` (`probeExisting {adopted}`, `appAdopted`, `cdpConnected {adopted: true}`);
  - the Edge window was IsIconic and IsWindowVisible, and the shell was foreground 16 ms later;
  - synthetic resume: `renewSession {renewing}`, then `cdpReconnected {attempts 1, trigger renewSession}`;
  - OK brought Edge to the foreground, and F5 gave a new `pageHello`;
  - `after-adoption.png`, checked by eye: the shell is on top and the tile reads "W tle · wstrzymane".
- Regression smokes (exit 0 before and after the patches):
  - `tools/check-browserhost-tracer.ps1`: PASS (`…-184504`, `-185648`);
  - `tools/check-browserhost-reconnect.ps1`: PASS (`…-184516`, `-185702`);
  - `tools/check-browserhost-errors.ps1`: PASS (`…-184557`, `-185742`).
- Matrix audit: every Matrix row has a passing test (see Implementation Notes).
- Not applicable: a real Modern Standby wake. The renew is driven by a synthetic `PBT_APMRESUMEAUTOMATIC`, the same signal path as a real wake.

**Residual risks.**
- **Race with a failed adoption:** a tile selected during a probe whose adoption then fails to get CDP goes Failed, while the host stops the orphan and launches a fresh, unmanaged browser. AppMenu Restart recovers it.
- **No-window orphan:** the deferred item above.
- **Real wake untested:** a real sleep/wake was not exercised.
- **Whole-epic acceptance on UM760** (user, `fp-acceptance`):
  - With a tile open, kill MediaStation and start it again. The same Edge root should be adopted, minimized and muted, with the shell on top, and the log should have `probeExisting {adopted}`.
  - After a real sleep and wake, the log should have `renewSession {renewing}` and `cdpReconnected {trigger: renewSession}`.
  - OK on the tile, then F5, should give a new `pageHello`.
  - Or run `tools/check-browserhost-adopt.ps1`.
