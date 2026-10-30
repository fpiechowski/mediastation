---
title: 'DW-48: with no Video tile the start focus lands nowhere, so OK starts nothing'
type: 'bugfix'
ticket: ''
created: '2026-10-30'
status: 'built'
baseline_revision: '66acda261fb66ab426663732917007ebb14a15c2'
route: 'oneshot'
route_source: 'auto'
risk: 'medium'
review: 'thorough'
review_source: 'pinned'
lenses_ran:
  - blind-hunter
  - edge-case-hunter
  - verification-gap
  - intent-alignment
review_loop_iteration: 0
followup_review_recommended: false
context: []
warnings: []
deferred:
  - summary: >-
      With no tiles at all, the start screen still has no visible focus, and a new test pins that state.
    evidence: |-
      `{ "apps": [] }` passes config validation. `applyFocusRequest` then leaves zone `tiles` with `focusedAppId: null`, while EXPERIENCE.md and DESIGN.md say focus stays on the tab of an empty state. It predates this change and OK has nothing to start, so only the visible focus is missing; Up still reaches the tabs. The fix would be to set zone `tabs` when there is no tile.
    location: >-
      web/shell/src/focus.ts:81
    severity: low
---

<intent-contract>

## Intent

**Problem:** With only a `game` tile in `apps.json` the injected OK logs `action ok` and starts nothing (DW-48). `applyFocusRequest` in `web/shell/src/focus.ts` resolves `focusRequest default` to the first Video tile only, so with no Video tile the shell stays on the empty Video tab in zone `tiles` with `focusedAppId: null`. The screen then has no visible focus, which breaks "dokładnie jeden fokus" (EXPERIENCE.md), and OK has no target. This is a product bug: anyone whose config has no Video tile gets a dead start screen, and only Up gets out of it.

**Approach:** `default` keeps meaning the first Video tile, and falls back to the first tile of the next non-empty tab in `CATEGORIES` order, activating that tab. Prove it first with a failing shell test and a real-process run of a game-only config, then fix it and repeat both. Record the rule in the docs and correct the stale comment in `New-RestoreAppsJson`. That script's tile stays in `video`: the restore pass does not test the category.

Expected results:

| Config | After `focusRequest default` | OK |
|--------|------------------------------|-----|
| Video and other tiles | First Video tile, as today | `launchApp` on it |
| Game only | Game tab, first game tile | `launchApp` on it |
| Audio and game, no Video | Audio tab, first audio tile | `launchApp` on it |
| No tiles at all | Unchanged: no tile to focus | Nothing, as today |

The same rule applies to a wake, which sends the same `default`. A snapshot with the same `seq` still changes nothing.

</intent-contract>

## Implementation Notes

Route oneshot: about 40 changed lines, mostly a test. Machine: ran on UM760 (host `MEDIASTATION`, board `F7BSI`, model `EliteMini Series`).

Files to change:
- `web/shell/src/focus.ts` -- the fallback in `applyFocusRequest`.
- `web/shell/src/focus.test.ts`, `web/shell/src/Placeholder.test.tsx` -- reproduction and the table above, including exactly one rendered focus.
- `tools/check-web-start.ps1` -- `-Category` parameter (default `video`), so `-Category game` is the repeatable real-process check of a game-only config.
- `tools/check-home-s10.ps1` -- comment in `New-RestoreAppsJson` only.
- `src/MediaStation.Core/Contracts/ShellSnapshot.cs` -- doc comment of `DefaultTarget`.
- `_bmad-output/initiative-mediastation-mvp/ux-mediastation/EXPERIENCE.md`, `change-mediastation/change-mediastation.md` -- the `default` rule.

Not changed: the Orchestrator (it only sends `default`; the target is resolved in the shell), the empty-tab copy, and the zero-tile start.

Also changed: the same `default` rule in `architecture-mediastation.md` (AD-16), which repeats the sentence from `change-mediastation.md`.

**Before the fix, on UM760 (build of `66acda2` plus the tests and the `-Category` parameter):**
- `npx vitest run shell/src/focus.test.ts shell/src/Placeholder.test.tsx`: exit 1, 4 failed, 59 passed. All 4 are the new tests; the state was `activeTab: video`, `focusedAppId: null`.
- Real process with a game-only `apps.json` (pid 22680): log has `action ok/press`, `appLaunch` x0, `appWindowAttached` x0 after 15 s; exit code 0. Screenshot: Video tab active, "Tu jeszcze cisza…", no focus anywhere.
- `tools/check-web-start.ps1 -Category game -AppId dw48-game -DiagnosticUrl about:blank -MediaStation <publish>`: exit 1, `clean` and `leftover` both `No appWindowAttached within 30s`.

