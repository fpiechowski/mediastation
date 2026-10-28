---
title: 'Release publish of MediaStation'
type: 'chore'
ticket: ''
created: '2026-10-28'
status: 'built'
baseline_revision: 'cd8ab31089c89e6a373214a88c8130a6a78fa767'
route: 'oneshot'
route_source: 'auto'
risk: 'low'
review: 'thorough'
review_source: 'pinned'
lenses_ran: []
review_loop_iteration: 0
context: ['mediastation/src/MediaStation.App/MediaStation.App.csproj', 'mediastation/global.json']
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** The user needs the current checkout published as the latest Release artifact.

**Approach:** Publish `src/MediaStation.App` for `win-x64` as a self-contained Release build using the repository's configured .NET 10 SDK.

</frozen-after-approval>

## Implementation Notes

This is a build-only oneshot; no source changes were needed. Verification ran on MEDIASTATION (UM760 target: board F7BSI, model EliteMini Series). `dotnet publish src/MediaStation.App -c Release -r win-x64 --self-contained true` completed successfully in 19.8 seconds. The output contains `MediaStation.exe` (162304 bytes), 424 files total, and product version `1.0.0+cd8ab31089c89e6a373214a88c8130a6a78fa767`. Code review lenses were skipped because no production code or configuration changed.

## Verification

**Commands:**
- `dotnet publish src/MediaStation.App -c Release -r win-x64 --self-contained true` -- expected: exit code 0 and `MediaStation.exe` in the publish directory.
- Inspect the publish directory and executable metadata -- expected: non-empty self-contained Release output produced from the current HEAD.

## Review Triage Log

- skipped -- no production code or configuration diff; artifact and command verification were sufficient for this build-only request.
