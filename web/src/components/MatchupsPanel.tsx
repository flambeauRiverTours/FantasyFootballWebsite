import type { CSSProperties } from 'react';
import { matchupStatus } from '../api/calculations';
import { teamColours } from '../api/teamColours';
import type { MatchupsPayload, SeasonPayload } from '../api/types';
import Panel from './Panel';

interface MatchupsPanelProps {
  payload: SeasonPayload;
  /** The payload's own; the page renders the panel only when there are any (A41). */
  matchups: MatchupsPayload;
}

/**
 * One week's games in head-to-head (T71): a card per game, each side a line read
 * as written: name, points, status. Nothing is announced as a poll changes the
 * scores, and nothing moves.
 */
export default function MatchupsPanel({ payload, matchups }: MatchupsPanelProps) {
  const colours = teamColours(payload.teams);
  const title = `Week ${matchups.week} ${matchups.isFinal ? 'results' : 'matchups'}`;

  return (
    <Panel id="matchups" title={title}>
      {/* the role keeps list semantics in Safari, which drops them with list-style: none */}
      <ul role="list" className="matchups">
        {matchups.games.map((game) => {
          const status = matchupStatus(game, matchups.isFinal);
          return (
            <li key={game.matchupId} className="matchup">
              {game.teams.map((side, i) => (
                <p
                  key={side.rosterId}
                  className={status[i] === 'Won' ? 'matchup-side is-winner' : 'matchup-side'}
                >
                  <span
                    className="swatch"
                    aria-hidden="true"
                    style={{ '--swatch': colours.get(side.rosterId) } as CSSProperties}
                  />
                  <span>{side.name}</span>{' '}
                  <span className="matchup-points">{side.actual.toFixed(1)}</span>{' '}
                  <span className="matchup-status">{status[i]}</span>
                </p>
              ))}
            </li>
          );
        })}
      </ul>
    </Panel>
  );
}
