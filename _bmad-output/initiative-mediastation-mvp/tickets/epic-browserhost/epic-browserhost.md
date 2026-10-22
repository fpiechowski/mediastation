---
type: epic
title: "BrowserHost"
parent: initiative-mediastation-mvp
covers: [CAP-7, CAP-2]
after: []
assignee: ""
risk: high
---

# BrowserHost

## Description

CAP-7: MediaStation uruchamia przeglądarkę dobraną pod serwis, z osobnym profilem, i steruje nią bez śladu automatyzacji. Epika dostarcza własny launch Edge/Chrome, surowy klient CDP na loopbacku, ponawiany binding, reconnect, odczyt wersji spoza CDP, rozróżnialne błędy startu, adopcję przez `ProbeExisting`, `RenewSession` po wybudzeniu oraz minimalny PageRuntime (`hello`, binding) z buildem page bundle do `<install>/page/`, który rozszerza epic-netflix-dpad. Z CAP-2 bierze weryfikację J4 na UM760.

## Outcome

Przeglądarka serwisu jest w pełni zarządzana przez MediaStation, a Eliza naprawia zawieszoną aplikację z pilota (J4); sygnałem jest success CAP-7 i J4.

## Done when

1. Edge startuje z profilem `profiles\<appId>`, portem CDP na loopbacku, flagami i zakazami z AD-1 i jedną flagą trybu okna; `navigator.webdriver` ma wartość `false` (AD-1).
2. Binding wraca po każdej nawigacji głównej ramki; zerwane CDP przy żywym procesie wraca przez reconnect, a po `cdpReconnectTimeout` aplikacja przechodzi w `Failed`, przy czym Home działa cały czas (AD-1, AD-10).
3. Wersja przeglądarki jest odczytywana spoza CDP przed każdym startem; zmiana daje w logu `BrowserVersionChanged`, także gdy port CDP nie odpowiada (AD-13).
4. `CdpUnavailable` jest odróżnialne od timeoutu startu i `ProcessExited`, a okno i tak jest podpięte, wyciszone i zminimalizowane; zajęty port albo profil daje `PortInUse`/`ProfileInUse` bez startu (AD-4, AD-13).
5. Na UM760 żywy Edge jest adoptowany po restarcie MediaStation, a jedno OK na kafelku `Failed` i jedno OK na „Uruchom ponownie” przywracają działającą aplikację (J4) (CAP-2, AD-10).

## Boundaries

`Hosts/Browser` i minimalny `web/page/runtime`. Nie: ServiceDriver Netflixa i silnik fokusu (epic-netflix-dpad), plan C z rozszerzeniem (tylko po triggerach z AD-1), exit codes `--smoke` (epic-deploy-resilience).

## References

- parent — _bmad-output/initiative-mediastation-mvp/spec-mediastation/spec-mediastation.md, Capabilities CAP-7, CAP-2 (J4), Open Questions (tryb okna, źródło wersji)
- constraint — ten sam spec, Constraints (własny launch i surowy CDP, zakazane flagi)
- architecture — _bmad-output/initiative-mediastation-mvp/architecture-mediastation/architecture-mediastation.md, AD-1, AD-4, AD-6, AD-10, AD-13 (Aktualizacja przeglądarki i dostępność CDP), AD-15
- research — _bmad-output/initiative-mediastation-mvp/research-browser-control-for-streaming-services/digests/spike-um760.md, claims S1, S2, S4, binding
- ux — _bmad-output/initiative-mediastation-mvp/ux-mediastation/EXPERIENCE.md, Key Flows J4

## Notes

- Open question: tryb okna przeglądarki (`--app`, `--start-fullscreen` czy `--kiosk`). Rozstrzyga story 1 tej epiki, przed stories epic-windows-audio i epic-osk.
- Open question: źródło wersji przeglądarki poza CDP (bieżącej przed startem i poprzedniej w `profiles\<appId>\`); kandydaci: `FileVersion` binarki i `Last Version` w `user-data-dir`. Rozstrzyga ta sama story 1.
- Decision: story 1 to spike bez prerequisites (Edge na UM760), wyprzedza epic-windows-audio i epic-osk; wynik wraca do AD-1 i AD-13 w spine (2026-10-18).
- Decision: weryfikacja J4 na UM760 z CAP-2 jest tutaj (2026-10-18).
- Decision: ta epika jest właścicielem minimalnego PageRuntime (`hello`, binding) i buildu page bundle; epic-netflix-dpad go rozszerza (2026-10-18).
- Waits on epic-windows-audio because: okno przeglądarki bez CDP jest wyciszane i minimalizowane (AD-5) przez WindowManager i AudioRouter.
- Waits on epic-home-screen because: AppMenu dla J4.
- Deferred: rekord koperty nie ma pola `id?` z architektury (`{ v, type, id?, payload }`), a odczyt dziś `id` ignoruje (triage 1.1 #3). Źródło: F6 w retro epic-foundation-contracts (`_bmad-output/initiative-mediastation-mvp/epic-foundation-contracts/epic-foundation-contracts-retrospective.md`). Warunek: pierwsza story z request/response w protokole strony (PageRuntime) dodaje `id` do koperty (2026-10-20).
- Deferred: `service` w `apps.json` nie musi być kebab-case, choć jest segmentem ścieżki `web/page/drivers/<service>`. Źródło: F18 w retro epic-foundation-contracts (`_bmad-output/initiative-mediastation-mvp/epic-foundation-contracts/epic-foundation-contracts-retrospective.md`). Warunek: pierwsza story, która wybiera driver albo ścieżkę page bundle po `service`, tu albo w epic-netflix-dpad (2026-10-20).
- Deferred: `cdpConnectTimeout` i `cdpReconnectTimeout` w `settings.json` nie mają górnej granicy. Źródło: F18 w retro epic-foundation-contracts (`_bmad-output/initiative-mediastation-mvp/epic-foundation-contracts/epic-foundation-contracts-retrospective.md`); retro wskazało epic-deploy-resilience, tu, bo BrowserHost jest pierwszym konsumentem. Warunek: story, która pierwsza używa któregoś z tych czasów (2026-10-20).
