---
id: 12
type: bug
title: "Moonlight uruchomiony przez MediaStation zawiesza się na ustanawianiu połączenia"
parent: epic-windows-audio
covers: [R2, R8]
after: [8]
assignee: ""
refined: true
hitl: false
risk: high
severity: P1
---

# Moonlight uruchomiony przez MediaStation zawiesza się na ustanawianiu połączenia

## Description

Moonlight uruchomiony ręcznie poza MediaStation łączy się z hostem poprawnie. Ten sam Moonlight uruchomiony przez MediaStation nie kończy połączenia i zatrzymuje się na komunikacie „Ustanawianie połączenia z komputerem…”. To osobny punkt startowy względem ticketu 8, który opisuje powrót po Home i ponowne wejście na kafelek.

## Reproduction

1. Na UM760 uruchomić Moonlighta ręcznie z tym samym hostem i aplikacją co w konfiguracji MediaStation — połączenie działa.
2. Zamknąć Moonlighta.
3. Uruchomić MediaStation z MEDIASTATION_DEV_HARNESS=1 i MEDIASTATION_HARNESS_MOONLIGHT.
4. Uruchomić kafelek moonlight-pc.
5. Actual: Moonlight wisi na „Ustanawianie połączenia z komputerem…”. Expected: handshake kończy się, a strumień jest widoczny.

## Acceptance Criteria

1. Start przez MediaStation kończy handshake i pokazuje działający strumień na osiągalnym hoście Sunshine.
2. Diagnostyka zapisuje pełną komendę procesu, argumenty, katalog roboczy, środowisko, PID, klasę/uchwyt okna i etap, na którym start się zatrzymał.
3. Test porównuje uruchomienie ręczne z uruchomieniem przez MediaStation i wykrywa różnicę w parametrach lub środowisku; test nie przechodzi przed poprawką.
4. Poprawka nie psuje minimalizacji/przywracania okna ani scenariusza powrotu po Home z ticketu 8.
5. Jeśli przyczyna leży w Moonlight/Sunshine poza kodem epiki, ticket kończy się Source conflict z logami obu sposobów uruchomienia i decyzją architektoniczną.

## References

- parent — _bmad-output/initiative-mediastation-mvp/tickets/epic-windows-audio/epic-windows-audio.md, R2, R8, Done when 2
- related — _bmad-output/initiative-mediastation-mvp/tickets/epic-windows-audio/tickets/bug-moonlight-po-home-i-powrocie-stoi-na-uruchamianie-input-stre.md, ticket 8
- acceptance — _bmad-output/initiative-mediastation-mvp/tickets/epic-windows-audio/epic-windows-audio-um760-acceptance.md, S6/U1
- ledger — _bmad-output/initiative-mediastation-mvp/deferred-work.md, DW-43, DW-47, DW-48

## Notes

- New evidence from user, 2026-10-27: manual Moonlight works; MediaStation-launched Moonlight hangs during initial connection.
- First capture the exact process launch contract before changing window selection or foreground behavior.
- High risk: blocks the next UM760 acceptance of S6 and dependent S18–S21 checks.
