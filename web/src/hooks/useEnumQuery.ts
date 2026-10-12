import { useQueryState } from './useQueryState';

/** One choice: its value in the URL, and its label on screen. */
export type Option<T extends string> = readonly [T, string];

/** At least one choice, so there is always a default. */
export type Options<T extends string> = readonly [Option<T>, ...Option<T>[]];

/** A query parameter narrowed to one of `options`. The first is the default and is left out of the URL. */
export function useEnumQuery<T extends string>(
  key: string,
  options: Options<T>,
): readonly [T, (next: T) => void] {
  const fallback = options[0][0];
  const [raw, set] = useQueryState(key, fallback);
  const value = options.some(([v]) => v === raw) ? (raw as T) : fallback;

  return [value, set] as const;
}
