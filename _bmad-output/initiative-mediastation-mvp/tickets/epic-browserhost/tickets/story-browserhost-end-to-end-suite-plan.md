---
title: 'Story 6.9: BrowserHost end-to-end suite'
type: 'feature'
ticket: '9'
created: '2026-10-29'
status: done
baseline_revision: '7c72f93b80160b1566f26c50046f3db1b2949417'
route: 'full'
route_source: 'auto'
risk: 'high'
review: 'thorough'
review_source: 'pinned'
lenses_ran: [blind-hunter, edge-case-hunter, verification-gap, intent-alignment]
review_loop_iteration: 0
followup_review_recommended: false
context: []
warnings: [oversized]
deferred: []
---

<intent-contract>

## Intent

**Problem:** Stories 6.2–6.7 left five separate smokes (`tools/check-browserhost-{tracer,reconnect,errors,adopt,restart}.ps1`). All of them run against the real Netflix driver, swap the user's `apps.json`, and leave their evidence in five folders. Nothing runs the whole BrowserHost path in one isolated command that writes one report with a pass/fail per epic requirement.

**Approach:** Add one unattended suite runner, `tools/check-browserhost-e2e.ps1`. It copies the published app to a temporary install folder and adds a test-only sample driver (`browserhost-sample`, a public https start page) to that copy. It then runs the five existing smokes against the copy in sequence, with a throwaway profile and port. The smokes get two backward-compatible parameters, `-Service` and `-EvidenceDir`. Finally the runner gathers one report folder: the MediaStation JSON Lines log cut per phase, each smoke's evidence, and a requirement matrix that it evaluates itself from the log and measurements. A `-SelfTest` mode scores a recorded fixture without hardware.

## Boundaries & Constraints

**Always:**
- **Isolation:**
  - The suite never starts the published folder itself. It runs a copy under `%TEMP%\mediastation-browserhost-e2e-<ts>\app`, removed in `finally` unless `-KeepInstall` is passed.
  - It never uses `profiles\netflix` or the `netflix` driver. It refuses to run if the appId is `netflix`.
  - It removes `profiles\browserhost-check` before the first phase and after the last.
  - `apps.json` has the same SHA256 after the run as before, and no `apps.json.restore-bak` is left. The runner checks both and fails the run if either check does not hold.
