---
epic: epic-orchestrator-lifecycle
type: acceptance-plan
machine: UM760
base_head: 3517448
created: 2026-10-22
status: executed
---

# Odbiór epiki 2 na UM760 — plan i scenariusz

Całościowy test UM760 przy `epic-boundary` (AGENTS.md; Decision 2026-10-21 w epice). Zakres ustala to, co na ALDER było tylko syntetyczne albo niemożliwe: **jedyna rzecz sprzętowa w tej epice to wybudzenie z Modern Standby** (ALDER: `powercfg /a` bez S0ix). Reszta epiki (Home, Close, Restart, `Failed`, adopcja, timery, `transitionId`) nie ma jeszcze wejścia (InputHub, AppMenu) ani prawdziwych hostów, więc nie da się jej wykonać na żadnej maszynie; jej dowodem są testy (524/524 na `bf9bb86`).

## Źródła

- Done when 2 epiki: Orchestrator w composition root, shell dostaje snapshoty, wybudzenie przez SystemEvents (`epic-orchestrator-lifecycle.md`).
- Story 2.2 (Notes, reguła sygnału wybudzenia, okno 5 s) i `deferred` w jej planie: echo rejestracji `GUID_CONSOLE_DISPLAY_STATE` nieobserwowane na UM760.
- `deferred-work.md` DW-1: shim Win32 (`PowerNotifications`) bez testu automatycznego — rozstrzyga obserwacja na UM760.
- Retro: `epic-orchestrator-lifecycle-retrospective.md`, sekcja „Behavior verification” (jak wygląda dobry log).

## Scenariusze

| # | Scenariusz | Oczekiwane (log `%LOCALAPPDATA%\MediaStation\logs\mediastation-<data>.jsonl`) | Rodzaj |
|---|------------|-----------------------------------------------------------------|--------|
| P0 | Przygotowanie: `powercfg /a` pokazuje „Standby (S0 Low Power Idle)”; build z `master` (HEAD ≥ `3517448`); poprawny config w `%LOCALAPPDATA%\MediaStation\config` (próbki: `tools/samples/config`) | S0 Low Power Idle dostępny | warunek |
| S1 | Start aplikacji | `start`, `inputRouting {mode shell, epoch 1}`, `powerSignalsRegistered {displayState, windowHook, powerModeChanged: true}`, `snapshotPublished {rev 1, focusRequest default, seq 1}`; 0 `warn`/`error`; okno shella widoczne | pass/fail |
| S2 | Echo rejestracji (DW-1): czy OS wysyła `GUID_CONSOLE_DISPLAY_STATE` zaraz po starcie | jeśli jest `powerSignal` w pierwszych sekundach, **nie** ma po nim `systemResumed` | pass/fail + obserwacja |
| S3 | Sygnał syntetyczny na UM760: `powershell -File tools/inject-power.ps1` | `OK: 3 powerSignal, 2 systemResumed, 3 snapshotPublished (seq 1..3)`, exit 0 | pass/fail |
| S4 | **Prawdziwe wybudzenie z Modern Standby**: uśpić (Start → Uśpij), odczekać ≥ 60 s, wybudzić | które źródła się odpaliły (`PBT_APMRESUMEAUTOMATIC`, `displayState`, `PowerModeChanged`) i w jakiej kolejności; **dokładnie jeden** `systemResumed` w oknie 5 s; `snapshotPublished` z `seq` + 1 i `focusRequest default`; proces żyje; opóźnienie od wybudzenia do `systemResumed` | pass/fail + pomiar |
| S5 | S4 powtórzone 3× (w tym wybudzenie innym urządzeniem: pilot/klawiatura/przycisk zasilania) | każdorazowo dokładnie jeden `systemResumed` i rosnący `seq` | pass/fail |
| S6 | Wygaszenie i włączenie ekranu bez uśpienia (np. TV off/on) | brak pass/fail — zapisujemy, czy daje `systemResumed` | obserwacja |
| S7 | Zamknięcie aplikacji po testach | `stop {exitCode 0}`, 0 `warn`/`error` w całym przebiegu | pass/fail |

## Kryterium odbioru

Epika jest zaakceptowana, gdy S1, S2, S3, S5 (z S4) i S7 przechodzą. S4 jest sercem: jeśli wygrywa inny sygnał niż zakłada reguła z Notes epiki, to nie jest porażka — autor (użytkownik) może zmienić regułę na podstawie pomiaru (Notes story 2.2) i zapisujemy to jako Decision. S6 nie blokuje.

## Poza zakresem tego odbioru

Home/Close/Restart, `Failed`, adopcja, timeouty `Starting`/`Stopping`, `RenewSession` — brak wejścia i hostów; J4 przeszło do `epic-browserhost`.

## Wyniki

Wypełniamy w trakcie (data, HEAD, maszyna UM760):

UM760, 2026-10-22, build `3517448` (`1.0.0+3517448802c2`), `powercfg /a`: Standby (S0 Low Power Idle) Network Connected. Wykonał użytkownik; logi z `%LOCALAPPDATA%\MediaStation\logs\mediastation-2026-10-22.jsonl`.

| # | Wynik | Dowód |
|---|-------|-------|
| P0 | pass | S0 Low Power Idle dostępny; pierwszy build (`da900b7`, stary kod) odrzucony, przebudowa z `3517448` |
| S1 | pass | `start`, `inputRouting {shell, epoch 1}`, `powerSignalsRegistered` (3× true), `snapshotPublished {rev 1, seq 1}`; okno shella = placeholder z tytułem |
| S2 | pass (brak echa) | w dwóch startach (pid 6984, 2080) brak `powerSignal` po rejestracji; samo echo nadal nieobserwowane (DW-1) |
| S3 | pass | `inject-power.ps1`: 3 powerSignal, 2 systemResumed, seq 1..3, exit 0 (po `Set-ExecutionPolicy -Scope CurrentUser RemoteSigned`) |
| S4 | pass | uśpienie ≥60 s, wybudzenie 11:07:59: **tylko** `GUID_CONSOLE_DISPLAY_STATE` (brak `PBT_APMRESUMEAUTOMATIC` i `PowerModeChanged`), 1× `systemResumed`, `snapshotPublished` seq 2; aplikacja żyje |
| S5 | pass | wybudzenia 11:12:08, 11:14:15, 11:16:12: po 1× `systemResumed`, seq 3, 4, 5; 0 duplikatów |
| S6 | obserwacja | ekran wyłączony przyciskiem power ekranu: brak jakiegokolwiek sygnału, brak fałszywego `systemResumed` |
| S7 | pass | zamknięcie okna: `stop {exitCode 0}`; w całym przebiegu 0 `warn`/`error` |

**Pomiar:** na UM760 sygnałem wybudzenia z Modern Standby jest `GUID_CONSOLE_DISPLAY_STATE`; `PBT_APMRESUMEAUTOMATIC` nie przychodzi. Reguła „pierwszy z dwóch sygnałów” z Notes epiki działa, zmiana reguły niepotrzebna.

**Werdykt użytkownika:** do wpisania przy zamknięciu epiki.
