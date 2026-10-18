---
type: epic
title: "Home screen"
parent: initiative-mediastation-mvp
covers: [CAP-6]
after: []
assignee: ""
risk: medium
---

# Home screen

## Description

CAP-6: Eliza widzi pełnoekranowy ekran główny w stylu smart TV, z zakładkami Video, Audio i Game, i przechodzi po nim D-padem, z jednym widocznym fokusem i bez kursora. Epika dostarcza React shell w `web/shell` na hoście z epic-foundation-contracts: zakładki z `apps.json`, kafelki ze stanem cyklu życia, kropkę „w tle”, model fokusu kluczowany stabilnymi id i AppMenu.

## Outcome

Pierwsze demo na TV: ekran główny obsługiwany pilotem, zgodny z `DESIGN.md`; sygnałem jest success CAP-6.

## Done when

1. Zakładki i kolejność kafelków wynikają z `category` i kolejności w `apps.json`; Audio pokazuje empty state (AD-11).
2. Kafelek pokazuje stan aplikacji, a kropka na zakładce sygnalizuje aplikację działającą w tle (AD-16).
3. Zimny start, restart i wybudzenie stawiają fokus na pierwszym kafelku Video, a powrót z aplikacji na ostatnim kafelku (AD-16).
4. AppMenu otwiera long-press OK na kafelku `Background` albo `Failed` oraz OK na `Failed` (AD-8).
5. Na TV z UM760 wygląd jest zgodny z `DESIGN.md` w 1920×1080, w title-safe 5%, obsługa D-padem bez kursora.

## Boundaries

`web/shell` i obsługa Intents z UI. Nie: karta „Do oceny” i check „nowa karta nie kradnie fokusu” (epic-do-oceny), Z-order okna shella (epic-windows-audio), przycisk aktualizacji w nagłówku (epic-deploy-resilience; nagłówek ma do niego miejsce w layoucie z DESIGN.md), widgety i tło ze zdjęć (Non-goals).

## References

- parent — _bmad-output/initiative-mediastation-mvp/spec-mediastation/spec-mediastation.md, Capabilities CAP-6
- architecture — _bmad-output/initiative-mediastation-mvp/architecture-mediastation/architecture-mediastation.md, AD-8, AD-11, AD-16
- ux — _bmad-output/initiative-mediastation-mvp/ux-mediastation/EXPERIENCE.md (Information Architecture, Component Patterns, State Patterns, Voice and Tone), _bmad-output/initiative-mediastation-mvp/ux-mediastation/DESIGN.md
- design — _bmad-output/initiative-mediastation-mvp/ux-mediastation/mockups/

## Notes

- Decision: linię success CAP-6 „Nowa karta »Do oceny« nie kradnie fokusu” weryfikuje epic-do-oceny; ta epika dostarcza model fokusu kluczowany stabilnymi id (2026-10-18).
- Waits on epic-foundation-contracts, epic-orchestrator-lifecycle, epic-input because: host Shell, snapshot z `focusRequest`, Actions z `phase: long`.
