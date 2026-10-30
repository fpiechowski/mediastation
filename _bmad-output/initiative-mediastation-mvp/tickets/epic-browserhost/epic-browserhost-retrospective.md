---
epic: epic-browserhost
date: 2026-10-30
verdict: accepted-with-open-items
criteria: declared
headless: true
---

# Retrospektywa: epic-browserhost

Maszyna: **UM760** (płyta F7BSI, model EliteMini Series; `tickets.py`, git, testy i build uruchomione tu).

## Epic summary

- Epika: `epic-browserhost` (id 6), folder `_bmad-output/initiative-mediastation-mvp/tickets/epic-browserhost/`. Poprzednia retro z 2026-10-27 objęła tylko spike 6.1 (werdykt `rejected`, bo reszta epiki nie była jeszcze zaplanowana). Ten plik ją zastępuje. Stara wersja leży w `checkpoint-2026-10-27-spike-only.md` (commit `73dad8f`).
- Tickety (`tickets.py status <folder>`): 9 z 9 `done`. `pending_tickets`: puste. Tickety w `built`: brak.

| Ticket | Tytuł | Commit | Sesje |
|---|---|---|---|
| 6.1 | Spike: tryb okna i źródło wersji poza CDP | `2ea151f` | poza tym runem |
| 6.2 | Tracer BrowserHost: Edge kiosk, CDP i hello | `9aaa811` | dev×2, review×1 |
| 6.3 | Binding po nawigacji i reconnect CDP | `9252f45`, `4a7262f` | dev×3, review×1 |
| 6.4 | Wersja przeglądarki i rozróżnialne błędy startu | `6319f01` | dev×1 |
| 6.5 | ProbeExisting i RenewSession | `ec04642` | dev×1 |
| 6.6 | Minimalny PageRuntime i build page bundle | `040fcaf` | dev×2 |
| 6.7 | J4: restart aplikacji Failed jednym OK | `b261608` | dev×1 |
| 6.8 | Refactor sweep | `3cf71e4` | dev×1 |
| 6.9 | BrowserHost end-to-end suite | `9124dcb` | dev×2 |

- Zakresy (z `baseline_revision` planów, uporządkowane wg historii; ostatni zakres ucięty na ostatnim commicie epiki, nie na `HEAD`, bo po nim są tylko commity retro):
  - 6.2 `cd6a046..9aaa811`; 6.3 `9aaa811..4a7262f`; 6.4 `4a7262f..6319f01`; 6.5 `6319f01..ec04642`; 6.6 `ec04642..040fcaf`; 6.7 `2a7f81e..b261608`; 6.8 `ec50bad..3cf71e4`; 6.9 `7c72f93..9124dcb`.
  - **Odchylenie od planów:** plan 6.3 ma `baseline_revision: 9252f45`, ale zakres 6.3 liczę od `9aaa811` (koniec 6.2), bo `9252f45` jest commitem dev samej 6.3. Wartość w planie została ręcznie przestawiona z `9aaa811` na `9252f45` w trakcie runu (patrz P1). Dla 6.7 i 6.8 zakresy kończą się na commicie ticketu, a nie na baseline następnego planu, bo `ec50bad` i `7c72f93` to commity spoza epiki (skill `delegate`).
  - 6.1: zakres `78610779..2ea151f` (jeden commit, wyłącznie dokumentacja i pomiary, bez kodu produktu); jego retro i dowody są w `checkpoint-2026-10-27-spike-only.md`. Nie analizowane ponownie.
- Wolumen (`git_evidence.py`, zakresy 6.2–6.9, poza `_bmad-output`): 84 pliki, +13 286 / −471 linii. Największe: `BrowserHostTests.cs` +4 104 (4 059 linii), `BrowserHost.cs` +1 828 (1 752 linii), `check-browserhost-e2e.ps1` +759, `check-browserhost-restart.ps1` +568, `check-browserhost-errors.ps1` +554.
- Inwentarz dowodów: dostępne: epic file, `tickets.toml`, 9 planów (z Review Triage Log, Verification, Auto Run Result), diff z gita, logi sesji runu `.bmad-loop/runs/20261029-160513-64bb/logs/` (15 plików), run journal. Brakujące: story files (tickety nie były „pulled”, `file: null`); logi sesji spike'a 6.1; dowody pomiarowe 6.1 poza repo (`spike/logs/`, ignorowane).

