import { matchupLeader, matchupSpoken, weekStarted } from '../api/calculations';
import { teamColours } from '../api/teamColours';
import type { MatchupsPayload, SeasonPayload } from '../api/types';
import MatchupSide from './MatchupSide';
import MatchupsNotes from './MatchupsNotes';
import Panel from './Panel';

interface MatchupsPanelProps {
  payload: SeasonPayload;
  /** The payload's own; the page renders the panel only when there are any (A41). */
  matchups: MatchupsPayload;
}

/** One week's games, higher score bold (A43). */
export default function MatchupsPanel({ payload, matchups }: MatchupsPanelProps) {
  const colours = teamColours(payload.teams);
  const { week, isFinal } = matchups;
  const state = isFinal ? 'results' : weekStarted(matchups) ? '· live' : '· upcoming';

  return (
    <Panel id="matchups" title={`Week ${week} ${state}`}>
      {/* the role keeps list semantics in Safari, which drops them with list-style: none */}
      <ul role="list" className="matchups">
        {matchups.games.map((game) => {
          const leader = matchupLeader(game);
          const spoken = matchupSpoken(game, isFinal);
          return (
            <li key={game.matchupId} className="matchup">
              {game.teams.map((side, i) => (
                <MatchupSide
                  key={side.rosterId}
                  side={side}
                  colour={colours.get(side.rosterId)}
                  ahead={leader === i}
                  won={isFinal && leader === i}
                  spoken={spoken[i] ?? ''}
                  versus={i === 1}
                />
              ))}
            </li>
          );
        })}
      </ul>
      <MatchupsNotes matchups={matchups} ranked={colours} />
    </Panel>
  );
}
