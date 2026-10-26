import type { ShellSnapshot } from '../../contracts/generated/shell-snapshot';

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

/** Stan kafelka tekstem (ikony i chip stanu dojdą w kolejnych story). */
const STATE_LABELS: Record<AppTile['state'], string> = {
  notRunning: 'Nie uruchomiona',
  starting: 'Uruchamianie',
  foreground: 'Na ekranie',
  background: 'W tle',
  stopping: 'Zamykanie',
  failed: 'Błąd',
};

interface PlaceholderProps {
  apps: readonly AppTile[];
  focusedAppId: string | null;
}

/** Jeden rząd kafelków; fokus (jeden widoczny) kluczowany `appId`, bez handlerów myszy i klawiatury. */
export function Placeholder({ apps, focusedAppId }: PlaceholderProps) {
  return (
    <main className="screen placeholder" aria-label="MediaStation">
      <Brand />
      <ul className="tiles">
        {apps.map((app) => (
          <li key={app.appId} className={app.appId === focusedAppId ? 'tile tile--focused' : 'tile'}>
            <span className="tile__title">{app.title}</span>
            <span className="tile__state">{STATE_LABELS[app.state]}</span>
          </li>
        ))}
      </ul>
    </main>
  );
}
