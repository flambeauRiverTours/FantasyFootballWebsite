import { vi } from 'vitest';
import type { SeasonPayload } from '../src/api/types';

/**
 * Answers the page's two requests, each with a fresh Response (a body reads once):
 * `/api/seasons` with the given years, every `/api/season` with `season`.
 * @param season The season payload.
 * @param available The years `/api/seasons` lists, newest first.
 * @returns The spy, to inspect the requests made.
 */
export function serveApi(season: SeasonPayload, available: number[] = [season.season]) {
  return vi.spyOn(globalThis, 'fetch').mockImplementation(async (input) => {
    const url = input instanceof Request ? input.url : String(input);
    const body = url.includes('/api/seasons') ? { current: available[0], available } : season;
    return new Response(JSON.stringify(body), { status: 200, headers: { ETag: 'W/"1"' } });
  });
}

/** The URLs of the `/api/season` requests a spy saw, in order. */
export function seasonRequests(spy: ReturnType<typeof serveApi>): string[] {
  return spy.mock.calls
    .map(([input]) => String(input))
    .filter((url) => /\/api\/season(\?|$)/.test(url));
}
