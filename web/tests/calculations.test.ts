import { describe, expect, it } from 'vitest';
import type { TeamPayload, WeekPayload } from '../src/api/types';
import {
  canPlotMedian,
  canPlotSigma,
  cumulative,
  hasMovement,
  latestExtremes,
  perWeek,
  populationSigma,
  rankByPoints,
  sigmaSeries,
  slotDeviations,
  slotRatios,
  standings,
  startedWeeks,
  vsMedian,
  weeklyMedians,
} from '../src/api/calculations';
import { payload } from './fixture';

function weeks(...rows: [number, number, number, boolean][]): WeekPayload[] {
  return rows.map(([week, actual, ideal, isFinal]) => ({ week, actual, ideal, isFinal }));
}

function team(rosterId: number, w: WeekPayload[]): TeamPayload {
  return {
    rosterId,
    name: `Team ${rosterId}`,
    totals: { actual: 0, ideal: 0, sigma: null, cv: null, mean: null, min: null, max: null },
    slotAverages: {},
    weeks: w,
  };
}

describe('cumulative', () => {
  it('runs a total across weeks', () => {
    const points = cumulative(weeks([1, 100, 120, true], [2, 50, 60, true]));
    expect(points.map((p) => p.value)).toEqual([100, 150]);
  });

  it('sums the ideal when asked', () => {
    const points = cumulative(weeks([1, 100, 120, true], [2, 50, 60, true]), 'ideal');
    expect(points.map((p) => p.value)).toEqual([120, 180]);
  });

  it('sorts before summing and does not reorder the input', () => {
    const input = weeks([2, 50, 60, true], [1, 100, 120, true]);
    const points = cumulative(input);
    expect(points.map((p) => p.week)).toEqual([1, 2]);
    expect(input.map((w) => w.week)).toEqual([2, 1]);
  });
});

describe('perWeek', () => {
  it('returns the weekly values untouched', () => {
    expect(perWeek(weeks([1, 100, 120, true], [2, 50, 60, true])).map((p) => p.value)).toEqual([
      100, 50,
    ]);
  });
});

describe('startedWeeks', () => {
  it('leaves out the current week until anyone has scored', () => {
    const teams = [
      team(1, weeks([1, 100, 120, true], [2, 0, 0, false])),
      team(2, weeks([1, 90, 110, true], [2, 0, 0, false])),
    ];
    expect([...startedWeeks(teams)]).toEqual([1]);
  });

  it('includes the current week for every team once one team has scored', () => {
    const teams = [
      team(1, weeks([1, 100, 120, true], [2, 12.4, 20, false])),
      team(2, weeks([1, 90, 110, true], [2, 0, 0, false])),
    ];
    expect([...startedWeeks(teams)].sort()).toEqual([1, 2]);
  });

  it('keeps a finished week even if everyone scored zero', () => {
    const teams = [team(1, weeks([1, 0, 0, true])), team(2, weeks([1, 0, 0, true]))];
    expect([...startedWeeks(teams)]).toEqual([1]);
  });
});

describe('weeklyMedians', () => {
  it('ignores weeks nobody has played', () => {
    const teams = [
      team(1, weeks([1, 100, 0, true], [2, 0, 0, false])),
      team(2, weeks([1, 200, 0, true], [2, 0, 0, false])),
      team(3, weeks([1, 300, 0, true], [2, 0, 0, false])),
    ];
    const medians = weeklyMedians(teams);
    expect(medians.get(1)).toBe(200);
    expect(medians.has(2)).toBe(false);
  });

  it('is not dragged down by a half played week', () => {
    const played = weeks([1, 150, 0, true]);
    const teams = [team(1, played), team(2, played), team(3, weeks([1, 0, 0, false]))];
    expect(weeklyMedians(teams).get(1)).toBe(150);
  });
});

describe('vsMedian', () => {
  it('accumulates the signed difference from the league', () => {
    const medians = new Map([
      [1, 100],
      [2, 100],
    ]);
    const points = vsMedian(weeks([1, 120, 0, true], [2, 90, 0, true]), medians);
    expect(points.map((p) => p.value)).toEqual([20, 10]);
  });

  it('produces negatives, which the chart must not clip', () => {
    const medians = new Map([[1, 100]]);
    expect(vsMedian(weeks([1, 60, 0, true]), medians)[0].value).toBe(-40);
  });

  it('drops weeks with no league figure', () => {
    expect(vsMedian(weeks([1, 120, 0, true]), new Map())).toEqual([]);
  });
});

describe('populationSigma', () => {
  it('returns null below four values', () => {
    expect(populationSigma([1, 2, 3])).toBeNull();
  });

  it('divides by n and not by n minus one', () => {
    // mean 5, squared deviations 9 1 1 9, population variance 5
    expect(populationSigma([2, 4, 6, 8])).toBeCloseTo(Math.sqrt(5), 10);
  });

  it('is zero for identical values', () => {
    expect(populationSigma([7, 7, 7, 7])).toBe(0);
  });
});

