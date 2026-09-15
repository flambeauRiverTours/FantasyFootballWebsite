import { useSeason } from './hooks/useSeason';
import StandingsPanel from './components/StandingsPanel';
import ChartPanel from './components/ChartPanel';
import RadarPanel from './components/RadarPanel';

/**
 * Owns the fetch and its three states, then orders the panels. A panel may
 * assume a payload exists.
 */
export default function App() {
  const { payload, error, loading } = useSeason(null);

  if (loading) return <p>Loading…</p>;
  if (error) return <p role="alert">Could not load the season: {error.message}</p>;
  if (!payload) return <p>No season data.</p>;

  // M6 fills leagueSlotAverages; until then the radar has nothing to plot, so the
  // data answers the question and no flag has to be flipped when T48 ships.
  // ?radar forces the panel open to build against hand-written data first.
  const hasRadar =
    Object.keys(payload.leagueSlotAverages).length > 0 ||
    new URLSearchParams(window.location.search).has('radar');

  return (
    <>
      <header className="masthead">
        <div className="panel-inner">
          <h1>Bosty Base Fantasy Football</h1>
          <p className="muted">
            {payload.season} · Week {payload.currentWeek}
          </p>
        </div>
      </header>

      <main className="panels">
        <StandingsPanel teams={payload.teams} finalThroughWeek={payload.finalThroughWeek} />
        <ChartPanel payload={payload} />
        {hasRadar ? <RadarPanel payload={payload} /> : null}
      </main>
    </>
  );
}
