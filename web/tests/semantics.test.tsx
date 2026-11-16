import { describe, expect, it } from 'vitest';
import { fireEvent, render, screen, within } from '@testing-library/react';
import StandingsPanel from '../src/components/StandingsPanel';
import ChartPanel from '../src/components/ChartPanel';
import Panel from '../src/components/Panel';
import { headToHead, payload, teams } from './fixture';

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
    render(<StandingsPanel payload={payload} />);
    const sorted = screen.getAllByRole('columnheader').filter((h) => h.hasAttribute('aria-sort'));
    expect(sorted).toHaveLength(1);
    expect(sorted[0]).toHaveTextContent('Points');
    expect(sorted[0]).toHaveAttribute('aria-sort', 'descending');
  });

  it('hides the gap column on narrow screens, on the header and every body cell', () => {
    const { container } = render(<StandingsPanel payload={payload} />);
    expect(container.querySelectorAll('th.col-wide')).toHaveLength(1);
    expect(container.querySelectorAll('td.col-wide')).toHaveLength(teams.length);
  });

  it('puts gap next to points, then ideal, with no sigma column even once every team has one', () => {
    const withSigma = teams.map((t) => ({
      ...t,
      totals: { ...t.totals, sigma: 12.5, cv: 0.1, mean: 120, min: 100, max: 140 },
    }));
    render(<StandingsPanel payload={{ ...payload, teams: withSigma }} />);
    const headers = screen.getAllByRole('columnheader').map((h) => h.textContent);
    expect(headers).toEqual(['Team', 'Points', 'Gap', 'Ideal']);
  });

  it('puts the table in its own scroll container', () => {
    const { container } = render(<StandingsPanel payload={payload} />);
    expect(container.querySelector('.table-wrap > table')).not.toBeNull();
  });

  it('renders an em dash, never a zero, for the leader gap, muted', () => {
    render(<StandingsPanel payload={payload} />);
    const rows = screen.getAllByRole('row').slice(1);
    const gap = (row: HTMLElement) => row.querySelector('td.col-wide.muted');
    expect(gap(rows[0])).toHaveTextContent('—');
    expect(gap(rows[1])).toHaveTextContent('−');
  });

  it('keeps one heading and no table when there is nothing to show', () => {
    const { container } = render(<StandingsPanel payload={{ ...payload, teams: [] }} />);
    expect(container.querySelectorAll('h2')).toHaveLength(1);
    expect(container.querySelector('table')).toBeNull();
    expect(screen.getByRole('region', { name: 'Standings' })).toBeInTheDocument();
  });
});

