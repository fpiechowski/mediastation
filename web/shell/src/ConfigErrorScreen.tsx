import type { ConfigError } from '../../contracts/generated/shell-snapshot';
import { Brand } from './Placeholder';

/** Ekran błędu konfiguracji (DESIGN.md → components.config-error; EXPERIENCE.md → A4). Bez fokusu i akcji. */
export function ConfigErrorScreen({ errors }: { errors: ConfigError[] }) {
  return (
    <main className="screen config-error">
      <Brand />
      <div className="config-error__content">
        <h1>Coś nie gra w konfiguracji</h1>
        <p className="config-error__hint">
          Popraw pliki w %LOCALAPPDATA%\MediaStation\config i uruchom MediaStation ponownie.
        </p>
        <ul>
          {errors.map((error, i) => (
            <li key={`${error.file}:${error.path ?? ''}:${i}`}>
              <span className="config-error__file">{error.file}</span>
              {error.path !== null && <span className="config-error__path">{error.path}</span>}
              <span className="config-error__reason">{error.reason}</span>
            </li>
          ))}
        </ul>
      </div>
    </main>
  );
}
