---
title: 'Story 6.6: Minimalny PageRuntime i build page bundle'
type: 'feature'
ticket: '6'
created: '2026-10-29'
status: done
baseline_revision: 'ec0464278c8dfa3f980342b391cb085952806536'
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

**Problem:** The page side of BrowserHost is still the 6.2 stopgap: a hand-written `web/page/runtime/runtime.js` that can only queue and send `hello`. There is no `bridge.send/receive` for a ServiceDriver, no host → page envelope with `id`, no late-reply handling, no TS/esbuild build into `<install>/page/`, and a page envelope with another major `v` is only logged, never shown on the tile (AD-7, DW-54).

**Approach:** Replace `runtime.js` with a vanilla-TS PageRuntime under `web/page/runtime/`. esbuild bundles it into one IIFE at `web/page/dist/runtime.js`, and the `BuildWeb` MSBuild target copies it to `<exe>\page\runtime.js` for build and publish. BrowserHost injects that file as a function body that receives the config. On the host side, the page envelope gets a Core record with `id`, and a wrong major `v` posts a new `PageVersionRejected` Event, which the Orchestrator turns into `Failed` the same way as `CdpLost`.

## Boundaries & Constraints

**Always:**
- **Envelope v1 (AD-7):** `{ v: 1, type, id, payload }`, and every field is always present. Page → host messages the page starts itself have `id: null`. Host → page messages have a non-empty string `id`, and a reply repeats it. A missing payload is serialized as `payload: null`.
- **PageRuntime (TS, no framework, no DOM library):**
  - `start(config, win)` returns `null` in an iframe (`win !== win.top`). Otherwise it creates the bridge, sends `hello`, and returns `{ bridge, deliver }`. The `hello` payload has the generated type `PageHello` (`service`, `driverVersion`, `webdriver: win.navigator.webdriver === true`).
  - **Outbound buffer:** while `typeof win[config.binding] !== 'function'`, envelopes wait in a FIFO queue capped at 256, and the oldest is dropped when it is full. A waiting episode retries every 50 ms for at most 30 s. After a give-up the messages stay queued, and the next `send` checks the binding again and starts a new episode. If the binding throws, the message stays at the head of the queue and the retry is scheduled.
  - **Reinitialization after navigation:** every new main-frame document runs a fresh runtime (the init script runs per document), with its own queue and its own `hello`. A bfcache restore (`pageshow` with `persisted: true`) sends `hello` again on the same runtime and starts a new waiting episode. `pageshow` without `persisted` sends nothing.
  - **Driver API (`bridge`):** `send(type, payload?)` sends page → host with `id: null`. `receive(type, handler)` registers the single handler for a host → page `type`, replaces an earlier one, and returns an unsubscribe function. The handler is `(payload, reply) => void`, and `reply(replyType, payload?) → boolean` sends `{ v: 1, type: replyType, id: <request id>, payload }`.
  - **Inbound (`deliver(json)`, the transport-agnostic entry; the host transport comes from epic-netflix-dpad):** it returns `'handled' | 'unhandled' | 'duplicate' | 'invalid' | 'version' | 'error'`, never throws, and sends nothing for a rejected message. The checks run in this order: input not a JSON string or not an object → `invalid`; `v` not a number → `invalid`; `v !== 1` → `version`; `type` not a non-empty string, `id` not a non-empty string, or no `payload` property → `invalid`; an `id` among the last 64 delivered ids → `duplicate`; no handler → `unhandled`; a handler that throws → `error`.
  - **Late replies (stale `id`):** `reply` returns `false` and sends nothing when a newer request of the same `type` was delivered after this one, or when this request was already answered.
- **Bundle:**
  - `web/scripts/build-page.mjs` runs esbuild 0.28.2 (an exact devDependency) with `bundle`, `format: 'iife'`, `platform: 'browser'`, `target: 'es2022'`, no `globalName`, no minify, and `legalComments: 'none'`. Its first line is the banner `// MediaStation PageRuntime (esbuild IIFE, AD-6)`. The entry is `web/page/runtime/main.ts`, which has no exports and calls `start(__msPageConfig, window)`, with `__msPageConfig` declared ambient. The output is `web/page/dist/runtime.js` (git-ignored by `dist/`); `--outfile <path>` overrides it.
  - npm scripts: `build:page`, and `build` = contracts + `build:shell` + `build:page`.
