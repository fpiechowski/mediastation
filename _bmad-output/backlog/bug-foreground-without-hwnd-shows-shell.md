---
id: 12
type: bug
title: "Home z aplikacji Foreground bez Hwnd przywraca shell"
parent: none
covers: []
after: []
assignee: ""
refined: false
hitl: false
risk: low
severity: P3
---

# Home z aplikacji Foreground bez Hwnd przywraca shell

## Description

Home wysłany, gdy aplikacja jest Foreground, ale nie ma znanego Hwnd, nie wysyła ShowShell. Stan powinien mieć zdefiniowane i przetestowane zachowanie awaryjne.

## Acceptance Criteria

1. **Problem is settled**
   **Given** the reproduction or question from DW-42
   **When** the ticket is implemented or investigated
   **Then** the expected behavior or decision is recorded and verified.
2. **Regression evidence exists**
   **Given** the relevant test or acceptance flow
   **When** it runs
   **Then** it covers the condition from the ledger.

## References

- ledger — _bmad-output/initiative-mediastation-mvp/deferred-work.md, DW-42
- source — _bmad-output/initiative-mediastation-mvp/plan-dw-decision-dw-41-home-in-shell-mode.md

## Notes

- Open question: szczegóły rozwiązania rozstrzyga refinement; ten ticket nie zakłada konkretnej implementacji.
