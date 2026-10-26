import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
// Font lokalnie (UM760 bywa offline); wariant „standard” ma oś opsz.
import '@fontsource-variable/roboto-flex/standard.css';
// Ikony Material Symbols Rounded (DESIGN.md → Components → Chip), też lokalnie; klasa `.material-symbols` w tokens.css.
import '@fontsource/material-symbols-rounded/400.css';
import './tokens.css';
import { App } from './App';

const STAGE_WIDTH = 1920;
const STAGE_HEIGHT = 1080;

// Skaluje powierzchnię 1920×1080 do okna (np. TV z 200% DPI daje viewport 960×540).
function fitStage() {
  const scale = Math.min(window.innerWidth / STAGE_WIDTH, window.innerHeight / STAGE_HEIGHT);
  const style = document.documentElement.style;
  style.setProperty('--stage-scale', String(scale));
  style.setProperty('--stage-x', `${(window.innerWidth - STAGE_WIDTH * scale) / 2}px`);
  style.setProperty('--stage-y', `${(window.innerHeight - STAGE_HEIGHT * scale) / 2}px`);
}

fitStage();
window.addEventListener('resize', fitStage);

// Kursor jest ukryty w bezruchu i widoczny po ruchu myszy (AD-8: mysz nigdy nie jest blokowana).
const CURSOR_IDLE_MS = 2000;
let cursorIdleTimer: number | undefined;
window.addEventListener('mousemove', () => {
  document.documentElement.classList.add('pointer-active');
  window.clearTimeout(cursorIdleTimer);
  cursorIdleTimer = window.setTimeout(
    () => document.documentElement.classList.remove('pointer-active'),
    CURSOR_IDLE_MS,
  );
});

const root = document.getElementById('root');
if (!root) throw new Error('Brak #root w index.html');

createRoot(root).render(
  <StrictMode>
    <div className="stage">
      <App />
    </div>
  </StrictMode>,
);
