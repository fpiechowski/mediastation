---
type: epic
title: "Foundation & contracts"
parent: initiative-mediastation-mvp
covers: [CAP-1, CAP-12]
after: []
assignee: ""
risk: medium
---

# Foundation & contracts

## Description

Platform baseline i CAP-1: MediaStation działa jako jedna instancja jednego procesu, ma rdzeń niezależny od Windows i jedno źródło kontraktów oraz konfiguracji dla wszystkich kolejnych epik. Epika zakłada solution z granicą Core/App wg Structural Seed, `global.json`, build web jako target MSBuild (Debug z Vite dev servera, Release z plików lokalnych), eksport JSON Schema z Core z generowanymi typami TS, named mutex, walidację trzech plików `config/` z ekranem błędu w pustym hoście Shell (WPF + WebView2 + most `postMessage`) oraz logger JSON Lines. Z CAP-12 bierze wyłącznie logi.

## Outcome

Każda kolejna epika buduje na jednym procesie, jednym źródle kontraktów i działającym ręcznym deployu na UM760; sygnałem jest success CAP-1.

## Done when

1. Testy Core (xunit) przechodzą, a `MediaStation.Core` nie ma referencji do Win32, WebView2, WPF ani systemu plików (AD-3).
2. Typy TS powstają w buildzie web z JSON Schema Core i nikt ich nie edytuje ręcznie (AD-16).
3. Druga instancja, także `--smoke`, nie startuje (AD-2).
4. Na UM760 błędny `apps.json`, `input.json` albo `settings.json` (nieznane pole, zduplikowany `cdpPort`, port spoza 9220–9239) daje ekran błędu konfiguracji zamiast crasha (AD-11).
5. Build z `dotnet publish` uruchomiony na UM760 pokazuje shell z plików lokalnych i loguje JSON Lines w UTC z rotacją dzienną (AD-13).

## Boundaries

Scaffold, kontrakty i konfiguracja. Nie: Orchestrator i jego logika (epic-orchestrator-lifecycle), ekran główny (epic-home-screen), `install.ps1` i `--supervise` (epic-deploy-resilience). Rekordy kontraktów zakłada ta epika, kolejne epiki je rozszerzają.

## References

- parent — _bmad-output/initiative-mediastation-mvp/spec-mediastation/spec-mediastation.md, Capabilities CAP-1, CAP-12 (logi)
- architecture — _bmad-output/initiative-mediastation-mvp/architecture-mediastation/architecture-mediastation.md, AD-2, AD-3, AD-11, AD-13 (Build, Środowiska, Logi), AD-16, Stack, Structural Seed
- reference — spike/ (zamrożona referencja zachowania, nie kod do skopiowania)

## Notes

- Decision: bez CI; build trafia na UM760 przez ręczne `dotnet publish` i kopię aż do epiki Deploy & resilience (2026-10-18).
- Decision: logger JSON Lines (UTC, rotacja dzienna) z CAP-12 powstaje tutaj, bo checki CAP-3 i CAP-9 czytają logi (2026-10-18).
- Decision: rekord `SetInputRouting` i `Action` są w kontraktach Core tej epiki, żeby epiki Orchestrator i Input szły równolegle (2026-10-18).
- Decision: tracer bullet tej epiki: `MediaStation.exe` na UM760 → Core → WebView2 shell z buildu React pokazuje placeholder albo ekran błędu konfiguracji (2026-10-18).
- Decision: lane'y Core (kontrakty → config) i App (logger → jedna instancja i CLI); kontrakty i logger idą równolegle po tracerze (2026-10-18).
- Decision: rotacja i nazwa pliku logu to czysta logika w Core testowana w `MediaStation.Core.Tests` z FakeTimeProvider; bez projektu `App.Tests` (2026-10-18).
- Decision: parser CLI zna `--smoke` i `--calibrate-input` (implementacje w epikach 3 i 11, do tego czasu kod „nieobsługiwane”); `--supervise` poza tą epiką (2026-10-18).
- Decision: bez osobnej story end-to-end; każdy punkt Done when sprawdza `verify` swojej entry na UM760 (2026-10-18).
