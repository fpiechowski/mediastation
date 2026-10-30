---
type: epic
title: "Deploy & resilience"
parent: initiative-mediastation-mvp
covers: [CAP-12]
after: []
assignee: ""
risk: medium
---

# Deploy & resilience

## Description

CAP-12: MediaStation instaluje się skryptem, startuje po zalogowaniu, sama wraca po awarii i aktualizuje się z GitHub Releases jednym OK z ekranu głównego, więc nocny restart przez Windows Update nie zostawia Elizy przed pulpitem, a nowa wersja nie wymaga autora przy UM760. Epika dostarcza `tools/install.ps1` z paczki release'u do katalogu wersji, zadanie Task Scheduler „at logon”, tryb `--supervise` z backoffem, handoffem i rollbackiem, release workflow w GitHub Actions (`.github/workflows/release.yml`), Updater z przyciskiem aktualizacji na Home, ścieżkę hotfixu drivera i exit codes `--smoke`. Logger JSON Lines dostarcza już epic-foundation-contracts.

## Outcome

Eliza po każdym restarcie PC zastaje ekran główny, autor poprawia driver bez reinstalacji, a nowa wersja trafia na UM760 przez tag w repo i jedno OK na Home; sygnałem jest success CAP-12.

## Done when

1. `tools/install.ps1` instaluje pierwszą wersję z paczki release'u do katalogu wersji i rejestruje zadanie „at logon” z `--supervise`; po restarcie Windows na UM760 MediaStation sama wraca na ekran główny (AD-2, AD-13, AD-17).
2. Zabicie procesu dziecka kończy się restartem z backoffem i powrotem na ekran główny, z adopcją żywych aplikacji (AD-2, AD-10).
3. Podmiana pliku drivera w `page/` bieżącej wersji i „Uruchom ponownie” z pilota wdrażają hotfix bez ponownej instalacji (AD-13).
4. `--smoke` zgłasza osobnymi kodami wyjścia `CdpUnavailable`, `PortInUse` i `ProfileInUse` i nie przechodzi przy różnicy `Browser.getVersion` (AD-1, AD-13).
5. Tag testowy `v<semver>` uruchamia `.github/workflows/release.yml` i daje GitHub Release z dwoma assetami (zip i `.sha256`), a `MediaStation.exe` z paczki raportuje wersję z tagu (AD-13, AD-17).
6. Na UM760 ręczne zastosowanie wersji N+1 nad N kończy się ekranem głównym z adopcją Netflixa, a celowo zepsuta N+2 po 3 nieudanych startach sama wraca do N+1 (AD-2, AD-17).
7. Przy nowszym release przycisk aktualizacji pojawia się na Home i jedno OK kończy się nową wersją na ekranie głównym; bez nowszego release przycisku nie ma; zła suma sha256 daje stan błędu i brak podmiany (AD-17).

## Boundaries

`tools/install.ps1`, `--supervise`, `Smoke/`, `.github/workflows/release.yml`, Updater i przycisk aktualizacji w nagłówku Home (Shell). Nie: autologowanie, ukrycie pulpitu, polityka Windows Update (autor, poza produktem), automatyczna obsługa awarii (Non-goals); layout nagłówka Home (epic-home-screen).

## References

- parent — _bmad-output/initiative-mediastation-mvp/spec-mediastation/spec-mediastation.md, Capabilities CAP-12
- architecture — _bmad-output/initiative-mediastation-mvp/architecture-mediastation/architecture-mediastation.md, AD-1 (trigger planu C), AD-2, AD-10, AD-13 (Build, Deploy, Hotfix drivera, Smoke, Aktualizacja przeglądarki), AD-17
- ux — _bmad-output/initiative-mediastation-mvp/ux-mediastation/EXPERIENCE.md, Component Patterns → Przycisk aktualizacji, State Patterns → Powierzchnie, Interaction Primitives (model fokusu), Voice and Tone; _bmad-output/initiative-mediastation-mvp/ux-mediastation/DESIGN.md (`update-button`)

