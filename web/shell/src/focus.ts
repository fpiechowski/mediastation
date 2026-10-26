// Czysta logika fokusu kafelków (AD-16): bez DOM i Reacta, żeby dała się testować w Vitest.
// Fokus kluczujemy `appId`, nigdy indeksem; stan jest efemeryczny i nie wraca do hosta.
import type { Action } from '../../contracts/generated/action';
import type { LaunchApp } from '../../contracts/generated/launch-app';
import type { ShellSnapshot } from '../../contracts/generated/shell-snapshot';

type AppTile = ShellSnapshot['apps'][number];
type AppCategory = AppTile['category'];

/** Zakładki zawsze w tej kolejności i zawsze trzy (kategorie to zamknięty enum, AD-11). */
export const CATEGORIES: readonly AppCategory[] = ['video', 'audio', 'game'];

/** Kafelki kategorii w kolejności snapshotu. */
export function tilesOf(apps: readonly AppTile[], category: AppCategory): AppTile[] {
  return apps.filter((app) => app.category === category);
}

/** Zakładki z aplikacją w `Background` (kropka „w tle”); wyliczane ze snapshotu po `category`, bez własnego stanu. */
export function tabsWithBackground(apps: readonly AppTile[]): ReadonlySet<AppCategory> {
  return new Set(apps.filter((app) => app.state === 'background').map((app) => app.category));
}

export interface FocusState {
  /** Gdzie stoi fokus: w rzędzie zakładek albo na kafelkach aktywnej zakładki. */
  zone: 'tabs' | 'tiles';
  /** Aktywna zakładka (aktywuje się, gdy dostaje fokus). */
  activeTab: AppCategory;
  /** `appId` kafelka w fokusie; tylko w strefie `tiles`, w `tabs` (i do pierwszego `focusRequest`) `null`. */
  focusedAppId: string | null;
  /** `appId` ostatnio sfokusowanego kafelka każdej zakładki (nigdy indeks). */
  memory: Partial<Record<AppCategory, string>>;
  /** `seq` ostatnio zastosowanego `focusRequest`; każdy `seq` stosujemy dokładnie raz. */
  appliedSeq: number | null;
}

export const initialFocusState: FocusState = {
  zone: 'tiles',
  activeTab: 'video',
  focusedAppId: null,
  memory: {},
  appliedSeq: null,
};

/** Cel `focusRequest.target` oznaczający pierwszy kafelek (FocusRequest.DefaultTarget w Core). */
const DEFAULT_TARGET = 'default';

/** Ustawia fokus na kafelku: strefa `tiles`, zakładka kafelka i pamięć tej zakładki. */
function focusTile(state: FocusState, tile: AppTile): FocusState {
  return {
    ...state,
    zone: 'tiles',
    activeTab: tile.category,
    focusedAppId: tile.appId,
    memory: { ...state.memory, [tile.category]: tile.appId },
  };
}

/**
 * Stosuje `focusRequest` ze snapshotu raz na `seq`: `default` → pierwszy kafelek Video, `<appId>` → ten kafelek
 * i jego zakładka. Nieznany `appId` zostawia fokus bez zmian (a `seq` i tak jest zużyty).
 */
export function applyFocusRequest(state: FocusState, snapshot: ShellSnapshot): FocusState {
  const request = snapshot.focusRequest;
  if (request === null || request.seq === state.appliedSeq) return state;

  const target =
    request.target === DEFAULT_TARGET
      ? tilesOf(snapshot.apps, 'video')[0]
      : snapshot.apps.find((app) => app.appId === request.target);

  return { ...(target ? focusTile(state, target) : state), appliedSeq: request.seq };
}

export interface ActionResult {
  state: FocusState;
  intent?: LaunchApp;
}

/**
 * Obsługa Action z mostu. Strefa `tiles`: Left/Right po `appId` w obrębie aktywnej zakładki (na krańcu zostaje),
 * Up → `tabs`, OK (`press`) na kafelku `notRunning`/`background` daje Intent `launchApp`.
 * Strefa `tabs`: Left/Right zmieniają aktywną zakładkę (na krańcu zostaje), Down → zapamiętany kafelek
 * zakładki (pierwszy, gdy brak pamięci; nic, gdy zakładka pusta). Pozostałe Actions są bez skutku.
 */
export function applyAction(state: FocusState, apps: readonly AppTile[], action: Action): ActionResult {
  const tiles = tilesOf(apps, state.activeTab);
  switch (action.kind) {
    case 'left':
    case 'right': {
      const step = action.kind === 'left' ? -1 : 1;
      if (state.zone === 'tabs') {
        const next = CATEGORIES[CATEGORIES.indexOf(state.activeTab) + step];
        return next ? { state: { ...state, activeTab: next } } : { state };
      }
      const index = tiles.findIndex((app) => app.appId === state.focusedAppId);
      const next = index < 0 ? tiles[0] : tiles[index + step];
      return next ? { state: focusTile(state, next) } : { state };
    }
    case 'up': {
      if (state.zone !== 'tiles') return { state };
      return { state: { ...state, zone: 'tabs', focusedAppId: null } };
    }
    case 'down': {
      if (state.zone !== 'tabs') return { state };
      const target = tiles.find((app) => app.appId === state.memory[state.activeTab]) ?? tiles[0];
      return target ? { state: focusTile(state, target) } : { state };
    }
    case 'ok': {
      if (action.phase !== 'press' || state.zone !== 'tiles') return { state };
      const focused = tiles.find((app) => app.appId === state.focusedAppId);
      if (focused && (focused.state === 'notRunning' || focused.state === 'background')) {
        return { state, intent: { appId: focused.appId } };
      }
      return { state };
    }
    default:
      return { state };
  }
}
