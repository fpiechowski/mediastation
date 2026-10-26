import type { ConfigError } from '../../contracts/generated/shell-snapshot';
import { hiddenLabel, visibleErrors } from './configErrors';
import { Brand } from './Header';

/** Ekran błędu konfiguracji (DESIGN.md → components.config-error; EXPERIENCE.md → A4). Bez fokusu i akcji. */
export function ConfigErrorScreen({ errors }: { errors: ConfigError[] }) {
  const { shown, hidden } = visibleErrors(errors);
  return (
    <main className="screen config-error">
      <Brand />
      <div className="config-error__content">
        <h1>Coś nie gra w konfiguracji</h1>
        <p className="config-error__hint">
          Popraw pliki w %LOCALAPPDATA%\MediaStation\config i uruchom MediaStation ponownie.
        </p>
        <ul>
          {shown.map((error, i) => (
            <li key={`${error.file}:${error.path ?? ''}:${i}`}>
              <span className="config-error__where">
                <span className="config-error__file">{error.file}</span>
                {error.path !== null && <span className="config-error__path">{error.path}</span>}
              </span>
              <span className="config-error__reason">{error.reason}</span>
            </li>
          ))}
        </ul>
        {hidden > 0 && <p className="config-error__more">{hiddenLabel(hidden)}</p>}
      </div>
    </main>
  );
}
