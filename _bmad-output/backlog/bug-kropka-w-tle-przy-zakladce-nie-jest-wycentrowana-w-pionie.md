---
id: 5
type: bug
title: "Kropka „w tle” przy zakładce nie jest wycentrowana w pionie"
parent: none
covers: []
after: []
assignee: ""
refined: false
hitl: false
risk: low
severity: P3
---

# Kropka „w tle” przy zakładce nie jest wycentrowana w pionie

## Description

Gdy w zakładce (np. Video) działa aplikacja w tle, przy nazwie zakładki pojawia się kropka. Siedzi w prawym górnym rogu pigułki zakładki. Użytkownik chce, żeby była wycentrowana w pionie względem pigułki, nadal po prawej stronie i bez przesuwania napisu.

## Source

Odbiór epiki 5 na UM760, 2026-10-27, S3, uwaga U4 (`_bmad-output/initiative-mediastation-mvp/tickets/epic-windows-audio/epic-windows-audio-um760-acceptance.md`); ledger DW-46. Dotyczy ekranu głównego z epiki 4 (`epic-home-screen`, zamknięta), więc ticket jest w backlogu.

## Reproduction

1. Uruchom MediaStation z dev harnessem, OK na kafelku Netflix, potem Home.
2. Spójrz na zakładkę Video na ekranie głównym.
3. Actual: kropka przyklejona do górnego prawego rogu pigułki (stały odstęp od góry). Expected: środek kropki na środku wysokości pigułki, po prawej stronie.

## Cause Hypothesis

Styl kropki pozycjonuje ją stałym odstępem od góry pigułki zamiast względem jej wysokości; DESIGN.md (`tab-running-dot`) określa rozmiar, kolor i halo, ale nie położenie, więc build je zgadł.

## Acceptance Criteria

1. **Kropka wycentrowana w pionie**
   **Given** aplikacja działa w tle w zakładce
   **When** ekran główny pokazuje zakładki, w każdym stanie zakładki (nieaktywna, aktywna, z fokusem)
   **Then** środek kropki leży na środku wysokości pigułki, po prawej stronie, nie nachodzi na napis
2. **Nic nie skacze**
   **Given** kropka pojawia się albo znika
   **When** aplikacja przechodzi do tła albo się zamyka
   **Then** szerokość pigułki i położenie napisów w rzędzie zakładek się nie zmieniają
3. **Testy pokrywają położenie**
   **Given** testy `web/`
   **When** się wykonują
   **Then** test sprawdza, że kropka jest poza przepływem i wycentrowana w pionie, a istniejące testy kropki przechodzą
4. **Albo: nie trzeba zmiany, z dowodem**
   **Given** reprodukcja
   **When** wykonana na bieżącym kodzie
   **Then** kropka jest już wycentrowana, z zrzutem ekranu w Notes; to zastępuje 1–3

## References

- design — _bmad-output/initiative-mediastation-mvp/ux-mediastation/DESIGN.md, components `tab-running-dot`, tabela komponentów (Tab)
- acceptance — _bmad-output/initiative-mediastation-mvp/tickets/epic-windows-audio/epic-windows-audio-um760-acceptance.md, Uwagi użytkownika U4
- ledger — _bmad-output/initiative-mediastation-mvp/deferred-work.md, DW-46

## Notes

- Decision: kropka wycentrowana w pionie, po prawej stronie pigułki; słowa użytkownika z odbioru (2026-10-27).
- Open question: czy DESIGN.md dostaje zapis położenia kropki (prompt dla `bmad-ux`); build nie czeka na to.
