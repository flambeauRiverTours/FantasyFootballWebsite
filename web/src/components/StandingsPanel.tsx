import { extent } from 'd3-array';
import { scaleLinear } from 'd3-scale';
import { hasConsistency } from '../api/types';
import type { TeamPayload } from '../api/types';
import Panel from './Panel';
import SigmaDisplay from './SigmaDisplay';

interface StandingsPanelProps {
  teams: TeamPayload[];
  finalThroughWeek: number;
}

/*
 * Computes the range scale for sigmas
 * @param teams Team payload to calculate the range scale over
 * @returns Null for various edge cases, otherwise range scale
 */
function rangeScale(teams: TeamPayload[]) {
  const consistent = teams.map((t) => t.totals).filter(hasConsistency);
  const [lo, hi] = extent(consistent.flatMap((t) => [t.min, t.max]));
  return lo === undefined || lo === hi ? null : scaleLinear().domain([lo, hi]).range([2, 138]);
}

export default function StandingsPanel({ teams, finalThroughWeek }: StandingsPanelProps) {
  const ranked = [...teams].sort((a, b) => b.totals.actual - a.totals.actual);
  const leader = ranked[0]?.totals.actual ?? 0;

  // holding the scale is the same fact as having a sigma column to draw
  const mark = rangeScale(ranked);

  return (
    <Panel id="standings" title="Standings">
      {ranked.length === 0 ? (
        <p className="muted">No teams to show yet.</p>
      ) : (
      <div className="table-wrap">
        <table>
          <thead>
            <tr>
              <th scope="col">Team</th>
              <th scope="col" aria-sort="descending">Points</th>
              {mark ? (
                <th scope="col">
                  <span className="symbol">σ</span>
                  <span className="th-note">through wk {finalThroughWeek}</span>
                </th>
              ) : null}
              <th scope="col" className="col-ideal">Ideal</th>
              <th scope="col">Gap</th>
            </tr>
          </thead>
          <tbody>
            {ranked.map((team) => (
              <tr key={team.rosterId}>
                <td>{team.name}</td>
                <td className="is-sorted">{team.totals.actual.toFixed(1)}</td>
                {mark ? (
                  <td>
                    {hasConsistency(team.totals) ? (
                      <SigmaDisplay totals={team.totals} scaleFunction={mark} />
                    ) : (
                      <span className="muted">—</span>
                    )}
                  </td>
                ) : null}
                <td className="col-ideal">{team.totals.ideal.toFixed(1)}</td>
                <td className="muted">
                  {team.totals.actual === leader
                    ? '—'
                    : `−${(leader - team.totals.actual).toFixed(1)}`}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      )}
    </Panel>
  );
}
