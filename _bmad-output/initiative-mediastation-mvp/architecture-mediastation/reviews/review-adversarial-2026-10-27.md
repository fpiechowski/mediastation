---
title: 'Adversarial review: poprawki 2026-10-27 (AD-5, AD-8, AD-14, AD-15; U2/DW-44, U3/DW-45)'
target: ../architecture-mediastation.md
scope: 'AD-5 („Przełączenie z zewnątrz”, „Reguła audio to stan docelowy…”), AD-8 („`mode` idzie za ustalonym oknem na wierzchu”), AD-14 (timer ustalenia foreground), AD-15 („Polityka `ForegroundChanged`”). Kontekst: AD-2, AD-3, AD-4, bramka AD-8, AD-9, AD-10, AD-14.'
context: [src/MediaStation.Core/Orchestrator/Orchestrator.Foreground.cs, Orchestrator.Home.cs, Orchestrator.Failures.cs, Orchestrator.ManagedWindows.cs, ForegroundOwnerResolver.cs, src/MediaStation.App/Platform/Audio/AudioRouterEngine.cs, AudioRule.cs, Win32AudioRouterPort.cs, AudioSweepSchedule.cs, CoreAudioSessionBackend.cs, src/MediaStation.App/Platform/Window/WinEventForegroundHook.cs, Win32WindowApi.cs]
date: '2026-10-27'
---

# Adversarial review: poprawki 2026-10-27

## Werdykt

**Nie puszczać pętli bez poprawek.** 0 critical, 3 high, 5 medium, 4 low.

Poprawki dobrze ustalają *co* ma się stać po Alt+Tab (ta sama sekwencja co Home/kafelek, bez kroków okiennych) i *kto* trzyma stan audio (reguła, nie mikser). Dziury są w *kiedy*: obserwacja odłożona przez „Kiedy nie oceniać” nigdy nie wraca do oceny, a ochrona przed echem opiera się na „komenda jeszcze bez `CommandCompleted`”, choć echo WinEvent przychodzi po `CommandCompleted`. Do tego tryb `osk` nie ma reguły przy przełączeniu z zewnątrz, a fallback `IChannelAudioVolume` nie ma „flagi wyciszenia”, którą reguła audio ma wyrównywać. Wszystkie poprawki to dopiski do AD-5, AD-8, AD-9 i AD-15; kontrakty `v` się nie zmieniają, poza polem `pid` w `ForegroundChanged` (M4).

Uwaga: kod w `src/` jest jeszcze sprzed poprawek (`OnForegroundChanged` tylko loguje, `AudioRouterEngine` ma księgę „odciszam tylko swoje”, `AudioSweepSchedule` skanuje okresowo tylko przy `anyMuted`). Recenzja ocenia regułę na tle tego, jak kod faktycznie działa (wątki, kolejność zdarzeń, API), a nie zgodność kodu z regułą.

## Podsumowanie

