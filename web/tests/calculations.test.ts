import { describe, expect, it } from 'vitest';
import type { TeamPayload, TeamRecord, WeekPayload } from '../src/api/types';
import {
  canPlotMedian,
  canPlotSigma,
  canZoom,
  cumulative,
  formatWeekRange,
  hasMovement,
  hasRecords,
  hasTies,
  isZoomed,
  latestExtremes,
  gameResults,
  hasMedianGame,
  matchupLeader,
  matchupSpoken,
  medianSplit,
  weekStarted,
  parseWeekRange,
  perWeek,
  pointsIn,
  populationSigma,
  rankByPoints,
  rankByRecord,
  rankTeams,
  recordSpoken,
  recordText,
  settledIdeal,
  sigmaSeries,
  slotDeviations,
  slotRatios,
  standings,
  startedWeeks,
  vsMedian,
  weeklyMedians,
} from '../src/api/calculations';
import { headToHead, payload } from './fixture';
import type { GamePayload } from '../src/api/types';

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
    const rows = standings({ ...payload, teams: [...payload.teams].reverse() });
    expect(rows.map((r) => r.team.name)).toEqual(['Alpha', 'Bravo', 'Charlie']);
    expect(rows.map((r) => Number(r.gap.toFixed(1)))).toEqual([0, 40.2, 89.5]);
  });

  it('leaves the input order alone', () => {
    const input = [...payload.teams].reverse();
    rankByPoints(input);
    expect(input[0]?.name).toBe('Charlie');
  });

  it('is empty for no teams', () => {
    expect(standings({ ...payload, teams: [] })).toEqual([]);
  });

  it('ranks by record in head-to-head, the gap still from the points leader', () => {
    const rows = standings(headToHead);
    expect(rows.map((r) => r.team.name)).toEqual(['Charlie', 'Bravo', 'Alpha']);
    expect(rows.map((r) => Number(r.gap.toFixed(1)))).toEqual([89.5, 40.2, 0]);
  });

  it('numbers the rows in rank order', () => {
    expect(standings(headToHead).map((r) => r.rank)).toEqual([1, 2, 3]);
  });

  it('counts games back from the record leader, a tie as half', () => {
    // 3-1 leads; 2-2 is one back; 1-3 is two back
    expect(standings(headToHead).map((r) => r.gamesBack)).toEqual([0, 1, 2]);
    const tied = headToHead.teams.map((t, i) =>
      i === 0 ? { ...t, record: { ...t.record!, losses: 2, ties: 1 } } : t,
    );
    expect(standings({ ...headToHead, teams: tied }).map((r) => r.gamesBack)).toEqual([0, 1, 1.5]);
  });

  it('counts games back from the best wins less losses, never below zero', () => {
    // a bye leaves teams on different game counts: win rate ranks 4-0 first, but
    // 7-1 has the best wins less losses, so 4-0 is one behind it, never 7-1 one ahead
    const at = (i: number, wins: number, losses: number) => ({
      ...headToHead.teams[i % 3],
      rosterId: i,
      name: `T${i}`,
      record: { ...headToHead.teams[0].record!, wins, losses, ties: 0 },
    });
    const teams = [at(1, 4, 0), at(2, 5, 1), at(3, 7, 1), at(4, 1, 3)];
    const rows = standings({ ...headToHead, teams });
    expect(rows.map((r) => r.team.name)).toEqual(['T1', 'T3', 'T2', 'T4']);
    expect(rows.map((r) => r.gamesBack)).toEqual([1, 0, 1, 4]);
  });

  it('has no games back without records', () => {
    expect(standings(payload).every((r) => r.gamesBack === null)).toBe(true);
  });
});

