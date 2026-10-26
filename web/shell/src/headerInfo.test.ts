import { describe, expect, it } from 'vitest';
import { formatClock, greeting, initial, msToNextMinute } from './headerInfo';

const at = (h: number, m: number, s = 0, ms = 0) => new Date(2026, 9, 26, h, m, s, ms);

describe('greeting', () => {
  it.each([
    [4, 59, 'Dobry wieczór, Eliza'],
    [5, 0, 'Dzień dobry, Eliza'],
    [17, 59, 'Dzień dobry, Eliza'],
    [18, 0, 'Dobry wieczór, Eliza'],
    [0, 0, 'Dobry wieczór, Eliza'],
  ])('%i:%i -> %s', (h, m, expected) => {
    expect(greeting(at(h, m))).toBe(expected);
  });
});

describe('formatClock', () => {
  it('daje 24h z dwiema cyframi', () => {
    expect(formatClock(at(9, 5))).toBe('09:05');
    expect(formatClock(at(21, 47))).toBe('21:47');
    expect(formatClock(at(0, 0))).toBe('00:00');
  });
});

describe('msToNextMinute', () => {
  it('liczy do zmiany minuty', () => {
    expect(msToNextMinute(at(10, 0, 0, 0))).toBe(60_000);
    expect(msToNextMinute(at(10, 0, 59, 999))).toBe(1);
    expect(msToNextMinute(at(10, 0, 30, 500))).toBe(29_500);
  });
});

describe('initial', () => {
  it('to wielka pierwsza litera imienia', () => {
    expect(initial()).toBe('E');
    expect(initial('ola')).toBe('O');
  });
});
