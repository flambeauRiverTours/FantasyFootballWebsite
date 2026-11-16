import type { CSSProperties } from 'react';
import type { MatchupTeamPayload } from '../api/types';

interface MatchupSideProps {
  side: MatchupTeamPayload;
  /** Its team colour; none for a roster with no table row (A1). */
  colour: string | undefined;
  ahead: boolean;
  won: boolean;
  /** What a screen reader hears after the points; empty for nothing. */
  spoken: string;
  /** The second side, which a screen reader hears after "versus". */
  versus: boolean;
}

/** One side's line: swatch, name, points. */
export default function MatchupSide({
  side,
  colour,
  ahead,
  won,
  spoken,
  versus,
}: MatchupSideProps) {
  const unranked = colour === undefined;
  return (
    <p className={won ? 'matchup-side is-winner' : 'matchup-side'}>
      {versus ? <span className="visually-hidden">versus </span> : null}
      <span
        className={unranked ? 'swatch is-unranked' : 'swatch'}
        aria-hidden="true"
        style={{ '--swatch': colour } as CSSProperties}
      />
      <span>
        {side.name}
        {unranked ? (
          <>
            {' '}
            <span className="matchup-tag">(not ranked)</span>
          </>
        ) : null}
      </span>{' '}
      <span className={ahead ? 'matchup-points is-ahead' : 'matchup-points'}>
        {side.actual.toFixed(1)}
      </span>
      {spoken ? <span className="visually-hidden">, {spoken}</span> : null}
    </p>
  );
}
