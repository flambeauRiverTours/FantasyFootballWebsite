import { describe, expect, it } from 'vitest';
import { contrast, tokens } from './colour';

const themes = Object.entries(tokens());

describe('oklch conversion', () => {
  it('agrees with the known black and white extremes', () => {
    expect(contrast('oklch(100% 0 0)', 'oklch(0% 0 0)')).toBeCloseTo(21, 1);
  });
});

describe.each(themes)('%s theme contrast', (_name, t) => {
  it('body text clears 4.5:1', () => {
    expect(contrast(t['--fg'], t['--bg'])).toBeGreaterThanOrEqual(4.5);
  });

  it('muted text clears 4.5:1 on both grounds', () => {
    expect(contrast(t['--fg-muted'], t['--bg'])).toBeGreaterThanOrEqual(4.5);
    expect(contrast(t['--fg-muted'], t['--bg-subtle'])).toBeGreaterThanOrEqual(4.5);
  });

  it('the accent clears 3:1 as a UI colour', () => {
    expect(contrast(t['--accent'], t['--bg'])).toBeGreaterThanOrEqual(3);
  });

  it('both diverging bar colours clear 3:1 as graphical objects', () => {
    expect(contrast(t['--above'], t['--bg'])).toBeGreaterThanOrEqual(3);
    expect(contrast(t['--below'], t['--bg'])).toBeGreaterThanOrEqual(3);
  });

  it('every team colour in use clears 3:1 as a graphical object', () => {
    for (let i = 1; i <= 5; i += 1) {
      expect(contrast(t[`--team-${i}`], t['--bg']), `--team-${i}`).toBeGreaterThanOrEqual(3);
    }
  });
});
