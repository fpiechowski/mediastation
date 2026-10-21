---
title: 'LogAppendCheck hardening and sample configs'
type: 'chore'
ticket: 1
created: '2026-10-21'
status: done
baseline_revision: 'fb0042f7eec2d73c5f42657094cff0db33381199'
route: 'full'
route_source: 'auto'
risk: 'low'
review: 'quick'
review_source: 'pinned'
lenses_ran: ['quick']
review_loop_iteration: 0
context: []
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** `LogAppendCheck` jest manualny i zbyt słaby, by chronić atomowy append przed regresją (N2): brak wspólnej bariery startu, brak timeoutu, katalog tymczasowy zawsze jest usuwany, linie nie są sprawdzane w pełni, a sample configi z A9 nie istnieją.

**Approach:** Utwardzić checker tak, by dowodził appendu pod realną kontencją i bezpiecznie padał, dodać jeden skrypt uruchamiający oraz poprawne i błędne sample configi.

## Boundaries & Constraints

**Always:** zachowanie produkcyjnego `FileLogSink` i kształt linii logu bez zmian; jedna linia = jeden `Write`; checker zostaje `Exe` linkującym źródło `FileLogSink`; build solution bez ostrzeżeń (`TreatWarningsAsErrors`); wszystkie trzy pliki configu walidowane tym samym kodem co produkcja.

**Never:** projekt `App.Tests`; zmiana walidacji, schematów i specu; commit tymczasowego cofnięcia `FileLogSink`; uruchamianie checkera z `dotnet test`.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Happy path | 2 writery × 5000 linii, wspólna bariera | 10000 linii, `writer` ∈ {1,2}, `seq` ∈ 1..5000, `pad` 150 znaków, pary unikalne, start w oknie 1 s | exit 0, katalog usunięty |
| Live-read | trzeci proces czyta w trakcie zapisu | brak linii częściowej i uszkodzonego JSON | exit 0 |
| Hung writer | writer z `--hang` | checker zabija drzewo procesów po 30 s, wypisuje ścieżkę katalogu | exit 1, katalog zachowany |
| Regressed sink | tymczasowy `FileMode.Append` bez `AppendData` | zgubione lub uszkodzone linie | exit 1 |
| Valid samples | 3 pliki w `%LOCALAPPDATA%\MediaStation\config` | start bez `configError` | — |
| Unknown field | `settings.json` z `"x":1` | `settings.json`, `$.x`, „nieznane pole” | ekran błędu i log |
| Duplicate port | dwa web z `cdpPort` 9222 | `apps.json`, `$.apps[1].cdpPort` | ekran błędu i log |
| Out of range | web z `cdpPort` 9300 | `apps.json`, `$.apps[0].cdpPort`, zakres 9220–9239 | ekran błędu i log |

</frozen-after-approval>

## Code Map

- `tools/MediaStation.LogAppendCheck/Program.cs` -- writer subcommand :13-16; linia `{"writer","seq","pad"}` :18-23 (dziś writer 0/1, seq 0..4999, pad 150×`x`); sekwencyjny spawn :32-36; `WaitForExit()` bez timeoutu :38-40; katalog :28-29 i zawsze usuwany w `finally` :91-99; asercje :49-86. Tu wchodzą bariera, reader, `--hang`, timeout, zachowanie katalogu i pełny kształt linii.
- `tools/MediaStation.LogAppendCheck/MediaStation.LogAppendCheck.csproj` -- `net10.0-windows`, link do `FileLogSink` :13, `TreatWarningsAsErrors` :8; bez zmian.
- `src/MediaStation.App/Logging/FileLogSink.cs` -- `AppendData` :32-39, jeden `Write` :40, połknięte I/O :42-45; nie zmieniać.
- `MediaStation.slnx` -- narzędzie już w solution :10.
- `tools/check-log-append.ps1` -- nowy skrypt: build Release, uruchom checker, przekaż kod wyjścia.
- `tools/samples/config/` -- nowe `apps.json`, `input.json`, `settings.json` oraz `invalid/` z trzema wariantami.
- `src/MediaStation.Core/Config/AppsConfig.cs:33-88`, `InputConfig.cs:7-18`, `Settings.cs:4-28` -- wymagane pola i domyślne wartości dla sample.
- `src/MediaStation.Core/Config/ConfigValidation.cs:24-25,189-231,292-366` -- zakres i unikalność `cdpPort`, regex kluczy, reguły settings i styl ścieżek.
- `tests/MediaStation.Core.Tests/ConfigValidationTests.cs:14-46` -- poprawne fixture'y do adaptacji; :239-262 błędne porty.
- `src/MediaStation.App/CompositionRoot.cs:28-33,71` -- docelowy katalog configu i odczyt przy starcie.

## Tasks & Acceptance

