/** The parts of a moment that NFL scheduling is decided by, in Eastern time. */
interface Eastern {
  weekday: string;
  month: string;
  date: number;
  hour: number;
}

const EasternParts = new Intl.DateTimeFormat('en-US', {
  timeZone: 'America/New_York',
  weekday: 'short',
  month: 'short',
  day: 'numeric',
  hour: 'numeric',
  // h23, not hour12: false, which some engines render as 24 at midnight
  hourCycle: 'h23',
});

function eastern(at: Date): Eastern {
  const parts = EasternParts.formatToParts(at);
  const part = (type: Intl.DateTimeFormatPartTypes) =>
    parts.find((p) => p.type === type)?.value ?? '';

  return {
    weekday: part('weekday'),
    month: part('month'),
    date: Number(part('day')),
    hour: Number(part('hour')),
  };
}

/** Thanksgiving is the fourth Thursday of November, so it falls between the 22nd and the 28th. */
function isThanksgivingWeek(month: string, date: number, offset: number): boolean {
  return month === 'Nov' && date >= 22 + offset && date <= 28 + offset;
}

/**
 * Whether an NFL game may be in progress, in Eastern time (D6). A poll outside
 * these windows would only ever find the same numbers.
 * @param at The moment to check.
 * @returns True inside a game window, or in the hours after one that a late
 * game or the Tuesday finalize run can still change.
 */
export function isGameWindow(at: Date): boolean {
  const { weekday, month, date, hour } = eastern(at);
  const lateSeason = month === 'Dec' || month === 'Jan';

  // Christmas has hosted games on every weekday, so it is checked apart from the week
  if (month === 'Dec' && ((date === 25 && hour >= 12) || (date === 26 && hour < 1))) return true;

  switch (weekday) {
    case 'Wed':
      // the night before Thanksgiving has hosted a game
      return isThanksgivingWeek(month, date, -1) && hour >= 17;
    case 'Thu':
      return (isThanksgivingWeek(month, date, 0) && hour >= 12) || hour >= 20;
    case 'Fri':
      // Thursday's game runs past midnight; Black Friday has an afternoon game; openers abroad play Friday night
      return hour < 1 || (isThanksgivingWeek(month, date, 1) && hour >= 14) || hour >= 20;
    case 'Sat':
      return hour < 1 || (lateSeason && hour >= 13);
    case 'Sun':
      // from 9:00 for the London and Europe games
      return hour < 1 || hour >= 9;
    case 'Mon':
      return hour < 1 || hour >= 20;
    case 'Tue':
      // the Monday night game, then FinalizeWeek at 12:00 UTC, 07:00 or 08:00 in New York
      return hour < 9;
    default:
      return false;
  }
}
