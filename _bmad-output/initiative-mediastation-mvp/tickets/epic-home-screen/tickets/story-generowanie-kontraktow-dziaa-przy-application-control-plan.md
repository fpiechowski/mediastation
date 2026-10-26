---
title: 'Generowanie kontraktów działa przy Application Control'
type: 'chore'
ticket: '1'
created: '2026-10-26'
status: done
baseline_revision: 'fe0a400ca29497ccb3d42ff9c740c49f2c3d6ee8'
route: 'full'
route_source: 'auto'
risk: 'medium'
review: 'quick'
review_source: 'pinned'
lenses_ran: [quick]
review_loop_iteration: 0
followup_review_recommended: false
context:
  - '{project-root}/_bmad-output/initiative-mediastation-mvp/architecture-mediastation/architecture-mediastation.md'
warnings: []
deferred: []
---

<intent-contract>

## Intent

**Problem:** Eksport JSON Schema robi osobny, świeży niepodpisany `MediaStation.SchemaExport.dll`, który Application Control blokuje (0x800711C7). `dotnet build` nie odświeża `web/contracts`, a publish wymaga `-p:SkipWebBuild=true`, ręcznego `npm run build` i kopiowania `web/shell/dist` (DW-28).

**Approach:** Wg AD-16 ("Eksport schematów działa w przebiegu testów") eksport robi test `Category=SchemaExport` w `MediaStation.Core.Tests`; `BuildWeb` buduje projekt testów zadaniem `<MSBuild>`, uruchamia `dotnet test --no-build` z filtrem, potem tylko generowanie TS i Vite. `tools/MediaStation.SchemaExport` znika.

## Boundaries & Constraints

**Always:** `BuildWeb` nigdy nie uruchamia budującego `dotnet build`/`dotnet test` jako osobnego procesu (wyścig o `bin/obj`). Zapis schematu tylko przy zmianie; usuwanie nieaktualnych `*.schema.json`. W `web/` skrypt eksportu i skrypt generowania są osobne. Zero pasujących testów albo zablokowany host testów kończy build błędem, bez fallbacku na stare typy. Zwykły `dotnet publish` zawiera `shell/`. Komentarze/komunikaty po polsku jak w otoczeniu.

**Never:** Nie podpisujemy narzędzi ani nie ruszamy reguł Application Control. Nie zmieniamy rekordów kontraktów ani `ContractJson`. Nie edytujemy ręcznie `web/contracts/generated` ani `schema` (pliki w .gitignore).

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Zmiana pola w rekordzie Core | `dotnet build` potem `npm run build` w `web/` | schemat i typ TS zaktualizowane bez kroków ręcznych | — |
| Schemat nieaktualny | plik `*.schema.json` bez rekordu w Core | test eksportu go usuwa; TS generowany z reszty | — |
| Filtr bez pasujących testów | kategoria nie istnieje | build kończy się błędem | `dotnet test` zwraca niezerowy kod (MTP: 8), `Exec` przerywa |
| Zwykły publish | `dotnet publish` bez `SkipWebBuild` | wynik zawiera `shell/index.html` | — |

</intent-contract>

## Code Map

- `tools/MediaStation.SchemaExport/Program.cs` -- logika eksportu do przeniesienia do testu (zapis tylko przy zmianie, usuwanie nieaktualnych); projekt do skasowania
- `MediaStation.slnx` -- usunąć wpis `tools/MediaStation.SchemaExport`
- `src/MediaStation.App/MediaStation.App.csproj` -- target `BuildWeb` (SchemaExport MSBuild + Exec dotnet dll) do przepisania
- `tests/MediaStation.Core.Tests/` -- xunit.v3 na Microsoft.Testing.Platform (`global.json` `test.runner`); `CoreMessageContractTests.cs` zostaje zielony
- `src/MediaStation.Core/Contracts/ContractJson.cs` -- `ExportSchemas()` (tylko wołać)
- `web/package.json`, `web/scripts/contracts.mjs` -- skrypty `contracts`/`build`; komunikat błędu w `contracts.mjs` wskazuje na `dotnet build`
- `_bmad/custom/fp-acceptance.toml:18` -- opis obejścia SchemaExport; zaktualizować na nieaktualne
- `_bmad-output/initiative-mediastation-mvp/deferred-work.md` -- DW-28 nie ruszać (status zamyka orkiestrator/PO)

## Tasks & Acceptance

