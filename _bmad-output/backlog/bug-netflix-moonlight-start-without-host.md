---
id: 17
type: bug
title: "Netflix i Moonlight przechodzą w „Nie odpowiada”, bo build nie rejestruje hostów aplikacji"
parent: none
covers: []
after: []
assignee: ""
refined: true
hitl: false
risk: high
severity: P1
---

# Netflix i Moonlight przechodzą w „Nie odpowiada”, bo build nie rejestruje hostów aplikacji

## Description

W najnowszym buildzie MediaStation naciśnięcie OK na kafelku Netflix albo Moonlight nie uruchamia aplikacji. Oba kafelki przechodzą do badge „Nie odpowiada”, a użytkownik nie dostaje działającego okna. Oczekiwane jest uruchomienie właściwego hosta, przypięcie okna i przejście kafelka do stanu działającego.

## Reproduction

1. Na UM760 uruchomić najnowszy build MediaStation z konfiguracją zawierającą wpisy 
etflix i moonlight-pc.
2. Nacisnąć OK na kafelku Netflix.
3. Actual: kafelek pokazuje „Nie odpowiada”; log z 2026-10-28 zawiera startWithoutHost dla 
etflix oraz commandFailed z rrorCode: unexpected; nie ma ppLaunch ani ppWindowAttached dla tej próby.
4. Wrócić do ekranu głównego i nacisnąć OK na kafelku Moonlight.
5. Actual: kafelek pokazuje „Nie odpowiada”; log zawiera startWithoutHost dla moonlight-pc oraz commandFailed z rrorCode: unexpected.
6. Expected: Netflix otwiera okno Edge i Moonlight otwiera okno klienta, dla obu pojawia się ppWindowAttached, a żaden kafelek nie przechodzi do „Nie odpowiada”.

## Cause Hypothesis

W ścieżce uruchomienia najnowszego builda hosty dla wpisów web i moonlight nie są rejestrowane albo są tracone przed obsługą StartApp. Wspólny sygnał startWithoutHost dla obu typów wskazuje na błąd inicjalizacji/rejestracji hostów, a nie na osobny problem Edge lub Moonlighta.

## Acceptance Criteria

1. **Netflix uruchamia się z kafelka**
   **Given** najnowszy build i poprawny wpis 
etflix
   **When** użytkownik naciska OK
   **Then** host Netflix przyjmuje start, Edge tworzy okno, pojawia się ppWindowAttached, a kafelek pozostaje w stanie działającym.
2. **Moonlight uruchamia się z kafelka**
   **Given** osiągalny host Sunshine i poprawny wpis moonlight-pc
   **When** użytkownik naciska OK
   **Then** host Moonlight przyjmuje start, klient tworzy okno, pojawia się ppWindowAttached, a kafelek pozostaje w stanie działającym.
3. **Brak hosta nie maskuje przyczyny**
   **Given** wpis aplikacji bez zarejestrowanego hosta
   **When** start zostaje odrzucony
   **Then** log zawiera rozróżnialny błąd inicjalizacji wraz z ppId, zamiast ogólnego commandFailed bez kontekstu.
4. **Regresja ma test**
   **Given** test suite
   **When** uruchamiane są ścieżki inicjalizacji i startu aplikacji dla typów web i moonlight
   **Then** test odtwarza startWithoutHost z obecnego builda i przechodzi po poprawce, a testy potwierdzają ppWindowAttached dla obu hostów.
5. **Start nie psuje dalszego cyklu życia**
   **Given** działający Netflix lub Moonlight
   **When** użytkownik wykonuje Home, a następnie ponownie OK
   **Then** aplikacja jest minimalizowana i przywracana zgodnie z istniejącą polityką, bez tworzenia drugiego procesu.
6. **Albo brak zmiany, z dowodem**
   **Given** reprodukcja na UM760
   **When** uruchomienie zostaje powtórzone na aktualnym buildzie z logiem
   **Then** oczekiwane zachowanie już zachodzi albo raport był błędny, a Notes zawiera dowód; to kryterium zastępuje 1–5.

## References

- parent — none; standalone regression in backlog
- logs — %LOCALAPPDATA%/MediaStation/logs/mediastation-2026-10-28.jsonl, entries startWithoutHost and commandFailed for 
etflix and moonlight-pc (2026-10-28)
- prior successful run — %LOCALAPPDATA%/MediaStation/logs/mediastation-2026-10-28.jsonl, entries with ppLaunch and ppWindowAttached for 
etflix and moonlight-return (2026-10-28)
- related ticket — _bmad-output/initiative-mediastation-mvp/tickets/epic-windows-audio/tickets/bug-start-kafelka-netflix-konczy-sie-devharnessstartfailed-zamia.md, Reproduction and Cause Hypothesis
- related ticket — _bmad-output/initiative-mediastation-mvp/tickets/epic-windows-audio/tickets/bug-dev-harness-moonlight-nie-znajduje-okna-sdl-app-po-starcie.md, Reproduction and Cause Hypothesis

## Notes

- Decision: hitl = false; implementation and regression tests are agent-owned, with final whole-epic acceptance left to the operator (2026-10-28).
- Decision: severity P1, because two primary application launch paths are broken with no user workaround (2026-10-28).
- Decision: current machine is UM760 (MEDIASTATION, board F7BSI, model EliteMini Series) (2026-10-28).
- High-risk extra check: after the fix, launch both apps from a clean MediaStation process and compare the log with the failing startWithoutHost entries.
