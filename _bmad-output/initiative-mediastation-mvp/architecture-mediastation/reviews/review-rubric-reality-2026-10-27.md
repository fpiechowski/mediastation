---
title: 'Review: update spine MediaStation MVP 2026-10-27 (rubric + reconcile, reality check Core Audio / foreground)'
target: ../architecture-mediastation.md
decision-log: ../.memlog.md
driver: ../../tickets/epic-windows-audio/epic-windows-audio.md
inputs:
  - ../../tickets/epic-windows-audio/epic-windows-audio-um760-acceptance.md (U2, U3, uwagi przed S18/S20)
  - ../../deferred-work.md (DW-37, DW-44, DW-45)
  - ../../tickets/epic-windows-audio/epic-windows-audio.md (R3, R5, R6, Notes 2026-10-26/27)
date: 2026-10-27
machine: 'UM760 (host MEDIASTATION, board F7BSI, model EliteMini Series)'
lenses: [rubric-walker, reconcile, reality-check]
scope: 'git diff spine: AD-5 (przełączenie z zewnątrz, reguła audio jako stan docelowy), AD-8 (mode idzie za ustalonym foreground), AD-14 (timer ustalenia), AD-15 (polityka ForegroundChanged)'
---

# Review: update spine'u 2026-10-27 (U2/U3 z odbioru epiki 5)

Maszyna: review powstał na **UM760** (`MEDIASTATION` / `F7BSI` / `EliteMini Series`). Spine nie był edytowany.

**Werdykt:** accept with fixes. Delta pokrywa U2 i U3 co do kierunku (pilot steruje oknem po Alt+Tab, reguła audio wygrywa z mikserem, DW-37 zastąpione), a twierdzenia Core Audio o GUID-zie kontekstu potwierdza dokumentacja Microsoftu. Przed przekazaniem do Sofii trzeba jednak domknąć dwie luki wysokiej wagi. Pierwsza: Windows **utrwala** wyciszenie sesji między uruchomieniami aplikacji, więc reguła działająca tylko „dopóki istnieje reguła” zostawia ciszę po zamknięciu aplikacji lub MediaStation (to prawdopodobne źródło „ciszy po przerwie” przed S18). Druga: obserwacja odłożona w czasie komend okiennych albo `Starting` jest oceniana „przy następnym ustaleniu”, które może nie nadejść albo przyjść ze starą obserwacją. Wtedy U2 wraca, a własny `CommandFailed` może zostać uznany za przełączenie z zewnątrz i cofnąć Home.

## Podsumowanie findings