| # | Tier | Para jednostek / scenariusz | Dziura | Poprawka (skrót) |
| --- | --- | --- | --- | --- |
| H1 | high | Orchestrator (polityka `ForegroundChanged`) × AppHost (`Starting`, adopcja) × WindowManager (hook) | Obserwacja odłożona przez „Kiedy nie oceniać” nie ma wyzwalacza ponownej oceny; brak obserwacji początkowej i ponownej oceny po `AppWindowAttached` | Ponowna ocena ostatniej obserwacji przy końcu blokady (ostatni wynik komendy okiennej, wyjście ze `Starting`, zmiana zbioru okien) i obserwacja początkowa po starcie hooka |
| H2 | high | Orchestrator (przełączenie z zewnątrz) × OSK / TextSink (AD-9) | Alt+Tab w trybie `osk` — reguła mówi tylko `shell`/`app`; OSK zostaje topmost z `target` starej aplikacji | Przełączenie z zewnątrz w trybie `osk` najpierw robi `HideOsk` (+ `osk{active:false}`) jak Home |
| H3 | high | WindowManager (WinEvent `OUTOFCONTEXT`) × Orchestrator (guard „komenda bez wyniku”) | Echo własnej komendy przychodzi **po** `CommandCompleted`, a przy `CommandFailed` pośrednie okno (np. Background Y odsłonięte po minimalizacji) staje się „przełączeniem z zewnątrz” — Home kończy się aktywacją Y | Timer ustalenia startuje także od wyniku ostatniej komendy okiennej; obserwacja różna od zamiaru po `CommandFailed` własnej komendy w tym samym `transitionId` to nieudane przejście, nie decyzja |
| M1 | medium | AudioRouter: fallback `IChannelAudioVolume` × „flaga wyciszenia = reguła” × „poziom należy do użytkownika” × „poprzednia instancja” | Sesja w fallbacku nie ma flagi; jej „wyciszenie” to poziomy kanałów (należące do użytkownika); po crashu poziomów do przywrócenia nie ma | Zdefiniować stan fallbacku i wartość przy odciszeniu bez zapamiętanych poziomów (albo usunąć fallback po pomiarze) |
| M2 | medium | AudioRouter (`ForgetProcessTree`, `PruneDead`) × „sesje spoza drzew nie są dotykane” | Bez księgi sesja wyciszona regułą zostaje wyciszona na zawsze, gdy korzeń zginie, a potomek żyje; możliwa trwałość wyciszenia per aplikacja w Windows | Forget/prune = jednorazowe odciszenie sesji, które były w drzewie; sprawdzić trwałość mute per aplikacja |
| M3 | medium | AudioRouter (skan ~1 s) × aplikacja wyciszająca własną sesję (in-app mute, ew. Moonlight `--mute-on-focus-loss`) | Walka co sekundę i wpis `audioSessionOverridden` co sekundę | Jawnie: mute sesji aplikacji `Foreground` należy do reguły (zaakceptowany koszt); limit wpisu na sesję; pomiar mechanizmu Moonlighta |
| M4 | medium | WindowManager (`ForegroundChanged{hwnd}`) × Orchestrator (rozpoznanie po `rootPid`, AD-14) | Fakt nie niesie PID; Orchestrator musi zawołać Win32 synchronicznie przez port na swoim wątku, i to z opóźnieniem (okno już zamknięte → `foreign`) | `ForegroundChanged{ hwnd, pid }`, PID odczytany w callbacku hooka |
| M5 | medium | Orchestrator (właściciel `Stopping`/`Failed` „tylko logowany”) × bramka InputHub (zbiór zarządzany) | Nie wiadomo, czy okno w `Stopping`/`Failed` jest „zarządzane”; jeśli tak, pilot nad nim wysyła Actions do niewidocznego Shell | Zbiór `SetManagedWindows` = tylko `Starting`/`Foreground`/`Background`; `Stopping`/`Failed` są dla bramki obce |
| L1 | low | AD-4 × AD-5 (korekta reguły bez Commandu) | AD-4 mówi „nie wycisza z własnej inicjatywy”, wyjątek żyje tylko w AD-5; AD-5 cytuje AD-2 dla wątku AudioRoutera, którego AD-2 nie wymienia | Dopisek w AD-4 i liście wątków AD-2 |
| L2 | low | WindowManager (`minimize`) × przełączenie z zewnątrz X → Y | AD-1/AD-5 mówią `SW_MINIMIZE`/„minimize”; `SW_MINIMIZE` aktywuje następne okno w Z-order i może odwrócić decyzję użytkownika; kod używa `SW_SHOWMINNOACTIVE`, spine tego nie wiąże | AD-5: `minimize` = `SW_SHOWMINNOACTIVE` |
| L3 | low | AD-8 „między Alt+Tab a ustaleniem obowiązuje poprzedni `mode`” × bramka | Przez ~300 ms D-pad/OK nad shellem lub innym zarządzanym oknem trafia do strony poprzedniej aplikacji (OK → `video.play()` w tle) | Zaakceptować jawnie albo odrzucać `action` do strony, gdy właściciel foreground ≠ aktywna aplikacja |
| L4 | low | AudioRouter (skan „dopóki istnieje jakakolwiek reguła”) | Reguła `false` istnieje dla każdej uruchomionej aplikacji, więc skan 1 Hz (wszystkie urządzenia + migawka Toolhelp) działa stale | Zaakceptować koszt jawnie; `AudioSweepSchedule` przejść z `anyMuted` na `anyRule` |

