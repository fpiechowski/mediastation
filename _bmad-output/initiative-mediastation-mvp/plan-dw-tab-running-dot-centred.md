---
title: 'DW-46: wycentruj kropkę „w tle” w zakładce w pionie'
type: 'bugfix'
ticket: ''
created: '2026-10-28'
status: 'built'
baseline_revision: '84cea46eedff30899b8db9afc85d91c23e1b271a'
route: 'oneshot'
route_source: 'auto'
risk: 'low'
review: 'quick'
review_source: 'pinned'
lenses_ran: ['quick']
review_loop_iteration: 0
followup_review_recommended: false
context: []
warnings: []
deferred: []
---

<intent-contract>

## Intent

**Problem:** `.tab-running-dot` in `web/shell/src/tokens.css` sat at `top: 10px; right: 14px`, i.e. pinned near the top-right corner of the Video tab pill; DESIGN.md (dac1625) says it is vertically centred.

**Approach:** Centre it on the pill height (`top: 50%` + `translateY(-50%)`), keep it `position: absolute` (out of flow) and `right: 14px`, so the tab row does not jump. Update any test pinning the position.

</intent-contract>

## Implementation Notes

Oneshot: a two-line CSS change. Ran on UM760 (host MEDIASTATION, board F7BSI). `.tab` is already `position: relative`, height 64px; dot 12px at right 14px lies inside the 32px right padding. No shell test or snapshot pins the dot position (Placeholder.test.tsx only checks presence by tab), so no test change. Files touched: `web/shell/src/tokens.css`.

## Review Triage Log

### 2026-10-28 — Review pass
- verdicts: 0 findings — high 0, medium 0, low 0, false 0, maybe-false 0
- findings: none

## Verification

- `cd web && npx vitest run` -- expected: all pass (118/118 on UM760)

## Auto Run Result

Status: built

- **Summary:** `.tab-running-dot` is now vertically centred on the pill (`top: 50%; transform: translateY(-50%)`), still absolute and `right: 14px`, so the tab row does not jump. Matches DESIGN.md.
- **Files changed:** `web/shell/src/tokens.css` (dot position).
- **Review:** quick lens, 0 findings; no patches, nothing deferred, nothing rejected.
- **Follow-up review recommended:** false.
- **Verification:** ran on UM760 (MEDIASTATION / F7BSI); `npx vitest run` in `web`: 10 files, 118 tests passed. No test pins the position. Visual check on the UM760 screen is left for the user's epic acceptance.
- **Residual risks:** none known; `web/shell/dist` not rebuilt.
