import { describe, expect, it } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import axe from 'axe-core';
import StandingsPanel from '../src/components/StandingsPanel';
import ChartPanel from '../src/components/ChartPanel';
import Panel from '../src/components/Panel';
import { payload, teams } from './fixture';

async function violations(container: HTMLElement) {
  const results = await axe.run(container, {
    // both need layout, which jsdom does not have
    rules: { 'color-contrast': { enabled: false }, 'target-size': { enabled: false } },
  });
  return results.violations.map((v) => `${v.id}: ${v.nodes.length} node(s)`);
}

describe('Panel', () => {
  it('labels the region with its own heading', () => {
    render(
      <Panel id="demo" title="Demo Panel">
        <p>body</p>
      </Panel>,
    );
    const region = screen.getByRole('region', { name: 'Demo Panel' });
    expect(region).toHaveAttribute('id', 'demo');
    expect(within(region).getByRole('heading', { level: 2 })).toHaveAttribute('id', 'demo-h');
  });

  it('gives each panel a unique heading id', () => {
    const { container } = render(
      <>
        <Panel id="a" title="A">
          <p>a</p>
        </Panel>
        <Panel id="b" title="B">
          <p>b</p>
        </Panel>
      </>,
    );
    const ids = [...container.querySelectorAll('h2')].map((h) => h.id);
    expect(new Set(ids).size).toBe(ids.length);
  });
});

describe('StandingsPanel', () => {
  it('marks the sorted column with aria-sort', () => {
    render(<StandingsPanel teams={teams} />);
    const sorted = screen.getAllByRole('columnheader').filter((h) => h.hasAttribute('aria-sort'));
    expect(sorted).toHaveLength(1);
    expect(sorted[0]).toHaveTextContent('Points');
    expect(sorted[0]).toHaveAttribute('aria-sort', 'descending');
  });

  it('tags the gap column on the header and every body cell', () => {
    const { container } = render(<StandingsPanel teams={teams} />);
    expect(container.querySelectorAll('th.col-gap')).toHaveLength(1);
    expect(container.querySelectorAll('td.col-gap')).toHaveLength(teams.length);
  });

  it('has no sigma column, even once every team has a sigma', () => {
    const withSigma = teams.map((t) => ({
      ...t,
      totals: { ...t.totals, sigma: 12.5, cv: 0.1, mean: 120, min: 100, max: 140 },
    }));
    render(<StandingsPanel teams={withSigma} />);
    const headers = screen.getAllByRole('columnheader').map((h) => h.textContent);
    expect(headers).toEqual(['Team', 'Points', 'Gap', 'Ideal']);
  });

  it('puts gap next to points, then ideal', () => {
    render(<StandingsPanel teams={teams} />);
    const headers = screen.getAllByRole('columnheader').map((h) => h.textContent);
    expect(headers.slice(0, 4)).toEqual(['Team', 'Points', 'Gap', 'Ideal']);
  });

  it('puts the table in its own scroll container', () => {
    const { container } = render(<StandingsPanel teams={teams} />);
    expect(container.querySelector('.table-wrap > table')).not.toBeNull();
  });

  it('renders an em dash, never a zero, for the leader gap', () => {
    render(<StandingsPanel teams={teams} />);
    const rows = screen.getAllByRole('row').slice(1);
    const gap = (row: HTMLElement) => row.querySelector('td.col-gap');
    expect(gap(rows[0])).toHaveTextContent('—');
    expect(gap(rows[1])).toHaveTextContent('−');
  });

  it('keeps one heading and no table when there is nothing to show', () => {
    const { container } = render(<StandingsPanel teams={[]} />);
    expect(container.querySelectorAll('h2')).toHaveLength(1);
    expect(container.querySelector('table')).toBeNull();
    expect(screen.getByRole('region', { name: 'Standings' })).toBeInTheDocument();
  });

  it('has no axe violations', async () => {
    const { container } = render(<StandingsPanel teams={teams} />);
    expect(await violations(container)).toEqual([]);
  });
});

