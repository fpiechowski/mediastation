---
type: initiative
title: "MediaStation MVP"
parent: none
covers: [CAP-1, CAP-2, CAP-3, CAP-4, CAP-5, CAP-6, CAP-7, CAP-8, CAP-9, CAP-10, CAP-11, CAP-12]
after: []
assignee: ""
risk: high
---

# MediaStation MVP

## Description

UM760 pod TV przejmuje rolę TV boxa: pełnoekranowy shell obsługiwany pilotem, Netflix w Edge sterowany D-padem przez CDP, audio focus, OSK, Moonlight i aplikacje natywne oraz „Do oceny” z QR do Filmwebu. Kontrakt jest w specu `spec-mediastation` i jego companions (architecture spine, `EXPERIENCE.md`, `DESIGN.md`, digest spike'a UM760); ta inicjatywa dostarcza CAP-1–CAP-12 w jedenastu epikach, w kolejności z `tickets.toml`.

## Outcome

Eliza, nietechniczna codzienna użytkowniczka VIDAA, sama wybiera MediaStation do Netflixa; sygnałem jest success signal specu (7 kolejnych seansów, tydzień bez prośby o pomoc).

## Done when

1. Success signal specu: Eliza przez 7 kolejnych seansów z Netflixem sama wybiera MediaStation zamiast VIDAA i przez tydzień ani razu nie prosi autora o pomoc.
2. Na UM760 przy TV cały wieczór z Netflixem (profil, przeglądanie, wyszukiwanie, odtwarzanie, Back, wyjście) przechodzi D-padem, bez trybu myszy, w co najmniej 1080p.
3. Home zawsze przywraca ekran główny, także gdy fokus ukradło inne okno, przy zawieszonym UI i przy zerwanym CDP.
4. Każda capability CAP-1–CAP-12 działa w buildzie zainstalowanym przez `tools/install.ps1` i startującym „at logon” z `--supervise`.

## Boundaries

Granica: capabilities specu, jedna na epikę, poza CAP-4 + CAP-5 (jeden właściciel i moduł `Platform/Window+Audio`). Jeden właściciel (autor) z lane'ami agentów, jedna maszyna produkcyjna (UM760). Poza zakresem: Non-goals specu; autologowanie, ukrycie pulpitu i polityka Windows Update konfiguruje autor poza produktem (AD-13). Tracer path: E1 → E4 daje ekran główny z pilota na TV; E7 daje pierwszy wieczór z Netflixem.

- Touch point: Netflix (serwis, tylko konsumowany przez stronę) — owner: epic-netflix-dpad
- Touch point: Microsoft Edge / Chrome (przeglądarka evergreen, uruchamiana i sterowana) — owner: epic-browserhost
- Touch point: wyszukiwarka filmweb.pl (tylko cel URL w QR) — owner: epic-do-oceny
- Touch point: Moonlight-qt 6.2.0 i host Sunshine/Apollo w LAN (klient uruchamiany z CLI, host konfigurowany) — owner: epic-moonlight-native
- Touch point: Plex HTPC (uruchamiany przez `Process.Start`) — owner: epic-moonlight-native
- Touch point: Windows Task Scheduler (zadanie „at logon”) — owner: epic-deploy-resilience
- Touch point: `spike/` (zamrożona referencja zachowania, nie budowana) — owner: epic-foundation-contracts

## References

- spec — _bmad-output/initiative-mediastation-mvp/spec-mediastation/spec-mediastation.md, sekcje Capabilities, Constraints, Non-goals, Open Questions
- constraint — ten sam spec, sekcja Constraints (prywatność OSK i Filmwebu, jedna powierzchnia 1920×1080, zakazy sterowania przeglądarką)
- architecture — _bmad-output/initiative-mediastation-mvp/architecture-mediastation/architecture-mediastation.md (AD-1–AD-16, Stack, Structural Seed)
- ux — _bmad-output/initiative-mediastation-mvp/ux-mediastation/EXPERIENCE.md, _bmad-output/initiative-mediastation-mvp/ux-mediastation/DESIGN.md
- research — _bmad-output/initiative-mediastation-mvp/research-browser-control-for-streaming-services/digests/spike-um760.md

## Notes

- Decision: podział na 11 epik wzdłuż CAP-N, CAP-4 i CAP-5 w jednej epice; kolejność wg łańcucha zależności specu (zaakceptowane przez użytkownika, 2026-10-18).
- Decision: story 1 epiki BrowserHost (tryb okna przeglądarki i źródło wersji poza CDP) wyprzedza stories epik Windows & audio focus i OSK, zgodnie ze specem. `tickets.py` zgłosi `order_conflict` epic-windows-audio → epic-browserhost; jest zamierzony, kolejność epik zostaje ze specu (2026-10-18).
- Decision: weryfikacja J4 na UM760 z CAP-2 przechodzi do epiki BrowserHost, bo wymaga AppMenu (CAP-6) i prawdziwego hosta (CAP-7); Core testy CAP-2 zostają w epic-orchestrator-lifecycle (2026-10-18).
- Decision: logger JSON Lines (UTC, rotacja dzienna) z CAP-12 dostarcza epic-foundation-contracts, bo checki CAP-3 i CAP-9 czytają logi; epic-deploy-resilience go nie powtarza (2026-10-18).
- Decision: bez CI; do epiki Deploy & resilience build trafia na UM760 przez ręczne `dotnet publish` i kopię (2026-10-18).
- Decision: CAP-2 i CAP-3 idą równolegle po E1 — rekord `SetInputRouting` żyje w kontraktach Core z E1 (2026-10-18).
- Open question: osiem Open Questions specu ma przypisane epiki i wczesne stories spike/pomiarowe — szczegóły w Notes każdej epiki.
