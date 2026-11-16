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

/** What a side's line shows on screen, without the words only a screen reader hears. */
function shown(line: HTMLElement): string {
  const copy = line.cloneNode(true) as HTMLElement;
  copy.querySelectorAll('.visually-hidden').forEach((e) => e.remove());
  return copy.textContent!.replace(/\s+/g, ' ').trim();
}

afterEach(() => {
  vi.restoreAllMocks();
});

describe('MatchupsPanel', () => {
  it('shows one card per game, each side its name and points', () => {
    render(panel());
    expect(cards()).toHaveLength(2);
    const [first, second] = cards()[0].querySelectorAll<HTMLElement>('.matchup-side');
    expect(shown(first)).toBe('Alpha 96.4');
    expect(shown(second)).toBe('Bravo 41.2');
  });

  it('calls a live week live and a settled one results', () => {
    const { rerender } = render(panel());
    expect(screen.getByRole('heading', { level: 2 })).toHaveTextContent('Week 3 · live');

    rerender(panel(settled));
    expect(screen.getByRole('heading', { level: 2 })).toHaveTextContent('Week 3 results');
  });

  it('bolds the higher score, live or final, and neither of a tie', () => {
    const { container, rerender } = render(panel());
    const ahead = () => [...container.querySelectorAll('.is-ahead')].map((e) => e.textContent);
    expect(ahead()).toEqual(['96.4']);

    rerender(panel(settled));
    expect(ahead()).toEqual(['96.4']);
  });

  it('tells a screen reader who leads, and who won once final', () => {
    const { rerender } = render(panel());
    const hidden = () =>
      cards().map((c) => [...c.querySelectorAll('.visually-hidden')].map((e) => e.textContent));
    expect(hidden()).toEqual([
      [', leading', 'versus '],
      [', level', 'versus ', ', level'],
    ]);

    rerender(panel(settled));
    expect(hidden()).toEqual([
      [', won', 'versus '],
      [', tied', 'versus ', ', tied'],
    ]);
  });

  it('calls a week upcoming before kick-off, with no live note and no median line', () => {
    const upcoming: MatchupsPayload = {
      ...matchups,
      games: matchups.games.map((g) => ({
        ...g,
        teams: [
          { ...g.teams[0], actual: 0 },
          { ...g.teams[1], actual: 0 },
        ],
      })),
    };
    render(panel(upcoming));
    expect(screen.getByRole('heading', { level: 2 })).toHaveTextContent('Week 3 · upcoming');
    expect(screen.queryByText(/live scores/i)).toBeNull();
    expect(screen.queryByText(/^Median/)).toBeNull();
  });

  it('bolds the whole winning line only once the week is final', () => {
    const { container, rerender } = render(panel());
    expect(container.querySelector('.is-winner')).toBeNull();

    rerender(panel(settled));
    const winners = container.querySelectorAll('.is-winner');
    expect(winners).toHaveLength(1);
    expect(winners[0]).toHaveTextContent('Alpha');
  });

  it('says the scores are live and not yet in the table, until the week is final', () => {
    const { rerender } = render(panel());
    expect(screen.getByText(/live scores/i)).toHaveTextContent(/once the week is final/);

    rerender(panel(settled));
    expect(screen.queryByText(/live scores/i)).toBeNull();
  });

  it('shows the median game line and who is above it', () => {
    const { rerender } = render(panel());
    expect(screen.getByText(/^Median/)).toHaveTextContent(
      'Median 50.00 so far: Alpha above it; Charlie, Placeholder (not ranked) on it.',
    );

    rerender(panel(settled));
    expect(screen.getByText(/^Median/)).toHaveTextContent(
      'Median 50.00: Alpha above it; Charlie, Placeholder (not ranked) on it.',
    );

    rerender(panel({ ...matchups, median: 99 }));
    expect(screen.getByText(/^Median/)).toHaveTextContent('Median 99.00 so far: nobody above it.');

    rerender(panel({ ...matchups, median: undefined }));
    expect(screen.queryByText(/^Median/)).toBeNull();
  });

  it('gives a roster with no table row no colour, but keeps its place', () => {
    render(panel());
    const swatches = cards()[1].querySelectorAll<HTMLElement>('.swatch');
    expect(swatches).toHaveLength(2);
    expect(swatches[0].style.getPropertyValue('--swatch')).toBe('var(--team-3)');
    expect(swatches[1].style.getPropertyValue('--swatch')).toBe('');
    expect(swatches[1]).toHaveClass('is-unranked');
    expect(swatches[0]).not.toHaveClass('is-unranked');
    swatches.forEach((s) => expect(s).toHaveAttribute('aria-hidden', 'true'));
  });

  it('says a roster with no table row is not ranked', () => {
    render(panel());
    expect(within(cards()[1]).getByText('(not ranked)')).toBeInTheDocument();
    expect(within(cards()[0]).queryByText('(not ranked)')).toBeNull();
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
    expect(screen.getByRole('heading', { level: 2 })).toHaveTextContent('Week 4 · live');
  });
});

describe('The page', () => {
  it('shows the matchups right under the standings in head-to-head', async () => {
    serveApi(headToHead);
    render(<App />);
    const panel = await screen.findByRole('region', { name: 'Week 3 · live' });
    expect(panel.previousElementSibling).toHaveAttribute('id', 'standings');
  });

  it('shows no matchups panel when the season has none', async () => {
    serveApi(payload);
    render(<App />);
    await screen.findByRole('region', { name: 'Standings' });
    expect(screen.queryByRole('region', { name: /^Week/ })).toBeNull();
  });
});
