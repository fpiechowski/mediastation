# Triage ledgera deferred-work, 2026-10-26

Zrobione na UM760 (board F7BSI, EliteMini Series), ręcznie z `grep`/`wc` względem kodu. To nie jest wynik `bmad-loop sweep`, tylko propozycja zmian; ledger nie został zmieniony.

Stan wejściowy: 32 wpisy, 25 `open`, 7 `resolved`, zero `gate:`.

## 1. Zamknąć (kod potwierdza)

| DW | Zmiana | Dowód |
|----|--------|-------|
| DW-18 | `status: done 2026-10-26`, `resolution:` | `Orchestrator.cs:315,353` zapisuje `app.RootPid = attached.RootPid`; `Orchestrator.cs:105` oddaje go do `ManagedWindow` |

## 2. Znormalizować status (`resolved` → `done <data>` + `resolution:`)

DW-2, DW-5, DW-7, DW-8, DW-9, DW-12, DW-16. Treść `evidence:` zostaje. Daty: DW-7, DW-9, DW-12 mają 2026-10-22 w evidence, DW-16 ma 2026-10-23 (scalone w retro); dla DW-2, DW-5, DW-8 data z commitu domykającego story.
DW-16 zamknąć jako duplikat DW-15 (`resolution: duplikat DW-15`); wpis już to mówi, brakuje tylko kanonicznego statusu.

## 3. Uzupełnić pola

- Brak `severity:` w DW-1, DW-2, DW-11, DW-13, DW-14, DW-16, DW-31. Propozycja: DW-1 low, DW-11 medium, DW-13 low, DW-14 low, DW-31 medium; DW-2 i DW-16 są zamykane.
- DW-15: `medium (unverified)` → `medium`, jeśli S12 na UM760 potwierdzi; do tego czasu zostawić.

## 4. Zostają otwarte, nadal prawdziwe w kodzie

| DW | Co sprawdzone |
|----|---------------|
| DW-3 | `ProcessExited(string AppId)` nadal bez PID (`Messages.cs:78`) |
| DW-4 | `app.Hwnd = null` nadal w `Orchestrator.cs:517,638` |
| DW-6 | `_held`/`_pendingLong` bez resetu przy re-rejestracji hooka |
| DW-10 | `KeyInjector.SendInput` nadal bez wywołania w testach |
| DW-11 | brak deklaracji DPI w csproj/manifeście |
| DW-17 | `Orchestrator.cs` nie ma bramki `textFocus` ani obsługi `Keyboard` |
| DW-19 | `OrchestratorTests.cs` ma 1909 linii (w ledgerze 1553), rośnie |
| DW-22 | `ClipCursorGate.Apply` nadal tylko po `epoch` |
| DW-24 | zawężone (czyści tylko przy VK pilota), ale `_heldUsages.Clear()` nadal globalny |
| DW-25 | `InputHub.cs` 935 linii (było 815), `InputPolicy.cs` 909 (było 678) |
| DW-1, 13, 14, 15, 29, 32 | kod bez zmian; rozstrzyga przebieg S10/S12 na UM760 |

DW-19 i DW-25 rosną, więc ich wyzwalacz (przed pierwszą epiką z nową gałęzią dispatchu lub rozszerzeniem Inputu) jest realny, nie hipotetyczny.

## 5. Nie do zweryfikowania w repo

DW-20, DW-21, DW-26 dotyczą narzędzia `bmad-loop` i procesu PO, w repo nie ma ich kodu. DW-23 to konwencja do dopisania przy następnym passie architektury. DW-27, DW-28, DW-30, DW-31 zależą od sprzętu, polityki Application Control albo decyzji architektonicznej.

## 6. Propozycja grupowania w story (na kolejną epikę)

- **S10/Edge**: DW-13, DW-14, DW-15, DW-29, DW-32 w jednej story `tools/check-home-s10.ps1`, po wynikach S12 na UM760.
- **Przed epic-osk**: DW-11 (pomiar DPI), DW-17 (bramka `textFocus`), DW-22 (clip), a przed nimi wydzielenie z `InputHub` (DW-25).
- **Orchestrator**: DW-3, DW-4, DW-19 przed pierwszą epiką dodającą gałąź dispatchu.
- **Input**: DW-6, DW-24, DW-27 przy następnym dotknięciu kalibracji i hooka.

## 7. Decyzje człowieka

- DW-4: czy `Failed` zachowuje `Hwnd` i minimalizuje spóźnione okno, czy zostawia to hostowi.
- DW-30: HID zamiast VK jako wyzwalacz Klawiatury (zmiana AD-8, Source conflict do `bmad-architecture`).
- DW-31: czy zmieniać uprawnienia hooka, gdy fizyczny pilot potwierdzi problem.
- DW-28: podpisać `MediaStation.SchemaExport` albo dodać wyjątek Application Control.
- DW-20/21/26: zgłosić do narzędzia `bmad-loop` albo zapisać jako notatki procesu poza ledgerem.
