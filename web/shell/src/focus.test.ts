import { describe, expect, it } from 'vitest';
import type { Action } from '../../contracts/generated/action';
import type { ShellSnapshot } from '../../contracts/generated/shell-snapshot';
import { CATEGORIES, applyAction, applyFocusRequest, initialFocusState, tilesOf, type FocusState } from './focus';

type AppTile = ShellSnapshot['apps'][number];

const tile = (
  appId: string,
  state: AppTile['state'] = 'notRunning',
  category: AppTile['category'] = 'video',
): AppTile => ({
  appId,
  category,
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
const focused = (focusedAppId: string): FocusState => ({
  ...initialFocusState,
  focusedAppId,
  memory: { video: focusedAppId },
  appliedSeq: 1,
});
const tabsState = (activeTab: FocusState['activeTab'], memory: FocusState['memory'] = {}): FocusState => ({
  ...initialFocusState,
  zone: 'tabs',
  activeTab,
  memory,
  appliedSeq: 1,
});
const mixed = [tile('netflix', 'notRunning', 'video'), tile('moonlight', 'notRunning', 'game'), tile('plex', 'notRunning', 'video')];
const press = (state: FocusState, list: AppTile[], kind: Action['kind']) => applyAction(state, list, action(kind)).state;

describe('applyFocusRequest', () => {
  it('default ustawia fokus na pierwszym kafelku', () => {
    const state = applyFocusRequest(initialFocusState, snapshot(apps, { target: 'default', seq: 1 }));
    expect(state).toMatchObject({ zone: 'tiles', activeTab: 'video', focusedAppId: 'a', appliedSeq: 1 });
  });

  it('appId ustawia fokus na tym kafelku', () => {
    const state = applyFocusRequest(initialFocusState, snapshot(apps, { target: 'c', seq: 2 }));
    expect(state).toMatchObject({ focusedAppId: 'c', appliedSeq: 2 });
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
    expect(state).toMatchObject({ focusedAppId: 'a', appliedSeq: 2 });
  });

  it('nieznany appId zostawia fokus', () => {
    const state = applyFocusRequest(focused('b'), snapshot(apps, { target: 'zzz', seq: 2 }));
    expect(state).toMatchObject({ focusedAppId: 'b', appliedSeq: 2 });
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
    for (const kind of ['down', 'back', 'home', 'appMenu', 'keyboard'] as const) {
      expect(applyAction(before, apps, action(kind))).toEqual({ state: before });
    }
  });

  it('bez fokusu strzałka ustawia pierwszy kafelek, a OK nic nie wysyła', () => {
    expect(applyAction(initialFocusState, apps, action('right')).state.focusedAppId).toBe('a');
    expect(applyAction(initialFocusState, apps, action('ok')).intent).toBeUndefined();
  });
});

describe('zakładki i category', () => {
  it('grupuje po category z zachowaniem kolejności snapshotu', () => {
    expect(tilesOf(mixed, 'video').map((a) => a.appId)).toEqual(['netflix', 'plex']);
    expect(tilesOf(mixed, 'audio')).toEqual([]);
    expect(tilesOf(mixed, 'game').map((a) => a.appId)).toEqual(['moonlight']);
  });

  it('zakładki są zawsze trzy w stałej kolejności', () => {
    expect(CATEGORIES).toEqual(['video', 'audio', 'game']);
  });

  it('Lewo/Prawo w tabs zmieniają aktywną zakładkę, na krańcu zostają', () => {
    expect(press(tabsState('video'), mixed, 'right').activeTab).toBe('audio');
    expect(press(tabsState('audio'), mixed, 'right').activeTab).toBe('game');
    expect(press(tabsState('game'), mixed, 'right').activeTab).toBe('game');
    expect(press(tabsState('video'), mixed, 'left').activeTab).toBe('video');
    expect(press(tabsState('game'), mixed, 'left').zone).toBe('tabs');
  });

  it('Lewo/Prawo na kafelkach zostają w aktywnej zakładce', () => {
    const state = press(focused('netflix'), mixed, 'right');
    expect(state.focusedAppId).toBe('plex');
    expect(press(state, mixed, 'right').focusedAppId).toBe('plex');
  });

  it('Góra z kafelka przechodzi na zakładkę, Dół wraca na pierwszy kafelek bez pamięci', () => {
    const up = press(focused('plex'), mixed, 'up');
    expect(up).toMatchObject({ zone: 'tabs', activeTab: 'video', focusedAppId: null });
    expect(up.memory.video).toBe('plex');
    expect(press(tabsState('game'), mixed, 'down')).toMatchObject({ zone: 'tiles', focusedAppId: 'moonlight' });
    expect(press(tabsState('video'), mixed, 'down').focusedAppId).toBe('netflix');
  });

  it('Dół z zapamiętanym appId, który zniknął z zakładki, idzie na pierwszy kafelek', () => {
    expect(press(tabsState('video', { video: 'gone' }), mixed, 'down').focusedAppId).toBe('netflix');
  });

  it('Góra w tabs, Dół w tiles i OK w tabs są bez skutku', () => {
    const tabs = tabsState('video', { video: 'netflix' });
    expect(applyAction(tabs, mixed, action('up'))).toEqual({ state: tabs });
    expect(applyAction(tabs, mixed, action('ok'))).toEqual({ state: tabs });
    const tiles = focused('netflix');
    expect(applyAction(tiles, mixed, action('down'))).toEqual({ state: tiles });
  });

  it('pamięć per zakładka po appId przeżywa przestawienie wpisów', () => {
    let state = focused('plex');
    for (const kind of ['up', 'right', 'right', 'left', 'left'] as const) state = press(state, mixed, kind);
    expect(state).toMatchObject({ zone: 'tabs', activeTab: 'video' });
    const reordered = [mixed[2]!, mixed[1]!, mixed[0]!];
    expect(press(state, reordered, 'down').focusedAppId).toBe('plex');
  });

  it('pusta zakładka Audio: Dół nic nie robi, brak kafelka fokusowalnego', () => {
    const tabs = tabsState('audio');
    const result = applyAction(tabs, mixed, action('down'));
    expect(result).toEqual({ state: tabs });
    expect(tilesOf(mixed, 'audio')).toHaveLength(0);
    expect(result.state.focusedAppId).toBeNull();
  });

  it('focusRequest z appId z innej zakładki ustawia też zakładkę', () => {
    const state = applyFocusRequest(initialFocusState, snapshot(mixed, { target: 'moonlight', seq: 1 }));
    expect(state).toMatchObject({ zone: 'tiles', activeTab: 'game', focusedAppId: 'moonlight' });
  });

  it('focusRequest default wybiera pierwszy kafelek Video', () => {
    const list = [tile('m', 'notRunning', 'game'), tile('v', 'notRunning', 'video')];
    const state = applyFocusRequest(initialFocusState, snapshot(list, { target: 'default', seq: 1 }));
    expect(state).toMatchObject({ activeTab: 'video', focusedAppId: 'v' });
  });
});
