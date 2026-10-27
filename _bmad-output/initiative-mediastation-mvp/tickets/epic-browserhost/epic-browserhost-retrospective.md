---
epic: epic-browserhost
date: 2026-10-27
verdict: rejected
criteria: declared
headless: true
---

# Retrospektywa: epic-browserhost

Maszyna: **UM760** (płyta F7BSI, model EliteMini Series, host MEDIASTATION; `tickets.py` i git uruchomione tu).

## Epic summary

- Epika: `epic-browserhost` (id 6), folder `_bmad-output/initiative-mediastation-mvp/tickets/epic-browserhost/`.
- Tickety (`tickets.py status <folder>`): jeden wiersz, **6.1** spike „Tryb okna przeglądarki i źródło wersji poza CDP” — `status: done`, `state: done`. `pending_tickets`: puste. Tickety w `built`: brak.
- Epika jest zaplanowana tylko w części: `tickets.toml:3-4` i Notes epiki (decyzja 2026-10-26) mówią, że poza spike'em 6.1 reszta nie jest rozpisana na stories; `unpinned_after` zgłasza wszystkie trzy `after` epiki.
- Zakres: `baseline_revision` planu 6.1 = `78610779…` (commit story 5.4); zakres `7861077..HEAD` (wnioskowany, ostatni plan), jeden commit `2ea151f` „story 6.1: implemented and reviewed via bmad-loop”.
- Inwentarz: epika, `tickets.toml`, plan 6.1 (Implementation Notes, Review Triage Log, Verification, Auto Run Result), `git_evidence.py`, diff spine'u AD-1/AD-13. Brak story file (spike pozostał wpisem w `tickets.toml`, `file: null`). Brak logów sesji (process lessons pominięte). Poprzedniej retro brak (patrz niżej). Dowody pomiarowe spike'a są w `spike/logs/` (zignorowane przez `spike/.gitignore:4`), lokalne na UM760.
- Zmienione pliki: AD-1 / AD-13 / Deferred w `architecture-mediastation.md` (+4/−4), `.memlog.md` (+1), `deferred-work.md` (+23), plan (+138), 6 skryptów w `spike/helpers/` (poza produktem). Zero kodu produktu.

## Findings

Widoki zbiorcze (architecture delta, duplikacja, god-class, pattern divergence): **nie dotyczy** — zmiana jest wyłącznie dokumentacyjna i pomiarowa, bez kodu produktu. Spec-to-implementation: rozstrzygnięcia z planu zgadzają się z tym, co trafiło do spine'u (`architecture-mediastation.md:82`, `:360`, `:544`).

Przegląd diffu: nie uruchamiano ponownie `bmad-review`; polegam na przeglądzie z builda (plan, Review Triage Log 2026-10-27: soczewki blind-hunter, edge-case-hunter, verification-gap, intent-alignment; 5 patchy, 3 defery, 5 odrzuceń). Zakres zawężony: nie sprawdzano ponownie samych pomiarów.

| # | Finding | Źródło | Dyspozycja |
|---|---|---|---|
| F1 | Notes epiki nadal opisują tryb okna i źródło wersji jako „Open question”, choć rozstrzygnął je spike 6.1 (`--kiosk`; `FileVersion` + `Last Version`). Analogicznie notki w epic-osk i epic-windows-audio (wg planu). | `epic-browserhost.md:43-44`; plan `deferred[0]` | fix now (rekoncyliacja specu przez Sofię) |
| F2 | Epika ma tylko story 1; Done when 1–5 (start z AD-1, binding po nawigacji i reconnect, wersja przed każdym startem, rozróżnialne błędy, adopcja i J4) nie są zrealizowane ani rozpisane. Trzy `after` są nieprzypięte. | `epic-browserhost.md` Done when; `tickets.toml:3-4`; `status`: `unpinned_after` | fix now (zaplanować pozostałe stories przez Sofię, zachowując wpis 1) |
| F3 | Dowody pomiarowe (zrzuty, JSONL) nie są w repo; rozstrzygnięcie `--kiosk` opiera się na nich. | plan Implementation Notes; `spike/.gitignore:4` | accept (świadoma decyzja, odnotowana w planie); powtórzyć kluczowy pomiar w pierwszej story BrowserHost |
| F4 | Kiosk niesprawdzony przy wielu monitorach, zmianie rozdzielczości, wyłączeniu TV i z nakładką OSK; zakres: jedno konto, jeden tytuł, jeden monitor, jedna próba na tryb. | plan `deferred[2]` | defer (epic-osk / WindowManager) |
| F5 | Zmiana PlayReady (HEVC) → Widevine SW (H.264) w trakcie spike'a bez ustalonej przyczyny; AD-1 mówi to wprost, ale przyczyna pozostaje nieznana. | plan „DRM i jakość”; `architecture-mediastation.md:82` | defer (smoke AD-13 loguje system kluczy; zbadać przy `--smoke netflix`) |
| F6 | Awaria BrowserHost po starcie przeglądarki, a przed logiem `BrowserVersionChanged`, daje następnym razem `from==to` i zmiana ginie. | plan `deferred[1]`; `architecture-mediastation.md:360` | defer (decyzja w story BrowserHost) |
| F7 | Plan ma `status: done` we frontmatterze, a `Auto Run Result` mówi `Status: built`. | plan, frontmatter i ostatnia sekcja | accept (spójne ze stanem `done`; kosmetyka) |