describe('StandingsPanel in head-to-head', () => {
  // the visible labels, first text only: PF and PA also carry their full names for
  // screen readers, and the record its "games · median" line
  const labels = (c: HTMLElement) =>
    [...c.querySelectorAll('thead th')].map((th) => th.firstChild?.textContent);
  const bodyRows = () => screen.getAllByRole('row').slice(1);
  const recordShown = (row: HTMLElement) =>
    row.querySelectorAll('td')[2].querySelector('[aria-hidden="true"]')?.textContent;

  it('shows rank, record, games back, points for and against, and ideal, sorted by record', () => {
    const { container } = render(<StandingsPanel payload={headToHead} />);
    expect(labels(container)).toEqual(['#', 'Team', 'Record', 'GB', 'PF', 'PA', 'Ideal']);
    expect(screen.getByRole('columnheader', { name: 'Rank' })).toBeInTheDocument();
    expect(screen.getByRole('columnheader', { name: 'Games back' })).toBeInTheDocument();
    expect(screen.getByRole('columnheader', { name: 'Points for' })).toBeInTheDocument();
    expect(screen.getByRole('columnheader', { name: 'Points against' })).toBeInTheDocument();

    const sorted = screen.getAllByRole('columnheader').filter((h) => h.hasAttribute('aria-sort'));
    expect(sorted).toHaveLength(1);
    expect(sorted[0]).toHaveTextContent('Record');
    expect(container.querySelectorAll('td.is-sorted')).toHaveLength(teams.length);
    const cells = (i: number) => bodyRows().map((r) => r.querySelectorAll('td')[i].textContent);
    expect(cells(0)).toEqual(['1', '2', '3']);
    expect(cells(1)).toEqual(['Charlie', 'Bravo', 'Alpha']);
    expect(cells(3)).toEqual(['—', '1.0', '2.0']);
  });

  it('marks the rank as a lead column, so the team column stays the wide one', () => {
    const { container } = render(<StandingsPanel payload={headToHead} />);
    expect(container.querySelector('thead th')).toHaveClass('col-lead');
    expect(container.querySelectorAll('td.col-lead')).toHaveLength(teams.length);
    expect(container.querySelectorAll('.col-lead')).toHaveLength(teams.length + 1);
  });

  it('hides points against and ideal on narrow screens, keeping games back', () => {
    const { container } = render(<StandingsPanel payload={headToHead} />);
    const wide = [...container.querySelectorAll('th.col-wide')];
    expect(wide.map((th) => th.textContent)).toEqual(
      expect.arrayContaining([expect.stringContaining('PA'), 'Ideal']),
    );
    expect(wide).toHaveLength(2);
    expect(container.querySelectorAll('td.col-wide')).toHaveLength(2 * teams.length);
  });

  it('reads the record short on screen and in full to a screen reader', () => {
    render(<StandingsPanel payload={headToHead} />);
    const cell = bodyRows()[0].querySelectorAll('td')[2];
    expect(cell.querySelector('[aria-hidden="true"]')).toHaveTextContent('3-1');
    expect(cell.querySelector('.visually-hidden')).toHaveTextContent('3 wins, 1 loss');
  });

  it('splits each record into games and the median game, labelled in the header', () => {
    const { container } = render(<StandingsPanel payload={headToHead} />);
    const cell = bodyRows()[0].querySelectorAll('td')[2];
    expect(cell.querySelector('.record-split')).toHaveTextContent('2-0 · 1-1');
    expect(cell.querySelector('.visually-hidden')).toHaveTextContent(
      '3 wins, 1 loss: 2 wins, 0 losses in games, 1 win, 1 loss against the median',
    );
    expect(container.querySelector('thead .record-split')).toHaveTextContent('games · median');
    expect(screen.getByRole('columnheader', { name: 'Record' })).toBeInTheDocument();
  });

  it('shows ties in the games share only, never in the median share', () => {
    const tied = {
      ...headToHead,
      teams: headToHead.teams.map((t, i) =>
        i === 0 ? { ...t, record: { ...t.record!, losses: 2, ties: 1 } } : t,
      ),
    };
    render(<StandingsPanel payload={tied} />);
    const cell = bodyRows()[0].querySelectorAll('td')[2];
    expect(cell.querySelector('.record-split')).toHaveTextContent('2-0-0 · 1-1');
  });

  it('shows no split in a league without a median game', () => {
    const plain = {
      ...headToHead,
      teams: headToHead.teams.map((t) => ({ ...t, record: { ...t.record!, median: undefined } })),
    };
    const { container } = render(<StandingsPanel payload={plain} />);
    expect(container.querySelector('.record-split')).toBeNull();
  });

  it('shows ties on every row once any team has one', () => {
    const tied = {
      ...headToHead,
      teams: headToHead.teams.map((t, i) =>
        i === 0 ? { ...t, record: { ...t.record!, losses: 2, ties: 1 } } : t,
      ),
    };
    render(<StandingsPanel payload={tied} />);
    expect(bodyRows().map(recordShown)).toEqual(['3-1-0', '2-2-0', '1-2-1']);
  });

  it('counts settled weeks only while a week is live', () => {
    // Charlie is mid-game in week 3: the record's points and the settled ideal stand
    const live = {
      ...headToHead,
      teams: headToHead.teams.map((t) =>
        t.name === 'Charlie'
          ? { ...t, weeks: [...t.weeks, { week: 3, actual: 90, ideal: 99, isFinal: false }] }
          : t,
      ),
    };
    render(<StandingsPanel payload={live} />);
    const cells = [...bodyRows()[0].querySelectorAll('td')].map((td) => td.textContent);
    expect(cells.slice(4)).toEqual(['250.7', '280.9', '310.9']);
  });

  it('keeps the points table, and says why, until every record is known', () => {
    const waiting = { ...headToHead, isCurrent: false, teams };
    const { container } = render(<StandingsPanel payload={waiting} />);
    expect(labels(container)).toEqual(['Team', 'Points', 'Gap', 'Ideal']);
    expect(screen.getByText(/records are still loading/i)).toHaveTextContent(/ranked by points/i);
  });

  it('says nothing about records in total points', () => {
    render(<StandingsPanel payload={payload} />);
    expect(screen.queryByText(/still loading/i)).toBeNull();
  });

  it('takes the records when they arrive between polls', () => {
    const { container, rerender } = render(<StandingsPanel payload={{ ...headToHead, teams }} />);
    rerender(<StandingsPanel payload={headToHead} />);
    expect(labels(container)).toEqual(['#', 'Team', 'Record', 'GB', 'PF', 'PA', 'Ideal']);
    expect(container.querySelectorAll('tbody tr:first-child td')).toHaveLength(7);
    expect(screen.queryByText(/still loading/i)).toBeNull();
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

  it('orders the chips by record in head-to-head', () => {
    render(<ChartPanel payload={headToHead} />);
    const chips = within(screen.getByRole('group', { name: /teams/i })).getAllByRole('button');
    expect(chips.map((c) => c.textContent)).toEqual(['Charlie', 'Bravo', 'Alpha']);
  });

  it('hides the sigma metric below five finalized weeks and shows it at five', () => {
    // at four the first sigma exists (D1), but it is one point and a line needs two
    const { rerender } = render(<ChartPanel payload={{ ...payload, finalThroughWeek: 4 }} />);
    expect(within(screen.getByRole('group', { name: /metric/i })).queryByText('σ')).toBeNull();

    rerender(<ChartPanel payload={{ ...payload, finalThroughWeek: 5 }} />);
    expect(
      within(screen.getByRole('group', { name: /metric/i })).getByText('σ'),
    ).toBeInTheDocument();
  });

  it('draws a sigma line, not the empty message, as soon as sigma is offered', () => {
    // five finalized weeks: expanding sigma has points at weeks 4 and 5
    const scores = [100, 140, 90, 160, 120];
    const fiveWeeks = {
      ...payload,
      finalThroughWeek: 5,
      teams: payload.teams.map((t, i) => ({
        ...t,
        weeks: scores.map((s, w) => ({
          week: w + 1,
          actual: s + i * 7,
          ideal: s + 20,
          isFinal: true,
        })),
      })),
    };
    window.history.replaceState(null, '', '?metric=sigma');
    const { container } = render(<ChartPanel payload={fiveWeeks} />);
    // without sigma on offer the chart would fall back to points and still
    // draw three lines, so check these are the sigma ones
    expect(screen.getByRole('img').getAttribute('aria-label')).toMatch(/^Consistency sigma/);
    expect(container.querySelectorAll('path.series')).toHaveLength(3);
    expect(screen.queryByText(/Stay tuned/)).toBeNull();
  });

  it('hides vs median below two finalized weeks and shows it at two', () => {
    // one final week is a real median but only one point, and isPlottable
    // refuses to draw a line through a single point
    const { rerender } = render(<ChartPanel payload={{ ...payload, finalThroughWeek: 1 }} />);
    expect(
      within(screen.getByRole('group', { name: /^mode$/i })).queryByText('vs median'),
    ).toBeNull();

    rerender(<ChartPanel payload={{ ...payload, finalThroughWeek: 2 }} />);
    expect(
      within(screen.getByRole('group', { name: /^mode$/i })).getByText('vs median'),
    ).toBeInTheDocument();
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
      [...c.querySelectorAll('path.series')].map(
        (p) => (p.getAttribute('d') ?? '').split('L').length,
      );

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

  it('names what each mode plots', () => {
    // weeks that differ, so every mode has a line to draw
    const varied = {
      ...payload,
      teams: payload.teams.map((team, i) => ({
        ...team,
        weeks: team.weeks.map((w) => ({ ...w, actual: w.actual + w.week * (i + 1) * 10 })),
      })),
    };
    render(<ChartPanel payload={varied} />);
    const label = () => screen.getByRole('img').getAttribute('aria-label');

    expect(label()).toMatch(/^Cumulative points by week/);
    fireEvent.click(screen.getByRole('button', { name: 'Per week' }));
    expect(label()).toMatch(/^Weekly points by week/);
    fireEvent.click(screen.getByRole('button', { name: 'vs median' }));
    expect(label()).toMatch(/^Cumulative points above the league median by week/);
  });

  it('treats a mode in the URL that is not one of its own as the default', () => {
    // the code scanning alert: nothing is looked up or called by a name taken from the URL
    window.history.replaceState(null, '', '/?mode=constructor');
    render(<ChartPanel payload={payload} />);
    expect(screen.getByRole('img').getAttribute('aria-label')).toMatch(
      /^Cumulative points by week/,
    );
  });

  it('states the highest and lowest values at the latest week, not just what is drawn', () => {
    render(<ChartPanel payload={payload} />);
    expect(screen.getByRole('img').getAttribute('aria-label')).toMatch(
      /At week 2, highest Alpha at 340\.2, lowest Charlie at 250\.7\./,
    );
  });
});
