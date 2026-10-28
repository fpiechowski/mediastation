---
id: 6
type: bug
title: "Test harness dla rejestracji power notifications"
parent: none
covers: []
after: []
assignee: ""
refined: false
hitl: false
risk: low
severity: P3
---

# Test harness dla rejestracji power notifications

## Description

Adapter power notifications ma nieprzetestowane połączenie HWND/WPF i SystemEvents. Potrzebny jest test lub jawny przebieg odbioru, który potwierdzi rejestrację, kopiowanie POWERBROADCAST_SETTING i sprzątanie.

## Acceptance Criteria

1. **Problem is settled**
   **Given** the reproduction or question from DW-1
   **When** the ticket is implemented or investigated
   **Then** the expected behavior or decision is recorded and verified.
2. **Regression evidence exists**
   **Given** the relevant test or acceptance flow
   **When** it runs
   **Then** it covers the condition from the ledger.

## References

- ledger — _bmad-output/initiative-mediastation-mvp/deferred-work.md, DW-1
- source — _bmad-output/initiative-mediastation-mvp/epic-orchestrator-lifecycle/story-systemevents-wybudzenie-z-modern-standby-na-um760-plan.md

## Notes

- Open question: szczegóły rozwiązania rozstrzyga refinement; ten ticket nie zakłada konkretnej implementacji.
