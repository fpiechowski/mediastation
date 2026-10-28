---
id: 17
type: story
title: "Preflight odbioru wymaga zalogowanego i grającego netflix.com"
parent: epic-windows-audio
covers: [R8]
after: [16]
assignee: ""
refined: true
hitl: true
risk: high
---

# Preflight odbioru wymaga zalogowanego i grającego netflix.com

## Description

Przed scenariuszami audio odbiór musi potwierdzić, że Edge używa profilu `profiles\\netflix`, ma otwarte `netflix.com` i rzeczywiście odtwarza materiał. Preflight automatycznie sprawdza proces, okno i log, a użytkownik potwierdza logowanie oraz słyszalne odtwarzanie; bez tego S15–S27 pozostają niegotowe.

## Acceptance Criteria

1. **Warunek gotowości jest sprawdzany przed audio**
   **Given** świeży publish po ticket 16 i profil `profiles\\netflix`
   **When** uruchamiany jest preflight odbioru
   **Then** wynik `ready` wymaga widocznego okna Edge, `appWindowAttached` dla `netflix`, adresu lub tytułu wskazującego `netflix.com` oraz potwierdzenia użytkownika, że materiał odtwarza dźwięk.
2. **Niepełne środowisko blokuje S15–S27**
   **Given** brak logowania, zły profil, brak okna, pusty materiał albo niedostępny URL
   **When** preflight się kończy
   **Then** wynik jest niegotowy, zawiera konkretną przyczynę i plan odbioru nie pozwala oznaczyć scenariuszy audio jako wykonanych.
3. **Diagnostyka nie myli sukcesu procesu z sukcesem strony**
   **Given** `audioRuleApplied muted=false sessions=0` bez `appWindowAttached`
   **When** preflight ocenia log
   **Then** zwraca niegotowy wynik z przyczyną braku okna/materiału; sama sesja audio nie spełnia warunku.
4. **Preflight ma test regresyjny i instrukcję ponownego odbioru**
   **Given** poprawny i niepoprawny stan profilu
   **When** uruchamiany jest check
   **Then** testy rozróżniają oba stany, a plan odbioru wskazuje, że dopiero wynik `ready` odblokowuje S15–S27.

## Boundaries

- Must not substitute YouTube or `MEDIASTATION_HARNESS_URL` for the Netflix acceptance condition.
- Must not modify WindowManager, AudioRouter, Moonlight or the user's personal Edge profiles.
- User step: log in to Netflix in `profiles\\netflix`, start playback and confirm that it is visible and audible.

## References

- parent — `_bmad-output/initiative-mediastation-mvp/tickets/epic-windows-audio/epic-windows-audio.md`, R8, Done when 4
- acceptance — `_bmad-output/initiative-mediastation-mvp/tickets/epic-windows-audio/epic-windows-audio-um760-acceptance.md`, P0.4, S15–S27
- previous launch check — `_bmad-output/initiative-mediastation-mvp/tickets/epic-windows-audio/tickets/bug-start-kafelka-netflix-konczy-sie-devharnessstartfailed-zamia.md`

## Notes

- Decision: `netflix.com` w profilu `profiles\\netflix` jest obowiązkowym warunkiem rozpoczęcia S15–S27; materiał diagnostyczny z `MEDIASTATION_HARNESS_URL` może wspierać debugowanie, ale nie zamyka odbioru (2026-10-28).
- Hitl step: użytkownik wykonuje logowanie i potwierdza widoczny oraz słyszalny materiał przed rozpoczęciem scenariuszy audio.
