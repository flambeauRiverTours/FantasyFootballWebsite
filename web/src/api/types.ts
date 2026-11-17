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
  /** The last week that counts (A37): the week before the playoffs, else 18, less any settled weeks at the end nobody scored in. */
  lastRegularWeek: number;
  /**
   * How the league decides its winner (A38). Optional: a past season a
   * browser cached before A38 has none, so read a missing one as `TotalPoints`.
   */
  format?: LeagueFormat;
  /**
   * One week's games (T71, A41): the current week, held to the last that counts.
   * Only in `HeadToHead`, and only when that week has games; a missing one means no panel.
   */
  matchups?: MatchupsPayload;
  /** The table's champions once the season is complete (T70); absent until then. */
  champions?: number[];
  /** The week the season ends (A46); absent in a body cached before T69. */
  seasonEndWeek?: number;
  /** Whether the season is over (A46); absent in a body cached before T69. */
  isComplete?: boolean;
  /** The winners bracket (T69); absent without one to show. */
  playoffs?: PlayoffsPayload;
  /**
   * The divisions the standings are grouped by (T72b, A48), in number order. Only in
   * `HeadToHead`, only when every team is placed in one; absent otherwise.
   */
  divisions?: DivisionPayload[];
}

/** One division of the standings (T72b). */
export interface DivisionPayload {
  /** Sleeper's division number; each team's `division` names one. */
  number: number;
  /** The commissioner's name for it, or "Division N". */
  name: string;
}

/** The winners bracket (T69, A46). */
export interface PlayoffsPayload {
  rounds: RoundPayload[];
}

/** One round: "Final", "Semifinals" or "Round N". */
export interface RoundPayload {
  round: number;
  weeks: number[];
  name: string;
  matches: BracketMatchPayload[];
}

/** One match; its sides in Sleeper's order. */
export interface BracketMatchPayload {
  match: number;
  /** What the card says: "Final", "3rd place", "Semifinal 1". */
  label: string;
  placement: number | null;
  /** Every week final and Sleeper's winner set. */
  isSettled: boolean;
  sides: [BracketSidePayload, BracketSidePayload];
}

/** One side of a bracket match; an unknown one has no roster and says where it comes from. */
export interface BracketSidePayload {
  rosterId: number | null;
  name: string | null;
  from: { winnerOf?: number; loserOf?: number } | null;
  /** Summed over the round's weeks so far; null before it starts. */
  points: number | null;
  won: boolean;
}

/** One week's games, in matchup id order. */
export interface MatchupsPayload {
  week: number;
  /** Whether the week is settled; until then the points are live. */
  isFinal: boolean;
  games: GamePayload[];
  /** The week's median score (A43). */
  median?: number;
}

/** One game. Its sides are in roster id order, so they never swap between polls. */
export interface GamePayload {
  matchupId: number;
  teams: [MatchupTeamPayload, MatchupTeamPayload];
}

/** One side of a game. The placeholder's side carries its name, as it has no table row (A1). */
export interface MatchupTeamPayload {
  rosterId: number;
  name: string;
  actual: number;
}

/** The deployment's `League:Format`, spelled as the setting is (A38). */
export type LeagueFormat = 'TotalPoints' | 'HeadToHead';

/**
 * True once the fantasy season's last counted week is final: nothing on the
 * page can change after it, so it says so and stops polling (A37).
 */
export function isSeasonComplete(payload: SeasonPayload): boolean {
  // the server's word (A46); a body cached before it said falls back to the old rule, as format does (A38)
  return payload.isComplete ?? payload.finalThroughWeek >= payload.lastRegularWeek;
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
  /**
   * The head-to-head record (T68). Only in `HeadToHead`, and only once every
   * counted week has its games; every team has one or none does.
   */
  record?: TeamRecord;
  /** The team's division's number (T72b); present exactly when the payload has `divisions`. */
  division?: number;
}

/** Wins, losses and ties (C#'s `MedianPayload`). */
export interface Results {
  wins: number;
  losses: number;
  ties: number;
}

/** Results and points over the settled weeks that count; median results included (Q12). */
export interface TeamRecord extends Results {
  /** Every settled week's points, a bye included: the tiebreak after win rate. */
  pointsFor: number;
  pointsAgainst: number;
  /** The median game's share (A43). */
  median?: Results;
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

/** The §09 body of `/api/seasons`: every season that can be served, newest first. */
export interface SeasonsPayload {
  current: number;
  available: number[];
}

export interface WeekPayload {
  week: number;
  actual: number;
  ideal: number;
  isFinal: boolean;
}
