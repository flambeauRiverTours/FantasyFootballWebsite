import { medianSplit, weekStarted } from '../api/calculations';
import type { MatchupsPayload, MatchupTeamPayload } from '../api/types';

interface MatchupsNotesProps {
  matchups: MatchupsPayload;
  /** The rosters with a table row; any other is named "(not ranked)". */
  ranked: ReadonlyMap<number, string>;
}

/** The median line and the live note (A43). */
export default function MatchupsNotes({ matchups, ranked }: MatchupsNotesProps) {
  if (!weekStarted(matchups)) {
    return null;
  }
  const { isFinal, median } = matchups;
  const { above, on } = medianSplit(matchups);
  const names = (sides: MatchupTeamPayload[]) =>
    sides.map((t) => (ranked.has(t.rosterId) ? t.name : `${t.name} (not ranked)`)).join(', ');
  // two decimals: the list compares exact scores, so one decimal could list a side level with it
  const line =
    median === undefined
      ? null
      : `Median ${median.toFixed(2)}${isFinal ? '' : ' so far'}: ` +
        `${above.length === 0 ? 'nobody' : names(above)} above it` +
        `${on.length === 0 ? '' : `; ${names(on)} on it`}.`;

  return (
    <>
      {line ? <p className="muted matchups-note">{line}</p> : null}
      {isFinal ? null : (
        <p className="muted matchups-note">
          Live scores: the table adds them once the week is final.
        </p>
      )}
    </>
  );
}
