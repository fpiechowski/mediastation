import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it } from 'vitest';
import type { AppSnapshot } from '../../contracts/generated/shell-snapshot';
import { applyFocusRequest, initialFocusState, tabsWithBackground } from './focus';
import { Placeholder } from './Placeholder';

const app = (appId: string, category: AppSnapshot['category'], state: AppSnapshot['state']): AppSnapshot => ({
  appId,
  category,
  state,
  title: appId,
  icon: null,
  type: 'web',
});

const DOT = 'tab-running-dot';

/** Etykiety zakładek, w których wyrenderowano kropkę. */
const tabsWithDot = (apps: AppSnapshot[]): string[] => {
  const html = renderToStaticMarkup(<Placeholder apps={apps} focus={initialFocusState} />);
  return [...html.matchAll(/<li class="tab[^"]*">(Video|Audio|Game)(<span class="tab-running-dot")?/g)]
    .filter((m) => m[2] !== undefined)
    .map((m) => m[1] as string);
};

describe('kropka „w tle” na zakładce', () => {
  it('Video w Background zapala kropkę tylko na Video', () => {
    const apps = [app('netflix', 'video', 'background'), app('moonlight', 'game', 'notRunning')];

    expect(tabsWithBackground(apps)).toEqual(new Set(['video']));
    expect(tabsWithDot(apps)).toEqual(['Video']);
  });

  it.each(['foreground', 'failed', 'starting', 'stopping', 'notRunning'] as const)(
    '%s nie zapala kropki',
    (state) => {
      const apps = [app('netflix', 'video', state)];

      expect(tabsWithBackground(apps).size).toBe(0);
      expect(renderToStaticMarkup(<Placeholder apps={apps} focus={initialFocusState} />)).not.toContain(DOT);
    },
  );

  it('powrót do NotRunning gasi kropkę', () => {
    expect(tabsWithDot([app('netflix', 'video', 'background')])).toEqual(['Video']);
    expect(tabsWithDot([app('netflix', 'video', 'notRunning')])).toEqual([]);
  });

  it('kropka jest przy zakładce Game, gdy tam działa aplikacja w tle', () => {
    expect(tabsWithDot([app('moonlight', 'game', 'background'), app('plex', 'video', 'foreground')])).toEqual(['Game']);
  });
});

describe('fokus po starcie (focusRequest default)', () => {
  it('po pierwszym snapshocie jedyny kafelek z fokusem to pierwszy kafelek Video', () => {
    const apps = [app('moonlight', 'game', 'notRunning'), app('netflix', 'video', 'notRunning'), app('plex', 'video', 'notRunning')];
    const focus = applyFocusRequest(initialFocusState, {
      rev: 1,
      screen: 'placeholder',
      configErrors: [],
      apps,
      focusRequest: { target: 'default', seq: 1 },
    });
    const html = renderToStaticMarkup(<Placeholder apps={apps} focus={focus} />);

    expect(html.match(/tile--focused/g)).toHaveLength(1);
    expect(html).toMatch(/<li class="tile tile--focused">(?:(?!<li).)*netflix/);
  });
});

describe('AppMenu', () => {
  const apps = [app('netflix', 'video', 'failed'), app('plex', 'video', 'notRunning')];
  const html = (item: number) =>
    renderToStaticMarkup(
      <Placeholder
        apps={apps}
        focus={{ ...initialFocusState, focusedAppId: 'netflix', menu: { appId: 'netflix', item } }}
      />,
    );

  it('rysuje scrim, dwie pozycje i dokładnie jeden fokus (pozycja menu, nie kafelek)', () => {
    const out = html(0);

    expect(out).toContain('class="scrim"');
    expect(out).toContain('Uruchom ponownie');
    expect(out).toContain('Zamknij');
    expect(out).not.toContain('tile--focused');
    expect(out.match(/appmenu__item--focus/g)).toHaveLength(1);
    expect(out).toMatch(/appmenu__item appmenu__item--focus"[^>]*>(?:(?!<li).)*Uruchom ponownie/);
  });

  it('fokus pozycji 1 stoi na „Zamknij”', () => {
    expect(html(1)).toMatch(/appmenu__item--focus"[^>]*>(?:(?!<li).)*Zamknij/);
  });

  it('bez menu nie ma scrimu', () => {
    const out = renderToStaticMarkup(<Placeholder apps={apps} focus={initialFocusState} />);
    expect(out).not.toContain('class="scrim"');
  });
});

describe('przesunięcie rzędu w title-safe', () => {
  const apps = [app('a', 'video', 'notRunning'), app('b', 'video', 'notRunning'), app('c', 'video', 'notRunning')];
  const offsetOf = (focus: Parameters<typeof Placeholder>[0]['focus']) =>
    renderToStaticMarkup(<Placeholder apps={apps} focus={focus} />).match(/--row-offset:(-?\d+)px/)?.[1];

  it('ostatni kafelek w fokusie przesuwa rząd w lewo, a pierwszy w prawo', () => {
    expect(Number(offsetOf({ ...initialFocusState, focusedAppId: 'c' }))).toBeLessThan(0);
    expect(Number(offsetOf({ ...initialFocusState, focusedAppId: 'a' }))).toBeGreaterThan(0);
  });

  it('fokus na zakładkach zostawia rząd w spoczynku', () => {
    expect(offsetOf({ ...initialFocusState, zone: 'tabs', focusedAppId: null })).toBe('0');
  });

  it('otwarte menu trzyma przesunięcie kafelka, z którego wyszło', () => {
    const open = { ...initialFocusState, focusedAppId: 'c', menu: { appId: 'c', item: 0 } };
    expect(offsetOf(open)).toBe(offsetOf({ ...initialFocusState, focusedAppId: 'c' }));
  });
});