## Weryfikacja twierdzeń o API

| Twierdzenie (AD-5) | Ocena |
| --- | --- |
| `ISimpleAudioVolume::SetMute(BOOL bMute, LPCGUID EventContext)` przyjmuje GUID kontekstu | **Poprawne.** Kod woła dziś `SetMute(mute, null)` (`CoreAudioSessionBackend.cs:291`), więc GUID trzeba dopiero przekazać. |
| `IAudioSessionEvents::OnSimpleVolumeChanged(float NewVolume, BOOL NewMute, LPCGUID EventContext)` niesie ten GUID | **Poprawne.** Mikser (SndVol) i inne aplikacje podają własny GUID albo `NULL` (`GUID_NULL`), więc filtr „mój GUID” działa. |
| Odczyt stanu wyciszenia | **Poprawne:** `ISimpleAudioVolume::GetMute(BOOL*)`. |
| Fallback `IChannelAudioVolume` | `SetAllVolumes`/`SetChannelVolume` też mają `LPCGUID EventContext`, ale zmiana kanałów przychodzi przez **`IAudioSessionEvents::OnChannelVolumeChanged`**, nie `OnSimpleVolumeChanged`. Ten interfejs nie ma flagi mute (patrz M1). „Powiadomienia o zmianie głośności sesji” w AD-5 muszą objąć oba callbacki, jeśli fallback zostaje. |
| Powiadomienia o zmianie głośności sesji | Wymagają `IAudioSessionControl::RegisterAudioSessionNotification` **per sesja** (kod rejestruje dziś tylko `IAudioSessionNotification` i urządzenia). Callbacki przychodzą na wątkach COM, nie na wątku AudioRoutera — mogą tylko budzić skan (jak `RequestSweep`); `UnregisterAudioSessionNotification` nie wolno wołać z wnętrza callbacku. Reguła („mogą tylko skracać”) jest z tym zgodna, ale warto to zapisać. |
| `SetWinEventHook(EVENT_SYSTEM_FOREGROUND, WINEVENT_OUTOFCONTEXT)` | Poprawne co do dostarczania przez pętlę komunikatów wątku rejestrującego (wątek WindowManagera). Konsekwencja dla kolejności echa — H3. |

## Findings

### H1 (high): odłożona obserwacja nigdy nie jest oceniana

**Reguła.** AD-15 „Kiedy nie oceniać”: przy komendzie okiennej bez wyniku albo aplikacji w `Starting` „obserwacja jest tylko zapisana, a ocenia ją następne ustalenie”. „Ustalenie” startuje tylko `ForegroundChanged` („każde `ForegroundChanged` … (re)startuje timer”). Nowe przejście unieważnia oczekującą ocenę.

**Scenariusz A (Starting).** Netflix `Starting` (przeglądarka + CDP: kilka–kilkanaście sekund). Użytkownik Alt+Tab z shella na Moonlight (`Background`). Timer się kończy, ale jest `Starting` → zapis. Netflix dochodzi do `Failed` (timeout, `CdpUnavailable`) albo użytkownik wciska Home w trakcie `Starting` (AD-14: aplikacja zostaje `Starting`, okno później dostaje kompensującą minimalizację). Foreground się już nie zmienia, więc nie ma `ForegroundChanged` i oceny. Stan: Moonlight na wierzchu, `mode = shell`, Moonlight w zbiorze zarządzanym → bramka połyka D-pad i wysyła go do niewidocznego Shell. To dokładnie objaw U2, który poprawka miała usunąć. Dodatkowo blokada `Starting` jest globalna: start jednej aplikacji zamraża ocenę dla każdej innej na cały timeout `Starting`.

