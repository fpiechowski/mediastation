---
id: 19
type: bug
title: "Kropka na zakładce jest zbyt ściśnięta poziomo"
parent: none
covers: []
after: []
assignee: ""
refined: false
hitl: false
risk: low
severity: P3
---

# Kropka na zakładce jest zbyt ściśnięta poziomo

## Description

Kropka oznaczająca aplikację działającą w tle na zakładce (np. Video) jest już poprawnie wycentrowana w pionie, ale ma zbyt mało miejsca po lewej i prawej stronie. Przylega optycznie do napisu nazwy zakładki i do końca tła pigułki. Kropka powinna mieć wyraźny, równy oddech od napisu oraz od prawej krawędzi tła, razem z halo, bez zmiany pionowego wyśrodkowania.

## Reproduction

1. Uruchom MediaStation z aplikacją działającą w tle na jednej z zakładek, np. Video.
2. Otwórz ekran główny i spójrz na zakładkę z kropką.
3. Actual: kropka i jej halo są mocno ściśnięte między napisem a prawą krawędzią tła zakładki.
4. Expected: po lewej i prawej stronie kropki pozostaje wyraźna przestrzeń; kropka nadal jest pionowo wyśrodkowana, nie nachodzi na napis i nie przykleja się do końca pigułki.

## Cause Hypothesis

Poziomy inset przeznaczony na kropkę i halo jest zbyt mały względem szerokości napisu oraz prawego paddingu pigułki. Wcześniejsza korekta pionowego położenia nie rozwiązała geometrii poziomej.

## Acceptance Criteria

1. **Kropka ma więcej przestrzeni po obu stronach**
   **Given** aplikacja działa w tle na zakładce
   **When** ekran główny pokazuje zakładki w stanie nieaktywnym, aktywnym i z fokusem
   **Then** kropka ma wyraźny odstęp od końca napisu oraz od prawej krawędzi tła pigułki; halo także mieści się w tym odstępie i nie nachodzi na napis
2. **Pionowe wyśrodkowanie i układ zakładki pozostają stabilne**
   **Given** kropka pojawia się albo znika
   **When** aplikacja przechodzi do tła albo się zamyka
   **Then** środek kropki nadal leży na środku wysokości pigułki, a tekst i położenie pozostałych zakładek nie skaczą
3. **Testy pokrywają odstęp poziomy**
   **Given** testy interfejsu zakładek
   **When** się wykonują
   **Then** test sprawdza, że kropka i jej halo nie nachodzą na tekst ani na krawędź tła, a istniejące testy pionowego wyśrodkowania przechodzą
4. **Albo: nie trzeba zmiany, z dowodem**
   **Given** reprodukcja
   **When** wykonana na bieżącym kodzie
   **Then** odstęp po lewej i prawej stronie kropki jest już wyraźny i zgodny z kryterium, z dowodem zapisanym w Notes — to zastępuje 1–3

## References

- previous bug — _bmad-output/backlog/bug-kropka-w-tle-przy-zakladce-nie-jest-wycentrowana-w-pionie.md, Description and Acceptance Criteria
- design — _bmad-output/initiative-mediastation-mvp/ux-mediastation/DESIGN.md, `components.tab-running-dot` and Components → Tab

## Notes

- Assumption: dokładna wartość poziomego odstępu zostaje dobrana przez implementację w ramach istniejących tokenów i nie zmienia wymagań wizualnych ticketu.