- **Host injection:** `BrowserLaunch.BuildInitScript` returns exactly `(function (__msPageConfig) {\n<file>\n})(<json config>);`. The parameter name is a C# const that matches `main.ts`. The file is used verbatim: no semicolon stripping, and a trailing `//` comment is safe.
- **MSBuild (`BuildWeb`):** `npm run build:page` runs in every configuration. Inside the target, `web\page\dist\runtime.js` becomes a `Content` item linked as `page\runtime.js` (`PreserveNewest` for output and publish), and an `Error` fires when the file is missing after the build. The static `Content` item for `web\page\runtime\runtime.js` is removed. The manifests stay as they are.
- **Host protocol (`PageMessages.cs`):**
  - A record `PageEnvelope(int V, string Type, string? Id, JsonElement Payload)` is added. `PageProtocol.Parse` reads the envelope first and rejects with one of these reasons, in this order: `unparsable` (blank, bad JSON, not an object); `envelope` (`v` missing or not an integer); `version` (an integer `v` ≠ 1, with `Version` set on the result); `envelope` (`type` not a non-empty string, `id` missing or not null/non-empty string, `payload` missing); `type` (not `hello`); `id` (a `hello` with a non-null `id`); `payload`.
  - `PageHello` joins the schema export, so `web/contracts/generated/page-hello.ts` exists, and the runtime imports it with `import type`.
- **Version mismatch → tile error (AD-7, DW-54):**
  - **Host:** on `reason: version`, BrowserHost logs `warn browser pageMessageRejected { appId, reason: "version", v }`. Then it posts `PageVersionRejected(AppId, Version)` at most once per session, and only when the session is current and not stopping.
  - **Orchestrator:** it handles the Event exactly like `CdpLost`, through a shared handler: `Starting`/`Foreground`/`Background` → `Failed` with `processAlive: true`, and everything else is ignored.
  - **Loop log:** `OrchestratorLoop` logs `info orchestrator pageVersionRejectedHandled { appId, v, state }`, where `state` comes from the result snapshot, or `null` when the Event is ignored.
  - **Contracts:** the Event is registered in `MessageTypes.All` and the schema list.
- **Code rules:** comments are in Polish, matching the surrounding files. Port methods never block or throw (AD-4). New pure App code stays linked into `MediaStation.Core.Tests`.

**Never:**
- ServiceDriver code, a focus engine, the focus ring, `<video>`/`focusin` listeners, or any host → page sender or transport, such as `Runtime.evaluate`. These belong to epic-netflix-dpad.
- New CDP methods, `Runtime.enable`, or a global name or page-visible marker added by the bundle. The only page global stays the host's binding.
- Changes to the Shell bridge `Envelope<T>` or to `web/shell`.
- Changes to the reconnect, probe or start logic, or to `ErrorCode`.
- Minifying the bundle.
- Editing ticket files or `deferred-work.md`.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Hello buffered | binding appears 500 ms after start | nothing before; on the next 50 ms tick exactly one `{v:1,type:'hello',id:null,payload:{service,driverVersion,webdriver:false}}` | — |
| Binding never appears | 30 s pass | retries stop; a later `send` after the binding appears flushes the queue in order | — |
| Queue full | 300 sends without binding | the 256 newest are flushed in order | oldest dropped |
| New document | second `start` on a new window | its own `hello`; first runtime's queue unaffected | — |
| bfcache restore | `pageshow{persisted:true}` | one more `hello` | `persisted:false` → nothing |
| Request + reply | `deliver({v:1,type:'back',id:'7',payload:null})`, handler replies | `{v:1,type:'backResult',id:'7',payload:{handled:true}}`, `reply` → `true` | — |
| Late reply | `back` id `7`, then `back` id `8`, handler of `7` replies late | nothing sent for `7` (`false`); `8` still replies | second reply to `8` → `false` |
| Bad inbound | `v:2` / no `id` / junk / repeated id / no handler | `version` / `invalid` / `invalid` / `duplicate` / `unhandled`, nothing sent | handler throw → `error`, runtime keeps working |
| Host: `v:2` envelope | binding payload `{v:2,...}` twice | `pageMessageRejected{reason:version,v:2}` twice, one `PageVersionRejected` | stale/stopping session → no post |
| Orchestrator | `PageVersionRejected` in Foreground / Background / NotRunning | Failed with Mute, Minimize, ShowShell, focusRequest / Failed / ignored | — |
| Init script | file ends with `;` or `// x` | wrapped verbatim, still valid JS | — |

</intent-contract>

## Code Map

- `web/page/runtime/runtime.js`, `runtime.test.ts` -- 6.2's hand-written runtime and its eval-text test. Delete both; the new TS modules and tests replace them. Keep the behaviour the old tests pin (iframe silence, the 50 ms/30 s buffer, webdriver `undefined` → `false`).
- `web/shell/src/bridge.ts` -- style reference: a hand-written generic envelope, payload types from `../../contracts/generated/*` via `import type`.
- `web/package.json`, `web/package-lock.json` -- scripts and the exact `esbuild` 0.28.2 devDependency. It is already in `node_modules` as a transitive dependency; `npm install -D -E esbuild@0.28.2` records it.
- `web/tsconfig.json` (includes `page`, DOM lib, `verbatimModuleSyntax`) and `web/vitest.config.ts` (`page/**/*.test.ts`, node env) -- no change needed unless the build script test needs it.
- `src/MediaStation.App/MediaStation.App.csproj` L55–83 -- the static `Content` for `runtime.js` (L60) and `BuildWeb`. Follow the shell-dist pattern there (an item added inside the target, plus an `Error` check).
- `src/MediaStation.App/Hosts/Browser/BrowserLaunch.cs` `BuildInitScript` -- the new wrapper. Tests in `BrowserHostTests.cs` L73–87.
- `src/MediaStation.Core/Contracts/PageMessages.cs` -- `PageProtocol.Parse`, `PageHello` and `PageMessageResult` (add `int? Version`). Parse tests are at `BrowserHostTests.cs` ~L185–215 (reasons change: a missing `v` is now `envelope`).
- `src/MediaStation.App/Hosts/Browser/BrowserHost.cs`:
  - `OnBindingCalled` (L1225), `RejectPageMessage`: the version path and the post.
  - `Session` (~L1042+): a once-flag set under its lock.
  - The `_post` delegate, as used for `CdpLost` (L700, L1075).
