import { afterEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import App from '../src/App';
import Masthead from '../src/components/Masthead';
import type { SeasonPayload } from '../src/api/types';
import { payload } from './fixture';
import { seasonRequests, serveApi } from './api';

const past: SeasonPayload = {
  ...payload,
  season: 2025,
  isCurrent: false,
  currentWeek: 17,
  finalThroughWeek: 17,
};

afterEach(() => {
  vi.restoreAllMocks();
  window.history.replaceState(null, '', '/');
});

describe('Masthead', () => {
  it('shows no picker while there is only one season', () => {
    render(
      <Masthead payload={payload} updatedAgo="just now" seasons={[2026]} onSeason={() => {}} />,
    );

    expect(screen.queryByRole('combobox', { name: 'Season' })).toBeNull();
    expect(document.querySelector('.masthead p')).toHaveTextContent(
      '2026 · Week 3 · Updated just now',
    );
  });

  it('offers every season, newest first, once there are two', () => {
    render(
      <Masthead
        payload={payload}
        updatedAgo="just now"
        seasons={[2026, 2025]}
        onSeason={() => {}}
      />,
    );

    const picker = screen.getByRole('combobox', { name: 'Season' });
    expect(picker).toHaveValue('2026');
    expect([...picker.querySelectorAll('option')].map((o) => o.textContent)).toEqual([
      '2026',
      '2025',
    ]);
  });

  it('settles a completed season on its final week instead of an update time', () => {
    render(
      <Masthead payload={past} updatedAgo="just now" seasons={[2026, 2025]} onSeason={() => {}} />,
    );

    expect(document.querySelector('.masthead p')).toHaveTextContent('· Final · Week 17');
    expect(screen.queryByText(/Updated/)).toBeNull();
  });
});

describe('Choosing a season', () => {
  it('asks for that season and records it in the URL', async () => {
    const fetch = serveApi(payload, [2026, 2025]);
    render(<App />);

    fireEvent.change(await screen.findByRole('combobox', { name: 'Season' }), {
      target: { value: '2025' },
    });

    await waitFor(() =>
      expect(seasonRequests(fetch).at(-1)).toMatch(/\/api\/season\?season=2025$/),
    );
    expect(window.location.search).toBe('?season=2025');
  });

  it('keeps the URL clean for the current season', async () => {
    window.history.replaceState(null, '', '/?season=2025');
    serveApi(past, [2026, 2025]);
    render(<App />);

    fireEvent.change(await screen.findByRole('combobox', { name: 'Season' }), {
      target: { value: '2026' },
    });

    await waitFor(() => expect(window.location.search).toBe(''));
  });
});
