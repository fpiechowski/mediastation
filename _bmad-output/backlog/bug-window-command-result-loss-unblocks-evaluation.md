---
id: 15
type: bug
title: "Utrata wyniku komendy okna nie blokuje cyklu"
parent: none
covers: []
after: []
assignee: ""
refined: false
hitl: false
risk: medium
severity: P2
---

# Utrata wyniku komendy okna nie blokuje cyklu

## Description

Jeśli wynik komendy okna zaginie, Orchestrator pozostaje zablokowany do następnego transitionId. Trzeba zdefiniować zachowanie timeoutu lub odzyskania i pokryć je testem.

## Acceptance Criteria

1. **Problem is settled**
   **Given** the reproduction or question from DW-49
   **When** the ticket is implemented or investigated
   **Then** the expected behavior or decision is recorded and verified.
2. **Regression evidence exists**
   **Given** the relevant test or acceptance flow
   **When** it runs
   **Then** it covers the condition from the ledger.

## References

- ledger — _bmad-output/initiative-mediastation-mvp/deferred-work.md, DW-49
- source — _bmad-output/initiative-mediastation-mvp/epic-windows-audio/tickets/story-pilot-steruje-tym-co-jest-na-wierzchu-takze-po-alt-tab-plan.md

## Notes

- Open question: szczegóły rozwiązania rozstrzyga refinement; ten ticket nie zakłada konkretnej implementacji.
