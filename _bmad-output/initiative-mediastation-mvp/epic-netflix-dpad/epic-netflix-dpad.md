---
type: epic
title: "Netflix by D-pad"
parent: initiative-mediastation-mvp
covers: [CAP-8]
after: []
assignee: ""
risk: high
---

# Netflix by D-pad

## Description

CAP-8: cały wieczór z Netflixem obsługuje się D-padem, z wstrzykniętym fokusem i Back zachowującym się jak na TV, a pauza, wznowienie i fakty o odtwarzaniu idą przez protokół strony. Epika rozszerza PageRuntime z epic-browserhost (silnik fokusu, podświetlenie `focus-ring-injected`, nasłuch `<video>`), dostarcza ServiceDriver `netflix` z `manifest.json` i fixture'ami DOM, BackPolicy oraz `--smoke netflix`.

## Outcome

Eliza ogląda Netflixa od profilu do wyjścia samym pilotem; sygnałem są techniczne warunki success signal specu i success CAP-8.

## Done when

1. Vitest na fixture'ach DOM Netflixa pokrywa rozpoznanie ekranów, regułę Back, fokus na profilu bez wyboru i na głównej akcji ekranów `limit` i `error`; fakt `playback` niesie pola potrzebne epic-do-oceny: `title` serialu przy odcinku, `seriesId`, `lastInSeason`, `position`, `duration` (AD-6, AD-7, AD-12).
2. `--smoke netflix` przechodzi na UM760: start, `hello`, profil, odtwarzanie ≥ 1080p bez błędu DRM, pauza i reguła Back (AD-13).
3. Powrót do Netflixa zastaje odcinek na pauzie, a OK go wznawia (AD-5).
4. Wynik pomiaru pauzy wielogodzinnej jest zapisany, a jeśli wymaga polityki w Core, change do `bmad-architecture` jest rozstrzygnięty.
5. Na TV z UM760 cały wieczór (profil, przeglądanie, odtwarzanie, Back, wyjście) przechodzi D-padem bez trybu myszy; wyszukiwanie domyka epic-osk.

## Boundaries

`web/page/runtime` (rozszerzenie) i `web/page/drivers/netflix`. Nie: OSK (epic-osk), `RatingPolicy` (epic-do-oceny), kolejne serwisy S12 (Non-goals), automatyczna obsługa awarii (Non-goals).

## References

- parent — _bmad-output/initiative-mediastation-mvp/spec-mediastation/spec-mediastation.md, Capabilities CAP-8, Open Questions (pauza wielogodzinna), Success signal
- architecture — _bmad-output/initiative-mediastation-mvp/architecture-mediastation/architecture-mediastation.md, AD-5, AD-6, AD-7, AD-10 (wybudzenie), AD-12, AD-13 (Smoke, Hotfix drivera)
- research — _bmad-output/initiative-mediastation-mvp/research-browser-control-for-streaming-services/digests/spike-um760.md, claims S6, S7, S8
- ux — _bmad-output/initiative-mediastation-mvp/ux-mediastation/EXPERIENCE.md, Key Flows J1, J2; _bmad-output/initiative-mediastation-mvp/ux-mediastation/DESIGN.md, `focus-ring-injected`

## Notes

- Open question: pauza wielogodzinna bez uśpienia PC (S8 zmierzył 30 min). Rozstrzyga story pomiarowa: S8 dla kilku godzin, dwa warianty pauzy i chowania okna, fixture'y nowych ekranów błędu. Wynik wymagający polityki w Core wraca do `bmad-architecture` jako change. Ścieżka po Modern Standby jest ustalona (AD-10).
- Decision: story pomiarowa pauzy jest pierwsza w breakdownie tej epiki i nie ma prerequisites (spike harness), żeby wynik i ewentualny change do spine'u przyszły jak najwcześniej. Można incepcjonować tę epikę wcześniej niż wynika z kolejności, żeby ją uruchomić równolegle z wcześniejszymi epikami (2026-10-18).
- Waits on epic-browserhost because: minimalny PageRuntime, build page bundle, CDP `Input.dispatchKeyEvent`.
