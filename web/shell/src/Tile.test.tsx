import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it } from 'vitest';
import type { AppSnapshot } from '../../contracts/generated/shell-snapshot';
import { Tile } from './Tile';

const app = (overrides: Partial<AppSnapshot> = {}): AppSnapshot => ({
  appId: 'netflix',
  category: 'video',
  state: 'notRunning',
  title: 'Netflix',
  icon: 'netflix.png',
  type: 'web',
  ...overrides,
});

const html = (a: AppSnapshot, focused = false) => renderToStaticMarkup(<Tile app={a} focused={focused} />);

describe('Tile', () => {
  it('z ikoną pokazuje <img> z hosta ikon i nie pokazuje znaku-tytułu', () => {
    const out = html(app());

    expect(out).toContain('<img');
    expect(out).toContain('src="https://icons.mediastation.example/netflix.png"');
    expect(out).not.toContain('tile__mark');
  });

  it('bez ikony (icon: null) pokazuje sam tytuł jako znak, bez <img>', () => {
    const out = html(app({ icon: null }));

    expect(out).not.toContain('<img');
    expect(out).toContain('<span class="tile__mark">Netflix</span>');
  });

  it('failed: chip „Nie odpowiada” i linia „Coś się zacięło. Wciśnij OK”', () => {
    const out = html(app({ state: 'failed' }));

    expect(out).toContain('Nie odpowiada');
    expect(out).toContain('Coś się zacięło. Wciśnij OK');
  });

  it('background + web: chip „Wstrzymane” i kropka stanu', () => {
    const out = html(app({ state: 'background' }));

    expect(out).toContain('Wstrzymane');
    expect(out).toContain('state-dot');
    expect(out).toContain('W tle · wstrzymane');
  });

  it('notRunning: bez chipa, fokus dodaje klasę', () => {
    expect(html(app())).not.toContain('class="chip"');
    expect(html(app(), true)).toContain('tile tile--focused');
  });
});
