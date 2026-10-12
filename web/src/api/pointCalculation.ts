import { median } from 'd3-array';
import type { SeasonPayload, TeamPayload, WeekPayload } from './types';

/** One plotted value at one week. */
export interface Point {
  week: number;
  value: number;
}

/** How weekly values are transformed before plotting. */
export type PointMode = 'cumulative' | 'perWeek' | 'vsMedian';

/** Which number is being plotted. */
export type Metric = 'actual' | 'ideal' | 'sigma';

function valueOf(w: WeekPayload, metric: Metric): number {
  return metric === 'ideal' ? w.ideal : w.actual;
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
export function cumulative(weeks: WeekPayload[], metric: Metric = 'actual'): Point[] {
  let total = 0;
  return ordered(weeks).map((w) => ({ week: w.week, value: (total += valueOf(w, metric)) }));
}

/** Each week on its own. */
export function perWeek(weeks: WeekPayload[], metric: Metric = 'actual'): Point[] {
  return ordered(weeks).map((w) => ({ week: w.week, value: valueOf(w, metric) }));
}

/** League median per finalized week. */
export function weeklyMedians(
  teams: TeamPayload[],
  metric: Metric = 'actual',
): Map<number, number> {
  const byWeek = new Map<number, number[]>();
  for (const t of teams)
    for (const w of t.weeks)
      if (w.isFinal) {
        const bucket = byWeek.get(w.week) ?? [];
        bucket.push(valueOf(w, metric));
        byWeek.set(w.week, bucket);
      }

  return new Map([...byWeek].map(([week, vals]) => [week, median(vals) ?? 0]));
}

/** Running total of the gap to the league. */
export function vsMedian(
  weeks: WeekPayload[],
  medians: Map<number, number>,
  metric: Metric = 'actual',
): Point[] {
  let total = 0;
  return ordered(weeks).flatMap((w) => {
    const league = medians.get(w.week);
    if (league === undefined) return [];
    total += valueOf(w, metric) - league;
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

/** A team's row in the standings. */
export interface Standing {
  team: TeamPayload;
  /** Points behind the leader. Zero for the leader. */
  gap: number;
}

/** Teams ranked by points, highest first. Points decide the league. */
export function rankByPoints(teams: TeamPayload[]): TeamPayload[] {
  return [...teams].sort((a, b) => b.totals.actual - a.totals.actual);
}

/** The standings table: teams ranked by points, each with its gap to the leader. */
export function standings(teams: TeamPayload[]): Standing[] {
  const ranked = rankByPoints(teams);
  const leader = ranked[0]?.totals.actual ?? 0;
  return ranked.map((team) => ({ team, gap: leader - team.totals.actual }));
}

/** Final weeks before sigma draws a line: four for its first value (D1), one more for a second. */
const SigmaLineWeeks = 5;

/** Final weeks before the league median gives a line two points long. */
const MedianLineWeeks = 2;

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
export interface NamedValue {
  name: string;
  value: number;
}

/** The highest and lowest line at the latest week any line reaches. */
export interface Extremes {
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
