import type { TeamPayload } from './types';

/**
 * Each team's colour for the season, as a CSS variable.
 *
 * Assigned in roster id order rather than by rank, so a team keeps its colour
 * when it is overtaken and every panel draws it the same (teams are encoded by
 * colour, design §10).
 */
export function teamColours(teams: TeamPayload[]): ReadonlyMap<number, string> {
  const ids = teams.map((t) => t.rosterId).sort((a, b) => a - b);
  return new Map(ids.map((id, i) => [id, `var(--team-${i + 1})`]));
}
