---
title: 'Story 6.3: Binding po nawigacji i reconnect CDP'
type: 'feature'
ticket: '3'
created: '2026-10-29'
status: done
baseline_revision: '9252f4575caa7310385b9dfde1f8ec2cd33e63d0'
route: 'full'
route_source: 'auto'
risk: 'high'
review: 'thorough'
review_source: 'pinned'
lenses_ran: [blind-hunter, edge-case-hunter, verification-gap, intent-alignment]
review_loop_iteration: 0
followup_review_recommended: false
context:
  - '{project-root}/_bmad-output/initiative-mediastation-mvp/tickets/epic-browserhost/tickets/story-tracer-browserhost-edge-kiosk-cdp-i-hello-plan.md'
warnings: [oversized]
deferred:
  - summary: >-
      A successful reconnect is never shown to give the page a working bridge: the document already loaded keeps its old PageRuntime, and no real-browser run proves it can call the binding re-added by the new CDP session (a real page sends no new `hello` after reconnect).
    evidence: |-
      maybe-false. Unit tests cover the recovery path only on FakeTransport (the quick-recovery test pushes a `hello` the real page would not send), and the live smoke reaches only the give-up path because dropping the socket of a live target needs elevation. Settle with an elevated run on UM760 that resets the host's CDP TCP connection (SetTcpEntry DELETE_TCB) while Netflix is open: expect `cdpReconnected`, then trigger a page message from the existing document (or a main-frame navigation) and look for `pageHello` / a `Runtime.bindingCalled` on the new session.
    location: >-
      src/MediaStation.App/Hosts/Browser/BrowserHost.cs (TryReconnectOnceAsync, InstallAsync)
    severity: medium (unverified)
  - summary: >-
      A main-frame navigation during a CDP outage leaves the new document without PageRuntime: the init script died with the old session, and reconnect neither reloads nor evaluates, so the bridge stays dead (no `hello`) until the next main-frame navigation, while the tile stays Foreground/Background.
    evidence: |-
      Real, not caused by this change: before 6.3 a drop lost binding and script for good. Recovery would need `Page.reload` or `Runtime.evaluate`, both excluded by AD-1 and this story's Never list, or a `hello` watchdog after `cdpReconnected` that turns a silent page into `CdpLost`/Failed. Netflix is a single-page app, so main-frame navigations are rare and the window can be long. Settle as a product decision: either a post-reconnect hello watchdog (needs a page message on the new session, see the item above) or an AD-1 amendment allowing one reload after reconnect.
    location: >-
      src/MediaStation.App/Hosts/Browser/BrowserHost.cs (ReconnectAsync, TryReconnectOnceAsync)
    severity: medium
---

<intent-contract>

## Intent

**Problem:** After the tracer (6.2) a dropped CDP socket on a live browser is only logged (`cdpClosed`): the page loses its binding and script for good, nothing reconnects, and the Orchestrator never learns the app is broken, so the tile stays `Foreground`/`Background` forever instead of `Failed` (AD-10, R8).

**Approach:** BrowserHost keeps the binding refresh on every main-frame navigation and adds a reconnect loop: when the CDP socket of a started session closes while the process lives and no Stop is under way, it reconnects with backoff to the **same page target** (same WebSocket URL), reinstalls binding, `Page.enable` and the init script on the new session, and if nothing works within `settings.cdpReconnectTimeout` (default 30 s) it posts `CdpLost(appId)`; the existing Orchestrator handler turns that into `Failed`, Home keeps working throughout.

## Boundaries & Constraints

**Always:**
- Reconnect starts only from a session whose start completed (after `CommandCompleted`); a close during setup keeps the 6.2 `CdpUnavailable` path.
- Reconnect target = the endpoint of the lost transport (`ws://127.0.0.1:<cdpPort>/devtools/page/<targetId>`), checked again with `CdpTargets.LoopbackWebSocket`; never another page target, never `/json/list` re-selection.
- Deadline `cdpReconnectTimeout` measured from the detected close on the host's `TimeProvider`; backoff between attempts 250 ms, 500 ms, 1 s, then 2 s cap; every attempt and every setup call bounded by the deadline token linked with the session stop token.
- Successful reconnect sends exactly `Runtime.addBinding`, `Page.enable`, `Page.addScriptToEvaluateOnNewDocument` (no `Page.navigate`, no `Runtime.enable`) on the new session, then watches that session the same way (repeatable reconnects).
- Main-frame `Page.frameNavigated` re-adds only the binding; the init script is registered once per CDP session (re-adding it per navigation would run PageRuntime twice per document).
- Events and replies of a replaced or closed CDP session are ignored: callbacks check the session is still current; a transport that arrives after the deadline, after Stop or after process exit is disposed without effect.
- No host→page queue: while disconnected the session has no CDP channel (`Cdp == null`), nothing is buffered and nothing is replayed after reconnect.
- `CdpLost` only when the process is alive and the session is not stopping; process exit during reconnect stops the loop silently (`ProcessExited` path from 6.2); Stop during reconnect cancels it, nothing is posted besides Stop's own results. After `CdpLost` the session stays registered so AppMenu Close/Restart stop the live tree.
- Log entries: `info browser cdpClosed { appId, timeoutSeconds }`, `info browser cdpReconnected { appId, attempts, elapsedMs }`, `warn browser cdpLost { appId, timeoutSeconds, attempts }`; the Orchestrator loop logs `info orchestrator cdpLostHandled { appId, state }` with the app state from the resulting snapshot (`failed`) or `null` when ignored.
- `cdpReconnectTimeout` gets the upper bound `MaxTimeoutSeconds` (600) in `ConfigValidation` (closes deferred F18 note for it).
- Port methods never block or throw (AD-4); host never shows/raises/minimizes windows (AD-14/15); comments in Polish; new pure code stays linked into `MediaStation.Core.Tests`.

