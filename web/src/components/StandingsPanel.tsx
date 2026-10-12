import { m } from 'motion/react';
import { standings } from '../api/pointCalculation';
import type { TeamPayload } from '../api/types';
import Panel from './Panel';

interface StandingsPanelProps {
  teams: TeamPayload[];
}

/**
 * The league table, ranked by points. Rows slide past each other when a poll
 * changes the order.
 *
 * No sigma column: its range mark was illegible at table size (A17). Sigma
 * gets a panel of its own instead - see T59.
 */
export default function StandingsPanel({ teams }: StandingsPanelProps) {
  const rows = standings(teams);

  return (
    <Panel id="standings" title="Standings">
      {rows.length === 0 ? null : (
        <div className="table-wrap">
          <table>
            <thead>
              <tr>
                <th scope="col">Team</th>
                <th scope="col" aria-sort="descending">
                  Points
                </th>
                <th scope="col" className="col-gap">
                  Gap
                </th>
                <th scope="col">Ideal</th>
              </tr>
            </thead>
            <tbody>
              {rows.map(({ team, gap }) => (
                <m.tr
                  layout
                  key={team.rosterId}
                  transition={{ type: 'spring', stiffness: 400, damping: 40 }}
                >
                  <td>{team.name}</td>
                  <td className="is-sorted">{team.totals.actual.toFixed(1)}</td>
                  <td className="muted col-gap">{gap === 0 ? '—' : `−${gap.toFixed(1)}`}</td>
                  <td>{team.totals.ideal.toFixed(1)}</td>
                </m.tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </Panel>
  );
}
