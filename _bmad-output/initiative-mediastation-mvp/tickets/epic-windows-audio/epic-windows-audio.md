---
type: epic
title: "Windows & audio focus"
parent: initiative-mediastation-mvp
covers: [CAP-4, CAP-5]
sources: [_bmad-output/initiative-mediastation-mvp/spec-mediastation/spec-mediastation.md, _bmad-output/initiative-mediastation-mvp/architecture-mediastation/architecture-mediastation.md, _bmad-output/initiative-mediastation-mvp/research-browser-control-for-streaming-services/digests/spike-um760.md, _bmad-output/initiative-mediastation-mvp/deferred-work.md]
companions: []
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
- R3 (spec CAP-4 success 3; AD-15, AD-4, AD-8): Każda zmiana okna na pierwszym planie, także bez udziału MediaStation, trafia do Orchestratora jako `ForegroundChanged`; Orchestrator rozpoznaje właściciela okna (shell, aplikacja, obce) i zapisuje go w logu, a tryb, stan aplikacji i dźwięk zostają bez zmian (AD-8: `mode` nie zmienia się, gdy użytkownik przełączy okno; powrót daje Home).
- R4 (spec CAP-4 success 4; AD-14, AD-15): Tylko WindowManager zmienia widoczność, Z-order i foreground jakiegokolwiek okna, w tym shella; hosty (także dev harness) i `ShellWindow` nigdy same nie wynoszą okna, a build odrzuca takie wywołanie poza `Platform/Window`.
- R5 (spec CAP-5; AD-5, AD-15; digest S9): Słychać tylko aktywną aplikację: AudioRouter wycisza każdą sesję audio w drzewie procesów od `rootPid` nieaktywnej aplikacji i odcisza drzewo aktywnej, z `IChannelAudioVolume` jako fallbackiem, gdy sesja nie udostępnia `ISimpleAudioVolume`. Na ekranie głównym wszystkie aplikacje są wyciszone.
- R6 (spec CAP-5, nowa sesja audio; AD-5; spec Open Questions): Nowa sesja audio, która powstaje w drzewie procesów aplikacji, podlega bieżącej regule tego drzewa w ciągu ok. 1 s, bez udziału Orchestratora; wpis w logu to pokazuje.
- R7 (AD-5, AD-10, AD-13, AD-14): Orchestrator wysyła komendy w kolejności z AD-5 na każdej ścieżce: opuszczenie aplikacji to `mute` → `minimize` (krok `pause` dokłada epic-netflix-dpad), aktywacja to `restore` z foreground → `unmute`. Aplikacja adoptowana, spóźniona po Home w `Starting` albo `Failed` przy żywym procesie jest wyciszona i zminimalizowana, a wynik ze starego `transitionId` nie cofa nowszej decyzji.
- R8 (spec CAP-4 i CAP-5, checki; Decision 2026-10-18): Dev harness, włączany wyłącznie zmienną środowiskową, uruchamia dla wpisów `apps.json` okna Edge, Chrome i Moonlighta (borderless) i raportuje `AppWindowAttached{ appId, hwnd, rootPid }` oraz `ProcessExited`; bez zmiennej hosty zostają `MissingHostPort`.

## Constraints

- AD-14, AD-2: WindowManager i AudioRouter wykonują wywołania Win32 i COM na własnych wątkach, nigdy na wątku Orchestratora ani UI; zawieszony UI shella nie blokuje `ShowShell`, więc operacje na oknie shella są asynchroniczne wobec jego wątku.
- AD-4: wykonawcy nie decydują: odpowiadają `CommandCompleted` albo `CommandFailed{ errorCode }` i nie przepuszczają wyjątków przez port. Ponowienie reguły wyciszenia przy nowej sesji to wykonanie stanu zadanego komendą, nie decyzja.
- AD-8: odblokowanie foreground symulowanym Alt idzie przez `SendInput` ze znacznikiem `dwExtraInfo` (helper ze story 3.7), więc hook go ignoruje.
- AD-15: WindowManager i AudioRouter działają tylko na `hwnd` i `rootPid` z Command oraz na uchwycie shella wpisanym przez App.
- Consistency Conventions: interop tylko w `Platform/*` przez CsWin32; logika decyzji (drzewo procesów, reguła wyciszenia, wybór odblokowania, właściciel okna z `ForegroundChanged`) leży poza typami Win32 i COM i jest testowana przez linkowane źródła App.

