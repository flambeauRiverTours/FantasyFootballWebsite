import { median } from 'd3-array';
import { hasConsistency } from './types';
import type {
  ConsistentTotals,
  BracketMatchPayload,
  DivisionPayload,
  GamePayload,
  MatchupsPayload,
  MatchupTeamPayload,
  Results,
  SeasonPayload,
  TeamPayload,
  TeamRecord,
  WeekPayload,
} from './types';

/** One plotted value at one week. */
export interface Point {
  week: number;
  value: number;
}

/** How weekly values are transformed before plotting. */
export type PointMode = 'cumulative' | 'perWeek' | 'vsMedian';

/** Which weekly score a points calculation reads. */
export type Measure = 'actual' | 'ideal';

function valueOf(w: WeekPayload, measure: Measure): number {
  return measure === 'ideal' ? w.ideal : w.actual;
}

function ordered(weeks: WeekPayload[]): WeekPayload[] {
  return [...weeks].sort((a, b) => a.week - b.week);
}

/**
 * The weeks with data that should be plotted
 */
export function startedWeeks(teams: TeamPayload[]): Set<number> {
  const started = new Set<number>();
  for (const t of teams)
    for (const w of t.weeks) if (w.isFinal || w.actual !== 0) started.add(w.week);
  return started;
}

/** Running total across the season. */
export function cumulative(weeks: WeekPayload[], measure: Measure = 'actual'): Point[] {
  let total = 0;
  return ordered(weeks).map((w) => ({ week: w.week, value: (total += valueOf(w, measure)) }));
}

/** Each week on its own. */
export function perWeek(weeks: WeekPayload[], measure: Measure = 'actual'): Point[] {
  return ordered(weeks).map((w) => ({ week: w.week, value: valueOf(w, measure) }));
}

/** League median per finalized week. */
export function weeklyMedians(
  teams: TeamPayload[],
  measure: Measure = 'actual',
): Map<number, number> {
  const byWeek = new Map<number, number[]>();
  for (const t of teams)
    for (const w of t.weeks)
      if (w.isFinal) {
        const bucket = byWeek.get(w.week) ?? [];
        bucket.push(valueOf(w, measure));
        byWeek.set(w.week, bucket);
      }

  return new Map([...byWeek].map(([week, vals]) => [week, median(vals) ?? 0]));
}

/** Running total of the gap to the league. */
export function vsMedian(
  weeks: WeekPayload[],
  medians: Map<number, number>,
  measure: Measure = 'actual',
): Point[] {
  let total = 0;
  return ordered(weeks).flatMap((w) => {
    const league = medians.get(w.week);
    if (league === undefined) return [];
    total += valueOf(w, measure) - league;
    return [{ week: w.week, value: total }];
  });
}

/** Population standard deviation. Null below four values. */
export function populationSigma(values: number[]): number | null {
  if (values.length < 4) return null; // D1
  const mean = values.reduce((a, b) => a + b, 0) / values.length;
  const variance = values.reduce((a, v) => a + (v - mean) ** 2, 0) / values.length;
  return Math.sqrt(variance);
}

/** Consistency week by week, expanding or rolling. */
export function sigmaSeries(weeks: WeekPayload[], rolling: boolean, asCv: boolean): Point[] {
  const final = weeks.filter((w) => w.isFinal).sort((a, b) => a.week - b.week);

  return final.flatMap((w, i) => {
    const slice = rolling ? final.slice(Math.max(0, i - 3), i + 1) : final.slice(0, i + 1);
    const values = slice.map((x) => x.actual);
    const sigma = populationSigma(values);
    if (sigma === null) return []; // not enough weeks yet

    const mean = values.reduce((a, b) => a + b, 0) / values.length;
    if (asCv && mean === 0) return []; // a CV with no mean is undefined
    return [{ week: w.week, value: asCv ? sigma / mean : sigma }];
  });
}

/** An inclusive span of weeks, first then last, always at least two weeks wide. */
export type WeekRange = readonly [number, number];

/** True when the season is long enough for zooming to help. */
export function canZoom(maxWeek: number): boolean {
  return maxWeek >= 4;
}

/**
 * Keeps a range inside weeks 1 to `maxWeek` and at least two weeks wide (the
 * week scale's floor), moving the end that was not dragged when it must.
 */
export function clampRange([first, last]: WeekRange, maxWeek: number): WeekRange {
  const top = Math.max(maxWeek, 2);
  const lo = Math.min(Math.max(Math.round(first), 1), top - 1);
  const hi = Math.min(Math.max(Math.round(last), lo + 1), top);
  return [lo, hi];
}

