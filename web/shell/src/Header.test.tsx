import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it } from 'vitest';
import { Header } from './Header';

describe('Header', () => {
  const html = renderToStaticMarkup(<Header now={new Date(2026, 9, 26, 21, 7)} />);

  it('pokazuje logo, powitanie, inicjał i zegar', () => {
    expect(html).toContain('MediaStation');
    expect(html).toContain('Dobry wieczór, Eliza');
    expect(html).toContain('>E<');
    expect(html).toContain('>21:07<');
    expect(html).toContain('header__update-slot');
  });

  it('jest dekoracją bez fokusu', () => {
    expect(html).not.toMatch(/tabindex|<button|<a /);
    expect(html).not.toContain('focus');
  });
});
