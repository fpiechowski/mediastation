---
id: 8
type: story
title: "Kalibracja nie kasuje równoległych usages przy key-up"
parent: none
covers: []
after: []
assignee: ""
refined: false
hitl: false
risk: low
---

# Kalibracja nie kasuje równoległych usages przy key-up

## Description

Przy trzymaniu dwóch przycisków pilota puszczenie jednego nie może wyczyścić deduplikacji drugiego. Kalibracja ma prowadzić stan per VK i usage.

## Acceptance Criteria

1. **Problem is settled**
   **Given** the reproduction or question from DW-24
   **When** the ticket is implemented or investigated
   **Then** the expected behavior or decision is recorded and verified.
2. **Regression evidence exists**
   **Given** the relevant test or acceptance flow
   **When** it runs
   **Then** it covers the condition from the ledger.

## References

- ledger — _bmad-output/initiative-mediastation-mvp/deferred-work.md, DW-24
- source — _bmad-output/initiative-mediastation-mvp/epic-input/epic-input-retrospective.md, A8

## Notes

- Open question: szczegóły rozwiązania rozstrzyga refinement; ten ticket nie zakłada konkretnej implementacji.
