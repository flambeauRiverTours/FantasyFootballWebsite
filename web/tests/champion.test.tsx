import { describe, expect, it } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import StandingsPanel from '../src/components/StandingsPanel';
import type { SeasonPayload } from '../src/api/types';
import { bracket, headToHead, payload } from './fixture';

// T70: the champion's row in the standings
const crowned = (shown: SeasonPayload) => render(<StandingsPanel payload={shown} />);
const row = (name: string) => screen.getByRole('row', { name: new RegExp(name) });

describe('The champion', () => {
  it('wears a crown named "Champion" before the name, and a tinted row', () => {
    crowned({ ...payload, champions: [2] });
    expect(within(row('Bravo')).getByRole('img', { name: 'Champion' })).toBeInTheDocument();
    expect(row('Bravo')).toHaveClass('is-champion');
    expect(row('Alpha')).not.toHaveClass('is-champion');
  });

  it('keeps the crown on the same line as the first word of the name', () => {
    crowned({
      ...payload,
      teams: [{ ...payload.teams[0], name: 'Gridiron Gnomes' }],
      champions: [1],
    });
    const lead = screen.getByRole('img', { name: 'Champion' }).closest('.champion-lead');
    expect(lead).toHaveTextContent(/^Gridiron$/);
  });

  it('hides the sparkles from assistive technology', () => {
    const { container } = crowned({ ...payload, champions: [2] });
    const sparkles = container.querySelector('.sparkles');
    expect(sparkles).toHaveAttribute('aria-hidden', 'true');
    expect(sparkles?.children.length).toBeGreaterThanOrEqual(3);
  });

  it('crowns nobody while there is no champion', () => {
    const { container } = crowned(payload);
    expect(screen.queryByRole('img', { name: /champion/i })).toBeNull();
    expect(screen.queryByText(/^Season complete/)).toBeNull();
    expect(container.querySelector('.is-champion')).toBeNull();
  });

  it('crowns everyone tied as co-champions', () => {
    crowned({ ...payload, champions: [1, 3] });
    expect(screen.getAllByRole('img', { name: 'Co-champion' })).toHaveLength(2);
  });

  it('says in words that the season is complete and who won it', () => {
    crowned({ ...payload, champions: [2] });
    expect(screen.getByText(/^Season complete/)).toHaveTextContent(
      'Season complete. Champion: Bravo.',
    );
  });

  it('names a champion with no table row from the bracket', () => {
    // Q9: the placeholder won the final; it has no row to crown
    crowned({ ...headToHead, champions: [9], playoffs: bracket });
    expect(screen.getByText(/^Season complete/)).toHaveTextContent(
      'Season complete. Champion: Placeholder.',
    );
    expect(screen.queryByRole('img', { name: /champion/i })).toBeNull();
  });

  it('names co-champions in words', () => {
    crowned({ ...payload, champions: [1, 3] });
    expect(screen.getByText(/^Season complete/)).toHaveTextContent(
      'Season complete. Co-champions: Alpha and Charlie.',
    );
  });

  it('crowns the record leader in the head-to-head table too', () => {
    crowned({ ...headToHead, champions: [3] });
    expect(within(row('Charlie')).getByRole('img', { name: 'Champion' })).toBeInTheDocument();
  });
});
