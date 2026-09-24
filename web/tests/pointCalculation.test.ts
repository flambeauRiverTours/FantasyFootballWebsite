import { describe, expect, it } from 'vitest';
import type { TeamPayload, WeekPayload } from '../src/api/types';
import {
  cumulative,
  perWeek,
  populationSigma,
  sigmaSeries,
  vsMedian,
  weeklyMedians,
} from '../src/api/pointCalculation';

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

describe('the current week', () => {
  const unplayed = weeks([1, 100, 120, true], [2, 50, 60, true], [3, 0, 0, false]);

  it('is left off the chart until someone scores', () => {
    expect(perWeek(unplayed).map((p) => p.week)).toEqual([1, 2]);
    expect(cumulative(unplayed).map((p) => p.week)).toEqual([1, 2]);
    expect(cumulative(unplayed, 'ideal').map((p) => p.week)).toEqual([1, 2]);
  });

  it('appears once points are on the board', () => {
    const live = weeks([1, 100, 120, true], [2, 50, 60, true], [3, 12.4, 20, false]);
    expect(perWeek(live).map((p) => p.value)).toEqual([100, 50, 12.4]);
  });

  it('keeps a finished week that genuinely scored zero', () => {
    const zero = weeks([1, 100, 120, true], [2, 0, 0, true]);
    expect(perWeek(zero).map((p) => p.value)).toEqual([100, 0]);
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
    const zeros = weeks(
      [1, 0, 0, true],
      [2, 0, 0, true],
      [3, 0, 0, true],
      [4, 0, 0, true],
    );
    expect(sigmaSeries(zeros, false, true)).toEqual([]);
    expect(sigmaSeries(zeros, false, false)).toHaveLength(1);
  });
});
