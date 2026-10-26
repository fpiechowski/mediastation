import { describe, expect, it } from 'vitest';
import type { AppSnapshot } from '../../contracts/generated/shell-snapshot';
import { iconUrl, tileStatus } from './tileView';

const status = (state: AppSnapshot['state'], type: AppSnapshot['type'] = 'web') => tileStatus({ state, type });

describe('tileStatus', () => {
  it.each(['web', 'moonlight', 'native'] as const)('notRunning i foreground nie mają chipa ani linii (%s)', (type) => {
    expect(status('notRunning', type)).toEqual({ chip: null, line: null });
    expect(status('foreground', type)).toEqual({ chip: null, line: null });
  });

  it.each(['web', 'moonlight', 'native'] as const)('starting: statyczna ikona i „Chwilka…” (%s)', (type) => {
    expect(status('starting', type)).toEqual({
      chip: { icon: 'progress_activity', label: 'Uruchamiam…' },
      line: { text: 'Chwilka…', dot: false },
    });
  });

  it('background + web: pause „Wstrzymane”, kropka i „W tle · wstrzymane”', () => {
    expect(status('background', 'web')).toEqual({
      chip: { icon: 'pause', label: 'Wstrzymane' },
      line: { text: 'W tle · wstrzymane', dot: true },
    });
  });

  it.each(['moonlight', 'native'] as const)('background + %s: volume_off „Wyciszony”, kropka i „W tle · wyciszony”', (type) => {
    expect(status('background', type)).toEqual({
      chip: { icon: 'volume_off', label: 'Wyciszony' },
      line: { text: 'W tle · wyciszony', dot: true },
    });
  });

  it.each(['web', 'moonlight', 'native'] as const)('stopping: close „Zamykam…” bez linii (%s)', (type) => {
    expect(status('stopping', type)).toEqual({ chip: { icon: 'close', label: 'Zamykam…' }, line: null });
  });

  it.each(['web', 'moonlight', 'native'] as const)('failed: error „Nie odpowiada” i podpowiedź (%s)', (type) => {
    expect(status('failed', type)).toEqual({
      chip: { icon: 'error', label: 'Nie odpowiada' },
      line: { text: 'Coś się zacięło. Wciśnij OK', dot: false },
    });
  });
});

describe('iconUrl', () => {
  it('buduje adres z hosta ikon i zakodowanej nazwy pliku', () => {
    expect(iconUrl('netflix.png')).toBe('https://icons.mediastation.example/netflix.png');
    expect(iconUrl('plex htpc#1.png')).toBe('https://icons.mediastation.example/plex%20htpc%231.png');
  });
});