**After the fix, same machine, same commands:**
- `npx vitest run`: exit 0, 180 passed. `npm run typecheck`: exit 0.
- `dotnet build MediaStation.slnx -c Release`: exit 0, 0 warnings. `dotnet test MediaStation.slnx -c Release --no-build`: exit 0, 1684 passed, 0 failed, 0 skipped.
- Real process with the same config (pid 18732): `action ok/press`, `appLaunch` x1, `appWindowAttached` x1, no `devHarnessStartFailed`; exit code 0; warnings only `appIconMissing` and `devHarnessEnabled`. Screenshot: Game tab active, the tile has the focus ring.
- `tools/check-web-start.ps1 -Category game` (same arguments): exit 0, both passes, 3 Home/OK cycles each. Without `-Category` (Video tile): exit 0, both passes.
- Afterwards the real `apps.json` is back, no `.restore-bak` is left, and the temporary `dw48-game` profile is removed.

Screenshots are in `%LOCALAPPDATA%\MediaStation\logs\dw48-game-only-evidence\`: `baseline-before-ok.png`, `baseline-after-ok.png`, `fixed-before-ok.png`, `fixed-after-ok.png`. Script evidence is beside it in `web-start-evidence-20261030-154837` (before), `-155119` (game) and `-155208` (video). The capture process saw a 1920×1080 screen while the display runs at 3840×2160, so the images appear to show the top-left quarter at full size; the tabs and the focused tile are inside it.

Limits: the tile in these runs is a `web` tile in `game` under the dev harness, because no Moonlight client is configured in this session. The ledger's tile was `moonlight`, but the fix runs before any host is chosen, and the target is picked by category alone. `check-web-start.ps1` defaults to a publish path with `win-x64`, while a plain `dotnet publish` here writes without it, so `-MediaStation` was passed; this is not new.

**After review patches (final tree, same machine). These replace the counts, pids and folders above.**
- `npx vitest run`: exit 0, 182 passed. `npm run typecheck`: exit 0.
- `dotnet build MediaStation.slnx -c Release`: exit 0, 0 warnings. `dotnet test MediaStation.slnx -c Release --no-build`: exit 0, 1684 passed, 0 failed, 0 skipped.
- Real process with a game-only config (pid 19784): `action ok/press`, `appLaunch` x1, `appWindowAttached` x1, no `devHarnessStartFailed`; app exit code 0; warnings only `appIconMissing` and `devHarnessEnabled`.
- `tools/check-web-start.ps1 -Category game -AppId dw48-game -DiagnosticUrl about:blank -MediaStation <publish>`: exit 0, PASS for category `game` (`web-start-evidence-20261030-155849`). Without `-Category`: exit 0, PASS for `video` (`-155938`). With `-Category Game`: rejected by parameter validation, exit 1, as intended.
- Screenshot `final-before-ok.png`, now captured DPI-aware so it covers the whole screen: Game tab active, the only focus ring on the tile, no empty-state text. `final-after-ok.png` is the post-OK capture. The `baseline-*` and `fixed-*` images keep the quarter-screen limit; the proof that no tile was focused before the fix is the failing render test, which found no `tile--focused` in the markup.
- My throwaway screenshot script (not in the repo) stopped in its cleanup on the last run: `taskkill` reported a child process it could not terminate, so the script did not put `apps.json` back itself. The next `check-web-start.ps1` run restored it from the backup, as that script does for a stale backup. Checked afterwards: `apps.json` has the original three tiles and its 2026-10-28 timestamp, no `.restore-bak` is left, no browser holds the `dw48-game` profile, and the profile is removed.

Review changes against the file list above: `change-mediastation.md` is reverted, because it is an approved historical proposal; `spec-mediastation.md` now carries the rule; the docs use one wording and name the tab order.

## Plan Change Log

## Review Triage Log

### 2026-10-30 — Review pass
- verdicts: 24 findings — high 0, medium 1, low 21, false 2, maybe-false 0
- findings:
  - `[low]` `[patch]` Blind: `spec-mediastation.md:76` keeps the old rule — confirmed; added the fallback there.
  - `[low]` `[patch]` Blind: `EXPERIENCE.md:332` and `:149` keep the old rule — confirmed; `:332` has the fallback, and the `:149` parenthetical is deleted.
  - `[low]` `[defer]` Blind: the zero-tile start has no visible focus and a test pins it — confirmed reachable, since `{ "apps": [] }` validates; it predates this change; recorded in `deferred`.
  - `[low]` `[reject]` Blind: the original Moonlight restore flow was not re-run and nothing schedules it — true, but the target is chosen by category alone before any host runs, and no Moonlight client is set up in this session; named as a UM760 check in the result.
  - `[low]` `[reject]` Blind: the plan's Verification command differs from the commands run, and the bare form uses the `netflix` profile — the default `AppId` and publish path predate this change, and the fix would edit this plan; the exact commands are recorded in the notes.
  - `[low]` `[patch]` Blind: the script does not report its category — the PASS line now names it; the synopsis and an `audio` run are not worth more surface, so they are rejected.
  - `[low]` `[patch]` Blind: quarter-screen screenshots cannot show "no focus anywhere" — confirmed; the final capture is DPI-aware, and the baseline's proof is the failing render test.
  - `[low]` `[patch]` Blind: the reversal of an earlier rejection and the rule's provenance are unrecorded — `story-zakadki-video-audio-i-game-z-category-plan.md:95` did reject this fallback; added DW-48 tags, reverted the in-place edit of the approved `change-mediastation.md`, and named the reversal in the result.
  - `[low]` `[patch]` Blind: tests do not cover the whole table — added the OK intent to the audio row; the open-menu and second-tile cases are rejected, because menu closing is unchanged and already tested, and the new wake test covers tab return.
  - `[low]` `[patch]` Blind: the fallback wording differs between documents and omits the order — unified on "pierwszy kafelek pierwszej niepustej zakładki", with the order in the normative lines.
  - `[low]` `[patch]` Edge: `-Category Game` passes the case-insensitive `ValidateSet` and would become a config error — confirmed by `StrictEnumConverter`'s ordinal lookup; added `IgnoreCase = $false` and checked that the run is rejected.
  - `[low]` `[patch]` Edge: `EXPERIENCE.md:332` contradicts the wake claim — same defect as the second row and the same fix.
  - `[medium]` `[patch]` Verification: no test sends a new `default` while on another tab, so a resolver keeping the current tab would pass — added an `it.each` from the Game tab with Video present and with Audio present.
  - `[low]` `[reject]` Intent: the restore pass in `check-home-s10.ps1` was not run with `game` — the pass still emits `video` and needs Moonlight; the same condition ran through `check-web-start.ps1 -Category game`.
  - `[low]` `[reject]` Intent: real-process evidence uses a `web` tile, not `moonlight` — true and stated; see the fourth row.
  - `[false]` `[reject]` Intent: config generation was not excluded as a suspect — the ledger shows the same generated tile starts in `video`, and a game-only `web` tile failed the same way until the focus fix.
  - `[low]` `[reject]` Intent: committed tests stop at the reducer and static markup — true; `App.tsx` only forwards the intent and has no test harness of its own, and `check-web-start.ps1` exercises the wiring.
  - `[low]` `[reject]` Intent: the script asserts attachment, not focus — attachment after OK cannot happen unless focus reached the tile, and the render test and screenshot cover focus.
  - `[low]` `[reject]` Intent: the video-tile workaround stays although this took the product-bug branch — the restore pass does not test the category, and switching it would change a hardware pass that cannot run here.
  - `[low]` `[reject]` Intent: the rule covers any config without Video, wider than game-only — a game-only special case would leave the same dead screen for audio-only and add a branch; stated in the plan.
  - `[low]` `[patch]` Intent: reversal of the earlier rejection not mentioned — same as the Blind reversal row.
  - `[low]` `[patch]` Intent: wake and update rows in `EXPERIENCE.md` not updated — same as the second row.
  - `[low]` `[defer]` Intent: the one-focus argument does not hold for zero tiles — same as the Blind zero-tile row.
  - `[false]` `[reject]` Intent: plan has `ticket: ''` and the ticket file is unchanged — bundle plans use this form (`plan-dw-dw-power-echo-not-wake.md`), and the orchestrator records resolution.

## Verification

**Commands (from `web/` unless noted):**
- `npm test` -- expected: exit 0; before the fix the new game-only tests fail.
- `npm run typecheck` -- expected: exit 0.
- `dotnet build MediaStation.slnx -c Release` (repo root) -- expected: exit 0, 0 warnings.
- `dotnet test MediaStation.slnx -c Release --no-build` (repo root) -- expected: exit 0, 0 failed.
- `tools/check-web-start.ps1 -Category game` on the published build -- expected: exit 1 with `No appWindowAttached` before the fix, exit 0 after it.
- `tools/check-web-start.ps1` with the default category -- expected: exit 0, no regression.

**Manual checks (if no CLI):**
- A screenshot of the game-only start screen shows the Game tab active with the tile focused. Record its path.

## Auto Run Result

Status: built

**Summary.** DW-48 was a product bug in the shell, not a script artifact. `focusRequest default` looked only for a Video tile, so with no Video tile nothing got focus: the screen showed the empty Video tab with no focus ring, and OK had no target. `default` now goes to the first Video tile as before and, with no Video tile, to the first tile of the first non-empty tab (Video, Audio, Game). A game-only config now opens on the Game tab with the tile focused, and OK starts it.

**Decision to note.** This reverses an earlier rejection: `story-zakadki-video-audio-i-game-z-category-plan.md:95` rejected this fallback because "MVP config always has Video apps". DW-48 and its ticket ask for the behaviour directly, and the change is one line, so the fallback now applies. The rule is recorded in the spec, AD-16 and `EXPERIENCE.md`.

**Files changed.**
- `web/shell/src/focus.ts` -- the fallback.
- `web/shell/src/focus.test.ts`, `web/shell/src/Placeholder.test.tsx` -- regression tests for game-only, audio plus game, wake from another tab, same `seq`, zero tiles, and rendered focus.
- `tools/check-web-start.ps1` -- `-Category` parameter, the repeatable real-process check, and the category in the PASS line.
- `tools/check-home-s10.ps1` -- comment only; the restore pass keeps its `video` tile.
- `src/MediaStation.Core/Contracts/ShellSnapshot.cs` -- doc comment of `DefaultTarget`.
- `spec-mediastation.md`, `architecture-mediastation.md`, `EXPERIENCE.md` -- the `default` rule.

**Review findings.** 24 findings. Patched: 8 entries covering 12 findings (1 medium, 7 low). Deferred: 1 entry covering 2 findings (zero-tile start, low). Rejected: 10, each with its reason in the Review Triage Log: Moonlight flow not re-run; Verification command differs; restore pass not run with `game`; `web` tile evidence; config generation (false); tests stop at the reducer; script asserts attachment; workaround kept; wider scope; `ticket` field (false).

**Follow-up review recommended: false.** This first pass patched 0 high and 1 medium entries.

**Verification (ran on UM760: host `MEDIASTATION`, board `F7BSI`, model `EliteMini Series`).**
- Before the fix: 4 new shell tests failed; the real app logged `action ok/press` with `appLaunch` x0 and `appWindowAttached` x0; `check-web-start.ps1 -Category game` exited 1 with `No appWindowAttached`.
- After the fix: `npx vitest run` exit 0, 182 passed; `npm run typecheck` exit 0; `dotnet build` exit 0, 0 warnings; `dotnet test` exit 0, 1684 passed, 0 failed, 0 skipped.
- Real process, game-only: pid 19784, `appLaunch` x1, `appWindowAttached` x1, app exit 0. `check-web-start.ps1 -Category game`: exit 0. Default category: exit 0.
- Screenshot: `%LOCALAPPDATA%\MediaStation\logs\dw48-game-only-evidence\final-before-ok.png` shows the Game tab active and one focus ring on the tile.

**Residual risks.**
- A game-only `moonlight` tile was not started, and no physical remote was used; the runs used a `web` tile under the dev harness and an injected key. Check on UM760: put only the Moonlight tile in `apps.json` with category `game`, start the app, press OK on the remote and confirm the stream starts; then sleep and wake the machine and confirm focus returns to that tile. This bundle has no parent epic, so the check needs its own slot in your acceptance.
- The wake path with this fallback is covered by shell tests only; no real wake was run.
- A config with no tiles at all still shows no focus; see `deferred`.
- The empty-state text "Tu jeszcze cisza. Muzyka dojdzie wkrótce." still shows on any empty tab, including Video and Game, when the user moves there. This change does not touch it.