**Execution:**
- [x] `tools/MediaStation.LogAppendCheck/Program.cs` -- writer 1..2 i seq 1..5000, bariera plikowa `ready-<id>`/`go`, sidecar start/end, równoległe czekanie z 30 s deadline i `Kill(entireProcessTree)`, zachowanie katalogu i wypis ścieżki przy błędzie, walidacja typu roota, pól, `pad`, zakresów i unikalności, subkomenda `reader` z `done`, tryb `--hang`/env -- spełnia AC.
- [x] `tools/check-log-append.ps1` -- build Release + `--no-build`, propagacja kodu wyjścia -- jedna komenda z AC8.
- [x] `tools/samples/config/apps.json`, `input.json`, `settings.json` -- poprawne sample z fixture'ów testów -- AC6.
- [x] `tools/samples/config/invalid/apps-duplicate-port.json`, `apps-port-out-of-range.json`, `settings-unknown-field.json` -- po jednym wariancie na błąd -- AC7.
- [x] `tests/MediaStation.Core.Tests/ConfigValidationTests.cs` -- test wczytujący sample z `tools/samples/config/` (repo root znaleziony po `MediaStation.slnx`) i sprawdzający poprawne i błędne ścieżki -- AC6, AC7.

**Acceptance Criteria:**
- Given `powershell -File tools/check-log-append.ps1`, when run, then exit 0 and the temp directory is removed.
- Given a writer started with `--hang`, when the checker runs, then it exits 1 within ~35 s, keeps the temp directory, and prints its path.
- Given a temporary, uncommitted `FileLogSink` revert to plain `FileMode.Append`, when the checker runs, then it exits 1 and reports lost or broken lines.
- Given the xUnit test loads the three valid samples, when validation runs, then the result has no errors.
- Given the test loads each invalid sample over its valid file, when validation runs, then exactly one error names the expected file, path, and reason.
- Given `dotnet test` and `dotnet build -c Release`, when run, then both pass with 0 warnings and no schema change.

## Implementation Notes

- `Program.cs`: bariera plikowa `ready-<id>`/`go`, sidecary `start-`/`end-`, równoległe czekanie z 30 s deadline, `reader` z `FileShare.ReadWrite` i buforowaniem końcowej linii, `--hang`/env tylko dla writera; `FileLogSink` bez zmian.
- `StartSelf` usuwa `MEDIASTATION_LOGCHECK_HUNG` z env dzieci, żeby tryb wiszący włączał się tylko jawnie przez `--hang`.
- Weryfikacja: build Release 0 ostrzeżeń; `dotnet test` 259/259; skrypt exit 0 (10000 linii, start 0 ms, nachodzenie ~1191 ms); hung mode exit 1 po 30 s, katalog zachowany i wypisany (posprzątany ręcznie).
- Cofnięcie `FileLogSink` do `FileMode.Append` sprawdzone przez implementera: checker zgłosił 9377/10000 linii; plik przywrócony, `git diff` czysty.
- Live-read ma dowód przeplotu: reader zapisuje `reader-first`/`reader-last`, a `CheckSidecars` odrzuca przebieg, w którym pierwsza linia przyszła po ostatnim `end-<id>`; przebieg 2026-10-21 dał 1151 ms przed końcem writerów.
- App-level sample sprawdzone na Release exe (backup i przywrócenie `%LOCALAPPDATA%\MediaStation\config`): poprawne sample bez `configError`, a każdy błędny wariant loguje `configError` z oczekiwanym `file` i `path`.

## Plan Change Log

## Review Triage Log

Pass 1 (quick): 3 findings — 0 high, 3 medium, 0 low, 0 false, 0 maybe-false.

| # | Finding | Verdict | Route | Evidence |
|---|---------|---------|-------|----------|
| 1 | Working-tree plan was double-encoded UTF-8 | medium | patch | Confirmed BOM + mojibake; restored the byte-clean copy from the index and re-applied status/review with a UTF-8-safe edit; verified no BOM and readable Polish. |
| 2 | Live-read could pass without a reader reading during writes | medium | patch | Confirmed no overlap proof; added `reader-first`/`reader-last` sidecars and a check that fails when the first read is after the last writer end; the passing run reports 1151 ms before the writers finished. |
| 3 | App-level sample behavior was unverified | medium | patch | Confirmed only Core validation was tested; ran the Release exe with the valid samples and each invalid variant against `%LOCALAPPDATA%` (config backed up and restored): valid had no `configError`, each invalid logged `configError` with the expected file and path. |

## Design Notes

Bariera plikowa zamiast nazwanego eventu: działa między procesami bez uprawnień i łatwo ją obserwować. Reader otwiera plik z `FileShare.ReadWrite` i buforuje końcowy fragment linii; inaczej połknięte przez sink błędy otwarcia wyglądają jak zgubione linie. `--hang` działa tylko w procesie writera, więc checker nie może przypadkiem zawisnąć. Błędne sample leżą w `invalid/`, żeby nie zastąpić poprawnych plików przez pomyłkę.

## Verification

**Commands:**
- `dotnet build MediaStation.slnx -c Release` -- expected: 0 ostrzeżeń.
- `dotnet test` -- expected: wszystkie zielone.
- `powershell -File tools/check-log-append.ps1` -- expected: exit 0.
- `$env:MEDIASTATION_LOGCHECK_HUNG=1; powershell -File tools/check-log-append.ps1; $LASTEXITCODE` -- expected: 1 po ~30 s.

**Manual checks (if no CLI):**
- Cofnięcie `FileLogSink` do `FileMode.Append` wykonane tymczasowo i cofnięte; checker ma zgłosić błąd, a `git status` czysty.
- Sample: backup `%LOCALAPPDATA%\MediaStation\config`, kopia plików, start Release exe, odczyt logu, przywrócenie backupu.
