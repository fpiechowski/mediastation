---
type: epic
title: "Windows & audio focus"
parent: initiative-mediastation-mvp
covers: [CAP-4, CAP-5]
after: []
assignee: ""
risk: high
---

# Windows & audio focus

## Description

CAP-4 i CAP-5: jeden komponent decyduje o widoczności, Z-order i foreground każdego okna, więc ekran główny wraca nad dowolne okno, a słychać tylko aktywną aplikację. Epika dostarcza WindowManager (jedyny właściciel foreground, raportuje `ForegroundChanged`), AudioRouter (wyciszanie drzewa procesów od `rootPid`, ponawiane przy nowej sesji audio) i sekwencję przełączania w Orchestratorze. Okna Edge, Chrome i Moonlighta do weryfikacji uruchamia dev harness, bo hosty powstają w późniejszych epikach.

## Outcome

Home zawsze przywraca ekran główny i nic nie gra z tła; sygnałem są success CAP-4 i CAP-5.

## Done when

1. Opuszczona przeglądarka jest zminimalizowana, nigdy `SW_HIDE` (AD-5).
2. Na UM760 Home przywraca shell nad Edge, Chrome i Moonlightem (borderless), z odblokowaniem foreground, 16/16 (AD-5, AD-15).
3. Zmiana foreground bez udziału MediaStation trafia do Orchestratora jako `ForegroundChanged`, a host nigdy sam nie wynosi okna na wierzch (AD-14, AD-15).
4. Na UM760 po przełączeniu Netflix ↔ Moonlight i z powrotem słychać wyłącznie aplikację na pierwszym planie, także po nowej sesji audio w drzewie przeglądarki (np. zmiana odcinka) (AD-5, AD-15).

## Boundaries

`Platform/Window`, `Platform/Audio` i sekwencja przełączania w Orchestratorze. Nie: launch przeglądarki i Moonlighta (epic-browserhost, epic-moonlight-native), pauza przez stronę (epic-netflix-dpad). Produktowe powtórzenie checku audio przez kafelki robi epic-moonlight-native.

## References

- parent — _bmad-output/initiative-mediastation-mvp/spec-mediastation/spec-mediastation.md, Capabilities CAP-4, CAP-5, Open Questions (nowe sesje audio)
- architecture — _bmad-output/initiative-mediastation-mvp/architecture-mediastation/architecture-mediastation.md, AD-5, AD-14, AD-15
- research — _bmad-output/initiative-mediastation-mvp/research-browser-control-for-streaming-services/digests/spike-um760.md, claims S9, S10, S11

## Notes

- Open question: nowe sesje audio po zmianie odcinka (S9 `-Watch` niezmierzony). Rozstrzyga wczesna story pomiarowa AudioRoutera, na spike/dev harness z Netflixem w Edge.
- Decision: stories tej epiki zależne od trybu okna czekają na story 1 epic-browserhost; `order_conflict` z `tickets.py` jest zamierzony (2026-10-18).
- Decision: check 2 i 4 na UM760 używa okien uruchomionych przez dev harness; produktowe powtórzenie audio jest w epic-moonlight-native (2026-10-18).
- Waits on epic-browserhost because: story 1 ustala flagę trybu okna, od której zależy obsługa okna przeglądarki.
- Waits on epic-home-screen because: wspólny kod hosta Shell i okno ekranu głównego do przywracania.