- `src/MediaStation.Core/Contracts/Messages.cs` (next to `CdpLost`, L85), `MessageTypes.cs` (`All`), `ContractJson.cs` `ExportSchemas` (L117–129: add `typeof(PageHello)`) -- the contracts.
- `tests/MediaStation.Core.Tests/CoreMessageContractTests.cs` L594+ -- the expected schema file list. Add `page-hello.schema.json` and `page-version-rejected.schema.json`.
- `src/MediaStation.Core/Orchestrator/Orchestrator.cs` L126 (dispatch) and `Orchestrator.Failures.cs` `OnCdpLost` (L73) -- extract a shared live-failure handler.
- `src/MediaStation.App/Orchestrator/OrchestratorLoop.cs` L137–157 -- the `cdpLostHandled` pattern for the new log entry.
- Tests:
  - `OrchestratorFailureTests.cs`: parameterized over `"cdpLost"` facts (L29, L51, L85, L151). Add the new fact to those theories.
  - `OrchestratorLoopTests.cs`: the `cdpLostHandled` tests.
  - `OrchestratorTestSupport`: available for a real-Orchestrator test.
- `tools/check-browserhost-tracer.ps1` -- the live smoke to extend:
  - assets check at L169;
  - OK at L201;
  - `pageHello` wait at L224–230;
  - Home at L258;
  - `Send-Key`, `Wait-For` and `Get-RunEntries` helpers.
- `tools/check-browserhost-adopt.ps1` -- the regression smoke: renew, then F5 → `pageHello`.

## Tasks & Acceptance

**Execution:**
- [x] `web/page/runtime/envelope.ts` -- `ENVELOPE_VERSION`, the `Envelope` type, the `envelope()` factory and `parseInbound()`, per Always.
- [x] `web/page/runtime/bridge.ts` -- the outbound queue and flush, `send`/`receive`/`deliver`, duplicate and late-reply rules.
- [x] `web/page/runtime/runtime.ts` -- `PageConfig`, `PageWindow`, `start(config, win)`: the iframe guard, `hello` typed from `contracts/generated/page-hello`, and the `pageshow` re-hello.
- [x] `web/page/runtime/main.ts` -- the bundle entry: `declare const __msPageConfig: PageConfig; start(__msPageConfig, window);`.
- [x] Delete `web/page/runtime/runtime.js` and `runtime.test.ts`.
- [x] `web/scripts/build-page.mjs` + `web/package.json`/`package-lock.json` -- the esbuild build per Always, `build:page`, `build`, and the esbuild devDependency.
- [x] `web/page/runtime/envelope.test.ts`, `bridge.test.ts`, `runtime.test.ts` -- every runtime row of the Matrix, with fake timers and a fake window.
- [x] `web/page/runtime/bundle.test.ts`:
  - run `node scripts/build-page.mjs --outfile <temp>` (child process);
  - assert that the first line is the banner and that the file has no top-level `import`/`export`;
  - evaluate `(function (__msPageConfig) {\n<file>\n})(<config>);` with a fake `window` and check that `hello` reaches the binding;
  - check that no new key appears on the fake window except what the test set.
- [x] `src/MediaStation.App/MediaStation.App.csproj` -- the `BuildWeb` and `Content` changes per Always.
- [x] `src/MediaStation.App/Hosts/Browser/BrowserLaunch.cs` + tests -- the new `BuildInitScript`: an exact-string test, a null `driverVersion`, and a file ending with `;` and with `// x`.
- [x] `src/MediaStation.Core/Contracts/PageMessages.cs`, `ContractJson.cs` + Parse tests -- `PageEnvelope`, the reason order, `Version`, and the `PageHello` export.
- [x] `src/MediaStation.Core/Contracts/Messages.cs`, `MessageTypes.cs`, `Orchestrator.cs`, `Orchestrator.Failures.cs`, `OrchestratorLoop.cs` -- `PageVersionRejected`, the shared failure handler, the loop log; update `CoreMessageContractTests`.
- [x] `src/MediaStation.App/Hosts/Browser/BrowserHost.cs` -- the version path with the once-per-session post; update the class summary.
- [x] `tests/MediaStation.Core.Tests/BrowserHostTests.cs`, `OrchestratorFailureTests.cs`, `OrchestratorLoopTests.cs` -- the host rows of the Matrix:
  - `v:2` twice gives one post;
  - stale and stopping sessions get no post;
  - the Orchestrator states;
  - the loop log with `failed` and `null`;
  - one real-Orchestrator test: a host-posted `PageVersionRejected` in Foreground → `Failed`, then Home still shows the shell.
