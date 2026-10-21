---
id: 9
type: story
title: "Refactor sweep"
parent: epic-orchestrator-lifecycle
covers: [CAP-2]
after: [1, 2, 3, 10, 4, 5, 11, 6, 7, 8]
hitl: false
risk: low
---

# Refactor sweep

## Description

Sprząta kod epiki: zakresem są findings z dispozycją `defer` w planach stories 1–8, 10 i 11, które dotyczą kodu tej epiki, bez zmiany zachowania; zakres wypchnięty z innych stories nie wchodzi, bo to nowa story.

## Acceptance Criteria

Verify: `dotnet test` przechodzi, a autor powtarza na UM760 skrypty HITL ze stories 1 i 2 (snapshot z `focusRequest{default}` po starcie i po wybudzeniu z Modern Standby), z datowanym wynikiem w notatce odbioru epiku.

## References

- parent — _bmad-output/initiative-mediastation-mvp/epic-orchestrator-lifecycle/epic-orchestrator-lifecycle.md

## Notes

- HITL (autor, UM760): powtórzyć skrypty HITL z Notes stories 1 i 2 na buildzie po sweepie.
- HITL: wynik z datą, HEAD i nazwą maszyny trafia do notatki odbioru epiku; maszyna dev nie spełnia checku bez decyzji autora.
