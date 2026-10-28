---
id: 10
type: bug
title: "Home działa przy zawieszonym shellu i oknie-duchu dwm"
parent: none
covers: []
after: []
assignee: ""
refined: false
hitl: false
risk: medium
severity: P2
---

# Home działa przy zawieszonym shellu i oknie-duchu dwm

## Description

Gdy zawieszony shell jest zasłonięty oknem-duchem dwm, fizyczny Home nie daje keyEnter/action i otwiera przeglądarkę. Należy odtworzyć problem z prawdziwym pilotem i ustalić poprawne zachowanie hooka.

## Acceptance Criteria

1. **Problem is settled**
   **Given** the reproduction or question from DW-31
   **When** the ticket is implemented or investigated
   **Then** the expected behavior or decision is recorded and verified.
2. **Regression evidence exists**
   **Given** the relevant test or acceptance flow
   **When** it runs
   **Then** it covers the condition from the ledger.

## References

- ledger — _bmad-output/initiative-mediastation-mvp/deferred-work.md, DW-31
- source — _bmad-output/initiative-mediastation-mvp/epic-input/tickets/bug-check-home-s10-home-przy-zawieszonym-shellu-i-cel-edge-nie-d-plan.md

## Notes

- Open question: szczegóły rozwiązania rozstrzyga refinement; ten ticket nie zakłada konkretnej implementacji.