**Scenariusz B (adopcja / restart po crashu).** Supervisor restartuje MediaStation, gdy Netflix był na wierzchu. Hook WinEvent raportuje tylko *zmiany*, więc okna obecnego przy starcie nikt nie zgłasza. Jeśli `ShowShell` po starcie zawiedzie (nowy proces z supervisora nie ma prawa foreground — stąd AD-5 „odblokowanie”), Netflix zostaje na wierzchu, adoptowany jako `Background`, `mode = shell`, zero obserwacji. Wariant: `ForegroundChanged` przychodzi przed `AppWindowAttached` → okno jest jeszcze `foreign` → nic; `AppWindowAttached` nie uruchamia ponownej oceny.

**Poprawka (AD-15).** „Ostatnia obserwacja jest oceniana ponownie (z nowym timerem ustalenia), gdy znika powód odłożenia: wynik ostatniej komendy okiennej bieżącego `transitionId`, wyjście aplikacji ze `Starting`, oraz gdy zmienia się zbiór okien zarządzanych (`AppWindowAttached`, zamknięcie, awaria). WindowManager po rejestracji hooka raportuje jednorazowo bieżące okno foreground jako `ForegroundChanged`.” Rozważyć zawężenie blokady `Starting` do obserwacji, których właścicielem jest aplikacja w `Starting` albo shell.

### H2 (high): przełączenie z zewnątrz w trybie `osk`

**Reguła.** AD-5/AD-15 opisują przełączenie tylko do `shell` i `app`. AD-9: OSK zamykają tylko Back, Home i `Keyboard`. AD-8: `clipHwnd` zwalniany przy każdej zmianie trybu. Echo: zamiar to `shell` albo `app` X wg stanu `Foreground`, tryb `osk` w nim nie występuje.

**Scenariusz.** Netflix `Foreground`, pole wyszukiwania, tryb `osk`, `ShowOsk{ target: web(netflix) }`. Alt+Tab na shell → „opuszczenie aplikacji `Foreground`, tryb `shell`”. Nic nie wysyła `HideOsk`: okno OSK (topmost, `WS_EX_NOACTIVATE`) wisi nad shellem, strona nie dostaje `osk{active:false}` (pole zostaje w stylu `input-active`), a OSK, gdyby dostało kliknięcie myszą (wskaźnik nigdy nie jest połykany), wpisze tekst przez CDP do zminimalizowanej, zapauzowanej aplikacji. Wariant z `target: foreground` (pole Shell albo Moonlight): po aktywacji innej aplikacji `SendInput` trafia do nowego okna na wierzchu — wyciek tekstu do niezamierzonej aplikacji (AD-9 „Prevents: wyciek wpisywanego tekstu”).

**Poprawka (AD-5/AD-9).** „Przełączenie z zewnątrz w trybie `osk` zaczyna od `HideOsk` (i `osk{active:false}` do strony), tak jak Home; dopiero potem opuszczenie/aktywacja. Echo w trybie `osk`: zamiarem jest właściciel `app` X, gdy OSK stoi nad X, a `shell`, gdy nad shellem.”

### H3 (high): echo przychodzi po `CommandCompleted`; nieudana własna komenda staje się decyzją

**Mechanizm.** `WinEventForegroundHook` jest `WINEVENT_OUTOFCONTEXT` i zarejestrowany na wątku WindowManagera, więc callback idzie przez pętlę komunikatów tego wątku. `WindowCommandExecutor` wykonuje `SetForegroundWindow`/`ShowWindow` i publikuje `CommandCompleted` w tym samym handlerze, zanim pętla wypompuje zdarzenie WinEvent. Kolejność w kolejce Orchestratora: `CommandCompleted`, potem `ForegroundChanged` (echo). Guard „komenda okienna bez wyniku” nie obejmuje więc echa ani zdarzeń pośrednich; cała ochrona spoczywa na regule „echo = zgodne z zamiarem”.

**Scenariusz (Home → aktywacja innej aplikacji).** X `Foreground`, Y `Background` z oknem niezminimalizowanym (adoptowane, albo użytkownik wcześniej je przywrócił). Home: `Mute X` → `Minimize X` → `ShowShell`. Po zminimalizowaniu aktywnego X Windows aktywuje następne okno w Z-order: Y → `ForegroundChanged(Y)`. `ShowShell` dostaje `CommandFailed` (blokada foreground; odblokowanie z AD-5 nie zawsze działa, stąd istnienie `CommandFailed`). Po 300 ms ostatnia obserwacja = Y `Background`, brak komend bez wyniku → „przełączenie z zewnątrz” → `Unmute Y`, tryb `app`. Wciśnięcie Home kończy się dźwiękiem i sterowaniem Y. To samo przy `ActivateWindow` z kafelka, które nie wygrało foreground.

