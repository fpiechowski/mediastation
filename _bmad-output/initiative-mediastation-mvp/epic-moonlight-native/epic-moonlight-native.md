---
type: epic
title: "Moonlight & native apps"
parent: initiative-mediastation-mvp
covers: [CAP-10, CAP-5]
after: []
assignee: ""
risk: medium
---

# Moonlight & native apps

## Description

CAP-10: Moonlight i Plex (oraz inne aplikacje natywne) startują z kafelka i wracają do ekranu głównego, a sesja gry przeżywa Home. Epika dostarcza MoonlightHost (launch borderless z CLI, `quit` z limitem, adopcja przez `ProbeExisting`) i NativeAppHost (`Process.Start` z `apps.json`) oraz routing `app + moonlight/native` z polityką `back`. Z CAP-5 bierze produktowe powtórzenie checku audio przez kafelki.

## Outcome

UM760 zostaje klientem Moonlight i uruchamia Plexa bez wychodzenia z MediaStation; sygnałem jest success CAP-10.

## Done when

1. Moonlight startuje borderless, bez przełączania trybu monitora; Back trafia do gry; Home chowa i wycisza Moonlighta, a sesja po powrocie żyje (AD-5, AD-8, AD-10).
2. „Zamknij” kończy `quit`, a po 30 s MediaStation zamyka klienta (AD-10).
3. Plex startuje z kafelka, a Back i Home wracają do ekranu głównego (AD-8, AD-11).
4. Na UM760 żywy Moonlight jest adoptowany po restarcie MediaStation, a po przełączeniu Netflix ↔ Moonlight kafelkami słychać tylko aplikację na pierwszym planie (CAP-5, AD-5, AD-10).

## Boundaries

`Hosts/Moonlight`, `Hosts/Native`. Nie: D-pad w Plexie, kafelki per host i gra, HDR i przełączanie trybów obrazu (Non-goals).

## References

- parent — _bmad-output/initiative-mediastation-mvp/spec-mediastation/spec-mediastation.md, Capabilities CAP-10, CAP-5
- architecture — _bmad-output/initiative-mediastation-mvp/architecture-mediastation/architecture-mediastation.md, AD-5, AD-8, AD-10, AD-11, AD-15
- research — _bmad-output/initiative-mediastation-mvp/research-browser-control-for-streaming-services/digests/spike-um760.md, claims S10, S11

## Notes

- Decision: produktowe powtórzenie checku CAP-5 (przez kafelki, z prawdziwymi hostami) jest tutaj; epic-windows-audio sprawdza go na dev harness (2026-10-18).
- Waits on epic-osk because: wspólny kod polityki pointer w InputHub.
- Waits on epic-do-oceny because: kolejność specu (CAP-11 → CAP-10) i wspólne rozszerzenia Orchestratora.
- Waits on epic-netflix-dpad because: ścieżka pauzy Netflixa w checku audio.
