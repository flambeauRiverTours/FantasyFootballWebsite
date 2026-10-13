import { useEffect } from 'react';
import { useSeason } from './hooks/useSeason';
import StandingsPanel from './components/StandingsPanel';
import ChartPanel from './components/ChartPanel';
import RadarPanel from './components/RadarPanel';
import ConsistencyPanel from './components/ConsistencyPanel';
import { consistencyRows } from './api/calculations';
import { useRelativeTime } from './hooks/useRelativeTime';
import { useElapsedSince } from './hooks/useElapsedSince';
import Skeleton from './components/Skeleton';
import LoadFailed from './components/LoadFailed';
import StaleBanner from './components/StaleBanner';
import Masthead from './components/Masthead';
import { LazyMotion, MotionConfig, domAnimation } from 'motion/react';
import { useQueryState } from './hooks/useQueryState';

/**
 * Owns the fetch and its three states, then orders the panels. A panel may
 * assume a payload exists.
 */
export default function App() {
  // ?season=2025 asks for a past season; anything that is not a year means the current one
  const [seasonParam] = useQueryState('season', '');
  const season = /^\d{4}$/.test(seasonParam) ? Number(seasonParam) : null;

  const { payload, error, loading, refetch } = useSeason(season);
  const updated = useRelativeTime(payload?.asOfUtc);
  // a wait this long is the database resuming, and the Skeleton says so
  const coldStart = useElapsedSince(loading, 3000);

  // T55: #boot is index.html's transparent loading overlay: a ball thrown above
  // the Skeleton, painted before this bundle runs. It stays until the first
  // response, not just until mount, because the bundle parses long before a
  // cold start (the wait it covers for) resolves - removed on mount, it would
  // never be seen.
  useEffect(() => {
    if (!loading) document.getElementById('boot')?.remove();
  }, [loading]);

  // index.html's title stands in until the league's own name arrives
  const leagueName = payload?.leagueName;
  useEffect(() => {
    if (leagueName) document.title = leagueName;
  }, [leagueName]);

  // the radar appears once any slot has a league average to compare against
  const hasRadar = Object.keys(payload?.leagueSlotAverages ?? {}).length > 0;
  // consistency appears once sigma exists, at four finalized weeks (D1)
  const consistency = consistencyRows(payload?.teams ?? []);

  return (
    // reducedMotion="user" makes layout animations, not only explicit ones, respect the preference
    <MotionConfig reducedMotion="user">
      <LazyMotion features={domAnimation}>
        <Masthead payload={payload} updatedAgo={updated} />

        {!payload ? (
          loading ? (
            <Skeleton coldStart={coldStart} />
          ) : (
            <LoadFailed error={error} onRetry={refetch} />
          )
        ) : (
          <>
            {error ? <StaleBanner asOfUtc={payload.asOfUtc} onRetry={refetch} /> : null}

            <main className="panels">
              <StandingsPanel teams={payload.teams} />
              {consistency.length > 0 ? (
                <ConsistencyPanel payload={payload} rows={consistency} />
              ) : null}
              <ChartPanel payload={payload} />
              {hasRadar ? <RadarPanel payload={payload} /> : null}
            </main>
          </>
        )}
      </LazyMotion>
    </MotionConfig>
  );
}
