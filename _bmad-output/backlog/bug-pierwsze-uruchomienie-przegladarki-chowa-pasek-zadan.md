---
id: 18
type: bug
title: "Ponowne wejście do przeglądarki pokazuje pasek zadań Windows"
parent: none
covers: []
after: []
assignee: ""
refined: true
hitl: false
risk: medium
severity: P2
---

# Ponowne wejście do przeglądarki pokazuje pasek zadań Windows

## Description

Przy pierwszym uruchomieniu aplikacji webowej, na przykład Netflixa, przeglądarka zajmuje cały ekran i pasek zadań Windows nie jest widoczny. Po powrocie do Home i ponownym wejściu do tej samej aplikacji pasek zadań staje się widoczny, więc aplikacja nie wraca do tego samego trybu okna. Każde wejście do aplikacji powinno zachować tryb pełnego ekranu ustalony dla pierwszego uruchomienia.

## Reproduction

1. Uruchom MediaStation na **UM760** (host `MEDIASTATION`, płyta `F7BSI`, model `EliteMini Series`) z kafelkami zawierającymi aplikację webową, np. Netflix.
2. Z Home wybierz Netflix i poczekaj, aż przeglądarka się otworzy.
3. Actual po pierwszym uruchomieniu: okno przeglądarki zajmuje cały ekran, a pasek zadań Windows jest niewidoczny.
4. Wróć pilotem do Home.
5. Ponownie wybierz Netflix i poczekaj na aktywację tej samej aplikacji.
6. Actual po ponownym wejściu: pasek zadań Windows jest widoczny przy aktywnej przeglądarce.
7. Expected: pierwsze i kolejne wejścia mają ten sam tryb okna; pasek zadań pozostaje niewidoczny, a treść aplikacji pokrywa obszar ekranu.

## Cause Hypothesis

Ścieżka ponownej aktywacji może przywracać albo maksymalizować okno inną operacją niż ścieżka pierwszego uruchomienia. Taka różnica może wyprowadzać przeglądarkę z ustalonego trybu kiosk/fullscreen albo pozwalać paskowi zadań przejąć widoczność. Hipoteza obejmuje także kolejność operacji przy Home, minimalizacji i ponownym przywróceniu; ticket nie zakłada konkretnej poprawki.

## Acceptance Criteria

1. **Tryb okna jest spójny po pierwszym i kolejnym uruchomieniu**
   **Given** aplikacja webowa jest uruchamiana z Home po raz pierwszy
   **When** aplikacja zostanie otwarta, zamknięta przez Home i otwarta ponownie
   **Then** drugie i każde następne wejście ma ten sam pełnoekranowy tryb okna co pierwsze wejście, a pasek zadań Windows pozostaje niewidoczny

2. **Powrót przez Home nie psuje następnej aktywacji**
   **Given** aktywna przeglądarka została opuszczona przez Home
   **When** ta sama aplikacja zostanie ponownie wybrana z Home
   **Then** okno aplikacji wraca na pierwszy plan bez widocznego paska zadań i bez odsłonięcia pulpitu przy dolnej krawędzi

3. **Regresja jest pokryta testem i sprawdzeniem okna**
   **Given** zestaw testów hosta przeglądarki i zarządzania oknami
   **When** testy uruchamiają sekwencję pierwsze uruchomienie → Home → ponowne wejście
   **Then** test sprawdza sekwencję pierwsze uruchomienie → Home → ponowne wejście, a weryfikacja okna potwierdza ten sam obszar pokrycia ekranu i brak widocznego paska zadań dla obu aktywacji; test reprodukuje błąd na kodzie przed poprawką

4. **Brak zmiany wymaga dowodu**
   **Given** dokładna reprodukcja z UM760
   **When** zostanie wykonana na bieżącym kodzie
   **Then** jeśli pasek zadań nie pojawia się po ponownym wejściu, Notes zawierają logi lub zrzuty potwierdzające, że zgłoszenie nie występuje; to kryterium zastępuje 1–3

## References

- parent — none; standalone bug in `_bmad-output/backlog/`
- architecture — `_bmad-output/initiative-mediastation-mvp/tickets/epic-browserhost/epic-browserhost.md`, Done when 1 and Notes (tryb `--kiosk`)
- architecture — `_bmad-output/initiative-mediastation-mvp/tickets/epic-windows-audio/epic-windows-audio.md`, Requirements R1–R4 and R7 (właściciel okna, minimize/restore)
- measurement — `_bmad-output/initiative-mediastation-mvp/tickets/epic-browserhost/tickets/spike-tryb-okna-przegladarki-i-zrodo-wersji-poza-cdp-plan.md`, Rozstrzygnięcie trybu okna i tabela pomiarów `--kiosk`

## Notes

- Decision (2026-10-28): „fullscreen” oznacza produktowy tryb `--kiosk` ustalony dla BrowserHosta, a nie pełny ekran odtwarzacza wywołany klawiszem `f`.
- Decision (2026-10-28): zakres obejmuje żywy proces przeglądarki po `Home`; ponowny start po zamknięciu procesu jest osobnym przypadkiem tylko wtedy, gdy implementacja używa tej samej ścieżki.
- Decision (2026-10-28): pasek zadań jest uznany za widoczny, gdy pozostaje odsłonięty po ustabilizowaniu okna, bez celowego ruchu myszy do krawędzi ekranu.
- Verification boundary: automatyczne testy i dev harness sprawdzają stan oraz geometrię okna; użytkownik wykonuje produktową weryfikację na UM760 w odbiorze całej epiki.
