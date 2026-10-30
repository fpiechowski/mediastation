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

CAP-9: gdy trzeba coś wpisać w Netflixie, Plexie, UI Moonlighta albo polu MediaStation, pojawia się jedna klawiatura ekranowa MediaStation, obsługiwana D-padem i opcjonalnie kursorem pilota, z polskimi znakami. Epika dostarcza okno nakładki OSK (albo fallback rysowany przez PageRuntime dla webu), tryb `osk` w routingu, `ShowOsk{ target }` i `HideOsk` z obsługą Action `Keyboard` w Orchestratorze, `TextSink` z dwiema drogami — `web` (CDP `Input.insertText`) i `foreground` (`SendInput` Unicode) — oraz komunikat `osk{active}` do strony.

## Outcome

Wyszukiwanie w Netflixie (J3) przechodzi pilotem, bez wycieku wpisanego tekstu; sygnałem jest success CAP-9.

## Done when

1. OSK pojawia się automatycznie na `textFocus` w Netflixie i nie zabiera fokusu z pola (AD-9).
2. Każdy znak od razu trafia do strony i wyniki `/search?q=` się aktualizują; long OK wstawia polski wariant bez litery bazowej (AD-8, AD-9).
3. Na ekranie jest jeden fokus, a kursor nie wychodzi poza okno OSK (AD-8, AD-9).
4. Back zamyka rząd wariantów, a potem OSK; Home zamyka OSK i pokazuje ekran główny (AD-8).
5. Po sesji wyszukiwania na UM760 wpisany tekst nie występuje w logach, Intents ani view-modelu (AD-9, AD-13).
6. Action `Keyboard` otwiera i zamyka OSK według tabeli routingu AD-8; w `shell` bez zgłoszonego `textFocus` nic nie robi (AD-8, AD-9).
7. `TextSink` `foreground` wpisuje znaki, w tym polskie warianty, do okna na pierwszym planie przez `SendInput` Unicode ze znacznikiem `dwExtraInfo`, a hook ich nie łapie; sprawdzone na dev harness z Notatnikiem (AD-8, AD-9).

## Boundaries

Okno OSK w `Shell/`, tryb `osk` w InputHub, `TextSink` (`web` w BrowserHost, `foreground` w `Platform/Input`), `ShowOsk`/`HideOsk` w Orchestratorze i wysłanie `osk` do strony (obsługę w PageRuntime daje epic-netflix-dpad). Nie: weryfikacja nad prawdziwym Plexem i UI Moonlighta (epic-moonlight-native); OSK w sesji streamu (Non-goals).

## References

- parent — _bmad-output/initiative-mediastation-mvp/spec-mediastation/spec-mediastation.md, Capabilities CAP-9, Open Questions (nakładka OSK), Constraints (prywatność)
- architecture — _bmad-output/initiative-mediastation-mvp/architecture-mediastation/architecture-mediastation.md, AD-7, AD-8, AD-9, AD-15
- ux — _bmad-output/initiative-mediastation-mvp/ux-mediastation/EXPERIENCE.md, Key Flows J3, Interaction Primitives → OSK, Component Patterns → OSK; _bmad-output/initiative-mediastation-mvp/ux-mediastation/DESIGN.md (OSK, `input-active`)

## Notes

- Open question: czy nakładka OSK działa nad pełnoekranowym Edge (fallback: OSK rysowana przez PageRuntime). Rozstrzyga story 1 tej epiki, po story 1 epic-browserhost (tryb okna).
- Decision: dwie drogi wpisywania zostają — CDP `Input.insertText` dla webu, `SendInput` Unicode dla `foreground`; jedna droga przez `SendInput` odrzucona (change-osk-everywhere-github-update, 2026-10-18).
- Decision: nakładkę nad pełnoekranowym Plexem i polskie znaki przez `SendInput` w Plexie i UI Moonlighta rozstrzyga story „OSK w Plexie i UI Moonlighta” w epic-moonlight-native; ta epika sprawdza `foreground` na dev harness (2026-10-18).
- Waits on epic-input because: tryb `osk` w routingu, pointer i `ClipCursor`, Action `Keyboard` z mapowaniem z `input.json`.
- Waits on epic-browserhost because: story 1 ustala tryb okna, od którego zależy nakładka; `Input.insertText`.
- Waits on epic-netflix-dpad because: `textFocus` z drivera i obsługa `osk` w PageRuntime.
- Decision: ledger DW-6, DW-10, DW-11, DW-17, DW-22 i DW-25 przypisane tej epice; treść jest w `deferred-work.md` (refinement epiki 4, 2026-10-26).
- Update: spike 6.1 (`2ea151f`, 2026-10-27, UM760) rozstrzygnął tryb okna przeglądarki: `--kiosk` (AD-1), a źródło wersji poza CDP: `FileVersion` binarki i `Last Version` w `user-data-dir` (AD-13). Pytania o tryb okna w tych Notes są zamknięte; DW-38 (2026-10-27). Otwarte zostaje pytanie o nakładkę OSK nad pełnoekranowym Edge w `--kiosk` (DW-40).
- Open question: Done when 7 zakłada dev harness z Notatnikiem, a po wpisie 5 epic-netflix-dpad harness obsługuje tylko `moonlight`, a `native` to `MissingHostPort`; story `TextSink` `foreground` musi nazwać sposób uruchomienia Notatnika (inception epic-netflix-dpad, 2026-10-30).
