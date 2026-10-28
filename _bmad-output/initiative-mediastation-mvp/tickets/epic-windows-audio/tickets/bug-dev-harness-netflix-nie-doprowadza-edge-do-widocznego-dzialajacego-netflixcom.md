---
id: 16
type: bug
title: "Dev harness Netflix nie doprowadza Edge do widocznego, działającego netflix.com"
parent: epic-windows-audio
covers: [R2, R8]
after: [14]
assignee: ""
refined: true
hitl: false
risk: high
severity: P1
---

# Dev harness Netflix nie doprowadza Edge do widocznego, działającego netflix.com

## Description

W świeżym przebiegu odbioru na UM760 (HEAD 60d6215) z `MEDIASTATION_HARNESS_URL` po OK na kafelku Netflix nie pojawiło się widoczne okno Edge ani materiał. Log zawierał `audioRuleApplied muted=false sessions=0`, ale nie zawierał `appWindowAttached` dla `netflix`. Oczekiwane jest jedno gotowe okno Edge w profilu `profiles\\netflix`, na którym użytkownik widzi `netflix.com` i może rozpocząć odtwarzanie.

## Reproduction

1. Na UM760 uruchomić świeży publish z `MEDIASTATION_DEV_HARNESS=1` i konfiguracją odbioru.
2. Ustawić `MEDIASTATION_HARNESS_URL` na materiał używany do diagnostyki i pozostawić profil `profiles\\netflix`.
3. Nacisnąć OK na kafelku Netflix.
4. Actual: brak widocznego okna lub materiału; brak `appWindowAttached` dla `netflix`; występuje `audioRuleApplied muted=false sessions=0`.
5. Expected: Edge z profilem `netflix` otwiera `netflix.com`, loguje `appWindowAttached`, a materiał jest gotowy do odtworzenia.

## Cause Hypothesis

Błąd może być w przekazaniu URL, użyciu profilu, nawigacji Edge, wyborze okna po starcie albo w tym, że harness uznaje samą sesję procesu za gotowość. Trzeba rozdzielić te etapy w logu i porównać świeży profil, żywy profil oraz scenariusz z niedostępnym URL; ticket nie narzuca konkretnej poprawki.

## Acceptance Criteria

1. **Netflix ma gotowe okno**
   **Given** świeży publish, `MEDIASTATION_DEV_HARNESS=1` i profil `profiles\\netflix`
   **When** użytkownik naciska OK na kafelku Netflix
   **Then** Edge otwiera widoczne okno na `netflix.com`, log zawiera dokładnie jedno `appWindowAttached` dla `netflix`, a kafelek nie przechodzi do „Nie odpowiada”.
2. **Żywy profil jest obsługiwany bez drugiego okna**
   **Given** proces Edge profilu `netflix` już działa i ma okno
   **When** użytkownik naciska OK
   **Then** harness adoptuje to samo okno albo kończy się jednoznacznym wynikiem, bez drugiego procesu dla tego profilu i bez utraty `appWindowAttached`.
3. **Brak gotowości ma rozróżnialną diagnozę**
   **Given** zły profil, niedostępny URL, brak okna albo błąd nawigacji
   **When** start nie osiągnie gotowości
   **Then** log rozróżnia etap i zawiera PID, komendę, argumenty, katalog roboczy, URL oraz przyczynę; nie raportuje sukcesu audio jako substytutu gotowego okna.
4. **Regresja jest pokryta testami**
   **Given** test suite
   **When** uruchamia scenariusze świeżego profilu, żywego profilu i nieudanego startu
   **Then** test reprodukujący obecny brak okna przechodzi po poprawce, a testy nie zmieniają zasad WindowManagera ani Moonlighta.
5. **Albo: brak zmiany jest uzasadniony dowodem**
   **Given** reprodukcja na bieżącym kodzie
   **When** porównanie pokaże, że okno i `netflix.com` są gotowe, a błąd pochodzi z warunków środowiska
   **Then** plan zapisuje dowód, oznacza `Source conflict:` dla przyczyny poza kodem i nie zamyka ticketu bez decyzji PO.

## References

- parent — `_bmad-output/initiative-mediastation-mvp/tickets/epic-windows-audio/epic-windows-audio.md`, R2, R8
- acceptance — `_bmad-output/initiative-mediastation-mvp/tickets/epic-windows-audio/epic-windows-audio-um760-acceptance.md`, S15 i pełny pass z 2026-10-28
- previous fix — `_bmad-output/initiative-mediastation-mvp/tickets/epic-windows-audio/tickets/bug-start-kafelka-netflix-konczy-sie-devharnessstartfailed-zamia.md`
- dependency — `_bmad-output/initiative-mediastation-mvp/tickets/epic-browserhost/epic-browserhost.md`

## Notes

- Decision: ticket 14 pozostaje zamkniętym przypadkiem handoffu istniejącego procesu Edge; ten ticket obejmuje świeży przebieg, w którym nie powstało gotowe okno ani materiał (2026-10-28).
- Risk high: bez tego nie da się rzetelnie wykonać S15–S27 ani ocenić Done when 4.
