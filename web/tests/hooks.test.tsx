import { act, renderHook } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { useElapsedSince } from '../src/hooks/useElapsedSince';
import { useEnumQuery } from '../src/hooks/useEnumQuery';
import { useQueryState } from '../src/hooks/useQueryState';
import { useRelativeTime } from '../src/hooks/useRelativeTime';
import { useTeamFilter } from '../src/hooks/useTeamFilter';
import { teams } from './fixture';

function setSearch(search: string) {
  window.history.replaceState(null, '', search || window.location.pathname);
}

afterEach(() => setSearch(''));

describe('useQueryState', () => {
  it('reads the initial value out of the url', () => {
    setSearch('?metric=ideal');
    const { result } = renderHook(() => useQueryState('metric', 'actual'));
    expect(result.current[0]).toBe('ideal');
  });

  it('falls back when the parameter is absent', () => {
    const { result } = renderHook(() => useQueryState('metric', 'actual'));
    expect(result.current[0]).toBe('actual');
  });

  it('mirrors a change into the url', () => {
    const { result } = renderHook(() => useQueryState('metric', 'actual'));
    act(() => result.current[1]('ideal'));
    expect(window.location.search).toBe('?metric=ideal');
  });

  it('drops the parameter when it returns to the default', () => {
    setSearch('?metric=ideal');
    const { result } = renderHook(() => useQueryState('metric', 'actual'));
    act(() => result.current[1]('actual'));
    expect(window.location.search).toBe('');
  });

  it('leaves other parameters alone', () => {
    setSearch('?teams=1,2');
    const { result } = renderHook(() => useQueryState('metric', 'actual'));
    act(() => result.current[1]('ideal'));
    expect(new URLSearchParams(window.location.search).get('teams')).toBe('1,2');
  });

  it('composes two updates in one batch', () => {
    const { result } = renderHook(() => useQueryState('n', '0'));
    act(() => {
      result.current[1]((prev) => String(Number(prev) + 1));
      result.current[1]((prev) => String(Number(prev) + 1));
    });
    expect(result.current[0]).toBe('2');
  });
});

describe('useEnumQuery', () => {
  const Options = [
    ['actual', 'Points'],
    ['ideal', 'Ideal'],
  ] as const;

  it('defaults to the first option', () => {
    const { result } = renderHook(() => useEnumQuery('metric', Options));
    expect(result.current[0]).toBe('actual');
  });

  it('accepts a value the options contain', () => {
    setSearch('?metric=ideal');
    const { result } = renderHook(() => useEnumQuery('metric', Options));
    expect(result.current[0]).toBe('ideal');
  });

  it('falls back on a value the options do not contain', () => {
    setSearch('?metric=nonsense');
    const { result } = renderHook(() => useEnumQuery('metric', Options));
    expect(result.current[0]).toBe('actual');
  });
});

describe('useTeamFilter', () => {
  const ids = teams.map((t) => t.rosterId);

  it('shows every team when the parameter is absent', () => {
    const { result } = renderHook(() => useTeamFilter(teams));
    expect([...result.current.shown].sort()).toEqual(ids);
  });

  it('drops ids the payload does not have', () => {
    setSearch(`?teams=${ids[0]},999`);
    const { result } = renderHook(() => useTeamFilter(teams));
    expect([...result.current.shown]).toEqual([ids[0]]);
  });

  it('writes a sorted list so click order does not change the link', () => {
    const { result } = renderHook(() => useTeamFilter(teams));
    act(() => result.current.toggle(ids[0]));
    const written = new URLSearchParams(window.location.search).get('teams')!;
    const written2 = written.split(',').map(Number);
    expect(written2).toEqual([...written2].sort((a, b) => a - b));
  });

  it('clears the parameter when everything is selected again', () => {
    const { result } = renderHook(() => useTeamFilter(teams));
    act(() => result.current.toggle(ids[0]));
    expect(window.location.search).not.toBe('');
    act(() => result.current.toggle(ids[0]));
    expect(window.location.search).toBe('');
  });

  it('refuses to deselect the last team', () => {
    setSearch(`?teams=${ids[0]}`);
    const { result } = renderHook(() => useTeamFilter(teams));
    act(() => result.current.toggle(ids[0]));
    expect([...result.current.shown]).toEqual([ids[0]]);
  });
});

describe('useRelativeTime', () => {
  it('returns empty for an absent timestamp', () => {
    const { result } = renderHook(() => useRelativeTime(undefined));
    expect(result.current).toBe('');
  });

  it('returns empty for an unparseable timestamp', () => {
    const { result } = renderHook(() => useRelativeTime('not a date'));
    expect(result.current).toBe('');
  });

  it('describes a moment in the past', () => {
    const tenMinutes = new Date(Date.now() - 600_000).toISOString();
    const { result } = renderHook(() => useRelativeTime(tenMinutes));
    expect(result.current).toMatch(/10 minutes ago/);
  });

  it('crosses into hours', () => {
    const threeHours = new Date(Date.now() - 3 * 3_600_000).toISOString();
    const { result } = renderHook(() => useRelativeTime(threeHours));
    expect(result.current).toMatch(/3 hours ago/);
  });
});

describe('useElapsedSince', () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  it('is false before the wait is up', () => {
    const { result } = renderHook(() => useElapsedSince(true, 3000));
    expect(result.current).toBe(false);
    act(() => void vi.advanceTimersByTime(2999));
    expect(result.current).toBe(false);
  });

  it('becomes true once the wait passes', () => {
    const { result } = renderHook(() => useElapsedSince(true, 3000));
    act(() => void vi.advanceTimersByTime(3000));
    expect(result.current).toBe(true);
  });

  it('is false while inactive however long has passed', () => {
    const { result, rerender } = renderHook(({ on }) => useElapsedSince(on, 3000), {
      initialProps: { on: true },
    });
    act(() => void vi.advanceTimersByTime(3000));
    expect(result.current).toBe(true);

    rerender({ on: false });
    expect(result.current).toBe(false);
  });

  it('clears its timer on unmount', () => {
    const clear = vi.spyOn(window, 'clearTimeout');
    const { unmount } = renderHook(() => useElapsedSince(true, 3000));
    unmount();
    expect(clear).toHaveBeenCalled();
  });
});
