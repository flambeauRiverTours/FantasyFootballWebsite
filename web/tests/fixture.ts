import type { PlayoffsPayload, SeasonPayload, TeamPayload } from '../src/api/types';

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

function result(wins: number, losses: number, ties: number) {
  return { wins, losses, ties };
}

/**
 * A head-to-head season whose records rank the teams in the reverse of their
 * points, so anything still ordered by points shows (T68).
 */
/** A two-round bracket: semifinals settled (the second on a tiebreak, won by roster 9, which has no table row), the final to come (T69). */
export const bracket: PlayoffsPayload = {
  rounds: [
    {
      round: 1,
      weeks: [15],
      name: 'Semifinals',
      matches: [
        {
          match: 1,
          label: 'Semifinal 1',
          placement: null,
          isSettled: true,
          sides: [
            { rosterId: 1, name: 'Alpha', from: null, points: 120.5, won: true },
            { rosterId: 2, name: 'Bravo', from: null, points: 99, won: false },
          ],
        },
        {
          match: 2,
          label: 'Semifinal 2',
          placement: null,
          isSettled: true,
          sides: [
            { rosterId: 3, name: 'Charlie', from: null, points: 80, won: false },
            { rosterId: 9, name: 'Placeholder', from: null, points: 80, won: true },
          ],
        },
      ],
    },
    {
      round: 2,
      weeks: [16],
      name: 'Final',
      matches: [
        {
          match: 3,
          label: 'Final',
          placement: 1,
          isSettled: false,
          sides: [
            { rosterId: 1, name: 'Alpha', from: { winnerOf: 1 }, points: 50, won: false },
            { rosterId: null, name: null, from: { winnerOf: 2 }, points: null, won: false },
          ],
        },
        {
          match: 4,
          label: '3rd place',
          placement: 3,
          isSettled: false,
          sides: [
            { rosterId: null, name: null, from: { loserOf: 1 }, points: null, won: false },
            { rosterId: null, name: null, from: null, points: null, won: false },
          ],
        },
      ],
    },
  ],
};

export const headToHead: SeasonPayload = {
  ...payload,
  format: 'HeadToHead',
  teams: [
    {
      ...teams[0],
      record: { ...result(1, 3, 0), pointsFor: 340.2, pointsAgainst: 300, median: result(1, 1, 0) },
    },
    {
      ...teams[1],
      record: { ...result(2, 2, 0), pointsFor: 300, pointsAgainst: 310, median: result(1, 1, 0) },
    },
    {
      ...teams[2],
      record: {
        ...result(3, 1, 0),
        pointsFor: 250.7,
        pointsAgainst: 280.9,
        median: result(1, 1, 0),
      },
    },
  ],
  // week 3 is live; Charlie plays roster 9, which has no table row (A1)
  matchups: {
    week: 3,
    isFinal: false,
    median: 50,
    games: [
      {
        matchupId: 1,
        teams: [
          { rosterId: 1, name: 'Alpha', actual: 96.4 },
          { rosterId: 2, name: 'Bravo', actual: 41.2 },
        ],
      },
      {
        matchupId: 2,
        teams: [
          { rosterId: 3, name: 'Charlie', actual: 50 },
          { rosterId: 9, name: 'Placeholder', actual: 50 },
        ],
      },
    ],
  },
};