- [x] `tools/check-browserhost-tracer.ps1`:
  - assert that the published `page\runtime.js` starts with the banner, and record its SHA256 in `measurements.jsonl`;
  - after the first `pageHello` (wait 3 s for the count to settle), inject F5 into the foreground Edge and require a new `pageHello` within 15 s;
  - then Home, as before.

**Acceptance Criteria:**
- Given `dotnet publish src/MediaStation.App -c Release -r win-x64 --self-contained`, when it finishes, then `publish\page\runtime.js` exists, starts with the banner, and is byte-identical to `web\page\dist\runtime.js`.
- Given that published build, harness off and the Netflix tile, when `tools/check-browserhost-tracer.ps1` runs, then it passes: `pageHello` with `webdriver: false` from the bundle, a new `pageHello` after F5, the shell on top after Home with Edge `IsIconic` and visible, and no `warn`/`error` except `appIconMissing`.
- Given `npm test` and `npm run typecheck` in `web/`, and `dotnet build` + `dotnet test` of `MediaStation.slnx`, then all pass, including the new web, BrowserHost, protocol, Orchestrator and loop tests.

## Implementation Notes

Ran on **UM760** (`MEDIASTATION`, board `F7BSI`, model `EliteMini Series`), 2026-10-29.

- **Web:** `web/page/runtime/{envelope,bridge,runtime,main}.ts` replace `runtime.js`. The bridge exposes `createBridge(win, binding)` → `{ bridge, deliver, restartWaiting }`; `start` returns `{ bridge, deliver }`. A `send` that finds no pending retry starts a new 30 s episode; a `send` during an episode does not extend it; `pageshow{persisted:true}` calls `restartWaiting()` and sends `hello`. The bridge uses the global `setTimeout` (the page's window) and reads the binding only from `win`, so the bundle adds no keys to `window`.
- **`bundle.test.ts` is excluded from `tsc`** (`web/tsconfig.json` `exclude`): it needs `node:child_process`/`node:fs`, and the repo has no `@types/node`. Adding `@types/node` to the shared tsconfig would put Node globals into the browser code's type space, so the test stays outside `tsc` and Vitest still runs it.
- **esbuild 0.28.2** was already in `node_modules` but not in the lockfile; `npm install -D -E esbuild@0.28.2` recorded it (npm warned that the esbuild postinstall is not in `allowScripts`; the `@esbuild/win32-x64` binary works without it).
- **Host:** `PageProtocol.Parse` checks `v` first (`envelope` / `version` with `Version`), then `ReadEnvelope` builds `PageEnvelope` (payload cloned). `BrowserHost.RejectPageVersion` logs every rejection and posts `PageVersionRejected` once per session through `Session.TryMarkVersionRejected()` (under the session lock, false when stopping) and only while the session is still the one registered in `_sessions`.
- **Orchestrator:** `OnCdpLost` became `OnLiveFailure(appId)`, used by both Events. The `OrchestratorFailureTests` theories that had a `cdpLost` row now also have a `pageVersionRejected` row, and the Stopping fact became a theory over both.
- `BrowserLaunch.PageConfigParameter` is the C# const; `Page_config_parameter_matches_the_bundle_entry` checks that `main.ts` uses the same name.

**Verification evidence (UM760):**
- `npm test` (web): 14 files, 174 tests passed; `npm run typecheck`: clean; `npm run build:page`: `web/page/dist/runtime.js` (4303 B) with the banner on line 1, `(() => { … })();` with no global name.
- `dotnet build MediaStation.slnx -c Release`: 0 warnings, 0 errors (`BuildWeb` ran `build:page`); Debug build also 0/0 and copied `page\runtime.js`.
- `dotnet test --solution MediaStation.slnx -c Release --no-build`: 1610 total, 1609 passed, 1 skipped (the existing DW-1 `PowerNotificationsHarnessTests` skip).
- `dotnet publish src/MediaStation.App -c Release -r win-x64 --self-contained`: `publish\page\runtime.js` SHA256 `7C02F685282CFC1CD4123307E1DC8C8BC956C2DFB00E2575B61F43C8022FF0F4`, identical to `web\page\dist\runtime.js`, and it starts with the banner.
- `tools/check-browserhost-tracer.ps1`: **PASS**. Banner OK and hash recorded; `pageHello` `webdriver:false`; F5 in the foreground Edge produced a new `pageHello` in ~2.3 s; after Home the shell is on top and Edge is `IsIconic` and visible; no `warn`/`error`. Evidence: `%LOCALAPPDATA%\MediaStation\logs\browserhost-tracer-evidence-20261029-194717`.
- `tools/check-browserhost-adopt.ps1`: **PASS** (adopt, renew, then F5 `pageHello` with the bundle). Evidence: `%LOCALAPPDATA%\MediaStation\logs\browserhost-adopt-evidence-20261029-194739`.
- **Live `v:2` check (hotfix-style, then restored):** the published `page\runtime.js` was patched to `ENVELOPE_VERSION = 2` and the tracer was run with `-HelloSeconds 15`. It failed as expected: no `pageHello`, Edge not on top. The log of that run (MediaStation pid 18632) shows `warn browser pageMessageRejected {appId:netflix, reason:version, v:2}`, then `info orchestrator pageVersionRejectedHandled {appId:netflix, v:2, state:failed}`, then `MinimizeWindow`, a snapshot with `focusRequest {target:netflix, seq:2}`, `ShowShell`, and after the injected Home another `ShowShell`. The original file was restored afterwards (hash `7C02F685…F0F4` again). Evidence: `%LOCALAPPDATA%\MediaStation\logs\browserhost-tracer-evidence-20261029-194857`.

## Plan Change Log

## Review Triage Log

### 2026-10-29 — Review pass
- verdicts: 26 findings — high 0, medium 3, low 12, false 11, maybe-false 0
- findings:
  - `[medium]` `[patch]` (blind-hunter) A `-p:SkipWebBuild=true` publish no longer ships `page\runtime.js`, because the static `Content` item was removed and the dist item lives only inside `BuildWeb`. This is the documented fallback when Application Control blocks the build (epic-input acceptance P0.2), and with it every web start would fail with `pageAssetMissing`. — Fix: a static `Content` item for `web\page\dist\runtime.js` → `page\runtime.js`, conditioned on `SkipWebBuild == true` and the file existing. Verified with a `SkipWebBuild` publish to the scratchpad: `page\runtime.js` is present and its hash equals the dist file.
  - `[low]` `[reject]` (blind-hunter) `JSON.stringify` of a function/symbol payload drops `payload`, a circular/BigInt payload throws out of `send`, and `reply` marks itself answered before `enqueue`. — Only a driver programming error triggers this, and no driver exists yet (epic-netflix-dpad). Guarding it adds try/branches for a case nobody meets in use.
  - `[low]` `[reject]` (blind-hunter) The 256-message cap can evict `hello` under a flood, with no drop counter. — It needs more than 256 driver messages while the binding is missing (it appears within a few hundred ms of the document start). Pinning `hello` adds complexity for an unrealistic case.
  - `[low]` `[patch]` (blind-hunter) The page and the host disagree on a non-integer `v` (TS `version`, C# `envelope`). — Fix: `parseInbound` returns `invalid` unless `v` is an integer, and the test `v: 1.5 → invalid` was added (envelope tests 20/20). The out-of-Int32 part is the separate row below.
  - `[false]` `[reject]` (blind-hunter) The host → page half (`deliver`, `receive`) is unreachable in the shipped IIFE, and `PageEnvelope` has no consumer. — By design: AD-6 keeps the bridge independent of the transport, and the plan's Design Notes give the transport and the driver's `main.ts` wiring to epic-netflix-dpad. `PageEnvelope` is the record that closes the deferred F6 `id` note, and `Parse` builds it. Nothing misbehaves.
  - `[low]` `[reject]` (blind-hunter) The wrapper template and config keys are duplicated between `BuildInitScript` and `bundle.test.ts`, and `PageConfig` is not generated. — A rename on one side breaks the C# exact-string test or the live tracer (no `pageHello`). A shared cross-language fixture adds machinery for a developer-only risk.
  - `[low]` `[reject]` (blind-hunter) `bundle.test.ts` is excluded from `tsc`. — Vitest still runs it on every `npm test`. Adding `@types/node` and a second tsconfig adds setup for one test file. Recorded in Implementation Notes.
  - `[false]` `[reject]` (blind-hunter) `PageVersionRejected` has no session correlation, so a late event from an old session could fail a new `Starting` launch. — `StopSessionAsync` and the exit/replace paths call `MarkStopping()` before posting Stop results, and `TryMarkVersionRejected` is atomic with it. An old session's post therefore lands before Stop's results in the Orchestrator FIFO, while the app is `Stopping`, and `OnLiveFailure` ignores it there (test `Cdp_lost_in_stopping_is_ignored…` over both facts). The flag is also set at the first `hello` of a document, and the new session injects the same file.
  - `[low]` `[reject]` (blind-hunter) The tracer's F5 step cannot prove F5 caused the new `pageHello`, since a Netflix redirect in the window would also count. — The count is taken after a 3 s settle, and both runs saw exactly one new `hello` 2.3–3.6 s after F5. Tracking navigations adds smoke code for a false-pass that needs a self-navigation within 15 s of an idle page.
  - `[low]` `[reject]` (blind-hunter) `npm run build:page` runs on every build without Inputs/Outputs. — esbuild takes about 100 ms, and `build:shell` already runs unconditionally in Release. Splitting the target for incrementality adds complexity for no user-visible gain.
  - `[low]` `[reject]` (blind-hunter) The rejected version is named `v` in the logs and `version` in the Event contract. — The plan sets `v` (the envelope's field name) in the logs, and the C# record follows naming rules. Cosmetic.
  - `[low]` `[reject]` (blind-hunter) `BindingHost = object` plus a cast weakens typing, and `payload.Clone()` is unnecessary. — Cosmetic. `Clone()` keeps the public `PageEnvelope` valid if it ever escapes `Parse`.
  - `[medium]` `[patch]` (edge-case-hunter) An existing checkout whose `node_modules` predates this change skips `npm ci` and fails at `build:page`: the baseline `package-lock.json` has no `node_modules/esbuild`. — Fix: `npm ci` also runs when `node_modules\esbuild\package.json` is missing. Verified by moving `node_modules\esbuild` aside: `dotnet build` ran `npm ci` (added 59 packages), built the bundle, and reported 0 warnings and 0 errors.
  - `[medium]` `[patch]` (edge-case-hunter) The `SkipWebBuild` publish has no `runtime.js`. — Same root cause and fix as the first blind-hunter row.
  - `[false]` `[reject]` (edge-case-hunter) A stale 6.2 arrow-expression `runtime.js` would be wrapped and never run, failing silently. — Not reachable. Each release unpacks to its own version directory (AD-13/AD-17), and build/publish copy the freshly built dist file with `PreserveNewest`; the publish and tracer hashes equal the dist file. Only a hand-copied old file would do it, and the hotfix contract says the next update overwrites `page\`.
  - `[false]` `[reject]` (edge-case-hunter) A start that replaces the session between the registered check and `MarkStopping` lets a late `v:2` fail the new launch. — Same refutation as the session-correlation row: ordering through `MarkStopping` and the Orchestrator FIFO, plus the same bundle in the new session.
  - `[false]` `[reject]` (edge-case-hunter) A binding call that re-enters `send`/`reply` before `queue.shift()` would duplicate or drop an envelope. — The real binding is the native CDP stub from `Runtime.addBinding`: it queues `Runtime.bindingCalled` for the host and runs no page code, so `flush` cannot be re-entered.
  - `[low]` `[reject]` (edge-case-hunter) A circular, BigInt, function or symbol payload breaks `send`/`reply`. — Same as the first `JSON.stringify` row (driver programming error, no driver yet).
  - `[low]` `[patch]` (edge-case-hunter) A non-integer inbound `v` is classified as `version` on the page. — Same root cause and fix as the TS/C# `v` row (`Number.isInteger`).
  - `[low]` `[reject]` (edge-case-hunter) An integer `v` outside the Int32 range is `envelope` in C#, so no `PageVersionRejected` is posted. — No page sends `v: 3000000000`, and the message is still rejected and logged. A second integer branch adds code for an impossible input.
  - `[false]` `[reject]` (intent-alignment) Reinitialization is unit-tested on two fake windows, not on a real navigation, and SPA `pushState` is not covered. — The live tracer shows a real new document giving a new `pageHello` after F5, and bfcache is handled. An SPA navigation keeps the same document and runtime, so there is nothing to reinitialize; screen changes are the driver's `screen` messages.
  - `[false]` `[reject]` (intent-alignment) Host-side ignoring of a stale reply `id` (AD-7) is not implemented. — No host → page sender exists in this epic, and `Parse` rejects every non-`hello` type as `type`, so the host can never accept a reply, stale or not. The ticket's Verify places stale-id handling under `npm test` (page side), which is covered.
  - `[false]` `[reject]` (intent-alignment) The host tile-error path goes beyond the ticket's Verify. — AD-7's rule ("Host odrzuca inny major `v`: loguje i oznacza kafelek stanem błędu") and DW-54 assign it to the envelope-version story. Nothing misbehaves.
  - `[false]` `[reject]` (intent-alignment) No automated test checks that the artifact lands in `<install>/page/`. — Checked in Verification by building and publishing: published `page\runtime.js` hash equals `web\page\dist\runtime.js` on both passes, and the tracer asserts the banner and records the hash of the published file.
  - `[false]` `[reject]` (intent-alignment) The tracer checks the file on disk, not the injected script. — BrowserHost reads `page\runtime.js` from disk at every start (AD-13). The 6.2 runtime is deleted, and a non-bundle file in the new wrapper would never send `hello`. So the `pageHello` from the published exe comes from the bundle.
  - `[false]` `[reject]` (intent-alignment) `bridge.receive` cannot be reached in the shipped IIFE. — Same as the blind-hunter host → page row: by design per AD-6 and the plan's Design Notes, wired by epic-netflix-dpad.

## Design Notes

- **A real IIFE, with config passed as a function parameter.** `format: 'iife'` without `globalName` leaves no page global. The host wraps the file in a function whose parameter is the config, so the artifact stays a real esbuild IIFE (AD-6). A hotfixed file may end with `;` or a comment, which removes the 6.2 risk of a trailing comment.
- **`bridge.receive` without a transport.** AD-6 makes the bridge independent of the transport. AD-1's CDP list has no host → page call, so how messages reach `deliver` (for example `Runtime.evaluate` or an AD-1 amendment) is a decision for epic-netflix-dpad. This story ships and tests the page-side contract only.
- **Where `id` lives.** The deferred F6 note ("first story with request/response in the page protocol adds `id`") is closed for the page protocol by `PageEnvelope.Id`. The Shell bridge `Envelope<T>` keeps `{ v, type, payload }`, as in AD-16's bridge table, because that bridge has no request/response.
- **Late replies on the page.** No host → page sender exists in this epic, so "ignores late responses" is enforced where replies are produced: a reply to a superseded request of the same type is dropped. Host-side correlation comes with the first sender.
- **A dedicated Event, not `CdpLost`.** The tile ends in the same `Failed` state, but the logs say why (`pageVersionRejectedHandled`), and `CdpLost` keeps meaning "CDP did not come back".

## Verification

**Commands:**
- `npm test` and `npm run typecheck` in `web/` -- expected: all pass (new runtime, bundle and envelope tests).
- `npm run build:page` in `web/` -- expected: `web/page/dist/runtime.js` with the banner.
- `dotnet build MediaStation.slnx -c Release` -- expected: 0 warnings, 0 errors (`BuildWeb` runs `build:page`).
- `dotnet test --solution MediaStation.slnx -c Release --no-build` -- expected: all pass; the pre-existing skip stays.
- `dotnet publish src/MediaStation.App -c Release -r win-x64 --self-contained` -- expected: `publish\page\runtime.js` equals the dist file (`Get-FileHash`).
- `powershell -NoProfile -ExecutionPolicy Bypass -File tools/check-browserhost-tracer.ps1` -- expected: exit 0, with the evidence folder holding screenshots and `measurements.jsonl` (banner, hash, F5 `pageHello`).
- `powershell -NoProfile -ExecutionPolicy Bypass -File tools/check-browserhost-adopt.ps1` -- expected: exit 0 (regression: renew, then F5 `pageHello` with the bundle).

**Manual checks (if no CLI):**
- Screenshots in the tracer evidence folder: Edge covers the monitor after start; the shell is on top after Home.

## Auto Run Result

Status: built (ran on **UM760**: `MEDIASTATION` / `F7BSI` / `EliteMini Series`)

**Summary.** The hand-written 6.2 `runtime.js` is replaced by a vanilla-TS PageRuntime in `web/page/runtime/`. esbuild bundles it into one IIFE with no global name at `web/page/dist/runtime.js`, and `BuildWeb` copies it to `<exe>\page\runtime.js` for every build and publish.
- **Page runtime:** it buffers envelopes until the host binding appears (50 ms retries, 30 s per waiting episode, cap 256), then sends `hello` (type generated from Core's `PageHello`). Each new document gets a fresh runtime, and a bfcache restore sends `hello` again.
- **Driver API:** `bridge.send/receive` for the ServiceDriver, and a transport-agnostic `deliver` with envelope v1 checks: duplicate `id`s and late or second replies are dropped.
- **Host injection:** BrowserHost injects the file verbatim inside `(function (__msPageConfig) {…})(<config>);`.
- **Host protocol:** a `PageEnvelope` record with `id`.
- **Version mismatch:** a page envelope with another major `v` is logged and posts `PageVersionRejected` once per session. The Orchestrator handles it like `CdpLost` (tile `Failed`), and the loop logs `pageVersionRejectedHandled`. This closes DW-54.

**Files changed.**
- **Page runtime and its tests:**
  - `web/page/runtime/envelope.ts`, `bridge.ts`, `runtime.ts`, `main.ts`: the envelope, bridge, `start` and bundle entry.
  - `web/page/runtime/envelope.test.ts`, `bridge.test.ts`, `runtime.test.ts`, `bundle.test.ts`: Matrix rows on fake windows and timers, plus a real esbuild build run in the host's wrapper.
  - `web/page/runtime/runtime.js`: deleted (6.2 stopgap).
- **Web build:**
  - `web/scripts/build-page.mjs`: the esbuild IIFE build (banner, no minify, `--outfile`).
  - `web/package.json`, `package-lock.json`: `build:page`, `build`, and the exact `esbuild` 0.28.2.
  - `web/tsconfig.json`, `vitest.config.ts`: exclude `bundle.test.ts` from `tsc`; update the comment.
- **MSBuild:** `src/MediaStation.App/MediaStation.App.csproj` runs `build:page` and adds the dist `Content` item. **Review patches:** `npm ci` also runs when esbuild is missing, and a `SkipWebBuild` fallback `Content` item was added.
- **Host:**
  - `src/MediaStation.App/Hosts/Browser/BrowserLaunch.cs`: the new `BuildInitScript` wrapper and the `PageConfigParameter` const.
  - `src/MediaStation.App/Hosts/Browser/BrowserHost.cs`: the version-rejection path with a once-per-session post.
- **Core contracts:**
  - `src/MediaStation.Core/Contracts/PageMessages.cs`: `PageEnvelope`, the reason order, and `Version` on the result.
  - `ContractJson.cs`: `PageHello` schema export.
  - `Messages.cs`, `MessageTypes.cs`: `PageVersionRejected`.
- **Orchestrator:** `src/MediaStation.Core/Orchestrator/Orchestrator.cs`, `Orchestrator.Failures.cs` share the `OnLiveFailure` handler; `src/MediaStation.App/Orchestrator/OrchestratorLoop.cs` logs `pageVersionRejectedHandled`.
- **.NET tests:** `tests/MediaStation.Core.Tests/BrowserHostTests.cs`, `CoreMessageContractTests.cs`, `OrchestratorFailureTests.cs`, `OrchestratorLoopTests.cs`.
- **Smoke:** `tools/check-browserhost-tracer.ps1` asserts the banner and hash of the published bundle and requires a new `pageHello` after F5.

**Review findings.** 26 findings: high 0, medium 3, low 12, false 11, maybe-false 0. No intent gaps and no plan defects.
- **Patches (3 entries):**
  - medium: the `SkipWebBuild` publish missed `runtime.js` (blind-hunter and edge-case-hunter);
  - medium: a stale `node_modules` without esbuild skipped `npm ci`;
  - low: a non-integer inbound `v` was classified as `version` on the page.
- **Deferred:** none.
- **Rejected:**
  - low:
    - `JSON.stringify` edge payloads (twice);
    - `hello` evicted by a queue flood;
    - duplicated wrapper/config across languages;
    - `bundle.test.ts` outside `tsc`;
    - the F5 step cannot exclude a self-navigation;
    - `build:page` without Inputs/Outputs;
    - `v`/`version` naming;
    - weak `BindingHost` typing and `Clone()`;
    - an out-of-Int32 `v`.
  - false:
    - the host → page half unreachable (twice, by design);
    - the session-correlation race (twice);
    - a stale 6.2 file;
    - binding re-entrancy;
    - reinit without SPA;
    - host-side stale `id`;
    - the tile-error path beyond Verify;
    - no automated install-dir test;
    - the tracer checking the file on disk.

**Follow-up review recommended:** false. Patched counts: high 0, medium 2, low 1. Two mediums would normally recommend a follow-up, but both fixes were verified on the real path: a stale-`node_modules` build ran `npm ci`, and a `SkipWebBuild` publish shipped the bundle. No specific unverified risk remains.

**Verification (UM760, 2026-10-29, after the patches).**
- `npm test` in `web/`: 14 files, 175 tests passed. `npm run typecheck`: clean. `npm run build:page`: dist with the banner.
- `dotnet build MediaStation.slnx -c Release`, with `web\node_modules\esbuild` moved aside to simulate an old checkout: `npm ci` ran (added 59 packages) and `build-page` ran; 0 warnings, 0 errors; esbuild restored.
- `dotnet test --solution MediaStation.slnx -c Release --no-build`: 1610 total, 1609 passed, 1 skipped (the pre-existing `PowerNotificationsHarnessTests` skip), 0 failed.
- `dotnet publish … -p:SkipWebBuild=true -o <scratchpad>`: exit 0; `page\runtime.js` present.
- `dotnet publish src/MediaStation.App -c Release -r win-x64 --self-contained`: exit 0. `publish\page\runtime.js` starts with the banner. Its SHA256 `E76702243287397A0689862947DCA6D59C09D6F427EF25B622D6078391956590` equals both the dist file and the `SkipWebBuild` publish.
- `tools/check-browserhost-tracer.ps1`: **PASS** (exit 0). Evidence: `%LOCALAPPDATA%\MediaStation\logs\browserhost-tracer-evidence-20261029-195812`.
  - Banner OK.
  - `pageHello {webdriver:false}` 3.1 s after OK.
  - F5 → a new `pageHello` 3.6 s later.
  - After Home the shell is foreground, and Edge is `IsIconic` and visible.
  - No `warn`/`error`.
- Before the patches, same session: tracer PASS (`…-195120`; the screenshots show Edge covering the monitor without browser UI, then the shell after Home) and `tools/check-browserhost-adopt.ps1` PASS (`browserhost-adopt-evidence-20261029-195141`: adopt, renew, F5 `pageHello`). The implementer's live `v:2` hotfix run gave `pageMessageRejected {version, v:2}` → `pageVersionRejectedHandled {state: failed}` → shell (Implementation Notes).

**Residual risks.**
- **No host → page transport yet:** `deliver`/`receive` cannot be reached in production until epic-netflix-dpad picks a transport. AD-1's CDP list has no host → page call, so that epic must decide (e.g. an AD-1 amendment for `Runtime.evaluate`).
- **Host-side stale-reply correlation** (AD-7) arrives with the first host → page sender.
- **Whole-epic acceptance on UM760 (user, `fp-acceptance`):** from a fresh publish, start the Netflix tile with the remote and expect `pageHello webdriver=false`. Reload the page and expect a new `pageHello`. Then press Home: the shell must be on top and Edge minimised but visible.
