import { describe, expect, it, vi } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import { scaleLinear } from 'd3-scale';
import Chart from '../src/components/Chart';
import type { Series } from '../src/components/Chart';
import SegmentedRow from '../src/components/SegmentedRow';
import SigmaDisplay from '../src/components/SigmaDisplay';
import Masthead from '../src/components/Masthead';
import { payload } from './fixture';

function series(rosterId: number, name: string, values: number[]): Series {
  return {
    rosterId,
    name,
    colour: `var(--team-${rosterId})`,
    points: values.map((value, i) => ({ week: i + 1, value })),
  };
}

const two = [series(1, 'Alpha', [10, 40]), series(2, 'Bravo', [20, 30])];
const paths = (c: HTMLElement) => [...c.querySelectorAll('path.series')];

describe('Chart', () => {
  it('renders nothing when there is no room for the margins', () => {
    const { container } = render(
      <Chart series={two} width={50} height={200} onHover={() => {}} label="x" />,
    );
    expect(container.querySelector('svg')).toBeNull();
  });

  it('renders nothing with no series', () => {
    const { container } = render(
      <Chart series={[]} width={800} height={400} onHover={() => {}} label="x" />,
    );
    expect(container.querySelector('svg')).toBeNull();
  });

  it('draws one path and one label per team', () => {
    const { container } = render(
      <Chart series={two} width={800} height={400} onHover={() => {}} label="x" />,
    );
    expect(paths(container)).toHaveLength(2);
    expect(container.querySelectorAll('.series-label')).toHaveLength(2);
  });

  it('drops the line-end names on a narrow chart, where they would collide and clip', () => {
    const { container } = render(
      <Chart series={two} width={343} height={172} onHover={() => {}} label="x" />,
    );
    expect(paths(container)).toHaveLength(2);
    expect(container.querySelectorAll('.series-label')).toHaveLength(0);
  });

  it('is announced as an image with the label it is given', () => {
    render(
      <Chart series={two} width={800} height={400} onHover={() => {}} label="Cumulative points" />,
    );
    expect(screen.getByRole('img')).toHaveAttribute('aria-label', 'Cumulative points');
  });

  it('marks the hovered team raised and the rest muted', () => {
    const { container } = render(
      <Chart series={two} width={800} height={400} hovered={1} onHover={() => {}} label="x" />,
    );
    const states = paths(container).map((p) => [
      p.getAttribute('data-raised'),
      p.getAttribute('data-muted'),
    ]);
    expect(states).toContainEqual(['true', 'false']);
    expect(states).toContainEqual(['false', 'true']);
  });

  it('draws the hovered line last because svg has no z-index', () => {
    const { container } = render(
      <Chart series={two} width={800} height={400} hovered={1} onHover={() => {}} label="x" />,
    );
    expect(paths(container).at(-1)).toHaveAttribute('data-raised', 'true');
  });

  it('omits the zero rule when every value is above it', () => {
    const { container } = render(
      <Chart series={two} width={800} height={400} onHover={() => {}} label="x" />,
    );
    expect(container.querySelector('.zero')).toBeNull();
  });

  it('draws the zero rule once values cross it', () => {
    const diverging = [series(1, 'Alpha', [-20, 30])];
    const { container } = render(
      <Chart
        series={diverging}
        width={800}
        height={400}
        zeroBased={false}
        onHover={() => {}}
        label="x"
      />,
    );
    expect(container.querySelector('.zero')).not.toBeNull();
  });

  it('keeps negatives out of the domain when zero based', () => {
    const { container } = render(
      <Chart series={two} width={800} height={400} zeroBased onHover={() => {}} label="x" />,
    );
    const labels = [...container.querySelectorAll('.grid text')].map((t) => Number(t.textContent));
    expect(Math.min(...labels)).toBeGreaterThanOrEqual(0);
  });

  it('shows the axis title only when given one', () => {
    const { container, rerender } = render(
      <Chart series={two} width={800} height={400} onHover={() => {}} label="x" />,
    );
    expect(container.querySelector('.y-title')).toBeNull();

    rerender(
      <Chart series={two} width={800} height={400} onHover={() => {}} label="x" yTitle="CV" />,
    );
    expect(container.querySelector('.y-title')).toHaveTextContent('CV');
  });

  it('covers the plot with one transparent hit area', () => {
    const { container } = render(
      <Chart series={two} width={800} height={400} onHover={() => {}} label="x" />,
    );
    const hit = container.querySelector('.hit')!;
    // none would receive no pointer events at all
    expect(hit).toHaveAttribute('fill', 'transparent');
    expect(container.querySelector('svg')!.lastElementChild).toBe(hit);
  });

  it('clears the hover when the pointer leaves', () => {
    const onHover = vi.fn();
    const { container } = render(
      <Chart series={two} width={800} height={400} onHover={onHover} label="x" />,
    );
    container
      .querySelector('.hit')!
      .dispatchEvent(new MouseEvent('pointerout', { bubbles: true, relatedTarget: document.body }));
    expect(onHover).toHaveBeenCalledWith(null);
  });
});

