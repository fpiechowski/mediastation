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

Verify: `dotnet test` przechodzi, a automatyczny smoke na aktualnej maszynie (publish, start, log `snapshotPublished` z `focusRequest{default}`) przechodzi; UM760 nie jest wymagany per ticket; całościowy test UM760 wykonuje użytkownik przy `epic-boundary`.

## References

- parent — _bmad-output/initiative-mediastation-mvp/epic-orchestrator-lifecycle/epic-orchestrator-lifecycle.md

## Notes

- Automated smoke (current machine): powtórzyć start i log `snapshotPublished` z `focusRequest{default}` na buildzie po sweepie; UM760 nie jest wymagany per ticket; całościowy test UM760 wykonuje użytkownik przy `epic-boundary`.
- Automated acceptance: wynik z datą, HEAD i nazwą maszyny trafia do notatki odbioru epiku; UM760 nie jest wymagany per ticket; całościowy test UM760 wykonuje użytkownik przy `epic-boundary`.
