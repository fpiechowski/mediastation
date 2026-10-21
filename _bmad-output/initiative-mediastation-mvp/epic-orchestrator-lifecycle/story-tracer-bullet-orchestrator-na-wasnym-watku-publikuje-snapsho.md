---
id: 1
type: story
title: "Tracer bullet: Orchestrator na własnym wątku publikuje snapshot do shella"
parent: epic-orchestrator-lifecycle
covers: [CAP-2]
after: [1.2, 1.3, 1.6]
hitl: true
risk: medium
---

# Tracer bullet: Orchestrator na własnym wątku publikuje snapshot do shella

## Description

Dodaje w Core Orchestrator jako czysty handler (komunikat → Commands i snapshot) z monotonicznym `rev` i Eventem `OrchestratorStarted`, z którego powstaje pierwszy snapshot z `focusRequest{default}`, a w kontraktach enum stanów cyklu życia (NotRunning, Starting, Foreground, Background, Stopping, Failed), `apps[]` w snapshocie (`appId`, `category`, stan) i `focusRequest { target: default|<appId>, seq }`, które używają kolejne stories; w App pętlę Orchestratora na dedykowanym wątku z jedną kolejką (przy błędzie configu Orchestrator nie startuje, a shell dostaje snapshot `ConfigError` jak dziś), publikację snapshotu latest-wins przeniesioną na wątek UI (F12) z wpisem logu `snapshotPublished {rev, focusRequest}`, podpięcie w composition root z pokryciem jednego wpisu logu na nieobsłużony wyjątek UI (`DispatcherLogged`, R4/N3) helperem wydzielonym do Core z testem, aktualizację guardu `isSnapshot` w `web/shell/src/bridge.ts` i `ShellSnapshotFactoryTests` na poprawnym `ConfigResult` (N8).

## Acceptance Criteria

Verify: `dotnet test` przechodzi z testami monotonicznego `rev`, `focusRequest{default}` w snapshocie z `OrchestratorStarted` i helpera `DispatcherLogged`, a Release self-contained uruchomiony przez autora na UM760 pokazuje placeholder shella i zostawia w logu `snapshotPublished` z `focusRequest {target: default, seq: 1}`.

## References

- parent — _bmad-output/initiative-mediastation-mvp/epic-orchestrator-lifecycle/epic-orchestrator-lifecycle.md
- architecture-mediastation/architecture-mediastation.md, AD-2
- architecture-mediastation/architecture-mediastation.md, AD-14
- architecture-mediastation/architecture-mediastation.md, AD-16
- epic-foundation-contracts/epic-foundation-contracts-retrospective.md, Findings F12, N3, N8

## Notes

- HITL (autor, UM760): na maszynie dev `dotnet publish` projektu `MediaStation.App` w Release, self-contained win-x64; skopiować katalog publish na UM760; uruchomić `MediaStation.exe`; sprawdzić, że shell pokazuje placeholder; w `%LOCALAPPDATA%\MediaStation\logs\mediastation-<dzień UTC>.jsonl` ostatnie linie zawierają `snapshotPublished` z `focusRequest {target: default, seq: 1}`.
- HITL: wynik z datą, HEAD i nazwą maszyny trafia do `Verification` planu; maszyna dev nie spełnia checku bez decyzji autora (Notes epiki, retro epiki 1 P2/P7).
- Obsługę `OrchestratorStarted` z tej story rozszerzają story 3 (`SetInputRouting{shell}`) i story 7 (`ProbeExisting{appId}`); ta story daje tylko pierwszy snapshot.
- Monotoniczność `rev` dowodzi test jednostkowy, nie log; stałe `Rev: 1` z composition root znika w tej story.
