import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen, within } from '@testing-library/react';
import Chart from '../src/components/Chart';
import type { Series } from '../src/components/Chart';
import Brush from '../src/components/Brush';
import type { WeekRange } from '../src/api/calculations';
import SegmentedRow from '../src/components/SegmentedRow';

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
      .dispatchEvent(pointer('pointerout', 'mouse', { relatedTarget: document.body }));
    expect(onHover).toHaveBeenCalledWith(null);
  });

  it('ignores a touch moving over the plot, which is the start of a scroll (T66)', () => {
    const onHover = vi.fn();
    const { container } = render(
      <Chart series={two} width={800} height={400} onHover={onHover} label="x" />,
    );
    const hit = container.querySelector('.hit')!;
    hit.dispatchEvent(pointer('pointermove', 'touch', { clientX: 400, clientY: 200 }));
    hit.dispatchEvent(pointer('pointerout', 'touch', { relatedTarget: document.body }));
    expect(onHover).not.toHaveBeenCalled();
  });

  it('raises the nearest line on a tap, and lowers it on a second tap', () => {
    const onHover = vi.fn();
    const { container, rerender } = render(
      <Chart series={two} width={800} height={400} onHover={onHover} label="x" />,
    );
    const tap = () => {
      const hit = container.querySelector('.hit')!;
      hit.dispatchEvent(pointer('pointerdown', 'touch'));
      hit.dispatchEvent(new MouseEvent('click', { bubbles: true, clientX: 0, clientY: 0 }));
    };
    tap();
    const raised = onHover.mock.calls[0]![0] as number;
    expect([1, 2]).toContain(raised);

    rerender(
      <Chart series={two} width={800} height={400} hovered={raised} onHover={onHover} label="x" />,
    );
    tap();
    expect(onHover).toHaveBeenLastCalledWith(null);
  });

  it('keeps one path per team when the lines change shape, so nothing redraws (T66)', () => {
    const { container, rerender } = render(
      <Chart series={two} width={800} height={400} onHover={() => {}} label="x" />,
    );
    const before = paths(container);
    const reshaped = [series(1, 'Alpha', [5, 6, 7]), series(2, 'Bravo', [9, 1, 2])];
    rerender(<Chart series={reshaped} width={800} height={400} onHover={() => {}} label="x" />);
    expect(paths(container)).toEqual(before);
  });

  it('leaves opacity to the stylesheet, so a muted line really dims', () => {
    const { container } = render(
      <Chart series={two} width={800} height={400} hovered={1} onHover={() => {}} label="x" />,
    );
    for (const p of paths(container)) expect(p.getAttribute('style') ?? '').not.toMatch(/opacity/);
  });

  it('shows only the zoomed weeks and fits the values to them', () => {
    const long = [
      series(1, 'Alpha', [1, 2, 3, 4, 5, 6, 7, 8]),
      series(2, 'Bravo', [80, 70, 60, 50, 40, 30, 20, 10]),
    ];
    const { container } = render(
      <Chart series={long} width={800} height={400} weeks={[3, 5]} onHover={() => {}} label="x" />,
    );
    const ticks = [...container.querySelectorAll('.x-axis text')].map((t) => Number(t.textContent));
    expect(Math.min(...ticks)).toBe(3);
    expect(Math.max(...ticks)).toBe(5);
    expect(container.querySelector('clipPath')).not.toBeNull();

    // Bravo reaches 80 in week 1, out of view; in view it peaks at 60
    const values = [...container.querySelectorAll('.grid text')].map((t) => Number(t.textContent));
    expect(Math.max(...values)).toBeLessThan(80);
  });
});

/** A pointer event jsdom can dispatch, with the pointer type React reads. */
function pointer(type: string, pointerType: string, init: MouseEventInit = {}) {
  const event = new MouseEvent(type, { bubbles: true, ...init });
  Object.defineProperty(event, 'pointerType', { value: pointerType });
  Object.defineProperty(event, 'pointerId', { value: 1 });
  return event;
}

describe('Brush', () => {
  const season = [series(1, 'Alpha', [1, 2, 3, 4, 5, 6, 7, 8])];
  const sliders = () => screen.getAllByRole('slider');

  function brush(weeks: WeekRange, onChange = vi.fn(), onCommit = vi.fn()) {
    render(
      <Brush
        series={season}
        width={800}
        maxWeek={8}
        weeks={weeks}
        onChange={onChange}
        onCommit={onCommit}
      />,
    );
    return { onChange, onCommit };
  }

  it('offers two labelled sliders that say which week they stand on', () => {
    brush([3, 6]);
    const [first, last] = sliders();
    expect(first).toHaveAccessibleName('First week');
    expect(last).toHaveAccessibleName('Last week');
    expect(first).toHaveAttribute('aria-valuetext', 'Week 3');
    // a handle cannot pass the other
    expect(first).toHaveAttribute('aria-valuemax', '5');
    expect(last).toHaveAttribute('aria-valuemin', '4');
  });

  it('steps a week with the arrows and commits when the key comes up', () => {
    const { onChange, onCommit } = brush([3, 6]);
    const [first] = sliders();
    fireEvent.keyDown(first!, { key: 'ArrowRight' });
    expect(onChange).toHaveBeenLastCalledWith([4, 6]);
    expect(onCommit).not.toHaveBeenCalled();
    fireEvent.keyUp(first!, { key: 'ArrowRight' });
    expect(onCommit).toHaveBeenCalled();
  });

  it('stops a handle at the other rather than crossing it', () => {
    const { onChange } = brush([3, 4]);
    fireEvent.keyDown(sliders()[0]!, { key: 'End' });
    expect(onChange).toHaveBeenLastCalledWith([3, 4]);
    fireEvent.keyDown(sliders()[1]!, { key: 'Home' });
    expect(onChange).toHaveBeenLastCalledWith([3, 4]);
  });

  it('chooses weeks from a drag across an unzoomed strip, where the window has nowhere to pan', () => {
    const { onChange, onCommit } = brush([1, 8]);
    const strip = document.querySelector('.brush')!;
    // the strip is 800 wide: weeks 1 to 8 run from 44 to 716, 96 px apart
    const at = (week: number) => 44 + (week - 1) * 96;
    strip.dispatchEvent(pointer('pointerdown', 'touch', { clientX: at(3) }));
    strip.dispatchEvent(pointer('pointermove', 'touch', { clientX: at(6) }));
    expect(onChange).toHaveBeenLastCalledWith([3, 6]);
    strip.dispatchEvent(pointer('pointerup', 'touch', { clientX: at(6) }));
    expect(onCommit).toHaveBeenCalledTimes(1);
  });

  it('sends Home and End to the ends of the season', () => {
    const { onChange } = brush([3, 6]);
    fireEvent.keyDown(sliders()[0]!, { key: 'Home' });
    expect(onChange).toHaveBeenLastCalledWith([1, 6]);
    fireEvent.keyDown(sliders()[1]!, { key: 'End' });
    expect(onChange).toHaveBeenLastCalledWith([3, 8]);
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
});
