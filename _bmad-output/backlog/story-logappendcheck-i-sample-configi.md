---
id: 1
type: story
title: "LogAppendCheck wykrywa regresję appendu, a sample configi są w repo"
parent: none
covers: []
after: []
assignee: ""
refined: true
hitl: false
risk: low
---

# LogAppendCheck wykrywa regresję appendu, a sample configi są w repo

## Description

`LogAppendCheck` uruchamia się jedną komendą z repo i niezawodnie wykrywa regresję atomowego appendu z F8: dwa procesy startują jednocześnie, a wynik sprawdza pełny kształt każdej linii. W repo leżą sample configi z A9, z których można odtworzyć stan poprawny i błędy walidacji.

## Acceptance Criteria

1. **Wspólna bariera startu**
   **Given** dwa procesy writera uruchamiane przez checker
   **When** startują
   **Then** oba czekają na wspólny sygnał startu i zaczynają pisać w oknie 1 s
   **And** checker raportuje, że okresy zapisu obu procesów nachodzą na siebie

2. **Pełny kształt linii**
   **Given** 2 × 5000 linii zapisanych do jednego pliku
   **When** checker czyta plik
   **Then** jest 10000 linii, każda parsuje się jako JSON i ma pola `writer`, `seq`, `pad` z zadeklarowaną długością `pad`
   **And** `writer` ∈ {1, 2}, `seq` ∈ 1..5000, a każda para (writer, seq) występuje dokładnie raz

3. **Live-read**
   **Given** trzeci proces czytający plik w trakcie zapisu
   **When** writery piszą
   **Then** czytelnik nie widzi linii częściowej ani uszkodzonego JSON

4. **Timeout i zachowanie katalogu**
   **Given** writer uruchomiony z testowym przełącznikiem `--hang` (albo `MEDIASTATION_LOGCHECK_HUNG=1`)
   **When** checker czeka
   **Then** kończy się kodem 1 po 30 s zamiast wisieć
   **And** przy błędzie zostawia katalog tymczasowy i wypisuje jego ścieżkę, a przy sukcesie go usuwa

5. **Regresja złapana**
   **Given** `FileLogSink` tymczasowo, bez commita, cofnięty do `FileMode.Append` bez `AppendData`
   **When** uruchamiam checker
   **Then** kończy się kodem 1 i raportuje zgubione lub uszkodzone linie (sprawdzone raz przy implementacji)

6. **Poprawne sample configi**
   **Given** `tools/samples/config/` z `apps.json`, `input.json`, `settings.json`
   **When** kopiuję trzy pliki do `%LOCALAPPDATA%\MediaStation\config` i uruchamiam aplikację
   **Then** aplikacja startuje bez `configError`

7. **Błędne sample configi**
   **Given** `tools/samples/config/` z trzema błędnymi wariantami: nieznane pole `$.x` w `settings.json`, duplikat `cdpPort` w `apps.json`, port poza zakresem 9220–9239 w `apps.json`
   **When** podmieniam dany plik i uruchamiam aplikację
   **Then** każdy wariant daje `configError` na ekranie błędu i w logu, z nazwą pliku i ścieżką pola

8. **Jedna komenda i brak regresji testów**
   **Given** checkout repo
   **When** uruchamiam `powershell -File tools/check-log-append.ps1`
   **Then** skrypt buduje Release i uruchamia checker, a całość kończy się kodem 0
   **And** `dotnet test` pozostaje zielony i nie wymaga uruchamiania checkera

## Boundaries

- Must not change: zachowanie `FileLogSink` w produkcji i kształt linii logu.
- Must not add: projekt `App.Tests`; checker zostaje `Exe` linkującym źródło `FileLogSink`.

## References

- parent — none
- retro — _bmad-output/initiative-mediastation-mvp/tickets/epic-foundation-contracts/epic-foundation-contracts-retrospective.md, N2, R3, A9 (sample configi)
- plan — _bmad-output/initiative-mediastation-mvp/tickets/epic-foundation-contracts/tickets/story-scisa-koperta-i-diagnostyka-hosta-plan.md, Tasks i Verification
- source — tools/MediaStation.LogAppendCheck/
- source — src/MediaStation.App/Logging/FileLogSink.cs

## Notes

- Decision: R3 wykonujemy teraz, przed epiką 2, bo logger jest używany przez wszystkie kolejne epiki (2026-10-21).
- Decision: sample configi z A9 wchodzą do tego ticketu, bo dotyczą tych samych manualnych checków (2026-10-21).
- Decision: checker uruchamiamy skryptem `tools/check-log-append.ps1`; `release.yml` podepnie ten skrypt w epic-deploy-resilience, gdy powstanie (2026-10-21).
- Assumption: za „start jednoczesny” uznajemy rozpoczęcie zapisu obu writerów w oknie 1 s; próg do potwierdzenia przy implementacji.
