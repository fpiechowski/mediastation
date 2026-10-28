---
id: 9
type: story
title: "Kalibracja obsługuje nową sygnaturę pilota"
parent: none
covers: []
after: []
assignee: ""
refined: false
hitl: false
risk: medium
---

# Kalibracja obsługuje nową sygnaturę pilota

## Description

Tryb --calibrate-input nie może milczeć tylko dlatego, że nowy pilot ma inną sygnaturę VID/PID niż input.json. Należy ustalić i wdrożyć bezpieczny sposób wykrycia oraz zapisania nowej sygnatury.

## Acceptance Criteria

1. **Problem is settled**
   **Given** the reproduction or question from DW-27
   **When** the ticket is implemented or investigated
   **Then** the expected behavior or decision is recorded and verified.
2. **Regression evidence exists**
   **Given** the relevant test or acceptance flow
   **When** it runs
   **Then** it covers the condition from the ledger.

## References

- ledger — _bmad-output/initiative-mediastation-mvp/deferred-work.md, DW-27
- source — _bmad-output/initiative-mediastation-mvp/deferred-work.md, DW-27

## Notes

- Open question: szczegóły rozwiązania rozstrzyga refinement; ten ticket nie zakłada konkretnej implementacji.
