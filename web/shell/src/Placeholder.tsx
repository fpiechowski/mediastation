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

export function Placeholder() {
  return (
    <main className="screen placeholder" aria-label="MediaStation">
      <Brand />
    </main>
  );
}
