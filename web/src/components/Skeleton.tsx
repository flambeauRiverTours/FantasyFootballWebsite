import Panel from './Panel';

interface SkeletonProps {
    /** The database may be resuming. Say so rather than implying seconds. */
    coldStart?: boolean;
}

const Rows = [0, 1, 2, 3, 4];
const Columns = ['Team', 'Points', 'Ideal', 'Gap'];

/*Skeleton of StandingsPanel that's shown while loading */
export default function Skeleton({ coldStart = false }: SkeletonProps) {
    return (
        <main className="panels">
            {/* same id as the real panel so nothing moves on load */}
            <Panel id="standings" title={coldStart ? 'Waking the database…' : 'Loading the season…'}>
                <div className="table-wrap" aria-busy="true">
                    <table className="skeleton">
                        <thead>
                            <tr>
                                {Columns.map((c) => (
                                    <th key={c} scope="col">
                                        {c}
                                    </th>
                                ))}
                            </tr>
                        </thead>
                        <tbody>
                            {Rows.map((r) => (
                                <tr key={r}>
                                    {Columns.map((c) => (
                                        <td key={c}>
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
