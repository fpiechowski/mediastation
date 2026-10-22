---
title: 'Wstrzyknięcie klawisza ze znacznikiem'
type: 'feature'
ticket: '7'
created: '2026-10-22'
status: done
baseline_revision: '31a62e0c0f456a2772a0667ac18aceacf5ad617a'
route: 'full'
route_source: 'auto'
risk: 'medium'
review: 'quick'
review_source: 'pinned'
lenses_ran: ['quick']
review_loop_iteration: 1
followup_review_recommended: false
context: []
warnings: ['oversized']
deferred:
  - summary: >-
      Realne wywołanie SendInput w KeyInjector nigdy nie zostaje wykonane przez testy ani przez przebiegi aplikacji, więc przyjęcie dwóch rekordów INPUT przez system pozostaje niepotwierdzone automatem.
    evidence: |-
      KeyInjector jest tylko przekazywany jako delegat w CompositionRoot.cs, testy karmią KeyInjectionPump własnym delegatem, a logi z 2026-10-20..22 nie mają żadnego wpisu keyInjection. Układ struktur pochodzi z CsWin32 (wygenerowane INPUT/KEYBDINPUT), więc ryzyko jest niskie. Rozstrzyga odbiór epiku na UM760: prawdziwy SendKey daje wpis keyInjection, a wstrzyknięty klawisz nie daje Action i wraca przez hook; alternatywnie jednorazowe wywołanie helpera poza testami.
    location: >-
      src/MediaStation.App/Platform/Input/KeyInjector.cs:22-38
    severity: low
---

<intent-contract>

## Intent

**Problem:** MediaStation nie umie wysłać własnego klawisza: brak komendy `SendKey`, brak helpera `SendInput`, a hook nie czyta `dwExtraInfo`, więc polityka nie odróżni własnego wstrzyknięcia od naciśnięcia pilota. Bez tego `TextSink foreground` (epic-osk), `back: key:<VK>` (epic-moonlight-native) i odblokowanie foreground symulowanym Alt (epic-windows-audio) zapętliłyby się na własnych klawiszach.

**Approach:** Command `SendKey{ vk }` w Core (koperta, JSON Schema, TS), port `IKeyInjectionPort` z adapterem w `Platform/Input`, który wysyła key-down i key-up przez `SendInput` ze znacznikiem MediaStation w `dwExtraInfo`, oraz reguła w `InputPolicy`: zdarzenie z tym znacznikiem nie daje Action i nie jest połykane. `LLKHF_INJECTED` zostaje bez zmian, bo Home z pilota ją ma.

## Boundaries & Constraints

**Always:** jeden znacznik zdefiniowany w Core (`InjectionMarker`) i używany zarówno przez injector, jak i przez politykę; hook przekazuje `dwExtraInfo` do Core w `KeyStroke.ExtraInfo`; znacznik sprawdzany w polityce **przed** mapowaniem i stanem trzymania, wynikiem jest `Swallow=false` bez Actions (także dla key-up); `LLKHF_INJECTED` bez znacznika nadal daje korelację `source: remote` i Action; `SendKey` zarejestrowany w `MessageTypes.All` (koperta `sendKey`) i w routingu Command; adapter nie odpowiada `CommandCompleted` (wzór portów z 2.3); log bez surowych kodów klawiszy (AD-13); P/Invoke wyłącznie w `App/Platform` przez CsWin32 (AD-3).

**Never:** gałąź Orchestratora dla `back: key:<VK>`, `TextSink`/OSK i Alt-unlock (epic-osk, epic-windows-audio), pointer/`ClipCursor` (3.8), zmiany korelacji `source` i zachowania `InputCalibration`, ręczne `DllImport`, surowe VK w logach, `timeBeginPeriod`.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| zdarzenie ze znacznikiem, down i up | `ExtraInfo == InjectionMarker.MediaStation`, VK zmapowany na Home, `shell` | zero Actions, `Swallow=false` dla obu | brak błędu |
| zdarzenie ze znacznikiem przy trzymanym klawiszu | prawdziwy key-down OK, potem oznaczony key-up tego VK | trzymanie nietknięte; press dopiero na prawdziwym key-up | brak błędu |
| wstrzyknięty bez znacznika | `0xAC`, `Injected=true`, `ExtraInfo=0`, wildcard `shell` | jedna `{home, press, keyboard}` i połknięcie | brak błędu |
| `SendKey` w kopercie | `SendKey(11, 172)` | `{"v":1,"type":"sendKey","payload":{"cmdId":11,"vk":172}}`, round-trip | typ niezarejestrowany odrzucony |
| `SendKey` w routingu | komenda z Orchestratora | dokładnie jedno `IKeyInjectionPort.Execute` | port nieznany → `warn commandWithoutPort` |
| `SendInput` nic nie wstawił | `SendKey` z `vk` poza 1–255 albo błąd systemu | wpis `warn keyInjectionFailed {cmdId}` bez VK | brak wyjątku przez granicę portu |

</intent-contract>

## Code Map

