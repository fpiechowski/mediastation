---
type: epic
title: "„Do oceny”"
parent: initiative-mediastation-mvp
covers: [CAP-11, CAP-6]
after: []
assignee: ""
risk: medium
---

# „Do oceny”

## Description

CAP-11: po obejrzanym filmie albo sezonie serialu, na dowolnym profilu Netflixa, na ekranie głównym czeka karta z kodem QR do wyszukiwarki Filmwebu. Epika dostarcza `RatingPolicy` w Core, Store (jeden dokument `data/rating-queue.json` ze `schemaVersion` i zapisem atomowym), kartę na ekranie głównym z QR generowanym lokalnie (bez skracacza) i akcje „Ocenione” / „Pomiń”. Z CAP-6 bierze check „nowa karta nie kradnie fokusu”.

## Outcome

Eliza ma powód, żeby woleć MediaStation od VIDAA: ocenia obejrzany tytuł telefonem, bez szukania i pamiętania; sygnałem jest success CAP-11.

## Done when

1. Testy `RatingPolicy` pokrywają reguły kolejki z success CAP-11: film, serial z `lastInSeason`, `pending` po `ratingPendingDays`, „raz na zawsze” także po „Pomiń”, tożsamość serialu z i bez `seriesId` (AD-12).
2. Fakt `playback` sprzed `StoreLoaded` trafia do `RatingPolicy` po wczytaniu kolejki (AD-12).
3. „Ocenione” i „Pomiń” usuwają kartę bez undo, a nowa karta nie kradnie fokusu (AD-12, AD-16).
4. Na TV z UM760 QR z karty skanuje się z 3 m dwoma telefonami i otwiera wyszukiwarkę Filmwebu z tytułem (AD-12).

## Boundaries

`RatingPolicy` w Core, `App/Store`, karta w `web/shell`. Nie: konto Filmwebu (nie przechowujemy), strona tytułu przez Wikidata, toast nad playerem, undo, ocena z pilota (Non-goals).

## References

- parent — _bmad-output/initiative-mediastation-mvp/spec-mediastation/spec-mediastation.md, Capabilities CAP-11, CAP-6 (karta), Open Questions (kontrole z forge i UX), Constraints (prywatność, trwały stan)
- architecture — _bmad-output/initiative-mediastation-mvp/architecture-mediastation/architecture-mediastation.md, AD-7 (`playback`), AD-12, AD-14, AD-16
- ux — _bmad-output/initiative-mediastation-mvp/ux-mediastation/EXPERIENCE.md, sekcja „Do oceny”
- forge — _bmad-output/initiative-mediastation-mvp/forge-filmweb-rating-prompt/forge-filmweb-rating-prompt.md

## Notes

- Open question: kontrole z forge i UX — czy QR otwiera aplikację Filmweb, czy przeglądarkę; czy wyszukiwarka Filmwebu znajduje tytuł w języku profilu; skan QR (ECC L) z 3 m dwoma telefonami. Rozstrzyga wczesna story spike na prawdziwym TV; dokładny format URL ustala story (AD-12).
- Decision: linię success CAP-6 „nowa karta nie kradnie fokusu” weryfikuje ta epika (2026-10-18).
- Decision: buforowanie faktów `playback` w Orchestratorze do `StoreLoaded` (AD-12) dostarcza ta epika, nie epic-orchestrator-lifecycle; z epiki 2 bierze tylko `TimerElapsed` (inception epiki 2, 2026-10-21).
- Waits on epic-netflix-dpad because: fakty `playback` z `title` serialu, `seriesId`, `lastInSeason`, `position`, `duration`.
