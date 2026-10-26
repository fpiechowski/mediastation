import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it } from 'vitest';
import type { ConfigError } from '../../contracts/generated/shell-snapshot';
import { ConfigErrorScreen } from './ConfigErrorScreen';

const errors = (n: number): ConfigError[] =>
  Array.from({ length: n }, (_, i) => ({ file: `apps${i}.json`, path: `$.apps[${i}]`, reason: `powód ${i}` }));

describe('ConfigErrorScreen', () => {
  it('10 błędów: 5 pozycji i wiersz „…i jeszcze 5 błędów”', () => {
    const html = renderToStaticMarkup(<ConfigErrorScreen errors={errors(10)} />);
    expect(html.match(/config-error__reason/g)).toHaveLength(5);
    expect(html).toContain('apps4.json');
    expect(html).not.toContain('apps5.json');
    expect(html).toContain('…i jeszcze 5 błędów');
  });

  it('5 błędów: bez wskaźnika', () => {
    const html = renderToStaticMarkup(<ConfigErrorScreen errors={errors(5)} />);
    expect(html.match(/config-error__reason/g)).toHaveLength(5);
    expect(html).not.toContain('jeszcze');
  });
});
