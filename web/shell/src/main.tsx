import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
// Font lokalnie (UM760 bywa offline); wariant „standard” ma oś opsz.
import '@fontsource-variable/roboto-flex/standard.css';
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

const root = document.getElementById('root');
if (!root) throw new Error('Brak #root w index.html');

createRoot(root).render(
  <StrictMode>
    <div className="stage">
      <App />
    </div>
  </StrictMode>,
);
