---
id: 22
type: story
title: "Strona, której dokument podmienił się w przerwie CDP, odzyskuje most albo kafelek przechodzi w Failed"
parent: none
covers: []
after: [20]
assignee: ""
refined: false
hitl: false
risk: high
---

# Strona, której dokument podmienił się w przerwie CDP, odzyskuje most albo kafelek przechodzi w Failed

## Description

Dokument załadowany w trakcie przerwy CDP nie ma PageRuntime, a host raportuje taką stronę jako zdrową. Ta story buduje decyzję z AD-10 (DW-56): po reconnekcie host wykrywa inny dokument głównej ramki i robi jeden cichy reload, a gdy most nie wróci, aplikacja przechodzi w `Failed`, skąd ratuje ją J4. Zdrowa strona po reconnekcie nie jest przeładowywana. Po tej story pilot nie zostaje przy stronie, która tylko wygląda na działającą.

## Requirements

1. **Ten sam dokument nie jest ruszany** (AD-10)
   **Given** przerwa CDP, w której dokument głównej ramki się nie zmienił, także gdy strona zmieniła URL przez `history.pushState`
   **When** reconnect się udaje
   **Then** host nie wysyła `Page.reload`, aplikacja zostaje w swoim stanie, a odtwarzanie nie jest przerwane

2. **Inny dokument dostaje jeden reload** (AD-1, AD-10)
   **Given** przerwa CDP, w której główna ramka załadowała nowy dokument
   **When** reconnect się udaje i odczyt drzewa ramek pokazuje inny `loaderId`
   **Then** host wysyła dokładnie jeden `Page.reload`, bez UI, także w `Background`, a nowy dokument wysyła `hello` i most działa
   **And** `Runtime.enable` nie jest użyte, a `navigator.webdriver` zostaje `false`

3. **Brak mostu kończy się w J4** (AD-10)
   **Given** host wysłał reload
   **When** `hello` nie przychodzi w `cdpReconnectTimeout` albo `Page.reload` zwraca błąd
   **Then** log ma wpis z przyczyną, host zgłasza jeden `CdpLost` dokładnie po terminie, kafelek jest `Failed`, a J4 przywraca aplikację

4. **Inne zakończenia nie dają fałszywej awarii** (AD-10)
   **Given** host czeka na `hello` po reloadzie
   **When** przychodzi Stop, proces wychodzi albo połączenie znów się zrywa
   **Then** czekanie kończy się bez `CdpLost`, a o kolejnym zerwaniu rozstrzyga następny reconnect
   **And** błąd odczytu drzewa ramek jest nieudaną próbą reconnectu

5. **Adopcja nie przeładowuje** (AD-10)
   **Given** MediaStation adoptuje żywą przeglądarkę albo odnawia sesję po raz pierwszy bez znanego dokumentu
   **When** sesja jest gotowa
   **Then** host tylko zapisuje dokument jako odniesienie, bez porównania i bez reloadu

6. **Zachowanie Netflixa po reloadzie jest zmierzone** (AD-5, AD-10)
   **Given** Netflix na `/watch`, zalogowany profil, bieżąca maszyna
   **When** aplikacja jest w `Background` z pauzą i dostaje cichy reload
   **Then** plan zapisuje wynik: czy odcinek wznawia się bez OK, czy pozycja zostaje oraz jak często Netflix nawiguje przy wybudzeniu

## Boundaries

- Must not change: Orchestrator (`CdpLost` → `Failed` już działa), zamknięta lista metod CDP z AD-1, zakaz `Runtime.enable`.
- Must not change: reconnect bez nowego dokumentu, sekwencja instalacji bindingu i skryptu; lista metod na reconnekcie rośnie tylko o odczyt drzewa ramek i warunkowy reload.
- Poza zakresem: dokument podmieniony, gdy MediaStation nie działa (Deferred w architekturze), transport host → page, DW-55.

## References

- architecture — _bmad-output/initiative-mediastation-mvp/architecture-mediastation/architecture-mediastation.md, AD-1 (`Page.reload` tylko po dowodzie), AD-5, AD-10 (Dokument podmieniony w trakcie przerwy CDP)
- ledger — _bmad-output/initiative-mediastation-mvp/deferred-work.md, DW-56
- probe — _bmad-output/initiative-mediastation-mvp/architecture-mediastation/probes/dw56-frame-tree-probe.mjs, punkt wyjścia żywego smoke'a
- source — _bmad-output/initiative-mediastation-mvp/tickets/epic-browserhost/epic-browserhost-retrospective.md, F7, A4
- plan — _bmad-output/initiative-mediastation-mvp/tickets/epic-browserhost/tickets/story-binding-po-nawigacji-i-reconnect-cdp-plan.md (reguła „exactly” dla metod na reconnekcie jest nieaktualna; zmiana testów, które ją przypinają, jest tu zamierzona)

## Notes

- Decision: użytkownik wybrał cichy reload (wątek Winstona, 2026-10-30).
- Decision: możliwe wznowienie odtwarzania bez OK po reloadzie to wyjątek od AD-5, zaakceptowany pod warunkiem pomiaru. Jeśli pomiar z kryterium 6 pokaże wznowienie bez OK, PO pokazuje wynik użytkownikowi, zanim oznaczy story jako `done`; wynik pomiaru nie zmienia zachowania w tej story (użytkownik, 2026-10-30).
- Decision: story jest warunkiem wejścia w epic-netflix-dpad, obok story 20, i idzie po niej, bo dotyka tego samego kodu (użytkownik, 2026-10-30).
- Decision: story leży w backlogu, poza epikiem 6; odbiór tej epiki nie oczekuje tego zachowania (2026-10-30).
- Ryzyko wysokie: reload przerywa odtwarzanie, jeśli wykrycie się pomyli. Dodatkowe sprawdzenie poza kryteriami: żywy smoke na Edge, w którym `pushState` i udany reconnect nie dają `Page.reload`, oraz mutacja każdej gałęzi z kryteriów 1–4, która oblewa własny test.
- Weryfikacja: maszynę ustala się przed pracą (reguła „Current machine” w `AGENTS.md`). Bez zalogowanego Netflixa lub ekranu kryterium 6 oznacza się jako nie dotyczy z powodem, a PO zbiera pomiar z użytkownikiem przed `done`.
