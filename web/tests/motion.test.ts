import { afterEach, describe, expect, it, vi } from 'vitest';
import { domAnimation } from 'motion/react';
import { loadLayout } from '../src/components/motion';

describe('the row slide', () => {
  afterEach(() => {
    vi.doUnmock('../src/components/layoutFeatures');
    vi.resetModules();
  });

  // why the extra chunk exists: the tables' m.tr layout is inert on domAnimation alone (T74)
  it('needs a layout feature that the up-front features lack', async () => {
    expect(domAnimation).not.toHaveProperty('layout');
    expect(await loadLayout()).toHaveProperty('layout');
  });

  it('falls back to the up-front features when the chunk fails to load', async () => {
    vi.resetModules();
    vi.doMock('../src/components/layoutFeatures', () => {
      throw new Error('chunk failed');
    });
    const { loadLayout: failing } = await import('../src/components/motion');
    const { domAnimation: upFront } = await import('motion/react');

    expect(await failing()).toBe(upFront);
  });
});
