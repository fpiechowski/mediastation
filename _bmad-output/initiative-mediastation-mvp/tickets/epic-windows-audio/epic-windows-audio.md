---
type: epic
title: "Windows & audio focus"
parent: initiative-mediastation-mvp
covers: [CAP-4, CAP-5]
sources: [_bmad-output/initiative-mediastation-mvp/spec-mediastation/spec-mediastation.md, _bmad-output/initiative-mediastation-mvp/architecture-mediastation/architecture-mediastation.md, _bmad-output/initiative-mediastation-mvp/research-browser-control-for-streaming-services/digests/spike-um760.md, _bmad-output/initiative-mediastation-mvp/deferred-work.md]
companions: [_bmad-output/initiative-mediastation-mvp/ux-mediastation/EXPERIENCE.md]
after: []
assignee: ""
risk: high
---

# Windows & audio focus

## Description

CAP-4 i CAP-5: jeden komponent decyduje o widoczności, Z-order i foreground każdego okna, więc ekran główny wraca nad dowolne okno, a słychać tylko aktywną aplikację. Epika dostarcza WindowManager (jedyny właściciel foreground, raportuje `ForegroundChanged`), AudioRouter (wyciszanie drzewa procesów od `rootPid`, ponawiane przy nowej sesji audio) i sekwencję przełączania w Orchestratorze. Okna Edge, Chrome i Moonlighta do weryfikacji uruchamia dev harness, bo hosty powstają w późniejszych epikach.

## Outcome

Home zawsze przywraca ekran główny i nic nie gra z tła; sygnałem są success CAP-4 i CAP-5.

## Requirements

- R1 (spec CAP-4 success 1; AD-5): Opuszczane okno aplikacji jest minimalizowane, nigdy ukrywane (`SW_HIDE`); po Home okno przeglądarki jest zminimalizowane i nadal widoczne (`IsIconic` i `IsWindowVisible` prawdziwe).
- R2 (spec CAP-4 success 2; AD-5, AD-15; digest S10): Home przywraca shell na pierwszy plan nad Edge, Chrome, Moonlightem (borderless) i pulpitem, a aktywacja aplikacji przywraca i wynosi jej okno, w obu przypadkach z odblokowaniem foreground (symulowany Alt albo `AttachThreadInput`). Każda komenda okienna kończy się `CommandCompleted` albo `CommandFailed` z kodem błędu.
- R3 (spec CAP-4 success 3; AD-5, AD-8, AD-15; decyzja 2026-10-27, U2/DW-44): Każda zmiana okna na pierwszym planie, także bez udziału MediaStation, trafia do Orchestratora jako `ForegroundChanged{ hwnd, pid }` i jest logowana z właścicielem (shell, aplikacja, obce). Gdy okno na wierzchu się ustali (`foregroundSettleMs`), shell na wierzchu daje opuszczenie aplikacji `Foreground` jak po Home (bez `ShowShell`) i tryb `shell`, a okno aplikacji w `Background` daje jej aktywację jak po OK na kafelku (bez `restore` i foreground) i tryb `app`; pilot steruje oknem na wierzchu bez Home. Echo własnych komend okiennych i okno obce nie zmieniają trybu, stanu ani dźwięku.
- R4 (spec CAP-4 success 4; AD-14, AD-15): Tylko WindowManager zmienia widoczność, Z-order i foreground jakiegokolwiek okna, w tym shella; hosty (także dev harness) i `ShellWindow` nigdy same nie wynoszą okna, a build odrzuca takie wywołanie poza `Platform/Window`.
- R5 (spec CAP-5; AD-5, AD-15; digest S9; decyzja 2026-10-27, U3/DW-45): Słychać tylko aktywną aplikację: AudioRouter trzyma wyciszenie każdej sesji audio w drzewie procesów od `rootPid` równe regule tego drzewa, w obie strony, także dla sesji wyciszonych albo odciszonych poza nim (mikser Windows, poprzednia instancja); zmiana w mikserze jest cofana w ok. 1 s z wpisem w logu. `IChannelAudioVolume` jest fallbackiem, gdy sesja nie udostępnia `ISimpleAudioVolume`. Na ekranie głównym wszystkie aplikacje są wyciszone; głośność systemu i sesje spoza drzew z regułą należą do użytkownika.
- R6 (spec CAP-5, nowa sesja audio; AD-5; spec Open Questions): Nowa sesja audio, która powstaje w drzewie procesów aplikacji, podlega bieżącej regule tego drzewa w ciągu ok. 1 s, bez udziału Orchestratora; wpis w logu to pokazuje.
- R7 (AD-5, AD-10, AD-13, AD-14): Orchestrator wysyła komendy w kolejności z AD-5 na każdej ścieżce: opuszczenie aplikacji to `mute` → `minimize` (krok `pause` dokłada epic-netflix-dpad), aktywacja to `restore` z foreground → `unmute`. Aplikacja adoptowana, spóźniona po Home w `Starting` albo `Failed` przy żywym procesie jest wyciszona i zminimalizowana, a wynik ze starego `transitionId` nie cofa nowszej decyzji.
- R8 (spec CAP-4 i CAP-5, checki; Decision 2026-10-18): Dev harness, włączany wyłącznie zmienną środowiskową, uruchamia dla wpisów `apps.json` okna Edge, Chrome i Moonlighta (borderless) i raportuje `AppWindowAttached{ appId, hwnd, rootPid }` oraz `ProcessExited`; bez zmiennej hosty zostają `MissingHostPort`.
- R9 (AD-5, zamykanie aplikacji i koniec reguły; decyzja 2026-10-27): Zamknięcie aplikacji odcisza jej drzewo, zanim proces zniknie, a koniec reguły drzewa (`ForgetProcessTree`, śmierć korzenia, zamknięcie MediaStation) raz odcisza każdą sesję przypisaną do tego drzewa, więc żadna sesja nie zostaje wyciszona poza MediaStation.