Lekcja procesowa (F1, F2): spike wyprzedzający zaplanowaną epikę zamyka pytanie w spine'ie, ale nie aktualizuje Notes epik zależnych, bo workflow zabrania zapisu w tickecie. Zapobiega temu krok „Sofia reconciles Notes” po zamknięciu spike'a.

## Behavior verification

Runtime produktu nie został zmieniony (zero kodu produktu), więc nie uruchamiano aplikacji. Sprawdzono tylko, że rozstrzygnięcia z planu są w spine'ie (grep `kiosk`, `Last Version`, `FileVersion` w `architecture-mediastation.md`). Pomiarów spike'a nie powtarzano; opierają się na planie i lokalnych logach UM760.

## Previous-retro follow-through

Brak pliku poprzedniej retrospektywy: poprzednia epika w kolejności, `epic-windows-audio`, nie ma `epic-windows-audio-retrospective.md` (nie została dokończona). To brak pliku, nie brak otwartych punktów.

## Action items

1. **Sofia (bmad-agent-po):** zaktualizować Notes w `epic-browserhost.md:43-44`, epic-osk i epic-windows-audio — pytania rozstrzygnięte w AD-1/AD-13 (F1). Propozycja, do zastosowania przez człowieka.
2. **Sofia:** zaplanować pozostałe stories epiki (Done when 1–5), zachowując wpis 1, i przypiąć trzy `after` (F2). Przy planowaniu uwzględnić DW-3, DW-4 i trzy Deferred z Notes epiki.
3. **Dev (story BrowserHost):** w pierwszej story rozstrzygnąć `from==to` po awarii (F6) i powtórzyć w zapisanej w repo formie pomiar `--kiosk` (F3).
4. **Dev (epic-osk / WindowManager):** zmierzyć kiosk przy wielu monitorach i z nakładką (F4).

Do UM760 (całość epiki, po dostarczeniu wszystkich stories, przez `fp-acceptance`): start Edge z `--kiosk`, minimalizacja i przywrócenie z pilota, brak paska adresu, log `BrowserVersionChanged` po aktualizacji, plus J4 i adopcja po restarcie.

## Decision records

Propozycja (headless, nie zapisano): **`adr-browser-window-mode-kiosk.md`** — kontekst: trzy flagi trybu okna Edge; decyzja: `--kiosk`; dlaczego: jedyna daje pełny monitor bez UI przeglądarki i bez dodatkowego wywołania, przeżywa `SW_MINIMIZE`/`SW_RESTORE`. Plan: spike 6.1, commit `2ea151f`. Test: trudna do odwrócenia (wpływa na WindowManager i OSK), nieoczywista, rzeczywisty kompromis, brak ADR w repo (`docs/decisions/` nie istnieje). Wymaga zatwierdzenia przez użytkownika. Źródło wersji (`FileVersion` + `Last Version`) opisane w AD-13; nie proponowane osobno.

## Acceptance verdict

**Rejected** (kryteria zadeklarowane, nie profilowane). Ticket 6.1 spełnia swoje `verify` (wyniki dla trzech flag, odczyt wersji na UM760, rozstrzygnięcie w AD-1 i AD-13). Jednak Done when epiki 1–5 nie są spełnione: poza spike'em nie ma żadnej zaplanowanej ani zbudowanej story (F2). `pending_tickets` jest puste, więc to nie jest wymuszone regułą o niedokończonych ticketach, tylko brakiem realizacji kryteriów epiki. Epika nie jest zaakceptowana; werdykt człowieka ma pierwszeństwo, a akceptacja na UM760 należy do użytkownika.

## Open questions

- Czy spike 6.1 miał być traktowany jako cała „epika” do tej retro, czy retro dla epiki z jednym ticketem jest tylko punktem kontrolnym przed planowaniem? Odpowiedź zmienia werdykt z rejected na „checkpoint”.
- Przyczyna zniknięcia PlayReady w trakcie spike'a (F5) pozostaje nieustalona.

## Assumptions

- Epika `epic-browserhost` wzięta z `-H epic-browserhost` (dopasowanie po slugu w `status`; folder `epic-browserhost`).
- `pending_tickets` puste, więc nie było potwierdzenia do pominięcia.
- Maszyna zidentyfikowana jako UM760 po płycie F7BSI i modelu EliteMini Series; polecenie `$env:COMPUTERNAME` w PowerShell zwróciło `MEDIASTATION`.
- Werdykt maszynowy rejected wyprowadzony z niespełnionych Done when, bez decyzji człowieka.
- Wszystkie action items i propozycja ADR są tylko propozycjami; nic nie zastosowano.
- Brak logów sesji: analiza procesowa ograniczona do planu.