**Never:** `Runtime.enable`, `Runtime.evaluate`, `Page.reload`, `Page.removeScriptToEvaluateOnNewDocument` or any CDP method outside AD-1; attaching to another target on reconnect; a heartbeat/ping; changes to Orchestrator state logic (`OnCdpLost` exists); host→page `action` command or its rejection reply (epic-netflix-dpad owns Actions → BrowserHost); envelope `id` / bundle build (6.6); ProbeExisting/RenewSession (6.5); version/`PortInUse`/`ProfileInUse` (6.4); editing ticket files.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Main-frame navigation | `Page.frameNavigated` without `parentId` | `Runtime.addBinding` re-sent on the current session, no script re-add; iframe ignored | send failure on a live session → `warn bindingRefreshFailed`; on a closed one → nothing |
| Drop, quick recovery | socket closes, 2nd attempt connects | `cdpClosed`, backoff 250 ms → 500 ms, new session gets the 3 setup calls, `cdpReconnected{attempts:2}`, no Event posted | — |
| Drop, never back | every attempt fails | at exactly `cdpReconnectTimeout` (30 s; nothing at 29.9 s): `warn cdpLost`, one `CdpLost(appId)` posted | session kept for Stop |
| Setup fails on reconnect | new transport, `addBinding` error reply | transport disposed, loop continues to the deadline | — |
| Late transport | attempt returns after the deadline / Stop / exit | transport disposed; no reinstall, no second `CdpLost` | — |
| Stale session traffic | old transport pushes `frameNavigated`, reply or `hello` after replacement | ignored: nothing sent on the new session, no `pageHello` from it | — |
| Process exits during reconnect | `ProcessExited` posted by watcher | loop ends, no `CdpLost` | — |
| Stop during reconnect | `StopApp` | loop cancelled; only `ProcessExited` + `CommandCompleted` | — |
| Home | app Foreground, CDP down | Orchestrator Home → Background/shell as usual; later `CdpLost` → `Failed`; Home after that still shows shell | — |

</intent-contract>

## Code Map

- `src/MediaStation.App/Hosts/Browser/BrowserHost.cs` -- main change. `ConnectCdpAsync` (L285–375) ends with the `cdp.Closed.ContinueWith` stub (L361–372) that becomes "watch session → reconnect". `OnCdpEvent`/`RefreshBindingAsync` (L377–416) must receive the `CdpSession` that raised the event and ignore it unless `ReferenceEquals(session.Cdp, cdp)`. Extract the three setup calls (L324–326) into one helper used by start and reconnect (start still adds `Page.navigate` with `session.StopToken`, unchanged). `Session` (L650) gets the transport endpoint; `WatchExit` (L522) already sets `Stopping` and calls `CancelStart()` (cancels `StopToken`) — reuse `StopToken` to end the loop. Constructor gains `TimeSpan cdpReconnectTimeout` and `Func<Uri, CancellationToken, Task<ICdpTransport?>> reconnect`.
- `src/MediaStation.App/Hosts/Browser/CdpSession.cs` -- `ICdpTransport` gains `Uri Endpoint { get; }`; `WebSocketCdpTransport` stores the uri it connected to; `CdpConnector` gets `ReconnectAsync(Uri endpoint, int cdpPort, CancellationToken)`-style single attempt (re-check `CdpTargets.LoopbackWebSocket`, `WebSocketCdpTransport.ConnectAsync`, any `WebSocketException`/`IOException`/`HttpRequestException`/cancel → `null`). Backoff lives in BrowserHost (testable with `FakeTimeProvider`), not in the connector.
- `src/MediaStation.App/Hosts/Browser/CdpTargets.cs` -- `LoopbackWebSocket` reused, unchanged.
- `src/MediaStation.App/CompositionRoot.cs` `CreateWebHost` (L358–380) -- pass `TimeSpan.FromSeconds(config.Settings.CdpReconnectTimeout)` and the reconnect delegate.
- `src/MediaStation.App/Orchestrator/OrchestratorLoop.cs` `Run` (L117–205) -- add the `cdpLostHandled` log next to the `commandFailed` one; state from `result.Snapshot?.Apps` (enum logs camelCase via `JsonLinesLog`).
- `src/MediaStation.Core/Config/ConfigValidation.cs` `CheckSettings` (~L535) -- `CheckAtMost(settings.CdpReconnectTimeout, MaxTimeoutSeconds, "cdpReconnectTimeout", errors)`; test beside the existing at-most cases in `tests/MediaStation.Core.Tests/ConfigValidationTests.cs`.
- `src/MediaStation.Core/Orchestrator/Orchestrator.Failures.cs` `OnCdpLost` (L73) -- read-only: already `Starting|Foreground|Background → Failed` with `processAlive: true` (mute + minimize when foreground, shell, focusRequest).
- `tests/MediaStation.Core.Tests/BrowserHostTests.cs` -- `Harness` (L967) builds the host; extend with a reconnect seam that hands out queued `FakeTransport`s (or `null`), count/endpoint of attempts; `FakeTransport` (L1138) gets `Endpoint`. Existing 82 tests must stay green. `OrchestratorTestSupport` (`Started`, `Foreground`, `StateOf`) is available for the Home test; `OrchestratorLoopTests` has the loop harness (`CreateLoop`) for the log test.
- `tools/check-browserhost-tracer.ps1` -- template for the new smoke (apps.json backup/restore, publish exe, harness off, OK/Home via `keybd_event`, foreground/iconic checks, log reading, cleanup).

## Tasks & Acceptance

**Execution:**
- [x] `src/MediaStation.App/Hosts/Browser/CdpSession.cs` -- `ICdpTransport.Endpoint`, `WebSocketCdpTransport` endpoint, `CdpConnector.ReconnectAsync` single attempt -- reconnect to the same target.
- [x] `src/MediaStation.App/Hosts/Browser/BrowserHost.cs` -- reconnect loop, current-session guards, shared setup helper, logs and `CdpLost` per Always/Matrix; update class summary (6.3 owns reconnect).
- [x] `src/MediaStation.App/CompositionRoot.cs` -- wiring.
- [x] `src/MediaStation.App/Orchestrator/OrchestratorLoop.cs` -- `cdpLostHandled` log.
- [x] `src/MediaStation.Core/Config/ConfigValidation.cs` + `ConfigValidationTests.cs` -- upper bound for `cdpReconnectTimeout`.
- [x] `tests/MediaStation.Core.Tests/BrowserHostTests.cs` -- every Matrix row on fakes with `FakeTimeProvider` (advance time explicitly, assert backoff instants and the exact 30 s limit with default-like settings), exact method list on the new transport, no `Page.navigate` on reconnect, two successive drops both recover, reconnect endpoint equals the lost one, `ReconnectAsync` rejects a non-loopback endpoint and returns `null` on an unused loopback port; Home test: host-posted `CdpLost` fed into a real Orchestrator (`OrchestratorTestSupport`) — Home during the outage gives Background + shell, `CdpLost` gives `Failed`, a later Home still gives `ShowShell`.
- [x] `tests/MediaStation.Core.Tests/OrchestratorLoopTests.cs` -- `cdpLostHandled` with `state: Failed` (and `null` when ignored).
- [x] `tools/check-browserhost-reconnect.ps1` -- repeatable real-machine smoke (see Verification).

