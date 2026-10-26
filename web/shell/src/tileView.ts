// Widok stanu kafelka bez DOM (DESIGN.md → Components → „Stany kafelka: chip + linia stanu”).
import type { AppSnapshot } from '../../contracts/generated/shell-snapshot';

/** Origin ikon kafelków: host mapuje `config/icons/` na ten adres (AD-11); CSP przepuszcza `img-src` tylko stąd. */
export const ICONS_ORIGIN = 'https://icons.mediastation.example';

export interface TileChip {
  /** Nazwa ligatury z Material Symbols Rounded. */
  icon: string;
  label: string;
}

export interface TileLine {
  text: string;
  /** Kropka `state-dot` przed tekstem (tylko Background). */
  dot: boolean;
}

export interface TileStatus {
  chip: TileChip | null;
  line: TileLine | null;
}

const NONE: TileStatus = { chip: null, line: null };

/** Chip i linia stanu z `state` i `type`; `notRunning` i `foreground` nie mają żadnego z nich. */
export function tileStatus(app: Pick<AppSnapshot, 'state' | 'type'>): TileStatus {
  switch (app.state) {
    case 'starting':
      return {
        chip: { icon: 'progress_activity', label: 'Uruchamiam…' },
        line: { text: 'Chwilka…', dot: false },
      };
    case 'background':
      // AD-5 pauzuje przy wyjściu tylko aplikacje `web`; Moonlight i natywne grają dalej, wyciszone.
      return app.type === 'web'
        ? { chip: { icon: 'pause', label: 'Wstrzymane' }, line: { text: 'W tle · wstrzymane', dot: true } }
        : { chip: { icon: 'volume_off', label: 'Wyciszony' }, line: { text: 'W tle · wyciszony', dot: true } };
    case 'stopping':
      return { chip: { icon: 'close', label: 'Zamykam…' }, line: null };
    case 'failed':
      return {
        chip: { icon: 'error', label: 'Nie odpowiada' },
        line: { text: 'Coś się zacięło. Wciśnij OK', dot: false },
      };
    default:
      return NONE;
  }
}

/** Adres ikony kafelka; `icon` to sama nazwa pliku z `config/icons/`. */
export function iconUrl(icon: string): string {
  return `${ICONS_ORIGIN}/${encodeURIComponent(icon)}`;
}
