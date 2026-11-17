import { LazyMotion, m } from 'motion/react';
import {
  championNames,
  hasMedianGame,
  hasRecords,
  hasTies,
  standingsGroups,
} from '../api/calculations';
import type { SeasonPayload } from '../api/types';
import { RowSlide, loadLayout } from './motion';
import Panel from './Panel';
import StandingsHead from './StandingsHead';
import { columnClass, standingsColumns } from './standingsColumns';

interface StandingsPanelProps {
  payload: SeasonPayload;
}

/**
 * The league table, ranked by points, or by record in head-to-head (T68), grouped by
 * division when the league has them (T72b): a body per division, opened by a header row.
 * Rows slide past each other when a poll changes the order, within their own division.
 *
 * No sigma column: its range mark was illegible at table size (A17). Sigma
 * has its own panel instead: ConsistencyPanel (T59).
 */
export default function StandingsPanel({ payload }: StandingsPanelProps) {
  const byRecord = hasRecords(payload);
  const groups = standingsGroups(payload);
  const columns = standingsColumns(byRecord, hasTies(payload.teams), hasMedianGame(payload.teams));
  // head-to-head, but a counted week still lacks its games: the points table stands in
  const waiting = payload.format === 'HeadToHead' && !byRecord;
  // in words as well as the crown: on a phone the masthead's "Season completed" is hidden
  const champions = championNames(payload);

  return (
    <Panel id="standings" title="Standings">
      {payload.teams.length === 0 ? null : (
        <>
          <div className="table-wrap">
            <table>
              <StandingsHead columns={columns} />
              <LazyMotion features={loadLayout}>
                {groups.map(({ division, rows }) => (
                  <tbody key={division?.number ?? 0}>
                    {division ? (
                      <tr className="division-head">
                        {/* a rowgroup header: some screen readers announce it with each row */}
                        <th scope="rowgroup" colSpan={columns.length}>
                          {division.name}
                        </th>
                      </tr>
                    ) : null}
                    {rows.map((row) => (
                      <m.tr
                        layout="position"
                        key={row.team.rosterId}
                        transition={RowSlide}
                        className={row.champion ? 'is-champion' : undefined}
                      >
                        {columns.map((column) => (
                          <td key={column.key} className={columnClass(column)}>
                            {column.cell(row)}
                          </td>
                        ))}
                      </m.tr>
                    ))}
                  </tbody>
                ))}
              </LazyMotion>
            </table>
          </div>
          {champions.length > 0 ? (
            <p className="muted">
              Season complete. {champions.length > 1 ? 'Co-champions' : 'Champion'}:{' '}
              {new Intl.ListFormat('en', { type: 'conjunction' }).format(champions)}.
            </p>
          ) : null}
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
