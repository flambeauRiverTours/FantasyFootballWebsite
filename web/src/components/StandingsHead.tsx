/**
 * The standings table's columns. The Skeleton draws from the same list, so the
 * loading table cannot drift from the real one and nothing jumps when it swaps.
 */
export const StandingsColumns = [
  { label: 'Team' },
  { label: 'Points', sorted: true },
  // hidden on narrow screens, where only team, points and ideal fit
  { label: 'Gap', className: 'col-gap' },
  { label: 'Ideal' },
] as const satisfies readonly { label: string; sorted?: boolean; className?: string }[];

/** The standings table's header row, with the sorted column marked. */
export default function StandingsHead() {
  return (
    <thead>
      <tr>
        {StandingsColumns.map((column) => (
          <th
            key={column.label}
            scope="col"
            className={'className' in column ? column.className : undefined}
            aria-sort={'sorted' in column ? 'descending' : undefined}
          >
            {column.label}
          </th>
        ))}
      </tr>
    </thead>
  );
}
