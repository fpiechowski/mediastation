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
- Decision: przekazanie Actions z Orchestratora do Shell w trybie `shell` (gałąź w Orchestratorze i komunikat przez most) dostarcza ta epika; epic-orchestrator-lifecycle obsługuje z Actions tylko Home (inception epiki 2, 2026-10-21).
- Waits on epic-foundation-contracts, epic-orchestrator-lifecycle, epic-input because: host Shell, snapshot z `focusRequest`, Actions z `phase: long`.
- Deferred: guard `e.Source` w `ShellBridge` (tylko wiadomości z origin shella) i blokada nawigacji WebView2 poza shell. Źródło: F11 w retro epic-foundation-contracts (`_bmad-output/initiative-mediastation-mvp/tickets/epic-foundation-contracts/epic-foundation-contracts-retrospective.md`); wcześniej triage 1.1 #6 („guard należy do story wprowadzającej Intents”). Warunek: pierwsza story tej epiki, która wysyła Intents z UI przez most (2026-10-20).
- Deferred: lista błędów na ekranie błędu konfiguracji ucina się bez informacji (`overflow: hidden` na scenie 1080 px); brakuje wskaźnika „i jeszcze K” albo innego rozwiązania z UX. Źródło: F17 w retro epic-foundation-contracts (`_bmad-output/initiative-mediastation-mvp/tickets/epic-foundation-contracts/epic-foundation-contracts-retrospective.md`). Warunek: story tej epiki, która dotyka ekranu błędu konfiguracji; gdy żadna, Refactor sweep tej epiki (2026-10-20).
- Deferred: `title` i `icon` w `apps.json` mogą być puste albo z samych spacji. Źródło: F18 w retro epic-foundation-contracts (`_bmad-output/initiative-mediastation-mvp/tickets/epic-foundation-contracts/epic-foundation-contracts-retrospective.md`); retro nie wskazuje epiki dla tych dwóch pól, ta jest ich pierwszym konsumentem. Warunek: story, która renderuje kafelki z `title` i `icon` (2026-10-20).
- Deferred: `ShellBridge` (ścieżki odrzucenia, `bridgeMessageRejected`) oraz `ShellWindow` (`ProcessFailed`, `NewWindowRequested`) nie mają testów ani pełnego manualnego pokrycia. Źródło: R4/N3 w retro epic-foundation-contracts (`_bmad-output/initiative-mediastation-mvp/tickets/epic-foundation-contracts/epic-foundation-contracts-retrospective.md`). Warunek: pierwsza story tej epiki dotykająca mostu albo okna shella dodaje test lub manualny check (2026-10-21).
- Deferred: `NavigationCompleted` loguje `OperationCanceled` jako `navigationFailed`. Źródło: R7/N8 w retro epic-foundation-contracts (`_bmad-output/initiative-mediastation-mvp/tickets/epic-foundation-contracts/epic-foundation-contracts-retrospective.md`). Warunek: pierwsza story tej epiki z większą liczbą nawigacji (2026-10-21).