**Acceptance Criteria:**
- Given a published build with harness off and the Netflix tile started (`pageHello` logged), when the smoke closes the host's page target while keeping Edge alive (new target first), then the log has `cdpClosed`, and after 30 s (±2 s) `cdpLost` and `cdpLostHandled` with `state: failed`, no `cdpReconnected`, and the shell is foreground with the tile shown as failed on the screenshot.
- Given that outage, when Home is injected before the limit and again after it, then each time the MediaStation shell is foreground within 2 s.
- Given `dotnet test`, then the new BrowserHost, loop and config tests and all existing tests pass; `npm test` in `web/` still passes.

## Implementation Notes

- Machine: ran on **UM760** (`MEDIASTATION`, board `F7BSI`, model `EliteMini Series`), 2026-10-29.
- Backoff is a wait **before** each attempt (250 ms, 500 ms, 1 s, then 2 s), counted from the detected close for the first attempt and from the previous attempt's start after that. This follows the Matrix row "backoff 250 ms -> 500 ms" for a 2nd-attempt success. Attempt instants are 0.25, 0.75, 1.75, 3.75, 5.75 ... 29.75 s, so a target that never comes back gets 17 attempts before `cdpLost` at 30 s. Counting from the attempt start keeps the instants pinned when an attempt is slow; a slow attempt is simply followed at once by the next one.
- Every attempt is bounded by the deadline token linked with the session stop token. The attempt task is awaited with `WaitAsync(token)`, so `CdpLost` is posted at the deadline even when a connector ignores its token. A transport that arrives later is disposed by a continuation.
- `Session` holds `Endpoint`, set once at the first connect through `CdpTargets.LoopbackWebSocket(transport.Endpoint, app.CdpPort)`. That check uses the app's port, so an endpoint outside it is never reconnected and the loop ends in `CdpLost` (test `Target_outside_the_app_loopback_port_...`). The constructor keeps the plan's `Func<Uri, CancellationToken, ...>` seam. `CompositionRoot` passes `CdpConnector.ReconnectAsync(endpoint, endpoint.Port, token)`, and the connector checks loopback, `ws` and no userinfo again.
- `Session.Cdp` is now changed only under a lock (`TryAttach`, `Detach`, `MarkStopping`, `IsCurrent`). Stop and process exit take the current channel atomically with setting `Stopping`, so a reconnect that finishes during Stop cannot leave a live channel behind. On reconnect the new session is attached **before** the setup calls, as at start, so a main-frame navigation during setup still refreshes the binding. If setup fails, the session is detached and disposed and the loop goes on. While disconnected, `Cdp == null`.
- `OnCdpEvent` and `RefreshBindingAsync` take the `CdpSession` that raised the event and act only when it is current. A binding refresh cut off by a closing socket is silent; only a failure on a live, current session gives `warn bindingRefreshFailed`.
- `CommandCompleted` is posted before the close watcher is attached, so reconnect only ever follows a completed start. A socket closing during setup keeps the 6.2 `CdpUnavailable`/`setupFailed` path (test `Socket_closing_during_setup_...`).
- `CdpLost` is posted only when the deadline expired, the session is not stopping, the stop token is not cancelled and the process has not exited.
- Extra log entry: `error browser cdpReconnectFailed` (unexpected exception in the loop; never seen in tests or smokes).
- `OrchestratorLoop` logs `cdpLostHandled { appId, state }` for every `CdpLost`: `state` is `AppLifecycleState` from the result snapshot (`failed` in JSON), or `null` when the snapshot is null (ignored).
- Deferred F18 note for `cdpReconnectTimeout` is closed: `CheckAtMost(..., MaxTimeoutSeconds)` (600). It is covered by the `Timeout_of_600_seconds_is_accepted` and `Timeout_above_600_seconds_is_rejected` cases.
- Stale-session test (review follow-up): `FakeTransport.DeliverAfterDispose` keeps a disposed session's receive loop delivering. A reconnect transport whose setup failed is replaced by the next one, and its main-frame `Page.frameNavigated` and `hello` still reach `OnCdpEvent`. The test fails with the `IsCurrent` guard removed (the stale `hello` is logged as `pageHello`).
- Review follow-up: `OnCdpClosed` disposes the session right after a successful `Detach`, before the `Stopping` check, so a Stop racing in between cannot leak it. The unused `Session.Cdp` getter is removed. New tests cover: a held setup reply on a reconnected transport (bounded by the deadline, transport disposed, `CdpLost` at exactly 30 s); a faulting reconnect seam (counted as a failed attempt, no `error` log); a late transport after process exit; and setup failure on each of the three setup methods.
- Smoke screenshots: `tools/check-browserhost-reconnect.ps1` calls `SetProcessDPIAware()`. Without it, Windows PowerShell on the scaled 4K TV output captures only the top-left quarter. The tracer script has the same limitation; it was left unchanged.

### Evidence (UM760, 2026-10-29)

