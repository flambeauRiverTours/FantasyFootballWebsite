import { describe, expect, it } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import axe from 'axe-core';
import StandingsPanel from '../src/components/StandingsPanel';
import type { SeasonPayload } from '../src/api/types';
import { headToHead } from './fixture';

// T72b: the standings grouped by division. North is Alpha (1-3) and Charlie (3-1); South is
// Bravo (2-2) alone, a game behind Charlie league-wide but leading its own division
const grouped: SeasonPayload = {
  ...headToHead,
  divisions: [
    { number: 1, name: 'North' },
    { number: 2, name: 'South' },
  ],
  teams: headToHead.teams.map((team) => ({ ...team, division: team.rosterId === 2 ? 2 : 1 })),
};

const show = (shown: SeasonPayload) => render(<StandingsPanel payload={shown} />);
const row = (name: string) => screen.getByRole('row', { name: new RegExp(name) });
// a cell by its column's header, so the test survives a column added in between
function cell(name: string, header: RegExp): string | null {
  const headers = screen.getAllByRole('columnheader').map((h) => h.textContent ?? '');
  return within(row(name)).getAllByRole('cell')[headers.findIndex((h) => header.test(h))]!
    .textContent;
}

describe('Division standings', () => {
  it('opens each division with a header row spanning the table, in division order', () => {
    const { container } = show(grouped);
    const heads = screen.getAllByRole('rowheader');
    expect(heads.map((h) => h.textContent)).toEqual(['North', 'South']);
    expect(heads[0]).toHaveAttribute('scope', 'rowgroup');
    expect(heads[0]).toHaveAttribute('colspan', String(screen.getAllByRole('columnheader').length));
    // a header row of its own, never a team row
    expect(heads[0]!.closest('tr')).toHaveClass('division-head');
    expect(container.querySelectorAll('tbody')).toHaveLength(2);
  });

  it('ranks and measures games back within each division', () => {
    show(grouped);
    expect(cell('Charlie', /#/)).toBe('1');
    expect(cell('Alpha', /#/)).toBe('2');
    expect(cell('Alpha', /GB/)).toBe('2.0');
    // league-wide Bravo is a game behind Charlie; in the South it leads
    expect(cell('Bravo', /#/)).toBe('1');
    expect(cell('Bravo', /GB/)).toBe('—');
  });

  it('shows a tie inside a division as T1', () => {
    const level = headToHead.teams[2]!.record!;
    show({
      ...grouped,
      teams: grouped.teams.map((t) => (t.rosterId === 1 ? { ...t, record: level } : t)),
    });
    // shown "T1", spoken "tied for 1"
    expect(cell('Alpha', /#/)).toBe('T1tied for 1');
    expect(cell('Charlie', /#/)).toBe('T1tied for 1');
    expect(cell('Bravo', /#/)).toBe('1');
  });

  it("crowns the league's champion, not each division's leader", () => {
    show({ ...grouped, champions: [3] });
    expect(row('Charlie')).toHaveClass('is-champion');
    expect(row('Bravo')).not.toHaveClass('is-champion');
  });

  it('is one table without divisions', () => {
    show(headToHead);
    expect(screen.queryByRole('rowheader')).toBeNull();
  });

  it('is one table while the records are still loading', () => {
    // the points table stands in; a division has no meaning without records
    show({ ...grouped, teams: grouped.teams.map(({ record: _, ...team }) => team) });
    expect(screen.queryByRole('rowheader')).toBeNull();
  });

  it('has no axe violations', async () => {
    const { container } = show(grouped);
    const results = await axe.run(container, {
      rules: { 'color-contrast': { enabled: false }, 'target-size': { enabled: false } },
    });
    expect(results.violations.map((v) => v.id)).toEqual([]);
  });
});
