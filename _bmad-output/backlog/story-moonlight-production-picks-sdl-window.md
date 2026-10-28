---
id: 13
type: story
title: "Production Moonlight wybiera okno SDL_app"
parent: none
covers: []
after: []
assignee: ""
refined: false
hitl: false
risk: medium
---

# Production Moonlight wybiera okno SDL_app

## Description

Host produkcyjny Moonlight musi wybrać okno strumienia klasy SDL_app, a nie pierwsze widoczne okno procesu. W przeciwnym razie powrót po Home może odtworzyć błąd 5.8.

## Acceptance Criteria

1. **Problem is settled**
   **Given** the reproduction or question from DW-47
   **When** the ticket is implemented or investigated
   **Then** the expected behavior or decision is recorded and verified.
2. **Regression evidence exists**
   **Given** the relevant test or acceptance flow
   **When** it runs
   **Then** it covers the condition from the ledger.

## References

- ledger — _bmad-output/initiative-mediastation-mvp/deferred-work.md, DW-47
- source — _bmad-output/initiative-mediastation-mvp/epic-windows-audio/tickets/bug-moonlight-po-home-i-powrocie-stoi-na-uruchamianie-input-stre-plan.md

## Notes

- Open question: szczegóły rozwiązania rozstrzyga refinement; ten ticket nie zakłada konkretnej implementacji.