- `dotnet build MediaStation.slnx -c Release`: 0 warnings, 0 errors.
- `dotnet test --solution MediaStation.slnx -c Release --no-build`: 1414 total, 1413 passed, 1 skipped (pre-existing `PowerNotificationsHarnessTests` skip), 0 failed. `BrowserHostTests` 105/105, five repeated runs without failure (after the review follow-up 110/110, three runs) (23 new: every Matrix row, exact 30 s limit with nothing at 29.9 s, attempt instants, exact method list on the new transport, two successive drops, same endpoint, connector rejection, unused port and a real loopback WebSocket round trip, Home + `CdpLost` on a real Orchestrator). `OrchestratorLoopTests` +2 (`cdpLostHandled` `Failed` / `null`), `ConfigValidationTests` +2 cases.
- `npm test` in `web/`: 11 files, 123 tests passed.
- `dotnet publish src/MediaStation.App -c Release -r win-x64 --self-contained`: exit 0.
- `tools/check-browserhost-reconnect.ps1`: PASS twice (`logsrowserhost-reconnect-evidence-20261029-163329`, `-163437`). Second run:
  - second target opened via `PUT /json/new`, host target closed;
  - `cdpClosed {timeoutSeconds: 30}` 0.6 s later;
  - Home 10 s after the close: shell foreground in 81 ms, tile shows "Wstrzymane / W tle";
  - `cdpLost {attempts: 17}` **30.01 s** after `cdpClosed`, `cdpLostHandled {state: failed}`, no `cdpReconnected`, no `processExited`, Edge root alive;
  - `after-limit.png` shows the shell on top with the Netflix tile "Nie odpowiada / Coś się zacięło. Wciśnij OK" (failed);
  - Home after the limit: shell foreground in 77 ms;
  - no other warn/error besides `appIconMissing`.
- `tools/check-browserhost-tracer.ps1` (regression): PASS (`logsrowserhost-tracer-evidence-20261029-163526`).
- Not applicable: the live recovery path. Dropping the TCP socket of a live target needs elevation (Design Notes), so it is covered by the fakes and the connector test on a real loopback socket. The physical remote stays in the user's whole-epic acceptance on UM760.

## Plan Change Log

## Review Triage Log

### 2026-10-29 — Review pass
- verdicts: 29 findings — high 0, medium 5, low 14, false 8, maybe-false 2
- findings:
  - `[low]` `[reject]` (blind-hunter) Evidence paths in Implementation Notes lost `\b` (`logs<BS>rowserhost-…`) — real, but the fix edits this build's plan; the correct folders are listed in Auto Run Result (`logs\browserhost-reconnect-evidence-…`, `logs\browserhost-tracer-evidence-…`).
  - `[false]` `[reject]` (blind-hunter) Reconnect port check is tautological in production — `Session.Endpoint` is set only through `CdpTargets.LoopbackWebSocket(…, app.CdpPort)` at first connect, so the stored endpoint always has the app's port; the `CompositionRoot` comment claims only a repeated loopback check, which is what happens.
  - `[low]` `[reject]` (blind-hunter) `attempts` counts sleeps when `Endpoint` is null — unreachable in production (`CdpConnector.ConnectAsync` returns only loopback endpoints of the app port); a fix adds a branch.
  - `[low]` `[patch]` (blind-hunter) `OnCdpClosed` leaks the closed session when Stop runs between `Detach` and the `Stopping` check — `cdp.Dispose()` moved right after a successful `Detach`.
  - `[low]` `[reject]` (blind-hunter) Failed reconnect attempts leave no reason in the log; connector doc says "never an exception" yet throws `ArgumentNullException` — diagnostics only, `ConnectSafelyAsync` catches everything at the host; a null endpoint is a programming error that should fail loudly; a reason field adds plumbing.
  - `[maybe-false]` `[defer]` (blind-hunter) A successful reconnect is not proven to give the loaded page a working bridge — needs an elevated socket reset on real Edge; deferred (medium, unverified).
  - `[medium]` `[patch]` (blind-hunter) Stale-session test never reaches the `IsCurrent` guard — `FakeTransport.DeliverAfterDispose`; the test now dispatches stale `frameNavigated`/`hello` through a replaced session and fails with the guard removed.
  - `[false]` `[reject]` (blind-hunter) Smoke picks the target by count, so it never proves it closed the host's target — the script aborts unless exactly one page target exists and the host is connected to a page target (`cdpConnected`), so that one target is the host's; `cdpClosed` follows the close.
  - `[low]` `[reject]` (blind-hunter) Smoke does not set `cdpReconnectTimeout` in `settings.json`; parameter ranges allow Home after the limit — default run is correct, a custom setting fails loudly with the expected/actual value; guards add code for a misuse nobody makes.
  - `[low]` `[reject]` (blind-hunter) Smoke covers only Background → Failed on hardware — the Foreground → Failed branch is unit-tested (`Cdp_lost_while_foreground_fails_the_tile_and_shows_the_shell`, `OrchestratorFailureTests`) and its window/audio commands are hardware-proven by the Home flow; the AC requires Home before the limit; a second scenario adds a full run.
  - `[low]` `[patch]` (blind-hunter) `MaxTimeoutSeconds` doc names only starting/stopping timeouts — doc now names `cdpReconnectTimeout`. The epic-Notes deferred line is a ticket file (out of scope), flagged in the handoff.
  - `[false]` `[reject]` (blind-hunter) Plan Change Log/Triage Log empty, wait before first attempt deviates — the change log records only bad_plan loopbacks, this pass writes the triage log, and the Matrix row "backoff 250 ms → 500 ms" for a 2nd-attempt success specifies the wait before the first attempt.
  - `[low]` `[patch]` (blind-hunter) Late transport after process exit and setup failure on `Page.enable`/`addScriptToEvaluateOnNewDocument` untested — test `Late_transport_after_process_exit_is_disposed_and_posts_nothing_more`; setup-failure test is a Theory over all three methods.
  - `[low]` `[reject]` (blind-hunter) Browser exit logs a spurious `cdpClosed` before `ProcessExited` — one info line, the loop ends silently on exit with no `CdpLost`; a `HasExited` check is itself racy and adds a guard.
  - `[low]` `[patch]` (edge-case-hunter) Detach/Stopping race leaks the closed session — same fix as the blind-hunter row.
  - `[low]` `[reject]` (edge-case-hunter) `-HomeBeforeLimitSeconds ≥ -ReconnectTimeoutSeconds` gives a false FAIL — parameter misuse only, the default run is correct; a guard adds code.
  - `[low]` `[reject]` (edge-case-hunter) Under pwsh 7 `ConvertFrom-Json` turns `ts` into `DateTime` and loses ms — the documented invocation is Windows PowerShell (`powershell -NoProfile …`), where `ts` stays a string.
  - `[medium]` `[patch]` (edge-case-hunter) Stale-session test does not pin the Matrix row — same fix as the blind-hunter row.
  - `[low]` `[patch]` (edge-case-hunter) `Session.Cdp` getter has no reader — deleted; contract kept in the `_cdp` field comment.
  - `[medium]` `[patch]` (verification-gap) Held setup reply on a reconnected transport not tested against the deadline — test `Setup_reply_held_on_a_reconnected_transport_is_bounded_by_the_deadline` (nothing at 29.9 s, one `CdpLost` at 30 s, held transport disposed).
  - `[medium]` `[patch]` (verification-gap) Throwing/faulting reconnect seam not tested to keep retrying — test `Faulting_reconnect_seam_is_a_failed_attempt_and_ends_in_cdp_lost` (attempts continue, `CdpLost` at 30 s, no error log).
  - `[medium]` `[patch]` (verification-gap) `Stale_session_traffic_after_replacement_is_ignored` checks nothing — same fix as the blind-hunter row.
  - `[false]` `[reject]` (intent-alignment) Ticket literally says re-add binding **and script** per navigation; the diff re-adds only the binding — AD-1 (cited in References) names only `Runtime.addBinding` as repeated per navigation; script registrations live per CDP session, so re-adding per navigation would run PageRuntime twice; both are re-installed per new session.
  - `[maybe-false]` `[defer]` (intent-alignment) Recovery path never runs against real Edge — same root as the deferred blind-hunter row (no elevation for a socket reset).
  - `[false]` `[reject]` (intent-alignment) D-pad clause is satisfied only structurally — no action path to BrowserHost exists (epic-netflix-dpad owns it per epic Notes 2026-10-21), so nothing can be buffered; no queue exists and reconnect replays nothing (exact-method-list test).
  - `[false]` `[reject]` (intent-alignment) "Late replies" may mean envelope `id` (AD-7) — no host→page message with an `id` exists yet; ticket 6.6 owns "kopertę v1 z `id`" and "ignorowanie starego id"; this ticket's verify sits on transport loss and retries, which the CDP-level stale-traffic tests cover.
  - `[false]` `[reject]` (intent-alignment) Home is not tested inside a running loop with an active reconnect — port methods return at once, reconnect runs on `Task.Run`; the real-machine smoke shows Home → shell in 72–86 ms during the outage and after `Failed`.
  - `[false]` `[reject]` (intent-alignment) 30 s from settings to host is covered only by the smoke — `CompositionRoot` is WPF composition outside unit tests; the smoke asserts `timeoutSeconds: 30` from default settings and `cdpLost` at 30.01 s.
  - `[low]` `[reject]` (intent-alignment) `covers: R3` does not match EXPERIENCE R3 — ticket metadata; fixing it edits a ticket file, outside this story.

