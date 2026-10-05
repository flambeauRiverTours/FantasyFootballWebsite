import type { SeasonPayload, SeasonsPayload } from './types';

/**
 * Where the API lives, from the browser's point of view.
 *
 * Empty in development, so paths stay relative and the Vite dev proxy forwards
 * them with no CORS involved. Set to the Function App's origin at build time for
 * production: the Static Web App has no linked backend (D5 declined the $9/month
 * Standard tier that provides one), so a relative /api path there is caught by
 * the SPA fallback and answered with index.html.
 */
const base = `${import.meta.env.VITE_API_ORIGIN ?? ''}/api`;

/** A response, plus the tag to send back next time. */
export interface Fetched<T> {
  /** Null when the server answered 304 and the caller should keep what it has. */
  payload: T | null;
  etag: string | null;
}

export class ApiError extends Error {
  readonly status: number;
  /** Seasons the API says it does have. Only a 404 carries them. */
  readonly available: number[] | null;

  constructor(status: number, message: string, available: number[] | null = null) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
    this.available = available;
  }
}

/**
 * Fetches one season.
 *
 * Pass the previous `etag` to get a 304 and no body when nothing has moved,
 * which is what makes the 30 second poll nearly free. No cache-busting
 * parameter: it would defeat the ETag, which is the point.
 */
export async function fetchSeason(season: number | null, etag: string | null, signal?: AbortSignal): Promise<Fetched<SeasonPayload>> {
  const query = season === null ? '' : `?season=${season}`;

  const response = await fetch(`${base}/season${query}`, {
    signal,
    headers: etag ? { 'If-None-Match': etag } : undefined,
  });

  if (response.status === 304) {
    return { payload: null, etag };
  }

  if (!response.ok) {
    //a platform 500 is html
    const body = (await response.json().catch(() => null)) as { available?: unknown } | null;
    throw new ApiError(
      response.status,
      `GET /api/season${query} returned ${response.status}`,
      Array.isArray(body?.available) ? (body.available as number[]) : null,
    );
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

export type Metric = 'actual' | 'ideal' | 'sigma';