- `src/MediaStation.Core/Input/InjectionMarker.cs` — nowy: `public static class InjectionMarker` z `public const long MediaStation = 0x4D53_0001L` („MS” + wersja) i `IsOurs(long)`. Jedno źródło znacznika dla injectora i polityki; Core nie robi P/Invoke.
- `src/MediaStation.Core/Input/InputPolicy.cs` — `KeyStroke` (22) dostaje `long ExtraInfo = 0` jako ostatni parametr (domyślny, więc istniejące konstrukcje w testach się nie zmieniają). `OnKey` (165-172): po `ThrowIfNull` i **przed** `_kinds` zwróć `PassDecision`, gdy `InjectionMarker.IsOurs(key.ExtraInfo)`; `ResolveSource` (623-640) i `ShouldSwallow` zostają nietknięte.
- `src/MediaStation.Core/Contracts/SendKey.cs` — nowy: `public sealed record SendKey(long CmdId, int Vk) : Command(CmdId);` z doc o key-down + key-up i znaczniku. `CmdId` pierwszy (wymóg `JsonPropertyOrder(-1)`).
- `src/MediaStation.Core/Contracts/MessageTypes.cs` — `All` (14-39): `typeof(SendKey)` po `typeof(SetInputRouting)`. To wystarcza do koperty, JSON Schema i TS.
- `src/MediaStation.Core/Ports/Ports.cs` — nowy `IKeyInjectionPort { void Execute(SendKey command); }` obok `IInputRoutingPort` (53-57); doc: fire-and-forget jak port z 2.3, brak wyjątku przez granicę portu.
- `src/MediaStation.App/Orchestrator/CommandRouter.cs` — nowy wymagany parametr ctor `IKeyInjectionPort keyInjection` (po `inputRouting`) i `case SendKey key: _keyInjection.Execute(key);` przed `default` (96). Doc klasy (9-15) wymienia nowy port.
- `src/MediaStation.App/CompositionRoot.cs` — utwórz `KeyInjectionPump` (z `KeyInjector.TrySendKey` i logiem) **przed** routerem, przekaż ją do routera jako `IKeyInjectionPort`, `Start()` przed `loop.Start()`, a w `Exit` zwolnij po `actionPump`, przed schedulerem i pętlą.
- `src/MediaStation.App/Platform/Input/KeyInjector.cs` — nowy: `internal static class KeyInjector`, `public static bool TrySendKey(int virtualKey)` budujący dwa `INPUT` (down, potem up z `KEYEVENTF_KEYUP`), `wVk = (VIRTUAL_KEY)vk`, `dwExtraInfo = (nuint)InjectionMarker.MediaStation`, jedno `PInvoke.SendInput`, `cbSize = sizeof(INPUT)`; `false`, gdy wstawiono mniej niż 2 zdarzenia. Helper jest miejscem, które epic-osk i epic-windows-audio rozszerzą (Unicode/Alt), zachowując ten sam znacznik. Sam nie waliduje zakresu — to robi pompa.
- `src/MediaStation.App/Platform/Input/KeyInjectionPump.cs` — nowy, **adapter portu**: `internal sealed class KeyInjectionPump : IKeyInjectionPort, IDisposable`, wzorem `ActionPump` (`:17-139`): `BlockingCollection<SendKey>` bez ograniczenia rozmiaru (nie blokuje producenta) + wątek tła `key-injection`; `Execute(SendKey)` tylko kolejkuje (`TryAdd`, po `Dispose` no-op), więc wątek pętli Orchestratora nigdy nie woła Win32 (AD-14); wątek sprawdza `Vk is >= 1 and <= 255` i loguje `info keyInjection {cmdId}` / `warn keyInjectionFailed {cmdId}` (bez VK), a wyjątek z `sendKey`, także `SendInput`, łapie jako `warn keyInjectionFailed` — port nie przepuszcza wyjątku (AD-4); `Dispose` robi `CompleteAdding`, czeka ograniczony czas i porzuca niewstrzyknięte komendy (wstrzyknięcie po zamknięciu nie ma sensu). Kolejność wstrzyknięć = kolejność komend.
- `src/MediaStation.App/Platform/System/NativeMethods.txt` — dopisz `SendInput`, `INPUT`, `INPUT_TYPE`, `KEYBDINPUT`, `KEYBD_EVENT_FLAGS`, `VIRTUAL_KEY`. Jeśli CsWin32 zgłosi brakujący typ zależny, dopisz dokładnie tę nazwę; nie pisz struktur ręcznie.
- `src/MediaStation.App/Platform/Input/InputHub.cs` — `HookCallback` (403-411): szóste pole `KeyStroke` to `unchecked((long)data.dwExtraInfo)`.
- `tests/MediaStation.Core.Tests/InputPolicyTests.cs` — `Down`/`Up` (100-104) dostają opcjonalny `long extraInfo = 0`; nowa sekcja znacznika, a teoria znacznika obejmuje **wszystkie pięć wierszy AD-8**: `shell`, `osk`, `app` + `web`, `app` + `moonlight`, `app` + `native`.
- `tests/MediaStation.Core.Tests/CoreMessageContractTests.cs` — koperta `sendKey` wzorem `:25-37`; lista nazw rejestru (`:461`) i lista plików schematów (`:480-509`) z `send-key.schema.json` (porządek `schedule-timer` < `send-key` < `set-input-routing`); test kształtu schematu wzorem `:530-549`.
- `tests/MediaStation.Core.Tests/CommandRouterTests.cs` — `CreateRouter` (`:234-259`) z `FakeKeyInjection`, nowy test obok `Input_routing_goes_to_input_port` (`:116`), wszystkie dekonstrukcje krotki zyskują element.
- `tests/MediaStation.Core.Tests/OrchestratorLoopTests.cs` — 7 miejsc `new CommandRouter(...)` (`:169, 226, 268, 322, 390, 460, 527`) dostaje `new FakeKeyInjection()`.
- `tests/MediaStation.Core.Tests/KeyInjectionPumpTests.cs` — nowy: `Execute` nie woła wstrzyknięcia na wątku wołającego (dowód AD-14), kolejność dwóch komend, `info`/`warn` z samym `cmdId`, `Vk` poza 1–255 daje `warn` bez wołania `sendKey`, wyjątek delegata daje `warn` i nie zabija wątku (kolejna komenda dochodzi), `Dispose` nie rzuca. Plik nie używa Win32 (delegat `Func<int,bool>`), więc jest linkowany.
- `tests/MediaStation.Core.Tests/MediaStation.Core.Tests.csproj` — dopisz `<Compile Include>` dla `KeyInjectionPump.cs` (bez Win32, wzorem `ActionPump.cs`/`InputHubRoutingPort.cs` w `26-42`); `KeyInjector.cs` zostaje poza testami (CsWin32).
- Reuse: `InputRoutingState`, `InputDecision.PassDecision` (74), `ContractJson`, `RecordingLog`, `FakeTimeProvider`. Nie ruszać: `InputCalibration` (poza zakresem), `MessageTypes.BuildIndex`, `web/**` (generowane z `MessageTypes.All`).

