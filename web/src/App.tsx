import { useSeason } from './hooks/useSeason';
import StandingsPanel from './components/StandingsPanel';
import ChartPanel from './components/ChartPanel';
import RadarPanel from './components/RadarPanel';
import { useRelativeTime } from './hooks/useRelativeTime';
import { useElapsedSince } from './hooks/useElapsedSince';
import Skeleton from './components/Skeleton';
import LoadFailed from './components/LoadFailed';
import StaleBanner from './components/StaleBanner';
import Masthead from './components/Masthead';
/**
 * Owns the fetch and its three states, then orders the panels. A panel may
 * assume a payload exists.
 */
export default function App() {
  const { payload, error, loading, refetch } = useSeason(null);
  const updated = useRelativeTime(payload?.asOfUtc);
  const coldStartHandler = useElapsedSince(loading, 3000);
  const mastHead = (<Masthead payload={payload} updatedAgo={updated} error={!!error} />)
  //payload wins. error only matters when there is nothing to show
  if (!payload) {
    return loading ? (<>
      {mastHead}
      <Skeleton coldStart={coldStartHandler} />
    </>
    ) : (
      <>
        {mastHead}
        <LoadFailed error={error} onRetry={refetch} />
      </>
    );
  }

  // M6 fills leagueSlotAverages; until then the radar has nothing to plot, so the
  // data answers the question and no flag has to be flipped when T48 ships.
  // ?radar forces the panel open to build against hand-written data first.
  const hasRadar =
    Object.keys(payload.leagueSlotAverages).length > 0 ||
    new URLSearchParams(window.location.search).has('radar');

  return (
    <>
      {mastHead}

      {error ? <StaleBanner asOfUtc={payload.asOfUtc} onRetry={refetch} /> : null}

      <main className="panels">
        <StandingsPanel teams={payload.teams} finalThroughWeek={payload.finalThroughWeek} />
        <ChartPanel payload={payload} />
        {hasRadar ? <RadarPanel payload={payload} /> : null}
      </main>
    </>
  );
}