describe('SegmentedRow', () => {
  const options = [
    ['a', 'Alpha'],
    ['b', 'Bravo'],
  ] as const;

  it('points the group at its visible label', () => {
    render(
      <SegmentedRow name="metric" label="Metric" options={options} value="a" onChange={() => {}} />,
    );
    const group = screen.getByRole('group', { name: 'Metric' });
    expect(group).toHaveAttribute('aria-labelledby', 'metric-label');
    expect(document.getElementById('metric-label')).toHaveTextContent('Metric');
  });

  it('presses exactly one option', () => {
    render(
      <SegmentedRow name="metric" label="Metric" options={options} value="b" onChange={() => {}} />,
    );
    const pressed = within(screen.getByRole('group', { name: 'Metric' }))
      .getAllByRole('button')
      .filter((b) => b.getAttribute('aria-pressed') === 'true');
    expect(pressed).toHaveLength(1);
    expect(pressed[0]).toHaveTextContent('Bravo');
  });

  it('reports the value rather than the label', () => {
    const onChange = vi.fn();
    render(
      <SegmentedRow name="metric" label="Metric" options={options} value="a" onChange={onChange} />,
    );
    screen.getByRole('button', { name: 'Bravo' }).click();
    expect(onChange).toHaveBeenCalledWith('b');
  });

  it('uses real buttons so enter and space work unaided', () => {
    const { container } = render(
      <SegmentedRow name="metric" label="Metric" options={options} value="a" onChange={() => {}} />,
    );
    for (const b of container.querySelectorAll('.segmented button')) {
      expect(b).toHaveAttribute('type', 'button');
    }
  });
});

describe('SigmaDisplay', () => {
  const scale = scaleLinear().domain([100, 200]).range([2, 138]);
  const totals = {
    actual: 600,
    ideal: 700,
    sigma: 10,
    cv: 0.07,
    mean: 150,
    min: 120,
    max: 180,
  };

  it('spans the min to max range on the shared scale', () => {
    const { container } = render(<SigmaDisplay totals={totals} scaleFunction={scale} />);
    const rule = container.querySelector('line')!;
    expect(Number(rule.getAttribute('x1'))).toBeCloseTo(scale(120), 5);
    expect(Number(rule.getAttribute('x2'))).toBeCloseTo(scale(180), 5);
  });

  it('shades one sigma either side of the mean', () => {
    const { container } = render(<SigmaDisplay totals={totals} scaleFunction={scale} />);
    const band = container.querySelector('rect')!;
    expect(Number(band.getAttribute('x'))).toBeCloseTo(scale(140), 5);
    expect(Number(band.getAttribute('width'))).toBeCloseTo(scale(160) - scale(140), 5);
  });

  it('reads out the figures rounded', () => {
    render(<SigmaDisplay totals={{ ...totals, min: 120.34 }} scaleFunction={scale} />);
    expect(screen.getByRole('img')).toHaveAttribute('aria-label', expect.stringContaining('120.3'));
  });
});

it('interpolates rather than printing the template', () => {
  const { container } = render(<Masthead payload={payload} updatedAgo="just now" error={false} />);
  expect(container.textContent).not.toContain('${');
});
