---
type: epic
title: "Orchestrator & app lifecycle"
parent: initiative-mediastation-mvp
covers: [CAP-2]
after: []
assignee: ""
status: done
risk: medium
---

# Orchestrator & app lifecycle

## Description

CAP-2: kilka aplikacji działa naraz i przełącza się bez zamykania, każda ma jawny stan cyklu życia. Epika dostarcza Orchestrator jako jedynego writera stanu sesji (jedna kolejka, Commands fire-and-forget, `transitionId`, `TimerElapsed`), adopcję sierot, ścieżkę wybudzenia i `focusRequest` w snapshotach oraz produkcyjny adapter SystemEvents (power, procesy), żeby wybudzenie z Modern Standby docierało do Core na aktualnej maszynie.

## Outcome

Kolejne epiki podpinają hosty i wykonawców do jednego właściciela decyzji, sprawdzonego testami na fake'ach; sygnałem są testy Orchestratora z success CAP-2.

## Done when

1. Testy Orchestratora na fake'ach portów z `FakeTimeProvider` pokrywają pięć scenariuszy z success CAP-2: przejścia z `Failed`, Home w trakcie `Starting`, adopcję przeglądarki i Moonlighta po restarcie, wybudzenie z `focusRequest{default}`, awarię aplikacji na pierwszym planie z fokusem na jej kafelku (AD-10, AD-14).
2. Na aktualnej maszynie Orchestrator działa w composition root, shell dostaje snapshoty (AD-16), a check wybudzenia z Modern Standby przez SystemEvents jest best-effort: jeśli sprzęt nie jest dostępny, wystarczają testy Core i logi (AD-10). Całościowy test UM760 całego epiku wykonuje użytkownik przy `epic-boundary`.

## Boundaries

Core: Orchestrator, stan sesji, polityka wyboru przy Home; App: SystemEvents. Nie: hosty aplikacji (epic-browserhost, epic-moonlight-native), WindowManager i AudioRouter (epic-windows-audio), UI (epic-home-screen). Weryfikacja J4 na aktualnej maszynie (best-effort) jest w epic-browserhost.

## References

- parent — _bmad-output/initiative-mediastation-mvp/spec-mediastation/spec-mediastation.md, Capabilities CAP-2
- architecture — _bmad-output/initiative-mediastation-mvp/architecture-mediastation/architecture-mediastation.md, AD-3, AD-4, AD-10, AD-14, AD-16
- ux — _bmad-output/initiative-mediastation-mvp/ux-mediastation/EXPERIENCE.md, State Patterns (Kafelek — cykl życia)

## Notes

