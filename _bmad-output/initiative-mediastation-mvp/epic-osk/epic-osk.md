---
type: epic
title: "On-screen keyboard (OSK)"
parent: initiative-mediastation-mvp
covers: [CAP-9]
after: []
assignee: ""
risk: high
---

# On-screen keyboard (OSK)

## Description

CAP-9: gdy w serwisie webowym trzeba coś wpisać, pojawia się jedna klawiatura ekranowa MediaStation, obsługiwana D-padem i opcjonalnie kursorem pilota, z polskimi znakami. Epika dostarcza okno nakładki OSK (albo fallback rysowany przez PageRuntime), tryb `osk` w routingu, wstawianie znaków przez CDP `Input.insertText` i komunikat `osk{active}` do strony.

## Outcome

Wyszukiwanie w Netflixie (J3) przechodzi pilotem, bez wycieku wpisanego tekstu; sygnałem jest success CAP-9.

## Done when

1. OSK pojawia się automatycznie na `textFocus` w Netflixie i nie zabiera fokusu z pola (AD-9).
2. Każdy znak od razu trafia do strony i wyniki `/search?q=` się aktualizują; long OK wstawia polski wariant bez litery bazowej (AD-8, AD-9).
3. Na ekranie jest jeden fokus, a kursor nie wychodzi poza okno OSK (AD-8, AD-9).
4. Back zamyka rząd wariantów, a potem OSK; Home zamyka OSK i pokazuje ekran główny (AD-8).
5. Po sesji wyszukiwania na UM760 wpisany tekst nie występuje w logach, Intents ani view-modelu (AD-9, AD-13).

## Boundaries

Okno OSK w `Shell/`, tryb `osk` w InputHub i obsługa `osk` w PageRuntime. Nie: OSK w Moonlight i aplikacjach natywnych (Action `Keyboard`, Non-goals).

## References

- parent — _bmad-output/initiative-mediastation-mvp/spec-mediastation/spec-mediastation.md, Capabilities CAP-9, Open Questions (nakładka OSK), Constraints (prywatność)
- architecture — _bmad-output/initiative-mediastation-mvp/architecture-mediastation/architecture-mediastation.md, AD-7, AD-8, AD-9, AD-15
- ux — _bmad-output/initiative-mediastation-mvp/ux-mediastation/EXPERIENCE.md, Key Flows J3; _bmad-output/initiative-mediastation-mvp/ux-mediastation/DESIGN.md (OSK, `input-active`)

## Notes

- Open question: czy nakładka OSK działa nad pełnoekranowym Edge (fallback: OSK rysowana przez PageRuntime). Rozstrzyga story 1 tej epiki, po story 1 epic-browserhost (tryb okna).
- Waits on epic-browserhost because: story 1 ustala tryb okna, od którego zależy nakładka; `Input.insertText`.
- Waits on epic-netflix-dpad because: `textFocus` z drivera i obsługa `osk` w PageRuntime.
