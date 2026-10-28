---
title: "Moonlight uruchomiony przez MediaStation zawiesza się na ustanawianiu połączenia"
ticket: 12
status: blocked
blocked_at: "2026-10-27"
blocked_reason: "dirty working tree: .bmad-loop/policy.toml has an uncommitted change (left_width = 40)"
---

## Auto Run Result

Status: blocked
Blocking condition: dirty working tree. `.bmad-loop/policy.toml` has an uncommitted change (`left_width = 40` added under the TUI section). The repo was clean at session start, so the change appeared during the run, most likely written by the bmad-loop TUI. No implementation work was started.
To resume: commit or revert that line, then re-run ticket 5.12.
