import { useEffect, useState } from 'react';

/** True once `active` has been true for `ms` without interruption. */
export function useElapsedSince(active: boolean, ms: number): boolean {
  const [passed, setPassed] = useState(false);

  useEffect(() => {
    if (!active) return;

    const id = window.setTimeout(() => setPassed(true), ms);
    return () => window.clearTimeout(id);
  }, [active, ms]);

  //masked rather than reset so nothing is set during an effect
  return active && passed;
}