## Constraints

- AD-14, AD-2: WindowManager i AudioRouter wykonują wywołania Win32 i COM na własnych wątkach, nigdy na wątku Orchestratora ani UI; zawieszony UI shella nie blokuje `ShowShell`, więc operacje na oknie shella są asynchroniczne wobec jego wątku.
- AD-4: wykonawcy nie decydują: odpowiadają `CommandCompleted` albo `CommandFailed{ errorCode }` i nie przepuszczają wyjątków przez port. Ponowienie reguły wyciszenia przy nowej sesji to wykonanie stanu zadanego komendą, nie decyzja.
- AD-8: odblokowanie foreground symulowanym Alt idzie przez `SendInput` ze znacznikiem `dwExtraInfo` (helper ze story 3.7), więc hook go ignoruje.
- AD-15: WindowManager i AudioRouter działają tylko na `hwnd` i `rootPid` z Command oraz na uchwycie shella wpisanym przez App.
- Consistency Conventions: interop tylko w `Platform/*` przez CsWin32; logika decyzji (drzewo procesów, reguła wyciszenia, wybór odblokowania, właściciel okna z `ForegroundChanged`) leży poza typami Win32 i COM i jest testowana przez linkowane źródła App.

## Done when

1. Opuszczona przeglądarka jest zminimalizowana, nigdy `SW_HIDE` (AD-5).
2. Na UM760 Home przywraca shell nad Edge, Chrome, Moonlightem (borderless) i pulpitem, z odblokowaniem foreground, 16/16 (4 cele × 4 naciśnięcia) (AD-5, AD-15).
3. Zmiana foreground bez udziału MediaStation trafia do Orchestratora jako `ForegroundChanged`, a host nigdy sam nie wynosi okna na wierzch; po Alt+Tab na ekran główny albo na okno aplikacji w tle pilot steruje oknem na wierzchu bez Home, a słychać tylko je (AD-5, AD-8, AD-14, AD-15).
4. Na UM760 po przełączeniu Netflix ↔ Moonlight i z powrotem słychać wyłącznie aplikację na pierwszym planie, także po nowej sesji audio w drzewie przeglądarki (np. zmiana odcinka) i po ręcznej zmianie wyciszenia w mikserze Windows, a po zamknięciu albo restarcie aplikacji jej sesje nie są wyciszone (AD-5, AD-15).

