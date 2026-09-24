import type { WeekPayload, TeamPayload } from "./types";
import { median } from "d3-array";
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

/**
 * The API includes the current week from kickoff eve, at zero, before anyone
 * has played. Plotted, that zero drops every per-week line to the floor and
 * flattens every running total, so leave a week out until someone scores.
 */
function hasStarted(w: WeekPayload): boolean {
    return w.isFinal || w.actual !== 0;
}

function ordered(weeks: WeekPayload[]): WeekPayload[] {
    return weeks.filter(hasStarted).sort((a, b) => a.week - b.week);
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
export function weeklyMedians(teams: TeamPayload[], metric: Metric = 'actual'): Map<number, number> {
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
    if (values.length < 4) return null;                    // D1
    const mean = values.reduce((a, b) => a + b, 0) / values.length;
    const variance =
        values.reduce((a, v) => a + (v - mean) ** 2, 0) / values.length;
    return Math.sqrt(variance);
}

/** Consistency week by week, expanding or rolling. */
export function sigmaSeries(weeks: WeekPayload[], rolling: boolean, asCv: boolean): Point[] {
    const final = weeks.filter((w) => w.isFinal).sort((a, b) => a.week - b.week);

    return final.flatMap((w, i) => {
        const slice = rolling ? final.slice(Math.max(0, i - 3), i + 1) : final.slice(0, i + 1);
        const values = slice.map((x) => x.actual);
        const sigma = populationSigma(values);
        if (sigma === null) return [];                       // not enough weeks yet

        const mean = values.reduce((a, b) => a + b, 0) / values.length;
        if (asCv && mean === 0) return [];                   // a CV with no mean is undefined
        return [{ week: w.week, value: asCv ? sigma / mean : sigma }];
    });
}