## Tasks & Acceptance

**Execution:**
- [ ] `src/MediaStation.Core/Contracts/SendKey.cs` + `MessageTypes.cs` — rekord i rejestracja — koperta `sendKey`, schemat i TS bez ręcznej listy.
- [ ] `src/MediaStation.Core/Input/InjectionMarker.cs` + `InputPolicy.cs` — znacznik i reguła ignorowania w `OnKey` — „polityka ignoruje każde zdarzenie ze znacznikiem”.
- [ ] `src/MediaStation.Core/Ports/Ports.cs` + `src/MediaStation.App/Orchestrator/CommandRouter.cs` — port i gałąź routingu — „rejestracja w routingu Command pętli jak porty z 2.3”.
- [ ] `src/MediaStation.App/Platform/System/NativeMethods.txt` + `KeyInjector.cs` — wspólny helper `SendInput` ze znacznikiem (jedno wywołanie, down + up).
- [ ] `src/MediaStation.App/Platform/Input/KeyInjectionPump.cs` + `InputHub.cs` + `CompositionRoot.cs` — adapter portu na własnym wątku `key-injection`, odczyt `dwExtraInfo` w hooku, podpięcie i zwolnienie w composition root — AD-14 (pętla Orchestratora nie woła Win32) i AD-4 (brak wyjątku przez port).
- [ ] `tests/MediaStation.Core.Tests/{InputPolicyTests,CoreMessageContractTests,CommandRouterTests,OrchestratorLoopTests,KeyInjectionPumpTests}.cs` — wiersze macierzy I/O (także `app` + `web`), koperta, schemat, routing, zachowanie pompy — `verify`.
- [ ] Uruchom `dotnet build`, `dotnet test`, eksport schematów, `web && npm run build`, publish + 10 s przebieg aplikacji i zapisz dowody w **Verification**.

**Acceptance Criteria:**
- Given zdarzenie hooka z `ExtraInfo` równym `InjectionMarker.MediaStation` i VK zmapowanym na Home, when `InputPolicy.OnKey` je dostaje, then wynik ma zero Actions i `Swallow == false`, niezależnie od key-down/key-up i w każdym z pięciu wierszy AD-8 (`shell`, `osk`, `app` + `web`/`moonlight`/`native`).
- Given zdarzenie `Injected=true` z VK `0xAC` i `ExtraInfo` 0 w trybie `shell`, when `OnKey`, then jedna Action `{kind: home, phase: press}` jest połykana — sama flaga `LLKHF_INJECTED` nie jest ignorowana.
- Given `Command command = new SendKey(11, 172)`, when `ContractJson.SerializeEnvelope`, then dokładnie `{"v":1,"type":"sendKey","payload":{"cmdId":11,"vk":172}}` i round-trip do równego `SendKey`; a `ContractJson.ExportSchemas()` zawiera `send-key.schema.json` z `title: SendKey`, `required: [cmdId, vk]` i `additionalProperties: false`.
- Given `SendKey` z Orchestratora, when `CommandRouter.Dispatch`, then `IKeyInjectionPort` dostaje tę komendę dokładnie raz, bez wpisów w innych portach, a wątek wołający `Dispatch` nie wykonuje `SendInput`: port tylko kolejkuje, a wstrzyknięcie i wpis `keyInjection` powstają na wątku `key-injection`.
- Given `SendKey` z `vk` poza 1–255 albo `sendKey` rzucający wyjątek, when pompa go opróżnia, then wpis `warn keyInjectionFailed {cmdId}` bez VK, brak wyjątku przez port, a kolejna komenda nadal dochodzi.
- Given zbudowany Release i `MediaStation.exe` z `tools/samples/config` (bez emitera `SendKey`), when proces działa 10 s, then kod wyjścia 0, a log nie zawiera `keyInjection`, `keyInjectionFailed`, `inputHubError` ani `warn`/`error`.