describe('the median game', () => {
  it('takes the median share off a record, leaving the games', () => {
    const record = {
      wins: 10,
      losses: 2,
      ties: 0,
      pointsFor: 1,
      pointsAgainst: 1,
      median: { wins: 5, losses: 1, ties: 0 },
    };
    expect(gameResults(record)).toEqual({ wins: 5, losses: 1, ties: 0 });
  });

  it('leaves a record without a median share as it is', () => {
    const record = { wins: 3, losses: 1, ties: 1, pointsFor: 1, pointsAgainst: 1 };
    expect(gameResults(record)).toEqual({ wins: 3, losses: 1, ties: 1 });
  });

  it('knows whether the league plays one', () => {
    expect(hasMedianGame(headToHead.teams)).toBe(true);
    const plain = headToHead.teams.map((t) => ({
      ...t,
      record: { ...t.record!, median: undefined },
    }));
    expect(hasMedianGame(plain)).toBe(false);
  });
});

describe('head-to-head records', () => {
  const record = (wins: number, losses: number, ties: number, pointsFor = 0): TeamRecord => ({
    wins,
    losses,
    ties,
    pointsFor,
    pointsAgainst: 0,
  });
  const named = (name: string, r: TeamRecord): TeamPayload => ({
    ...payload.teams[0],
    name,
    record: r,
  });
  const names = (teams: TeamPayload[]) => teams.map((t) => t.name);

  it('counts a tie as half a win', () => {
    // 2-1-1 is 2.5 of 4; 2-2 is 2 of 4
    expect(
      names(rankByRecord([named('Even', record(2, 2, 0)), named('Tied', record(2, 1, 1))])),
    ).toEqual(['Tied', 'Even']);
  });

  it('breaks an equal win rate on points for, then on name', () => {
    const ranked = rankByRecord([
      named('Zulu', record(2, 2, 0, 300)),
      named('Low', record(1, 1, 0, 100)),
      named('Alpha', record(2, 2, 0, 300)),
    ]);
    expect(names(ranked)).toEqual(['Alpha', 'Zulu', 'Low']);
  });

  it('ranks every 0-0 team by name before any week settles', () => {
    const ranked = rankByRecord([named('Bravo', record(0, 0, 0)), named('Alpha', record(0, 0, 0))]);
    expect(names(ranked)).toEqual(['Alpha', 'Bravo']);
  });

  it('compares names as a reader would, not by code point, as the CLI does (T72a)', () => {
    const ranked = rankByRecord([named('Bravo', record(0, 0, 0)), named('alpha', record(0, 0, 0))]);
    expect(names(ranked)).toEqual(['alpha', 'Bravo']);
  });

  it('leaves the input order alone', () => {
    const input = [...headToHead.teams];
    rankByRecord(input);
    expect(input[0]?.name).toBe('Alpha');
  });

  it('ranks by record only in head-to-head with every record present', () => {
    expect(hasRecords(headToHead)).toBe(true);
    expect(names(rankTeams(headToHead))).toEqual(['Charlie', 'Bravo', 'Alpha']);

    const missing = { ...headToHead, teams: [headToHead.teams[0], payload.teams[1]] };
    expect(hasRecords(missing)).toBe(false);
    expect(names(rankTeams(missing))).toEqual(['Alpha', 'Bravo']);
  });

  it('reads a missing format as total points (A38)', () => {
    const cached = { ...headToHead, format: undefined };
    expect(hasRecords(cached)).toBe(false);
    expect(names(rankTeams(cached))).toEqual(['Alpha', 'Bravo', 'Charlie']);
  });

  it('sums ideal points over settled weeks only', () => {
    const team: TeamPayload = {
      ...payload.teams[0],
      weeks: [
        { week: 1, actual: 10, ideal: 12, isFinal: true },
        { week: 2, actual: 20, ideal: 25, isFinal: true },
        { week: 3, actual: 5, ideal: 40, isFinal: false },
      ],
    };
    expect(settledIdeal(team)).toBe(37);
  });

  it('shows ties only once a team has one', () => {
    expect(hasTies(headToHead.teams)).toBe(false);
    expect(hasTies([named('Tied', record(1, 1, 1))])).toBe(true);
    expect(hasTies(payload.teams)).toBe(false);
  });

  it('writes the record short, with ties only when asked', () => {
    expect(recordText(record(13, 5, 0), false)).toBe('13-5');
    expect(recordText(record(13, 5, 0), true)).toBe('13-5-0');
    expect(recordText(record(12, 5, 1), true)).toBe('12-5-1');
  });

  it('spells the record out for screen readers', () => {
    expect(recordSpoken(record(13, 5, 0), false)).toBe('13 wins, 5 losses');
    expect(recordSpoken(record(1, 1, 1), true)).toBe('1 win, 1 loss, 1 tie');
    expect(recordSpoken(record(0, 2, 2), true)).toBe('0 wins, 2 losses, 2 ties');
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

describe('week ranges (T66)', () => {
  it('reads a range from the URL', () => {
    expect(parseWeekRange('6-12', 14)).toEqual([6, 12]);
  });

  it('treats anything malformed as the whole season', () => {
    for (const raw of ['', 'abc', '6', '6-', '-3-4', '6-12-14']) {
      expect(parseWeekRange(raw, 14)).toEqual([1, 14]);
    }
  });

  it('clamps to the weeks played, puts the ends in order and keeps two weeks at least', () => {
    expect(parseWeekRange('0-40', 14)).toEqual([1, 14]);
    expect(parseWeekRange('12-6', 14)).toEqual([6, 12]);
    expect(parseWeekRange('9-9', 14)).toEqual([9, 10]);
    expect(parseWeekRange('14-14', 14)).toEqual([13, 14]);
  });

  it('writes nothing for the whole season, so the URL stays clean', () => {
    expect(formatWeekRange([1, 14], 14)).toBe('');
    expect(formatWeekRange([6, 12], 14)).toBe('6-12');
    expect(isZoomed([1, 14], 14)).toBe(false);
    expect(isZoomed([1, 13], 14)).toBe(true);
  });

  it('offers zoom only once the season is long enough to need it', () => {
    expect(canZoom(3)).toBe(false);
    expect(canZoom(4)).toBe(true);
  });

  it('keeps the points inside a range', () => {
    const points = [1, 2, 3, 4].map((week) => ({ week, value: week }));
    expect(pointsIn(points, [2, 3]).map((p) => p.week)).toEqual([2, 3]);
  });
});

describe('matchupLeader', () => {
  const game = (a: number, b: number): GamePayload => ({
    matchupId: 1,
    teams: [
      { rosterId: 1, name: 'Alpha', actual: a },
      { rosterId: 2, name: 'Bravo', actual: b },
    ],
  });

  it('picks the first side when it has more points', () => {
    expect(matchupLeader(game(96.4, 41.2))).toBe(0);
  });

  it('picks the second side when it has more points', () => {
    expect(matchupLeader(game(41.2, 96.4))).toBe(1);
  });

  it('picks neither on equal scores, kick-off included', () => {
    expect(matchupLeader(game(50, 50))).toBeNull();
    expect(matchupLeader(game(0, 0))).toBeNull();
  });

  it('gives a screen reader the state of play while live', () => {
    expect(matchupSpoken(game(96.4, 41.2), false)).toEqual(['leading', '']);
    expect(matchupSpoken(game(50, 50), false)).toEqual(['level', 'level']);
    expect(matchupSpoken(game(0, 0), false)).toEqual(['', '']);
  });

  it('gives a screen reader the result once final', () => {
    expect(matchupSpoken(game(41.2, 96.4), true)).toEqual(['', 'won']);
    expect(matchupSpoken(game(0, 0), true)).toEqual(['tied', 'tied']);
  });

  it('splits the sides above the median from those on it, in card order', () => {
    const week = { week: 3, isFinal: false, median: 45, games: [game(96.4, 41.2), game(45, 50)] };
    const { above, on } = medianSplit(week);
    expect(above.map((t) => t.actual)).toEqual([96.4, 50]);
    expect(on.map((t) => t.actual)).toEqual([45]);
  });

  it('splits nothing without a median game', () => {
    expect(medianSplit({ week: 3, isFinal: false, games: [game(96.4, 41.2)] })).toEqual({
      above: [],
      on: [],
    });
  });

  it('knows whether the week has kicked off', () => {
    const week = (a: number, b: number) => ({ week: 3, isFinal: false, games: [game(a, b)] });
    expect(weekStarted(week(0, 0))).toBe(false);
    expect(weekStarted(week(0, 0.5))).toBe(true);
  });
});
