import Panel from './Panel';
import StandingsHead, { StandingsColumns } from './StandingsHead';

interface SkeletonProps {
  /** The database may be resuming. Say so rather than implying seconds. */
  coldStart?: boolean;
}

const Rows = [0, 1, 2, 3, 4];

/** The standings panel's shape, shown while the first response is on its way. */
export default function Skeleton({ coldStart = false }: SkeletonProps) {
  return (
    <main className="panels">
      {/* same id as the real panel so nothing moves on load */}
      <Panel id="standings" title={coldStart ? 'Waking the database…' : 'Loading the season…'}>
        <div className="table-wrap" aria-busy="true">
          <table className="skeleton">
            <StandingsHead />
            <tbody>
              {Rows.map((r) => (
                <tr key={r}>
                  {StandingsColumns.map((column) => (
                    <td
                      key={column.label}
                      className={'className' in column ? column.className : undefined}
                    >
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
