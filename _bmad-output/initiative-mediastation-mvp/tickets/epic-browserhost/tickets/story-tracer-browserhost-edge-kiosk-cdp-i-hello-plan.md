---
title: 'Story 6.2: Tracer BrowserHost — Edge kiosk, surowe CDP i hello'
type: 'feature'
ticket: '2'
created: '2026-10-29'
status: done
baseline_revision: 'cd6a04656e1b5fedb7ecba5b3423bae11ebbc0c1'
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
deferred:
  - summary: >-
      BrowserHost attaches CDP to the first `type: page` target in `/json/list`, not specifically the `about:blank` page it launched, so an extra Edge page target listed first (WebUI, restored tab) would get the binding, script and navigate.
    evidence: |-
      maybe-false. Three smoke runs on UM760 (Edge 156, fresh kiosk profile) picked the right target. Settle by dumping `/json/list` right after a kiosk start across several runs (also with a profile that had other tabs) and checking whether any other `page` target precedes the launched one; if so, prefer the target whose url is `about:blank`.
    location: >-
      src/MediaStation.App/Hosts/Browser/CdpTargets.cs (PageWebSocketUrl)
    severity: medium (unverified)
  - summary: >-
      Stop kills the browser tree without waiting for the root to exit, so a restart (StopApp → CommandCompleted → StartApp) can launch a new Edge on the same profile and CDP port while the old one still holds the profile lock.
    evidence: |-
      maybe-false. `StopSessionAsync` waits up to 3 s for a graceful close, then `Process.Kill(entireProcessTree)` without awaiting exit, and `StopCoreAsync` posts `ProcessExited` + `CommandCompleted` at once. Settle with a restart run on UM760 where the graceful close does not finish (J4, story 6.7): look for `launcherExited` (handoff) or a CDP connect to the old instance; if seen, await the root's exit (bounded) after `KillTree`.
    location: >-
      src/MediaStation.App/Hosts/Browser/BrowserHost.cs (StopSessionAsync)
    severity: medium (unverified)
  - summary: >-
      AD-7 says a page envelope with another major `v` is logged and marks the tile with an error state; BrowserHost only logs `pageMessageRejected`.
    evidence: |-
      `OnBindingCalled` logs and returns; no Event reaches the Orchestrator. Envelope versioning belongs to story 6.6 (envelope v1 with `id`, version tests), which should add the tile error path.
    location: >-
      src/MediaStation.App/Hosts/Browser/BrowserHost.cs (OnBindingCalled)
    severity: low
---

<intent-contract>

## Intent

**Problem:** Web tiles have no real host: without `MEDIASTATION_DEV_HARNESS=1` the composition root wires `MissingHostPort` (start fails), and the harness launches Edge without CDP, so nothing proves the AD-1 control path (own launch, raw CDP on loopback, page script → binding → host) works end to end.

**Approach:** Add the thinnest real `BrowserHost` (`src/MediaStation.App/Hosts/Browser/`): launch Edge/Chrome from the `apps.json` web entry with `profiles\<appId>`, the AD-1 flags, `--remote-debugging-port=<cdpPort>` and `--kiosk`; report `AppWindowAttached` as soon as the window exists (AD-13: attach does not wait for CDP); connect a raw CDP client (`ClientWebSocket`, no `Runtime.enable`) on `127.0.0.1`, install binding + minimal PageRuntime, navigate to the driver manifest's start URL, and log the page's `hello` with `navigator.webdriver`. Existing Orchestrator/WindowManager flow does OK → Foreground and Home → minimize without `SW_HIDE`.

## Boundaries & Constraints

**Always:**
- Launch args exactly, in order: `--user-data-dir=<DataRoot>\profiles\<appId>`, `--remote-debugging-port=<cdpPort>`, `--no-first-run`, `--no-default-browser-check`, `--autoplay-policy=no-user-gesture-required`, `--kiosk`, `about:blank` (AD-1, spike 6.1). The service URL is reached by CDP `Page.navigate` after binding and init script are installed, so every service document gets the runtime.
- CDP only to `127.0.0.1:<cdpPort>`: target discovery by HTTP `GET /json/list` (first `type: "page"` target not `devtools://`), and the WebSocket URL must have host `127.0.0.1` and the same port, else reject. `ClientWebSocket` without `Origin` header and without per-message deflate.
- CDP calls used in this story: `Runtime.addBinding` (initially and again on every main-frame `Page.frameNavigated`, i.e. frame without `parentId` — spike: without `Runtime.enable` the binding is not carried to new documents), `Page.enable`, `Page.addScriptToEvaluateOnNewDocument`, `Page.navigate`. The CDP session refuses `Runtime.enable` (throws before sending) — a guard, not only a convention.
- Page assets read from disk at every start (AD-13 hotfix rule): `<AppContext.BaseDirectory>\page\runtime.js` and `<…>\page\drivers\<service>\manifest.json`. Missing/invalid asset → `CommandFailed(Unexpected)` with a `warn` naming the file, no launch.
- `hello` arrives as `Runtime.bindingCalled{ name: "__msBridge", payload }` with payload JSON `{ v: 1, type: "hello", id: null, payload: { service, driverVersion, webdriver } }`; host logs `info browser pageHello { appId, service, driverVersion, webdriver }`. Other `v`, other `type`, unparsable payload or other binding name → `warn browser pageMessageRejected { appId, reason }`, never an exception.
- Start result: `AppWindowAttached` when the window is found; `CommandCompleted` once CDP is connected and set up (binding, script, navigate sent); CDP not connected within `settings.cdpConnectTimeout` from launch → `CommandFailed(CdpUnavailable)` + `warn browser cdpUnavailable` (6.4 refines/tests the full distinction). No window within 20 s or launcher exit before a window → `CommandFailed(Unexpected)` + `warn browser browserStartFailed { reason }`, process tree killed.
- Process exit not caused by Stop → `ProcessExited` (as the harness does). Stop: close CDP, `CloseMainWindow` (3 s), kill tree, then `ProcessExited` + `CommandCompleted`; no session → still both (idempotent).
- Port methods never block the Orchestrator thread and never throw across the port (AD-4); the host never shows/raises/minimizes windows (AD-14/15).
- Composition: `AppType.Web` → `BrowserHost` when the harness is off; with `MEDIASTATION_DEV_HARNESS=1` the existing `DevHarnessHostPort` stays for web and moonlight unchanged (keeps `tools/check-web-start.ps1` / `check-home-s10.ps1` valid).
- `apps.json` `service` must be kebab-case (it is now a path segment) — closes the deferred note F18/service in the epic Notes. `cdpConnectTimeout` is already bounded (`< startingTimeout ≤ 600`), so that deferred note needs no code here; record it in Implementation Notes.
- Code comments in Polish, matching surrounding files; new App sources free of WPF/Win32 types are linked into `MediaStation.Core.Tests` (Consistency Conventions).

