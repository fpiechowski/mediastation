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
