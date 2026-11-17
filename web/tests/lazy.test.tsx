import { afterEach, describe, expect, it, vi } from 'vitest';
import { act, render, screen } from '@testing-library/react';
import type { SeasonPayload } from '../src/api/types';
import { payload } from './fixture';
import { serveApi } from './api';

// slot data, so the radar would be on the page
const withRadar: SeasonPayload = {
  ...payload,
  slotOrder: ['QB', 'RB'],
  leagueSlotAverages: { QB: 20, RB: 10 },
};

describe('a chunk that fails to load', () => {
  afterEach(() => {
    vi.doUnmock('../src/components/RadarPanel');
    vi.resetModules();
    vi.restoreAllMocks();
  });

  // the radar loads as its own chunk (T74); a stale page after a deploy may not find it.
  // A long timeout: after resetModules the whole App graph is imported afresh, which under the
  // full suite's parallel load can take longer than the 5 s default
  it('leaves the page without the radar, not broken', { timeout: 20_000 }, async () => {
    vi.resetModules();
    vi.doMock('../src/components/RadarPanel', () => {
      throw new Error('chunk failed');
    });
    const { default: App } = await import('../src/App');
    serveApi(withRadar);
    render(<App />);

    await screen.findByRole('region', { name: 'Standings' }, { timeout: 10_000 });
    // let the failed import settle and React render what follows it
    await act(() => import('../src/components/RadarPanel').catch(() => undefined));

    expect(screen.getByRole('region', { name: 'Standings' })).toBeInTheDocument();
    expect(screen.queryByRole('region', { name: 'Positional Radar' })).toBeNull();
  });
});
