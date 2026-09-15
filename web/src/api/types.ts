/**
 * Season data
 */
export interface SeasonPayload {
  season: number;
  isCurrent: boolean;
  currentWeek: number;
  finalThroughWeek: number;
  /** ISO 8601 for when the snapshot was assembled */
  asOfUtc: string;
  /** Radar axis order */
  slotOrder: string[];
  leagueSlotAverages: Record<string, number>;
  teams: TeamPayload[];
}

/**
 * Data about a team
 */
export interface TeamPayload {
  rosterId: number;
  name: string;
  totals: TotalsPayload;
  /** Empty until M6 fills it. */
  slotAverages: Record<string, number>;
  weeks: WeekPayload[];
}

/**
 * The five consistency fields are null together or present together: all of them
 * are null below four finalized weeks (D1). One guard decides whether the
 * consistency column renders at all.
 */
export interface TotalsPayload {
  actual: number;
  ideal: number;
  sigma: number | null;
  cv: number | null;
  mean: number | null;
  min: number | null;
  max: number | null;
}

export interface WeekPayload {
  week: number;
  actual: number;
  ideal: number;
  isFinal: boolean;
}

export interface SeasonsPayload {
  current: number;
  /** [2026] until the season chain walk runs, which is deferred. */
  available: number[];
}

/** True when every consistency figure is available. */
export function hasConsistency(
  totals: TotalsPayload,
): totals is TotalsPayload & {
  sigma: number;
  cv: number;
  mean: number;
  min: number;
  max: number;
} {
  return totals.sigma !== null;
}
