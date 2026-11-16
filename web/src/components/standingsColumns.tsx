import type { ReactNode } from 'react';
import { recordSpoken, recordText, settledIdeal } from '../api/calculations';
import type { Standing } from '../api/calculations';
import Spoken from './Spoken';

/** One column of the standings table: its header, its cells and how both are marked. */
export interface StandingsColumn {
  key: string;
  label: ReactNode;
  /** Hidden on narrow screens, where only four columns fit. */
  wide?: boolean;
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
function byRecord(showTies: boolean): readonly StandingsColumn[] {
  return [
    TeamColumn,
    {
      key: 'record',
      label: 'Record',
      sorted: true,
      cell: ({ team }) =>
        team.record ? (
          <Spoken
            shown={recordText(team.record, showTies)}
            spoken={recordSpoken(team.record, showTies)}
          />
        ) : null,
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
    { key: 'ideal', label: 'Ideal', cell: ({ team }) => settledIdeal(team).toFixed(1) },
  ];
}

/**
 * The standings table's columns. The head, the body and the Skeleton all draw
 * from this one list, so the loading table cannot drift from the real one.
 * @param ranksByRecord Whether the league ranks by record (`hasRecords`).
 * @param showTies Whether any record has a tie, so every record shows three figures.
 */
export function standingsColumns(
  ranksByRecord: boolean,
  showTies = false,
): readonly StandingsColumn[] {
  return ranksByRecord ? byRecord(showTies) : ByPoints;
}

/** A body cell's classes: muted, hidden when narrow, emphasised when sorted. */
export function columnClass(column: StandingsColumn): string | undefined {
  const classes = [
    column.muted && 'muted',
    column.wide && 'col-wide',
    column.sorted && 'is-sorted',
  ].filter(Boolean);
  return classes.length === 0 ? undefined : classes.join(' ');
}