## Boundaries

`Platform/Window`, `Platform/Audio`, sekwencja przełączania w Orchestratorze i dev harness w `Hosts/DevHarness`. Nie: `HideOsk` przed przełączeniem z zewnątrz (AD-5) ani zamiar echa w trybie `osk` (właściciel okna pod OSK, AD-15); oba dokłada epic-osk razem z OSK, launch przeglądarki z CDP i Moonlighta jako produkt (epic-browserhost, epic-moonlight-native), pauza przez stronę (epic-netflix-dpad), okno OSK (epic-osk), Home przy zawieszonym shellu przykrytym oknem-duchem `dwm` (DW-31, zostaje w ledgerze). Produktowe powtórzenie checku audio przez kafelki robi epic-moonlight-native.

## References

- design — _bmad-output/initiative-mediastation-mvp/ux-mediastation/EXPERIENCE.md, State Patterns → Przełączenie okna poza pilotem (Alt+Tab)
- parent — _bmad-output/initiative-mediastation-mvp/spec-mediastation/spec-mediastation.md, Capabilities CAP-4, CAP-5, Open Questions (nowe sesje audio, tryb okna przeglądarki)
- architecture — _bmad-output/initiative-mediastation-mvp/architecture-mediastation/architecture-mediastation.md, AD-2, AD-4, AD-5, AD-8 (bramka foreground, znacznik `SendInput`), AD-10, AD-13 (`AppWindowAttached` bez CDP), AD-14, AD-15, Consistency Conventions (Interop Win32, Testy kodu App)
- research — _bmad-output/initiative-mediastation-mvp/research-browser-control-for-streaming-services/digests/spike-um760.md, claims S8 (`SW_HIDE`), S9 (sesje audio w procesie potomnym, `IChannelAudioVolume`), S10 (odblokowanie foreground 16/16), S11 (Moonlight borderless)
- ledger — _bmad-output/initiative-mediastation-mvp/deferred-work.md, DW-13, DW-14, DW-15, DW-32 (wybór celu w `check-home-s10.ps1`), DW-33 (scenariusze odbioru z epiki 4), DW-34 (`inject-power.ps1`), DW-31 (poza epiką)

## Notes