## Implementation Notes

- `InjectionMarker.MediaStation` (`0x4D53_0001L`) stoi w Core i jest jedynym źródłem znacznika: `KeyInjector` wpisuje go w `dwExtraInfo` obu `INPUT`, a `InputPolicy.OnKey` sprawdza go jako pierwszą rzecz po `ThrowIfNull`, przed `_kinds`, `_held` i timerem. Dzięki temu oznaczony key-down nie uzbraja `long`/`repeat`, a oznaczony key-up nie zamyka cudzego trzymania; `LLKHF_INJECTED` zostaje wyłącznie w korelacji `source` (`ResolveSource` bez zmian).
- `KeyStroke` dostał `long ExtraInfo = 0` jako ostatni parametr (domyślny), więc istniejące konstrukcje w testach, `InputCalibration` i `InputHub` nie wymagały zmian poza jednym nowym argumentem w `HookCallback` (`unchecked((long)data.dwExtraInfo)`); CsWin32 generuje `dwExtraInfo` jako `nuint` w `KBDLLHOOKSTRUCT`.
- `KeyInjector.TrySendKey` buduje `INPUT[2]` (down, potem kopia z `KEYEVENTF_KEYUP`), `wVk = (VIRTUAL_KEY)vk`, `dwExtraInfo = (nuint)InjectionMarker.MediaStation`, jedno `PInvoke.SendInput(inputs, sizeof(INPUT))` (`sizeof(INPUT)` = 40 B, wygenerowany overload liczy `cInputs` ze spanu, więc wstawiane są dokładnie dwa zdarzenia) i zwraca `true` tylko dla dwóch wstawionych zdarzeń; `vk` spoza 1–255 kończy się `false` przed P/Invoke. CsWin32 sam dobrał zależne typy (`INPUT_TYPE`, `KEYBDINPUT`, `KEYBD_EVENT_FLAGS`, `VIRTUAL_KEY`), więc lista w `NativeMethods.txt` to dokładnie sześć nazw z planu, bez ręcznych struktur.
- `KeyInjectionPort` loguje wyłącznie `{cmdId}` (bez VK, AD-13) i nie odpowiada `CommandCompleted`; w composition root powstaje przed `InputHub`, bo injector nie zależy od huba.
- **Odstępstwo od Code Map (dopisane w Plan Change Log):** `KeyInjectionPort` przyjmuje funkcję `Func<int, bool> sendKey` (composition root podaje `KeyInjector.TrySendKey`), a `KeyInjectionPort.cs` jest linkowany do testów. Dzięki temu wiersz macierzy „`SendInput` nic nie wstawił” ma test (`KeyInjectionPortTests`), a samo P/Invoke i struktury Win32 zostają poza testami — `KeyInjector.cs` nadal nie jest linkowany.
- `CommandRouter` dostał wymagany port po `inputRouting`, a `FakeKeyInjection` w dwóch plikach testów pilnuje, że `SendKey` nie trafia do innych portów; dekonstrukcje krotki `CreateRouter` i wszystkie siedem miejsc `new CommandRouter(...)` w `OrchestratorLoopTests` zaktualizowane.
- **Loopback 2026-10-22:** wpisy powyżej opisują kod z pierwszego podejścia, który został cofnięty do baseline po findingu `[medium]` o `SendInput` na wątku Orchestratora. Nadal aktualne są: znacznik w Core i reguła na początku `OnKey`, `KeyStroke.ExtraInfo`, `SendKey` + `MessageTypes.All`, kształt `INPUT[2]` i lista `NativeMethods.txt`, brak VK w logach, brak `CommandCompleted`. Adapterem portu jest teraz `KeyInjectionPump` (własny wątek, kolejka, walidacja `Vk` i łapanie wyjątku delegata), a nie `KeyInjectionPort` wołający `SendInput` synchronicznie.

## Plan Change Log