| # | Severity | Miejsce | Finding | Sugerowany fix |
| --- | --- | --- | --- | --- |
| H1 | high | AD-5 „Reguła audio to stan docelowy…” (l. 136–141) × DW-37 × uwaga przed S18 | Dokumentacja „Audio Sessions”: ustawienia głośności i wyciszenia sesji renderującej są domyślnie trwałe, a nowa instancja aplikacji z tym samym GUID-em sesji dziedziczy zapisane ustawienia ostatniej zakończonej sesji. Reguła mówi tylko o sesjach „w drzewie z regułą”. Gdy aplikacja w `Background` (wyciszona) jest zamykana (`CloseApp`, `ForgetProcessTree`, `ProcessExited`) albo MediaStation kończy się lub pada, ostatnia sesja kończy się wyciszona. Następne uruchomienie Edge/Moonlighta, także poza MediaStation, startuje wyciszone. Wewnątrz MediaStation naprawi to wymuszone odciszenie, ale poza nią nie. Pasuje to do obserwacji „po przerwie Edge i Moonlight wyciszone w mikserze” i do „Moonlight cichy mimo odciszonej reguły (sessions=0 w chwili reguły)”. Spine nie mówi też, co z regułami przy zamknięciu MediaStation (dzisiejszy `AudioRouterEngine.Dispose` odcisza księgę, a księga znika z reguły) | Dopisać do AD-5: „Zwolnienie reguły: przed `ForgetProcessTree` i przy zamknięciu aplikacji przez Orchestrator AudioRouter odcisza sesje drzewa (stan neutralny = odciszone), a przy uporządkowanym zamknięciu MediaStation odcisza wszystkie drzewa z regułą. Po awarii zostaje utrwalone wyciszenie: naprawia je adopcja i reguła następnej instancji; poza MediaStation zostaje ryzykiem zaakceptowanym (wpis w Deferred)”. Kolejność dla `CloseApp`: unmute → zamknięcie procesu, bo sesja musi jeszcze istnieć. Wpis `audioSessionOverridden` dla sesji, która przy pierwszym skanie ma odziedziczone wyciszenie, rozróżnić polem (`reason: inherited|changed`), żeby log odbioru nie mylił dziedziczenia z ruchem w mikserze |
| H2 | high | AD-15 „Kiedy nie oceniać” (l. 396) × AD-10 adopcja/wybudzenie × AD-14 | (a) „Obserwacja jest tylko zapisana, a ocenia ją następne ustalenie”. Gdy po ostatnim `CommandCompleted` albo po wyjściu z `Starting` nie przyjdzie już żadne `ForegroundChanged` (użytkownik zrobił Alt+Tab w trakcie komend i stoi), nikt jej nie oceni, a `mode` zostaje rozjechany z oknem. To dokładnie objaw U2. (b) Odwrotnie, jeśli implementacja oceni zapisaną obserwację później, może być już nieaktualna: przy adopcji zapisane „`app` X” (okno X było na wierzchu przed `Minimize`) po dokończeniu komend zostanie ocenione jako „X w `Background` → aktywacja X”, wbrew AD-10 („na wierzchu jest ekran główny”). (c) `SetForegroundWindow` może zawieść (dokumentacja: system może odmówić mimo spełnionych warunków). Po `CommandFailed` dla `ShowShell` okno aplikacji zostaje na wierzchu, właściciel `app` X ≠ zamiar `shell`, więc reguła „Różnica to przełączenie z zewnątrz” aktywuje X i cofa Home użytkownika | „Gdy warunek blokady ustaje (ostatnia komenda okienna bieżącego `transitionId` dostała wynik albo żadna aplikacja nie jest w `Starting`), Orchestrator (re)startuje timer ustalenia, a ocena używa **świeżego** odczytu foreground (WindowManager raportuje bieżące `ForegroundChanged` na żądanie, np. komendą `ReportForeground`), nigdy zapisanej obserwacji”. Do tego: „Różnica po `CommandFailed` komendy okiennej bieżącego `transitionId` nie jest przełączeniem z zewnątrz: Orchestrator loguje `foregroundMismatch{ transitionId, owner }` i nic nie zmienia; naprawia ją Home”. Doprecyzować „aplikacja jest w `Starting`” na „którakolwiek aplikacja” |
| M1 | medium | AD-5 „Przełączenie z zewnątrz” (l. 133) × U1 (bug 8) | (a) Ścieżka zewnętrzna minimalizuje okno, które **nie** jest już na wierzchu, podczas gdy użytkownik wybrał inne okno. `SW_MINIMIZE` według dokumentacji „aktywuje następne okno w Z-order”, więc ukradłby foreground wybranemu przez użytkownika oknu (shell albo X) i wywołał kolejne `ForegroundChanged`. Kod już używa `SW_SHOWMINNOACTIVE` (`Win32WindowApi.cs:27–32`), ale spine tego nie wiąże, a AD-5 mówi tylko „minimize”. (b) Alt+Tab z Moonlighta teraz minimalizuje Moonlighta. Jeśli przyczyną U1 (zamrożone „input stream establishment…” po Home i powrocie) jest minimalizacja i przywrócenie Moonlighta, Alt+Tab odtworzy U1 | (a) W AD-5: „`minimize` to zawsze minimalizacja bez aktywacji (`SW_SHOWMINNOACTIVE`); nigdy `SW_MINIMIZE` ani `SW_HIDE`”. (b) W epice: 9 (U2) po 8 (U1) i scenariusz odbioru „Alt+Tab z Moonlighta na shell i z powrotem” w `new-pass` |
| M2 | medium | AD-5 „w obie strony” (l. 137) × memlog D3 | Reguła „w obie strony” oznacza, że użytkownik nie wyciszy aktywnej aplikacji w mikserze: zmiana wraca po ok. 1 s. To decyzja produktowa. W memlogu D1 i D2 mają „user chose”, a D3 tego zapisu nie ma. U3 opisuje tylko kierunek „tło gra / cisza mimo odciszenia”. Głośność systemu i wyciszenie urządzenia zostają u użytkownika, co łagodzi skutek, ale spine o tym nie mówi | Potwierdzić z użytkownikiem i zapisać w AD-5 jednym zdaniem: „Wyciszenie aktywnej aplikacji w mikserze jest cofane; do wyciszenia służy głośność systemu (endpoint), której AudioRouter nie dotyka”. To samo w R5 i w nowym scenariuszu odbioru |
| M3 | medium | AD-15 Binds, AD-4 Rule, AD-11 `settings.json`, Deferred/otwarte pytania, log | Spine wymaga dopasowań poza zmienionymi AD: (1) AD-15 **Binds** nie zawiera Orchestratora ani InputHub, choć polityka `ForegroundChanged` to logika Orchestratora, a jej skutkiem jest `SetInputRouting`. (2) AD-4 „nie wycisza z własnej inicjatywy” bez odsyłacza do okresowej poprawki z AD-5 (uzasadnienie jest tylko w AD-5). (3) AD-11 wymienia `ratingPendingDays`, `cdpReconnectTimeout`, a nie `foregroundSettleMs` i jego zakres walidacji. (4) Otwarte pytanie z memlogu (sesja z fallbackiem `IChannelAudioVolume`, wszystkie kanały 0, poza księgą: czy wymuszone odciszenie ustawia 1.0?) nie trafiło do spine'u. (5) Brak nazwy wpisu logu dla przełączenia z zewnątrz; odbiór (S8/S9 i nowy S z mikserem) potrzebuje asercji, a dziś ma tylko `foregroundChanged` | (1) Binds AD-15 += Orchestrator, InputHub. (2) AD-4: „…poza utrzymaniem stanu zadanego Commandem (AD-5: reguła audio)”. (3) AD-11: `foregroundSettleMs` (domyślnie 300, walidacja np. 100–2000). (4) Dopisać do „Otwartych pytań”: fallback kanałowy przy wymuszonym odciszeniu, miejsce: story AudioRoutera (10). (5) AD-15: „przełączenie z zewnątrz loguje `externalSwitch{ transitionId, owner, appId }`, echo `foregroundEcho`” albo jawnie: asercją jest `inputRouting` + `audioRuleApplied` |
| L1 | low | AD-15 „`foreign` … secure desktop (`hwnd` 0)” × `WinEventForegroundHook.cs:70` | Hook odrzuca zdarzenia z `hwnd` NULL, więc dla secure desktop żadna obserwacja nie powstaje i „ostatnią obserwacją” zostaje poprzednie okno. Skutek ten sam (nic się nie zmienia), ale zapis w spine nie odpowiada kodowi | „Brak okna (`hwnd` 0) nie daje obserwacji albo daje `foreign`; obie drogi nic nie zmieniają” |
| L2 | low | AD-8 „Między Alt+Tab a ustaleniem obowiązuje poprzedni `mode`” | Przez ~300 ms po Alt+Tab na shell z `app` + `web` D-pad i OK idą jako `action` do strony w tle, a z `app` + `moonlight` surowe klawisze trafiają do WebView2 shella. Świadomy koszt, ale nie zapisany | Jedno zdanie kosztu albo opcja: `EVENT_SYSTEM_SWITCHEND` (dokumentacja: puszczenie Alt+Tab, `hwnd` = okno docelowe) jako sygnał do natychmiastowego ustalenia, timer tylko dla zmian bez przełącznika |
| L3 | low | AD-5 l. 140 × `IAudioSessionEvents` | Callbacki `IAudioSessionEvents` przychodzą z wątków usługi audio. Dokumentacja wymaga, żeby nie blokowały, nie wołały `UnregisterAudioSessionNotification` i nie zwalniały ostatniej referencji w callbacku. „Skan działa na wątku AudioRoutera” to pokrywa tylko pośrednio | „Callback tylko wrzuca żądanie skanu do kolejki AudioRoutera” |
| L4 | low | stale komentarze w kodzie (do build, nie do spine'u) | `AudioRouterEngine.cs` (summary „odciska tylko to, co sam wyciszył”), `AudioSweepSchedule.cs` (poll tylko „dopóki jakiekolwiek drzewo jest wyciszone”, teraz każda reguła), `Orchestrator.Foreground.cs` („nic więcej”), `CoreAudioSessionBackend.cs:291, 330` (`SetMute(mute, null)` / `SetAllVolumes(..., null)`: dziś bez GUID-u kontekstu) | Zakres story 9 i 10 |

## Lens 1: rubric + reconcile

### Pokrycie U2 / DW-44

| Element wejścia | Pokrycie w spine |
| --- | --- |
| Pilot i klawiatura nie sterują shellem po Alt+Tab (tryb `app`) | AD-8 l. 219: `mode` idzie za ustalonym foreground; `SetInputRouting` przy przełączeniu z zewnątrz. Pokryte |
| „Użytkownik chce, żeby pilot działał także po Alt+Tab” | AD-5 l. 133 + AD-15 l. 394–399. Pokryte dla shella i aplikacji zarządzanej. Dla okna obcego świadomie nie (D2, user chose); zgodne z bramką foreground |
| Zastąpienie Decision 2026-10-26 i [ASSUMPTION] AD-15 | Zapisane w nagłówku polityki AD-15. Pokryte |
| Dziura: zmiana w trakcie komend / `Starting` | **H2** |

### Pokrycie U3 / DW-45 (z wnioskiem użytkownika przed S20)

Wniosek użytkownika: „MediaStation widzi tylko stan wyciszenia okien, który sam zainicjował. Grzebanie w mikserze czy Alt+Tab szybko psuje stan, który aplikacja widzi.”

| Element | Pokrycie |
| --- | --- |
| Mikser odcisza tło → tło gra ze streamem (S19 1. podejście) | AD-5: poll ~1 s + wymuszenie w obie strony. Pokryte |
| Cisza mimo odciszonej reguły (Moonlight, sessions=0 w chwili reguły) | Wymuszone odciszenie każdej sesji drzewa przy skanie. Pokryte wewnątrz MediaStation; przyczyna (utrwalone wyciszenie) nie jest nazwana i nie jest zamknięta poza MediaStation: **H1** |
| „Po przerwie” Edge i Moonlight wyciszone w mikserze (przed S18, bez diagnozy) | Częściowo: **H1** (pochodzenie z utrwalenia przez Windows) |
| „Alt+Tab psuje stan” (część audio) | Alt+Tab na shell/aplikację zarządzaną przełącza też regułę audio (AD-5 ścieżka zewnętrzna). Alt+Tab na obce okno niczego nie zmienia, więc aktywna aplikacja gra pod obcym oknem; to decyzja D2, zgodna z U2, ale warto jej nie zgubić w R3 |
| Zastąpienie DW-37 (odciszanie tylko z księgi) | Zapisane w AD-5 l. 137. DW-37 ma `status: done` z rozwiązaniem opartym na księdze; ledger trzeba opisać jako „superseded by AD-5 2026-10-27” |

Nic z U2/U3 nie zostało pominięte poza tym, co opisują H1 i H2. U1 i U4 są poza zakresem tej zmiany (bug 8 i ledger), z jedną zależnością w M1(b).

### Egzekwowalność i skuteczność zmienionych reguł

| Reguła | Egzekwowalna? | Czy zapobiega deklarowanej rozbieżności? |
| --- | --- | --- |
| AD-5 przełączenie z zewnątrz | Tak (test Orchestratora na fake'ach: `ForegroundChanged` → `TimerElapsed` → komendy bez `restore`/`ShowShell`) | Tak, „dwie sekwencje o różnych skutkach” znika; minimalizacja bez aktywacji musi być wiązana (M1) |
| AD-5 reguła audio jako stan docelowy | Tak (silnik testowany na fake backendzie; poll i GUID sprawdzalne) | W MediaStation tak; utrwalenie poza nią nie (H1); decyzja „w obie strony” do potwierdzenia (M2) |
| AD-8 `mode` za foreground | Tak (asercja `inputRouting` w logu po Alt+Tab) | Tak, pod warunkiem H2 |
| AD-14 timer ustalenia | Tak (`FakeTimeProvider`) | Tak |
| AD-15 polityka | Częściowo: „ocenia ją następne ustalenie” nie da się sprawdzić testem jako gwarancji, bo zależy od zdarzenia, które może nie przyjść | Pętla echa: tak. Miganie: tak. Utrata/zakłamanie obserwacji: nie (H2) |

### Inne części spine'u: sprzeczności i potrzebne dotknięcia

- **Stara formuła „`mode` nie zmienia się przy przełączeniu okna”** została zastąpiona w AD-8. W samym spine nie znalazłem innych wystąpień. Zostaje w epice (R3, Notes 2026-10-26) i w planie odbioru (S8/S9, opis części 2 „nie reaguje na nie sam”).
- **„AudioRouter odcisza tylko to, co sam wyciszył”** nie występuje już w spine; zostaje w kodzie (L4) i w rozwiązaniu DW-37.
- **AD-4** (M3-2), **AD-11** (M3-3), **AD-15 Binds** (M3-1): wymagają dotknięcia.
- **AD-10** adopcja i wybudzenie: brak sprzeczności w tekście, ale interakcja z odłożoną obserwacją (H2b). Warto zdanie: „adopcja i wybudzenie to przejścia z `transitionId`, więc podlegają regule »Kiedy nie oceniać«”.
- **AD-8 l. 231** (Home na ekranie głównym) i DW-41: po zmianie scenariusz DW-41 („okno aplikacji wyniesione Alt+Tab, tryb `shell`”) przestaje być stanem trwałym; DW-41 do przeglądu przez Sofię (może zostać tylko dla okna wyniesionego skryptem/obcego).
- **Consistency Conventions:** bez sprzeczności. Drobiazg nazewniczy: `foregroundSettleMs` (sufiks jednostki) obok `cdpReconnectTimeout`/`cdpConnectTimeout` (bez sufiksu). Ujednolicić albo zapisać konwencję.
- **Zakres MVP → architektura:** wiersz „Kilka aplikacji naraz, przełączanie bez zamykania” może dostać AD-8 (Alt+Tab steruje trybem). Niska waga.
- **Deferred / otwarte pytania:** dopisać fallback kanałowy (M3-4) i utrwalone wyciszenie poza MediaStation po awarii (H1).

### R-lista dla Sofii (epic-windows-audio)

- **R3:** przepisać w całości. Zamiast „tryb, stan aplikacji i dźwięk zostają bez zmian” ma być: ustalona zmiana foreground (timer `foregroundSettleMs`) na shell opuszcza aplikację `Foreground` (`pause` → `mute` → minimalizacja bez aktywacji, bez `ShowShell`, tryb `shell`); na okno aplikacji w `Background` aktywuje ją bez `restore` i foreground (`unmute`, tryb `app`, `SetInputRouting`); echo własnych komend i okno obce nic nie zmieniają; brak oceny w trakcie komend okiennych i `Starting` z ponowną oceną świeżego foreground po ich końcu (po H2); log z właścicielem i nazwanym wpisem przełączenia.
- **R5:** dopisać stan docelowy w obie strony (wymuszone odciszenie także sesji wyciszonych poza AudioRouterem, cofnięcie zmiany w mikserze w ok. 1 s, `audioSessionOverridden`), poziom głośności i sesje spoza drzew należą do użytkownika, zwolnienie reguły przy zamknięciu aplikacji i MediaStation (po H1), decyzja o wyciszeniu aktywnej aplikacji w mikserze (po M2).
- **R6:** skan okresowy przy każdej regule (nie tylko „wycisz”); nowa sesja, także z odziedziczonym wyciszeniem, dostaje regułę w ok. 1 s; własne zmiany z GUID-em kontekstu bez pętli.
- **R7:** „na każdej ścieżce” obejmuje przełączenie z zewnątrz, z jego pominiętymi krokami okiennymi; minimalizacja zawsze bez aktywacji.
- **R2 (dotknięcie):** „aktywacja aplikacji przywraca i wynosi jej okno” zawęzić do aktywacji z kafelka (przełączenie z zewnątrz nie przywraca i nie wynosi).
- **Constraints, AD-4:** dopisać okresową poprawkę reguły obok ponawiania przy nowej sesji.
- **Done when 3:** z „trafia do Orchestratora jako `ForegroundChanged`” na „Alt+Tab na shell / aplikację zmienia tryb i regułę dźwięku, na okno obce nie”.
- **Done when 4:** dodać „po ręcznej zmianie w mikserze reguła wraca w ok. 1 s”.
- **Notes:** oznaczyć Decision 2026-10-26 „polityka `ForegroundChanged` (R3)…” jako zastąpioną przez AD-15 2026-10-27; zaktualizować Decision 2026-10-27 „do jej wyniku 9 i 10 mają `unknown`”.
- **Plan odbioru (`new-pass`):** S8 bez zmian; S9 odwrócony (`inputRouting:>=1`, `audioRuleApplied,muted=false` dla Netflixa, wyciszenie poprzedniej); nowe: Alt+Tab na shell z aplikacji, mikser odcisza tło (cofnięte w ~1 s), mikser wycisza aktywną (wg M2), zamknięcie aplikacji w tle → uruchomienie poza MediaStation ma dźwięk (H1), Alt+Tab z Moonlighta (M1b).
- **Ledger:** DW-37 → „superseded by AD-5 (2026-10-27)”; DW-44 i DW-45 → owner Sofia/story 9 i 10 po decyzji architektury; DW-41 do przeglądu.

## Lens 2: reality check (dokumentacja Microsoft Learn, 2026-10-27)

| Twierdzenie | Wynik | Źródło |
| --- | --- | --- |
| `ISimpleAudioVolume::SetMute` przyjmuje GUID kontekstu zdarzenia | **Potwierdzone.** `SetMute(BOOL bMute, LPCGUID EventContext)`; menedżer sesji przekazuje tę wartość w każdym powiadomieniu do **wszystkich** klientów z zarejestrowanym `IAudioSessionEvents` (także do wywołującego); unikalność zależy od klienta. Zdarzenie powstaje tylko, gdy stan faktycznie się zmienia (ustawienie tego samego stanu nie daje echa) | learn.microsoft.com/…/nf-audioclient-isimpleaudiovolume-setmute |
| `GetMute` udostępnia kontekst | **Nie.** `GetMute(BOOL *pbMute)`, bez kontekstu. Spine tego nie twierdzi; skutek: poll nie wie, kto zmienił stan, więc każdą różnicę wykrytą pollem traktuje jako zewnętrzną (zgodne z regułą, ale zob. H1 dla dziedziczenia) | …/nf-audioclient-isimpleaudiovolume-getmute |
| `IAudioSessionEvents::OnSimpleVolumeChanged` dostarcza `EventContext`, więc własne zmiany da się odfiltrować | **Potwierdzone.** `OnSimpleVolumeChanged(float NewVolume, BOOL NewMute, LPCGUID EventContext)`, „the same value that the caller passed to … SetMute”. Rejestracja per sesja (`IAudioSessionControl::RegisterAudioSessionNotification`). Callbacki muszą być nieblokujące, bez wyrejestrowania i bez zwalniania ostatniej referencji w callbacku (L3) | …/nf-audiopolicy-iaudiosessionevents-onsimplevolumechanged, …/nn-audiopolicy-iaudiosessionevents |
| Fallback `IChannelAudioVolume` ma ten sam mechanizm | Interfejs zawiera `OnChannelVolumeChanged` z kontekstem (lista metod `IAudioSessionEvents`); sygnatury `SetAllVolumes` nie pobierałem: niezweryfikowane bezpośrednio, niskie ryzyko | lista metod `IAudioSessionEvents` |
| (nie twierdzone w spine, istotne) Trwałość wyciszenia | **Potwierdzone, sprzeczne z założeniem „sesje poza regułą nas nie dotyczą”.** Ustawienia głośności i wyciszenia sesji renderującej są domyślnie trwałe między restartami systemu; nowa instancja aplikacji z tym samym GUID-em sesji dziedziczy zapisane ustawienia ostatniej zakończonej sesji → H1. Dokumentacja odradza też sterowanie sesjami obcych aplikacji; tu to celowe i ograniczone do drzew z regułą | learn.microsoft.com/…/coreaudio/audio-sessions |
| Kolejność `EVENT_SYSTEM_FOREGROUND` | **Rozsądna.** `WINEVENT_OUTOFCONTEXT`: asynchroniczne, kolejność zdarzeń gwarantowana, dostarczane przez pętlę komunikatów wątku, który ustawił hook. Brak gwarancji kolejności względem powrotu z `SetForegroundWindow` ani `CommandCompleted` z wątku WindowManagera: echo może przyjść **po** wyniku komendy. Dlatego „Echo to no-op” jest konieczne (sama blokada w trakcie komend by nie wystarczyła); spine to ma. Zdarzenie przychodzi też przy zmianie w obrębie tego samego wątku | …/winauto/event-constants, …/nf-winuser-setwineventhook |
| `SetForegroundWindow` się udaje po odblokowaniu | **Nie zawsze.** Zwraca 0, gdy okno nie przeszło na wierzch; „proces może dostać odmowę mimo spełnienia warunków”; zamiast tego miga przycisk na pasku. Spine obsługuje to `CommandFailed`, ale polityka różnicy po porażce cofa Home (H2c) | …/nf-winuser-setforegroundwindow |
| Minimalizacja nie zmienia foreground | **Tylko dla `SW_SHOWMINNOACTIVE`.** `SW_MINIMIZE` „aktywuje następne okno najwyższego poziomu w Z-order” → M1a | …/nf-winuser-showwindow |
| `EVENT_SYSTEM_SWITCHSTART/SWITCHEND` | Istnieją (wciśnięcie / puszczenie Alt+Tab, `hwnd` = okno docelowe); opcjonalny sygnał zamiast czekania na timer (L2) | …/winauto/event-constants |
| Moonlight `--mute-on-focus-loss` nie walczy z wymuszonym odciszeniem | **Niezweryfikowane.** Jeśli Moonlight wycisza przez sesję (`ISimpleAudioVolume`), poll odciszy go pod obcym oknem; jeśli wewnętrznie (pomija próbki), konfliktu nie ma. Do sprawdzenia w story 10 (jeden odczyt `GetMute` sesji Moonlighta po utracie fokusu) | — |

## Dowody

- Machine: `powershell … $env:COMPUTERNAME; Win32_BaseBoard.Product; Win32_ComputerSystem.Model` → `MEDIASTATION`, `F7BSI`, `EliteMini Series` (UM760).
- Diff spine'u: `git diff -- architecture-mediastation.md` (AD-5, AD-8, AD-14, AD-15).
- Kod: `src/MediaStation.App/Platform/Audio/CoreAudioSessionBackend.cs:291, 330` (kontekst `null`), `AudioRouterEngine.cs` (księga, `Dispose` odcisza), `AudioSweepSchedule.cs:38` (poll tylko przy wyciszonym drzewie), `Platform/Window/Win32WindowApi.cs:27–32` (`SW_SHOWMINNOACTIVE`), `WinEventForegroundHook.cs:47, 70` (`WINEVENT_OUTOFCONTEXT`, odrzucenie `hwnd` NULL), `Core/Orchestrator/Orchestrator.Foreground.cs` (dziś tylko log).
