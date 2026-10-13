import { useEffect, useState } from 'react';

const Units: [Intl.RelativeTimeFormatUnit, number][] = [
  ['second', 60],
  ['minute', 60],
  ['hour', 24],
  ['day', 7],
];

/**
 * How long ago a moment was, in words, refreshed every 15 seconds.
 * @param iso An ISO 8601 timestamp, or undefined before there is one.
 * @returns For example "2 minutes ago", or an empty string when there is no time.
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
