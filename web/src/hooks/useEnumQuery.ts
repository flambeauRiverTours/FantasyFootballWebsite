import { useQueryState } from './useQueryState';

type Options<T extends string> = readonly (readonly [T, string])[];

/** A query parameter narrowed to one of `options`. The first is the default. */
export function useEnumQuery<T extends string>(
  key: string,
  options: Options<T>,
): readonly [T, (next: T) => void] {
  const fallback = options[0][0];
  const [raw, set] = useQueryState(key, fallback);
  const value = options.some(([v]) => v === raw) ? (raw as T) : fallback;

  return [value, set] as const;
}