## Done when

1. Opuszczona przeglądarka jest zminimalizowana, nigdy `SW_HIDE` (AD-5).
2. Na UM760 Home przywraca shell nad Edge, Chrome, Moonlightem (borderless) i pulpitem, z odblokowaniem foreground, 16/16 (4 cele × 4 naciśnięcia) (AD-5, AD-15).
3. Zmiana foreground bez udziału MediaStation trafia do Orchestratora jako `ForegroundChanged`, a host nigdy sam nie wynosi okna na wierzch (AD-14, AD-15).
4. Na UM760 po przełączeniu Netflix ↔ Moonlight i z powrotem słychać wyłącznie aplikację na pierwszym planie, także po nowej sesji audio w drzewie przeglądarki (np. zmiana odcinka) (AD-5, AD-15).

## Boundaries

`Platform/Window`, `Platform/Audio`, sekwencja przełączania w Orchestratorze i dev harness w `Hosts/DevHarness`. Nie: launch przeglądarki z CDP i Moonlighta jako produkt (epic-browserhost, epic-moonlight-native), pauza przez stronę (epic-netflix-dpad), okno OSK (epic-osk), Home przy zawieszonym shellu przykrytym oknem-duchem `dwm` (DW-31, zostaje w ledgerze). Produktowe powtórzenie checku audio przez kafelki robi epic-moonlight-native.

## References

- parent — _bmad-output/initiative-mediastation-mvp/spec-mediastation/spec-mediastation.md, Capabilities CAP-4, CAP-5, Open Questions (nowe sesje audio, tryb okna przeglądarki)
- architecture — _bmad-output/initiative-mediastation-mvp/architecture-mediastation/architecture-mediastation.md, AD-2, AD-4, AD-5, AD-8 (bramka foreground, znacznik `SendInput`), AD-10, AD-13 (`AppWindowAttached` bez CDP), AD-14, AD-15, Consistency Conventions (Interop Win32, Testy kodu App)
- research — _bmad-output/initiative-mediastation-mvp/research-browser-control-for-streaming-services/digests/spike-um760.md, claims S8 (`SW_HIDE`), S9 (sesje audio w procesie potomnym, `IChannelAudioVolume`), S10 (odblokowanie foreground 16/16), S11 (Moonlight borderless)
- ledger — _bmad-output/initiative-mediastation-mvp/deferred-work.md, DW-13, DW-14, DW-15, DW-29, DW-32 (wybór celu w `check-home-s10.ps1`), DW-31 (poza epiką)

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
- Decision: dług z ledgera w tej epice: DW-13, DW-14, DW-15, DW-29, DW-32 (wybór celu w `check-home-s10.ps1`) → story 5, która rozszerza skrypt o check przywrócenia shella; DW-31 (okno-duch `dwm` nad zawieszonym shellem) zostaje w ledgerze, bo wymaga fizycznego pilota (2026-10-26).
- Decision: story 5 może iść równolegle ze stories 2, 3 i 4 mimo wspólnego `Platform/System/NativeMethods.txt` (jedna lista nazw dla CsWin32) i rejestracji w `CompositionRoot.cs`; konflikt przy merge sprowadza się do dopisanych linii (2026-10-26).
- Open question: czy zmiana odcinka Netflixa tworzy nową sesję audio w drzewie Edge (S9 `-Watch` niezmierzony); story 2 działa przy obu odpowiedziach, a odpowiedź daje odbiór epiki na UM760 (Done when 4).
- Decision: bez `plan_checkpoint` i `done_checkpoint`, jak w epic-home-screen: ryzyko pokrywa pełne review dla `risk` medium i wyższego (decyzja inicjatywy), `verify` w pętli na bieżącej maszynie i odbiór epiki przez `fp-acceptance` (2026-10-26).
