import { afterEach, describe, expect, it, vi } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import App from '../src/App';
import PlayoffsPanel from '../src/components/PlayoffsPanel';
import type { PlayoffsPayload, SeasonPayload } from '../src/api/types';
import { bracket, headToHead } from './fixture';
import { serveApi } from './api';

const panel = (shown: PlayoffsPayload = bracket, payload: SeasonPayload = headToHead) =>
  render(<PlayoffsPanel payload={payload} playoffs={shown} />);
const card = (label: string) =>
  screen.getByText(label, { selector: '.bracket-label' }).closest('li')!;

afterEach(() => {
  vi.restoreAllMocks();
});

describe('PlayoffsPanel', () => {
  it('is a region named Playoffs, with a heading per round', () => {
    panel();
    const region = screen.getByRole('region', { name: 'Playoffs' });
    expect(
      within(region)
        .getAllByRole('heading', { level: 3 })
        .map((h) => h.textContent),
    ).toEqual(['Semifinals · Week 15 results', 'Final · Week 16 · live']);
  });

  it('makes the bracket a named region a keyboard can reach, for when it scrolls', () => {
    panel();
    expect(screen.getByRole('region', { name: 'Playoff bracket' })).toHaveAttribute(
      'tabindex',
      '0',
    );
  });

  it('labels every match in visible text', () => {
    panel();
    for (const label of ['Semifinal 1', 'Semifinal 2', 'Final', '3rd place']) {
      expect(card(label)).toBeInTheDocument();
    }
  });

  it('names a side still to come by where it comes from', () => {
    panel();
    // by the card's own label, never Sleeper's match id, which no card shows
    expect(card('Final')).toHaveTextContent('Winner of Semifinal 2');
    expect(card('3rd place')).toHaveTextContent('Loser of Semifinal 1');
    expect(card('3rd place')).toHaveTextContent('To be decided');
    expect(card('3rd place').querySelector('.swatch.is-unranked')).toBeNull();
  });

  it('bolds a settled winner, and says a tiebreak decided it', () => {
    panel();
    expect(card('Semifinal 1').querySelector('.is-winner')).toHaveTextContent('Alpha');
    expect(card('Semifinal 2').querySelector('.is-winner')).toHaveTextContent('Placeholder');
    expect(card('Semifinal 2')).toHaveTextContent(/won, on tiebreak/);
    expect(card('Semifinal 2').querySelector('.is-winner')).toHaveTextContent('(tiebreak)');
    expect(card('Final').querySelector('.is-winner')).toBeNull();
  });

  it('crowns the final s winner once the season has its champion', () => {
    const settled: PlayoffsPayload = {
      rounds: bracket.rounds.map((r) => ({
        ...r,
        matches: r.matches.map((m) =>
          m.placement === 1
            ? {
                ...m,
                isSettled: true,
                sides: [
                  { ...m.sides[0], won: true },
                  {
                    rosterId: 9,
                    name: 'Placeholder',
                    from: { winnerOf: 2 },
                    points: 40,
                    won: false,
                  },
                ],
              }
            : m,
        ),
      })),
    };
    panel(settled, { ...headToHead, champions: [1], playoffs: settled });
    expect(within(card('Final')).getByRole('img', { name: 'Champion' })).toBeInTheDocument();
  });

  it('announces nothing as scores change', () => {
    const { container } = panel();
    expect(container.querySelector('[aria-live]')).toBeNull();
  });
});

describe('The page in the playoffs', () => {
  it('shows the bracket in place of this week s matchups', async () => {
    serveApi({ ...headToHead, playoffs: bracket });
    render(<App />);
    const region = await screen.findByRole('region', { name: 'Playoffs' });
    expect(region.previousElementSibling).toHaveAttribute('id', 'standings');
    expect(screen.queryByRole('region', { name: /^Week/ })).toBeNull();
  });
});