**Scenariusz (wyścig timera z wynikiem).** Timer ustalenia (300 ms od ostatniego `ForegroundChanged`) może wybić, zanim wróci wynik wolnej komendy (Alt + ponowienie + `AttachThreadInput` w `WindowCommandExecutor`), a wtedy obserwacja jest odkładana — i trafia w H1.

**Poprawka (AD-15).** (1) „Timer ustalenia (re)startuje także wynik każdej komendy okiennej bieżącego `transitionId`, więc ocena następuje najwcześniej `foregroundSettleMs` po ostatnim własnym wyniku.” (2) „Gdy w bieżącym `transitionId` komenda okienna dostała `CommandFailed`, obserwacja różna od zamiaru nie jest przełączeniem z zewnątrz: Orchestrator loguje `foregroundTransitionFailed` i zostawia stan (Home ponawia), a przełączenie z zewnątrz uruchamia dopiero obserwacja z następnym `ForegroundChanged` po tym wyniku.” Alternatywnie w Home `ShowShell` przed `Minimize`, co usuwa okno pośrednie, ale nie przypadek porażki.

### M1 (medium): fallback `IChannelAudioVolume` nie pasuje do „flaga = reguła”

AD-5 w jednym punkcie każe utrzymywać „flagę wyciszenia każdej sesji” równą regule, w innym oddaje „poziom głośności sesji” użytkownikowi, a w trzecim zostawia `IChannelAudioVolume` jako fallback. Fallback nie ma flagi: wycisza, ustawiając kanały na 0, czyli właśnie poziomy. Silnik (`AudioRouterEngine.Mute`) pamięta poziomy w księdze, a poprawka księgę likwiduje („zastępuje odciszam tylko to, co sam wyciszyłem”). Skutki: (a) „każdy skan odczytuje rzeczywisty stan” nie ma definicji dla fallbacku (wszystkie kanały 0? mniej niż ε?); (b) „`UnmuteProcessTree` odcisza każdą sesję drzewa, także wyciszoną przez poprzednią instancję” jest niewykonalne: po crashu poziomy sprzed wyciszenia zginęły, więc sesja zostaje na 0 albo AudioRouter zgaduje poziom; (c) użytkownik, który w mikserze ściszy kanał do zera, zostanie „odciszony” do zgadniętego poziomu — wbrew „poziom należy do użytkownika”; (d) powiadomienie przychodzi przez `OnChannelVolumeChanged`.

**Poprawka.** Albo zmierzyć na UM760, czy jakakolwiek sesja w drzewach (Edge, Moonlight, native z `apps.json`) nie daje `ISimpleAudioVolume`, i usunąć fallback; albo zapisać: „sesja w fallbacku jest wyciszona ⇔ wszystkie kanały = 0; AudioRouter pamięta poziomy sprzed swojego wyciszenia (jedyny zachowany fragment księgi); odciszenie bez zapamiętanych poziomów ustawia 1.0 i loguje `audioChannelRestoreGuessed`”.

### M2 (medium): osierocone wyciszone sesje i trwałość mute

Bez księgi i z regułą „sesji spoza drzew z regułą AudioRouter nie dotyka” znika ścieżka, którą dziś robi `PruneDead` + księga: odciszenie sesji przeżywającego potomka, gdy korzeń umarł. Przykład: `native` uruchamiany przez launcher (korzeń kończy się, potomek gra) albo awaria procesu przeglądarki z żyjącym chwilę procesem usługi audio. Gdy reguła korzenia zniknie (`ForgetProcessTree`, `PruneDead`), sesja wyciszona zostaje wyciszona na zawsze, a nowy proces tej aplikacji ma nowy `rootPid`. Do sprawdzenia: Windows zapamiętuje ustawienia sesji per aplikacja (identyfikator sesji ze ścieżką exe) między uruchomieniami; jeśli dotyczy to także mute, aplikacja wyciszona w `Background` i zamknięta podczas awarii MediaStation uruchomi się później poza MediaStation wyciszona, a AudioRouter jej nie ruszy.