## Findings

Widoki zbiorcze: policzone deterministycznie z `git_evidence.py` i `wc -l`/`grep` na drzewie. Przegląd diffu: **nie uruchamiano ponownie** `bmad-review` na całym diffie (ok. 13 tys. linii, 84 pliki); zakres zawężony do przeglądów per-ticket zapisanych w planach (łącznie 4 soczewki: blind-hunter, edge-case-hunter, verification-gap, intent-alignment, na każdej z 8 historii full-route) oraz do kontroli granic między ticketami poniżej. „Nie sprawdzano” znaczy tu właśnie to, nie „czysto”.

| # | Finding | Źródło | Dyspozycja |
|---|---|---|---|
| F1 | **God-class / rozmiar.** `BrowserHost.cs` urósł z 680 linii (po 6.2) do 1 752 (+157% w 6.3–6.8): 972 (6.3) → 1 130 (6.4) → 1 622 (6.5) → 1 679 (6.6) → 1 752 (6.8). Zawiera 42 metody i zagnieżdżoną klasę `Session` (od linii 1 569), a odpowiada za launch, preflight, CDP setup, reconnect, probe/adopcję, renew i stop. Podobnie `BrowserHostTests.cs`: 4 059 linii, dotykany przez 7 z 8 historii. Żaden ticket tego nie widział: każdy dodawał 100–500 linii. | `git show <commit>:…/BrowserHost.cs \| wc -l` dla 9aaa811…3cf71e4; `grep` struktury | defer: osobna story „wydzielić z BrowserHost: reconnect, probe/adopcję i sesję” przed epic-netflix-dpad, który będzie rozszerzał ten sam plik |
| F2 | **Duplikacja w skryptach smoke.** 9 funkcji (`Wait-For`, `Stop-Profile`, `Set-Front`, `Send-Key`, `Save-Evidence`, `Note`, `Get-RunEntries`, `Get-ProfileProcesses`, `Add-Jsonl`) zdefiniowanych 5 razy, po jednej w każdym z `check-browserhost-{tracer,reconnect,errors,adopt,restart}.ps1`; dalsze 7 funkcji w 3 skryptach. Razem ok. 3 000 linii PowerShella w 6 skryptach. 6.9 dopisała do wszystkich pięciu te same parametry (`-Service`, `-EvidenceDir`) i tę samą poprawkę `Stop-Profile`, czyli zmiana szła pięć razy. | `grep -h "^function" tools/check-browserhost-*.ps1 \| sort \| uniq -c` | defer: wspólny moduł dot-source (np. `tools/lib/smoke-common.ps1`) |
| F3 | **Dryf rejestru odroczeń.** Sweep 6.8 rozstrzygnął w planie DW-39, DW-53, DW-54, DW-55 (resolved z dowodem) oraz DW-52 i DW-57 (false, sondą na UM760), ale `deferred-work.md` nadal ma `status: open` dla DW-52…DW-58, czyli wszystkich siedmiu. Przy 29 otwartych wpisach w całym pliku następny sweep znowu je zweryfikuje. | `story-refactor-sweep-plan.md:44-50, 249-254`; `awk` po `deferred-work.md` | fix now: zamknąć DW-52/53/54/55/57 (i DW-39 jeśli otwarty) w rejestrze; zostają DW-56 i DW-58 |
| F4 | **Wyciek zakresu w 6.8.** Commit `3cf71e4` (story 6.8) zawiera 26 plików `.agents/skills/delegate/**` (SKILL.md, config.yaml, evals, `run_cli.py`, łącznie +496 linii) niezwiązanych z BrowserHost. To były niezacommitowane pliki użytkownika, które sesja dev dołączyła do commita ticketu. Następny commit użytkownika `7c72f93` edytuje już ten skill. | `git show --stat 3cf71e4`; zakres 6.8 w `git_evidence.py` pokazuje `.agents/skills/delegate/scripts/run_cli.py` +141 | accept (praca użytkownika nie zginęła); proces: patrz P5 |
| F5 | **Spec-to-implementation, Requirements 1–6.** Wszystkie spełnione w kodzie i testach: 1 (AD-1: `ForbiddenMethods = { "Runtime.enable" }` w `CdpSession.cs:40`; brak `remote-allow-origins`, Playwright, `SW_HIDE` w kodzie produktu; loopback `127.0.0.1` w `CdpTargets.cs:14`), 2 (6.3), 3 (6.5), 4 (6.4), 5 (6.6), 6 (6.7). Dopisek: 6.4 usuwa `__COMPAT_LAYER` ze środowiska procesu Edge, bo odziedziczona zmienna powodowała samo-relaunch wyglądający jak natychmiastowy exit (`BrowserProcess.cs`, plan 6.4). To zachowanie spoza specu. | `CdpSession.cs:33-40`; plan 6.4 „Stop and launch” | accept; propozycja ADR niżej |
| F6 | **Pokrycie testami vs. realny Edge.** Reconnect działa na `FakeTransport`; ścieżka sukcesu (strona po reconnect faktycznie ma działający most) nie była widziana w prawdziwej przeglądarce przy zerwaniu gniazda (potrzebuje podniesionego `SetTcpEntry DELETE_TCB`). 6.8 uznała DW-55 za rozstrzygnięte przez smoke adopcji (RenewSession → `cdpReconnected` → `pageHello`), ale ścieżka „zerwany socket żywego targetu” nadal nie jest dowiedziona. | `deferred-work.md` DW-55; plan 6.8 wiersz DW-55 | defer: sprawdzenie w odbiorze UM760 (elevated) |
| F7 | **Nawigacja podczas awarii CDP.** Główna ramka, która nawiguje w trakcie przerwy CDP, zostaje bez PageRuntime do następnej nawigacji (init script umarł ze starą sesją; reconnect nie robi `Page.reload` ani `Runtime.evaluate`). | `deferred-work.md` DW-56; plan 6.8: „needs an AD-1 or product decision” | defer: decyzja architektoniczna (`bmad-architecture`), wpis zostaje otwarty |
| F8 | **DevHarnessHostPort** nadal wysyła `ProcessExited` przy każdym Stop, także po zgłoszonym wyjściu (DW-3 naprawione tylko w BrowserHost); dev harness nie jest ścieżką produkcyjną. | `deferred-work.md` DW-58 | accept / defer (dev-only) |
| F9 | **Jeden test pominięty** z dawnego DW-1 (`PowerNotificationsHarnessTests.cs:99`, `Skip = …`), spoza tej epiki; 6.9 raportuje go jako „1 skipped (pre-existing)”. | `grep "Skip *=" tests` | accept |

