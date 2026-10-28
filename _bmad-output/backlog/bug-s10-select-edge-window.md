---
id: 11
type: bug
title: "S10 wybiera właściwe okno Edge"
parent: none
covers: []
after: []
assignee: ""
refined: false
hitl: false
risk: low
severity: P3
---

# S10 wybiera właściwe okno Edge

## Description

check-home-s10.ps1 może wybrać obce okno msedge, np. DevTools lub PWA. Test ma pozwalać wskazać dokładne okno celu i nie mierzyć innego procesu/tytułu.

## Acceptance Criteria

1. **Problem is settled**
   **Given** the reproduction or question from DW-32
   **When** the ticket is implemented or investigated
   **Then** the expected behavior or decision is recorded and verified.
2. **Regression evidence exists**
   **Given** the relevant test or acceptance flow
   **When** it runs
   **Then** it covers the condition from the ledger.

## References

- ledger — _bmad-output/initiative-mediastation-mvp/deferred-work.md, DW-32
- source — _bmad-output/initiative-mediastation-mvp/epic-input/tickets/bug-check-home-s10-home-przy-zawieszonym-shellu-i-cel-edge-nie-d-plan.md

## Notes

- Open question: szczegóły rozwiązania rozstrzyga refinement; ten ticket nie zakłada konkretnej implementacji.
