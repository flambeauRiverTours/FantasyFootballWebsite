import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';
import Radar from '../src/components/Radar';
import SlotBars from '../src/components/SlotBars';
import RadarPanel from '../src/components/RadarPanel';
import type { SeasonPayload } from '../src/api/types';
import { payload as base, teams as baseTeams } from './fixture';

const slots = ['QB', 'RB', 'WR'];

describe('Radar', () => {
  it('renders nothing with no slots', () => {
    const { container } = render(<Radar slots={[]} ratios={[]} size={400} label="x" />);
    expect(container.querySelector('svg')).toBeNull();
  });

  it('renders nothing with no room to draw', () => {
    const { container } = render(<Radar slots={slots} ratios={[1, 1, 1]} size={0} label="x" />);
    expect(container.querySelector('svg')).toBeNull();
  });

  it('places one vertex, one spoke and one label per slot', () => {
    const { container } = render(
      <Radar slots={slots} ratios={[1.2, 0.8, 1]} size={400} label="x" />,
    );
    expect(container.querySelectorAll('.spoke')).toHaveLength(3);
    expect(container.querySelectorAll('.axis-label')).toHaveLength(3);
    expect(container.querySelector('polygon.shape')).not.toBeNull();
  });

  it('starts the first axis at twelve oclock', () => {
    const { container } = render(<Radar slots={['QB']} ratios={[2]} size={400} label="x" />);
    const spoke = container.querySelector('.spoke')!;
    // clamped to 2x, so it sits straight above centre
    expect(Number(spoke.getAttribute('x2'))).toBeCloseTo(Number(spoke.getAttribute('x1')), 3);
    expect(Number(spoke.getAttribute('y2'))).toBeLessThan(Number(spoke.getAttribute('y1')));
  });

  it('marks a null axis rather than plotting it as 1.0', () => {
    const { container } = render(
      <Radar slots={slots} ratios={[1.2, null, 1]} size={400} label="x" />,
    );
    expect(container.querySelectorAll('.unmeasured')).toHaveLength(1);
  });

  it('is announced as an image with the label it is given', () => {
    render(<Radar slots={slots} ratios={[1, 1, 1]} size={400} label="Alpha above at QB" />);
    expect(screen.getByRole('img')).toHaveAttribute('aria-label', 'Alpha above at QB');
  });

  it('clamps a runaway axis rather than escaping the drawing', () => {
    const { container } = render(<Radar slots={['QB']} ratios={[50]} size={400} label="x" />);
    const [x, y] = container.querySelector('polygon')!.getAttribute('points')!.split(',').map(Number);
    // R=90, clamp at 2x -> 180 out from centre (230)
    expect(y).toBeCloseTo(230 - 180, 1);
    expect(x).toBeCloseTo(230, 1);
  });

  it('clamps a negative ratio to the centre rather than mirroring onto another axis', () => {
    // a penalty-heavy DST/IDP week can score negative; a negative radius would
    // otherwise plot the vertex on the opposite side of the wheel
    const { container } = render(<Radar slots={['QB']} ratios={[-3]} size={400} label="x" />);
    const [x, y] = container.querySelector('polygon')!.getAttribute('points')!.split(',').map(Number);
    expect(x).toBeCloseTo(230, 1);
    expect(y).toBeCloseTo(230, 1);
  });
});