## Behavior verification

Uruchomione na UM760 w trakcie tej retro:

- `dotnet test tests/MediaStation.Core.Tests -c Release`: **1 623 zaliczone, 0 niepowodzeń, 0 pominiętych**. (Plan 6.9 podaje 1 637 zaliczonych i 1 skipped dla całego `MediaStation.slnx`; liczby różnią się zakresem: tu jeden projekt testowy, tam całe solution.)
- `npm test` w `web/`: **14 plików, 175 testów zaliczonych**.
- `dotnet build src/MediaStation.App -c Release`: **0 ostrzeżeń, 0 błędów**.
- `tools/check-browserhost-e2e.ps1 -SelfTest`: **PASS**, każdy wiersz macierzy przechodzi na fixture, a każda z 25 mutacji oblewa dokładnie własny wiersz.

**Nie wykonano** ponownie pełnego, żywego przebiegu `tools/check-browserhost-e2e.ps1`: uruchamia Edge w trybie kiosk na ekranie, przejmuje okno pierwszego planu i wyciszenie, więc nie robi się go bez wiedzy użytkownika. Opieram się na zapisanym przebiegu 6.9 na UM760 (`browserhost-e2e-20261029-224125`: 5 faz zaliczonych, 11/11 wierszy macierzy, izolacja zachowana, screenshoty `tracer\after-home.png` i `restart\p5-foreground.png` obejrzane przez weryfikację orkiestratora) oraz na wcześniejszych smoke'ach per ticket (6.2–6.7) z ich planów. To jest dowód z drugiej ręki wobec tej retro.

