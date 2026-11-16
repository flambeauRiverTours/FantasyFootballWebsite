import type { ReactNode } from 'react';
import { settledIdeal } from '../api/calculations';
import type { Standing } from '../api/calculations';
import RecordCell from './RecordCell';
import Spoken from './Spoken';

/** One column of the standings table: its header, its cells and how both are marked. */
export interface StandingsColumn {
  key: string;
  label: ReactNode;
  /** Hidden on narrow screens, where only five columns fit. */
  wide?: boolean;
  /** A narrow column before the team. */
  lead?: boolean;
  /** Drawn in the muted colour: a figure that supports another. */
  muted?: boolean;
  /** The column the table is ranked by. */
  sorted?: boolean;
  cell: (row: Standing) => ReactNode;
}

const TeamColumn: StandingsColumn = { key: 'team', label: 'Team', cell: ({ team }) => team.name };

const ByPoints: readonly StandingsColumn[] = [
  TeamColumn,
  {
    key: 'points',
    label: 'Points',
    sorted: true,
    cell: ({ team }) => team.totals.actual.toFixed(1),
  },
  {
    key: 'gap',
    label: 'Gap',
    wide: true,
    muted: true,
    cell: ({ gap }) => (gap === 0 ? '—' : `−${gap.toFixed(1)}`),
  },
  { key: 'ideal', label: 'Ideal', cell: ({ team }) => team.totals.ideal.toFixed(1) },
];

/**
 * The head-to-head columns (T68). Points for, against and ideal all count
 * settled weeks only, as the record does, so no column moves mid-game.
 */
function byRecord(showTies: boolean, hasMedianGame: boolean): readonly StandingsColumn[] {
  return [
    {
      key: 'rank',
      label: <Spoken shown="#" spoken="Rank" />,
      lead: true,
      muted: true,
      cell: ({ rank }) => rank,
    },
    TeamColumn,
    {
      key: 'record',
      label: hasMedianGame ? (
        <>
          Record
          <span className="record-split" aria-hidden="true">
            games · median
          </span>
        </>
      ) : (
        'Record'
      ),
      sorted: true,
      cell: ({ team }) =>
        team.record ? <RecordCell record={team.record} showTies={showTies} /> : null,
    },
    {
      key: 'gb',
      label: <Spoken shown="GB" spoken="Games back" />,
      muted: true,
      cell: ({ gamesBack }) => (gamesBack === 0 ? '—' : gamesBack?.toFixed(1)),
    },
    {
      key: 'pf',
      label: <Spoken shown="PF" spoken="Points for" />,
      cell: ({ team }) => team.record?.pointsFor.toFixed(1),
    },
    {
      key: 'pa',
      label: <Spoken shown="PA" spoken="Points against" />,
      wide: true,
      cell: ({ team }) => team.record?.pointsAgainst.toFixed(1),
    },
    {
      key: 'ideal',
      label: 'Ideal',
      wide: true,
      cell: ({ team }) => settledIdeal(team).toFixed(1),
    },
  ];
}

/**
 * The standings table's columns. The head, the body and the Skeleton all draw
 * from this one list, so the loading table cannot drift from the real one.
 * @param ranksByRecord Whether the league ranks by record (`hasRecords`).
 * @param showTies Whether any record has a tie, so every record shows three figures.
 * @param hasMedianGame Whether the records carry a median share, labelled in the header.
 */
export function standingsColumns(
  ranksByRecord: boolean,
  showTies = false,
  hasMedianGame = false,
): readonly StandingsColumn[] {
  return ranksByRecord ? byRecord(showTies, hasMedianGame) : ByPoints;
}

/** A header cell's classes: hidden when narrow, or a lead column. */
export function headClass(column: StandingsColumn): string | undefined {
  return joined([column.wide && 'col-wide', column.lead && 'col-lead']);
}

/** A body cell's classes: muted, hidden when narrow, a lead column, emphasised when sorted. */
export function columnClass(column: StandingsColumn): string | undefined {
  return joined([
    column.muted && 'muted',
    column.wide && 'col-wide',
    column.lead && 'col-lead',
    column.sorted && 'is-sorted',
  ]);
}

function joined(classes: (string | false | undefined)[]): string | undefined {
  const present = classes.filter(Boolean);
  return present.length === 0 ? undefined : present.join(' ');
}
