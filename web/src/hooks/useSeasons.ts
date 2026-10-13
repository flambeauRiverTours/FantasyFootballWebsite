import { useEffect, useState } from 'react';
import { fetchSeasons } from '../api/client';

/**
 * The seasons the API can serve, fetched once. Empty until they arrive, and
 * empty if the request fails: the picker is a convenience, so a failure hides
 * it rather than reporting an error.
 * @returns Years, newest first.
 */
export function useSeasons(): number[] {
  const [seasons, setSeasons] = useState<number[]>([]);

  useEffect(() => {
    const request = new AbortController();
    fetchSeasons(request.signal)
      .then((payload) => setSeasons(payload.available))
      .catch(() => setSeasons([]));
    return () => request.abort();
  }, []);

  return seasons;
}
