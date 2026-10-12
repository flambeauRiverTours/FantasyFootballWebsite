import { useCallback, useEffect, useRef, useState } from 'react';
import { fetchSeason } from '../api/client';
import type { SeasonPayload } from '../api/types';

const PollIntervalMs = 30_000;

export interface SeasonState {
  payload: SeasonPayload | null;
  error: Error | null;
  /** True only until the first response; a poll never returns the page to a spinner. */
  loading: boolean;
  refetch: () => void;
}

/** What we hold, and which season it belongs to. */
interface Loaded {
  season: number | null;
  payload: SeasonPayload;
}

/**
 * Fetches a season and keeps it current while it is still being played.
 *
 * The ETag does the work: a poll that finds nothing new costs a 304 with no
 * body. A finished season is never polled at all, because it cannot change.
 */
export function useSeason(season: number | null): SeasonState {
  const [loaded, setLoaded] = useState<Loaded | null>(null);
  const [error, setError] = useState<Error | null>(null);

  const etag = useRef<string | null>(null);
  const controller = useRef<AbortController | null>(null);

  // derived during render rather than set from an effect
  const stale = loaded !== null && loaded.season !== season;
  const payload = stale ? null : (loaded?.payload ?? null);
  const loading = payload === null && error === null;

  const load = useCallback(async () => {
    controller.current?.abort();
    const current = new AbortController();
    controller.current = current;

    try {
      const result = await fetchSeason(season, etag.current, current.signal);

      // a 304 means keep what we have
      if (result.payload !== null) {
        setLoaded({ season, payload: result.payload });
      }

      etag.current = result.etag;
      setError(null);
    } catch (caught) {
      if (current.signal.aborted) return;
      setError(caught instanceof Error ? caught : new Error(String(caught)));
    }
  }, [season]);

  useEffect(() => {
    // a different season is a different resource
    etag.current = null;
    void load();

    return () => controller.current?.abort();
  }, [load]);

  useEffect(() => {
    if (payload !== null && !payload.isCurrent) return;

    const poll = () => {
      if (document.visibilityState === 'visible' && isGameWindow(new Date())) void load(); //only reload if in game window
    };

    // returning to the tab refreshes immediately, whatever day it is
    const onVisible = () => {
      if (document.visibilityState === 'visible') void load();
    };

    const id = window.setInterval(poll, PollIntervalMs);
    document.addEventListener('visibilitychange', onVisible);

    return () => {
      window.clearInterval(id);
      document.removeEventListener('visibilitychange', onVisible);
    };
  }, [load, payload]);

  return { payload, error, loading, refetch: () => void load() };
}

/**
 * Whether an NFL game may be in progress, in Eastern time (D6).
 * @param at The moment to check.
 * @returns True inside a game window, so a poll is worth making.
 */
function isGameWindow(at: Date): boolean {
  const parts = new Intl.DateTimeFormat('en-US', {
    timeZone: 'America/New_York',
    weekday: 'short',
    month: 'short',
    hour: 'numeric',
    hour12: false,
  }).formatToParts(at);

  const part = (type: Intl.DateTimeFormatPartTypes) => parts.find((p) => p.type === type)?.value;
  const day = part('weekday');
  const month = part('month');
  const hour = Number(part('hour'));
  if (month === 'Nov' && day === 'Wed') return hour >= 17; //wednesday night games before turkey day
  if (month === 'Nov' && day === 'Thu') return hour >= 12; //thursday turkey day games
  if (day === 'Thu') return hour >= 20;
  if (day === 'Fri') return hour < 1;
  if (day === 'Sun') return hour >= 13;
  if (day === 'Mon') return hour >= 20 || hour < 1;
  if (day === 'Tue') return hour < 9; //FinalizeWeek runs 12:00 UTC, 07:00/08:00 in New York
  return false;
}
