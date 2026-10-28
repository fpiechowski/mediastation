---
title: 'dw-s10-select-edge-window: S10 picks the right Edge window'
type: 'bugfix'
ticket: ''
created: '2026-10-28'
status: 'built'
baseline_revision: 'b2a60e8d3245be523a253eb1ea2b3f782816c2e8'
route: 'oneshot'
route_source: 'auto'
risk: 'low'
review: 'quick'
review_source: 'pinned'
lenses_ran: [quick]
review_loop_iteration: 0
followup_review_recommended: false
context: []
warnings: []
deferred: []
---

<intent-contract>

## Intent

**Problem:** DW-32: `Select-EdgeWindow` in `tools/check-home-s10.ps1` takes the first titled `msedge` window, which can be a DevTools or PWA window of the same process, so Home is sent over the wrong window.

**Approach:** Without `-EdgeTitle` the default target is narrowed deterministically by title rules (not DevTools, title ends with "Microsoft Edge"); an explicit `-EdgeTitle` replaces the rules. `-SelfTest` covers it.

</intent-contract>

## Implementation Notes

Oneshot: a single function with a few title rules plus fixture-free SelfTest cases (~25 lines).

- `tools/check-home-s10.ps1`: in the non-tree branch of `Select-EdgeWindow`, when `-TitlePattern` is empty skip titles matching `^DevTools(\s|$)` and titles not ending with `Microsoft Edge`. Tree-based selection (restore pass) is unchanged. Help text updated.
- SelfTest: new cases for DevTools, PWA ("Spotify"), a real multi-page title, explicit `-EdgeTitle` overriding the rules; the existing "owned windows" case now passes `-TitlePattern` because its fixture title is not an Edge title.
- Limitation: a PWA whose title ends in "Microsoft Edge" is indistinguishable by title; `-EdgeTitle` stays the explicit way to pin the target.

## Review Triage Log

### 2026-10-28 — Review pass
- verdicts: 5 findings — high 0, medium 1, low 0, false 0, maybe-false 0 (3 rejected as low/non-defect)
- findings:
  - `[medium]` `[patch]` Title rule needs ASCII space; Edge titles carry U+200B in "Microsoft​ Edge" — fixed: regex `Microsoft[\s​]*Edge\s*$`, SelfTest fixture 3004 now uses the zero-width space.
  - `[low]` `[reject]` `^DevTools` clause overlaps the suffix rule — only adds "DevTools - Microsoft Edge"; harmless, kept as explicit intent.
  - `[low]` `[reject]` No machine/evidence in plan — recorded below in Auto Run Result.
  - `[low]` `[reject]` Multi-page fixture hand-typed — acceptable for a decision-case SelfTest.
  - `[low]` `[reject]` Windows without the suffix are skipped silently — `-EdgeTitle` is the documented override; a log note would add complexity.

## Verification

**Commands:**
- `powershell -NoProfile -ExecutionPolicy Bypass -File tools/check-home-s10.ps1 -SelfTest` -- expected: `Self-test passed.`

## Auto Run Result

Status: built

- Summary: default Edge target no longer picks DevTools or app/PWA windows; titles must end with "Microsoft Edge" (zero-width space tolerated) and not be DevTools, unless `-EdgeTitle` is given (it replaces the rules). Resolves DW-32.
- Files: `tools/check-home-s10.ps1` (selector rule, help text, SelfTest cases); this plan.
- Review: 1 patch applied (zero-width space in the title), 0 deferred, 4 rejected (see triage log). Follow-up review recommended: false.
- Verification (ran on UM760, host MediaStation, board F7BSI): `check-home-s10.ps1 -SelfTest` -> `Self-test passed.`
- Residual risk: a PWA titled "... Microsoft Edge" is indistinguishable by title; use `-EdgeTitle`. Live Edge run not performed (hardware pass is the user's epic acceptance).