describe('ChartPanel', () => {
  it('gives every control an accessible name and a pressed state', () => {
    render(<ChartPanel payload={payload} />);
    const chips = within(screen.getByRole('group', { name: /teams/i })).getAllByRole('button');
    expect(chips.map((c) => c.textContent)).toEqual(['Alpha', 'Bravo', 'Charlie']);
    for (const chip of chips) expect(chip).toHaveAttribute('aria-pressed', 'true');

    // the fixture has two finalized weeks: a sigma line needs five, so it is
    // not offered — see the sigma/vs-median availability tests below
    const metric = within(screen.getByRole('group', { name: /metric/i })).getAllByRole('button');
    expect(metric.map((b) => b.textContent)).toEqual(['Points', 'Ideal']);
    expect(metric.map((b) => b.getAttribute('aria-pressed'))).toEqual(['true', 'false']);

    expect(screen.getByRole('group', { name: /^mode$/i })).toBeInTheDocument();
    expect(screen.queryByRole('group', { name: /window/i })).toBeNull();
  });

  it('hides the sigma metric below five finalized weeks and shows it at five', () => {
    // at four the first sigma exists (D1), but it is one point and a line needs two
    const { rerender } = render(<ChartPanel payload={{ ...payload, finalThroughWeek: 4 }} />);
    expect(within(screen.getByRole('group', { name: /metric/i })).queryByText('σ')).toBeNull();

    rerender(<ChartPanel payload={{ ...payload, finalThroughWeek: 5 }} />);
    expect(within(screen.getByRole('group', { name: /metric/i })).getByText('σ')).toBeInTheDocument();
  });

  it('draws a sigma line, not the empty message, as soon as sigma is offered', () => {
    // five finalized weeks: expanding sigma has points at weeks 4 and 5
    const scores = [100, 140, 90, 160, 120];
    const fiveWeeks = {
      ...payload,
      finalThroughWeek: 5,
      teams: payload.teams.map((t, i) => ({
        ...t,
        weeks: scores.map((s, w) => ({ week: w + 1, actual: s + i * 7, ideal: s + 20, isFinal: true })),
      })),
    };
    window.history.replaceState(null, '', '?metric=sigma');
    try {
      const { container } = render(<ChartPanel payload={fiveWeeks} />);
      // without sigma on offer the chart would fall back to points and still
      // draw three lines, so check these are the sigma ones
      expect(screen.getByRole('img').getAttribute('aria-label')).toMatch(/^Consistency sigma/);
      expect(container.querySelectorAll('path.series')).toHaveLength(3);
      expect(screen.queryByText(/Stay tuned/)).toBeNull();
    } finally {
      window.history.replaceState(null, '', window.location.pathname);
    }
  });

  it('hides vs median below two finalized weeks and shows it at two', () => {
    // one final week is a real median but only one point, and isPlottable
    // refuses to draw a line through a single point
    const { rerender } = render(<ChartPanel payload={{ ...payload, finalThroughWeek: 1 }} />);
    expect(within(screen.getByRole('group', { name: /^mode$/i })).queryByText('vs median')).toBeNull();

    rerender(<ChartPanel payload={{ ...payload, finalThroughWeek: 2 }} />);
    expect(within(screen.getByRole('group', { name: /^mode$/i })).getByText('vs median')).toBeInTheDocument();
  });

  it('uses real buttons, so Enter and Space work without a key handler', () => {
    const { container } = render(<ChartPanel payload={payload} />);
    for (const b of container.querySelectorAll('.chip, .segmented button')) {
      expect(b.tagName).toBe('BUTTON');
      expect(b).toHaveAttribute('type', 'button');
    }
  });

  it('keeps the controls and the measured wrapper when there is nothing to plot', () => {
    const noWeeks = {
      ...payload,
      teams: payload.teams.map((t) => ({ ...t, weeks: [] })),
    };
    const { container } = render(<ChartPanel payload={noWeeks} />);
    expect(container.querySelector('svg.chart')).toBeNull();
    expect(container.querySelector('.chart-wrap')).not.toBeNull();
    expect(container.querySelectorAll('.chip')).toHaveLength(3);
  });

  it('plots the current week for every team as soon as any team has scored', () => {
    const withWeek3 = (alphaScore: number) => ({
      ...payload,
      teams: payload.teams.map((t) => ({
        ...t,
        weeks: [
          ...t.weeks,
          { week: 3, actual: t.name === 'Alpha' ? alphaScore : 0, ideal: 0, isFinal: false },
        ],
      })),
    });
    const pointsPerLine = (c: HTMLElement) =>
      [...c.querySelectorAll('path.series')].map((p) => (p.getAttribute('d') ?? '').split('L').length);

    const before = render(<ChartPanel payload={withWeek3(0)} />);
    expect(pointsPerLine(before.container)).toEqual([2, 2, 2]);
    before.unmount();

    const after = render(<ChartPanel payload={withWeek3(12.4)} />);
    expect(pointsPerLine(after.container)).toEqual([3, 3, 3]);
  });

  it('describes the chart for a screen reader', () => {
    render(<ChartPanel payload={payload} />);
    const img = screen.getByRole('img');
    expect(img.getAttribute('aria-label')).toMatch(/Alpha, Bravo, Charlie/);
  });

  it('has no axe violations', async () => {
    const { container } = render(<ChartPanel payload={payload} />);
    expect(await violations(container)).toEqual([]);
  });
});