### 2026-10-29 — Review pass (re-dispatched, ran on UM760)
- verdicts: 32 findings — high 0, medium 4, low 18, false 9, maybe-false 1 (9 carried from the previous pass)
- findings:
  - `[low]` `[reject]` (blind-hunter) One hung reconnect attempt uses up the whole 30 s window (no per-attempt timeout) — real only when the loopback handshake or a setup reply stalls without failing, i.e. a wedged browser that a retry would not fix either; a refused/closed target fails at once; a per-attempt timeout adds a new constant and token plumbing. The plan's Always bounds attempts by the deadline only.
  - `[low]` `[reject]` (blind-hunter) `cdpReconnectFailed` catch leaves the tile stuck without `CdpLost` — the catch is reachable only by an unexpected exception from logging/posting/`ContinueWith` (the deadline CTS is created from a validated ≤600 s value); posting `CdpLost` from the catch adds a guarded branch for a path never seen.
  - `[low]` `[reject]` (blind-hunter) Final `CdpLost` check is not atomic with Stop — the window is between a volatile read and `_post`; the Orchestrator is already `Stopping` before the host's Stop runs and `OnCdpLost` ignores `Stopping` (`Orchestrator.Failures.cs`), so a racing `CdpLost` has no effect; a locked decision method adds surface.
  - `[medium]` `[defer]` (blind-hunter) Navigation during the outage leaves a page without PageRuntime that is reported as healthy, and it was tracked only as a residual risk — verified; pre-existing (6.2 lost everything on a drop) and the fix needs `Page.reload`/`Runtime.evaluate` (excluded by AD-1) or a hello watchdog; added to `deferred`.
  - `[low]` `[patch]` (blind-hunter) Stop while a reconnected transport holds its setup reply is untested — test `Stop_while_a_reconnected_transport_holds_its_setup_reply_leaves_no_live_channel` (only `ProcessExited`+`CommandCompleted`, transport disposed, no `cdpReconnected`/`cdpLost`/error).
  - `[medium]` `[patch]` (blind-hunter) Navigation during reconnect setup refreshing the binding is untested — same entry as the verification-gap row below.
  - `[low]` `[patch]` (blind-hunter) "Same endpoint" assertions cannot fail because every FakeTransport has the same endpoint — reconnect transports in `Drop_with_quick_recovery_…` and `Two_successive_drops_…` now carry a different target id (`OtherTarget`).
  - `[low]` `[reject]` carried (blind-hunter) `attempts` counts sleeps when `Endpoint` is null (foreign-port test logs 17) — code unchanged; unreachable in production.
  - `[low]` `[patch]` (blind-hunter) Faulting-seam test asserts only `ReconnectAttempts > 1` — now asserts the 17 attempt instants (shared `NeverBackScheduleMs`), exactly 3 posts and one `cdpLost`.
  - `[false]` `[reject]` carried (blind-hunter) Connector port re-check is tautological — code unchanged; the stored endpoint is checked against `app.CdpPort` when first stored.
  - `[low]` `[reject]` (blind-hunter) Smoke does not assert the failed tile on the screenshot — the state is asserted from `cdpLostHandled {state: failed}` and the screenshot is the plan's manual check, done by eye this pass on both runs; image assertions add code.
  - `[low]` `[reject]` (blind-hunter) Harness leaks dequeued reconnect transports — parked receive loops end with the test process; tracking adds fixture code with no user or CI harm seen.
  - `[low]` `[reject]` (blind-hunter) `< 1 s` wall-clock assertion in the connector rejection test may flake — the bound is what tells "rejected without connecting" from "tried 192.168.1.10 and failed"; a string check never nears 1 s; deleting it loses that coverage.
  - `[low]` `[reject]` (blind-hunter) Smoke reads only the newest log file / aborts without `apps.json` — a run across log rotation and a machine without `apps.json` are smoke misuse; the tracer smoke has the same preconditions; fixing adds code.
  - `[low]` `[reject]` carried (blind-hunter) Corrupted `\b` evidence paths in Implementation Notes — fix edits this build's plan; correct paths are in Auto Run Result.
  - `[false]` `[reject]` (blind-hunter) Plan status/iteration fields contradict each other — `in-review` is the correct state during review, `review_loop_iteration` counts only bad_plan loopbacks (none), and Finalize rewrites status; fix would edit the plan.
  - `[low]` `[reject]` (blind-hunter) `cdpLostHandled` `null` cannot separate "ignored" from "app missing", loop continues after match — a non-null snapshot without the app is unreachable (snapshots list every configured app, the host runs only configured apps); cosmetic.
  - `[low]` `[reject]` (edge-case-hunter) A target that reconnects and drops again restarts the deadline each cycle, so `CdpLost` never fires — each cycle completes the full setup, so the page does get its bridge; the plan specifies repeatable reconnects; an outage-window cap adds state for a flapping pattern not seen on Edge.
  - `[low]` `[reject]` (edge-case-hunter) Hung attempt uses the whole window — same entry as the blind-hunter row.
  - `[low]` `[reject]` (edge-case-hunter) Outer catch posts no `CdpLost` — same entry as the blind-hunter row.
  - `[false]` `[reject]` (edge-case-hunter) Old session's `CdpLost` fails a new launch after Stop/restart — Restart goes through `Stopping` (`Orchestrator.Stop.cs`), where `OnCdpLost` is ignored, and the new Start follows the stop's results, after the old loop was cancelled by the stop token; a Start without Stop on a running app does not occur.
  - `[low]` `[patch]` (verification-gap) Backoff counted from the previous attempt's start is not pinned for a slow attempt — test `Next_backoff_counts_from_the_start_of_a_slow_attempt` (attempt 2 at 850 ms); fails when the count moves to the attempt's end (checked by the implementer).
  - `[medium]` `[patch]` (verification-gap) Main-frame navigation during reconnect setup is not tested to refresh the binding — test `Main_frame_navigation_during_reconnect_setup_re_adds_the_binding_on_the_new_session`; fails when `TryAttach` moves after setup (checked by the implementer).
  - `[medium]` `[defer]` (intent-alignment) A3: a document loaded during the outage gets no PageRuntime — same entry as the blind-hunter defer row.
  - `[maybe-false]` `[defer]` carried (intent-alignment) Recovery path never runs against real Edge — already in `deferred` (medium, unverified).
  - `[false]` `[reject]` (intent-alignment) B2: a closed target with a live browser could be recovered by picking another page target — re-selection could install the bridge into a WebUI or popup (DW-52), and Failed → "Wciśnij OK" is the designed repair (J4, 6.7); the ticket asks for reconnect, not target re-selection.
  - `[false]` `[reject]` carried (intent-alignment) 30 s from settings to host covered only by the smoke.
  - `[false]` `[reject]` carried (intent-alignment) Home not exercised inside a running loop (smoke: 90 ms before, 63 ms after the limit this pass).
  - `[false]` `[reject]` carried (intent-alignment) D-pad satisfied only structurally (no action path to BrowserHost yet).
  - `[false]` `[reject]` carried (intent-alignment) "Late replies" as envelope `id` (ticket 6.6).
  - `[false]` `[reject]` (intent-alignment) F1/F2: "log ma `Failed`" might need a literal state-transition entry — `cdpLostHandled {state: failed}` is the Orchestrator loop's entry with the state from the resulting snapshot; the smoke asserts it.
  - `[low]` `[reject]` carried (intent-alignment) `covers: R3` mismatch — ticket metadata.

