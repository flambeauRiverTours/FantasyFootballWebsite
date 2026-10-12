import { act, renderHook, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { useSeason } from '../src/hooks/useSeason';
import type { SeasonPayload } from '../src/api/types';
import { payload } from './fixture';

// Sunday 13:00 in New York, inside a game window; and a Wednesday afternoon outside one
const InWindow = new Date('2026-09-13T17:00:00Z');
const OutOfWindow = new Date('2026-09-16T17:00:00Z');
const PollMs = 30_000;

function ok(body: SeasonPayload, etag = 'W/"1"'): Response {
  return new Response(JSON.stringify(body), { status: 200, headers: { ETag: etag } });
}

const notModified = () => new Response(null, { status: 304 });

function stubFetch(...responses: (() => Response)[]) {
  const queue = [...responses];
  return vi.spyOn(globalThis, 'fetch').mockImplementation(async () => {
    const next = queue.shift() ?? queue.at(-1) ?? notModified;
    return next();
  });
}

const sentEtag = (spy: ReturnType<typeof stubFetch>, call: number) =>
  new Headers((spy.mock.calls[call]?.[1] as RequestInit | undefined)?.headers).get('If-None-Match');

beforeEach(() => {
  vi.useFakeTimers({ shouldAdvanceTime: true, toFake: ['setInterval', 'clearInterval', 'Date'] });
});

afterEach(() => {
  vi.useRealTimers();
  vi.restoreAllMocks();
});

describe('useSeason', () => {
  it('is loading until the first response, then holds the payload', async () => {
    vi.setSystemTime(OutOfWindow);
    stubFetch(() => ok(payload));

    const { result } = renderHook(() => useSeason(null));
    expect(result.current.loading).toBe(true);

    await waitFor(() => expect(result.current.payload).not.toBeNull());
    expect(result.current.loading).toBe(false);
    expect(result.current.error).toBeNull();
  });

  it('polls every 30 seconds inside a game window, sending the last ETag', async () => {
    vi.setSystemTime(InWindow);
    const fetch = stubFetch(() => ok(payload, 'W/"first"'), notModified);

    const { result } = renderHook(() => useSeason(null));
    await waitFor(() => expect(result.current.payload).not.toBeNull());

    await act(() => vi.advanceTimersByTimeAsync(PollMs));
    await waitFor(() => expect(fetch).toHaveBeenCalledTimes(2));
    expect(sentEtag(fetch, 1)).toBe('W/"first"');
  });

  it('keeps its 30 second rhythm when new data arrives between polls', async () => {
    vi.setSystemTime(InWindow);
    const fetch = stubFetch(
      () => ok(payload, 'W/"1"'),
      () => ok({ ...payload, currentWeek: 4 }, 'W/"2"'),
      notModified,
    );

    const { result } = renderHook(() => useSeason(null));
    await waitFor(() => expect(result.current.payload).not.toBeNull());

    // fresh data 15 seconds in, from returning to the tab
    await act(() => vi.advanceTimersByTimeAsync(15_000));
    act(() => {
      document.dispatchEvent(new Event('visibilitychange'));
    });
    await waitFor(() => expect(result.current.payload?.currentWeek).toBe(4));

    // the poll still lands at 30 seconds, not 15 seconds after the new data
    await act(() => vi.advanceTimersByTimeAsync(15_000));
    await waitFor(() => expect(fetch).toHaveBeenCalledTimes(3));
  });

  it('keeps the payload it has when a poll answers 304', async () => {
    vi.setSystemTime(InWindow);
    stubFetch(() => ok(payload), notModified);

    const { result } = renderHook(() => useSeason(null));
    await waitFor(() => expect(result.current.payload).not.toBeNull());
    const first = result.current.payload;

    await act(() => vi.advanceTimersByTimeAsync(PollMs));
    expect(result.current.payload).toBe(first);
    expect(result.current.error).toBeNull();
  });

  it('does not poll outside a game window', async () => {
    vi.setSystemTime(OutOfWindow);
    const fetch = stubFetch(() => ok(payload));

    const { result } = renderHook(() => useSeason(null));
    await waitFor(() => expect(result.current.payload).not.toBeNull());

    await act(() => vi.advanceTimersByTimeAsync(PollMs * 3));
    expect(fetch).toHaveBeenCalledTimes(1);
  });

  it('never polls a finished season, even inside a game window', async () => {
    vi.setSystemTime(InWindow);
    const fetch = stubFetch(() => ok({ ...payload, isCurrent: false }));

    const { result } = renderHook(() => useSeason(2025));
    await waitFor(() => expect(result.current.payload).not.toBeNull());

    await act(() => vi.advanceTimersByTimeAsync(PollMs * 3));
    expect(fetch).toHaveBeenCalledTimes(1);
  });

  it('refreshes when the tab becomes visible again, whatever the day', async () => {
    vi.setSystemTime(OutOfWindow);
    const fetch = stubFetch(() => ok(payload), notModified);

    const { result } = renderHook(() => useSeason(null));
    await waitFor(() => expect(result.current.payload).not.toBeNull());

    act(() => {
      document.dispatchEvent(new Event('visibilitychange'));
    });
    await waitFor(() => expect(fetch).toHaveBeenCalledTimes(2));
  });

  it('keeps the last good payload and reports the error when a later request fails', async () => {
    vi.setSystemTime(OutOfWindow);
    stubFetch(
      () => ok(payload),
      () => new Response('oops', { status: 500 }),
    );

    const { result } = renderHook(() => useSeason(null));
    await waitFor(() => expect(result.current.payload).not.toBeNull());

    act(() => result.current.refetch());
    await waitFor(() => expect(result.current.error).not.toBeNull());
    expect(result.current.payload).not.toBeNull();
    expect(result.current.loading).toBe(false);
  });

  it('cancels the request in flight when it unmounts', async () => {
    vi.setSystemTime(OutOfWindow);
    let signal: AbortSignal | undefined;
    vi.spyOn(globalThis, 'fetch').mockImplementation((_, init) => {
      signal = init?.signal ?? undefined;
      return new Promise<Response>(() => {});
    });

    const { unmount } = renderHook(() => useSeason(null));
    await waitFor(() => expect(signal).toBeDefined());

    unmount();
    expect(signal?.aborted).toBe(true);
  });
});
