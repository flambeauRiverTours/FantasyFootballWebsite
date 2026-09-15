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

    const tick = () => {
      if (document.visibilityState === 'visible') void load();
    };

    const id = window.setInterval(tick, PollIntervalMs);

    // returning to the tab refreshes immediately
    document.addEventListener('visibilitychange', tick);

    return () => {
      window.clearInterval(id);
      document.removeEventListener('visibilitychange', tick);
    };
  }, [load, payload]);

  return { payload, error, loading, refetch: () => void load() };
}
