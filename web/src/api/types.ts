/**
 * Season data
 */
export interface SeasonPayload {
  leagueName: string;
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
  /** Season average per slot code. Empty when no finalized week produced an attribution. */
  slotAverages: Record<string, number>;
  weeks: WeekPayload[];
}

/**
 * Sigma, mean, min and max are null together, below four finalized weeks (D1).
 * CV is also null whenever the mean is zero, because it has no denominator (F1).
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
