---
title: 'Pointer is never swallowed; shell cursor hides only while idle'
type: 'bugfix'
ticket: ''
created: '2026-10-23'
status: 'built'
baseline_revision: '89fcefa67612c2825e3fa2e23b9157f638476b8f'
route: 'oneshot'
route_source: 'auto'
risk: 'medium'
review: 'quick'
review_source: 'pinned'
lenses_ran: ['quick']
review_loop_iteration: 0
context:
  - '{project-root}/_bmad-output/initiative-mediastation-mvp/architecture-mediastation/architecture-mediastation.md'
  - '{project-root}/_bmad-output/initiative-mediastation-mvp/tickets/epic-input/epic-input-um760-acceptance.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** UM760 acceptance of epic-input failed at S13: `pointer: blocked` made `InputPolicy.OnPointer` swallow every mouse event, and the LL hook cannot tell the remote's gyro from a physical mouse, so the user lost their mouse (no emergency exit).

**Approach:** Follow AD-8 as amended 2026-10-23: `OnPointer` never swallows, except a click outside the `clip` rectangle in `osk` (own-injection marker passes). `pointer` stays in the contracts as a cursor hint: the shell hides the cursor only while idle and shows it on mouse movement.

</frozen-after-approval>

## Implementation Notes

Oneshot: about 60 lines over 5 files (InputPolicy, its tests, shell CSS and one listener, docs). Shell cursor hide currently is an unconditional `cursor: none !important` in `web/shell/src/tokens.css`; the `pointer` value is not plumbed to the shell (shell mode is always Blocked), so the idle-hide applies to the shell unconditionally. app+web cursor belongs to the browser page and is out of scope.

## Verification

**Commands:**
- `dotnet test` -- expected: all green (865 before; pointer tests rewritten)
- `npm run build` in `web/` -- expected: success

**Evidence (UM760/MEDIASTATION, 2026-10-23):** `dotnet test tests/MediaStation.Core.Tests -c Release` 866/866 (865 before, +1 osk/Blocked test); `npm run typecheck` and `npm run build` in `web/` pass; `dotnet publish` with `-p:SkipWebBuild=true` succeeds and `publish\shell` refreshed. Physical check (mouse works, cursor hides after 2 s idle) is left to acceptance S1/S13 on UM760 with the user.

## Review Triage Log

- Stale class summary of `InputPolicy` (still said blocked swallows) -- medium, real -- patched.
- Cursor hidden until first mouse move -- false: that is the decided behaviour (movement shows it).
- No build evidence in plan -- low, real -- added above.
- `DESIGN.md:521`, `PRODUCT.md` "bez kursora" -- low, describes the look, not blocking -- rejected.
- `is ButtonDown or ButtonUp` precedence in `OnPointer` -- false: a single pattern, correct.
- `ConfigValidationTests` Blocked for moonlight -- false: explicit `"pointer": "blocked"` in the test JSON.
