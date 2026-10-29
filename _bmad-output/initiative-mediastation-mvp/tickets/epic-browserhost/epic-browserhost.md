---
type: epic
title: "BrowserHost"
parent: initiative-mediastation-mvp
covers: [CAP-7, CAP-2]
after: []
assignee: ""
risk: high
refined: true
---

# BrowserHost

## Description

CAP-7: MediaStation uruchamia przeglądarkę dobraną pod serwis, z osobnym profilem, i steruje nią bez śladu automatyzacji. Epika dostarcza własny launch Edge/Chrome, surowy klient CDP na loopbacku, ponawiany binding, reconnect, odczyt wersji spoza CDP, rozróżnialne błędy startu, adopcję przez `ProbeExisting`, `RenewSession` po wybudzeniu oraz minimalny PageRuntime (`hello`, binding) z buildem page bundle do `<install>/page/`, który rozszerza epic-netflix-dpad. Z CAP-2 bierze weryfikację J4 na UM760.

## Outcome

Przeglądarka serwisu jest w pełni zarządzana przez MediaStation, a Eliza naprawia zawieszoną aplikację z pilota (J4); sygnałem jest success CAP-7 i J4.

## Requirements

1. **Uruchomienie i sterowanie bez śladu automatyzacji** (CAP-7, AD-1)
   **Given** wpis web w `apps.json` wskazuje przeglądarkę, profil i port CDP
   **When** Orchestrator uruchamia aplikację
   **Then** BrowserHost startuje właściwą binarkę z osobnym profilem, `--kiosk`, CDP na `127.0.0.1` i flagami AD-1, a `navigator.webdriver` pozostaje `false`
   **And** zakazane mechanizmy (`Runtime.enable`, Playwright, `--remote-allow-origins`, nasłuch poza loopbackiem) nie są używane

2. **Binding i reconnect CDP** (CAP-7, AD-1, AD-10)
   **Given** główna ramka przechodzi nawigację albo żywy proces traci połączenie CDP
   **When** BrowserHost obsługuje zmianę
   **Then** binding PageRuntime wraca po każdej nawigacji, a reconnect ponawia się do `cdpReconnectTimeout`
   **And** po wyczerpaniu limitu aplikacja przechodzi do `Failed`, Home pozostaje dostępny, a D-pad nie jest buforowany

3. **Adopcja i odnowienie sesji** (CAP-2, AD-10)
   **Given** MediaStation startuje ponownie albo wraca po wybudzeniu
   **When** żywy proces przeglądarki ma profil aplikacji
   **Then** `ProbeExisting` adoptuje jego okno i proces bez drugiego startu, a `RenewSession` odnawia binding/CDP
   **And** spóźnione fakty starej sesji nie zmieniają nowego `transitionId`

4. **Wersja i rozróżnialne błędy startu** (CAP-7, AD-4, AD-13)
   **Given** BrowserHost przygotowuje start albo napotyka błąd procesu, portu lub profilu
   **When** odczytuje wersję i wykonuje start
   **Then** porównuje `FileVersion` binarki z `Last Version` profilu przed startem i loguje `BrowserVersionChanged` przy zmianie, także bez odpowiedzi CDP
   **And** rozróżnia `CdpUnavailable`, timeout startu, `ProcessExited`, `PortInUse` i `ProfileInUse`; przy `CdpUnavailable` okno jest mimo to podpięte, wyciszone i zminimalizowane

5. **Minimalny PageRuntime i pakiet strony** (CAP-7, AD-6, AD-7)
   **Given** przeglądarka nawiguje do strony usługi
   **When** bundle strony jest ładowany
   **Then** wspólny PageRuntime wysyła `hello`, obsługuje binding, buforuje komunikaty przed bindingiem i buduje artefakt do `<install>/page/`
   **And** koperta komunikatu ma `v: 1`, `id` dla komunikatów host → page i ignoruje spóźnione odpowiedzi