**Poprawka (AD-5).** „Usunięcie reguły korzenia (`ForgetProcessTree`, śmierć korzenia, zamknięcie MediaStation) odcisza jednorazowo sesje, które ostatni skan przypisał temu drzewu; dopiero potem są sesjami użytkownika.” Pomiar trwałości mute per aplikacja na UM760 dopisać do odbioru.

### M3 (medium): walka z aplikacją wyciszającą własną sesję

Reguła wygrywa „w obie strony”, a skan co ~1 s cofa każdą zmianę flagi spoza AudioRoutera. Aplikacja, która implementuje własny mute przez `ISimpleAudioVolume::SetMute` swojej sesji (przycisk mute w aplikacji `native`; Moonlight z `--mute-on-focus-loss`, jeśli robi to przez sesję, a nie przez bufor), będzie odciszana co sekundę, gdy jest `Foreground`, a przy utracie fokusu na obce okno (które „niczego nie zmienia”) Moonlight wyciszy się, AudioRouter go odciszy, i tak w kółko. Każda poprawka daje wpis `audioSessionOverridden` → wpis co sekundę.

**Poprawka.** Zapisać jawnie: „mute sesji w drzewie z regułą należy do reguły, także gdy zmienia go sama aplikacja (koszt zaakceptowany)” albo wyłączyć `Foreground` z wymuszania odciszenia (tylko wyciszenie jest wymuszane). Wpis `audioSessionOverridden` najwyżej raz na sesję na zmianę reguły. Zmierzyć mechanizm `--mute-on-focus-loss` na UM760.

### M4 (medium): `ForegroundChanged` bez PID — dwie jednostki zgodne z literą, niezgodne razem

AD-15 każe WindowManagerowi raportować `ForegroundChanged` (kod: tylko `hwnd` po `GA_ROOT`), a AD-8/AD-15 każą rozpoznawać właściciela „po `hwnd`, a gdy to nie pasuje, po `rootPid`”. AD-14 zabrania Orchestratorowi blokujących wywołań Win32 na swoim wątku, AD-3 każe trzymać Core bez Win32. Story WindowManagera zgodna z AD-15 wyśle sam `hwnd`; story Orchestratora zgodna z AD-14 nie ma jak dostać PID. Kod obchodzi to portem `IForegroundLookup.ProcessIdOf`, wołanym synchronicznie na wątku Orchestratora (`Orchestrator.Foreground.cs`), i to w chwili obsługi, nie zdarzenia: okno przejściowe zamknięte w międzyczasie daje PID 0 → `foreign`, a ponowne użycie `hwnd`/PID daje błędne przypisanie.

**Poprawka (AD-15).** „`ForegroundChanged{ hwnd, pid }`: PID (`GetWindowThreadProcessId`) odczytuje callback hooka w chwili zdarzenia. Orchestrator rozpoznaje właściciela wyłącznie z pól zdarzenia i własnego stanu.”

### M5 (medium): `Stopping`/`Failed` — „tylko logowany”, ale co z bramką?

AD-15: właściciel `app` w `NotRunning`/`Stopping`/`Failed` jest tylko logowany, więc `mode` zostaje (np. `shell`). AD-8: bramka połyka klawisze nad oknami „z `AppWindowAttached`”. Spine nie mówi, kiedy okno wypada ze zbioru. Kod usuwa `Hwnd` przy `Failed` (`EnterFailed`), więc okno `Failed` jest dla bramki obce — ale resolver nadal przypisuje je po `RootPid` do aplikacji. Dla `Stopping` (strona przechodzi na `/browse` po `prepareClose`, do 3 s) okno jest w zbiorze: Alt+Tab na nie → klawisze połykane i wysyłane do niewidocznego Shell. Inna implementacja InputHub (trzymająca okno do `NotRunning`) da martwy pilot nad oknem `Failed` z żywym procesem.

