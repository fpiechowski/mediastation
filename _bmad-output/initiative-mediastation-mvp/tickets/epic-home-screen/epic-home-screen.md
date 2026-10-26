---
type: epic
title: "Home screen"
parent: initiative-mediastation-mvp
covers: [CAP-6]
sources: [_bmad-output/initiative-mediastation-mvp/spec-mediastation/spec-mediastation.md, _bmad-output/initiative-mediastation-mvp/architecture-mediastation/architecture-mediastation.md, _bmad-output/initiative-mediastation-mvp/tickets/epic-foundation-contracts/epic-foundation-contracts-retrospective.md, _bmad-output/initiative-mediastation-mvp/deferred-work.md]
companions: [_bmad-output/initiative-mediastation-mvp/ux-mediastation/EXPERIENCE.md, _bmad-output/initiative-mediastation-mvp/ux-mediastation/DESIGN.md]
after: []
assignee: ""
risk: medium
---

# Home screen

## Description

CAP-6: Eliza widzi pełnoekranowy ekran główny w stylu smart TV, z zakładkami Video, Audio i Game, i przechodzi po nim D-padem, z jednym widocznym fokusem i bez kursora. Epika dostarcza React shell w `web/shell` na hoście z epic-foundation-contracts: zakładki z `apps.json`, kafelki ze stanem cyklu życia, kropkę „w tle”, model fokusu kluczowany stabilnymi id i AppMenu.

## Outcome

Pierwsze demo na TV: ekran główny obsługiwany pilotem, zgodny z `DESIGN.md`; sygnałem jest success CAP-6.

## Requirements

- R1 (spec CAP-6 success 1; AD-11, AD-16): Zakładki Video, Audio i Game oraz kolejność kafelków wynikają z `category` i kolejności wpisów w `apps.json`. Zakładka bez wpisów (w MVP Audio) pokazuje empty state z copy z `EXPERIENCE.md`; fokus zostaje wtedy na zakładce, a Dół nic nie robi.
- R2 (spec CAP-6 success 2; AD-10; DESIGN.md, Stany kafelka): Kafelek pokazuje ikonę, tytuł oraz chip i linię stanu z tabeli Stany kafelka dla każdego stanu cyklu życia. Kropka na zakładce świeci, gdy aplikacja tej zakładki jest w `Background`.
- R3 (spec CAP-6 success 3; AD-16; EXPERIENCE.md, Model fokusu): Na ekranie jest dokładnie jeden widoczny fokus, kluczowany `appId` i `category`, nigdy indeksem. Każde nowe `focusRequest.seq` jest stosowane dokładnie raz: `default` stawia fokus na pierwszym kafelku Video, `<appId>` na kafelku tej aplikacji w jej zakładce. Powrót z aplikacji stawia fokus na ostatnim kafelku.
- R4 (spec CAP-6 success 4; AD-8, AD-10; EXPERIENCE.md, AppMenu): AppMenu otwiera long-press OK na kafelku `Background` albo `Failed` oraz OK na `Failed`; przy `Failed` fokus stoi na „Uruchom ponownie”, w pozostałych stanach na pierwszej pozycji. Pozycje wysyłają `RestartApp` i `CloseApp`. Back zamyka menu z fokusem na kafelku, Home zamyka menu i zostawia ekran główny. Long-press na `NotRunning` i OK na `Starting` nic nie robią.
- R5 (spec CAP-6 intent; AD-3; AD-8, tabela routingu, wiersz `shell`): W trybie `shell` D-pad, OK, Back i Home docierają do Shell jako Actions z Orchestratora, a Shell konsumuje wyłącznie Actions. OK na kafelku `NotRunning` albo `Background` wysyła Intent `LaunchApp`.
- R6 (spec CAP-6 success 5; DESIGN.md; EXPERIENCE.md, Information Architecture): Wygląd jest zgodny z `DESIGN.md` w 1920×1080, a treść z fokusem mieści się w title-safe 5%. Nagłówek ma logo, powitanie według pory dnia, zegar i miejsce na przycisk aktualizacji. Kursor jest ukryty w bezruchu, a fonty i ikony są bundlowane lokalnie.
- R7 (spec CAP-1 success; AD-11; retro epiki 1, F17 i F18): Walidacja `apps.json` odrzuca puste albo złożone ze spacji `title` i `icon`. Ekran błędu konfiguracji pokazuje, że są błędy, które nie zmieściły się na ekranie.
- R8 (AD-16; retro epiki 1, F11; ledger DW-28): Most przyjmuje wiadomości tylko z origin shella, a WebView2 nie nawiguje poza shell. Build web odświeża typy TS z Core na bieżącej maszynie bez kroków ręcznych.

## Constraints

- AD-16: typy TS są wyłącznie generowane; Core wysyła pełny snapshot z monotonicznym `rev`; UI trzyma tylko efemeryczny stan nawigacji kluczowany stabilnymi id.
- AD-3: tylko Orchestrator mutuje stan cyklu życia i tryb wejścia; Shell wysyła Intents i nie decyduje.
- AD-8: Shell konsumuje wyłącznie Actions z core i nie czyta surowych klawiszy; AppMenu istnieje tylko w trybie `shell`.
- spec, Constraints, i EXPERIENCE.md, Accessibility Floor: jedna powierzchnia 1920×1080, tekst co najmniej 24px, dokładnie jeden fokus, brak ruchu poza przejściem fokusu, nic nie zmienia się pod fokusem samo z siebie.

