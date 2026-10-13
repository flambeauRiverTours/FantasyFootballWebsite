import { useEffect, useState } from 'react';

/** True once `active` has been true for `ms` without interruption. */
export function useElapsedSince(active: boolean, ms: number): boolean {
  const [passed, setPassed] = useState(false);

  useEffect(() => {
    if (!active) return;

    const id = window.setTimeout(() => setPassed(true), ms);
    return () => window.clearTimeout(id);
  }, [active, ms]);

  // `passed` stays true once set; ANDing with `active` hides it again when
  // loading restarts, without a state update inside the effect
  return active && passed;
}