/** Reads `?weeks=6-12`. Anything malformed, or no zoom, is the whole season. */
export function parseWeekRange(raw: string, maxWeek: number): WeekRange {
  const match = /^(\d{1,2})-(\d{1,2})$/.exec(raw);
  if (!match) return [1, Math.max(maxWeek, 2)];
  const [a, b] = [Number(match[1]), Number(match[2])];
  return clampRange([Math.min(a, b), Math.max(a, b)], maxWeek);
}

/** Writes a range for the URL; the whole season is empty, so the URL stays clean. */
export function formatWeekRange(range: WeekRange, maxWeek: number): string {
  return isZoomed(range, maxWeek) ? `${range[0]}-${range[1]}` : '';
}

/** True when the range is narrower than the season. */
export function isZoomed([first, last]: WeekRange, maxWeek: number): boolean {
  return first > 1 || last < maxWeek;
}

/** The points that fall inside a range. */
export function pointsIn(points: Point[], [first, last]: WeekRange): Point[] {
  return points.filter((p) => p.week >= first && p.week <= last);
}

/** A team's row in the standings. */
export interface Standing {
  team: TeamPayload;
  /** Points behind the leader. Zero for the leader. */
  gap: number;
  /** The place: 1 for the top row, shared by teams level on what decides it, the next place skipped. */
  rank: number;
  /** Shares its place with another team: shown as T1. */
  tied: boolean;
  /** Games behind the leader; ties count half. */
  gamesBack: number | null;
  /** Crowned once the season is complete (T70): co-champion when the title is shared. */
  champion: 'Champion' | 'Co-champion' | null;
}

/** Teams ranked by points, highest first. Points decide the league. */
export function rankByPoints(teams: TeamPayload[]): TeamPayload[] {
  return [...teams].sort((a, b) => b.totals.actual - a.totals.actual);
}

/**
 * True when the page ranks by record: a head-to-head league whose every team
 * has one (T68). A missing format reads as total points (A38).
 */
export function hasRecords(payload: SeasonPayload): boolean {
  return payload.format === 'HeadToHead' && payload.teams.every((t) => t.record !== undefined);
}

const NoRecord: TeamRecord = { wins: 0, losses: 0, ties: 0, pointsFor: 0, pointsAgainst: 0 };

/** Wins over games, a tie as half a win; zero before any game. */
function winRate({ wins, losses, ties }: TeamRecord): number {
  const games = wins + losses + ties;
  return games === 0 ? 0 : (wins + ties / 2) / games;
}

/**
 * Teams ranked by record: win rate, then points for, then name. Records move
 * only when a week settles, so the order never shuffles during a live poll.
 */
export function rankByRecord(teams: TeamPayload[]): TeamPayload[] {
  return [...teams].sort((a, b) => {
    const [ra, rb] = [a.record ?? NoRecord, b.record ?? NoRecord];
    return winRate(rb) - winRate(ra) || rb.pointsFor - ra.pointsFor || a.name.localeCompare(b.name);
  });
}

/** Teams in the order the league ranks them: by record in head-to-head, else by points. */
export function rankTeams(payload: SeasonPayload): TeamPayload[] {
  return hasRecords(payload) ? rankByRecord(payload.teams) : rankByPoints(payload.teams);
}

/** Ideal points over the settled weeks, as a record counts them. The payload stops at the last counted week. */
export function settledIdeal(team: TeamPayload): number {
  return team.weeks.filter((w) => w.isFinal).reduce((sum, w) => sum + w.ideal, 0);
}

/** A record without its median share (A43). */
export function gameResults(record: TeamRecord): Results {
  const median = record.median ?? { wins: 0, losses: 0, ties: 0 };
  return {
    wins: record.wins - median.wins,
    losses: record.losses - median.losses,
    ties: record.ties - median.ties,
  };
}

/** Whether the records carry a median share (A43). */
export function hasMedianGame(teams: TeamPayload[]): boolean {
  return teams.some((t) => t.record?.median !== undefined);
}

/** True when any team has a tied game, so every record shows its ties. */
export function hasTies(teams: TeamPayload[]): boolean {
  return teams.some((t) => (t.record?.ties ?? 0) > 0);
}

/** A record as the table shows it: "13-5", or "13-5-0" when the league has a tie. */
export function recordText({ wins, losses, ties }: Results, showTies: boolean): string {
  return showTies ? `${wins}-${losses}-${ties}` : `${wins}-${losses}`;
}

function counted(n: number, one: string, many: string): string {
  return `${n} ${n === 1 ? one : many}`;
}