## Done when

1. Zakładki i kolejność kafelków wynikają z `category` i kolejności w `apps.json`; Audio pokazuje empty state (AD-11).
2. Kafelek pokazuje stan aplikacji, a kropka na zakładce sygnalizuje aplikację działającą w tle (AD-16).
3. Zimny start, restart i wybudzenie stawiają fokus na pierwszym kafelku Video, a powrót z aplikacji na ostatnim kafelku (AD-16).
4. AppMenu otwiera long-press OK na kafelku `Background` albo `Failed` oraz OK na `Failed` (AD-8).
5. Na TV z UM760 wygląd jest zgodny z `DESIGN.md` w 1920×1080, w title-safe 5%, obsługa D-padem bez kursora.
6. Most odrzuca wiadomości spoza origin shella, a `npm run build` w `web/` odświeża kontrakty na bieżącej maszynie bez kroków ręcznych (AD-16).

## Boundaries

`web/shell` i obsługa Intents z UI. Nie: karta „Do oceny” i check „nowa karta nie kradnie fokusu” (epic-do-oceny), Z-order okna shella (epic-windows-audio), przycisk aktualizacji w nagłówku (epic-deploy-resilience; nagłówek ma do niego miejsce w layoucie z DESIGN.md), widgety i tło ze zdjęć (Non-goals).

## References

- parent — _bmad-output/initiative-mediastation-mvp/spec-mediastation/spec-mediastation.md, Capabilities CAP-6
- architecture — _bmad-output/initiative-mediastation-mvp/architecture-mediastation/architecture-mediastation.md, AD-3, AD-8, AD-10, AD-11, AD-16
- ux — _bmad-output/initiative-mediastation-mvp/ux-mediastation/EXPERIENCE.md (Information Architecture, Component Patterns, State Patterns, Interaction Primitives (Model fokusu), Accessibility Floor, Voice and Tone), _bmad-output/initiative-mediastation-mvp/ux-mediastation/DESIGN.md
- design — _bmad-output/initiative-mediastation-mvp/ux-mediastation/mockups/
- retro — _bmad-output/initiative-mediastation-mvp/tickets/epic-foundation-contracts/epic-foundation-contracts-retrospective.md, F11, F17, F18, R4/N3, R7/N8
- ledger — _bmad-output/initiative-mediastation-mvp/deferred-work.md, DW-19, DW-26, DW-28

## Notes

