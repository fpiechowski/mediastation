---
id: 20
type: story
title: "BrowserHost i jego testy są podzielone na pliki tematyczne bez zmiany zachowania"
parent: none
covers: []
after: []
assignee: ""
refined: false
hitl: false
risk: medium
---

# BrowserHost i jego testy są podzielone na pliki tematyczne bez zmiany zachowania

## Description

`BrowserHost.cs` ma 1752 linie, 42 metody i klasę `Session`, a `BrowserHostTests.cs` ma 4059 linii. Każda story epiki 6 dokładała do nich 100–500 linii, a epic-netflix-dpad ma rozszerzać ten sam kod. Ta story rozdziela launch i preflight, ustawienie CDP, reconnect, probe i adopcję, renew, stop oraz sesję na osobne pliki, zanim ta praca się zacznie. To czysty refactor.

## Requirements

1. **Zachowanie zostaje bez zmian**
   **Given** zestaw testów `BrowserHostTests` sprzed podziału (nazwy i liczba przypadków zapisane w planie)
   **When** podział jest gotowy
   **Then** każdy z tych testów istnieje pod tą samą nazwą i przechodzi, a jego asercje są niezmienione
   **And** cały build Release ma 0 ostrzeżeń, testy solution i `npm test` w `web/` przechodzą, a jedyny pominięty test to ten sprzed tej story

2. **Żywe ścieżki dają te same dowody**
   **Given** opublikowany build Release na bieżącej maszynie
   **When** uruchamiają się smoke'i tracer, reconnect, errors, adopt i restart oraz `tools/check-browserhost-e2e.ps1 -SelfTest`
   **Then** każdy kończy się kodem 0, a restart nadal loguje `stopping>starting:ProcessExited` w P2 i P5

3. **Każdy temat ma własny plik**
   **Given** kod hosta przeglądarki po podziale
   **When** ktoś szuka reconnectu, probe i adopcji albo sesji
   **Then** każdy z tych tematów leży w osobnym pliku nazwanym od tematu, a testy są podzielone według tych samych tematów
   **And** żaden plik kodu hosta nie przekracza 600 linii, a żaden plik testów 1000 linii

4. **Kontrakty zostają bez zmian**
   **Given** diff tej story
   **When** ktoś porównuje go z punktem wyjścia
   **Then** nie zmienia się żaden Command, Event, `ErrorCode`, schemat, klucz logu ani nic w `web/`

## Boundaries

- Must not change: reguły AD-1 (w tym lista zakazanych metod CDP), jedno zgłoszenie `ProcessExited` na proces (DW-3), kolejność blokad host → sesja z 6.8.
- Must not change: świadome skutki uboczne hosta, np. usuwanie `__COMPAT_LAYER` ze środowiska Edge.
- Poza zakresem: DW-56, DW-58, nowe funkcje, skrypty smoke (osobna story: wspólny moduł skryptów smoke).

## References

- source — _bmad-output/initiative-mediastation-mvp/tickets/epic-browserhost/epic-browserhost-retrospective.md, F1, A2
- parent — _bmad-output/initiative-mediastation-mvp/tickets/epic-browserhost/epic-browserhost.md, Requirements 1–6
- architecture — _bmad-output/initiative-mediastation-mvp/architecture-mediastation/architecture-mediastation.md, AD-1, AD-4, AD-10; Consistency Conventions, „Testy kodu App”
- precedent — _bmad-output/initiative-mediastation-mvp/deferred-work.md, DW-19 (podział Orchestratora w 4.2)

## Notes

- Decision: story leży w backlogu, poza epikiem 6, bo ten czeka na odbiór na UM760 (użytkownik, 2026-10-30).
- Decision: ta story jest warunkiem wejścia w epic-netflix-dpad (użytkownik, 2026-10-30; szczegóły w Notes tej epiki).
- Assumption: budujemy ją po odbiorze epiki 6, żeby odbiór dotyczył kodu z retro, a poprawki z odbioru weszły przed podziałem.
- Assumption: limity 600 i 1000 linii to propozycja PO, żeby kryterium 3 dało się sprawdzić; retro nie podaje liczb.
- Weryfikacja: maszynę ustala się przed pracą (reguła „Current machine” w `AGENTS.md`). Na maszynie bez Edge lub ekranu smoke'i oznacza się jako nie dotyczy z powodem. Pełne sprawdzenie zostaje w odbiorze.
