import type { CSSProperties } from 'react';
import ChampionName from './ChampionName';

/** A side as a card shows it: a known roster, or one still to come (T69). */
export interface SideLine {
  rosterId: number | null;
  name: string;
  /** Null before its round starts. */
  actual: number | null;
}

interface MatchupSideProps {
  side: SideLine;
  /** Its team colour; none for a roster with no table row (A1) or one still to come. */
  colour: string | undefined;
  ahead: boolean;
  won: boolean;
  /** What a screen reader hears after the points; empty for nothing. */
  spoken: string;
  /** The second side, which a screen reader hears after "versus". */
  versus: boolean;
  /** The crown's name, for the bracket's champion (T70). */
  crown?: string;
  /** A muted word after the name that the screen reader already hears, e.g. "(tiebreak)". */
  note?: string;
}

/** One side's line: swatch, name, points. */
export default function MatchupSide({
  side,
  colour,
  ahead,
  won,
  spoken,
  versus,
  crown,
  note,
}: MatchupSideProps) {
  const unknown = side.rosterId === null;
  const unranked = !unknown && colour === undefined;
  const classes = ['matchup-side', won && 'is-winner', unknown && 'is-unknown'].filter(Boolean);
  return (
    <p className={classes.join(' ')}>
      {versus ? <span className="visually-hidden">versus </span> : null}
      <span
        className={unranked ? 'swatch is-unranked' : 'swatch'}
        aria-hidden="true"
        style={{ '--swatch': colour } as CSSProperties}
      />
      <span>
        {crown ? <ChampionName name={side.name} title={crown} /> : side.name}
        {unranked ? (
          <>
            {' '}
            <span className="matchup-tag">(not ranked)</span>
          </>
        ) : null}
        {note ? (
          <>
            {' '}
            <span className="matchup-tag" aria-hidden="true">
              {note}
            </span>
          </>
        ) : null}
      </span>{' '}
      <span className={ahead ? 'matchup-points is-ahead' : 'matchup-points'}>
        {side.actual === null ? '–' : side.actual.toFixed(1)}
      </span>
      {spoken ? <span className="visually-hidden">, {spoken}</span> : null}
    </p>
  );
}
