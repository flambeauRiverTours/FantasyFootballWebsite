import { describe, expect, it } from 'vitest';
import { isGameWindow } from '../src/api/gameWindow';

// Eastern time is UTC-4 until 1 November 2026 and UTC-5 after. Thanksgiving
// 2026 is Thursday 26 November; Christmas is a Friday.
const cases: [string, string, boolean][] = [
  ['Sunday 08:30, before the London game', '2026-09-13T12:30:00Z', false],
  ['Sunday 09:30, the London game', '2026-09-13T13:30:00Z', true],
  ['Sunday 13:00', '2026-09-13T17:00:00Z', true],
  ['Monday 00:00, Sunday night running long', '2026-09-14T04:00:00Z', true],
  ['Monday 18:00', '2026-09-14T22:00:00Z', false],
  ['Monday 21:00', '2026-09-15T01:00:00Z', true],
  ['Tuesday 08:00, before FinalizeWeek', '2026-09-15T12:00:00Z', true],
  ['Tuesday 10:00', '2026-09-15T14:00:00Z', false],
  ['Wednesday 20:00', '2026-09-17T00:00:00Z', false],
  ['Thursday 13:00, not Thanksgiving', '2026-09-17T17:00:00Z', false],
  ['Thursday 20:30', '2026-09-18T00:30:00Z', true],
  ['Friday 20:30, a season opener abroad', '2026-09-12T00:30:00Z', true],
  ['Saturday 13:30 in October', '2026-10-17T17:30:00Z', false],
  ['Saturday 13:30 in December', '2026-12-19T18:30:00Z', true],
  ['Saturday 13:30 in January', '2027-01-09T18:30:00Z', true],
  ['the Wednesday before Thanksgiving, 18:00', '2026-11-25T23:00:00Z', true],
  ['a Wednesday a week earlier, 18:00', '2026-11-18T23:00:00Z', false],
  ['Thanksgiving, 12:30', '2026-11-26T17:30:00Z', true],
  ['a Thursday a week earlier, 12:30', '2026-11-19T17:30:00Z', false],
  ['Black Friday, 15:00', '2026-11-27T20:00:00Z', true],
  ['a Friday a week earlier, 15:00', '2026-11-20T20:00:00Z', false],
  ['Christmas morning, 11:00', '2026-12-25T16:00:00Z', false],
  ['Christmas Day, 12:30, a Friday', '2026-12-25T17:30:00Z', true],
  ['the night of Christmas, 00:30', '2026-12-26T05:30:00Z', true],
];

describe('isGameWindow', () => {
  it.each(cases)('%s', (_, utc, expected) => {
    expect(isGameWindow(new Date(utc))).toBe(expected);
  });
});
