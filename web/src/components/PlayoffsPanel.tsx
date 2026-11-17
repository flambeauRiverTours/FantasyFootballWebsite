import { bracketLeader, bracketSpoken } from '../api/calculations';
import { teamColours } from '../api/teamColours';
import type {
  BracketMatchPayload,
  BracketSidePayload,
  PlayoffsPayload,
  RoundPayload,
  SeasonPayload,
} from '../api/types';
import MatchupSide from './MatchupSide';
import Panel from './Panel';

interface PlayoffsPanelProps {
  payload: SeasonPayload;
  /** The payload's own; the page shows the panel only when there is one (A46). */
  playoffs: PlayoffsPayload;
}

/** The winners bracket, a column per round (T69). */
export default function PlayoffsPanel({ payload, playoffs }: PlayoffsPanelProps) {
  const colours = teamColours(payload.teams);
  const champions = new Set(payload.champions);
  const labels = new Map(playoffs.rounds.flatMap((r) => r.matches.map((m) => [m.match, m.label])));
  return (
    <Panel id="playoffs" title="Playoffs">
      {/* focusable and named, so a keyboard can scroll it when its columns overflow */}
      <div className="bracket" role="region" aria-label="Playoff bracket" tabIndex={0}>
        {playoffs.rounds.map((round) => (
          <div key={round.round} className="bracket-round">
            <h3>{roundHeading(round)}</h3>
            {/* the role keeps list semantics in Safari, which drops them with list-style: none */}
            <ul role="list" className="matchups">
              {round.matches.map((match) => (
                <MatchCard
                  key={match.match}
                  match={match}
                  colours={colours}
                  champions={champions}
                  labels={labels}
                />
              ))}
            </ul>
          </div>
        ))}
      </div>
    </Panel>
  );
}

interface MatchCardProps {
  match: BracketMatchPayload;
  colours: ReadonlyMap<number, string>;
  champions: ReadonlySet<number>;
  /** Every match's label by id, to name where a side still to come comes from. */
  labels: ReadonlyMap<number, string>;
}

/** One match: its label in words, then its two sides as the matchups show them. */
function MatchCard({ match, colours, champions, labels }: MatchCardProps) {
  const leader = bracketLeader(match);
  const spoken = bracketSpoken(match);
  return (
    <li className="matchup">
      <p className="bracket-label">{match.label}</p>
      {match.sides.map((side, i) => (
        <MatchupSide
          key={i}
          side={{
            rosterId: side.rosterId,
            name: side.name ?? source(side, labels),
            actual: side.points,
          }}
          colour={side.rosterId === null ? undefined : colours.get(side.rosterId)}
          ahead={leader === i}
          won={side.won}
          spoken={spoken[i] ?? ''}
          versus={i === 1}
          crown={
            match.placement === 1 && side.rosterId !== null && champions.has(side.rosterId)
              ? 'Champion'
              : undefined
          }
          note={side.won && spoken[i] === 'won, on tiebreak' ? '(tiebreak)' : undefined}
        />
      ))}
    </li>
  );
}

/** A round's heading: its name, its week and whether it is live or settled, as the matchups title says (A43). */
function roundHeading({ name, weeks, matches }: RoundPayload): string {
  const week =
    weeks.length > 1 ? `Weeks ${weeks[0]}–${weeks[weeks.length - 1]}` : `Week ${weeks[0]}`;
  if (matches.every((m) => m.isSettled)) {
    return `${name} · ${week} results`;
  }
  const started = matches.some((m) => m.sides.some((s) => s.points !== null));
  return started ? `${name} · ${week} · live` : `${name} · ${week}`;
}

/** Where a side still to come comes from, by the label its source match carries. */
function source({ from }: BracketSidePayload, labels: ReadonlyMap<number, string>): string {
  const name = (match: number) => labels.get(match) ?? `match ${match}`;
  if (from?.winnerOf !== undefined) {
    return `Winner of ${name(from.winnerOf)}`;
  }
  return from?.loserOf !== undefined ? `Loser of ${name(from.loserOf)}` : 'To be decided';
}