6. **Naprawa J4 z pilota** (CAP-2, CAP-6, AD-10)
   **Given** kafelek przeglądarki jest `Failed`
   **When** Eliza naciska OK, wybiera „Uruchom ponownie” i zatwierdza
   **Then** AppMenu prowadzi przez `Stopping → Starting` do działającej aplikacji na pierwszym planie jednym przebiegiem
   **And** ponowna awaria wraca do `Failed`, a „Zamknij” pozostaje dostępne bez eskalacji

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

- Decision: story 1 (spike) rozstrzygnęła `--kiosk` jako tryb okna oraz `FileVersion` binarki + `Last Version` profilu jako źródła wersji; pomiar wykonano na UM760 (2026-10-27).
- Decision: weryfikacja J4 na UM760 z CAP-2 jest tutaj (2026-10-18).
- Decision: ta epika jest właścicielem minimalnego PageRuntime (`hello`, binding) i buildu page bundle; epic-netflix-dpad go rozszerza (2026-10-18).
- Waits on epic-windows-audio because: okno przeglądarki bez CDP jest wyciszane i minimalizowane (AD-5) przez WindowManager i AudioRouter.
- Waits on epic-home-screen because: AppMenu dla J4.
- Deferred: rekord koperty nie ma pola `id?` z architektury (`{ v, type, id?, payload }`), a odczyt dziś `id` ignoruje (triage 1.1 #3). Źródło: F6 w retro epic-foundation-contracts (`_bmad-output/initiative-mediastation-mvp/tickets/epic-foundation-contracts/epic-foundation-contracts-retrospective.md`). Warunek: pierwsza story z request/response w protokole strony (PageRuntime) dodaje `id` do koperty (2026-10-20).
- Deferred: `service` w `apps.json` nie musi być kebab-case, choć jest segmentem ścieżki `web/page/drivers/<service>`. Źródło: F18 w retro epic-foundation-contracts (`_bmad-output/initiative-mediastation-mvp/tickets/epic-foundation-contracts/epic-foundation-contracts-retrospective.md`). Warunek: pierwsza story, która wybiera driver albo ścieżkę page bundle po `service`, tu albo w epic-netflix-dpad (2026-10-20).
- Deferred: `cdpConnectTimeout` i `cdpReconnectTimeout` w `settings.json` nie mają górnej granicy. Źródło: F18 w retro epic-foundation-contracts (`_bmad-output/initiative-mediastation-mvp/tickets/epic-foundation-contracts/epic-foundation-contracts-retrospective.md`); retro wskazało epic-deploy-resilience, tu, bo BrowserHost jest pierwszym konsumentem. Warunek: story, która pierwsza używa któregoś z tych czasów (2026-10-20).
- Decision: wpis 1 pozostaje pierwszym, ukończonym spike'em; nowe wpisy przypinają zależności do gotowych epik lub wpisu 1, więc drzewo nie ma `unpinned_after` (2026-10-29).
- Decision: ledger DW-3 (spóźniony `ProcessExited` po `RestartApp`) i DW-4 (spóźniony `AppWindowAttached` po timeoucie `Starting`, z decyzją o `Hwnd` w `Failed`) przypisane tej epice jako pierwszej z prawdziwym hostem (refinement epiki 4, 2026-10-26).
- Update: spike 6.1 (`2ea151f`, 2026-10-27, UM760) rozstrzygnął tryb okna przeglądarki: `--kiosk` (AD-1), a źródło wersji poza CDP: `FileVersion` binarki i `Last Version` w `user-data-dir` (AD-13). Pytania o tryb okna w tych Notes są zamknięte; DW-38 (2026-10-27).
- Decision: tracer epiki to start Edge → surowy CDP → PageRuntime `hello` → podpięcie okna → powrót Home; nowe stories rozwijają ten przepływ, a końcowy sweep zamyka dług techniczny (2026-10-29).
- Decision: BrowserHost zapisuje wersję odczytaną przed startem i porównuje ją z `Browser.getVersion`, aby nie zgubić zmiany po awarii między startem a logiem (2026-10-29).
- Decision: weryfikacja J4 na UM760 jest częścią odbioru całej epiki i pozostaje po stronie użytkownika; per-ticket automated evidence działa na bieżącej maszynie (2026-10-29).