**Execution:**
- [ ] `tests/MediaStation.Core.Tests/SchemaExportTests.cs` -- test `[Trait("Category","SchemaExport")]` eksportujący `ContractJson.ExportSchemas()` do `web/contracts/schema` (korzeń repo szukany w górę od `AppContext.BaseDirectory` po `MediaStation.slnx`), UTF-8 bez BOM, `\n` na końcu, zapis tylko przy zmianie, usuwanie nieaktualnych; test sprawdza też, że po eksporcie plik każdego schematu istnieje
- [ ] `tools/MediaStation.SchemaExport/` + `MediaStation.slnx` -- usunąć projekt i wpis
- [ ] `src/MediaStation.App/MediaStation.App.csproj` -- `BuildWeb`: `<MSBuild>` buduje `MediaStation.Core.Tests` (RemoveProperties jak dotąd), `Exec dotnet test --project … --no-build --no-restore -c $(Configuration)` z filtrem `Category=SchemaExport` (ustalić działającą składnię filtra dla MTP/xunit.v3 i sprawdzić, że zero testów daje błąd), potem `npm ci` gdy brak node_modules, `npm run contracts:generate`, typecheck/`build:shell`; usunąć property `MediaStationSchemaExportProject`
- [ ] `web/package.json` -- `contracts:export` (dotnet test z budowaniem i filtrem), `contracts:generate` (node scripts/contracts.mjs), `contracts` = oba po kolei, `build:shell` = typecheck + vite build, `build` = contracts + build:shell
- [ ] `web/scripts/contracts.mjs`, `_bmad/custom/fp-acceptance.toml` -- poprawić komentarze/komunikaty zależne od starego eksportera

**Acceptance Criteria:**
- Given zmiana pola w rekordzie Core, when `dotnet build src/MediaStation.App` i `npm run build` w `web/`, then schemat i typ TS są zaktualizowane bez kroków ręcznych, a nie powstaje żaden `MediaStation.SchemaExport.*`.
- Given filtr bez pasujących testów, when uruchomiony krok eksportu, then kończy się błędem.
- Given zwykły `dotnet publish`, when ukończony, then katalog wyjściowy zawiera `shell/index.html`.
- Given `dotnet test`, when uruchomiony, then przechodzi, w tym `CoreMessageContractTests`.

## Implementation Notes

## Plan Change Log

## Review Triage Log

## Design Notes

`dotnet test` w trybie MTP (global.json) – sprawdzić eksperymentalnie, czy filtr to `--filter-trait "Category=SchemaExport"` (xunit.v3) czy `--filter`; architektura pisze `--filter Category=SchemaExport`, więc przy różnicy użyć działającej formy i opisać w Implementation Notes.

## Verification

**Commands:**
- `dotnet build MediaStation.slnx` -- expected: sukces, schematy odświeżone
- `dotnet test` -- expected: wszystkie testy zielone
- `npm run build` w `web/` -- expected: sukces
- `dotnet publish src/MediaStation.App -c Release -o <tmp>` -- expected: `<tmp>/shell/index.html` istnieje
- filtr z nieistniejącą kategorią -- expected: niezerowy kod wyjścia

## Auto Run Result

**Machine:** UM760 (COMPUTERNAME MEDIASTATION, board F7BSI, model EliteMini Series).

**Summary:** Schema export now runs as the `Category=SchemaExport` test in `MediaStation.Core.Tests`; `tools/MediaStation.SchemaExport` is removed; `BuildWeb` builds the test project with `<MSBuild>`, runs `dotnet test --no-build --filter Category=SchemaExport`, then only TS generation and Vite. Filter `--filter Category=SchemaExport` works with MTP + xunit.v3 (`--filter-trait` also works); no matching tests exit with code 8.

**Files changed:**
- `tests/MediaStation.Core.Tests/SchemaExportTests.cs` — new export test (write only on change, remove stale)
- `tools/MediaStation.SchemaExport/*`, `MediaStation.slnx` — removed
- `src/MediaStation.App/MediaStation.App.csproj` — new `BuildWeb`
- `web/package.json` — `contracts:export`, `contracts:generate`, `contracts`, `build:shell`, `build`
- `web/scripts/contracts.mjs`, `_bmad/custom/fp-acceptance.toml` — messages/facts updated

**Review:** quick lens, 9 findings. Patched: 1 (fp-acceptance.toml overclaim softened; low). Rejected: missing evidence/notes (now recorded here); filter syntax unproven (run: works); zero-match error unproven (run: exit 8); possible bin/obj race (maybe-false, mechanism is prescribed by AD-16, solution build ran clean); export test runs in every `dotnet test` (by AD-16 design); delete-after-write order (same as the old tool, low); `contracts:export` Debug config (low); stale checkboxes (cosmetic). Follow-up review recommended: false.

**Verification on UM760:** `dotnet build MediaStation.slnx` 0 warnings/0 errors, stale planted schema removed; `dotnet test` 949/949 including `CoreMessageContractTests`; `npm run build` in `web/` exit 0; plain `dotnet publish -c Release` produced `shell/index.html`; no `MediaStation.SchemaExport.*` produced; `--filter Category=Nope` exits 8.

**Residual risks:** the Application Control block was not reproduced on this machine; whether the fresh unsigned test host runs under that policy on UM760 is for the user's epic acceptance (a block fails the build loudly).

### Review Triage Log
- patch(low): toml overclaim softened; rest rejected as above.