- `tests/MediaStation.Core.Tests/MediaStation.Core.Tests.csproj` i `src/MediaStation.App/Platform/Input/KeyInjectionPort.cs` — plan zakładał, że portu nie da się linkować do testów („używają CsWin32”). Sam port nie używa Win32, więc zamiast zostawiać wiersz macierzy „`SendInput` nic nie wstawił” bez testu, wysyłanie klawisza wchodzi do portu jako `Func<int, bool>` (`KeyInjector.TrySendKey` w composition root), a port jest linkowany i pokryty `KeyInjectionPortTests`. Zachowanie i logi portu bez zmian; `KeyInjector.cs` pozostaje poza testami.
- **2026-10-22 — loopback po przeglądzie (review_loop_iteration 1), wyzwolony findingiem `[medium]` „`SendInput` na wątku pętli Orchestratora łamie AD-14”.** Zrewidowany kod wrócił do baseline `31a62e0c0f456a2772a0667ac18aceacf5ad617a`; poniższe zastępuje wcześniejszy wpis o `KeyInjectionPort`.
  - **Co zmieniono:** adapterem `IKeyInjectionPort` jest teraz `KeyInjectionPump` (`Platform/Input`) — własny wątek `key-injection`, kolejka `BlockingCollection<SendKey>` i pętla jak w `ActionPump`; `Execute` tylko kolejkuje, więc wątek Orchestratora nie woła Win32. Pompa jest jedynym miejscem, które woła `KeyInjector.TrySendKey` i loguje `info keyInjection {cmdId}` / `warn keyInjectionFailed {cmdId}`; łapie wyjątek delegata (także przyszły Unicode/Alt), więc port nie przepuszcza wyjątku (AD-4). Walidacja `Vk` 1–255 przeniesiona z `KeyInjector` do pompy, bo pompa jest linkowana do testów — wiersz macierzy „`SendInput` nic nie wstawił / `vk` poza zakresem” ma wreszcie test na kodzie produkcyjnym. Teoria znacznika obejmuje wszystkie pięć wierszy AD-8 (dodany `app` + `web`). `CompositionRoot` tworzy i startuje pompę przed `loop.Start()` i zwalnia ją po `actionPump`.
  - **Stan znany jako zły, którego unikamy:** blokujące `SendInput` w `CommandRouter.Dispatch` (kolejka i Home czekają), wyjątek z wstrzyknięcia jako `error orchestratorFailed` z pominięciem reszty komend i snapshotu, nieprzetestowany warunek zakresu, brak wiersza `app` + `web` w dowodzie AC #1.
  - **KEEP (musi przetrwać ponowną derywację):** znacznik w Core (`InjectionMarker.MediaStation`) i reguła na początku `OnKey` przed `_kinds`/`_held`; `KeyStroke.ExtraInfo` jako ostatni parametr z domyślnym `0`; `SendKey(long CmdId, int Vk)` + `MessageTypes.All` (koperta, schemat, TS bez ręcznych list); kolejność parametrów `CommandRouter` (nowy port po `inputRouting`) i aktualizacja `FakeKeyInjection` we wszystkich miejscach; dwa `INPUT` (down + up z `KEYEVENTF_KEYUP`) ze znacznikiem w `dwExtraInfo` w jednym `SendInput`; log bez VK i bez `CommandCompleted`.

## Review Triage Log

### 2026-10-22 — Review pass
- verdicts: 5 findings — high 0, medium 1, low 3, false 1, maybe-false 0
- findings:
  - `[false]` `[reject]` AC #5 nie jest spełniony, bo pierwszy z trzech zapisanych przebiegów aplikacji miał `shell webViewProcessFailed` (error), więc kryterium spełniają tylko dwa powtórzenia — obalone: przebieg na tym samym drzewie powtórzony w triage (`dotnet publish -c Release -r win-x64 --self-contained true` + `MediaStation.exe` z `tools/samples/config`, 10 s) dał kod 0 i dokładnie pięć wpisów `start` → `inputRouting` → `powerSignalsRegistered` → `snapshotPublished` → `stop`, zero `warn`/`error`; flake to znany crash renderera WebView2 odnotowany w story 3.5, niezwiązany z wejściem (ten przebieg nie miał też `keyInjection`/`inputHubError`). Naprawa tego spostrzeżenia byłaby edycją tekstu tego planu, co triage odrzuca.
  - `[low]` `[patch]` Teoria „każdy wiersz AD-8” nie obejmuje wiersza `app` + `web`, choć AC #1 mówi „niezależnie od bieżącego wiersza AD-8”, a `InputPolicy.ShouldSwallow` (526-529) połyka Home także tam — potwierdzone: `InputPolicyTests.cs:1025-1045` ma tylko `Shell`, `App`+`Moonlight`, `App`+`Native`, `Osk`. Naprawa: dodać wiersz `[InlineData(InputMode.App, AppType.Web)]`.
  - `[low]` `[patch]` Wiersz macierzy „`SendInput` nic nie wstawił / `vk` poza 1–255” nie jest sprawdzony na kodzie produkcyjnym: `Out_of_range_key_fails_without_throwing` (`KeyInjectionPortTests.cs:61-70`) powtarza warunek zakresu w lambdzie, a `KeyInjector.cs` nie jest linkowany do testów (`MediaStation.Core.Tests.csproj:34`), więc obie gałęzie `TrySendKey` (`:20-23`, `:36-37`) nigdy się nie wykonują — potwierdzone. Naprawa: przenieść sprawdzenie zakresu do kodu linkowanego do testów (pompa/port wstrzyknięć) i pokryć je testem.
  - `[low]` `[patch]` `KeyInjectionPort.Execute` nie chroni delegata `_sendKey`: wyjątek przechodzi przez granicę portu do `CommandRouter.Dispatch` i `OrchestratorLoop.Run` (143-150) jako `error orchestratorFailed`, gubiąc resztę komend kroku i publikację snapshotu — sprzeczne z doc portu (`Ports.cs`, „nie przepuszcza wyjątku … (AD-4)”) i z wzorem `ActionPump.Deliver` (114-125); potwierdzone czytaniem kodu. Naprawa: złapać wyjątek i zalogować `warn keyInjectionFailed {cmdId}`.
  - `[medium]` `[bad_plan]` `PInvoke.SendInput` wykonuje się synchronicznie na wątku pętli Orchestratora (`CommandRouter.Dispatch` → `KeyInjectionPort.Execute` → `KeyInjector.TrySendKey`, wołane z `OrchestratorLoop.Run:136`), a AD-14 (`architecture-mediastation.md:337`) zabrania blokujących wywołań Win32 na tym wątku — potwierdzone; dotychczasowe adaptery routera tylko logują (`LogWindowManagerPort`, `InputHubRoutingPort`), a Win32 żyje na wątkach huba. Skutek: przy stanęciu `SendInput` czeka cała kolejka, także Home; dzisiejszy brak emitera tylko odracza szkodę do `back: key:<VK>` z epic-moonlight-native. Naprawa nie jest trywialna (potrzebny własny wątek/kolejka wstrzyknięć z zachowaniem kolejności), więc plan musi tę decyzję zawierać — loopback.

