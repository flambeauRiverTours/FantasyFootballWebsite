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
export async function fetchSeason(
  season: number | null,
  etag: string | null,
  signal?: AbortSignal,
): Promise<Fetched<SeasonPayload>> {
  const query = season === null ? '' : `?season=${season}`;

  const response = await fetch(`${base}/season${query}`, {
    signal,
    headers: etag ? { 'If-None-Match': etag } : undefined,
  });

  if (response.status === 304) {
    return { payload: null, etag };
  }

  if (!response.ok) {
    // a 500 from the platform, rather than from the API, is an HTML page, so the body may not parse
    const body = (await response.json().catch(() => null)) as { available?: unknown } | null;
    throw new ApiError(
      response.status,
      `GET /api/season${query} returned ${response.status}`,
      seasonsIn(body?.available),
    );
  }

  return {
    // trusted rather than checked: this is our own API, and its §09 shape is pinned by the server's tests
    payload: (await response.json()) as SeasonPayload,
    etag: response.headers.get('ETag'),
  };
}

/**
 * Fetches the seasons that can be served, for the season picker.
 * @param signal Aborts the request when the page no longer needs it.
 * @returns The current season and every season available, newest first.
 */
export async function fetchSeasons(signal?: AbortSignal): Promise<SeasonsPayload> {
  const response = await fetch(`${base}/seasons`, { signal });
  if (!response.ok) {
    throw new ApiError(response.status, `GET /api/seasons returned ${response.status}`);
  }

  // trusted, like the season payload: our own API, its shape pinned by the server's tests
  return (await response.json()) as SeasonsPayload;
}

/**
 * Reads the seasons a 404 says do exist. Checked, unlike the payload, because
 * an error body can come from the platform rather than the API.
 * @param value The body's `available` field, whatever it holds.
 * @returns The years, or null when the field is not a list of numbers.
 */
function seasonsIn(value: unknown): number[] | null {
  return Array.isArray(value) && value.every((year) => typeof year === 'number') ? value : null;
}
