import { useEffect, useLayoutEffect } from 'react';
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
  const coldStartHandler = useElapsedSince(loading, 3000);
  const mastHead = <Masthead payload={payload} updatedAgo={updated} error={!!error} />;

  // T55: #boot is a full-screen opaque overlay, so it can stay up over
  // whatever React renders underneath - including this component's own
  // Skeleton - until there's something real to reveal. Removing it on mount
  // instead of on !loading tears it down the instant the bundle finishes
  // parsing, which is almost always well before the fetch (and the cold
  // start it's actually covering for) has resolved - the loader would never
  // be seen in practice.
  useEffect(() => {
    if (!loading) document.getElementById('boot')?.remove();
  }, [loading]);

  // index.html's title stands in until the league's own name arrives
  const leagueName = payload?.leagueName;
  useEffect(() => {
    if (leagueName) document.title = leagueName;
  }, [leagueName]);

  // The Skeleton's title is centred with its panel, so where it lands depends
  // on the viewport - index.html can only estimate it. Measure it instead, so
  // the ball's arc is centred exactly between the masthead and the title.
  useLayoutEffect(() => {
    const boot = document.getElementById('boot');
    if (!loading || !boot) return;

    const place = () => {
      const masthead = document.querySelector('.masthead')?.getBoundingClientRect().bottom;
      const title = document.getElementById('standings-h')?.getBoundingClientRect().top;
      if (masthead === undefined || title === undefined) return;
      boot.style.setProperty('--boot-mid', `${(masthead + title) / 2}px`);
    };

    place();
    window.addEventListener('resize', place);
    return () => window.removeEventListener('resize', place);
  }, [loading]);

  // the data answers whether the radar has anything to plot; no flag needed.
  // ?radar forces the panel open against hand-written data.
  const hasRadar =
    !!payload &&
    (Object.keys(payload.leagueSlotAverages).length > 0 ||
      new URLSearchParams(window.location.search).has('radar'));

  return (
    // reducedMotion="user" makes layout animations, not only explicit ones, respect the preference
    <MotionConfig reducedMotion="user">
      <LazyMotion features={domAnimation}>
        {mastHead}

        {!payload ? (
          loading ? (
            <Skeleton coldStart={coldStartHandler} />
          ) : (
            <LoadFailed error={error} onRetry={refetch} />
          )
        ) : (
          <>
            {error ? <StaleBanner asOfUtc={payload.asOfUtc} onRetry={refetch} /> : null}

            <main className="panels">
              <StandingsPanel teams={payload.teams} />
              <ChartPanel payload={payload} />
              {hasRadar ? <RadarPanel payload={payload} /> : null}
            </main>
          </>
        )}
      </LazyMotion>
    </MotionConfig>
  );
}