### 2026-10-22 — Review pass
- verdicts: 3 findings — high 0, medium 0, low 3, false 0, maybe-false 0
- findings:
  - `[low]` `[defer]` Jedyne realne `SendInput` (`KeyInjector.cs:22-38`) nie jest wykonywane nigdy — ani przez testy (pompa dostaje własny delegat), ani przez przebiegi aplikacji (brak emitera `SendKey`), więc przyjęcie dwóch rekordów `INPUT` nie ma dowodu — potwierdzone: `KeyInjector` występuje tylko w `CompositionRoot.cs` obok własnej definicji, a logi z 2026-10-20..22 nie mają `keyInjection`. Nie da się tego pokryć testem bez CsWin32, a układ struktur jest generowany przez CsWin32, więc ryzyko niskie; zapisane w `deferred` i rozstrzyga je odbiór epiku na UM760 (prawdziwy `SendKey` → `keyInjection`, wstrzyknięty klawisz bez Action).
  - `[low]` `[reject]` Implementation Notes (linie o `KeyInjectionPort` i walidacji `vk` w `KeyInjector`) opisują cofnięte pierwsze podejście i przeczą Code Map — prawda, ale naprawa tego spostrzeżenia jest edycją tego planu, co triage odrzuca; wpis „Loopback 2026-10-22” na końcu Implementation Notes oznacza ten blok jako nieaktualny, a wiążące są Code Map, Plan Change Log i kod.
  - `[low]` `[patch]` AC #5 wymaga, by po odrzuconym `vk` kolejna komenda nadal dochodziła, a `Out_of_range_key_logs_warn_without_calling_send_key` enqueue'ował tylko jedną komendę — potwierdzone; poprawka: test dokłada drugą poprawną `SendKey(32, 0x41)` i sprawdza kolejność `warn keyInjectionFailed {cmdId: 31}` → `info keyInjection {cmdId: 32}` oraz `[0x41]` w delegacie.

## Design Notes

Znacznik jest wartością z Core, nie z App, bo reguła „ignoruj własne wstrzyknięcia” należy do polityki (testy w `MediaStation.Core.Tests`), a App tylko go wpisuje w `dwExtraInfo`. `0x4D53_0001` („MS”, wersja 1) zostawia zero dla sprzętu i obcych wstrzyknięć.

Reguła stoi na początku `OnKey`, przed `_kinds` i `_held`: oznaczony key-down nie może uzbroić terminu `long` ani wpisu trzymania, a oznaczony key-up nie może zamknąć cudzego trzymania. `LLKHF_INJECTED` zostaje wyłącznie w korelacji `source`; to dwie różne informacje.

Odrzucenie `SendKey` z portu bez odpowiedzi `CommandCompleted` jest świadome i zgodne z portem `SetInputRouting` z 2.3: Orchestrator nie czeka na wstrzyknięcie, a porażkę widać w logu. `InputCalibration` nie wymaga zmian, bo `--calibrate-input` nie uruchamia pętli Orchestratora, więc żadna `SendKey` tam nie dotrze.

Residual: własne `SendInput` nadal generuje `WM_INPUT`, więc raport Raw Input sprzed wstrzyknięcia może przez 50 ms wpłynąć na `source` następnego prawdziwego klawisza — zachowanie korelacji jest poza zakresem tej story.

**Wątek wstrzyknięć (poprawka po przeglądzie):** `SendInput` jest blokującym wywołaniem Win32, a `CommandRouter.Dispatch` chodzi na wątku pętli Orchestratora, którą AD-14 chroni przed blokadą (Home nie może czekać). Dlatego adapterem portu jest pompa na własnym wątku `key-injection`, wzorem `ActionPump`: `Execute` tylko kolejkuje, a kolejność komend odpowiada kolejności wstrzyknięć. Ta sama pompa jest jedynym miejscem, które woła `KeyInjector` i loguje wynik, więc wyjątek delegata (także przyszły wariant Unicode/Alt z epic-osk) nie przechodzi przez granicę portu (AD-4), a `CommandRouter` zostaje bez Win32 — tak jak przy `LogWindowManagerPort` i `InputHubRoutingPort`. Walidacja `Vk` (1–255) mieszka w pompie, bo tam jest kodem linkowanym do testów; `KeyInjector` zakłada poprawny kod.

