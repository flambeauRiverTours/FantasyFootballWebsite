import { afterEach, describe, expect, it, vi } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import { consistencyDomain, consistencyRows } from '../src/api/calculations';
import type { SeasonPayload, TeamPayload, TotalsPayload } from '../src/api/types';
import ConsistencyPanel from '../src/components/ConsistencyPanel';
import App from '../src/App';
import { payload } from './fixture';
import { serveApi } from './api';

function team(rosterId: number, name: string, consistency: Partial<TotalsPayload>): TeamPayload {
  return {
    rosterId,
    name,
    totals: {
      actual: 400,
      ideal: 450,
      sigma: null,
      cv: null,
      mean: null,
      min: null,
      max: null,
      ...consistency,
    },
    slotAverages: {},
    weeks: [],
  };
}

const steady = team(1, 'Steady', { sigma: 5, cv: 0.05, mean: 100, min: 92, max: 108 });
const wild = team(2, 'Wild', { sigma: 30, cv: 0.3, mean: 100, min: 60, max: 150 });
const middling = team(3, 'Middling', { sigma: 12, cv: 0.12, mean: 110, min: 95, max: 130 });

const season: SeasonPayload = { ...payload, finalThroughWeek: 4, teams: [wild, steady, middling] };

describe('consistencyRows', () => {
  it('ranks the steadiest team first', () => {
    expect(consistencyRows([wild, steady, middling]).map((r) => r.team.name)).toEqual([
      'Steady',
      'Middling',
      'Wild',
    ]);
  });

  it('breaks a tie in sigma by name', () => {
    const twin = team(4, 'Also steady', { sigma: 5, mean: 100, min: 92, max: 108 });
    expect(consistencyRows([steady, twin]).map((r) => r.team.name)).toEqual([
      'Also steady',
      'Steady',
    ]);
  });

  it('is empty below four finalized weeks, where sigma is null for everyone', () => {
    expect(consistencyRows(payload.teams)).toEqual([]);
  });
});

describe('consistencyDomain', () => {
  it('is symmetric about zero and wide enough for the widest team, so marks share one scale', () => {
    // wild reaches 50 above its mean of 100
    expect(consistencyDomain(consistencyRows([steady, wild]))).toEqual([-50, 50]);
  });

  it('reaches a band that passes its team’s own lowest week', () => {
    // mean 70, lowest 68: the band reaches 25 below the mean, the range only 2
    const skewed = team(5, 'Skewed', { sigma: 25, mean: 70, min: 68, max: 90 });
    expect(consistencyDomain(consistencyRows([skewed]))).toEqual([-25, 25]);
  });

  it('widens a range of identical weeks so the mark has room', () => {
    const flat = team(6, 'Flat', { sigma: 0, mean: 100, min: 100, max: 100 });
    expect(consistencyDomain(consistencyRows([flat]))).toEqual([-1, 1]);
  });
});

describe('ConsistencyPanel', () => {
  it('lists teams steadiest first, sorted by an ascending sigma column', () => {
    render(<ConsistencyPanel payload={season} rows={consistencyRows(season.teams)} />);

    const rows = within(screen.getByRole('table')).getAllByRole('row').slice(1);
    expect(rows.map((r) => within(r).getAllByRole('cell')[0]?.textContent)).toEqual([
      'Steady',
      'Middling',
      'Wild',
    ]);
    expect(screen.getByRole('columnheader', { name: 'σ' })).toHaveAttribute(
      'aria-sort',
      'ascending',
    );
  });

  it('states each range in figures, not only as a shape', () => {
    render(<ConsistencyPanel payload={season} rows={consistencyRows(season.teams)} />);

    expect(
      screen.getByRole('img', {
        name: 'Wild: lowest week 60.0, highest 150.0, mean 100.0, σ 30.0',
      }),
    ).toBeInTheDocument();
  });

  it('centres every mark on its own mean, so the ticks line up', () => {
    const { container } = render(
      <ConsistencyPanel payload={season} rows={consistencyRows(season.teams)} />,
    );

    const ticks = [...container.querySelectorAll('svg line:last-of-type')].map((l) =>
      l.getAttribute('x1'),
    );
    expect(new Set(ticks)).toEqual(new Set(['50']));
  });

  it('draws a wider band for a less steady team, on the shared scale', () => {
    const { container } = render(
      <ConsistencyPanel payload={season} rows={consistencyRows(season.teams)} />,
    );

    // rows run steady σ 5, middling σ 12, wild σ 30
    const widths = [...container.querySelectorAll('svg rect')].map((r) =>
      Number(r.getAttribute('width')),
    );
    expect(widths).toEqual([...widths].sort((a, b) => a - b));
    expect(widths[2]! / widths[0]!).toBeCloseTo(6);
  });
});

describe('The page', () => {
  afterEach(() => {
    vi.restoreAllMocks();
  });

  const serve = (body: SeasonPayload) => serveApi(body);

  it('shows the consistency panel once sigma exists', async () => {
    serve(season);
    render(<App />);

    expect(await screen.findByRole('heading', { name: 'Consistency' })).toBeInTheDocument();
  });

  it('hides it, rather than showing it empty, below four finalized weeks', async () => {
    serve(payload);
    render(<App />);

    await screen.findByRole('heading', { name: 'Standings' });
    expect(screen.queryByRole('heading', { name: 'Consistency' })).toBeNull();
  });
});