- Decision: weryfikacja J4 na UM760 (OK na `Failed`, OK na „Uruchom ponownie”) przechodzi do epic-browserhost, bo wymaga AppMenu i prawdziwego hosta (2026-10-18).
- Decision: produkcyjny adapter SystemEvents należy do tej epiki; `RenewSession` po stronie BrowserHost należy do epic-browserhost (2026-10-18).
- Waits on epic-foundation-contracts because: rekordy Commands/Events/`ErrorCode` i projekt testów.
- Deferred: `ShellBridge.Publish` nie przenosi wywołania na wątek UI (`CoreWebView2` ma thread affinity), a `rev` snapshotu podaje wywołujący (dziś stałe `Rev: 1` w composition root). Źródło: F12 (PLAUSIBLE) w retro epic-foundation-contracts (`_bmad-output/initiative-mediastation-mvp/tickets/epic-foundation-contracts/epic-foundation-contracts-retrospective.md`). Warunek: pierwsza story, która publikuje snapshot z Orchestratora do shella; jeśli ta ścieżka trafi do epic-home-screen, wpis idzie z nią (2026-10-20).
- Deferred: `startingTimeout` w `settings.json` nie ma górnej granicy, a bardzo duża wartość rzuci przy tworzeniu timera. Źródło: F18 w retro epic-foundation-contracts (`_bmad-output/initiative-mediastation-mvp/tickets/epic-foundation-contracts/epic-foundation-contracts-retrospective.md`); retro wskazało epic-deploy-resilience, tu, bo Orchestrator jest pierwszym konsumentem. Warunek: story, która pierwsza tworzy timeout `Starting` z `startingTimeout` (2026-10-20); po podziale story 5 górne granice dostarcza story 11 (2026-10-21).
- Deferred: `CompositionRoot` (jeden wpis logu na nieobsłużony wyjątek UI, `DispatcherLogged`) nie ma testu ani pełnego manualnego pokrycia. Źródło: R4/N3 w retro epic-foundation-contracts (`_bmad-output/initiative-mediastation-mvp/tickets/epic-foundation-contracts/epic-foundation-contracts-retrospective.md`). Warunek: pierwsza story, która podłącza Orchestrator do composition root (2026-10-21).
- Deferred: `BannedSymbols.txt` nie zakazuje `TimeProvider.System`, `Stopwatch`, `Environment.TickCount`. Źródło: R7/N8 w retro epic-foundation-contracts (`_bmad-output/initiative-mediastation-mvp/tickets/epic-foundation-contracts/epic-foundation-contracts-retrospective.md`). Warunek: pierwsza story z czasem w Core (2026-10-21).
- Deferred: `ShellSnapshotFactoryTests` buduje `new ConfigResult(null, [])` — stan sprzeczny z dokiem `ConfigResult`. Źródło: R7/N8 w retro epic-foundation-contracts (`_bmad-output/initiative-mediastation-mvp/tickets/epic-foundation-contracts/epic-foundation-contracts-retrospective.md`). Warunek: następny refactor testów w tej epice (2026-10-21).
- Decision: tracer bullet to story 1 — Orchestrator na własnym wątku publikuje snapshot (`rev`, `apps[]` ze stanem, `focusRequest`) do shella na UM760; po nim story 2 (SystemEvents, Modern Standby), bo to najmniej pewny element epiki (2026-10-21).
- Decision: jeden lane — stories 2–8, 10 i 11 zmieniają kod Orchestratora, więc `after` tworzy łańcuch i nie budujemy ich równolegle (2026-10-21).
- Decision: Orchestrator w Core to czysty handler (komunikat → Commands i snapshot); wątek z jedną kolejką żyje w App (2026-10-21).
- Decision: bez hostów composition root dostaje adapter „brak hosta” per `AppType`: na `ProbeExisting` nic, na `StartApp` `CommandFailed{Unexpected}`; epiki hostów podmieniają adapter (2026-10-21).
- Decision: Done when 2 weryfikuje log (`snapshotPublished {rev, focusRequest}`); shell zostaje na placeholderze, w `web/shell` zmienia się tylko guard `isSnapshot` (2026-10-21).
- Decision: ta epika wprowadza port WindowManager tylko z `MinimizeWindow` i `ShowShell` (fake w testach); sekwencję pause → mute → minimize, unmute i adapter dokłada epic-windows-audio (2026-10-21).
- Decision: `ForegroundChanged` w MVP Orchestrator zapisuje i loguje bez reakcji (Home zawsze ratuje); reakcję ustala epic-windows-audio (2026-10-21).
- Decision: Orchestrator wysyła `SetInputRouting` przy każdej zmianie trybu `shell`/`app` już w tej epice; epic-input go konsumuje (2026-10-21).
- Decision: buforowanie faktów `playback` do `StoreLoaded` (AD-12, CAP-11) przechodzi do epic-do-oceny; tutaj zostaje `TimerElapsed` (2026-10-21).
- Decision: SystemEvents w tej epice obsługuje tylko power (wybudzenie); zakończenie procesu raportują hosty jako `ProcessExited` (AD-4) (2026-10-21).
- Decision: pomiar sygnału wybudzenia z Modern Standby jest częścią story 2 (z `plan_checkpoint`), nie osobnym spike'iem (2026-10-21).
- Decision: port WindowManager z tej epiki ma też `ActivateWindow{hwnd}` (adapter tylko-log); sekwencję restore → odblokowanie foreground → unmute implementuje epic-windows-audio (2026-10-21).
- Decision: Orchestrator w tej epice obsługuje z Actions tylko Home; przekazanie Actions do Shell w trybie `shell` dokłada epic-home-screen, a `action`/`back` do strony w `app` + `web` epic-netflix-dpad — każda swoją gałęzią w Orchestratorze (2026-10-21).
- Decision: story ścieżki szczęśliwej podzielona na 3 (porty i uruchomienie) i 10 (Home, przełączanie, zamykanie), bo po check'u setu przekraczała jedną sesję (2026-10-21).
- Decision: timeout bezpieczeństwa `Stopping` → `Failed` z nowego `settings.stoppingTimeout` (domyślnie 45 s) planuje Orchestrator (po podziale story 5: story 11); limity `prepareClose` i `quit` Moonlighta zostają w hostach (2026-10-21).
- Decision: breakdown zaakceptowany (10 entries; po przeglądzie pod pętlę 11, story 5 podzielona na 5 i 11); epika będzie budowana w pełni automatycznie w pętli (`bmad-build-auto`) (2026-10-21).
- Decision (przegląd pod pętlę, 2026-10-21): pierwszy snapshot powstaje z Eventu `OrchestratorStarted`, który pętla wkłada do kolejki po starcie (story 1); story 7 dokłada do jego obsługi `ProbeExisting{appId}` per aplikacja. Przy błędzie configu Orchestrator nie startuje, a shell dostaje snapshot `ConfigError` jak dziś.
- Decision (przegląd pod pętlę, 2026-10-21): tryb wejścia — `SetInputRouting{shell}` przy starcie; `{app}` dopiero przy Foreground po `AppWindowAttached`, z `pointer` wg tabeli AD-8; w Starting tryb zostaje `shell`. Każda zmiana trybu dostaje nowy `epoch`.
- Decision (przegląd pod pętlę, 2026-10-21): `focusRequest` dostaje nowe `seq` tylko przy `OrchestratorStarted`, `SystemResumed` i awarii aplikacji w Foreground (`<appId>`). Home go nie zmienia (fokus na ostatnim kafelku, EXPERIENCE → Interaction Primitives); `Failed` ze Starting albo Background nie wysyła `focusRequest` ani `ShowShell`; adopcja nie zmienia `seq`.
- Decision (przegląd pod pętlę, 2026-10-21): port to interfejs w Core przyjmujący Command; pętla w App kieruje Command do adaptera po `AppType` (hosty) albo do jedynego adaptera (WindowManager, `SetInputRouting`), a wynik wraca do kolejki jako Event. Scheduler timerów jest w Core z testem; App go tylko hostuje.
- Decision (przegląd pod pętlę, 2026-10-21): właściciele rekordów — `OrchestratorStarted` story 1, `SystemResumed` story 2, `LaunchApp`/`AppWindowAttached`/`StartApp` i komendy WindowManager story 3, `CloseApp`/`RestartApp`/`StopApp`/`ProcessExited`/`ForegroundChanged` story 10, `CdpLost` story 4, `ScheduleTimer`/`CancelTimer`/`TimerElapsed` story 5, `ProbeExisting` story 7, `RenewSession` story 8.
- Decision (przegląd pod pętlę, 2026-10-21): wybór sygnału wybudzenia w story 2 — wygrywa pierwszy z `PBT_APMRESUMEAUTOMATIC` albo `GUID_CONSOLE_DISPLAY_STATE=on`, kolejne w ciągu 5 s są pomijane; autor może to zmienić przy `plan_checkpoint` story 2 na podstawie pomiaru.
- Decision (przegląd pod pętlę, 2026-10-21): story 5 podzielona na 5 (scheduler, timeout Starting, ambient time) i 11 (timeout Stopping, `stoppingTimeout`, granice); górna granica 600 s dla `startingTimeout` i `stoppingTimeout`, bez reguły „`stoppingTimeout` > 30 s”.
- Decision (przegląd pod pętlę, 2026-10-21): wynik każdego checku HITL trafia do notatki odbioru epiku z datą, HEAD i nazwą maszyny; UM760 per ticket nie jest wymagany, a całościowy test UM760 wykonuje użytkownik przy `epic-boundary` (retro epiki 1, P2/P7).
- Decision (przegląd pod pętlę, 2026-10-21): adopcja w story 7 wysyła po `MinimizeWindow` także `ShowShell`, bo Z-order shella zmienia tylko WindowManager (AD-15).
- Decision: bez closing end-to-end suite — spec nie ma test planu dla CAP-2, a scenariusze pokrywają testy stories (2026-10-21).
- Decision (bmad-loop, 2026-10-21): epika 2 jest puszczona w pełni automatycznie do `epic-boundary`; 2.1, 2.2 i 2.9 nie są `hitl` i nie mają `plan_checkpoint`/`done_checkpoint`. Checki sprzętowe są best-effort na aktualnej maszynie; UM760 per ticket nie jest wymagany, a całościowy test UM760 wykonuje użytkownik przy `epic-boundary`; wyniki idą do notatki odbioru lub bugów.
- Decision (2026-10-21): UM760 nie jest wymagany per ticket. Agent testuje każdy ticket automatycznie na aktualnej maszynie (ALDER, B760 Pro RS/D4, Windows 11 Home), akceptuje najmocniejszy dostępny dowód i nie parkuje ticketu dla checków sprzętowych. Po dostarczeniu epiku użytkownik wykonuje całościowy test UM760 całego epiku przy `epic-boundary` i tam akceptuje pracę; agent nie zamyka epiku.
- Decision (2026-10-22): epika zamknięta po odbiorze użytkownika na UM760 (build `3517448`, Modern Standby S0 Low Power Idle): P0, S1, S2, S3, S4, S5, S7 pass, S6 obserwacja; sygnałem wybudzenia na UM760 jest `GUID_CONSOLE_DISPLAY_STATE`, reguła z Notes bez zmian. Dowód: `epic-orchestrator-lifecycle-um760-acceptance.md`. Werdykt retro: accepted-with-open-items.
- Decision (2026-10-22): F6 (dwie aplikacje w `Foreground` po `LaunchApp`) jest osobnym bugiem w `backlog/`, wymaganym przed `epic-home-screen`.
- Deferred (retro epiki 2, 2026-10-22): A2 (full lenses dla `risk` medium|high w szablonie planu pętli), A3 (Orchestrator ma zachować `rootPid` z `AppWindowAttached` dla `epic-windows-audio`, AD-15), A4 (podział `OrchestratorTests` i switcha przed `epic-home-screen`/`epic-input`), A5 (wiring i kolejność zamykania `CompositionRoot` tylko smoke), A6 (zapis HEAD zamknięcia ticketu, uzgodnienie Notes z `deferred-work.md`), A7 (rekoncyliacja `built` po timeoucie sesji w pętli). Źródło: `epic-orchestrator-lifecycle-retrospective.md`.
