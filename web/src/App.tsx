import { useSeason } from './hooks/useSeason';
import { hasConsistency } from './api/types';
import './App.css';

/**
 * T32 only proves the pipe: fetch, types, polling and the 304 path.
 * T33 brings tokens, T34 the real table.
 */
export default function App() {
  const { payload, error, loading } = useSeason(null);

  if (loading) return <p>Loading…</p>;
  if (error) return <p role="alert">Could not load the season: {error.message}</p>;
  if (!payload) return <p>No season data.</p>;

  const ranked = [...payload.teams].sort((a, b) => b.totals.actual - a.totals.actual);
  const leader = ranked[0]?.totals.actual ?? 0;
  const hasAnyConsistency = hasConsistency(ranked[0].totals);
  return (
    <main>
      <h1>Bosty Base Fantasy Football Standings</h1>
      <p>
        {payload.season} · Week {payload.currentWeek} · Final Through{' '}
        {payload.finalThroughWeek}
      </p>

      <table>
        <thead>
            <tr>
              <th scope="col">Team</th>
              <th scope="col">Points</th>
              <th scope="col">Ideal</th>
              <th scope="col">Gap</th>
              {hasAnyConsistency ? 
              <th scope="col">σ</th> : null }
            </tr>
        </thead>
        <tbody>
          {ranked.map((team) => (
            <tr key={team.rosterId}>
              <td>{team.name}</td>
              <td>{team.totals.actual.toFixed(1)}</td>
              <td>{team.totals.ideal.toFixed(1)}</td>
              <td>
                {team.totals.actual === leader
                  ? '—'
                  : (team.totals.actual - leader).toFixed(1)}
              </td>
              {hasAnyConsistency ? <td>team.totals.sigma.toFixed(1)</td> : null }
            </tr>
          ))}
        </tbody>
      </table>
    </main>
  );
}
