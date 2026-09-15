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
    render(<StandingsPanel teams={teams} finalThroughWeek={2} />);
    const sorted = screen.getAllByRole('columnheader').filter((h) => h.hasAttribute('aria-sort'));
    expect(sorted).toHaveLength(1);
    expect(sorted[0]).toHaveTextContent('Points');
    expect(sorted[0]).toHaveAttribute('aria-sort', 'descending');
  });

  it('tags the ideal column on the header and every body cell', () => {
    const { container } = render(<StandingsPanel teams={teams} finalThroughWeek={2} />);
    expect(container.querySelectorAll('th.col-ideal')).toHaveLength(1);
    expect(container.querySelectorAll('td.col-ideal')).toHaveLength(teams.length);
  });

  it('puts the table in its own scroll container', () => {
    const { container } = render(<StandingsPanel teams={teams} finalThroughWeek={2} />);
    expect(container.querySelector('.table-wrap > table')).not.toBeNull();
  });

  it('renders an em dash, never a zero, for the leader gap', () => {
    render(<StandingsPanel teams={teams} finalThroughWeek={2} />);
    const rows = screen.getAllByRole('row').slice(1);
    const cells = within(rows[0]).getAllByRole('cell');
    expect(cells.at(-1)).toHaveTextContent('—');
    expect(within(rows[1]).getAllByRole('cell').at(-1)).toHaveTextContent('−');
  });

  it('keeps one heading per panel when there is nothing to show', () => {
    const { container } = render(<StandingsPanel teams={[]} finalThroughWeek={0} />);
    expect(container.querySelectorAll('h2')).toHaveLength(1);
    expect(screen.getByText(/no teams/i).tagName).toBe('P');
  });

  it('has no axe violations', async () => {
    const { container } = render(<StandingsPanel teams={teams} finalThroughWeek={2} />);
    expect(await violations(container)).toEqual([]);
  });
});

describe('ChartPanel', () => {
  it('gives every control an accessible name and a pressed state', () => {
    render(<ChartPanel payload={payload} />);
    const chips = within(screen.getByRole('group', { name: /teams/i })).getAllByRole('button');
    expect(chips.map((c) => c.textContent)).toEqual(['Alpha', 'Bravo', 'Charlie']);
    for (const chip of chips) expect(chip).toHaveAttribute('aria-pressed', 'true');

    const metric = within(screen.getByRole('group', { name: /metric/i })).getAllByRole('button');
    expect(metric.map((b) => b.getAttribute('aria-pressed'))).toEqual(['true', 'false']);
  });

  it('uses real buttons, so Enter and Space work without a key handler', () => {
    const { container } = render(<ChartPanel payload={payload} />);
    for (const b of container.querySelectorAll('.chip, .segmented button')) {
      expect(b.tagName).toBe('BUTTON');
      expect(b).toHaveAttribute('type', 'button');
    }
  });

  it('keeps the controls and the measured wrapper when there is nothing to plot', () => {
    const flat = {
      ...payload,
      teams: payload.teams.map((t) => ({
        ...t,
        weeks: t.weeks.map((w) => ({ ...w, actual: 0, ideal: 0 })),
      })),
    };
    const { container } = render(<ChartPanel payload={flat} />);
    expect(container.querySelector('svg.chart')).toBeNull();
    expect(container.querySelector('.chart-wrap')).not.toBeNull();
    expect(container.querySelectorAll('.chip')).toHaveLength(3);
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
