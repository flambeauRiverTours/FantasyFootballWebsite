import { headClass } from './standingsColumns';
import type { StandingsColumn } from './standingsColumns';

interface StandingsHeadProps {
  columns: readonly StandingsColumn[];
}

/** The standings table's header row, with the sorted column marked. */
export default function StandingsHead({ columns }: StandingsHeadProps) {
  return (
    <thead>
      <tr>
        {columns.map((column) => (
          <th
            key={column.key}
            scope="col"
            className={headClass(column)}
            aria-sort={column.sorted ? 'descending' : undefined}
          >
            {column.label}
          </th>
        ))}
      </tr>
    </thead>
  );
}
