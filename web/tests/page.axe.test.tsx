import { afterEach, describe, expect, it, vi } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import axe from 'axe-core';
import App from '../src/App';
import type { SeasonPayload } from '../src/api/types';
import { payload } from './fixture';

// the whole page in each of its states, so a panel added later is checked
// without anyone remembering to add a test for it

async function violations(container: HTMLElement) {
  const results = await axe.run(container, {
    // both need layout, which jsdom does not have
    rules: { 'color-contrast': { enabled: false }, 'target-size': { enabled: false } },
  });
  return results.violations.map((v) => `${v.id}: ${v.nodes.length} node(s)`);
}

// slot data and sigma too, so the radar and consistency panels are on the page
const withRadar: SeasonPayload = {
  ...payload,
  finalThroughWeek: 4,
  slotOrder: ['QB', 'RB'],
  leagueSlotAverages: { QB: 20, RB: 10 },
  teams: payload.teams.map((team, i) => ({
    ...team,
    totals: { ...team.totals, sigma: 10 + i, cv: 0.1, mean: 100, min: 80, max: 120 },
    slotAverages: i === 0 ? { QB: 24, RB: 5 } : { QB: 20, RB: 10 },
  })),
};

afterEach(() => {
  vi.restoreAllMocks();
});

describe('The page', () => {
  it('has no axe violations once the season loads, radar included', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      new Response(JSON.stringify(withRadar), { status: 200 }),
    );
    const { container } = render(<App />);

    // standings and consistency
    await waitFor(() => expect(screen.getAllByRole('table')).toHaveLength(2));
    expect(screen.getAllByRole('region').length).toBeGreaterThanOrEqual(4);
    expect(await violations(container)).toEqual([]);
  });

  it('has no axe violations while the season is loading', async () => {
    vi.spyOn(globalThis, 'fetch').mockReturnValue(new Promise<Response>(() => {}));
    const { container } = render(<App />);

    expect(container.querySelector('[aria-busy="true"]')).not.toBeNull();
    expect(await violations(container)).toEqual([]);
  });

  it('has no axe violations when the season fails to load', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response('nope', { status: 500 }));
    const { container } = render(<App />);

    await screen.findByRole('alert');
    expect(await violations(container)).toEqual([]);
  });
});
