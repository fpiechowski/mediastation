import { useEffect, useState } from 'react';
import { formatClock, greeting, initial, msToNextMinute } from './headerInfo';

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

/** Bieżący czas odświeżany równo ze zmianą minuty; timer sprzątany przy odmontowaniu. */
function useNow(): Date {
  const [now, setNow] = useState(() => new Date());
  useEffect(() => {
    let timer: ReturnType<typeof setTimeout>;
    const tick = () => {
      const current = new Date();
      setNow(current);
      timer = setTimeout(tick, msToNextMinute(current));
    };
    timer = setTimeout(tick, msToNextMinute(new Date()));
    return () => clearTimeout(timer);
  }, []);
  return now;
}

/**
 * Nagłówek Home (DESIGN.md → Layout & Spacing, avatar, clock, greeting): logo, separator, avatar + powitanie, puste miejsce
 * na przycisk aktualizacji (epic-deploy-resilience) i zegar na gutterze. Dekoracja, bez fokusu.
 * `now` pozwala testom podać stały czas.
 */
export function Header({ now }: { now?: Date }) {
  const live = useNow();
  const time = now ?? live;
  return (
    <header className="header">
      <Brand />
      <span className="header__separator" aria-hidden="true" />
      <div className="header__welcome">
        <span className="avatar" aria-hidden="true">
          {initial()}
        </span>
        <span className="greeting">{greeting(time)}</span>
      </div>
      <div className="header__right">
        <span className="header__update-slot" />
        <time className="clock">{formatClock(time)}</time>
      </div>
    </header>
  );
}
