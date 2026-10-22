---
title: 'Launch przy innej aplikacji na pierwszym planie daje dwa Foreground'
type: 'bugfix'
ticket: '3'
created: '2026-10-22'
status: 'built'
baseline_revision: 'e8990040f5786ba95070c7dc3b1c338d4ff8aae3'
route: 'oneshot'
route_source: 'auto'
risk: 'medium'
review: 'quick'
review_source: 'pinned'
lenses_ran: [quick]
review_loop_iteration: 0
context: []
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** `OnLaunchApp` dla aplikacji `NotRunning` nie sprawdza, czy inna aplikacja jest `Foreground`/`Starting`, więc po `AppWindowAttached` snapshot ma dwie aplikacje w `Foreground`.

**Approach:** Launch jest odrzucany jak resume (domyślna decyzja z Notes ticketu). Guard `AnotherAppIsActive` stoi we wspólnym wejściu `OnLaunchApp` dla obu gałęzi (`NotRunning` i `Background`); restart po stopie, który kończy się `Starting`, też go respektuje.

</frozen-after-approval>

## Implementation Notes

Oneshot: zmiana ~20 linii w jednym pliku plus testy. Guard przeniesiony z `OnResume` do `OnLaunchApp`; `OnStopped` (restart) przy aktywnej innej aplikacji kończy w `NotRunning` jak zwykłe zamknięcie.

Guard `AnotherAppIsActive` stoi teraz we wspólnym wejściu `OnLaunchApp` (launch i resume) i w `OnStopped` (restart, jedyna inna droga do `Starting`); `OnStop`/`OnAdopt` to osobne decyzje (zamknięcie, adopcja), nie wejścia na pierwszy plan.

Testy: usunięto `Home_from_foreground_makes_parallel_starting_app_lose_its_window` (setup nieosiągalny po poprawce; kompensację Home→późny attach pokrywa `Home_while_app_is_starting_...`); w teście SystemResumed `plex-web` zostaje `NotRunning` z tego samego powodu.

## Verification evidence (ALDER, Windows 11)

- `dotnet test` z poprawką: 526/526 zielone.
- Bez poprawki w `src` (stash): 3 nowe testy padają, reszta zielona.
- UM760: nie dotyczy per ticket (AGENTS.md).

## Review Triage Log

- AC2 częściowo (osobny guard w `OnStopped`): medium, świadomie — restart to druga droga do `Starting`; opisane wyżej.
- Brak evidence w planie: real, naprawione.
- Utrata pokrycia w SystemResumed/Home: low — setup nieosiągalny; Home-while-Starting pokryty istniejącym testem.
- Słaby test launch: real, wzmocniony o asercję na każdym snapshocie (≤1 Foreground).
- Ciche odrzucenie launchu / restart bez sygnału: zgodne z decyzją ticketu; false.
- Testy `OnStopped` przez CommandCompleted/timeout: low, ta sama ścieżka kodu.