## Verification

**Commands:**
- `dotnet build MediaStation.slnx -c Release` — Build succeeded, 0 ostrzeżeń, 0 błędów (kontrakty, `tsc` i Vite w tym samym buildzie; `send-key.ts` powstaje i kompiluje się).
- `dotnet test MediaStation.slnx -c Release` — **782/782 passed, 0 failed, 0 skipped** (baseline 760; +22: 11 `KeyInjectionPumpTests`, 8 polityki znacznika, 2 kontrakty, 1 routing). Wiersze macierzy pokrywają: `Marked_event_gives_no_action_and_is_not_swallowed_in_every_routing_row` (down i up, pięć wierszy AD-8), `Marked_key_up_leaves_real_hold_untouched`, `Marked_key_down_does_not_arm_long_press_or_repeat`, `Injected_without_marker_is_still_mapped_and_swallowed`, `Send_key_as_command_serializes_with_cmd_id_first_and_round_trips`, `Send_key_schema_has_title_required_and_integer_fields`, `Send_key_goes_to_key_injection_port` oraz `KeyInjectionPumpTests` (`Out_of_range_key_logs_warn_without_calling_send_key`, `Send_key_returning_false_logs_warn`, `Send_key_exception_logs_warn_and_the_thread_survives_the_next_command`, kolejność, brak wstrzyknięcia na wątku wołającym, `Dispose`).
- `dotnet run --project tools/MediaStation.SchemaExport -c Release` — `web/contracts/schema/send-key.schema.json` z `title: SendKey`, `required: [cmdId, vk]`, `additionalProperties: false`; reszta schematów bez zmian.
- `cd web && npm run build` — `contracts/generated/send-key.ts` (`cmdId: number; vk: number`), typecheck i Vite zielone.
- `dotnet publish src/MediaStation.App -c Release -r win-x64 --self-contained true` + `MediaStation.exe` z `tools/samples/config` 10 s — proces żył po 10 s, `CloseMainWindow` dał kod **0**; log dokładnie `start` → `inputRouting {cmdId: 1, mode: "shell", pointer: "blocked", epoch: 1}` → `powerSignalsRegistered` → `snapshotPublished {rev: 1}` → `stop {exitCode: 0}`, zero `keyInjection`/`keyInjectionFailed`/`inputHubError`/`warn`/`error`.
- Config użytkownika `%LOCALAPPDATA%\MediaStation\config` podmieniony na sample wyłącznie na czas przebiegu i odtworzony bajt w bajt: SHA-256 przed i po identyczne (`apps.json 325B57C9…`, `input.json 4F832612…`, `settings.json B5DDF4BE…`), `config/invalid` na miejscu, katalog publish usunięty.
- Sprzętowo N/A: brak emitera `SendKey` w tej story i brak fizycznego pilota na ALDER — zachowanie znacznika pokrywają testy polityki, a pełny przebieg (OSK, Alt-unlock, `back: key:<VK>`) należy do odbioru epiku na UM760. Na ALDER wstrzyknięte wejście nie dociera do hooka LL (ustalenie story 2), więc pętla „własny klawisz wraca przez hook” jest tu nieosiągalna automatem i zostaje do odbioru epiku.

## Auto Run Result

**Status:** built.

**Summary:** MediaStation ma Command `SendKey{ vk }` (koperta `sendKey`, generowany JSON Schema i TS) i port `IKeyInjectionPort` z adapterem `KeyInjectionPump` w `Platform/Input`, który wstrzykuje key-down i key-up przez wspólny helper `KeyInjector` (`SendInput`, znacznik MediaStation w `dwExtraInfo`) na własnym wątku `key-injection`, więc wątek pętli Orchestratora nie woła Win32 (AD-14) i żaden wyjątek nie przechodzi przez granicę portu (AD-4). Hook czyta `dwExtraInfo` do `KeyStroke.ExtraInfo`, a `InputPolicy.OnKey` ignoruje każde zdarzenie ze znacznikiem — bez Action i bez połknięcia, przed mapowaniem i stanem trzymania — podczas gdy sama flaga `LLKHF_INJECTED` bez znacznika nadal daje normalną Action (np. Home z pilota). Wejście spoza zakresu 1–255, brak wstawionych zdarzeń i wyjątek wstrzyknięcia kończą się `warn keyInjectionFailed {cmdId}` bez kodu klawisza i nie zatrzymują pompy. Znacznik i helper są gotowe do rozszerzenia przez epic-osk (Unicode `TextSink foreground`) i epic-windows-audio (symulowany Alt).

