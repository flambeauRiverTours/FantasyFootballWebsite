import { afterEach, describe, expect, it, vi } from 'vitest';
import { ApiError, fetchSeason } from '../src/api/client';
import { payload } from './fixture';

function respond(body: unknown, init: ResponseInit & { etag?: string } = {}) {
  const headers = new Headers(init.headers);
  if (init.etag) headers.set('ETag', init.etag);

  return {
    ok: (init.status ?? 200) < 400,
    status: init.status ?? 200,
    headers,
    json: async () => {
      if (typeof body === 'string') throw new SyntaxError('not json');
      return body;
    },
  } as Response;
}

const fetchMock = () => vi.spyOn(globalThis, 'fetch');

afterEach(() => vi.restoreAllMocks());

describe('fetchSeason', () => {
  it('asks for the current season with no query', async () => {
    const spy = fetchMock().mockResolvedValue(respond(payload, { etag: 'W/"a"' }));
    await fetchSeason(null, null);
    expect(String(spy.mock.calls[0][0])).toMatch(/\/api\/season$/);
  });

  it('passes a season through as a parameter', async () => {
    const spy = fetchMock().mockResolvedValue(respond(payload));
    await fetchSeason(2025, null);
    expect(String(spy.mock.calls[0][0])).toMatch(/\?season=2025$/);
  });

  it('sends the previous etag and returns the new one', async () => {
    const spy = fetchMock().mockResolvedValue(respond(payload, { etag: 'W/"new"' }));
    const result = await fetchSeason(null, 'W/"old"');

    const init = spy.mock.calls[0][1] as RequestInit;
    expect((init.headers as Record<string, string>)['If-None-Match']).toBe('W/"old"');
    expect(result.etag).toBe('W/"new"');
  });

  it('keeps what the caller has on a 304', async () => {
    fetchMock().mockResolvedValue(respond(null, { status: 304 }));
    const result = await fetchSeason(null, 'W/"old"');

    expect(result.payload).toBeNull();
    expect(result.etag).toBe('W/"old"');
  });

  it('carries the available seasons off a 404', async () => {
    fetchMock().mockResolvedValue(respond({ available: [2026] }, { status: 404 }));

    await expect(fetchSeason(1999, null)).rejects.toMatchObject({
      status: 404,
      available: [2026],
    });
  });

  it('survives a 500 whose body is html rather than json', async () => {
    fetchMock().mockResolvedValue(respond('<!doctype html>', { status: 500 }));

    const error = await fetchSeason(null, null).catch((e: unknown) => e);
    expect(error).toBeInstanceOf(ApiError);
    expect((error as ApiError).status).toBe(500);
    expect((error as ApiError).available).toBeNull();
  });

  it('ignores a list of seasons that holds anything but numbers', async () => {
    fetchMock().mockResolvedValue(respond({ available: [2026, '<script>'] }, { status: 404 }));
    const error = (await fetchSeason(1999, null).catch((e: unknown) => e)) as ApiError;
    expect(error.available).toBeNull();
  });

  it('ignores an available field that is not a list', async () => {
    fetchMock().mockResolvedValue(respond({ available: 'nope' }, { status: 404 }));
    const error = (await fetchSeason(null, null).catch((e: unknown) => e)) as ApiError;
    expect(error.available).toBeNull();
  });
});
