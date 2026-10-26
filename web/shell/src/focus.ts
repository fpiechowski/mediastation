// Czysta logika fokusu kafelków (AD-16): bez DOM i Reacta, żeby dała się testować w Vitest.
// Fokus kluczujemy `appId`, nigdy indeksem; stan jest efemeryczny i nie wraca do hosta.
import type { Action } from '../../contracts/generated/action';
import type { LaunchApp } from '../../contracts/generated/launch-app';
import type { ShellSnapshot } from '../../contracts/generated/shell-snapshot';

type AppTile = ShellSnapshot['apps'][number];

export interface FocusState {
  /** `appId` kafelka w fokusie albo `null`, dopóki żaden `focusRequest` go nie ustawił. */
  focusedAppId: string | null;
  /** `seq` ostatnio zastosowanego `focusRequest`; każdy `seq` stosujemy dokładnie raz. */
  appliedSeq: number | null;
}

export const initialFocusState: FocusState = { focusedAppId: null, appliedSeq: null };

/** Cel `focusRequest.target` oznaczający pierwszy kafelek (FocusRequest.DefaultTarget w Core). */
const DEFAULT_TARGET = 'default';

/**
 * Stosuje `focusRequest` ze snapshotu raz na `seq`: `default` → pierwszy kafelek, `<appId>` → ten kafelek.
 * Nieznany `appId` zostawia fokus bez zmian (a `seq` i tak jest zużyty).
 */
export function applyFocusRequest(state: FocusState, snapshot: ShellSnapshot): FocusState {
  const request = snapshot.focusRequest;
  if (request === null || request.seq === state.appliedSeq) return state;

  const target =
    request.target === DEFAULT_TARGET
      ? snapshot.apps[0]?.appId
      : snapshot.apps.find((app) => app.appId === request.target)?.appId;

  return { focusedAppId: target ?? state.focusedAppId, appliedSeq: request.seq };
}

export interface ActionResult {
  state: FocusState;
  intent?: LaunchApp;
}

/**
 * Obsługa Action z mostu: Left/Right przesuwają fokus po pozycji `appId` w bieżącej liście (na krańcu zostaje),
 * OK (`press`) na kafelku `notRunning`/`background` daje Intent `launchApp`. Pozostałe Actions są bez skutku.
 */
export function applyAction(state: FocusState, apps: readonly AppTile[], action: Action): ActionResult {
  switch (action.kind) {
    case 'left':
    case 'right': {
      const index = apps.findIndex((app) => app.appId === state.focusedAppId);
      if (index < 0) {
        const first = apps[0];
        return first ? { state: { ...state, focusedAppId: first.appId } } : { state };
      }
      const next = apps[action.kind === 'left' ? index - 1 : index + 1];
      return next ? { state: { ...state, focusedAppId: next.appId } } : { state };
    }
    case 'ok': {
      if (action.phase !== 'press') return { state };
      const focused = apps.find((app) => app.appId === state.focusedAppId);
      if (focused && (focused.state === 'notRunning' || focused.state === 'background')) {
        return { state, intent: { appId: focused.appId } };
      }
      return { state };
    }
    default:
      return { state };
  }
}