describe('SlotBars', () => {
  it('renders nothing with no slots', () => {
    const { container } = render(<SlotBars slots={[]} ratios={[]} width={800} label="x" />);
    expect(container.querySelector('svg')).toBeNull();
  });

  it('renders nothing with no room for the margins', () => {
    const { container } = render(
      <SlotBars slots={slots} ratios={[1, 1, 1]} width={100} label="x" />,
    );
    expect(container.querySelector('svg')).toBeNull();
  });

  it('draws one bar per measured slot and marks unmeasured ones as text', () => {
    const { container } = render(
      <SlotBars slots={slots} ratios={[1.2, null, 0.9]} width={800} label="x" />,
    );
    expect(container.querySelectorAll('.bar')).toHaveLength(2);
    expect(container.querySelectorAll('.unmeasured')).toHaveLength(1);
  });

  it('flags an above-average bar so it takes the other hue', () => {
    const { container } = render(
      <SlotBars slots={['QB']} ratios={[1.3]} width={800} label="x" />,
    );
    expect(container.querySelector('.bar')).toHaveAttribute('data-above', 'true');
  });

  it('flags a below-average bar the other way', () => {
    const { container } = render(
      <SlotBars slots={['QB']} ratios={[0.7]} width={800} label="x" />,
    );
    expect(container.querySelector('.bar')).toHaveAttribute('data-above', 'false');
  });

  it('is announced as an image with the label it is given', () => {
    render(<SlotBars slots={slots} ratios={[1, 1, 1]} width={800} label="Bravo below at RB" />);
    expect(screen.getByRole('img')).toHaveAttribute('aria-label', 'Bravo below at RB');
  });
});

describe('RadarPanel', () => {
  function withSlots(): SeasonPayload {
    return {
      ...base,
      slotOrder: ['QB', 'RB'],
      leagueSlotAverages: { QB: 20, RB: 10 },
      teams: baseTeams.map((t, i) => ({
        ...t,
        slotAverages: i === 0 ? { QB: 24, RB: 5 } : { QB: 20, RB: 10 },
      })),
    };
  }

  it('defaults to the ranked leader', () => {
    render(<RadarPanel payload={withSlots()} />);
    const pressed = screen
      .getAllByRole('button')
      .find((b) => b.getAttribute('aria-pressed') === 'true' && b.textContent?.includes('Alpha'));
    expect(pressed).toBeDefined();
  });

  it('falls back to the leader when ?team names a roster that no longer exists', () => {
    // unstubAllGlobals would also clear setup.ts's ResizeObserver stub, so
    // restore just this one rather than reaching for it
    const original = window.location;
    vi.stubGlobal('location', { ...original, search: '?team=999' });

    render(<RadarPanel payload={withSlots()} />);
    const pressed = screen
      .getAllByRole('button')
      .find((b) => b.getAttribute('aria-pressed') === 'true');
    expect(pressed).toHaveTextContent('Alpha');

    vi.stubGlobal('location', original);
  });

  it("draws the shape in the selected team's chip colour", () => {
    const { container } = render(<RadarPanel payload={withSlots()} />);
    const radarColour = () =>
      (container.querySelector('svg.radar') as SVGElement).style.getPropertyValue('--team');
    const chipColour = (name: string) =>
      screen.getByRole('button', { name }).style.getPropertyValue('--chip');

    expect(radarColour()).toBe(chipColour('Alpha'));

    try {
      fireEvent.click(screen.getByRole('button', { name: 'Bravo' }));
      expect(radarColour()).toBe(chipColour('Bravo'));
      expect(radarColour()).not.toBe(chipColour('Alpha'));
    } finally {
      // selecting a team writes ?team= into the URL, which the next test would read
      window.history.replaceState(null, '', window.location.pathname);
    }
  });

  it('draws the radar by default and switches to bars on toggle', () => {
    const { container } = render(<RadarPanel payload={withSlots()} />);
    expect(container.querySelector('svg.radar')).not.toBeNull();

    fireEvent.click(screen.getByRole('button', { name: 'Bars' }));
    expect(container.querySelector('svg.bars')).not.toBeNull();
    expect(container.querySelector('svg.radar')).toBeNull();
  });

  it("names the leader's above and below average axes in the aria label", () => {
    render(<RadarPanel payload={withSlots()} />);
    const img = screen.getByRole('img');
    expect(img.getAttribute('aria-label')).toContain('Above average: QB');
    expect(img.getAttribute('aria-label')).toContain('Below average: RB');
  });
});
