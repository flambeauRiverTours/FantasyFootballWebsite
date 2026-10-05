import { useMemo } from 'react';
import type { TeamPayload } from '../api/types';
import { useQueryState } from './useQueryState';

/** Ids from the parameter, dropping any the payload does not have. Empty means all. */
function toShown(param: string, valid: number[]): Set<number> {
  const ids = param.split(',').map(Number).filter((id) => valid.includes(id));
  return new Set(ids.length > 0 ? ids : valid);
}

/*
 * Filters teams down based on user input via toggle
 * @param teams Full list of teams
 * @returns Shown list of teams, toggle handler
 */
export function useTeamFilter(teams: TeamPayload[]) {
  const [param, setParam] = useQueryState('teams', '');

  const validIds = useMemo(() => teams.map((t) => t.rosterId), [teams]);
  const shown = useMemo(() => toShown(param, validIds), [param, validIds]);

  //never zero teams
  const toggle = (id: number) =>
    setParam((prev) => {
      const current = toShown(prev, validIds);
      if (current.has(id) && current.size === 1) return prev;

      const next = new Set(current);
      if (!next.delete(id)) next.add(id);
      return next.size === validIds.length ? '' : [...next].sort((a, b) => a - b).join(',');
    });

  return { shown, toggle };
}