## Previous-retro follow-through

Poprzednia epika w kolejności: `epic-windows-audio` (id 5). Jej plik retro istnieje (`epic-windows-audio-retrospective.md`, 2026-10-27, werdykt `rejected`); epika została potem zaakceptowana i zamknięta (`8945757`, `bb9bd11`, `99fdfbd`).

| Pozycja | Owner | Czy zrealizowana (dowód) |
|---|---|---|
| A1 Home w trybie shell (DW-41) | PO / Winston | **Tak.** `deferred-work.md` DW-41 `status: done 2026-10-27`; fix w `3d29fee` (wg `epic-windows-audio-um760-acceptance.md:38`). |
| A2 Odbiór całej epiki na UM760 przez `fp-acceptance` | użytkownik | **Tak.** Commity `8945757` „accept epic 5”, `bb9bd11` „record S16 acceptance rerun”, `99fdfbd` „close epic 5 after acceptance”. |
| A3 Poprawić cytowanie DW-37 w planie sweepu 5.7 (5.3 `b955fc4`, nie 5.4) | PO | **Nie znaleziono dowodu.** Nie udało się potwierdzić ani zaprzeczyć: nie znalazłem w planach epic-windows-audio wzmianki o DW-37 pasującej do tego cytatu. |
| A4 Scenariusze DW-33 w planie odbioru | `fp-acceptance` / użytkownik | **Tak.** `epic-windows-audio-um760-acceptance.md:38` i wiersz S3 (:67) wiążą DW-33 ze scenariuszami S3, S11–S14. |
| A5 Zapisywać koniec zakresu (end revision) w każdym planie | narzędzia loopa | **Nie.** Plany 6.x nadal mają tylko `baseline_revision`; w tej epice musiałem ręcznie wyznaczać końce zakresów 6.7, 6.8 i 6.9 z powodu commitów spoza epiki (F4). |

## Action items

Wszystkie to propozycje; nic nie zostało zastosowane.

| ID | Typ | Pozycja | Źródło | Owner |
|---|---|---|---|---|
| A1 | Fix (rejestr) | Zamknąć w `deferred-work.md` wpisy DW-52, 53, 54, 55, 57 (i DW-39, jeśli otwarty) według tabel planu 6.8. Zostawić DW-56 i DW-58. | F3 | Sofia (`bmad-agent-po`) |
| A2 | Remediation (nowa story) | Rozdzielić `BrowserHost.cs` (reconnect, probe/adopcja, sesja) i `BrowserHostTests.cs` na pliki tematyczne, bez zmiany zachowania, przed pracami epic-netflix-dpad nad tym samym plikiem. | F1 | Sofia → dev loop |
| A3 | Remediation (nowa story) | Wydzielić wspólny moduł skryptów smoke (`Wait-For`, `Stop-Profile`, `Set-Front`, `Send-Key`, `Save-Evidence`, `Note`, `Get-RunEntries`, `Get-ProfileProcesses`, `Add-Jsonl`). | F2 | Sofia → dev loop |
| A4 | Decyzja architektoniczna | Rozstrzygnąć DW-56 (nawigacja podczas awarii CDP: wolno `Page.reload`/`Runtime.evaluate`, czy akceptujemy brak PageRuntime do następnej nawigacji) i zapisać w AD-1/AD-10. | F7 | Winston (`bmad-architecture`) |
| A5 | Odbiór (użytkownik) | Uruchomić odbiór całej epiki na UM760 przez `fp-acceptance`, w tym podniesiony reset gniazda CDP przy żywym Netflixie (F6) oraz J4 i adopcję żywego Edge'a po restarcie MediaStation. | F6, Done when 5 | użytkownik |
| A6 | Proces | Zabezpieczyć commity ticketów loopa przed dołączaniem cudzych, niezacommitowanych plików (np. `git add` tylko ścieżek z planu albo check „pliki spoza Files changed”). | F4 | autor loopa / `bmad-project-context` |
| A7 | Proces | Zapisywać koniec zakresu w planie albo w journalu loopa (powtórka A5 z retro epiki 5). | Previous-retro A5 | narzędzia loopa |

