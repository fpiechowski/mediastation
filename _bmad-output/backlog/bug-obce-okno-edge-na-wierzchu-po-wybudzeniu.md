---
id: 24
type: bug
title: "Po wybudzeniu na wierzchu jest obce okno Edge zamiast ekranu głównego"
parent: none
covers: []
after: []
assignee: ""
refined: false
hitl: false
risk: medium
severity: P2
---

# Po wybudzeniu na wierzchu jest obce okno Edge zamiast ekranu głównego

## Description

Po wybudzeniu UM760 na wierzch trafiło okno Edge spoza MediaStation, którego użytkownik nie otwierał, zamiast ekranu głównego. Eliza widzi wtedy stronę „Nowa karta”, a nie swoje kafelki. Po wybudzeniu pilot ma dalej prowadzić do ekranu głównego, bez klawiatury.

## Reproduction

1. Na **UM760** (host `MEDIASTATION`, płyta `F7BSI`, model `EliteMini Series`) uruchom świeży publish bez dev harnessu i otwórz Netflixa z kafelka, a potem wróć na ekran główny.
2. Uśpij komputer (Start → Uśpij) i obudź go po kilkunastu sekundach.
3. Actual: w odbiorze epic-browserhost (2026-10-30, build `b041c5d`, notatka z S19) na wierzchu było okno Edge „Nowa karta — Osobisty” (pid 15620), z `foregroundChanged owner=foreign` o 13:41:06Z. Log MediaStation nie ma `appLaunch`. Agent przywrócił ekran główny narzędziem `A focus`, więc reakcja na Home z pilota w tym stanie nie została sprawdzona.
4. Expected: po wybudzeniu na wierzchu jest ekran główny MediaStation, a jeśli obce okno i tak go zakryje, Home z pilota przywraca ekran główny.

## Cause Hypothesis

Nie wiadomo, kto otworzył to okno. Ma inny pid niż root profilu `netflix` (16736), a przynależności do drzewa nikt nie sprawdził. Może to być Edge przywracający własne okno po wybudzeniu, proces spoza MediaStation albo narzędzia sesji odbioru; objaw wystąpił raz. Ticket nie zakłada konkretnej poprawki.

## Acceptance Criteria

1. **Źródło okna jest ustalone**
   **Given** kilka cykli uśpienia i wybudzenia na UM760, z zapisem procesów Edge przed i po każdym cyklu
   **When** obce okno się pojawi
   **Then** plan wskazuje proces i linię poleceń, które je otworzyły

2. **Home odzyskuje ekran główny nad obcym oknem**
   **Given** po wybudzeniu na wierzchu jest okno spoza MediaStation
   **When** użytkownik wciska Home na pilocie
   **Then** ekran główny wraca na wierzch, a log ma `action home`, `ShowShell completed` i `foregroundChanged owner=shell`

3. **Regresja ma test**
   **Given** poprawka zmienia kod MediaStation
   **When** działają testy .NET
   **Then** test przypina sekwencję wybudzenie → obce okno na wierzchu → Home i oblewa na kodzie przed poprawką

4. **Brak reprodukcji wymaga dowodu**
   **Given** co najmniej 10 cykli na bieżącym kodzie
   **When** obce okno się nie pojawi
   **Then** Notes zawierają zapis tych cykli, a kryterium 2 sprawdza się oknem Edge otwartym celowo przed uśpieniem

## References

- acceptance — `_bmad-output/initiative-mediastation-mvp/tickets/epic-browserhost/epic-browserhost-um760-acceptance.md`, S19–S20, notatka z S19
- architecture — `_bmad-output/initiative-mediastation-mvp/architecture-mediastation/architecture-mediastation.md`, AD-10 (wybudzenie)
- epic — `_bmad-output/initiative-mediastation-mvp/tickets/epic-windows-audio/epic-windows-audio.md`, Home i właściciel okna

## Notes

- Decision: użytkownik zaakceptował epic-browserhost, wiedząc o tym objawie (2026-10-30).
- Weryfikacja: maszynę ustala się przed pracą (reguła „Current machine” w `AGENTS.md`). Kryterium 2 z fizycznym pilotem jest najlepszym wysiłkiem na bieżącej maszynie, a bez pilota sprawdza je PO z użytkownikiem przed `done`.