describe('sigmaSeries', () => {
  const five = weeks(
    [1, 10, 0, true],
    [2, 20, 0, true],
    [3, 30, 0, true],
    [4, 40, 0, true],
    [5, 50, 0, true],
  );

  it('starts at week four, never earlier', () => {
    expect(sigmaSeries(five, false, false).map((p) => p.week)).toEqual([4, 5]);
  });

  it('ignores weeks that are not final', () => {
    const withPending = [...five, ...weeks([6, 0, 0, false])];
    expect(sigmaSeries(withPending, false, false).map((p) => p.week)).toEqual([4, 5]);
  });

  it('rolls over the last four weeks only', () => {
    const expanding = sigmaSeries(five, false, false);
    const rolling = sigmaSeries(five, true, false);
    expect(rolling.at(-1)!.value).toBeCloseTo(populationSigma([20, 30, 40, 50])!, 10);
    expect(expanding.at(-1)!.value).toBeCloseTo(populationSigma([10, 20, 30, 40, 50])!, 10);
  });

  it('divides by the mean for CV', () => {
    const [point] = sigmaSeries(five, false, true);
    expect(point.value).toBeCloseTo(populationSigma([10, 20, 30, 40])! / 25, 10);
  });

  it('drops a CV with no mean rather than dividing by zero', () => {
    const zeros = weeks([1, 0, 0, true], [2, 0, 0, true], [3, 0, 0, true], [4, 0, 0, true]);
    expect(sigmaSeries(zeros, false, true)).toEqual([]);
    expect(sigmaSeries(zeros, false, false)).toHaveLength(1);
  });
});

describe('standings', () => {
  it('ranks by points with each gap to the leader', () => {
    const rows = standings([...payload.teams].reverse());
    expect(rows.map((r) => r.team.name)).toEqual(['Alpha', 'Bravo', 'Charlie']);
    expect(rows.map((r) => Number(r.gap.toFixed(1)))).toEqual([0, 40.2, 89.5]);
  });

  it('leaves the input order alone', () => {
    const input = [...payload.teams].reverse();
    rankByPoints(input);
    expect(input[0]?.name).toBe('Charlie');
  });

  it('is empty for no teams', () => {
    expect(standings([])).toEqual([]);
  });
});

describe('canPlotSigma and canPlotMedian', () => {
  it('needs five final weeks for a sigma line: four for the first value, one more for a second', () => {
    expect(canPlotSigma(4)).toBe(false);
    expect(canPlotSigma(5)).toBe(true);
  });

  it('needs two final weeks for a median line', () => {
    expect(canPlotMedian(1)).toBe(false);
    expect(canPlotMedian(2)).toBe(true);
  });
});

describe('hasMovement', () => {
  it('is false for nothing, one point, or a flat line', () => {
    expect(hasMovement([])).toBe(false);
    expect(hasMovement([{ week: 1, value: 5 }])).toBe(false);
    expect(
      hasMovement([
        { week: 1, value: 5 },
        { week: 2, value: 5 },
      ]),
    ).toBe(false);
  });

  it('is true once two points differ', () => {
    expect(
      hasMovement([
        { week: 1, value: 5 },
        { week: 2, value: 6 },
      ]),
    ).toBe(true);
  });
});

describe('slotRatios and slotDeviations', () => {
  const radar = {
    ...payload,
    slotOrder: ['QB', 'RB', 'K'],
    leagueSlotAverages: { QB: 20, RB: 10, K: 0 },
    teams: payload.teams.map((t) =>
      t.rosterId === 1 ? { ...t, slotAverages: { QB: 25, K: 4 } } : t,
    ),
  };

  it('divides by the league average, in slot order', () => {
    expect(slotRatios(radar, 1)[0]).toBe(1.25);
  });

  it('is null where the team has no figure or the league average is zero', () => {
    expect(slotRatios(radar, 1).slice(1)).toEqual([null, null]);
  });

  it('is null at every slot for a team not in the payload', () => {
    expect(slotRatios(radar, 99)).toEqual([null, null, null]);
  });

  it('turns ratios into distance from the league average', () => {
    expect(slotDeviations([1.25, null, 0.5])).toEqual([0.25, null, -0.5]);
  });
});

describe('latestExtremes', () => {
  const line = (name: string, ...values: number[]) => ({
    name,
    points: values.map((value, i) => ({ week: i + 1, value })),
  });

  it('reads the highest and lowest at the latest week', () => {
    expect(latestExtremes([line('A', 1, 9), line('B', 5, 2), line('C', 3, 4)])).toEqual({
      week: 2,
      high: { name: 'A', value: 9 },
      low: { name: 'B', value: 2 },
    });
  });

  it('ignores a line that stops before the latest week', () => {
    expect(latestExtremes([line('A', 1, 9), line('B', 100)])?.low.name).toBe('A');
  });

  it('is null with nothing plotted', () => {
    expect(latestExtremes([])).toBeNull();
    expect(latestExtremes([{ name: 'A', points: [] }])).toBeNull();
  });
});
