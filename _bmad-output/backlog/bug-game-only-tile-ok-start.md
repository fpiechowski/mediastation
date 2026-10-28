---
id: 14
type: bug
title: "Pojedynczy kafelek game uruchamia się po OK"
parent: none
covers: []
after: []
assignee: ""
refined: false
hitl: false
risk: medium
severity: P2
---

# Pojedynczy kafelek game uruchamia się po OK

## Description

Przy apps.json zawierającym tylko kategorię game wstrzyknięte OK loguje action, ale nie uruchamia kafelka. Trzeba odtworzyć przypadek z czystym startem i ustalić, czy winny jest fokus, kategoria czy generowanie konfiguracji.

## Acceptance Criteria

1. **Problem is settled**
   **Given** the reproduction or question from DW-48
   **When** the ticket is implemented or investigated
   **Then** the expected behavior or decision is recorded and verified.
2. **Regression evidence exists**
   **Given** the relevant test or acceptance flow
   **When** it runs
   **Then** it covers the condition from the ledger.

## References

- ledger — _bmad-output/initiative-mediastation-mvp/deferred-work.md, DW-48
- source — _bmad-output/initiative-mediastation-mvp/epic-windows-audio/tickets/bug-moonlight-po-home-i-powrocie-stoi-na-uruchamianie-input-stre-plan.md

## Notes

- Open question: szczegóły rozwiązania rozstrzyga refinement; ten ticket nie zakłada konkretnej implementacji.