**Lekcje procesowe z tego runu** (z journala i logów `.bmad-loop/runs/20261029-160513-64bb/`; to nie findings o kodzie, ale kosztowały sesje):

- **P1 — niezgodność baseline.** Po `resume --accept-baseline` na 6.3 plan miał stary `baseline_revision` (`9aaa811`), a orchestrator nowy (`9252f45`); ticket stał jako `deferred` ze „spec baseline … does not match orchestrator-recorded baseline”. Naprawione ręcznie edycją planu i `resolve --reverify`. Wskazuje na lukę w loopie: `--accept-baseline` nie aktualizuje planu.
- **P2 — limit czasu powłoki.** Silnik `bmad-loop` działał w tle w powłoce harnessu z maksymalnym limitem 2 godzin; dwukrotnie (20:28 i 23:01) został zabity w środku sesji dev (6.6 i 6.9). Każdy raz osierocona sesja `claude` i brudne drzewo wymagały ręcznego `git stash` i wznowienia; praca tych sesji (kilkadziesiąt minut) przepadła. Zalecenie: długi run poza powłoką z limitem (osobne okno PowerShell).
- **P3 — flaga `hitl` na 6.7.** Run zatrzymał się na 6.7 (`hitl = true`). Flaga została zdjęta w `tickets.toml` (`2a7f81e`), tak jak wcześniej dla 3.13 i 3.14 (`3d9830d`); weryfikacja J4 na UM760 zostaje w odbiorze użytkownika, zgodnie z `AGENTS.md`. To zmiana metadanych ticketu bez zatwierdzenia Sofii; do potwierdzenia.
- **P4 — przestarzała retro blokowała retro.** Plik z 2026-10-27 (retro samego spike'a) sprawił, że loop zapisał `retro-auto-skipped: already-done` i zatrzymał run z werdyktem `rejected` w `state.json`. Plik został przesunięty (`73dad8f`), a retro uruchomiona ręcznie. Werdykt w `state.json` runu `64bb` nadal brzmi `rejected`; run `48b0` czeka na zamknięcie epiki.
- **P5 — cudze pliki w commicie ticketu** (F4).

## Decision records

Propozycje (headless, nic nie zapisano; `docs/decisions/` nie istnieje, żadnego `adr-*.md` w repo nie ma):

1. **`adr-browserhost-strips-compat-layer-from-browser-env.md`** — kontekst: odziedziczona zmienna `__COMPAT_LAYER` powodowała, że Edge relaunchował się sam i wyglądało to jak natychmiastowy exit procesu; decyzja: launcher usuwa ją ze środowiska procesu przeglądarki; dlaczego: plan 6.4 („Stop and launch”) podaje diagnozę z UM760. Plan 6.4, commit `6319f01`. Test ADR: nieoczywiste, ukryta przyczyna, brak zapisu w AD-1/AD-13 (`grep COMPAT_LAYER` w architekturze: 0).
2. **`adr-browserhost-profile-lock-and-port-preflight.md`** — kontekst: potrzebne rozróżnienie `ProfileInUse` od `PortInUse` bez startu; decyzja: profil zajęty = istnieje blokada `lockfile` Chromium, port zajęty = istnieje listener TCP; przy obu zajętych wygrywa `ProfileInUse`; dlaczego: plan 6.4. Commit `6319f01`. Test ADR: trudne do odwrócenia (kody błędów `ErrorCode` są kontraktem), brak w architekturze (`grep lockfile`: 0).

**Update 2026-10-30:** użytkownik zatwierdził oba, a Winston je napisał: `src/MediaStation.App/Hosts/Browser/adr-browserhost-strips-compat-layer-from-browser-env.md` (`93e6e89`) i `adr-browserhost-profile-lock-and-port-preflight.md` (`73ff58f`). Drugi prostuje zapis wyżej: profil zajęty to `lockfile`, którego nie da się otworzyć do zapisu; plik bez właściciela oznacza profil wolny.

Wymaga zatwierdzenia przez użytkownika. Pozostałe decyzje z planów (tryb okna `--kiosk`, źródło wersji, reconnect do tego samego targetu, `PageVersionRejected` → `Failed`) są już w AD-1, AD-7, AD-10 i AD-13.

## Acceptance verdict

**Accepted-with-open-items**, kryteria **zadeklarowane** (Requirements 1–6 i Done when 1–5 z `epic-browserhost.md`).

- **Dowód:** wszystkie 9 ticketów `done`, `pending_tickets` puste. Requirements 1–6 i Done when 1–4 spełnione w kodzie i dowodzie maszynowym (F5; testy .NET i web zielone na UM760; 11/11 wierszy macierzy E2E w zapisanym przebiegu 6.9; autotest macierzy przechodzi). Done when 5 (żywy Edge adoptowany po restarcie MediaStation i J4 jednym OK na UM760) ma dowód maszynowy ze smoke'ów 6.5, 6.7 i 6.9 na UM760, ale **odbiór całej epiki na UM760 należy do użytkownika** (`AGENTS.md`); nie jest tu zaznaczony jako wykonany.
- **Otwarte, śledzone elementy:** F1 (rozmiar `BrowserHost.cs`), F2 (duplikacja skryptów), F3 (dryf rejestru), F6 i F7 (DW-55 w wariancie „zerwany socket”, DW-56), F8 (DW-58). Żaden nie blokuje kryteriów epiki.
- **Brak blokujących findings.** Niezgodność baseline (P1) i utracone sesje (P2) to koszt procesu, nie wada dostarczonego kodu.
- Werdykt maszynowy nie jest odbiorem użytkownika; zamknięcie epiki należy do Sofii po potwierdzeniu przez użytkownika.

## Open questions

- Czy zdjęcie `hitl` z 6.7 (P3) ma pozostać, czy Sofia chce je cofnąć i traktować J4 jako ticket z człowiekiem w pętli przy następnych epikach?
- Czy rozdzielenie `BrowserHost.cs` (A2) ma być warunkiem wejścia w epic-netflix-dpad, czy tylko zaleceniem?
- Czy run `64bb` (wciąż `paused` z werdyktem `rejected` w `state.json`) i `48b0` (`awaiting-closure`) mają być zarchiwizowane po zamknięciu epiki?

## Assumptions

- Epika wzięta z `-H epic-browserhost` (dopasowanie po slugu; folder `epic-browserhost`).
- `pending_tickets` puste, więc nie było potwierdzenia do pominięcia; werdykt maszynowy nie został wymuszony do `rejected`.
- Maszyna zidentyfikowana jako UM760 po płycie F7BSI i modelu EliteMini Series (wyjście `Win32_BaseBoard`/`Win32_ComputerSystem`).
- Zakresy: dla 6.3 przyjąłem start `9aaa811` zamiast zapisanego w planie `9252f45` (patrz Epic summary, P1); dla 6.7 i 6.8 końce zakresów to commity ticketów, nie baseline następnego planu.
- Przegląd diffu: nie uruchamiałem `bmad-review` na całości; polegam na przeglądach per-ticket z planów i na kontrolach granic opisanych w Findings. Pełna analiza duplikacji i architecture delta na poziomie importów nie została wykonana (zakres zawężony do rozmiaru plików i powtórzonych funkcji).
- Wszystkie action items i propozycje ADR są tylko propozycjami. Jedyna rzecz zapisana przez tę retro to ten plik.
- To retro zostało wykonane ręcznie (poza etapem `retrospective = "auto"` loopa), bo loop pominął etap z powodu pliku z 27.10 (P4).