- Decision: stories tej epiki zależne od trybu okna czekają na story 1 epic-browserhost; `order_conflict` z `tickets.py` jest zamierzony (2026-10-18).
- Decision: check 2 i 4 na UM760 używa okien uruchomionych przez dev harness; produktowe powtórzenie audio jest w epic-moonlight-native (2026-10-18).
- Waits on epic-browserhost because: story 1 ustala flagę trybu okna, od której zależy obsługa okna przeglądarki.
- Waits on epic-home-screen because: wspólny kod hosta Shell i okno ekranu głównego do przywracania.
- Decision: refinement epiki (2026-10-26, na UM760): epika potrzebuje przed stories tylko rozstrzygnięć zapisanych niżej; architektura (AD-5, AD-14, AD-15) już ustala resztę, więc bez `bmad-architecture` i bez spike'a przed planowaniem (2026-10-26).
- Decision: otwarte pytanie o nowe sesje audio po zmianie odcinka rozstrzyga projekt, nie pomiar przed budową: AudioRouter zawsze ponawia regułę przy nowej sesji w drzewie (story 2, sprawdzane syntetyczną sesją w wyciszonym drzewie), a zmianę odcinka Netflixa mierzy odbiór całej epiki na UM760 (Done when 4); zastępuje plan „wczesnej story pomiarowej na Netflixie” z 2026-10-18 (2026-10-26).
- Decision: dev harness włącza zmienna `MEDIASTATION_DEV_HARNESS=1`, na wzór `MEDIASTATION_DEBUG_HANG_UI`; leży w `src/MediaStation.App/Hosts/DevHarness/`, używa profilu `profiles\<appId>`, żeby logowanie do Netflixa z odbioru przeszło na BrowserHost, i nie otwiera portu CDP. Zastępują go per typ prawdziwe hosty z epic-browserhost i epic-moonlight-native (2026-10-26).
- Decision: polityka `ForegroundChanged` (R3) trzyma się AD-8 i AD-15: Orchestrator tylko rozpoznaje właściciela okna i loguje go, bez zmiany trybu, stanu aplikacji i dźwięku; Alt+Tab na okno aplikacji w `Background` zostawia ją wyciszoną, a powrót daje Home. Zastępuje Assumption z tego samego dnia (aktywacja po Alt+Tab), którą set check wskazał jako sprzeczną z AD-8; zmiana tej reguły wymagałaby `bmad-architecture` (2026-10-26).
- Assumption: pierwsze pokazanie okna shella przy starcie zostaje w App (`window.Show()` bez `Activate` i `Topmost`), a każde późniejsze wyniesienie shella robi WindowManager (`ShowShell`); reguła buildu z R4 obejmuje wszystko poza tym jednym wywołaniem (2026-10-26).
- Decision: tracer bullet to story 1: na bieżącej maszynie z dev harnessem OK na kafelku `web` wynosi okno Edge, a Home minimalizuje je (nie ukrywa) i przywraca shell na pierwszy plan z odblokowaniem; prawdziwy WindowManager zastępuje `LogWindowManagerPort`. Warstwa `Platform/Audio` nie wchodzi do tracera, bo jest najmniej pewną częścią epiki i dostaje własną story zaraz po nim. Najmniej pewna jest story 2 (AudioRouter i nowe sesje), więc idzie zaraz po tracerze (2026-10-26).
- Decision: breakdown (7 entries, przyjęty w trybie auto na rekomendacjach PO, 2026-10-26): lane Core 1 → 2 → 3 → 4; lane harnessu 1 → 5 (też po 6.1); 6 (reguła jednego właściciela okien) po 4 i 5, bo obejmuje cały kod okienny epiki; 7 Refactor sweep na końcu; bez osobnego zestawu end-to-end, Done when 2 i 4 sprawdza odbiór całej epiki na UM760 przez `fp-acceptance` (2026-10-26).
- Decision: wpis 6.1 (spike trybu okna i źródła wersji, bez prerequisites) dopisany do breakdownu epic-browserhost już teraz, żeby story 5 tej epiki mogła na niego czekać przez `after = ["6.1"]`; wcześniej tylko proza w Notes obu epik (2026-10-26).
- Decision: dług z ledgera w tej epice: DW-13, DW-14, DW-15, DW-32 (wybór celu w `check-home-s10.ps1`) → story 5, która rozszerza skrypt o check przywrócenia shella; DW-31 (okno-duch `dwm` nad zawieszonym shellem) zostaje w ledgerze, bo wymaga fizycznego pilota (2026-10-26).
- Decision: story 5 może iść równolegle ze stories 2, 3 i 4 mimo wspólnego `Platform/System/NativeMethods.txt` (jedna lista nazw dla CsWin32) i rejestracji w `CompositionRoot.cs`; konflikt przy merge sprowadza się do dopisanych linii (2026-10-26).
- Open question: czy zmiana odcinka Netflixa tworzy nową sesję audio w drzewie Edge (S9 `-Watch` niezmierzony); story 2 działa przy obu odpowiedziach, a odpowiedź daje odbiór epiki na UM760 (Done when 4).
- Decision: bez `plan_checkpoint` i `done_checkpoint`, jak w epic-home-screen: ryzyko pokrywa pełne review dla `risk` medium i wyższego (decyzja inicjatywy), `verify` w pętli na bieżącej maszynie i odbiór epiki przez `fp-acceptance` (2026-10-26).
- Update: DW-29 (`no target window` dla Edge) zamknięte 2026-10-26 przez bug 3.13 i odbiór epiki 3 (S13 16/16 dwa razy), więc wypada z zakresu story 5; DW-13, DW-14, DW-15 i DW-32 zostają (2026-10-26).
- Deferred: scenariusze z odbioru epiki 4, których nie dało się sprawdzić bez okna aplikacji (DW-33): kafelek w `Starting` i `Foreground` po OK, w `Background` z kropką na zakładce po Home, fokus na ostatnim kafelku po powrocie, AppMenu z długiego OK na `Background` i `RestartApp` odróżniony od `CloseApp` w logu. Źródło: `tickets/epic-home-screen/epic-home-screen-um760-acceptance.md` (Poza zakresem, werdykt). Warunek: plan odbioru tej epiki na UM760 (`fp-acceptance`) dokłada je jako scenariusze z dev harnessem (2026-10-26).
- Deferred: `tools/inject-power.ps1` liczy snapshoty od startu procesu i daje fałszywy FAIL na używanej aplikacji (DW-34). Źródło: odbiór epiki 4, S18. Warunek: zamknięta lista Refactor sweep (story 7); do tego czasu plan odbioru uruchamia skrypt na świeżej aplikacji (2026-10-26).
- Update: spike 6.1 (`2ea151f`, 2026-10-27, UM760) rozstrzygnął tryb okna przeglądarki: `--kiosk` (AD-1), a źródło wersji poza CDP: `FileVersion` binarki i `Last Version` w `user-data-dir` (AD-13). Pytania o tryb okna w tych Notes są zamknięte; DW-38 (2026-10-27).
- Decision: architektura `36f9e2f` (2026-10-27) rozstrzygnęła U2 i U3: AD-5 (przełączenie z zewnątrz, reguła audio jako stan docelowy ponad mikserem, odciszenie przy zamknięciu i końcu reguły), AD-8 (`mode` idzie za ustalonym oknem na wierzchu), AD-15 (polityka `ForegroundChanged`: ustalenie, echo, `ReportForeground`). Zastępuje Decision z 2026-10-26 „Alt+Tab tylko log, powrót przez Home” i Notes o polityce `ForegroundChanged` z tego dnia; zachowanie widoczne dla użytkownika opisuje `EXPERIENCE.md` (UX `dac1625`, State Patterns → Przełączenie okna poza pilotem). R3, R5 przepisane, R9 dodane; 9 i 10 odblokowane (2026-10-27).
- Decision: bug 8 bez `hitl`, żeby szedł w pętli: strumień dowodzi log Moonlighta albo różniące się zrzuty okna, wzrokowo ponowny odbiór S6 (2026-10-27).
- Decision: odbiór epiki na UM760 (2026-10-27, HEAD 3d29fee) dał werdykt użytkownika **not accepted**: Moonlight po Home i powrocie stoi na zamrożonym UI (S6), więc Done when 2 nie jest spełnione. Uwagi U1–U3 wracają do tej epiki jako 8 (bug, blokuje akceptację), 9 i 10; U4 (kropka przy zakładce, epika 4) idzie do backlogu jako bug 5. Źródło: `epic-windows-audio-um760-acceptance.md`, tabela uwag; ledger DW-43–46 (2026-10-27).
- Decision: U2 (routing wejścia po Alt+Tab) i U3 (stan wyciszeń po ręcznych zmianach w mikserze) zostają w tej epice, bo zmieniają R3 i R5, na których epic-browserhost buduje „minimize i mute”; decyzja użytkownika (2026-10-27).
- Decision: polityka `ForegroundChanged` z 2026-10-26 (tylko log, powrót przez Home) przestaje wystarczać: użytkownik chce, żeby pilot działał po Alt+Tab. Zmiana AD-8/AD-15 i AD-5 (Audio) należy do `bmad-architecture`; do jej wyniku 9 i 10 mają `unknown`, a po niej R3 i R5/R6 zostaną przepisane przez SPEC (2026-10-27).
- Decision: bez nowego Refactor sweep dla 8–10; trzy pozycje nie uzasadniają osobnej story porządkowej (2026-10-27).
- Decision: ponowny odbiór na UM760 po 8–10 jako `new-pass` do planu odbioru: S6 i S18–S21 (U1), S8–S9 według nowej reguły AD-8 (U2), nowy scenariusz z ręczną zmianą w mikserze (U3) i scenariusz „po Zamknij i Uruchom ponownie sesje aplikacji nie są wyciszone” (R9) (2026-10-27).
- Decision: 8, 9 i 10 nie zależą od siebie, ale dotykają wspólnego kodu (8 i 9 WindowManager, 9 i 10 Orchestrator i testy Mute/Unmute), więc w pętli idą po kolei 8 → 9 → 10, bez `after`; set check 2026-10-27.