## Design Notes

- **Same target only.** The binding and init script belong to the page the host launched and navigated. Re-selecting "first page target" on reconnect could install them into a WebUI or popup (DW-52 risk). A vanished target with a live process is a real failure → `Failed`, which J4 (6.7) repairs.
- **D-pad not buffered.** Actions do not reach BrowserHost yet (epic-netflix-dpad, epic Notes 2026-10-21). This story guarantees the host side of R8: no outgoing queue exists and reconnect replays nothing, which the exact-method-list test pins. The future `action` command must check for a current CDP session and reject when there is none.
- **Script once per session.** `Page.addScriptToEvaluateOnNewDocument` registrations live as long as their CDP session, so a navigation needs only the binding (AD-1), and a new session needs both.
- **Real-machine reach.** Without elevation a TCP socket of a live target cannot be dropped from outside (`SetTcpEntry` needs admin), so the live smoke exercises the give-up path (target closed, process alive); the recovery path is covered on fakes and by the connector test against a real loopback socket.

## Verification

**Commands:**
- `dotnet build MediaStation.slnx -c Release` -- expected: 0 warnings, 0 errors.
- `dotnet test --solution MediaStation.slnx -c Release` -- expected: all pass (pre-existing skip only).
- `npm test` in `web/` -- expected: pass.
- `dotnet publish src/MediaStation.App -c Release -r win-x64 --self-contained` -- expected: exit 0.
- `powershell -NoProfile -ExecutionPolicy Bypass -File tools/check-browserhost-reconnect.ps1` -- on the current machine: same preconditions and cleanup as the tracer smoke; OK on the Netflix tile, wait for `pageHello`; open a second page target (`PUT /json/new?about:blank` on `127.0.0.1:<cdpPort>`, fallback browser-level `Target.createTarget`) and close the original (`/json/close/<id>`, id from `/json/list` matching the host's target); assert the AC log entries and timing, Home before and after the limit (shell foreground), screenshots before/after the limit and `measurements.jsonl` under `logs\browserhost-reconnect-evidence-<ts>`; exit 0 = PASS.
- `powershell -NoProfile -ExecutionPolicy Bypass -File tools/check-browserhost-tracer.ps1` -- regression, expected PASS.

**Manual checks (if no CLI):**
- Screenshot after the limit: shell on top, Netflix tile in the failed state.

## Auto Run Result

Status: built (ran on **UM760**: `MEDIASTATION` / `F7BSI` / `EliteMini Series`)

**Summary.** If BrowserHost loses the CDP connection to a running browser, it now reconnects to the same page target, waiting 250 ms, 500 ms, 1 s and then 2 s between attempts. On success it installs `Runtime.addBinding`, `Page.enable` and the PageRuntime init script on the new session. It does not navigate and replays nothing, and it watches the new session for later drops too. If it cannot reconnect within `cdpReconnectTimeout` (30 s by default), it logs `warn cdpLost` and posts one `CdpLost`. The existing Orchestrator handler then puts the tile in `Failed`, and the Orchestrator loop logs `cdpLostHandled {state: failed}`. A Stop or the browser exiting ends the loop without a report. A connection that arrives late is closed without effect. Events from a replaced or closed CDP session are ignored. A main-frame navigation still re-adds only the binding. `cdpReconnectTimeout` is now capped at 600 s.

**Files changed.**
- `src/MediaStation.App/Hosts/Browser/BrowserHost.cs`: reconnect loop with backoff and deadline, a shared setup helper, guards so only the current session's events count, locked `Session` channel handling, and logs `cdpClosed`/`cdpReconnected`/`cdpLost`.
- `src/MediaStation.App/Hosts/Browser/CdpSession.cs`: `ICdpTransport.Endpoint`, and `CdpConnector.ReconnectAsync`, which makes one loopback-only attempt.
- `src/MediaStation.App/CompositionRoot.cs`: passes in the reconnect timeout and the reconnect function.
- `src/MediaStation.App/Orchestrator/OrchestratorLoop.cs`: new `cdpLostHandled {appId, state}` log entry.
- `src/MediaStation.Core/Config/ConfigValidation.cs`: 600 s cap on `cdpReconnectTimeout`, and the doc comment now names it.
- `tests/MediaStation.Core.Tests/BrowserHostTests.cs`: BrowserHost tests go from 82 to 110, covering every Matrix row, the deadline, the backoff instants, stale-session handling and Home on a real Orchestrator. `OrchestratorLoopTests.cs` gets 2 tests and `ConfigValidationTests.cs` 2 cases.
- `tools/check-browserhost-reconnect.ps1`: new real-machine smoke for the give-up path, Failed and Home.

**Review findings.** There were 29: high 0, medium 5, low 14, false 8, maybe-false 2. None were intent gaps or plan defects.
- **Patched (7 entries):**
  - Medium (3):
    - the stale-session test now actually reaches the guard (blind-hunter, edge-case and verification-gap all found this);
    - a setup reply held on a reconnected transport is now tested against the deadline;
    - a reconnect function that throws is now tested to keep retrying.
  - Low (4):
    - a race between Detach and Stop could leak a closed session, which is now disposed (blind-hunter and edge-case);
    - the unused `Session.Cdp` getter is removed;
    - the `MaxTimeoutSeconds` doc now names `cdpReconnectTimeout`;
    - new tests cover a late transport after process exit and setup failure on each of the three setup methods.
- **Deferred (1):** it is not yet proven that the already-loaded page can use the binding after a reconnect on real Edge (medium, unverified). Checking it needs an elevated socket reset.
- **Rejected, with reasons:**
  - broken evidence paths in the notes (low, the fix would edit the plan; the correct paths are below);
  - tautological port check (false: the endpoint is checked against the app's port when first stored);
  - `attempts` count when there is no endpoint (low, unreachable);
  - no failure reason in the log (low, would need extra plumbing);
  - smoke picks the target by count (false: exactly one page target is enforced);
  - smoke timeout and parameter ranges, twice (low, misuse only);
  - Foreground → Failed not run on hardware (low, unit-tested, and the commands are proven on hardware by the Home flow);
  - empty change log and the wait before the first attempt (false);
  - spurious `cdpClosed` on browser exit (low);
  - pwsh 7 date parsing (low, the script runs under Windows PowerShell);
  - intent-alignment:
    - re-adding the script on every navigation (false: AD-1, and it would double-run PageRuntime);
    - D-pad satisfied only by structure (false: there is no action path yet, epic-netflix-dpad owns it);
    - envelope `id` as the "late replies" (false: that is 6.6's scope);
    - Home inside a running loop (false: the smoke shows 72–86 ms);
    - the 30 s setting reaches the host only in the smoke (false);
    - `covers: R3` mismatch (low, ticket metadata).

**Follow-up review recommended:** true. This was a first pass and three medium entries were patched. Named risk: the new `FakeTransport.DeliverAfterDispose` fixture and the three new reconnect tests added in this pass were not seen by any review lens. Patched counts: high 0, medium 3, low 4.

**Verification (UM760, 2026-10-29, after the patches).**
- `dotnet build MediaStation.slnx -c Release`: 0 warnings, 0 errors.
- `dotnet test --solution MediaStation.slnx -c Release --no-build`: 1419 total, 1418 passed, 1 skipped (the existing skip), 0 failed.
- `npm test` in `web/`: 123 passed.
- `dotnet publish src/MediaStation.App -c Release -r win-x64 --self-contained`: exit 0.
- `tools/check-browserhost-reconnect.ps1`: PASS, before the patches (`%LOCALAPPDATA%\MediaStation\logs\browserhost-reconnect-evidence-20261029-163803`) and after them (`…-164905`).
  - `cdpClosed {timeoutSeconds: 30}`, then `cdpLost {attempts: 17}` 30.01 s later;
  - `cdpLostHandled {state: failed}`, with no `cdpReconnected` and no `processExited`, and the Edge root still alive;
  - Home brought the shell to the front in 86 ms before the limit and 72 ms after it;
  - `after-limit.png` (checked by eye) shows the Netflix tile as "Nie odpowiada / Coś się zacięło. Wciśnij OK".
- `tools/check-browserhost-tracer.ps1` (regression): PASS (`…\browserhost-tracer-evidence-20261029-163848`, `-164945`).
- Correct evidence paths for the implementer's earlier runs: `logs\browserhost-reconnect-evidence-20261029-163329`, `-163437`, and `logs\browserhost-tracer-evidence-20261029-163526`.
- Not tested on the real machine: the reconnect-success path, because dropping a live socket needs elevation. It is covered on fakes and by a connector test on a real loopback socket.

**Residual risks.**
- If the page navigates during an outage, the new document gets no PageRuntime: the old session's script went with it, and reconnect does not reload. There is no `hello` until the next navigation after reconnect.
- After a reconnect, the loaded page's use of the re-added binding is unproven on real Edge (see the deferred item).
- The epic Notes still list the `cdpReconnectTimeout` upper bound as deferred. It is now closed in code; the PO should update the epic Notes.
- Whole-epic acceptance on UM760 (the user, via `fp-acceptance`): with Netflix running, run `tools/check-browserhost-reconnect.ps1`, or close the page target by hand through `127.0.0.1:9222/json/close/<id>` after opening a second target. Expect the tile to show "Nie odpowiada" about 30 s later, with `cdpLostHandled {state: failed}` in the log, and the remote's Home to bring back the shell both before and after that.

### Re-dispatched review pass (2026-10-29, ran on UM760: `MEDIASTATION` / `F7BSI` / `EliteMini Series`)

Status: built

**Summary.** The orchestrator sent the `built` plan back as `ready-for-dev`. The implementation subagent found every task already done in `9252f4575caa7310385b9dfde1f8ec2cd33e63d0` and changed no code. A full four-lens review then ran on the whole diff since `9aaa8117fa5fdcbf76044e39e779fc5b35f36fe3`. This pass changes only tests; production code is unchanged.

**Files changed in this pass.**
- `tests/MediaStation.Core.Tests/BrowserHostTests.cs`: three new tests (navigation during reconnect setup re-adds the binding; backoff counted from the start of a slow attempt; Stop while a reconnected transport holds its setup reply). The endpoint tests now use reconnect transports with a different target id, and the faulting-seam test now checks the full attempt schedule and a single `CdpLost`, using the shared `NeverBackScheduleMs`. BrowserHost tests: 113.
- This plan: triage log, a second `deferred` item, and this result.

**Review findings.** 32 findings: high 0, medium 4, low 18, false 9, maybe-false 1. Nine were carried over from the previous pass.
- **Patched (5 entries, all test-only):**
  - medium (1): navigation during reconnect setup (blind-hunter and verification-gap);
  - low (4): Stop during a held reconnect setup, distinct endpoints in the same-endpoint tests, the faulting-seam assertions, and backoff from a slow attempt's start.
  - The implementer showed that the two behaviour-pinning tests fail when the code is deliberately broken (backoff counted from the attempt's end; `TryAttach` moved after setup).
- **Deferred (1 new):** a main-frame navigation during an outage leaves the new document without PageRuntime until the next navigation (medium). Recovering needs `Page.reload`/`Runtime.evaluate` (excluded by AD-1) or a hello watchdog, which is a product decision. The earlier deferred item (recovery on real Edge, medium, unverified) stays.
- **Rejected, with reasons (see the triage log):**
  - Per-attempt timeout (low, raised twice): only a wedged browser stalls a loopback attempt.
  - `CdpLost` from the outer catch (low, raised twice): the catch path is unreachable in practice.
  - Non-atomic `CdpLost` vs Stop (low): the Orchestrator ignores `CdpLost` in `Stopping`.
  - Stale `CdpLost` failing a new launch (false): Restart goes through `Stopping`.
  - Flapping reconnects restarting the deadline (low): every cycle restores a working bridge.
  - Screenshot not asserted by the smoke (low): the state is asserted from the log, and the screenshot was checked by eye.
  - Harness leak (low), the `< 1 s` assertion (low) and smoke log rotation / missing `apps.json` (low).
  - Plan status fields (false) and `cdpLostHandled` null ambiguity (low).
  - Re-selecting a target on reconnect (false: DW-52 risk, and J4 repairs the tile).
  - "Log ma Failed" as a literal transition (false).
  - Carried rows keep their earlier verdicts.

**Follow-up review recommended:** false. One medium and no high entries were patched in this pass, and all patches are tests. Patched counts: high 0, medium 1, low 4.

**Verification (UM760, 2026-10-29, after the patches).**
- `dotnet build MediaStation.slnx -c Release`: 0 warnings, 0 errors.
- `dotnet test --solution MediaStation.slnx -c Release --no-build`: 1422 total, 1421 passed, 1 skipped (the existing `PowerNotificationsHarnessTests` skip), 0 failed.
- `npm test` in `web/`: 123 passed.
- `dotnet publish src/MediaStation.App -c Release -r win-x64 --self-contained`: exit 0.
- `tools/check-browserhost-reconnect.ps1`: PASS, before the patches (`%LOCALAPPDATA%\MediaStation\logs\browserhost-reconnect-evidence-20261029-171355`) and after them (`…-172353`). After the patches:
  - `cdpClosed {timeoutSeconds: 30}`, then `cdpLost {attempts: 17}` 30.01 s later;
  - `cdpLostHandled {state: failed}`, with no `cdpReconnected` and the Edge root still alive;
  - Home brought the shell to the front in 90 ms before the limit and 63 ms after it;
  - `after-limit.png` (checked by eye) shows the Netflix tile as "Nie odpowiada / Coś się zacięło. Wciśnij OK".
- `tools/check-browserhost-tracer.ps1` (regression): PASS (`…\browserhost-tracer-evidence-20261029-171440`, `-172433`).
- Matrix audit: every Matrix row has a passing test. The iframe and `bindingRefreshFailed` cases are covered by the 6.2 tests `Binding_is_re_added_only_after_main_frame_navigation` and `Failed_binding_refresh_is_only_a_warning`.
- Not applicable: the live recovery path on real Edge, because dropping a live socket needs elevation. It is covered by fakes and the loopback connector test, and remains a deferred item.

**Residual risks.**
- Both `deferred` items: recovery on real Edge is unproven, and a navigation during an outage leaves the page without PageRuntime.
- The epic Notes still list the `cdpReconnectTimeout` upper bound as deferred, but it is done in code. The PO should update them.
- Whole-epic acceptance on UM760 is unchanged from the previous pass's instructions above.
