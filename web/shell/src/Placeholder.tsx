import type { ShellSnapshot } from '../../contracts/generated/shell-snapshot';
import { AppMenu } from './AppMenu';
import { CATEGORIES, tabsWithBackground, tilesOf, type FocusState } from './focus';
import { Tile } from './Tile';

/** Logo placeholder (DESIGN.md → components.logo): zaokrąglony ekran + play w currentColor, wordmark obok. */
export function Brand() {
  return (
    <div className="brand">
      <svg viewBox="0 0 48 48" aria-hidden="true">
        <rect x="2" y="8" width="44" height="32" rx="9" fill="none" stroke="currentColor" strokeWidth="3.2" />
        <path d="M20 17.5v13l11-6.5z" fill="currentColor" />
      </svg>
      <b>MediaStation</b>
    </div>
  );
}

type AppTile = ShellSnapshot['apps'][number];

/** Etykiety zakładek (kolejność z `CATEGORIES`). */
const TAB_LABELS: Record<AppTile['category'], string> = { video: 'Video', audio: 'Audio', game: 'Game' };

interface PlaceholderProps {
  apps: readonly AppTile[];
  focus: FocusState;
}

/** Rząd zakładek i kafelki aktywnej zakładki; jeden widoczny fokus (zakładka albo kafelek), bez handlerów myszy i klawiatury. */
export function Placeholder({ apps, focus }: PlaceholderProps) {
  const tiles = tilesOf(apps, focus.activeTab);
  const running = tabsWithBackground(apps);
  return (
    <main className="screen placeholder" aria-label="MediaStation">
      <Brand />
      <ul className="tabs">
        {CATEGORIES.map((category) => {
          const active = category === focus.activeTab;
          const pill = active && focus.zone === 'tabs';
          return (
            <li
              key={category}
              className={pill ? 'tab tab--active tab--focused' : active ? 'tab tab--active' : 'tab'}
            >
              {TAB_LABELS[category]}
              {running.has(category) && <span className="tab-running-dot" aria-hidden="true" />}
            </li>
          );
        })}
      </ul>
      {tiles.length === 0 ? (
        <p className="empty-state">Tu jeszcze cisza. Muzyka dojdzie wkrótce.</p>
      ) : (
        <ul className="tiles">
          {tiles.map((app) => (
            <Tile key={app.appId} app={app} focused={focus.zone === 'tiles' && focus.menu === null && app.appId === focus.focusedAppId} />
          ))}
        </ul>
      )}
      {focus.menu !== null && (
        <AppMenu
          title={apps.find((app) => app.appId === focus.menu?.appId)?.title ?? ''}
          index={Math.max(0, tiles.findIndex((app) => app.appId === focus.menu?.appId))}
          item={focus.menu.item}
        />
      )}
    </main>
  );
}
