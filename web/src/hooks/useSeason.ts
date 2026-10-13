import { useCallback, useEffect, useRef, useState } from 'react';
import { fetchSeason } from '../api/client';
import { isGameWindow } from '../api/gameWindow';
import type { SeasonPayload } from '../api/types';

const PollIntervalMs = 30_000;

export interface SeasonState {
  payload: SeasonPayload | null;
  error: Error | null;
  /** True only until the first response; a poll never returns the page to a spinner. */
  loading: boolean;
  refetch: () => void;
}

/**
 * Fetches a season and keeps it current while it is still being played.
 *
 * The ETag does the work: a poll that finds nothing new costs a 304 with no
 * body. A finished season is never polled at all, because it cannot change.
 *
 * @param season The season to show, or null for the current one. Fixed for the
 * page's life: it comes from the URL at load, and nothing changes it after.
 */
export function useSeason(season: number | null): SeasonState {
  const [payload, setPayload] = useState<SeasonPayload | null>(null);
  const [error, setError] = useState<Error | null>(null);

  const etag = useRef<string | null>(null);
  const controller = useRef<AbortController | null>(null);

  const loading = payload === null && error === null;

  // unknown until the first response, so poll until told otherwise
  const isCurrent = payload?.isCurrent ?? true;

  const load = useCallback(async () => {
    controller.current?.abort();
    const current = new AbortController();
    controller.current = current;

    try {
      const result = await fetchSeason(season, etag.current, current.signal);

      // a 304 means keep what we have
      if (result.payload !== null) setPayload(result.payload);

      etag.current = result.etag;
      setError(null);
    } catch (caught) {
      if (current.signal.aborted) return;
      setError(caught instanceof Error ? caught : new Error(String(caught)));
    }
  }, [season]);

  useEffect(() => {
    void load();
    return () => controller.current?.abort();
  }, [load]);

  useEffect(() => {
    if (!isCurrent) return;

    const poll = () => {
      if (document.visibilityState === 'visible' && isGameWindow(new Date())) void load();
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
  }, [load, isCurrent]);

  return { payload, error, loading, refetch: () => void load() };
}
