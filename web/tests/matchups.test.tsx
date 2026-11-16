import { afterEach, describe, expect, it, vi } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import App from '../src/App';
import MatchupsPanel from '../src/components/MatchupsPanel';
import type { MatchupsPayload } from '../src/api/types';
import { headToHead, payload } from './fixture';
import { serveApi } from './api';

const matchups = headToHead.matchups!;

const settled: MatchupsPayload = { ...matchups, isFinal: true };

const panel = (shown: MatchupsPayload = matchups) => (
  <MatchupsPanel payload={headToHead} matchups={shown} />
);

const cards = () => within(screen.getByRole('list')).getAllByRole('listitem');

afterEach(() => {
  vi.restoreAllMocks();
});

describe('MatchupsPanel', () => {
  it('shows one card per game, each side as a line of text', () => {
    render(panel());
    expect(cards()).toHaveLength(2);
    expect(cards()[0]).toHaveTextContent(/Alpha\s*96\.4\s*Leading/);
    expect(cards()[0]).toHaveTextContent(/Bravo\s*41\.2/);
  });

  it('calls a live week matchups and a settled one results', () => {
    const { rerender } = render(panel());
    expect(screen.getByRole('heading', { level: 2 })).toHaveTextContent('Week 3 matchups');

    rerender(panel(settled));
    expect(screen.getByRole('heading', { level: 2 })).toHaveTextContent('Week 3 results');
  });

  it('says won only once the week is final, and marks the winner beyond colour', () => {
    const { container, rerender } = render(panel());
    expect(screen.queryByText('Won')).toBeNull();
    expect(container.querySelector('.is-winner')).toBeNull();

    rerender(panel(settled));
    expect(screen.getAllByText('Won')).toHaveLength(1);
    expect(container.querySelector('.is-winner')).toHaveTextContent('Alpha');
    expect(within(cards()[1]).getAllByText('Tied')).toHaveLength(2);
  });

  it('gives a roster with no table row no colour, but keeps its place', () => {
    render(panel());
    const swatches = cards()[1].querySelectorAll<HTMLElement>('.swatch');
    expect(swatches).toHaveLength(2);
    expect(swatches[0].style.getPropertyValue('--swatch')).toBe('var(--team-3)');
    expect(swatches[1].style.getPropertyValue('--swatch')).toBe('');
    swatches.forEach((s) => expect(s).toHaveAttribute('aria-hidden', 'true'));
  });

  it('announces nothing as the scores change', () => {
    const { container } = render(panel());
    expect(container.querySelector('[aria-live]')).toBeNull();
  });

  it('shows new games when a poll brings them', () => {
    const { rerender } = render(panel());
    const next: MatchupsPayload = { ...matchups, week: 4, games: matchups.games.slice(0, 1) };
    rerender(panel(next));
    expect(cards()).toHaveLength(1);
    expect(screen.getByRole('heading', { level: 2 })).toHaveTextContent('Week 4 matchups');
  });
});

describe('The page', () => {
  it('shows the matchups right under the standings in head-to-head', async () => {
    serveApi(headToHead);
    render(<App />);
    const panel = await screen.findByRole('region', { name: 'Week 3 matchups' });
    expect(panel.previousElementSibling).toHaveAttribute('id', 'standings');
  });

  it('shows no matchups panel when the season has none', async () => {
    serveApi(payload);
    render(<App />);
    await screen.findByRole('region', { name: 'Standings' });
    expect(screen.queryByRole('region', { name: /matchups|results/ })).toBeNull();
  });
});