- **Smokes with default parameters behave exactly as before:** the `netflix` service, the same evidence folder naming, the same assertions. The new parameters only replace the hard-coded `netflix` service (asset check, tile, the tracer's `pageHello.service` check) and the evidence path.
- **Phase failures:** a failing phase does not stop the suite. Every selected phase runs, and the exit code is 0 only when every phase exits 0, every requirement passes and the isolation checks hold.
- **Requirement matrix:** evaluated by the runner from `<phase>\mediastation.jsonl` (the log lines whose `ts` falls inside that phase's UTC window) and `<phase>\measurements.jsonl`. A smoke passing is not enough on its own. Rows:
  - **R-start (tracer):**
    - `appLaunch` (`src` `browser`, args include `--kiosk` and `--remote-debugging-port=`, and no `--remote-allow-origins`);
    - `pageHello` with `webdriver` false and `service` `browserhost-sample`;
    - `browserHostEnabled` present and `devHarnessEnabled` absent.
  - **R-navigation (tracer):** at least 2 `pageHello`, and the `reload` measurement has a non-empty `newPageHello`.
  - **R-reconnect-limit (reconnect):** `cdpLost` plus `cdpLostHandled` with `state` `failed`.
  - **R-reconnect-renew (adopt):** `cdpReconnected` with `trigger` `renewSession`.
  - **R-version (errors):** `browserVersionChanged`.
  - **R-port-in-use / R-profile-in-use / R-cdp-unavailable (errors):** each has a `commandFailed` with that `errorCode`.
  - **R-probe-existing (adopt):** `probeExisting` with `outcome` `adopted`, plus `appAdopted`.
  - **R-restart (restart):** an `appStateChanged` `failed→stopping` with cause `RestartApp`, later followed by `stopping→starting` and `starting→foreground`.
  - **R-no-sw-hide:**
    - the tracer's `after-home` measurement is `iconic` and `visible`;
    - at least one iconic record exists in each of tracer, adopt and restart;
    - every measurement record in every phase that has `iconic` true also has `visible` true.
  - A row whose phase was not selected (`-Phases`) is reported `skipped`. Any skip makes the summary `partial`, not `pass`, and the exit code non-zero.
- **Report:** `%LOCALAPPDATA%\MediaStation\logs\browserhost-e2e-<ts>\` (overridable with `-Out`). It holds:
  - `report.jsonl`, UTF-8 without BOM, with one `phase` record per phase, one `requirement` record per row, one `isolation` record and a final `summary` record. The summary records the machine (`COMPUTERNAME`, board, model), the Edge FileVersion, the install copy path, the sample start URL and the overall verdict;
  - per phase, `<phase>\` (the smoke's evidence: screenshots and `measurements.jsonl`), `<phase>\mediastation.jsonl` and `<phase>\output.txt` (the smoke's console output).
- Each smoke runs as a child `powershell -NoProfile -ExecutionPolicy Bypass -File …` process, matching how the smokes are invoked today. The reconnect phase gets `-ReconnectTimeoutSeconds` from `settings.json` `cdpReconnectTimeout` (default 30).
- **Preflight fails fast with a clear message when:**
  - the published exe or its `page\runtime.js` is missing;
  - MediaStation is running;
  - the sample start URL is unreachable;
  - port 9237 is listening.
- Script comments and messages are in English, matching `tools/check-browserhost-*.ps1`.

**Never:**
- Any change under `src/` or `web/`, or to `DriverManifest` rules (https only), AD-1 flags, contracts or `ErrorCode`.
- Shipping the sample driver in the product publish (no change to `web/page/drivers` or the csproj).
- A GitHub Actions workflow: AD-13 makes `release.yml` the only CI.
- Rewriting the smokes' scenarios or assertions, or extracting their helpers into a shared module. Each phase stays a separately runnable smoke.
- Elevation, certificate stores, firewall or hosts-file changes.
- Editing ticket files or `deferred-work.md`.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Full run | Release publish present, online, nothing running | 5 phases exit 0, all rows `pass`, isolation holds, summary `pass`, exit 0 | — |
| Phase fails | one smoke exits 1 | the remaining phases still run; that phase's record lists the smoke's `FAIL` lines; summary `fail`, exit 1 | — |
| Marker missing | a smoke exits 0 but its slice lacks a row's marker | that row `fail` with a detail naming the missing marker; exit 1 | — |
| Subset | `-Phases tracer,errors` | other rows `skipped`, summary `partial`, exit 1 | — |
| Offline / no publish / MS running / port busy | preflight | no phase runs, `summary` `fail` with the reason, exit 1 | install copy not created or removed |
| `-SelfTest` full fixture | `tools/fixtures/check-browserhost-e2e.fixture.jsonl` | every row `pass`, exit 0 | — |
| `-SelfTest` mutations | each row's markers removed from the fixture in turn | exactly that row fails, while all other rows still pass; any deviation fails the self-test, exit 1 | — |
| Smoke defaults | e.g. `check-browserhost-tracer.ps1` with no new params | unchanged behaviour and evidence folder naming | — |

</intent-contract>

## Code Map

- `tools/check-browserhost-tracer.ps1`:
  - `param` (L25): add `[string]$Service = 'netflix'` and `[string]$EvidenceDir = ''`.
  - Evidence folder (L63): `$EvidenceDir` when given, else the current name.
  - Asset check L176 (`page\drivers\$AppId\manifest.json` → `$Service`), tile L191 (`service`), `pageHello.service` check L243.
  - The header doc gains the two params.
- `tools/check-browserhost-reconnect.ps1`: same changes. `param` L28, evidence L66, tile L206; its asset check sits near the tile.
- `tools/check-browserhost-{errors,adopt,restart}.ps1`: same changes.
  - Locations: errors at L33/L74/L390/L415, adopt at L31/L72/L249/L261, restart at L34/L78/L383/L397.
  - They keep the fixed `browserhost-check` appId, port 9237 and the profile guard.
- The smokes cover these mechanisms; read the scripts to confirm field names and casing before writing matrix predicates:
  - **tracer:** OK → `appLaunch`, `appWindowAttached`, `cdpConnected`, `pageHello`; F5 → new `pageHello` and a `reload` record; Home → `after-home` iconic and visible.
  - **reconnect:** `/json/new` + `/json/close` drop the target → `cdpClosed`, then `cdpLost` at the timeout and `cdpLostHandled failed`.
  - **errors:** A/A2/B write `Last Version` → `browserVersion`/`browserVersionChanged`; C uses a `TcpListener` → `portInUse`; D runs its own msedge on the profile → `profileInUse`; E writes `Local State` → `cdpUnavailable`.
  - **adopt:** kill MediaStation → restart → `probeExisting adopted`/`appAdopted`; a `WM_POWERBROADCAST` resume → `renewSession` → `cdpReconnected trigger=renewSession`.
  - **restart:** `runtime.js` patched to `ENVELOPE_VERSION = 2` in the `-MediaStation` folder → `PageVersionRejected` → Failed; AppMenu OK → `failed>stopping:RestartApp`, …; P5 restores the file.
- Shared helper idioms to copy, not import: `Add-Jsonl` (UTF-8 without BOM), `Note`, `Wait-For`, and the `Get-RunEntries` file opening with `FileShare.ReadWrite`. The P/Invoke `Add-Type` block is not needed in the runner.
- `tools/check-home-s10.ps1`: the `-SelfTest`/`-Fixture` pattern (L43–L98): the same scoring functions serve both the live run and the fixture.
- `src/MediaStation.App/CompositionRoot.cs`:
  - L372: the page directory is `AppContext.BaseDirectory\page`, so a copied install reads its own `page\drivers\<service>\manifest.json`;
  - L47: config, profiles and logs stay under `%LOCALAPPDATA%\MediaStation` (read-only fact, no change).
- `src/MediaStation.Core/Config/DriverManifest.cs` L15–L95 (read-only): fields `service` (must equal the app's service), non-empty `matches`, `startUrl` (absolute https), `driverVersion`.
- `src/MediaStation.Core/Config/ConfigValidation.cs` L280 (read-only): `service` must be kebab-case.
- Publish folder: `src/MediaStation.App/bin/Release/net10.0-windows/win-x64/publish` (~147 MB). Build it with `dotnet publish src/MediaStation.App -c Release -r win-x64 --self-contained`.

## Tasks & Acceptance

**Execution:**
- [x] `tools/samples/page/drivers/browserhost-sample/manifest.json`: add a sample driver. Fields: `service` `browserhost-sample`, `matches` `["https://example.com/*"]`, `startUrl` `https://example.com/`, `driverVersion` `0.0.0`. It gives the suite a non-Netflix service on the real product path.
- [x] `tools/check-browserhost-{tracer,reconnect,errors,adopt,restart}.ps1`: add `-Service` and `-EvidenceDir` as in the Code Map. Defaults change nothing.
- [x] `tools/check-browserhost-e2e.ps1`: the runner.
  - Params: `-MediaStation`, `-Publish` (runs the dotnet publish first), `-Phases` (default `tracer,reconnect,errors,adopt,restart`), `-Out`, `-KeepInstall`, `-SelfTest`, `-Fixture`.
  - Covers preflight, the install copy with the sample driver, the phases, the per-phase log cut, the matrix, the isolation checks, the report and the exit code, all per Always.
  - Header doc in the smokes' style.
- [x] `tools/fixtures/check-browserhost-e2e.fixture.jsonl`: a minimal recorded fixture. Each line is `{ "phase", "source": "log"|"measurement", "record" }` and satisfies every row. `-SelfTest` runs it, then the per-row removal mutations.

**Acceptance Criteria:**
- Given a Release publish on the current machine, when `powershell -NoProfile -ExecutionPolicy Bypass -File tools/check-browserhost-e2e.ps1` runs, then it exits 0. `report.jsonl` then shows:
  - 5 phases with exit 0;
  - every requirement row `pass`;
  - isolation holds;
  - the summary is `pass` with the machine name.
- Given that report folder, when it is inspected, then each phase folder has `mediastation.jsonl`, `measurements.jsonl`, `output.txt` and at least one screenshot. The tracer's `after-home.png` shows the shell, and the restart phase's `p5-foreground.png` shows the sample page.
- Given `-SelfTest`, when it runs, then it exits 0, and its output shows each mutation failing exactly its own row.
- Given the tracer smoke with default parameters, when it runs, then it exits 0 with the `netflix` service as before.
- Given `dotnet build MediaStation.slnx -c Release`, `dotnet test --solution MediaStation.slnx -c Release --no-build` and `npm test` in `web/`, when they run, then they pass, with no change under `src/` or `web/`.

## Implementation Notes

- **Machine:** ran on UM760 (`MEDIASTATION` / `F7BSI` / `EliteMini Series`), Edge 156.0.4314.50, 2026-10-29.
- **Evidence (current machine):**
  - `dotnet build MediaStation.slnx -c Release`: 0 warnings, 0 errors. `dotnet test --solution MediaStation.slnx -c Release --no-build`: 1637 passed, 1 skipped (pre-existing), 0 failed. `npm test` in `web/`: 14 files, 175 tests passed. `dotnet publish src/MediaStation.App -c Release -r win-x64 --self-contained`: exit 0.
  - `check-browserhost-e2e.ps1 -SelfTest`: exit 0; all 11 rows pass on the fixture; 18 mutations (one removal per row plus 7 clause variants) each fail exactly their own row; subset → `partial`; verdict rules and the log window cut are checked too.
  - `check-browserhost-e2e.ps1` (full): exit 0, report `%LOCALAPPDATA%\MediaStation\logs\browserhost-e2e-20261029-222830\report.jsonl`. Phases tracer 15 s, reconnect 38 s, errors 62 s, adopt 16 s, restart 36 s, all exit 0; all 11 rows `pass`; isolation ok (apps.json SHA256 unchanged, no restore-bak, profile and install copy removed); summary `pass` with the machine. Every phase folder holds `mediastation.jsonl`, `measurements.jsonl`, `output.txt` and screenshots; `tracer\after-home.png` shows the shell, `restart\p5-foreground.png` shows example.com in the foreground.
  - `-Phases tracer,errors`: both phases pass, 5 rows `skipped`, summary `partial`, exit 1.
  - Preflight with a missing exe: summary `fail` with the reason, no install copy, exit 1.
  - Phase-failure path observed live (run `browserhost-e2e-20261029-222521`): the errors smoke aborted, the remaining phases still ran, its phase record listed the FAIL line, its three rows failed on the missing markers, and the summary was `fail` (exit 1).
  - `check-browserhost-tracer.ps1` with default parameters: exit 0, `pageHello service=netflix`, apps.json unchanged.
  - `git diff 7c72f93 -- src web`: empty.
- **Additions beyond the listed preflight checks:** the runner also fails preflight on a missing `apps.json`, an orphaned `apps.json.restore-bak` (otherwise the first smoke would overwrite `apps.json`) and a missing msedge.exe (needed for the summary's Edge FileVersion). A phase also fails when its evidence is incomplete (no `measurements.jsonl`, no screenshot or an empty log slice) or it left `apps.json.restore-bak` behind (the runner puts the backup back). Each phase has a 15-minute timeout (kill the process tree, exit code -1).
- **Known flake in the smokes (not changed):** their `Stop-Profile` runs `& taskkill … 2>&1 | Out-Null` under `$ErrorActionPreference = 'Stop'`. Windows PowerShell turns the stderr line "could not be terminated" (a child already exiting) into a terminating error, and that aborts the smoke. It hit the errors phase once in its three suite runs. The runner's own copy goes through `Invoke-TaskKill`, which tolerates this. The smokes keep the old idiom, per the plan's "no rewriting the smokes" constraint.
- **Launch note:** starting Windows PowerShell from `pwsh` with PowerShell 7's `PSModulePath` breaks module autoloading (`Get-FileHash` not found). The runner hashes through .NET; the smokes still call `Get-FileHash`, so launch from a plain console (or clear `PSModulePath`).
- **Orchestrator verification fixes (UM760, 2026-10-29, after the implementer's run):**
  - **Launch environment:** the orchestrator's first re-run (launched from this tool session, whose environment carries PowerShell 7's `PSModulePath`) failed tracer and restart with `Get-FileHash` not recognized (`browserhost-e2e-20261029-223603`). The runner now sets `$env:PSModulePath` to the User + Machine value at the start of the live run, so its smoke children see what a console started from Explorer sees, whatever the parent shell is.
  - **Taskkill flake:** the next re-run failed the errors phase on `ERROR: The process with PID … could not be terminated` (`browserhost-e2e-20261029-223849`), the flake recorded above. A suite that fails one run in three does not meet "CI przechodzi", so the five smokes' `Stop-Profile` now runs taskkill inside `& { $ErrorActionPreference = 'Continue'; … }`. That is cleanup only: no scenario or assertion changed, and defaults behave the same. Repeatable check: a script under `Stop` running `& cmd /c 'echo boom 1>&2' 2>&1 | Out-Null` aborts with the old form and survives with the new one (run and confirmed).
  - **Re-verification after both fixes:**
    - full suite exit 0, `browserhost-e2e-20261029-224125`: 5 phases pass, 11/11 rows pass, isolation ok, summary `pass`. Screenshots `tracer\after-home.png` (shell) and `restart\p5-foreground.png` (example.com in front) were checked;
    - `-SelfTest` exit 0 (18 mutations);
    - tracer with defaults exit 0, `service=netflix` (`browserhost-tracer-evidence-20261029-224433`, run from a clean-`PSModulePath` parent);
    - preflight with a missing exe: `fail` with the reason, exit 1, no install copy;
    - build 0 warnings, `dotnet test` 1637 passed / 1 skipped, `npm test` 175 passed, publish exit 0; `git diff 7c72f93 -- src web` empty.
  - **Matrix audit:**
    - Full run and Smoke defaults: the live runs above.
    - Phase fails: live, `-223849` (errors failed, the remaining phases ran, exit 1) and `-222521`, plus the self-test verdict case.
    - Marker missing: the self-test removal and variant mutations, plus live `-223849` rows failing on missing markers.
    - Subset: the self-test subset case, plus the implementer's live `-Phases tracer,errors` run.
    - Preflight: live missing exe, plus the self-test case.
    - `-SelfTest` full and mutations: the self-test run.
  - **Note for review:** the example.com page itself says "avoid relying on it for testing and monitoring purposes". The suite loads it a handful of times per run, preflight makes an outage explicit, and the Design Notes record why a local page was not possible.

## Plan Change Log

## Review Triage Log

### 2026-10-29 — Review pass
- verdicts: 32 findings — high 0, medium 0, low 20, false 12, maybe-false 0
- findings:
  - `[low]` `[patch]` (blind-hunter) No browser cleanup between phases: a killed or crashed phase can leave Edge on 9237, which cascades into later phases. — Fixed: `Invoke-Phase` now calls `Stop-Profile` after `Stop-InstallProcesses`. Re-verified with a full run (`browserhost-e2e-20261029-225240`, exit 0).
  - `[low]` `[reject]` (blind-hunter) `cdpReconnectTimeout` is passed unchecked to `ValidateRange(5,600)`; the empty catch falls back to 30; a 600 s value is close to the 15 min phase timeout. — `settings.json` validation already caps the value at 600, and a value below 5 is not a realistic setting. 600 s plus about 1 min of setup fits in 15 min. A clamp and a preflight check add guards for a case nobody meets.
  - `[false]` `[reject]` (blind-hunter) R-reconnect-limit does not compare `cdpLost.timeoutSeconds` with the setting. — A phase passes only when the smoke exits 0, and the reconnect smoke asserts `cdpClosed.timeoutSeconds` equals the parameter and that `cdpLost` comes at that timeout ±2 s. A host that ignored the setting fails the phase, so the run fails.
  - `[false]` `[reject]` (blind-hunter) R-start passes with mixed services. — The tracer smoke fails on any `pageHello` with another service, which fails the phase, so the run fails. The row cannot pass alone.
  - `[low]` `[reject]` (blind-hunter) No row asserts that the browser opened the sample start URL. — A host that never navigates gives no initial `pageHello` (the about:blank document predates the init script), so the tracer fails. `restart\p5-foreground.png` shows example.com. An extra URL predicate guards a case the hello gate already catches.
  - `[low]` `[reject]` (blind-hunter) Isolation is not checked against the logs: appLaunch appId, profile and port; `profiles\netflix` and `settings.json` untouched. — Every smoke writes a single-tile `apps.json` with `browserhost-check`/9237 and refuses `netflix`; the runner refuses it too. Nothing in the suite writes `settings.json`. More checks would guard a case no code path reaches.
  - `[false]` `[reject]` (blind-hunter) The matrix filters by appId unevenly and checks no order. — Each phase runs MediaStation with exactly one tile (`browserhost-check`), so the slice holds no other app's facts and no unrelated session.
  - `[low]` `[reject]` (blind-hunter) Reusing `-Out` mixes old and new evidence. — The default is a fresh timestamped folder, and pointing `-Out` at an old report is unusual. A refusal guard adds a branch for that misuse.
  - `[false]` `[reject]` (blind-hunter) Child output is decoded with the wrong code page. — Checked: `tracer\output.txt` of the full run holds correct UTF-8 for `Użytkownik` (bytes C5 BC). Parent and child are both Windows PowerShell on the same console.
  - `[low]` `[reject]` (blind-hunter) `PSModulePath` is changed for the whole session when the script is dot-sourced. — The script is documented and run with `-File`, where the change dies with the process. Dot-sourcing a runner that ends in `exit` is not a supported use.
  - `[low]` `[reject]` (blind-hunter) The self-test skips the live-only logic (evidence rule, argument quoting, FAIL-line parsing, the abort rule). — All of it was exercised live: the phase-failure runs `-223603` and `-223849` parsed FAIL lines and failed phases with missing evidence. Unit seams for PowerShell glue add complexity.
  - `[low]` `[patch]` (blind-hunter) The help text promises one requirement record per row, but a preflight failure writes none. — Fixed: the `.DESCRIPTION` now says requirement records are written only when preflight passed.
  - `[low]` `[reject]` (blind-hunter) Smoke tiles are still titled "Netflix" / `netflix.png`, and help text is out of date. — Cosmetic: the title is not asserted anywhere, and the plan changes only the service. `.PARAMETER EvidenceDir` documents the new path.
  - `[low]` `[reject]` (blind-hunter) `-Service` is not validated in the smokes. — The runner passes a fixed kebab-case value. A manual typo fails at once with "Publish has no page\drivers\<x>\manifest.json".
  - `[low]` `[reject]` (blind-hunter) The taskkill handling is copied five times and differs from the runner's helper. — The smokes copy their helpers by design, and the plan's Never forbids extracting a shared module. The reason is recorded in Implementation Notes.
  - `[false]` `[reject]` (blind-hunter) The diff carries no plan or evidence. — The plan, with machine and evidence, is committed with the change at finalize.
  - `[low]` `[reject]` (edge-case-hunter) `cdpReconnectTimeout` of 1–4, or ≤ `HomeBeforeLimitSeconds`, breaks the reconnect phase. — Same as the second row: an unrealistic setting, and the guard adds branches.
  - `[low]` `[patch]` (edge-case-hunter) A crashed or killed smoke leaves Edge on profile/9237 and failures cascade. — Same root cause and fix as the first row (`Stop-Profile` in `Invoke-Phase`).
  - `[low]` `[reject]` (edge-case-hunter) A throw in output polling or log reading aborts the remaining phases and orphans the child. — The files are opened with ReadWrite/Delete sharing, the same as the smokes, and four full runs read them without error. A try/finally around the poll adds handling for an error never seen.
  - `[false]` `[reject]` (edge-case-hunter) An 8.3 short `%TEMP%` path does not match the long `Process.Path`. — Checked: `GetTempPath()` returned the long form. The report's `installCopy` is `C:\Users\Użytkownik\AppData\Local\Temp\…`, and the leftover-process kill compares the same form.
  - `[low]` `[reject]` (edge-case-hunter) A relative `-Out`/`-Fixture` resolves differently in PowerShell and .NET. — With `-File`, `$PWD` equals the process CWD; it differs only when the script is dot-sourced, which is unsupported.
  - `[low]` `[reject]` (edge-case-hunter) A sample manifest without `service`/`startUrl` makes `$Service` empty. — The manifest is a fixed file committed in the repo, and the self-test reads its `service`. A guard protects against editing your own fixture.
  - `[false]` `[reject]` (edge-case-hunter, claim) `-AppId X` callers now check `drivers\netflix` instead of `drivers\X`. — The old check validated `drivers\$AppId` while the tile always used service `netflix`, so it checked a driver the tile never loaded. Defaults (AppId = service = netflix) check the same file as before.
  - `[low]` `[reject]` (edge-case-hunter, claim) The taskkill change alters default smoke behaviour, against "defaults change nothing". — It is an intended verification fix and recorded in Implementation Notes. Reverting it brings back a 1-in-3 errors-phase abort that fails the ticket's "CI przechodzi". No scenario or assertion changed, and the only other fix is a plan edit.
  - `[low]` `[patch]` (verification-gap) The `-SelfTest` mutations never change only the value a row filters on (`trigger`, `state`, `outcome`, `browserHostEnabled` alone, `webdriver`, `--kiosk`, restart iconic). — Fixed: 7 variant mutations added. The self-test now runs 25 mutations, each failing exactly its own row, and exits 0.
  - `[low]` `[reject]` (verification-gap) `-SelfTest` runs only by hand and is not wired into `dotnet test`. — This matches the repo's habit (`check-home-s10.ps1 -SelfTest` is manual too), and the plan's Verification runs it. A new xUnit test that spawns powershell.exe adds a test surface beyond a direct correction.
  - `[false]` `[reject]` (intent-alignment) C1: no hosted CI pipeline runs the suite. — AD-13 names `release.yml` as the only CI, and the suite needs an interactive desktop. The plan's Design Notes choose C2 on that evidence; the architecture selects the reading.
  - `[false]` `[reject]` (intent-alignment) H1: the diff is the opposite of the named Dev Harness. — `DevHarnessHostPort` bypasses BrowserHost, so under H1 none of the Verify items (CDP, version, port/profile, adoption) could be produced. Only H2 is consistent with the Verify line.
  - `[low]` `[reject]` (intent-alignment) I1: not hermetic (public internet; shared `%LOCALAPPDATA%` root, `apps.json` swapped temporarily). — Recorded design decision. The UM760 probe showed a local https page is impossible without cert trust or admin, and `DriverManifest` accepts only https (no `src/` change). Preflight states the offline case, and `apps.json` is checked unchanged by hash. Going hermetic needs a product change.
  - `[false]` `[reject]` (intent-alignment) S1: "navigation" is only an F5 reload. — A reload is a main-frame navigation that creates a new document, the epic's "każdej nawigacji głównej ramki". The binding/init-script path is the same for any cross-document navigation.
  - `[false]` `[reject]` (intent-alignment) Matrix rows cover less than the epic clauses (loopback-only, `Runtime.enable`, window flag, CdpUnavailable mute/minimize, late `transitionId`, J4 re-failure/Zamknij, R8 Home/D-pad). — A phase passes only when its smoke exits 0, and the smokes assert the rest: tracer loopback/no `--remote-allow-origins`/`--kiosk`; errors E mute/minimize; restart P2/P3 re-failure and Zamknij; reconnect Home during the drop. `Runtime.enable`, late `transitionId` and D-pad buffering are covered by `dotnet test` (1637 passed).
  - `[false]` `[reject]` (intent-alignment) Requirement 5 (PageRuntime) has no matrix row. — The Verify line lists no R5 item. R5 is exercised by R-start/R-navigation (hello, re-init after reload), the tracer's runtime banner check and restart's version rejection, while envelope `v`/`id`/late responses are covered by `npm test` (175 passed).

## Design Notes

- **Why example.com and not a local page:** a UM760 probe on 2026-10-29 (Edge 156.0.4314.50, throwaway profile, Kestrel on `https://localhost:8443` with a self-signed cert) found:
  - Edge shows "Błąd prywatności", both with and without `allow-insecure-localhost` in `Local State`;
  - trusting a cert needs a CurrentUser Root prompt or admin;
  - `DriverManifest` accepts only https, and AD-1 forbids extra flags.

  `https://example.com/` (an IANA reserved documentation domain) keeps the product path unchanged and needs only internet. Preflight makes the offline case explicit.
- **"CI" in the ticket:** read as "one unattended command with an exit code and a report". AD-13 names `release.yml` as the only CI, and the suite needs an interactive desktop (key injection, foreground). The runner is therefore not wired into a hosted pipeline.
- **"Dev harness" in the ticket:** read as this isolated test harness, not `MEDIASTATION_DEV_HARNESS`. `DevHarnessHostPort` bypasses BrowserHost, so it cannot produce CDP, version, port or profile facts. R-start asserts that `devHarnessEnabled` is absent.
- **Reusing the smokes:** the five scripts are the accepted 6.2–6.8 evidence. Parameterizing them keeps one source of truth per scenario, and the runner's matrix adds the cross-phase verdict the ticket asks for.

## Verification

**Commands:**
- `powershell -NoProfile -Command "$env:COMPUTERNAME; (Get-CimInstance Win32_BaseBoard).Product; (Get-CimInstance Win32_ComputerSystem).Model"`: record the machine (planned on UM760: `MEDIASTATION`/`F7BSI`/`EliteMini Series`).
- `dotnet build MediaStation.slnx -c Release`: 0 warnings, 0 errors.
- `dotnet test --solution MediaStation.slnx -c Release --no-build`: all pass; the pre-existing skip is the only one.
- `npm test` in `web/`: passes.
- `dotnet publish src/MediaStation.App -c Release -r win-x64 --self-contained`: exit 0.
- `powershell -NoProfile -ExecutionPolicy Bypass -File tools/check-browserhost-e2e.ps1 -SelfTest`: exit 0.
- `powershell -NoProfile -ExecutionPolicy Bypass -File tools/check-browserhost-e2e.ps1`: exit 0; record the report folder and the requirement table.
- `powershell -NoProfile -ExecutionPolicy Bypass -File tools/check-browserhost-tracer.ps1`: exit 0 (default parameters, `netflix`).
- `git diff --stat 7c72f93b80160b1566f26c50046f3db1b2949417 -- src web`: empty.

**Manual checks (if no CLI):**
- Open the screenshots `tracer\after-home.png` (shell on top) and `restart\p5-foreground.png` (sample page foreground) from the report folder.

## Auto Run Result

Status: built (ran on **UM760**: `MEDIASTATION` / `F7BSI` / `EliteMini Series`, Edge 156.0.4314.50)

**Summary.** One command, `powershell -NoProfile -ExecutionPolicy Bypass -File tools/check-browserhost-e2e.ps1`, now runs the whole BrowserHost path unattended and isolated from the user's setup:
- It copies the Release publish to `%TEMP%` and adds a test-only sample driver (`browserhost-sample` → `https://example.com/`).
- It runs the five existing smokes against the copy, on the throwaway tile `browserhost-check` (port 9237): tracer, reconnect, errors, adopt, restart (J4).
- It cuts the MediaStation JSON Lines log per phase.
- It evaluates its own 11-row requirement matrix: start/hello, navigation, reconnect limit → Failed, RenewSession reconnect, BrowserVersionChanged, PortInUse, ProfileInUse, CdpUnavailable, ProbeExisting, RestartApp, no SW_HIDE.
- It checks isolation: `apps.json` hash unchanged, no leftover backup, profile and copy removed.
- It writes `report.jsonl` plus per-phase evidence.

`-SelfTest` scores a recorded fixture with no hardware; it covers the full fixture and 25 mutations, each failing exactly its own row. Nothing under `src/` or `web/` changed.

**Files changed.**
- `tools/check-browserhost-e2e.ps1` (new): the suite runner, the matrix and `-SelfTest`.
- `tools/fixtures/check-browserhost-e2e.fixture.jsonl` (new): the self-test fixture.
- `tools/samples/page/drivers/browserhost-sample/manifest.json` (new): the test-only sample driver.
- `tools/check-browserhost-{tracer,reconnect,errors,adopt,restart}.ps1`:
  - new `-Service` and `-EvidenceDir` parameters (defaults unchanged);
  - `Stop-Profile`'s taskkill no longer aborts the smoke on a benign stderr line.

**Review findings.** 32 findings: high 0, medium 0, low 20, false 12, maybe-false 0.
- **Patched (3 entries, all low):**
  - per-phase `Stop-Profile` (blind-hunter + edge-case-hunter);
  - the preflight help text;
  - 7 value-variant self-test mutations.
- **Deferred:** none.
- **Rejected, low (17):**
  - **Settings and inputs:**
    - the reconnect timeout range (×2);
    - unvalidated `-Service`;
    - an incomplete sample manifest;
    - reusing `-Out`;
    - relative paths.
  - **Unchecked by the matrix:**
    - the start URL;
    - the isolation-by-log checks.
  - **Runner robustness:**
    - `PSModulePath` when dot-sourced;
    - a throw during output polling;
    - self-test coverage of the live-only glue.
  - **Smoke edits:**
    - the Netflix tile title;
    - the duplicated taskkill idiom;
    - the taskkill change against "defaults change nothing".
  - **Self-test and design choices:**
    - the self-test not wired into `dotnet test`;
    - not hermetic (example.com, shared data root).
- **Rejected, false (12):**
  - matrix rows that the smoke exit gate already covers (timeoutSeconds, mixed services);
  - appId filtering and order;
  - output encoding (verified UTF-8);
  - the diff carrying no plan;
  - 8.3 paths (verified long form);
  - the `-AppId` driver check;
  - the C1, H1 and S1 readings;
  - epic clauses covered by the smokes and unit tests;
  - R5 coverage.
- The reasons are in the Review Triage Log.

**Follow-up review recommended:** false. This was the first pass, and only 3 low entries were patched (patched counts: high 0, medium 0, low 3).

**Verification (UM760, 2026-10-29, after the patches).**
- `dotnet build MediaStation.slnx -c Release`: 0 warnings, 0 errors.
- `dotnet test --solution MediaStation.slnx -c Release --no-build`: 1638 total, 1637 passed, 1 skipped (pre-existing), 0 failed.
- `npm test` in `web/`: 175 passed.
- `dotnet publish src/MediaStation.App -c Release -r win-x64 --self-contained`: exit 0.
- `check-browserhost-e2e.ps1 -SelfTest`: exit 0, with 25 mutations ok.
- `check-browserhost-e2e.ps1`: exit 0. Report `%LOCALAPPDATA%\MediaStation\logs\browserhost-e2e-20261029-225240\report.jsonl`: 5 phases pass, 11/11 rows pass, isolation ok. The previous full pass was `-224125`, whose screenshots `tracer\after-home.png` (shell on top) and `restart\p5-foreground.png` (example.com in front) were checked.
- `check-browserhost-tracer.ps1` with default parameters: exit 0, `service=netflix`, from a clean-`PSModulePath` parent (`browserhost-tracer-evidence-20261029-224433`).
- Preflight with a missing exe: `fail` with the reason, exit 1, no install copy created.
- `git diff 7c72f93 -- src web`: empty.

**Residual risks and handoff.**
- **Internet dependency:** the suite needs `https://example.com/`. That page itself asks not to be relied on for testing; preflight fails clearly when it is unreachable.
- **No hosted CI:** the suite needs an interactive desktop, and AD-13 keeps `release.yml` as the only CI.
- **Launching the standalone smokes from PowerShell 7:** they fail on `Get-FileHash` (PowerShell 7's `PSModulePath`). The runner corrects this for its children.
- **`.bmad-loop/policy.toml`:** a user edit (`left_width = 45`) was uncommitted before this run started. It was left untouched and not committed, so it remains the only modified file.
- **Whole-epic acceptance on UM760 (user, `fp-acceptance`):**
  - run `powershell -NoProfile -ExecutionPolicy Bypass -File tools/check-browserhost-e2e.ps1` from a normal console;
  - check real Netflix by hand: J4 on a Failed Netflix tile, and Edge adopted after a MediaStation restart.
