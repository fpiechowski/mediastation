import type { CSSProperties } from 'react';
import { MENU_ITEMS } from './focus';

interface AppMenuProps {
  /** Nazwa aplikacji (etykieta menu dla czytników). */
  title: string;
  /** Pozycja kafelka w rzędzie aktywnej zakładki; menu leży nad nim. */
  index: number;
  /** Pozycja menu w fokusie (pigułka). */
  item: number;
}

/** AppMenu (DESIGN.md → appmenu): scrim na cały ekran i panel nad kafelkiem; jedyny fokus to pozycja menu (focus-pill). */
export function AppMenu({ title, index, item }: AppMenuProps) {
  return (
    <>
      <div className="scrim" aria-hidden="true" />
      <ul
        className="appmenu"
        role="menu"
        aria-label={title}
        style={{ '--appmenu-index': index } as CSSProperties}
      >
        {MENU_ITEMS.map((entry, i) => (
          <li key={entry.intent} className={i === item ? 'appmenu__item appmenu__item--focus' : 'appmenu__item'} role="menuitem">
            <span className="material-symbols" aria-hidden="true">
              {entry.icon}
            </span>
            {entry.label}
          </li>
        ))}
      </ul>
    </>
  );
}