**Poprawka (AD-15).** „Zbiór `SetManagedWindows` obejmuje tylko aplikacje w `Starting`, `Foreground` i `Background`; okno w `Stopping` i `Failed` jest dla bramki obce.”

### L1 (low): wyjątek od AD-4 i lista wątków AD-2

AD-4 nadal brzmi „wykonawca … nie wycisza z własnej inicjatywy”; okresowa korekta bez Commandu jest uzasadniona tylko w AD-5. Dopisać w AD-4: „utrzymanie reguły zadanej ostatnim Commandem (AD-5) nie jest własną inicjatywą”. AD-5 powołuje się na „wątek AudioRoutera (AD-2, AD-14)”, a AD-2 wymienia tylko wątki UI, Input, Orchestrator i I/O; brak też wątku WindowManagera (pętla komunikatów dla `AttachThreadInput` i hooka WinEvent). Dopisać oba do AD-2.

### L2 (low): `minimize` nie jest związany z `SW_SHOWMINNOACTIVE`

AD-1 mówi o `ShowWindow(SW_MINIMIZE)`, AD-5 o „minimize”. `SW_MINIMIZE` aktywuje następne okno najwyższego poziomu w Z-order; w przełączeniu z zewnątrz X → Y (Y już aktywne) mogłoby to oddać foreground np. shellowi i odwrócić decyzję użytkownika (echo po `CommandCompleted`, H3). Kod używa `SW_SHOWMINNOACTIVE` (`Win32WindowApi.cs`), ale to wybór implementacji, nie reguła. Dopisać w AD-5: „`minimize` = `SW_SHOWMINNOACTIVE`”.

### L3 (low): okno ~300 ms ze starym `mode`

„Między Alt+Tab a ustaleniem obowiązuje poprzedni `mode`” + bramka „nasze lub zarządzane” = D-pad i OK nad shellem albo nad Moonlightem w trybie `app`+`web` idą jako `action` do strony Netflixa w tle (OK na zapauzowanym `/watch` → `video.play()`, a Netflix nie jest jeszcze wyciszony). Samo się naprawia po ustaleniu (pause → mute), ale jest słyszalne. Zaakceptować jawnie albo: Orchestrator nie przekazuje `action`/`back` do strony, gdy ostatnia obserwacja foreground ≠ aktywna aplikacja.

### L4 (low): skan 1 Hz staje się stały

„Okresowo co ~1 s, dopóki istnieje jakakolwiek reguła” — reguła `false` istnieje dla każdej uruchomionej aplikacji, więc przy dowolnej działającej aplikacji skan (enumeracja sesji wszystkich aktywnych urządzeń + `CreateToolhelp32Snapshot`) idzie bez końca. To raczej akceptowalne na UM760, ale warto zapisać koszt; `AudioSweepSchedule.NextDelay(…, anyMutedRoot)` i warunek w `Win32AudioRouterPort.Run` (`AnyMuted || HasMutedSessions`) trzeba przestawić na „jest reguła”.

## Sprawdzone i bez zastrzeżeń

- Zgodność z AD-14: timer ustalenia to `TimerElapsed` planowany przez Orchestrator; skan i korekta audio na wątku AudioRoutera, bez `await` w Core. Wyjątek: M4.
- Zgodność z AD-3/AD-4 dla przełączenia z zewnątrz: decyzję podejmuje Orchestrator z faktu `ForegroundChanged`, wykonawcy dostają zwykłe Commands.
- Pętla audio przez GUID kontekstu: zamknięta poprawnie dla `ISimpleAudioVolume` (API potwierdzone); dla fallbacku patrz M1.
- Okna chwilowe Alt+Tab, pulpit, UAC, `hwnd` 0: „ocenia się tylko ostatnią obserwację” + `foreign` niczego nie zmienia — bez migania, o ile H1/H3 są domknięte.
- Dwie aplikacje (X `Foreground` → Alt+Tab na Y `Background`): sekwencja opuszczenie X → aktywacja Y bez kroków okiennych jest jednoznaczna, przy `SW_SHOWMINNOACTIVE` (L2).
