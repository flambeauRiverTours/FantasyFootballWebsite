import Panel from './Panel';
import StandingsHead from './StandingsHead';
import { columnClass, standingsColumns } from './standingsColumns';

interface SkeletonProps {
  /** The database may be resuming. Say so rather than implying seconds. */
  coldStart?: boolean;
}

const Rows = [0, 1, 2, 3, 4];

// the format is unknown until the season loads, so the points table stands in (T68)
const Columns = standingsColumns(false);

/** The standings panel's shape, shown while the first response is on its way. */
export default function Skeleton({ coldStart = false }: SkeletonProps) {
  return (
    <main className="panels">
      {/* same id as the real panel so nothing moves on load */}
      <Panel id="standings" title={coldStart ? 'Waking the database…' : 'Loading the season…'}>
        <div className="table-wrap" aria-busy="true">
          <table className="skeleton">
            <StandingsHead columns={Columns} />
            <tbody>
              {Rows.map((r) => (
                <tr key={r}>
                  {Columns.map((column) => (
                    <td key={column.key} className={columnClass(column)}>
                      <span />
                    </td>
                  ))}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </Panel>
    </main>
  );
}
