import { useEffect, useState } from 'react';

const Units: [Intl.RelativeTimeFormatUnit, number][] = [
  ['second', 60],
  ['minute', 60],
  ['hour', 24],
  ['day', 7],
];

/*
 * Gets the relative time, but React-y
 * @param iso ISO format
 * @returns Relative time string
 */
export function useRelativeTime(iso: string | undefined): string {
  const [now, setNow] = useState(() => Date.now());

  useEffect(() => {
    const id = window.setInterval(() => setNow(Date.now()), 15_000);
    return () => window.clearInterval(id);
  }, []);

  const at = iso === undefined ? NaN : new Date(iso).getTime();
  if (!Number.isFinite(at)) return '';

  const format = new Intl.RelativeTimeFormat(undefined, { numeric: 'auto' });
  let delta = (at - now) / 1000;

  for (const [unit, step] of Units) {
    if (Math.abs(delta) < step) return format.format(Math.round(delta), unit);
    delta /= step;
  }
  return format.format(Math.round(delta), 'week');
}