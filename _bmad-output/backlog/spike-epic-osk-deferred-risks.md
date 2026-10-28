---
id: 16
type: spike
title: "Decyzje i testy wejścia dla epic-osk"
parent: none
covers: []
after: []
assignee: ""
refined: false
hitl: false
risk: medium
---

# Decyzje i testy wejścia dla epic-osk

## Description

Przed startem epic-osk trzeba rozstrzygnąć reset stanu hooka, realne SendInput, DPI/clip, bramkę Keyboard przez textFocus, degenerację GetWindowRect i podział InputHub/InputPolicy. Wynik ma trafić do refinementu epiki.

## Acceptance Criteria

1. **Problem is settled**
   **Given** the reproduction or question from DW-6/DW-10/DW-11/DW-17/DW-22/DW-25
   **When** the ticket is implemented or investigated
   **Then** the expected behavior or decision is recorded and verified.
2. **Regression evidence exists**
   **Given** the relevant test or acceptance flow
   **When** it runs
   **Then** it covers the condition from the ledger.

## References

- ledger — _bmad-output/initiative-mediastation-mvp/deferred-work.md, DW-6/DW-10/DW-11/DW-17/DW-22/DW-25
- source — _bmad-output/initiative-mediastation-mvp/deferred-work.md, DW-6, DW-10, DW-11, DW-17, DW-22, DW-25

## Notes

- Open question: szczegóły rozwiązania rozstrzyga refinement; ten ticket nie zakłada konkretnej implementacji.
