import { useEffect, useState } from 'react';

/*
 * React effect to pull values from the URL 
 * @param key Key in the query to identify
 * @param fallback Fallback value to use
 * @returns Value for the key in query if found, otherwise fallback
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
