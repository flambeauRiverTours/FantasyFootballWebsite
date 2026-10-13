import { afterEach, describe, expect, it, vi } from 'vitest';
import { render, waitFor } from '@testing-library/react';
import App from '../src/App';
import { payload } from './fixture';
import { seasonRequests, serveApi } from './api';

const stubFetch = () => serveApi(payload);

afterEach(() => {
  vi.restoreAllMocks();
  window.history.replaceState(null, '', '/');
});

describe('App', () => {
  it('asks for the season named in the URL', async () => {
    window.history.replaceState(null, '', '/?season=2025');
    const fetch = stubFetch();

    render(<App />);

    await waitFor(() => expect(seasonRequests(fetch)).not.toHaveLength(0));
    expect(seasonRequests(fetch)[0]).toMatch(/\/api\/season\?season=2025$/);
  });

  it('asks for the current season when the URL names none, or names something that is not a year', async () => {
    window.history.replaceState(null, '', '/?season=latest');
    const fetch = stubFetch();

    render(<App />);

    await waitFor(() => expect(seasonRequests(fetch)).not.toHaveLength(0));
    expect(seasonRequests(fetch)[0]).toMatch(/\/api\/season$/);
  });

  it('titles the tab with the league once it loads', async () => {
    stubFetch();

    render(<App />);

    await waitFor(() => expect(document.title).toBe('Test League'));
  });
});