- Decision: przed startem tej epiki ma być zrobiony bug `backlog/bug-launch-przy-aplikacji-na-pierwszym-planie-daje-dwa-foreground.md` (F6 z retro epiki 2); dopiero tu da się wybrać kafelek przy aplikacji na wierzchu (2026-10-22).
- Update: warunek powyżej spełniony — bug zamknięty jako done, poprawka w `21633a2` (launch przy innej aplikacji na pierwszym planie jest odrzucany), 2026-10-22.
- Decision: linię success CAP-6 „Nowa karta »Do oceny« nie kradnie fokusu” weryfikuje epic-do-oceny; ta epika dostarcza model fokusu kluczowany stabilnymi id (2026-10-18).
- Decision: przekazanie Actions z Orchestratora do Shell w trybie `shell` (gałąź w Orchestratorze i komunikat przez most) dostarcza ta epika; epic-orchestrator-lifecycle obsługuje z Actions tylko Home (inception epiki 2, 2026-10-21).
- Waits on epic-foundation-contracts, epic-orchestrator-lifecycle, epic-input because: host Shell, snapshot z `focusRequest`, Actions z `phase: long`.
- Deferred: guard `e.Source` w `ShellBridge` (tylko wiadomości z origin shella) i blokada nawigacji WebView2 poza shell. Źródło: F11 w retro epic-foundation-contracts (`_bmad-output/initiative-mediastation-mvp/tickets/epic-foundation-contracts/epic-foundation-contracts-retrospective.md`); wcześniej triage 1.1 #6 („guard należy do story wprowadzającej Intents”). Warunek: pierwsza story tej epiki, która wysyła Intents z UI przez most (2026-10-20).
- Deferred: lista błędów na ekranie błędu konfiguracji ucina się bez informacji (`overflow: hidden` na scenie 1080 px); brakuje wskaźnika „i jeszcze K” albo innego rozwiązania z UX. Źródło: F17 w retro epic-foundation-contracts (`_bmad-output/initiative-mediastation-mvp/tickets/epic-foundation-contracts/epic-foundation-contracts-retrospective.md`). Warunek: story tej epiki, która dotyka ekranu błędu konfiguracji; gdy żadna, Refactor sweep tej epiki (2026-10-20).
- Deferred: `title` i `icon` w `apps.json` mogą być puste albo z samych spacji. Źródło: F18 w retro epic-foundation-contracts (`_bmad-output/initiative-mediastation-mvp/tickets/epic-foundation-contracts/epic-foundation-contracts-retrospective.md`); retro nie wskazuje epiki dla tych dwóch pól, ta jest ich pierwszym konsumentem. Warunek: story, która renderuje kafelki z `title` i `icon` (2026-10-20).
- Deferred: `ShellBridge` (ścieżki odrzucenia, `bridgeMessageRejected`) oraz `ShellWindow` (`ProcessFailed`, `NewWindowRequested`) nie mają testów ani pełnego manualnego pokrycia. Źródło: R4/N3 w retro epic-foundation-contracts (`_bmad-output/initiative-mediastation-mvp/tickets/epic-foundation-contracts/epic-foundation-contracts-retrospective.md`). Warunek: pierwsza story tej epiki dotykająca mostu albo okna shella dodaje test lub manualny check (2026-10-21).
- Deferred: `NavigationCompleted` loguje `OperationCanceled` jako `navigationFailed`. Źródło: R7/N8 w retro epic-foundation-contracts (`_bmad-output/initiative-mediastation-mvp/tickets/epic-foundation-contracts/epic-foundation-contracts-retrospective.md`). Warunek: pierwsza story tej epiki z większą liczbą nawigacji (2026-10-21).
- Decision: refinement epiki (2026-10-26, na UM760): stan otwartego AppMenu trzyma Shell jako efemeryczny stan nawigacji (AD-16); Orchestrator w trybie `shell` przekazuje do Shell także Home, żeby zamykał nakładkę; nowego Intentu `OpenAppMenu` nie ma. Pozycje AppMenu używają istniejących `RestartApp` i `CloseApp` (2026-10-26).
- Decision: linia stanu `Background` w tej epice nie ma tytułu treści („W tle · wstrzymane” dla `web`, „W tle · wyciszony” dla `moonlight`); tytuł w snapshocie i w linii dokłada epic-netflix-dpad jako osobną pozycję, zapisaną w jej Notes (2026-10-26).
- Decision: ikony kafelków (`icon` w `apps.json` to nazwa pliku, np. `netflix.png`) leżą w `config/icons/`; brak pliku daje kafelek z samym tytułem i wpis `warn` w logu (2026-10-26).
- Decision: DW-28 (generowanie kontraktów przy Application Control) jest pierwszą story epiki, bo każda kolejna zmienia kontrakty Core ↔ Shell (2026-10-26).
- Decision: dług z ledgera w tej epice: DW-19 → story 2 (refactor otwierający), DW-28 → story 1, DW-26 → zakres story 10 (lista zamknięta z ID). Poza epiką: DW-3 i DW-4 → epic-browserhost (pierwszy prawdziwy host), DW-6, DW-10, DW-11, DW-17, DW-22, DW-25 → epic-osk, DW-24 → story z backlogu „`--calibrate-input` zapisuje wybrany przycisk Klawiatura do input.json”; DW-13/14/15/29/32 (S10) i DW-20/21 (narzędzie pętli) zostają w ledgerze (2026-10-26).
- Decision: odroczone pozycje z retro epiki 1 przypisane: F11 (guard `e.Source`, blokada nawigacji), R4/N3 (testy `ShellBridge`/`ShellWindow`) i R7/N8 (`OperationCanceled`) → story 3; F18 (puste `title`/`icon`) → story 5; F17 (ucinana lista błędów) → story 9 (2026-10-26).
- Decision: tracer bullet to story 3: na bieżącej maszynie strzałki pilota albo klawiatury przesuwają jeden fokus po kafelkach, a OK na kafelku daje `LaunchApp` i — przy `MissingHostPort` — kafelek w `Failed`. Stan `Background` w tej epice sprawdzają tylko testy na snapshotach testowych, bo hostów jeszcze nie ma (2026-10-26).
- Decision: breakdown zaakceptowany przez użytkownika (10 entries): lane Core (1 i 2 równolegle → 3), lane web 3 → 4 → 5 → 6, 4 → 7, 5 + 7 → 8, story 9 równolegle po 3, story 10 (Refactor sweep) na końcu; bez osobnego zestawu end-to-end, Done when 5 sprawdza odbiór całej epiki na UM760 (2026-10-26).
- Source conflict: EXPERIENCE.md, Interaction Primitives i Handoff („OK na kafelku Failed = Intent AppMenu”) vs Decision z 2026-10-26 (stan AppMenu w Shell, bez Intentu `OpenAppMenu`) — EXPERIENCE.md do poprawy przez `bmad-ux`; AD-8 („Orchestrator ignoruje Actions inne niż Home”) zdezaktualizuje się po story 3 — do poprawy przez `bmad-architecture` (2026-10-26).
- Open question: co pokazuje kafelek `native` (Plex) w `Background` — DESIGN.md, Stany kafelka, definiuje chip i linię tylko dla Netflixa i Moonlighta; czeka story 5 (2026-10-26).
- Open question: story 9 idzie równolegle ze stories 4–8, choć dzieli z nimi layout Home, `tokens.css` i fonty; albo `after = [5]`, albo świadomie przyjęta kolizja przy merge (2026-10-26).
