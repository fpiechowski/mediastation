---
type: epic
title: "Orchestrator & app lifecycle"
parent: initiative-mediastation-mvp
covers: [CAP-2]
after: []
assignee: ""
risk: medium
---

# Orchestrator & app lifecycle

## Description

CAP-2: kilka aplikacji działa naraz i przełącza się bez zamykania, każda ma jawny stan cyklu życia. Epika dostarcza Orchestrator jako jedynego writera stanu sesji (jedna kolejka, Commands fire-and-forget, `transitionId`, `TimerElapsed`), adopcję sierot, ścieżkę wybudzenia i `focusRequest` w snapshotach oraz produkcyjny adapter SystemEvents (power, procesy), żeby wybudzenie z Modern Standby docierało do Core na UM760.

## Outcome

Kolejne epiki podpinają hosty i wykonawców do jednego właściciela decyzji, sprawdzonego testami na fake'ach; sygnałem są testy Orchestratora z success CAP-2.

## Done when

1. Testy Orchestratora na fake'ach portów z `FakeTimeProvider` pokrywają pięć scenariuszy z success CAP-2: przejścia z `Failed`, Home w trakcie `Starting`, adopcję przeglądarki i Moonlighta po restarcie, wybudzenie z `focusRequest{default}`, awarię aplikacji na pierwszym planie z fokusem na jej kafelku (AD-10, AD-14).
2. Na UM760 Orchestrator działa w composition root, shell dostaje snapshoty (AD-16), a wybudzenie z Modern Standby przez SystemEvents daje `focusRequest{default}` (AD-10).

## Boundaries

Core: Orchestrator, stan sesji, polityka wyboru przy Home; App: SystemEvents. Nie: hosty aplikacji (epic-browserhost, epic-moonlight-native), WindowManager i AudioRouter (epic-windows-audio), UI (epic-home-screen). Weryfikacja J4 na UM760 jest w epic-browserhost.

## References

- parent — _bmad-output/initiative-mediastation-mvp/spec-mediastation/spec-mediastation.md, Capabilities CAP-2
- architecture — _bmad-output/initiative-mediastation-mvp/architecture-mediastation/architecture-mediastation.md, AD-3, AD-4, AD-10, AD-14, AD-16
- ux — _bmad-output/initiative-mediastation-mvp/ux-mediastation/EXPERIENCE.md, State Patterns (Kafelek — cykl życia)

## Notes

- Decision: weryfikacja J4 na UM760 (OK na `Failed`, OK na „Uruchom ponownie”) przechodzi do epic-browserhost, bo wymaga AppMenu i prawdziwego hosta (2026-10-18).
- Decision: produkcyjny adapter SystemEvents należy do tej epiki; `RenewSession` po stronie BrowserHost należy do epic-browserhost (2026-10-18).
- Waits on epic-foundation-contracts because: rekordy Commands/Events/`ErrorCode` i projekt testów.
- Deferred: `ShellBridge.Publish` nie przenosi wywołania na wątek UI (`CoreWebView2` ma thread affinity), a `rev` snapshotu podaje wywołujący (dziś stałe `Rev: 1` w composition root). Źródło: F12 (PLAUSIBLE) w retro epic-foundation-contracts (`_bmad-output/initiative-mediastation-mvp/epic-foundation-contracts/epic-foundation-contracts-retrospective.md`). Warunek: pierwsza story, która publikuje snapshot z Orchestratora do shella; jeśli ta ścieżka trafi do epic-home-screen, wpis idzie z nią (2026-10-20).
- Deferred: `startingTimeout` w `settings.json` nie ma górnej granicy, a bardzo duża wartość rzuci przy tworzeniu timera. Źródło: F18 w retro epic-foundation-contracts (`_bmad-output/initiative-mediastation-mvp/epic-foundation-contracts/epic-foundation-contracts-retrospective.md`); retro wskazało epic-deploy-resilience, tu, bo Orchestrator jest pierwszym konsumentem. Warunek: story, która pierwsza tworzy timeout `Starting` z `startingTimeout` (2026-10-20).
