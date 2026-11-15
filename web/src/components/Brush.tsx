import { useRef } from 'react';
import { max, min } from 'd3-array';
import { scaleLinear } from 'd3-scale';
import { line } from 'd3-shape';
import type { Point, WeekRange } from '../api/calculations';
import { clampRange } from '../api/calculations';
import type { Series } from './Chart';
import { plotInsets } from './drawing';

interface BrushProps {
  /** The lines to sketch, every week of them. */
  series: Series[];
  width: number;
  /** The last week of the season so far. */
  maxWeek: number;
  /** The weeks the chart shows. */
  weeks: WeekRange;
  /** Every move, while dragging or while a key is held. */
  onChange: (next: WeekRange) => void;
  /** Once a drag or key press ends: the moment to write the URL (Safari limits history calls). */
  onCommit: () => void;
}

const Height = 44;
const Pad = 4;

/** What a drag is moving. */
type Grip =
  { kind: 'first' | 'last' } | { kind: 'pan'; offset: number } | { kind: 'new'; anchor: number };

/**
 * An overview of the whole season under the chart, with a window over the
 * weeks the chart shows. Drag across the strip to choose weeks, drag the window
 * to pan, drag a handle to widen or narrow. The handles are sliders, so a
 * keyboard can do everything a pointer can (T66).
 *
 * React draws it and handles the pointer; D3 only does the arithmetic (D9).
 */
export default function Brush({ series, width, maxWeek, weeks, onChange, onCommit }: BrushProps) {
  const grip = useRef<Grip | null>(null);
  const { left, right } = plotInsets(width);

  const x = scaleLinear()
    .domain([1, maxWeek])
    .range([left, width - right]);
  const values = series.flatMap((s) => s.points.map((p) => p.value));
  const y = scaleLinear()
    .domain([min(values) ?? 0, max(values) ?? 1])
    .range([Height - Pad, Pad]);
  const sketch = line<Point>()
    .x((p) => x(p.week))
    .y((p) => y(p.value));

  const [first, last] = weeks;
  const weekAt = (clientX: number, box: DOMRect) => x.invert(clientX - box.left);
  const set = (next: WeekRange) => onChange(clampRange(next, maxWeek));

  function onPointerDown(e: React.PointerEvent<HTMLDivElement>) {
    const target = e.target as HTMLElement;
    const week = weekAt(e.clientX, e.currentTarget.getBoundingClientRect());
    const part = target.dataset.part;

    // unzoomed, the window is the whole strip and has nowhere to pan, so a drag on it chooses weeks
    const whole = first === 1 && last === maxWeek;

    if (part === 'first' || part === 'last') grip.current = { kind: part };
    else if (part === 'window' && !whole) grip.current = { kind: 'pan', offset: week - first };
    else grip.current = { kind: 'new', anchor: Math.round(week) };

    e.currentTarget.setPointerCapture(e.pointerId);
    e.preventDefault();
  }

  function onPointerMove(e: React.PointerEvent<HTMLDivElement>) {
    const held = grip.current;
    if (!held) return;
    const week = Math.round(weekAt(e.clientX, e.currentTarget.getBoundingClientRect()));

    switch (held.kind) {
      case 'first':
        set([Math.min(week, last - 1), last]);
        break;
      case 'last':
        set([first, Math.max(week, first + 1)]);
        break;
      case 'pan': {
        const span = last - first;
        const start = Math.min(Math.max(Math.round(week - held.offset), 1), maxWeek - span);
        if (start !== first) onChange([start, start + span]);
        break;
      }
      case 'new':
        if (week !== held.anchor) set([Math.min(week, held.anchor), Math.max(week, held.anchor)]);
        break;
    }
  }

  function onPointerUp() {
    if (!grip.current) return;
    grip.current = null;
    onCommit();
  }

  /** Arrows step a week, Home and End go as far as the other handle allows. */
  function onKeyDown(end: 'first' | 'last', e: React.KeyboardEvent<HTMLDivElement>) {
    const step = { ArrowLeft: -1, ArrowDown: -1, ArrowRight: 1, ArrowUp: 1 }[e.key];
    const value = end === 'first' ? first : last;
    let next: number;

    if (step !== undefined) next = value + step;
    else if (e.key === 'Home') next = end === 'first' ? 1 : first + 1;
    else if (e.key === 'End') next = end === 'first' ? last - 1 : maxWeek;
    else return;

    e.preventDefault();
    // a handle stops at the other rather than pushing it
    if (end === 'first') set([Math.min(next, last - 1), last]);
    else set([first, Math.max(next, first + 1)]);
  }

  function handle(end: 'first' | 'last', label: string) {
    const value = end === 'first' ? first : last;

    return (
      <div
        className="brush-handle"
        data-part={end}
        role="slider"
        tabIndex={0}
        aria-label={label}
        aria-valuemin={end === 'first' ? 1 : first + 1}
        aria-valuemax={end === 'first' ? last - 1 : maxWeek}
        aria-valuenow={value}
        aria-valuetext={`Week ${value}`}
        style={{ left: x(value) }}
        onKeyDown={(e) => onKeyDown(end, e)}
        onKeyUp={onCommit}
      />
    );
  }

  return (
    <div
      className="brush"
      style={{ height: Height }}
      onPointerDown={onPointerDown}
      onPointerMove={onPointerMove}
      onPointerUp={onPointerUp}
      onPointerCancel={onPointerUp}
    >
      <svg width={width} height={Height} aria-hidden="true" focusable="false">
        {series.map((s) => (
          <path
            key={s.rosterId}
            className="brush-line"
            d={sketch(s.points) ?? undefined}
            stroke={s.colour}
          />
        ))}
      </svg>
      <div
        className="brush-window"
        data-part="window"
        style={{ left: x(first), width: x(last) - x(first) }}
      />
      {handle('first', 'First week')}
      {handle('last', 'Last week')}
    </div>
  );
}
