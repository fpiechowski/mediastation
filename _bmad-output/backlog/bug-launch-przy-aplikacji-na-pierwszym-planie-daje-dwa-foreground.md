---
id: 3
type: bug
title: "Uruchomienie nieuruchomionej aplikacji przy innej na pierwszym planie daje dwie aplikacje w Foreground"
parent: none
covers: []
after: []
assignee: ""
refined: true
hitl: false
risk: medium
severity: P2
---

# Uruchomienie nieuruchomionej aplikacji przy innej na pierwszym planie daje dwie aplikacje w Foreground

## Description

`OnLaunchApp` w Orchestratorze sprawdza tylko stan docelowej aplikacji i nie pyta, czy inna aplikacja jest już na pierwszym planie, inaczej niż `OnResume`, `OnStop` i ich wspólny `AnotherAppIsActive`. Po `LaunchApp` dla aplikacji `NotRunning`, gdy inna jest `Foreground`, i po `AppWindowAttached` snapshot ma dwie aplikacje w `Foreground`, a `Home` chowa tylko pierwszą z nich. Błąd jest nieosiągalny z UI do czasu `epic-home-screen`, który pozwoli wybrać kafelek przy aplikacji na wierzchu.

## Source

Retro epiki 2, F6 i akcja A1 (`_bmad-output/initiative-mediastation-mvp/tickets/epic-orchestrator-lifecycle/epic-orchestrator-lifecycle-retrospective.md`); decyzja o osobnym bugu: 2026-10-22.

## Reproduction

1. Test Orchestratora: aplikacja `plex` w `Foreground`, `netflix` `NotRunning`.
2. `LaunchApp("netflix")`, potem `AppWindowAttached("netflix")` bez `Home` pomiędzy.
3. Actual: snapshot `netflix=Foreground, plex=Foreground`, brak `MinimizeWindow` dla `plex`. Expected: najwyżej jedna aplikacja w `Foreground`.

## Cause Hypothesis

Gałąź `NotRunning` w `OnLaunchApp` (`src/MediaStation.Core/Orchestrator/Orchestrator.cs`, ok. linii 171–193) nie ma guardu `AnotherAppIsActive`. Niezmiennik „jedna aplikacja na pierwszym planie” był dokładany story po story (2.3 launch, 2.10 resume/close/restart) zamiast jednym wspólnym wejściem.

## Acceptance Criteria

1. **Launch przy innej aplikacji na pierwszym planie**
   **Given** `plex` w `Foreground` i `netflix` `NotRunning`
   **When** `LaunchApp("netflix")`, a potem `AppWindowAttached("netflix")`
   **Then** w żadnym snapshocie nie ma dwóch aplikacji w `Foreground`
   **And** zachowanie jest zgodne z decyzją: launch jest odrzucany jak resume, albo `plex` przechodzi najpierw w `Background` z `MinimizeWindow`

2. **Wspólny guard**
   **Given** każde przejście, które stawia aplikację na pierwszym planie
   **When** inna aplikacja jest w `Foreground`
   **Then** przejście przechodzi przez jedno wspólne wejście z niezmiennikiem, a nie przez osobne guardy

3. **Testy**
   **Given** zestaw testów
   **When** `dotnet test`
   **Then** test z punktu 1 pokrywa kolejność attach bez `Home` pomiędzy, a istniejące testy przechodzą
   **And** na kodzie przed poprawką ten test pada

## Notes

- Decision (2026-10-22): bug jest osobnym ticketem w `backlog/`, a nie w epice; musi być zrobiony przed `epic-home-screen` (wpis w jej Notes).
- Open question: czy launch przy innej aplikacji na pierwszym planie ma być odrzucany, czy ma przełączać (minimalizować poprzednią). Przełączanie między dwiema żywymi aplikacjami jest poza epiką 2 (`epic-orchestrator-lifecycle`, Notes), więc domyślnie: odrzucić.