/** A record in words, for screen readers: "13 wins, 5 losses". */
export function recordSpoken({ wins, losses, ties }: Results, showTies: boolean): string {
  const parts = [counted(wins, 'win', 'wins'), counted(losses, 'loss', 'losses')];
  return (showTies ? [...parts, counted(ties, 'tie', 'ties')] : parts).join(', ');
}

/** The champions' names: from the table, else from the bracket for one with no row (Q9). */
export function championNames(payload: SeasonPayload): string[] {
  const sides = payload.playoffs?.rounds.flatMap((r) => r.matches.flatMap((m) => m.sides)) ?? [];
  return (payload.champions ?? []).map(
    (id) =>
      payload.teams.find((t) => t.rosterId === id)?.name ??
      sides.find((s) => s.rosterId === id)?.name ??
      `Roster ${id}`,
  );
}

/** The live leader of a bracket card; none once settled (the winner's whole line is bold) or when level. */
export function bracketLeader({ isSettled, sides: [a, b] }: BracketMatchPayload): 0 | 1 | null {
  return isSettled ? null : leaderOf(a.points, b.points);
}

/** Screen reader result per bracket side (A43): "won, on tiebreak" when Sleeper's tiebreak decided it. */
export function bracketSpoken(match: BracketMatchPayload): [string, string] {
  const [a, b] = match.sides;
  if (match.isSettled) {
    const word = a.points !== null && a.points === b.points ? 'won, on tiebreak' : 'won';
    return [a.won ? word : '', b.won ? word : ''];
  }
  if (a.points === null || b.points === null || (a.points === 0 && b.points === 0)) {
    return ['', ''];
  }
  if (a.points === b.points) {
    return ['level', 'level'];
  }
  return a.points > b.points ? ['leading', ''] : ['', 'leading'];
}

/** Index of the side ahead; null when level. */
export function matchupLeader({ teams: [a, b] }: GamePayload): 0 | 1 | null {
  return leaderOf(a.actual, b.actual);
}

/** The index of the higher of two scores; null when level or either is unknown. */
function leaderOf(a: number | null, b: number | null): 0 | 1 | null {
  if (a === null || b === null || a === b) {
    return null;
  }
  return a > b ? 0 : 1;
}

/** Screen reader result per side (A43). */
export function matchupSpoken(game: GamePayload, isFinal: boolean): [string, string] {
  const leader = matchupLeader(game);
  if (leader === null) {
    const kickOff = !isFinal && game.teams[0].actual === 0;
    const word = kickOff ? '' : isFinal ? 'tied' : 'level';
    return [word, word];
  }
  const word = isFinal ? 'won' : 'leading';
  return leader === 0 ? [word, ''] : ['', word];
}

/** Sides above and on the week's median (A43). */
export function medianSplit({ games, median }: MatchupsPayload): {
  above: MatchupTeamPayload[];
  on: MatchupTeamPayload[];
} {
  if (median === undefined) {
    return { above: [], on: [] };
  }
  const sides = games.flatMap((g) => g.teams);
  return {
    above: sides.filter((t) => t.actual > median),
    on: sides.filter((t) => t.actual === median),
  };
}

/** Whether any side has scored this week. */
export function weekStarted({ games }: MatchupsPayload): boolean {
  return games.some((g) => g.teams.some((t) => t.actual !== 0));
}

/** Wins less losses: what games back compares. */
function margin({ wins, losses }: Results): number {
  return wins - losses;
}

/**
 * The standings table: teams as the league ranks them, each with its gap to the points leader.
 * `teams` defaults to the league; a division's teams are ranked and measured on their own
 * (T72b), so its leader is rank 1 and games back count from its own best margin.
 */
export function standings(payload: SeasonPayload, teams = payload.teams): Standing[] {
  const byRecord = hasRecords(payload);
  const byPoints = rankByPoints(teams);
  const leader = byPoints[0]?.totals.actual ?? 0;
  const ranked = byRecord ? rankByRecord(teams) : byPoints;
  // from the best wins less losses, not the top row: a bye can leave win rate and margin apart.
  // record! is safe: hasRecords covers every team in the payload, and teams are drawn from it
  const best = byRecord ? Math.max(...teams.map((t) => margin(t.record!))) : null;
  const champions = new Set(payload.champions);
  const title = champions.size > 1 ? 'Co-champion' : 'Champion';
  const places = ranked.map((team) => place(team, byRecord));
  return ranked.map((team) => {
    const key = place(team, byRecord);
    return {
      team,
      gap: leader - team.totals.actual,
      rank: places.indexOf(key) + 1,
      tied: places.indexOf(key) !== places.lastIndexOf(key),
      gamesBack: best === null ? null : (best - margin(team.record!)) / 2,
      champion: champions.has(team.rosterId) ? title : null,
    };
  });
}

