---
id: 19
type: bug
title: "Kropka na zakładce jest zbyt ściśnięta poziomo"
parent: none
covers: []
after: []
assignee: ""
refined: true
hitl: false
risk: low
severity: P3
---

# Kropka na zakładce jest zbyt ściśnięta poziomo

## Description

Kropka oznaczająca aplikację działającą w tle na zakładce (np. Video) jest już poprawnie wycentrowana w pionie, ale ma zbyt mało miejsca po lewej i prawej stronie. Jej widoczne halo jest zbyt blisko napisu nazwy zakładki i końca tła pigułki. Kropka powinna mieć co najmniej 24 px wolnej przestrzeni od obu tych granic, bez zmiany pionowego wyśrodkowania.

## Reproduction

1. Uruchom MediaStation z aplikacją działającą w tle na jednej z zakładek, np. Video.
2. Otwórz ekran główny i spójrz na zakładkę z kropką.
3. Actual: widoczne halo kropki jest mocno ściśnięte między napisem a prawą krawędzią tła zakładki.
4. Expected: od prawej krawędzi napisu do lewej krawędzi halo oraz od prawej krawędzi halo do prawej krawędzi pigułki są co najmniej 24 px; kropka pozostaje pionowo wyśrodkowana.

## Cause Hypothesis

Poziomy inset przeznaczony na kropkę i halo jest zbyt mały względem szerokości napisu oraz prawego paddingu pigułki. Wcześniejsza korekta pionowego położenia nie rozwiązała geometrii poziomej.

## Acceptance Criteria

1. **Kropka ma co najmniej 24 px przestrzeni po obu stronach**
   **Given** aplikacja działa w tle na zakładce
   **When** ekran główny pokazuje zakładki w stanie nieaktywnym, aktywnym i z fokusem
   **Then** widoczne halo kropki ma co najmniej 24 px odstępu od końca napisu i co najmniej 24 px odstępu od prawej krawędzi tła pigułki
2. **Pionowe wyśrodkowanie i układ zakładki pozostają stabilne**
   **Given** kropka pojawia się albo znika
   **When** aplikacja przechodzi do tła albo się zamyka
   **Then** środek kropki nadal leży na środku wysokości 64 px pigułki, a szerokość pigułki, tekst i położenie pozostałych zakładek nie skaczą
3. **Testy pokrywają odstęp poziomy**
   **Given** testy interfejsu zakładek
   **When** się wykonują
   **Then** test sprawdza geometrię widocznego halo względem obwiedni tekstu i pigułki dla zakładek nieaktywnej, aktywnej i z fokusem, z minimalnym odstępem 24 px; istniejące testy wyśrodkowania pionowego i stanów kropki przechodzą
4. **Albo: nie trzeba zmiany, z dowodem**
   **Given** reprodukcja
   **When** wykonana na bieżącym kodzie
   **Then** odstęp po lewej i prawej stronie kropki jest już wyraźny i zgodny z kryterium, z dowodem zapisanym w Notes — to zastępuje 1–3

## References

- previous bug — _bmad-output/backlog/bug-kropka-w-tle-przy-zakladce-nie-jest-wycentrowana-w-pionie.md, Description and Acceptance Criteria
- design — _bmad-output/initiative-mediastation-mvp/ux-mediastation/DESIGN.md, `components.tab-running-dot` and Components → Tab
- implementation plan — _bmad-output/initiative-mediastation-mvp/plan-dw-tab-running-dot-centred.md, Intent and Implementation Notes

## Boundaries

- Kropka nadal pojawia się wyłącznie dla aplikacji w stanie `Background`.
- Kropka pozostaje poza przepływem, więc jej pojawienie się nie zmienia szerokości zakładki ani położenia innych zakładek.
- Rozmiar 12 px, halo 4 px, wysokość pigułki 64 px, pionowe wyśrodkowanie i stany fokusu pozostają bez zmian.

## Notes

- Decision: minimalny widoczny odstęp od tekstu i od prawej krawędzi pigułki wynosi 24 px, zgodnie ze skalą spacing `24px` w DESIGN.md (2026-10-29).
- Risk: low — zmiana jest kosmetyczna i nie zmienia semantyki ani nawigacji zakładek.
