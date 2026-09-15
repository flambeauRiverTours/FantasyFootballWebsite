import type { SeasonPayload, SeasonsPayload } from './types';

/**
 * Relative paths in both environments: the Vite dev server proxies /api to the
 * deployed Function App, and in production the Static Web App and the API share
 * an origin. Nothing here needs to know which it is talking to.
 */
const base = '/api';

/** A response, plus the tag to send back next time. */
export interface Fetched<T> {
  /** Null when the server answered 304 and the caller should keep what it has. */
  payload: T | null;
  etag: string | null;
}

export class ApiError extends Error {
  readonly status: number;

  constructor(status: number, message: string) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
  }
}

/**
 * Fetches one season.
 *
 * Pass the previous `etag` to get a 304 and no body when nothing has moved,
 * which is what makes the 30 second poll nearly free. No cache-busting
 * parameter: it would defeat the ETag, which is the point.
 */
export async function fetchSeason(season: number | null, etag: string | null,signal?: AbortSignal): Promise<Fetched<SeasonPayload>> {
  const query = season === null ? '' : `?season=${season}`;

  const response = await fetch(`${base}/season${query}`, {
    signal,
    headers: etag ? { 'If-None-Match': etag } : undefined,
  });

  if (response.status === 304) {
    return { payload: null, etag };
  }

  if (!response.ok) {
    throw new ApiError(response.status, `GET /api/season${query} returned ${response.status}`);
  }

  return {
    payload: (await response.json()) as SeasonPayload,
    etag: response.headers.get('ETag'),
  };
}

/** Fetches the seasons that can be asked for. Never cached by us; it is cheap and rarely changes. */
export async function fetchSeasons(signal?: AbortSignal): Promise<SeasonsPayload> {
  const response = await fetch(`${base}/seasons`, { signal });

  if (!response.ok) {
    throw new ApiError(response.status, `GET /api/seasons returned ${response.status}`);
  }

  return (await response.json()) as SeasonsPayload;
}
