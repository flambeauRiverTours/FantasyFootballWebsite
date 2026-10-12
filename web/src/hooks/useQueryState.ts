import { useEffect, useState } from 'react';

/**
 * A string kept in the URL's query, so the view it describes is a shareable link.
 * @param key The query parameter.
 * @param fallback The value when the parameter is absent. Never written to the URL.
 * @returns The current value and a setter that updates the URL in place.
 */
export function useQueryState(key: string, fallback: string) {
  const [value, setValue] = useState(
    () => new URLSearchParams(window.location.search).get(key) ?? fallback,
  );

  useEffect(() => {
    const params = new URLSearchParams(window.location.search);
    if (value === fallback) params.delete(key);
    else params.set(key, value);
    const query = params.toString();
    window.history.replaceState(null, '', query ? `?${query}` : window.location.pathname);
  }, [key, fallback, value]);

  return [value, setValue] as const;
}