**Never:** `Runtime.enable`, Playwright (no package anywhere), `--remote-allow-origins`, `--remote-debugging-address`, `--enable-automation`, any non-loopback listen or connect; `Runtime.evaluate` (webdriver comes from the page's own `hello`); ProbeExisting/RenewSession behaviour (stay silent no-ops — story 6.5); reconnect/`CdpLost` (6.3: on socket close only `info browser cdpClosed`); `FileVersion`/`Last Version`/`Browser.getVersion`, `PortInUse`/`ProfileInUse` (6.4); esbuild/TS page bundle build, envelope `id` correlation, `bridge.send/receive` API (6.6); ServiceDriver code (epic-netflix-dpad); changes to Orchestrator, WindowManager or AudioRouter; editing ticket files.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Happy path | web entry `netflix`, Edge present, assets present | `appLaunch` with exact args → `AppWindowAttached{hwnd,rootPid}` → CDP setup → `CommandCompleted` → `pageHello{webdriver:false}` | — |
| Browser missing | no `msedge.exe` candidate | `CommandFailed(Unexpected)`, `browserStartFailed{reason:browserNotFound}`, no process | — |
| Asset missing | no `page\runtime.js` or manifest | `CommandFailed(Unexpected)`, `browserStartFailed{reason:pageAssetMissing, file}` | — |
| CDP never answers | window found, `/json/list` fails until deadline | `AppWindowAttached`, then `CommandFailed(CdpUnavailable)`, `cdpUnavailable` | process left alive (Orchestrator decides) |
| No window | launcher exits / 20 s pass | `CommandFailed(Unexpected)`, tree killed | — |
| Non-loopback ws URL | `/json/list` returns `ws://0.0.0.0:…` or other port | target rejected, treated as not-yet-available | ends in `CdpUnavailable` |
| Bad page message | binding payload `v:2`, unknown `type`, junk | `pageMessageRejected{reason}` | no throw, session continues |
| Main-frame navigation | `Page.frameNavigated` without `parentId` | `Runtime.addBinding` re-sent; iframe navigation ignored | send failure → `warn` only |
| Stop | running session / none | `ProcessExited` + `CommandCompleted` | — |

</intent-contract>

## Code Map

- `src/MediaStation.App/Hosts/DevHarness/DevHarnessHostPort.cs` -- reference for session lifecycle (register before start, `WatchExit`, `StopSession` = CloseMainWindow 3 s + `Kill(entireProcessTree)`, `Task.Run` off the Orchestrator thread, `FindWindow` via `WindowTreePicker`). Do not modify behaviour.
- `src/MediaStation.App/Hosts/DevHarness/HarnessBrowser.cs` -- `ExecutableCandidates`/`ResolveExecutable`/`ProfileArgument`/`SafeDiagnosticUrl`. Move the browser-agnostic pieces (candidates, resolve, profile arg) into a new `Hosts/Browser/BrowserLaunch.cs` and make `HarnessBrowser` forward to them so `DevHarnessTests` stay green unchanged.
- `src/MediaStation.App/Platform/Window/WindowTreePicker.cs` (`Pick`, `Descendants`, `BrowserWindowClass`), `Win32WindowApi`, `Win32ProcessSnapshot.ParentMap` -- reuse to find the main `Chrome_WidgetWin_1` window in the launched tree.
- `src/MediaStation.App/CompositionRoot.cs` `CreateHarnessHost` / `hosts` dictionary -- wire `BrowserHost` for `AppType.Web` when harness disabled; pass `config.Settings.CdpConnectTimeout`, `Path.Combine(DataRoot,"profiles")`, `Path.Combine(AppContext.BaseDirectory,"page")`, `TimeProvider.System`.
- `src/MediaStation.Core/Ports/Ports.cs` `IAppHostPort` -- contract to implement (Start/Stop/Probe/Renew).
- `src/MediaStation.Core/Contracts/Messages.cs` -- `AppWindowAttached`, `ProcessExited`, `CommandCompleted`, `CommandFailed`, `ErrorCode.CdpUnavailable` already exist; no new Orchestrator messages.
- `src/MediaStation.Core/Config/AppsConfig.cs` (`WebApp.Service/Browser/CdpPort`), `Settings.CdpConnectTimeout`; `ConfigValidation.cs` ~L267 `KebabCase` and `case WebApp` block (~L279) for the new `service` rule; tests in `tests/MediaStation.Core.Tests/ConfigValidationTests.cs` (~L589 pattern).
- `src/MediaStation.App/MediaStation.App.csproj` -- `$(MediaStationWebDir)`; add `Content` items copying page assets to `page\` for build and publish.
- `tests/MediaStation.Core.Tests/MediaStation.Core.Tests.csproj` -- `<Compile Include … Link="App\…">` list; `RecordingLog`, `FakeTimeProvider` available.
- `spike/harness/drivers.mjs` `Cdp` class + `rawDriver` -- behaviour reference for id correlation, binding re-add on main-frame navigation, no `Runtime.enable` (do not copy blindly).
- `tools/check-web-start.ps1` -- template for the new smoke script (apps.json backup/restore in `finally`, publish exe path, `keybd_event` OK=0x0D, Home=0x24 extended, `IsIconic`/`IsWindowVisible`, log reading).
- `web/vitest.config.ts` (include only `shell/src`), `web/tsconfig.json` -- extend for the page runtime test.

## Tasks & Acceptance

**Execution:**
- [x] `src/MediaStation.Core/Contracts/PageMessages.cs` -- records `PageHello(string Service, string? DriverVersion, bool Webdriver)` and the page envelope constants (`v` = 1, binding name `__msBridge`, type `hello`) -- page protocol records live in Core (AD-16); not added to `MessageTypes` (that registry is the Orchestrator/Shell bridge).
- [x] `src/MediaStation.Core/Config/DriverManifest.cs` -- record `DriverManifest(string Service, IReadOnlyList<string> Matches, string StartUrl, string DriverVersion)` + pure `Parse(string json, string expectedService)` returning manifest or reason (service mismatch, `StartUrl` not absolute `https`) -- AD-6: start URL lives only in the manifest.
- [x] `src/MediaStation.Core/Config/ConfigValidation.cs` -- `WebApp.Service` must match `KebabCase`, error on `$.apps[i].service`; plus tests in `ConfigValidationTests.cs` -- service is now a path segment.
- [x] `web/page/runtime/runtime.js` -- hand-written ES2022 arrow-function expression `(config) => { … }` (config `{ binding, service, driverVersion }`): return if `window !== window.top`; queue envelopes, flush when `typeof globalThis[config.binding] === 'function'` (retry every 50 ms, give up after 30 s), send `hello` with `webdriver: navigator.webdriver === true` -- minimal PageRuntime; 6.6 replaces it with the TS/esbuild IIFE.
- [x] `web/page/drivers/netflix/manifest.json` -- `{ "service": "netflix", "matches": ["https://www.netflix.com/*"], "startUrl": "https://www.netflix.com/browse", "driverVersion": "0.0.0" }`.
- [x] `web/page/runtime/runtime.test.ts`, `web/vitest.config.ts`, `web/tsconfig.json` -- vitest (node env) evaluates the file text with a fake `globalThis`/`window`/`navigator`: hello buffered until binding appears, envelope shape `{v:1,type:'hello',id:null,payload:{service,driverVersion,webdriver:false}}`, iframe sends nothing.
- [x] `src/MediaStation.App/Hosts/Browser/BrowserLaunch.cs` -- pure: executable candidates/resolve (moved from harness), `BuildArguments(profileDir, cdpPort)` exactly as Always, `BuildInitScript(runtimeSource, binding, service, driverVersion)` = `(<runtime>)(<json config>);`; update `HarnessBrowser` to forward.
- [x] `src/MediaStation.App/Hosts/Browser/CdpTargets.cs` -- pure parse of `/json/list` JSON → loopback page ws URL or null (rules in Always).
- [x] `src/MediaStation.App/Hosts/Browser/CdpSession.cs` -- transport-agnostic client over `ICdpTransport` (`SendAsync(string)`, `ReceiveAsync()` → null on close): incrementing ids, pending map, `SendAsync(method, params)` → result or `CdpException` on `error`, event callback `(method, JsonElement params)`, `Closed` signal, forbidden-method guard (`Runtime.enable`). Plus `WebSocketCdpTransport` (`ClientWebSocket`, message reassembly) and `CdpConnector` (poll `/json/list` with `HttpClient` every 250 ms until token cancels).
- [x] `src/MediaStation.App/Hosts/Browser/BrowserHost.cs` -- `IAppHostPort` with seams: `IBrowserProcessLauncher` (start exe+args → `IBrowserProcess{Id, HasExited, ExitCode, WaitForExitAsync, CloseMainWindow, KillTree}`), `Func<int, WindowInfo?> findWindow`, `Func<int, CancellationToken, Task<ICdpTransport?>> connect`, `Func<string, string?> readFile`, `TimeProvider`; implements the Always/Matrix flow; `Src = "browser"`; `appLaunch` log uses `SafeDiagnosticUrl`-style args. Production adapters (`Process`-based launcher, Win32 window lookup) in `BrowserProcess.cs`.
- [x] `tests/MediaStation.Core.Tests/BrowserHostTests.cs` (+ link new App files in the csproj) -- fakes for launcher/window/transport: happy path message order and exact args; sent CDP methods = `Runtime.addBinding`, `Page.enable`, `Page.addScriptToEvaluateOnNewDocument`, `Page.navigate(startUrl)` and never `Runtime.enable`; guard throws on `Runtime.enable`; hello → `pageHello{webdriver:false}`; rejected messages; binding re-add only for main frame; CDP deadline → `CdpUnavailable` (FakeTimeProvider); missing exe/assets; launcher exit; Stop with/without session; `CdpTargets` loopback rules; boundary test: no `Playwright` in any `*.csproj` or `web/package.json`, no `--remote-allow-origins` literal in `src/**/*.cs`.
- [x] `src/MediaStation.App/MediaStation.App.csproj` -- `Content` items: `web\page\runtime\runtime.js` → `page\runtime.js`, `web\page\drivers\**\manifest.json` → `page\drivers\…`, `CopyToOutputDirectory`/`CopyToPublishDirectory` `PreserveNewest`.
- [x] `src/MediaStation.App/CompositionRoot.cs` -- wiring per Always; log `info app browserHostEnabled { apps }`.
- [x] `tools/check-browserhost-tracer.ps1` -- repeatable UM760 smoke (see Verification).

**Acceptance Criteria:**
- Given a published build, harness off and a `netflix` web tile, when OK is injected on the tile, then the log has `appLaunch` whose args contain `--kiosk` and `--remote-debugging-port=9222` and lack `--remote-allow-origins`, then `appWindowAttached`, `pageHello` with `webdriver: false`, and the Edge window is foreground covering the monitor.
- Given that running Edge, when Home is injected, then the shell (MediaStation pid) is foreground and the Edge window is `IsIconic` and `IsWindowVisible` (no `SW_HIDE`).
- Given the running Edge, when its listening sockets are listed, then port 9222 listens only on `127.0.0.1`, and its WMI command line has no `--remote-allow-origins`.
- Given `dotnet test`, then the BrowserHost, CDP, config and boundary tests pass, and `npm test` in `web/` passes including the runtime test.

## Implementation Notes

- Machine: ran on **UM760** (`MEDIASTATION`, board `F7BSI`, model `EliteMini Series`), Edge 156.0.4314.50, 2026-10-29.
- Deferred F18/`service` (epic Notes) closed: `ConfigValidation` rejects a non-kebab-case `apps[i].service` on `$.apps[i].service` (tests `Web_service_must_be_kebab_case_because_it_is_a_path_segment`, `Kebab_case_services_are_accepted`).
- Deferred F18/`cdpConnectTimeout` needs no code: it is already bounded by `cdpConnectTimeout < startingTimeout <= 600` in `CheckSettings`. `cdpReconnectTimeout` remains unbounded until 6.3 consumes it.
- Page protocol parsing lives in Core (`PageProtocol.Parse`, `PageHello`, `PageMessageResult` in `Contracts/PageMessages.cs`); it is not in `MessageTypes` or the schema export (6.6 decides the generated types).
- `DriverManifest.Parse` also rejects unknown fields and an empty/invalid `matches`, in line with the strict-config convention.
- `BrowserLaunch.SafeDiagnosticUrl` moved out of `HarnessBrowser` too (browser-agnostic); `HarnessBrowser` forwards candidates/resolve/profile arg/safe URL, `DevHarnessTests` unchanged and green.
- CDP deadline is a `CancellationTokenSource(cdpConnectTimeout, TimeProvider)` created right before `Process.Start`, linked with the session stop token; it also bounds the first three setup calls. The `Page.navigate` reply, which Chromium sends only after the navigation commits, waits on the session stop token alone, so a slow network cannot turn working CDP into `CdpUnavailable` (follow-up review). Setup failure (error reply, closed socket) is reported as `CdpUnavailable` with `reason: setupFailed`; process exit during connect gives `info cdpAbandoned` and `CommandFailed(Unexpected)` (ProcessExited already posted), so no second warning.
- `Page.navigate` with `errorText` logs `warn browser pageNavigateFailed` (still `CommandCompleted`).
- Extra log entries: `info browser cdpConnected { appId, cdpPort, url, driverVersion }`, `warn browser bindingRefreshFailed`, `info browser processExited`.
- `WebSocketCdpTransport` sets `Options.Proxy = null` (no system proxy) and no deflate; `CdpConnector` uses `SocketsHttpHandler { UseProxy = false }`.
- Boundary test checks `--remote-allow-origins` as a bare literal anywhere in `src/**/*.cs`; `--remote-debugging-address`, `--enable-automation` and `Runtime.evaluate` are checked as quoted code literals, because doc comments name them.

### Evidence (UM760, 2026-10-29)

- `dotnet build MediaStation.slnx -c Release`: 0 warnings, 0 errors.
- `dotnet test --solution MediaStation.slnx -c Release --no-build`: 1368 total, 1367 passed, 1 skipped (pre-existing), 0 failed; `BrowserHostTests` 63/63, repeated 5x without failure.
- `npm test` in `web/`: 11 files, 123 tests passed (incl. `page/runtime/runtime.test.ts`, 5 tests); `npm run typecheck` clean.
- `dotnet publish src/MediaStation.App -c Release -r win-x64 --self-contained`: `publish\page\runtime.js` and `publish\page\drivers\netflix\manifest.json` present.
- `tools/check-browserhost-tracer.ps1`: PASS twice (evidence `logs\browserhost-tracer-evidence-20261029-152754` and `-152833`). Log: `browserHostEnabled`, `appLaunch` (src `browser`, args exactly the AD-1 list with `--kiosk`, `--remote-debugging-port=9222`, no `--remote-allow-origins`), `appWindowAttached`, `cdpConnected`, `pageHello {service: netflix, driverVersion: 0.0.0, webdriver: false}`; only warning `appIconMissing`. After start Edge is foreground and covers the 1920x1080 monitor (Netflix page, no browser UI on the screenshot). Port 9222 listens only on `127.0.0.1`, owned by the browser root; WMI command lines have no `--remote-allow-origins`. After Home the MediaStation shell is foreground, Edge `IsIconic=True`, `IsWindowVisible=True`.
- Regression: `tools/check-web-start.ps1` (dev harness on) PASS, clean and leftover passes, 3 cycles each.
- Not applicable here: nothing; all checks ran on UM760. The user's whole-epic acceptance on UM760 remains (physical remote OK/Home instead of injected keys).

## Plan Change Log

## Review Triage Log

### 2026-10-29 — Review pass
- verdicts: 43 findings — high 0, medium 8, low 25, false 8, maybe-false 2
- findings:
  - `[low]` `[patch]` (blind-hunter) Stop before `session.Process` is set leaves the launched Edge running — window-null branch now always sets `Stopping` and calls `KillTree` (idempotent); test "stop during window wait".
  - `[false]` `[reject]` (blind-hunter) Stop can overtake Start through `Task.Run` ordering — the Orchestrator sends `StopApp` only from `Background`/`Failed` (`Orchestrator.Stop.cs` `OnStop`), states reached only after the start's events or a timeout, never right after `StartApp`.
  - `[low]` `[reject]` (blind-hunter) `Process.Start` throwing skips `Unregister` and logs `browserCommandFailed` instead of `browserStartFailed` — exe is resolved by `File.Exists`, `CommandFailed` is still posted and a later Start/Stop clears the session; fix adds a guard for a rare case.
  - `[low]` `[reject]` (blind-hunter) `Process` handles, `Session._stop` CTS and `_sendGate` never disposed — one handle per launch, launches are rare; fix adds dispose plumbing.
  - `[low]` `[patch]` (blind-hunter) Non-numeric `id` / non-string `error.message` throws in `Dispatch` and closes the CDP session — `ValueKind` checks added in `CdpSession.Dispatch`.
  - `[low]` `[patch]` (blind-hunter) Implementation Notes say empty `matches` is rejected but code accepts `[]` (startUrl-vs-matches check rejected: `matches` unused until the driver epic) — `DriverManifest.Parse` rejects an empty array; tests updated.
  - `[low]` `[reject]` (blind-hunter) Smoke script uses `$AppId` as the driver folder and hard-codes `netflix` — default run is correct and nobody passes another `-AppId`; fix needs a new parameter.
  - `[low]` `[reject]` (blind-hunter) `hello` from any page is logged without cross-checking service/driverVersion or origin — only effect is a log entry; origin gating via `matches` belongs to the driver/6.6 work.
  - `[maybe-false]` `[defer]` (blind-hunter) First `page` target may not be the launched `about:blank` — deferred (medium, unverified) with the check that settles it.
  - `[medium]` `[patch]` (blind-hunter) Failure branches untested (stop during start, restart, cdpAbandoned, setupFailed, navigate errorText, processStartFailed, connector) — tests added (73 BrowserHost tests); `unknownApp` and `WebSocketCdpTransport` reassembly left to the live smoke.
  - `[low]` `[reject]` (blind-hunter) In-flight binding refresh during Stop logs `bindingRefreshFailed` — needs a navigation in the same instant as Stop; fix adds a guard.
  - `[low]` `[reject]` (blind-hunter) `appLaunch.commandLine` is unquoted and logs the profile path — diagnostic only; the exact `args` array is logged next to it.
  - `[false]` `[reject]` (blind-hunter) Plan Change Log / Review Triage Log empty — the triage log is written by this pass, and the change log only records bad_plan loopbacks; fix would edit the plan.
  - `[low]` `[patch]` (edge-case-hunter) Stop before Process assigned orphans Edge — same fix as the first row.
  - `[low]` `[reject]` (edge-case-hunter) Launcher/findWindow/log throwing after registration leaves Edge unwatched — `CommandFailed` puts the app in `Failed`, and Close from AppMenu stops the still-registered session; fix adds a guard.
  - `[low]` `[reject]` (edge-case-hunter) Stop between window found and `AppWindowAttached` post — needs Starting timeout plus a user Close within microseconds; fix adds a guard.
  - `[maybe-false]` `[defer]` (edge-case-hunter) Restart relaunches before the killed tree releases the profile/port — deferred (medium, unverified) to the J4 restart run.
  - `[medium]` `[patch]` (edge-case-hunter) Transport exception mid-setup escapes the filter → `Unexpected` instead of `CdpUnavailable` — `CdpSession.SendAsync` wraps transport send exceptions in `CdpException`; test with `ObjectDisposedException`.
  - `[low]` `[patch]` (edge-case-hunter) Non-numeric `id` closes the session — same fix as the `Dispatch` row.
  - `[low]` `[patch]` (edge-case-hunter) Non-string `error.message` leaves the reply uncompleted — same fix as the `Dispatch` row.
  - `[low]` `[reject]` (edge-case-hunter) Page script spamming the binding floods `pageMessageRejected` — the service does not call `__msBridge`; rate limiting adds complexity.
  - `[low]` `[reject]` (edge-case-hunter) Syntax error in `runtime.js` still completes the start without hello — the file is shipped from the repo and vitest-tested; missing `pageHello` shows it; shape check adds a guard.
  - `[low]` `[patch]` (edge-case-hunter) Empty `matches` accepted despite the note — same fix as the manifest row.
  - `[medium]` `[patch]` (edge-case-hunter) Socket closing mid-setup gives `Unexpected` — same fix as the transport-exception row.
  - `[low]` `[patch]` (edge-case-hunter) `KillTree` skipped when `StopRequested` — same fix as the first row.
  - `[false]` `[reject]` (edge-case-hunter) Boundary test scans csproj only under src/tests/tools — `git ls-files '*.csproj'` lists only those folders; `spike/` is the frozen reference outside the build.
  - `[medium]` `[patch]` (verification-gap) `cdpAbandoned` untested — test added; `WatchExit` now posts `ProcessExited` before cancelling the start.
  - `[medium]` `[patch]` (verification-gap) `setupFailed` → `CdpUnavailable` untested — two tests added (error reply, throwing send).
  - `[medium]` `[patch]` (verification-gap) Restart over an existing session untested — test added; `FakeLauncher` hands out a fresh process per start.
  - `[medium]` `[patch]` (verification-gap) Stop during start untested — two tests added (window wait, CDP connect).
  - `[medium]` `[patch]` (verification-gap) `CdpConnector` give-up untested — test on an unused loopback port, cancelled after 1 s, returns `null`.
  - `[low]` `[patch]` (verification-gap) `pageNavigateFailed` untested — test with `errorText` added.
  - `[low]` `[patch]` (verification-gap) Empty-`matches` claim mismatch — same fix as the manifest row.
  - `[low]` `[patch]` (verification-gap) `CancelStart` before `ProcessExited` can post `CommandFailed` first — order swapped in `WatchExit`.
  - `[false]` `[reject]` (intent-alignment) "`dotnet build` without Playwright" checked by a source scan, not the build — the build graph has no Playwright: no PackageReference, nothing in `obj/project.assets.json`, and `web/package-lock.json` lists `@vitest/browser-playwright` only as an optional, uninstalled peer.
  - `[false]` `[reject]` (intent-alignment) No `dotnet test` combines Orchestrator and BrowserHost — the intent places OK→Foreground/Home at the UM760 log level, covered by `check-browserhost-tracer.ps1` (PASS 3x); the Orchestrator side is unit-tested in `OrchestratorAttachTests`/`OrchestratorHomeTests`.
  - `[low]` `[reject]` (intent-alignment) Smoke injects keyboard Home, not the remote's `VK_BROWSER_HOME` — both map to the same Home Action (accepted in epic 5); the physical remote stays in the user's epic acceptance.
  - `[low]` `[reject]` (intent-alignment) Real `runtime.js` and `BuildInitScript` never run together in an automated test — the live smoke runs exactly that on Edge and gets `pageHello`; a cross-language test adds complexity.
  - `[false]` `[reject]` (intent-alignment) Log message is `pageHello`, not `hello` — it is the page's `hello`, with service, driverVersion and webdriver; the verify clause asks for the fact, not the message name.
  - `[false]` `[reject]` (intent-alignment) `webdriver` is self-reported by the page — the runtime runs at document start before page scripts, reading the real `navigator.webdriver`; documented in Design Notes.
  - `[low]` `[defer]` (intent-alignment) Other major `v` is only logged, tile not marked as error (AD-7) — envelope versioning is story 6.6's entry; deferred there.
  - `[low]` `[reject]` (intent-alignment) No page build step into `<install>/page/`, `id` always null — ticket 6.6 owns the build and `id` correlation.
  - `[false]` `[reject]` (intent-alignment) UM760 evidence unverified by the auditor — rerun in this session on MEDIASTATION/F7BSI/EliteMini Series: smoke PASS (`browserhost-tracer-evidence-20261029-153258`, `-154127`).

### 2026-10-29 — Review pass
- verdicts: 42 findings — high 0, medium 6, low 20, false 14, maybe-false 2
- findings:
  - `[medium]` `[patch]` (blind-hunter) The `Page.navigate` reply comes only after the navigation commits, yet it was bounded by the CDP connect deadline, so a slow network turned working CDP into `CdpUnavailable` → `Failed` with a disposed session — navigate now waits on the session stop token only (`BrowserHost.ConnectCdpAsync`). Test `Slow_navigation_reply_does_not_count_against_the_cdp_deadline` (a mutation back to the deadline token fails it). Smoke: launch → `cdpConnected` 2.2–2.7 s on a good network.
  - `[low]` `[patch]` (blind-hunter) `SendAsync` waits forever when it adds its reply after `FailPending` but checks `_closed` before it is set — `CdpSession.CloseAndFailPending` (used by `Dispose` and the receive loop) now completes `Closed` before failing pending replies.
  - `[low]` `[reject]` (blind-hunter) Fixed global binding name `__msBridge` can be probed or pre-defined by the page — the intent contract fixes `name: "__msBridge"`; any binding adds a global function, no service is shown to look for this one, and a per-launch name adds session state.
  - `[low]` `[reject]` (blind-hunter) The guard blocks only `Runtime.enable`; `Runtime.evaluate` is kept out by a literal scan — nothing sends `Runtime.evaluate`, and the plan's guard is `Runtime.enable` only; a second guard and test rework add complexity for a hypothetical call.
  - `[false]` `[reject]` (blind-hunter) Unexpected browser exit logs `cdpClosed` before `processExited` — the intent contract specifies exactly `info cdpClosed` on socket close with nothing posted; the Orchestrator sees only `ProcessExited`, and `CdpLost` ordering belongs to 6.3.
  - `[false]` `[reject]` (blind-hunter) A window found after the CDP deadline gets no connect attempt — the intent contract measures `cdpConnectTimeout` from launch (AD-13), and no CDP within it is the specified `CdpUnavailable`.
  - `[medium]` `[patch]` (blind-hunter) 3 s graceful-close fallback in Stop is untested — shared root cause with the verification-gap row: `FakeProcess.IgnoreClose` + `TimedWaits`, test `Stop_kills_the_tree_when_the_window_ignores_close_for_3_seconds`.
  - `[low]` `[patch]` (blind-hunter) The `unknownApp` and blank `runtime.js` branches are untested anywhere (the smoke never reaches `unknownApp`) — tests `Unknown_app_fails_without_launching` and `Blank_runtime_is_invalid_and_fails_naming_the_file_without_launching`.
  - `[low]` `[reject]` (blind-hunter) WebSocket transport allocates 64 KB per message, has no size cap, and reassembly is untested — messages on this CDP surface are few and small (Page events, the runtime's `hello`); unbounded growth needs a page pushing gigabytes through the binding; pooling and caps add code, and the transport runs in every smoke.
  - `[low]` `[patch]` (blind-hunter) `PageHello` doc calls `webdriver` backward-compatible while `Parse` requires it — doc corrected: required in `hello` v1 because it arrived with the message; later AD-7 fields are added without a new `v`.
  - `[low]` `[patch]` (blind-hunter) Stale docs and leftovers after the extraction — the now unused `using System.IO` in `HarnessBrowser.cs` is removed; the `CreateHarnessHost` summary still describes that function (live for moonlight), the forwarders are the plan's own choice, and the English `SafeDiagnosticUrl` doc predates this change.
  - `[false]` `[reject]` (edge-case-hunter) Stop runs before `StartCoreAsync` registers the session — carried: the Orchestrator sends `StopApp` only from `Background`/`Failed` (`Orchestrator.Stop.cs` `OnStop`), never right after `StartApp`.
  - `[false]` `[reject]` (edge-case-hunter) Stop while Start awaits the previous session's stop still launches a browser — no `StopApp` while `Starting` (same `OnStop` rule); even then `WaitForWindowAsync` returns at once on `Stopping` and the window-null branch kills the tree as `stoppedDuringStart`.
  - `[low]` `[reject]` (edge-case-hunter) `Process.Start` throws rather than returning null, so `processStartFailed` is unreachable in production — carried: the exe is resolved by `File.Exists`, `CommandFailed` is still posted; the fix adds a guard for a rare case.
  - `[false]` `[reject]` (edge-case-hunter) `Kill(entireProcessTree)` can throw `AggregateException` — .NET skips access-denied/exited children; a tree that really could not be terminated is reported loudly as `CommandFailed(Unexpected)`, which is correct, and reachability was not shown.
  - `[maybe-false]` `[defer]` (edge-case-hunter) Relaunch before the killed tree releases the profile/port — carried: already deferred (DW-53), settled by the J4 restart run; not deferred again.
  - `[low]` `[reject]` (edge-case-hunter) A browser dying mid-setup can surface as `cdpUnavailable` before `ProcessExited` when the socket closes first — the Orchestrator ends in `Failed` either way (`OnCommandFailed`, then `OnProcessExited` → `ForgetRule`); needs a crash inside the sub-second setup window, and the fix adds a `HasExited` guard.
  - `[low]` `[patch]` (edge-case-hunter) `WatchExit` disposed CDP before posting `ProcessExited`, so a setup failure could post `CommandFailed` first, against the order the prior pass set — `Cdp.Dispose()` moved after the post, next to `CancelStart`; test `Process_exit_during_cdp_setup_reports_exit_first_then_abandons_cdp_without_warning`.
  - `[false]` `[reject]` (edge-case-hunter) Exit between the navigate reply and `CommandCompleted` posts a completion for a dead session — `OnCommandCompleted` for a start only removes `_pendingStarts` and never changes state.
  - `[medium]` `[reject]` (edge-case-hunter) A browser left by a previous MediaStation run on the same profile/port makes the launcher hand off (`launcherExited`) — real, but the intent contract's Never puts `PortInUse`/`ProfileInUse` in 6.4 and adoption in 6.5; already a recorded residual risk.
  - `[low]` `[reject]` (edge-case-hunter) Binding refresh is sent without a token or timeout and piles up on a hung target — one small pending task per main-frame navigation, released when the socket closes; the fix adds a timeout parameter.
  - `[maybe-false]` `[defer]` (edge-case-hunter) Another page target is listed before the kiosk tab — carried: already deferred (DW-52); not deferred again.
  - `[low]` `[reject]` (edge-case-hunter) No size cap on binding payload reassembly — same as the transport row: nothing sends large payloads through `__msBridge`, and a cap adds code.
  - `[low]` `[reject]` (edge-case-hunter) A hotfixed `runtime.js` ending in `;` plus a trailing comment breaks the init script — the shipped file ends in `}` with no comment; it needs a hand edit on the device, a missing `pageHello` shows it, and the fix adds parsing.
  - `[low]` `[reject]` (edge-case-hunter) Smoke `-AppId` vs the hard-coded `netflix` service — carried: the default run is correct and nobody passes another `-AppId`.
  - `[false]` `[reject]` (edge-case-hunter) `appLaunch` args are not `SafeDiagnosticUrl`-filtered as the plan says — the only URL argument is `about:blank`, which `SafeDiagnosticUrl` returns unchanged; nothing sensitive is logged.
  - `[medium]` `[patch]` (verification-gap) A Stop where the window ignores close for 3 s is never tested — same entry as the blind-hunter row; the test asserts no post and no kill at 2.9 s, then `KillTree`, `ProcessExited` and `CommandCompleted(8)` at 3 s.
  - `[low]` `[patch]` (verification-gap) The CDP deadline's start point (launch, window search included) is not pinned — test `Cdp_deadline_counts_from_launch_including_the_window_search` (window at ~10 s, nothing at 14.9 s, `CdpUnavailable` at 15 s).
  - `[medium]` `[patch]` (verification-gap) Deadline or Stop with a setup reply outstanding is never tested — `FakeTransport.HoldMethod`/`ReleaseHeld`; tests `Setup_reply_missing_at_the_deadline_is_cdp_unavailable_with_setup_failed` and `Stop_while_a_setup_reply_is_outstanding_fails_the_start_as_stopped`.
  - `[medium]` `[patch]` (verification-gap) The shipped `web/page/drivers/*/manifest.json` is never parsed by a test — test `Shipped_driver_manifests_parse_under_their_folder_name` (strict parse with the folder name as service; `netflix` present).
  - `[low]` `[patch]` (verification-gap) The no-window test only bounds the 20 s timeout from below — restructured: nothing at 19.9 s, `windowTimeout` at exactly 20 s.
  - `[false]` `[reject]` (intent-alignment) OK → Foreground and Home are not exercised by any `dotnet test` together with BrowserHost — carried: the intent places them at the UM760 log level, covered by the smoke (PASS again this pass); the Orchestrator side is unit-tested.
  - `[low]` `[reject]` (intent-alignment) The smoke injects keyboard Home/Enter, not the remote's HID path — carried: same Home Action; the physical remote stays in the user's epic acceptance.
  - `[false]` `[reject]` (intent-alignment) Foreground is reached before CDP/hello, so it does not prove CDP works — AD-13 makes attach independent of CDP by design; the smoke and the verify clause assert `pageHello` separately.
  - `[low]` `[reject]` (intent-alignment) `hello` is covered in `dotnet test` only via a fake binding call; the real `runtime.js` and `BuildInitScript` never run together — carried: the live smoke runs exactly that on Edge and gets `pageHello`.
  - `[low]` `[reject]` (intent-alignment) Production adapters, `WebSocketCdpTransport` and the `CreateWebHost` wiring are not in `dotnet test` — thin OS wrappers exercised by every smoke run (`browserHostEnabled`, `cdpConnected`); unit-testing them needs real processes and sockets.
  - `[false]` `[reject]` (intent-alignment) No-`Runtime.enable` is checked only at `CdpSession`, not on real traffic — `CdpSession` is the app's only CDP client, its guard throws before sending, and the exact method list is asserted.
  - `[false]` `[reject]` (intent-alignment) "`dotnet build` without Playwright / `--remote-allow-origins`" is a source scan, not the build graph — carried: no PackageReference, nothing in `obj/project.assets.json`; the lockfile entry is an uninstalled optional peer.
  - `[false]` `[reject]` (intent-alignment) The log says `pageHello`, and `webdriver` is self-reported — carried: the entry is the page's `hello` with the fact asked for; the runtime reads the real `navigator.webdriver` at document start.
  - `[false]` `[reject]` (intent-alignment) The live `chrome-test` tile now goes through BrowserHost and is not exercised — before this change that tile had no host without the harness (`MissingHostPort`); it uses service `netflix` (manifest present) and the Chrome candidates already covered by `DevHarnessTests`.
  - `[false]` `[reject]` (intent-alignment) Reconnect/`CdpLost`/`cdpReconnectTimeout` (epic Requirement 2) is absent — the ticket's description and verify never ask for reconnect; ticket 6.3 is "Binding po nawigacji i reconnect CDP", and its own `covers` labels show "R2" is not the epic's Requirement 2.
  - `[low]` `[reject]` (intent-alignment) Requirement 5 / AD-7 parts are absent (bundle build, `bridge.send/receive`, `id` correlation, tile error on another `v`) — carried: 6.6 owns the build and `id`; the AD-7 tile-error part is the prior deferred row (DW-54), not deferred again.

## Design Notes

- `about:blank` + `Page.navigate` instead of launching with the service URL: the init script and binding exist before the first service document, avoiding `Runtime.evaluate` on an already-loaded page.
- `webdriver` travels in `hello` (backward-compatible field, AD-7 "pola dodaje się wstecznie zgodnie") rather than via `Runtime.evaluate`, keeping the CDP surface to the AD-1 list.
- The harness stays as an explicit dev opt-in for this story to avoid regressing accepted epic-5 checks; stories 6.5/6.8 decide its web path's retirement once BrowserHost adopts and reports `ProfileInUse`.

## Verification

**Commands:**
- `dotnet build MediaStation.slnx -c Release` -- expected: 0 warnings/errors (TreatWarningsAsErrors).
- `dotnet test --solution MediaStation.slnx -c Release` (or per test project) -- expected: all pass, new BrowserHost tests included.
- `npm test` and `npm run typecheck` in `web/` -- expected: pass.
- `dotnet publish src/MediaStation.App -c Release -r win-x64 --self-contained` -- expected: `publish\page\runtime.js` and `publish\page\drivers\netflix\manifest.json` exist.
- `powershell -NoProfile -ExecutionPolicy Bypass -File tools/check-browserhost-tracer.ps1` -- on the current machine: backs up/restores `apps.json`, refuses if an Edge holds `profiles\netflix` or MediaStation runs; starts the publish exe without the harness, injects OK then Home, asserts the AC log entries (`appLaunch`, `appWindowAttached`, `pageHello` `webdriver=false`, no `warn`/`error` except `appIconMissing`), foreground/iconic/visible state, loopback-only listener and command line; saves screenshots after start and after Home plus `measurements.jsonl` under `logs\browserhost-tracer-evidence-<ts>`; closes MediaStation and the Edge tree at the end; exit code 0 = PASS.

**Manual checks (if no CLI):**
- Screenshots: after start Edge covers the monitor without browser UI; after Home the shell is visible.

## Auto Run Result

Status: built (follow-up review pass, ran on **UM760**: `MEDIASTATION` / `F7BSI` / `EliteMini Series`)

**Summary.** The story delivers the first real BrowserHost (`src/MediaStation.App/Hosts/Browser/`), used for web tiles when the dev harness is off. It launches Edge/Chrome from the `apps.json` entry with `profiles\<appId>`, the exact AD-1 flags, `--remote-debugging-port=<cdpPort>`, `--kiosk` and `about:blank`. It reports `AppWindowAttached` as soon as the window exists. It then connects a raw CDP client on `127.0.0.1`, which refuses `Runtime.enable`, sends no Origin header and uses no proxy. It installs the `__msBridge` binding (re-added on every main-frame navigation) and the minimal PageRuntime, navigates to the driver manifest's start URL, and logs the page's `hello` as `pageHello` with `webdriver`.

This follow-up pass fixed one real defect: the reply to `Page.navigate` arrives only after the navigation commits on the network, yet it was bounded by the CDP connect deadline. A slow network could therefore fail a working start with `CdpUnavailable`. That reply now waits only on Stop or process exit. The pass also fixed two ordering races (in `CdpSession` close, and in `WatchExit` disposing CDP before posting `ProcessExited`), corrected one doc and removed an unused `using`. Nine tests close the verification gaps.

**Files changed (whole story; this pass in bold).**
- `src/MediaStation.App/Hosts/Browser/BrowserHost.cs`: the `IAppHostPort` for web apps (start, stop, exit watch, CDP setup, hello logging; Probe/Renew stay silent until 6.5). **This pass:** `Page.navigate` waits on the stop token only; `WatchExit` posts `ProcessExited` before disposing CDP.
- `src/MediaStation.App/Hosts/Browser/CdpSession.cs`: transport-agnostic CDP client, `WebSocketCdpTransport`, `CdpConnector`. **This pass:** `CloseAndFailPending` completes `Closed` before failing pending replies.
- `src/MediaStation.App/Hosts/Browser/BrowserLaunch.cs`, `CdpTargets.cs`, `BrowserProcess.cs`: pure launch args, init script and safe URL; loopback-only target selection; production process/window/file adapters.
- `src/MediaStation.App/Hosts/DevHarness/HarnessBrowser.cs`: forwards the shared pieces to `BrowserLaunch`. **This pass:** removed the unused `using System.IO`.
- `src/MediaStation.App/CompositionRoot.cs`, `MediaStation.App.csproj`: `CreateWebHost` wiring and the `browserHostEnabled` log; copies `page\runtime.js` and `page\drivers\**\manifest.json` to the output.
- `src/MediaStation.Core/Contracts/PageMessages.cs`: `PageProtocol` (v1, binding name, hello parse) and `PageHello`. **This pass:** the doc states that `webdriver` is required in `hello` v1.
- `src/MediaStation.Core/Config/DriverManifest.cs`, `ConfigValidation.cs`: strict manifest parser (AD-6); `service` must be kebab-case.
- `web/page/runtime/runtime.js`, `runtime.test.ts`, `web/page/drivers/netflix/manifest.json`, `web/vitest.config.ts`, `web/tsconfig.json`: minimal PageRuntime, its tests, the Netflix manifest.
- `tests/MediaStation.Core.Tests/BrowserHostTests.cs` (+ `ConfigValidationTests.cs`, csproj links): 82 BrowserHost/CDP/manifest/boundary tests. **This pass:** +9 tests, `FakeTransport.HoldMethod`/`ReleaseHeld`, `FakeProcess.IgnoreClose`/`TimedWaits`, and the no-window test pinned to exactly 20 s.
- `tools/check-browserhost-tracer.ps1`: repeatable real-machine smoke.

**Review findings (this pass).** 42 findings: high 0, medium 6, low 20, false 14, maybe-false 2. No intent gaps and no plan defects.
- **Patches (11 entries).**
  - Medium (4):
    - navigate reply bounded by the CDP deadline;
    - graceful-close fallback untested (blind-hunter + verification-gap);
    - deadline/Stop with a setup reply outstanding untested;
    - shipped manifest never parsed by a test.
  - Low (7):
    - `CdpSession` close race;
    - `unknownApp` / blank `runtime.js` untested;
    - `PageHello` doc;
    - unused `using`;
    - `WatchExit` dispose order;
    - CDP deadline start point unpinned;
    - window timeout lower bound only.
- **Deferred:** nothing new. The two carried maybe-false rows (first page target, relaunch before the tree exits) are already DW-52/DW-53.
- **Rejected, with reasons:**
  - fixed binding name (low: the intent fixes `__msBridge`);
  - `Runtime.evaluate` guard (low: no caller, extra guard);
  - `cdpClosed` before `processExited` (false: specified behaviour);
  - window after the deadline (false: AD-13 deadline from launch);
  - transport allocation, size cap and reassembly (low: tiny CDP surface, extra code), twice;
  - Stop before registration and Stop during a restart (false: no `StopApp` while `Starting`);
  - `Process.Start` throw (low, carried);
  - `AggregateException` from kill (false: loud failure is correct);
  - socket closing before exit is seen (low: same end state, extra guard);
  - completion after exit (false: no-op in the Orchestrator);
  - stale browser holding the profile (medium: out of scope, 6.4/6.5 by the intent contract);
  - unbounded binding refresh (low);
  - trailing comment in a hotfixed `runtime.js` (low);
  - smoke `-AppId` (low, carried);
  - unsanitised `appLaunch` args (false: only `about:blank`);
  - intent-alignment:
    - no combined Orchestrator test (false, carried);
    - keyboard vs remote Home (low, carried);
    - Foreground before CDP (false: AD-13);
    - runtime + init script only in the smoke (low, carried);
    - adapters outside `dotnet test` (low: covered by the smoke);
    - guard only at `CdpSession` (false: the only client);
    - Playwright source scan (false, carried);
    - `pageHello` name / self-reported `webdriver` (false, carried);
    - `chrome-test` tile (false: no regression);
    - reconnect (false: 6.3);
    - Requirement 5 / AD-7 parts (low, carried: 6.6, DW-54).

**Follow-up review recommended:** false. This is a follow-up pass and no `high` was patched (patched counts: high 0, medium 4, low 7), so the work has converged.

**Verification (UM760, 2026-10-29, after the patches).**
- `dotnet build MediaStation.slnx -c Release --no-incremental`: 0 warnings, 0 errors.
- `dotnet test --solution MediaStation.slnx -c Release --no-build`: 1387 total, 1386 passed, 1 skipped (the pre-existing skip in `MediaStation.App.Tests`), 0 failed. `BrowserHostTests` 82/82, 6 runs without failure. A mutation that puts the deadline token back on `Page.navigate` fails `Slow_navigation_reply_does_not_count_against_the_cdp_deadline`.
- `npm test` in `web/`: 11 files, 123 tests passed; `npm run typecheck` clean.
- `dotnet publish src/MediaStation.App -c Release -r win-x64 --self-contained`: exit 0; `publish\page\runtime.js` and `publish\page\drivers\netflix\manifest.json` present.
- `powershell -NoProfile -ExecutionPolicy Bypass -File tools/check-browserhost-tracer.ps1`: exit 0, PASS. Evidence in `%LOCALAPPDATA%\MediaStation\logs\browserhost-tracer-evidence-20261029-160754`: `measurements.jsonl`, screenshots `after-start.png` and `after-home.png`.
  - `appLaunch` args are exactly `--user-data-dir=…\profiles\netflix --remote-debugging-port=9222 --no-first-run --no-default-browser-check --autoplay-policy=no-user-gesture-required --kiosk about:blank`, with no `--remote-allow-origins`.
  - `appWindowAttached` 0.5 s after OK; `pageHello {service: netflix, driverVersion: 0.0.0, webdriver: false}` 2.9 s after OK.
  - After start Edge is foreground and covers the monitor; the screenshot shows the Netflix page with no browser UI.
  - Port 9222 listens only on `127.0.0.1`, owned by the browser root.
  - After Home the MediaStation shell is foreground, and Edge is `IsIconic=True`, `IsWindowVisible=True`; the screenshot shows the shell.
- Not run this pass: `tools/check-web-start.ps1` (dev harness). This pass's only harness change is removing an unused `using`, so it compiles to the same code.

**Residual risks.**
- If another Edge already holds `profiles\<appId>`, the start fails generically (`launcherExited`) until 6.4 adds `ProfileInUse`.
- Adoption after a MediaStation restart and session renewal after wake stay silent until 6.5. A dropped CDP socket is only logged until 6.3.
- With the navigate reply no longer bounded by the deadline, a navigation that Chromium never answers leaves the start's `CommandCompleted` pending until Stop or exit. Chromium answers every navigation with a commit or a net error, and the window is already attached.
- The live `apps.json` on UM760 also has a `chrome-test` web tile (Chrome, port 9223), not exercised.
- Whole-epic acceptance on UM760 (user, `fp-acceptance`): OK and Home on the Netflix tile with the physical remote, expecting `appWindowAttached`, `pageHello webdriver=false`, the shell on top, and Edge minimised but visible.

