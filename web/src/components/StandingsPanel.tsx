import { m } from 'motion/react';
import { hasMedianGame, hasRecords, hasTies, standings } from '../api/calculations';
import type { SeasonPayload } from '../api/types';
import { RowSlide } from './motion';
import Panel from './Panel';
import StandingsHead from './StandingsHead';
import { columnClass, standingsColumns } from './standingsColumns';

interface StandingsPanelProps {
  payload: SeasonPayload;
}

/**
 * The league table, ranked by points, or by record in head-to-head (T68).
 * Rows slide past each other when a poll changes the order.
 *
 * No sigma column: its range mark was illegible at table size (A17). Sigma
 * has its own panel instead: ConsistencyPanel (T59).
 */
export default function StandingsPanel({ payload }: StandingsPanelProps) {
  const byRecord = hasRecords(payload);
  const rows = standings(payload);
  const columns = standingsColumns(byRecord, hasTies(payload.teams), hasMedianGame(payload.teams));
  // head-to-head, but a counted week still lacks its games: the points table stands in
  const waiting = payload.format === 'HeadToHead' && !byRecord;

  return (
    <Panel id="standings" title="Standings">
      {rows.length === 0 ? null : (
        <>
          <div className="table-wrap">
            <table>
              <StandingsHead columns={columns} />
              <tbody>
                {rows.map((row) => (
                  <m.tr layout key={row.team.rosterId} transition={RowSlide}>
                    {columns.map((column) => (
                      <td key={column.key} className={columnClass(column)}>
                        {column.cell(row)}
                      </td>
                    ))}
                  </m.tr>
                ))}
              </tbody>
            </table>
          </div>
          {waiting ? (
            <p className="muted">
              Records are still loading, so the table is ranked by points for now.
            </p>
          ) : null}
        </>
      )}
    </Panel>
  );
}