**Files changed:**
- `src/MediaStation.Core/Contracts/SendKey.cs` — nowy rekord komendy `SendKey(long CmdId, int Vk)`.
- `src/MediaStation.Core/Contracts/MessageTypes.cs` — rejestracja `SendKey` (koperta, schemat, TS).
- `src/MediaStation.Core/Input/InjectionMarker.cs` — nowy znacznik `0x4D53_0001` + `IsOurs`.
- `src/MediaStation.Core/Input/InputPolicy.cs` — `KeyStroke.ExtraInfo` i reguła ignorowania własnych wstrzyknięć w `OnKey`.
- `src/MediaStation.Core/Ports/Ports.cs` — nowy port `IKeyInjectionPort` (fire-and-forget, bez wyjątku przez granicę).
- `src/MediaStation.App/Platform/Input/KeyInjector.cs` — nowy wspólny helper `SendInput` (dwa `INPUT` ze znacznikiem).
- `src/MediaStation.App/Platform/Input/KeyInjectionPump.cs` — nowy adapter portu: kolejka + wątek `key-injection`, walidacja `Vk`, logi, łapanie wyjątków, `Dispose`.
- `src/MediaStation.App/Platform/Input/InputHub.cs` — hook przekazuje `dwExtraInfo`.
- `src/MediaStation.App/Platform/System/NativeMethods.txt` — `SendInput`, `INPUT`, `INPUT_TYPE`, `KEYBDINPUT`, `KEYBD_EVENT_FLAGS`, `VIRTUAL_KEY`.
- `src/MediaStation.App/Orchestrator/CommandRouter.cs` — nowy port w ctorze i `case SendKey`.
- `src/MediaStation.App/CompositionRoot.cs` — pompa tworzona i startowana przed pętlą, zwalniana po `actionPump`.
- `tests/MediaStation.Core.Tests/{InputPolicyTests,CoreMessageContractTests,CommandRouterTests,OrchestratorLoopTests}.cs` + `KeyInjectionPumpTests.cs` (nowy) + `MediaStation.Core.Tests.csproj` — macierz I/O (5 wierszy AD-8), koperta i schemat `sendKey`, routing, zachowanie pompy (kolejność, wątek, zakres, `false`, wyjątek, `Dispose`).

**Review findings:** dwa przebiegi. Przebieg 1: 5 findings (high 0, medium 1, low 3, false 1) — `medium` `bad_plan` (blokujące `SendInput` na wątku Orchestratora, AD-14) wymusił loopback: kod wrócił do baseline, plan dostał pompę wstrzyknięć, a trzy `low` patche (brak wiersza `app` + `web`, nieprzetestowany zakres `Vk`, brak ochrony wyjątku w porcie) wciągnięto do poprawionego planu przed ponowną derywacją. Przebieg 2: 3 findings (high 0, medium 0, low 3) — 1 `low` patch (test odrzuconego zakresu nie dowodził, że kolejna komenda dochodzi; dodana druga `SendKey` i kolejność `warn` → `info`), 1 `low` defer (realny `SendInput` nie jest wykonywany przez testy ani przebiegi — w `deferred`, rozstrzyga odbiór epiku), 1 `low` reject (Implementation Notes opisują cofnięte podejście; naprawa byłaby edycją tego planu, a wpis „Loopback” oznacza je jako nieaktualne). Deferred: 1. Rejected: 2 (1 `false` w przebiegu 1 — zarzut o AC #5 obalony własnym czystym przebiegiem; 1 `low` w przebiegu 2).

**Follow-up review recommended:** false — pierwszy przebieg; patchowane wpisy to wyłącznie `low` (0 `high`, 0 `medium`).

**Verification:** `dotnet build MediaStation.slnx -c Release` — Build succeeded, 0 ostrzeżeń, 0 błędów. `dotnet test MediaStation.slnx -c Release` — 782/782 passed, 0 failed, 0 skipped (baseline 760); każdy wiersz macierzy I/O ma przechodzący test (lista w `## Verification`). `dotnet run --project tools/MediaStation.SchemaExport -c Release` — `send-key.schema.json` z `title: SendKey`, `required: [cmdId, vk]`, `additionalProperties: false`; `cd web && npm run build` — `contracts/generated/send-key.ts` powstaje i kompiluje się, typecheck i Vite zielone. `dotnet publish src/MediaStation.App -c Release -r win-x64 --self-contained true` + `MediaStation.exe` z `tools/samples/config` 10 s — kod 0, log `start` → `inputRouting` → `powerSignalsRegistered` → `snapshotPublished` → `stop {exitCode: 0}`, zero `keyInjection`/`keyInjectionFailed`/`inputHubError`/`warn`/`error`; config użytkownika odtworzony bajt w bajt (SHA-256 trzech plików zgodne), `config/invalid` na miejscu, katalog publish usunięty.

**Residual risks / checks for UM760 (whole-epic acceptance):** realne wywołanie `SendInput` w `KeyInjector` nie ma dowodu automatycznego (brak emitera `SendKey`, na ALDER wstrzyknięte wejście nie wraca przez hook LL) — rozstrzyga odbiór epiku: prawdziwy `SendKey` (OSK, `back: key:<VK>`, Alt-unlock) daje wpis `keyInjection`, a wstrzyknięty klawisz nie daje Action i nie jest połykany. Własne `SendInput` nadal generuje `WM_INPUT`, więc raport Raw Input sprzed wstrzyknięcia może przez 50 ms wpłynąć na `source` następnego prawdziwego klawisza — korelacji `source` ta story nie zmienia. Pojedynczy flake renderera WebView2 odnotowany wcześniej (story 3.5) nie wystąpił w przebiegach weryfikacyjnych tego podejścia.
