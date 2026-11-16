import type { SeasonPayload, TeamPayload } from '../src/api/types';

const NoConsistency = { sigma: null, cv: null, mean: null, min: null, max: null };

function team(rosterId: number, name: string, actual: number, ideal: number): TeamPayload {
  return {
    rosterId,
    name,
    totals: { actual, ideal, ...NoConsistency },
    slotAverages: {},
    weeks: [
      { week: 1, actual: actual / 2, ideal: ideal / 2, isFinal: true },
      { week: 2, actual: actual / 2, ideal: ideal / 2, isFinal: true },
    ],
  };
}

export const teams: TeamPayload[] = [
  team(1, 'Alpha', 340.2, 400.1),
  team(2, 'Bravo', 300.0, 380.4),
  team(3, 'Charlie', 250.7, 310.9),
];

export const payload: SeasonPayload = {
  leagueName: 'Test League',
  season: 2026,
  isCurrent: true,
  currentWeek: 3,
  finalThroughWeek: 2,
  asOfUtc: new Date().toISOString(),
  slotOrder: [],
  leagueSlotAverages: {},
  teams,
  lastRegularWeek: 14,
  format: 'TotalPoints',
};

/**
 * A head-to-head season whose records rank the teams in the reverse of their
 * points, so anything still ordered by points shows (T68).
 */
export const headToHead: SeasonPayload = {
  ...payload,
  format: 'HeadToHead',
  teams: [
    { ...teams[0], record: { wins: 1, losses: 3, ties: 0, pointsFor: 340.2, pointsAgainst: 300 } },
    { ...teams[1], record: { wins: 2, losses: 2, ties: 0, pointsFor: 300, pointsAgainst: 310 } },
    {
      ...teams[2],
      record: { wins: 3, losses: 1, ties: 0, pointsFor: 250.7, pointsAgainst: 280.9 },
    },
  ],
};