## Notes

- Decision: release przez GitHub Actions, repo publiczne, Updater bez tokenu (2026-10-18).
- Decision: przy inception epika dostaje story „Release workflow w GitHub Actions” — `.github/workflows/release.yml` na tagu `v<semver>`: testy Core i web, `dotnet publish` self-contained win-x64 z `Version` z tagu, zip + `.sha256`, GitHub Release; verify: tag testowy daje release z dwoma assetami, a `MediaStation.exe` raportuje wersję z tagu (2026-10-18).
- Decision: przy inception epika dostaje story „Wersjonowana instalacja, handoff i rollback” — `install.ps1` z paczki release'u do `<wersja>\`, handoff supervisora z przepięciem zadania, sygnał gotowości, rollback po 3 nieudanych startach, sprzątanie starszych wersji; verify: na UM760 instalacja wersji N, ręczne zastosowanie N+1 kończy się Home z adopcją Netflixa; celowo zepsuta N+2 wraca do N+1 sama (2026-10-18).
- Decision: przy inception epika dostaje story „Updater i przycisk aktualizacji na Home” — Updater (`CheckForUpdate` przy starcie i raz na dobę, `DownloadUpdate` z weryfikacją sha256), pole `update` w snapshocie, przycisk w Shell wg DESIGN.md i EXPERIENCE.md (Component Patterns → Przycisk aktualizacji), `settings.json` → `update.repo`; verify: przy nowszym release przycisk się pojawia, jedno OK kończy się nową wersją na Home; bez nowszego release przycisku nie ma; zła suma daje stan błędu i brak podmiany (2026-10-18).
- Decision: kolejność nowych stories 1 → 2 → 3 („Release workflow” → „Wersjonowana instalacja” → „Updater”), po stories `install.ps1` i `--supervise`, które story „Wersjonowana instalacja, handoff i rollback” rozszerza (2026-10-18).
- Decision: logi JSON Lines z success CAP-12 dostarcza epic-foundation-contracts; ta epika ich nie powtarza (2026-10-18).
- Waits on epic-home-screen because: AppMenu „Uruchom ponownie” dla hotfixu drivera; nagłówek Home i model fokusu dla przycisku aktualizacji.
- Waits on epic-moonlight-native because: adopcja żywego Moonlighta po restarcie przez `--supervise`.
- Deferred: `update.repo` w `settings.json` przyjmuje `owner/..`. Źródło: F18 w retro epic-foundation-contracts (`_bmad-output/initiative-mediastation-mvp/tickets/epic-foundation-contracts/epic-foundation-contracts-retrospective.md`). Warunek: story „Updater i przycisk aktualizacji na Home”, zanim Updater zbuduje z `update.repo` URL (2026-10-20).
- Deferred: `SingleInstance` (`mutexUnavailable`) nie ma testu ani pełnego manualnego pokrycia. Źródło: R4/N3 w retro epic-foundation-contracts (`_bmad-output/initiative-mediastation-mvp/tickets/epic-foundation-contracts/epic-foundation-contracts-retrospective.md`). Warunek: pierwsza story dotykająca startu procesu, `--supervise` albo instalacji (2026-10-21).
- Deferred: modalny `MessageBox` przy awarii WebView2 blokuje start w trybie bezobsługowym. Źródło: R7/N8 w retro epic-foundation-contracts (`_bmad-output/initiative-mediastation-mvp/tickets/epic-foundation-contracts/epic-foundation-contracts-retrospective.md`). Warunek: story `--supervise` (2026-10-21).
- Waits on epic-netflix-dpad because: tryb `--smoke` i przebieg Netflixa powstają tam (wpisy 22 i 23); ta epika dokłada do `Smoke/` osobne kody wyjścia (inception epic-netflix-dpad, 2026-10-30).
