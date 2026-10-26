import { useState } from 'react';
import type { AppSnapshot } from '../../contracts/generated/shell-snapshot';
import { iconUrl, tileStatus } from './tileView';

interface TileProps {
  app: AppSnapshot;
  focused: boolean;
}

/**
 * Kafelek aplikacji (DESIGN.md → Tile): 544×306 z ikoną albo samym tytułem jako znakiem, chip stanu w lewym dolnym rogu,
 * pod spodem nazwa i linia stanu. Brak ikony (`icon: null`) i błąd ładowania `<img>` dają ten sam fallback.
 */
export function Tile({ app, focused }: TileProps) {
  // Ikona, która nie załadowała się; inna ikona w kolejnym snapshocie dostaje nową szansę.
  const [failedIcon, setFailedIcon] = useState<string | null>(null);
  const { chip, line } = tileStatus(app);
  const showIcon = app.icon !== null && app.icon !== failedIcon;

  return (
    <li className={focused ? 'tile tile--focused' : 'tile'}>
      <div className="tile__card">
        {showIcon && app.icon !== null ? (
          <img className="tile__icon" src={iconUrl(app.icon)} alt="" onError={() => setFailedIcon(app.icon)} />
        ) : (
          <span className="tile__mark">{app.title}</span>
        )}
        {chip && (
          <span className="chip">
            <span className="material-symbols" aria-hidden="true">
              {chip.icon}
            </span>
            {chip.label}
          </span>
        )}
      </div>
      <div className="tile__meta">
        <span className="tile__name">{app.title}</span>
        <span className="tile__line">
          {line?.dot && <span className="state-dot" aria-hidden="true" />}
          {line?.text}
        </span>
      </div>
    </li>
  );
}
