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

CAP-12: MediaStation instaluje się skryptem, startuje po zalogowaniu i sama wraca po awarii, więc nocny restart przez Windows Update nie zostawia Elizy przed pulpitem. Epika dostarcza `tools/install.ps1`, zadanie Task Scheduler „at logon”, tryb `--supervise` z backoffem, ścieżkę hotfixu drivera i exit codes `--smoke`. Logger JSON Lines dostarcza już epic-foundation-contracts.

## Outcome

Eliza po każdym restarcie PC zastaje ekran główny, a autor poprawia driver bez reinstalacji; sygnałem jest success CAP-12.

## Done when

1. `tools/install.ps1` instaluje build i rejestruje zadanie „at logon” z `--supervise`; po restarcie Windows na UM760 MediaStation sama wraca na ekran główny (AD-2, AD-13).
2. Zabicie procesu dziecka kończy się restartem z backoffem i powrotem na ekran główny, z adopcją żywych aplikacji (AD-2, AD-10).
3. Podmiana pliku drivera w `<install>/page/` i „Uruchom ponownie” z pilota wdrażają hotfix bez ponownej instalacji (AD-13).
4. `--smoke` zgłasza osobnymi kodami wyjścia `CdpUnavailable`, `PortInUse` i `ProfileInUse` i nie przechodzi przy różnicy `Browser.getVersion` (AD-1, AD-13).

## Boundaries

`tools/install.ps1`, `--supervise`, `Smoke/`. Nie: autologowanie, ukrycie pulpitu, polityka Windows Update (autor, poza produktem), automatyczna obsługa awarii (Non-goals).

## References

- parent — _bmad-output/initiative-mediastation-mvp/spec-mediastation/spec-mediastation.md, Capabilities CAP-12
- architecture — _bmad-output/initiative-mediastation-mvp/architecture-mediastation/architecture-mediastation.md, AD-1 (trigger planu C), AD-2, AD-10, AD-13 (Deploy, Hotfix drivera, Smoke, Aktualizacja przeglądarki)

## Notes

- Decision: logi JSON Lines z success CAP-12 dostarcza epic-foundation-contracts; ta epika ich nie powtarza (2026-10-18).
- Waits on epic-moonlight-native because: adopcja żywego Moonlighta po restarcie przez `--supervise`.
