import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { teamColours } from '../src/api/teamColours';
import ChartPanel from '../src/components/ChartPanel';
import RadarPanel from '../src/components/RadarPanel';
import type { SeasonPayload } from '../src/api/types';
import { payload, teams } from './fixture';

describe('teamColours', () => {
  it('assigns colours in roster id order, whatever order the teams arrive in', () => {
    const colours = teamColours([...teams].reverse());
    expect([...colours.entries()]).toEqual([
      [1, 'var(--team-1)'],
      [2, 'var(--team-2)'],
      [3, 'var(--team-3)'],
    ]);
  });

  it('keeps a team its colour when it is overtaken', () => {
    const overtaken = teams.map((t) =>
      t.rosterId === 3 ? { ...t, totals: { ...t.totals, actual: 999 } } : t,
    );
    expect(teamColours(overtaken).get(3)).toBe(teamColours(teams).get(3));
  });
});

describe('a team has one colour everywhere', () => {
  // Charlie leads on points but has the highest roster id, so a colour taken
  // from rank in one panel and from list order in another would disagree
  const charlieLeads: SeasonPayload = {
    ...payload,
    slotOrder: ['QB'],
    leagueSlotAverages: { QB: 10 },
    teams: payload.teams
      .map((t) => ({
        ...t,
        slotAverages: { QB: 10 },
        totals: { ...t.totals, actual: t.name === 'Charlie' ? 999 : t.totals.actual },
      }))
      .sort((a, b) => b.totals.actual - a.totals.actual),
  };

  const chipColours = () =>
    Object.fromEntries(
      screen
        .getAllByRole('button')
        .filter((b) => b.classList.contains('chip'))
        .map((b) => [b.textContent, b.style.getPropertyValue('--chip')]),
    );

  it('matches between the chart and the radar', () => {
    const chart = render(<ChartPanel payload={charlieLeads} />);
    const inChart = chipColours();
    chart.unmount();

    render(<RadarPanel payload={charlieLeads} />);
    expect(chipColours()).toEqual(inChart);
    expect(inChart['Charlie']).toBe('var(--team-3)');
  });
});