/** One group of the table: a division, or the whole league. */
export interface StandingsGroup {
  /** The division, or null for the league as one table. */
  division: DivisionPayload | null;
  rows: Standing[];
}

/**
 * The table's groups (T72b, A48): one per division, each ranked and measured within itself,
 * when the payload has divisions and the records are known; else the league as one group.
 * The champion is the league's either way: a division leader is not crowned.
 */
export function standingsGroups(payload: SeasonPayload): StandingsGroup[] {
  // records still loading: the points table stands in, ungrouped
  if (!payload.divisions || !hasRecords(payload)) {
    return [{ division: null, rows: standings(payload) }];
  }
  return payload.divisions.map((division) => ({
    division,
    rows: standings(
      payload,
      payload.teams.filter((t) => t.division === division.number),
    ),
  }));
}

/** What decides a place, as one comparable string: win rate then points for, else points; never the name. */
function place(team: TeamPayload, byRecord: boolean): string {
  return byRecord ? `${winRate(team.record!)}|${team.record!.pointsFor}` : `${team.totals.actual}`;
}

/** Final weeks before sigma draws a line: four for its first value (D1), one more for a second. */
const SigmaLineWeeks = 5;

/** Final weeks before the league median gives a line two points long. */
const MedianLineWeeks = 2;

/** A team that has every consistency figure. */
export interface ConsistencyRow {
  team: TeamPayload;
  totals: ConsistentTotals;
}

/**
 * Gets tteams with a sigma. Steadiest first, ties by name.
 */
export function consistencyRows(teams: TeamPayload[]): ConsistencyRow[] {
  return teams
    .flatMap((team) => (hasConsistency(team.totals) ? [{ team, totals: team.totals }] : []))
    .sort((a, b) => a.totals.sigma - b.totals.sigma || a.team.name.localeCompare(b.team.name));
}

/**
 * Points either side of a team's mean, shared by every row (design §04).
 */
export function consistencyDomain(rows: ConsistencyRow[]): [number, number] {
  // a skewed team's mean ± σ can pass its own lowest or highest week
  const reach = Math.max(
    0,
    ...rows.map(({ totals: t }) => Math.max(t.mean - t.min, t.max - t.mean, t.sigma)),
  );
  // every week equal: widen so the mark has somewhere to sit
  const half = reach === 0 ? 1 : reach;
  return [-half, half];
}

/** True once the sigma chart has a line to draw. */
export function canPlotSigma(finalThroughWeek: number): boolean {
  return finalThroughWeek >= SigmaLineWeeks;
}

/** True once the vs-median chart has a line to draw. */
export function canPlotMedian(finalThroughWeek: number): boolean {
  return finalThroughWeek >= MedianLineWeeks;
}

/** True when a line would show movement: at least two points, and they differ. */
export function hasMovement(points: Point[]): boolean {
  const first = points[0];
  return first !== undefined && points.some((p) => p.value !== first.value);
}

/**
 * Each slot's season average as a ratio to the league's (D10), in `slotOrder`
 * order. Null where the team has no figure or the league's is missing or zero.
 */
export function slotRatios(payload: SeasonPayload, rosterId: number | null): (number | null)[] {
  const team = payload.teams.find((t) => t.rosterId === rosterId);
  return payload.slotOrder.map((code) => {
    const league = payload.leagueSlotAverages[code];
    const value = team?.slotAverages[code];
    return value === undefined || !league ? null : value / league;
  });
}

/** How far each ratio sits from the league average: 0.12 is 12% above it. */
export function slotDeviations(ratios: (number | null)[]): (number | null)[] {
  return ratios.map((r) => (r === null ? null : r - 1));
}

/** One named value, for describing a chart in words. */
interface NamedValue {
  name: string;
  value: number;
}

/** The highest and lowest line at the latest week any line reaches. */
interface Extremes {
  week: number;
  high: NamedValue;
  low: NamedValue;
}

/** Finds the highest and lowest value at the latest plotted week, so a label can state them. */
export function latestExtremes(series: { name: string; points: Point[] }[]): Extremes | null {
  const week = Math.max(...series.flatMap((s) => s.points.map((p) => p.week)));
  const atWeek = series.flatMap((s) => {
    const point = s.points.find((p) => p.week === week);
    return point ? [{ name: s.name, value: point.value }] : [];
  });

  const [first, ...rest] = atWeek;
  if (first === undefined) return null;

  let high = first;
  let low = first;
  for (const entry of rest) {
    if (entry.value > high.value) high = entry;
    if (entry.value < low.value) low = entry;
  }

  return { week, high, low };
}
