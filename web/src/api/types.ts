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

/** Totals once a team has four finalized weeks: sigma, mean, min and max are known (D1). */
export type ConsistentTotals = TotalsPayload & {
  sigma: number;
  mean: number;
  min: number;
  max: number;
};

/** True when the consistency figures exist; they are null together below four weeks. */
export function hasConsistency(totals: TotalsPayload): totals is ConsistentTotals {
  return (
    totals.sigma !== null && totals.mean !== null && totals.min !== null && totals.max !== null
  );
}

export interface WeekPayload {
  week: number;
  actual: number;
  ideal: number;
  isFinal: boolean;
}
