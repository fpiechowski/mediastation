import { describe, expect, it } from 'vitest';
import type { Action } from '../../contracts/generated/action';
import type { ShellSnapshot } from '../../contracts/generated/shell-snapshot';
import { applyAction, applyFocusRequest, initialFocusState, type FocusState } from './focus';

type AppTile = ShellSnapshot['apps'][number];

const tile = (appId: string, state: AppTile['state'] = 'notRunning'): AppTile => ({
  appId,
  category: 'video',
  state,
  title: appId.toUpperCase(),
});

const snapshot = (apps: AppTile[], focusRequest: ShellSnapshot['focusRequest'], rev = 1): ShellSnapshot => ({
  rev,
  screen: 'placeholder',
  configErrors: [],
  apps,
  focusRequest,
});

const action = (kind: Action['kind'], phase: Action['phase'] = 'press'): Action => ({
  kind,
  phase,
  source: 'keyboard',
  tsMono: 1,
});

const apps = [tile('a'), tile('b'), tile('c')];
const focused = (focusedAppId: string): FocusState => ({ focusedAppId, appliedSeq: 1 });

describe('applyFocusRequest', () => {
  it('default ustawia fokus na pierwszym kafelku', () => {
    const state = applyFocusRequest(initialFocusState, snapshot(apps, { target: 'default', seq: 1 }));
    expect(state).toEqual({ focusedAppId: 'a', appliedSeq: 1 });
  });

  it('appId ustawia fokus na tym kafelku', () => {
    const state = applyFocusRequest(initialFocusState, snapshot(apps, { target: 'c', seq: 2 }));
    expect(state).toEqual({ focusedAppId: 'c', appliedSeq: 2 });
  });

  it('stosuje seq tylko raz', () => {
    const first = applyFocusRequest(initialFocusState, snapshot(apps, { target: 'default', seq: 1 }));
    const moved = applyAction(first, apps, action('right')).state;
    const again = applyFocusRequest(moved, snapshot(apps, { target: 'default', seq: 1 }, 2));
    expect(again.focusedAppId).toBe('b');
  });

  it('nowy seq zastosuje się ponownie', () => {
    const moved = applyAction(focused('a'), apps, action('right')).state;
    const state = applyFocusRequest(moved, snapshot(apps, { target: 'default', seq: 2 }));
    expect(state).toEqual({ focusedAppId: 'a', appliedSeq: 2 });
  });

  it('nieznany appId zostawia fokus', () => {
    const state = applyFocusRequest(focused('b'), snapshot(apps, { target: 'zzz', seq: 2 }));
    expect(state).toEqual({ focusedAppId: 'b', appliedSeq: 2 });
  });

  it('brak focusRequest nic nie zmienia', () => {
    const before = focused('b');
    expect(applyFocusRequest(before, snapshot(apps, null))).toBe(before);
  });
});

describe('applyAction', () => {
  it('Right i Left przesuwają fokus na sąsiedni appId', () => {
    expect(applyAction(focused('a'), apps, action('right')).state.focusedAppId).toBe('b');
    expect(applyAction(focused('c'), apps, action('left')).state.focusedAppId).toBe('b');
    expect(applyAction(focused('b'), apps, action('right', 'repeat')).state.focusedAppId).toBe('c');
  });

  it('na krańcach fokus zostaje', () => {
    expect(applyAction(focused('a'), apps, action('left')).state.focusedAppId).toBe('a');
    expect(applyAction(focused('c'), apps, action('right')).state.focusedAppId).toBe('c');
  });

  it('przesunięcie listy nie zmienia fokusu na appId', () => {
    const shifted = [tile('z'), ...apps];
    const result = applyAction(focused('b'), shifted, action('right'));
    expect(result.state.focusedAppId).toBe('c');
    expect(applyAction(focused('b'), shifted, action('left')).state.focusedAppId).toBe('a');
  });

  it('OK na notRunning i background wysyła launchApp', () => {
    const list = [tile('a', 'notRunning'), tile('b', 'background')];
    expect(applyAction(focused('a'), list, action('ok')).intent).toEqual({ appId: 'a' });
    expect(applyAction(focused('b'), list, action('ok')).intent).toEqual({ appId: 'b' });
  });

  it('OK na innym stanie nic nie robi', () => {
    for (const state of ['starting', 'foreground', 'stopping', 'failed'] as const) {
      const list = [tile('a', state)];
      expect(applyAction(focused('a'), list, action('ok')).intent).toBeUndefined();
    }
  });

  it('OK long i pozostałe Actions są bez skutku', () => {
    const before = focused('a');
    expect(applyAction(before, apps, action('ok', 'long'))).toEqual({ state: before });
    for (const kind of ['up', 'down', 'back', 'home', 'appMenu', 'keyboard'] as const) {
      expect(applyAction(before, apps, action(kind))).toEqual({ state: before });
    }
  });

  it('bez fokusu strzałka ustawia pierwszy kafelek, a OK nic nie wysyła', () => {
    expect(applyAction(initialFocusState, apps, action('right')).state.focusedAppId).toBe('a');
    expect(applyAction(initialFocusState, apps, action('ok')).intent).toBeUndefined();
  });
});
