import { useId, useRef } from 'react';
import { max, min } from 'd3-array';
import { scaleLinear } from 'd3-scale';
import type { LinearScale } from './drawing';
import { LabelledMinWidth, Margin, plotInsets } from './drawing';
import { line } from 'd3-shape';
import { AnimatePresence, m, useReducedMotion } from 'motion/react';
import type { Point, WeekRange } from '../api/calculations';
import { pointsIn } from '../api/calculations';

export interface Series {
  rosterId: number;
  name: string;
  colour: string;
  points: Point[];
}

interface ChartProps {
  series: Series[];
  width: number;
  height: number;
  hovered?: number | null;
  onHover: (id: number | null) => void;
  zeroBased?: boolean;
  label: string;
  yTitle?: string;
  /** The weeks shown. Omitted, every week any line reaches. */
  weeks?: WeekRange;
}

// scales

/** Weeks across. Always at least two wide so a single week is not a zero domain. */
function weekScale(width: number, right: number, [first, last]: WeekRange): LinearScale {
  return scaleLinear()
    .domain([first, Math.max(last, first + 1)])
    .range([Margin.left, width - right]);
}

/** Values up. Padding is a fraction of the range so the zoom is the same at any size. */
function valueScale(lo: number, hi: number, height: number, zeroBased: boolean): LinearScale {
  const pad = (hi - lo) * 0.08 || hi * 0.08 || 1;

  return scaleLinear()
    .domain([zeroBased ? Math.max(0, lo - pad) : lo - pad, hi + pad])
    .nice()
    .range([height - Margin.bottom, Margin.top]);
}

// parts

function Gridlines({
  y,
  width,
  right,
  count,
}: {
  y: LinearScale;
  width: number;
  right: number;
  count: number;
}) {
  const format = y.tickFormat(count);

  return (
    <g className="grid">
      {y.ticks(count).map((t) => (
        <g key={t} className="tick">
          <line x1={Margin.left} x2={width - right} y1={y(t)} y2={y(t)} />
          <text x={Margin.left - 8} y={y(t)} dy="0.32em" textAnchor="end">
            {format(t)}
          </text>
        </g>
      ))}
    </g>
  );
}

function WeekAxis({ x, height, span }: { x: LinearScale; height: number; span: number }) {
  return (
    <g className="x-axis">
      {x
        .ticks(Math.min(span, 9))
        .filter(Number.isInteger)
        .map((t) => (
          <text
            key={t}
            className="tick"
            x={x(t)}
            y={height - Margin.bottom + 18}
            textAnchor="middle"
          >
            {t}
          </text>
        ))}
    </g>
  );
}

function SeriesLabels({
  series,
  x,
  y,
  hovered,
}: {
  series: Series[];
  x: LinearScale;
  y: LinearScale;
  hovered: number | null;
}) {
  return (
    <>
      {series.map((s) => {
        const last = s.points.at(-1);
        if (!last) return null;

        return (
          <text
            key={s.rosterId}
            className="series-label"
            x={x(last.week) + 8}
            y={y(last.value)}
            dy="0.32em"
            fill={s.colour}
            data-muted={hovered !== null && hovered !== s.rosterId}
            data-raised={hovered === s.rosterId}
          >
            {s.name}
          </text>
        );
      })}
    </>
  );
}

/**
 * Every visible team's line over the weeks played. Hovering with a mouse, tapping,
 * or focusing a team raises its line and mutes the rest.
 *
 * A line keeps one path for its whole life: a change of metric or mode changes
 * its shape in place, so nothing fades out and redraws (T66). It draws in only
 * when it first appears. Opacity is left to the stylesheet, which mutes lines.
 */
export default function Chart({
  series,
  width,
  height,
  hovered = null,
  onHover,
  zeroBased = true,
  label,
  yTitle = '',
  weeks,
}: ChartProps) {
  const reduced = useReducedMotion();
  const clipId = useId();
  // a click does not say whether it was a tap, so the pointer that started it is kept
  const pointer = useRef<string>('mouse');

  // below this the line-end names collide and clip; the team chips are the legend instead
  const labelled = width >= LabelledMinWidth;
  const { right } = plotInsets(width);

  if (width <= Margin.left + right || series.length === 0) return null;

  // extents, from the weeks in view only, so a zoom rescales the values too
  const lastWeek = max(series, (s) => max(s.points, (p) => p.week)) ?? 1;
  const range: WeekRange = weeks ?? [1, Math.max(lastWeek, 2)];
  const inView = series.map((s) => ({ ...s, points: pointsIn(s.points, range) }));
  const extentOf = inView.some((s) => s.points.length > 0) ? inView : series;
  const maxValue = max(extentOf, (s) => max(s.points, (p) => p.value)) ?? 0;
  const minValue = min(extentOf, (s) => min(s.points, (p) => p.value)) ?? 0;

  const x = weekScale(width, right, range);
  const y = valueScale(minValue, maxValue, height, zeroBased);
  const tickCount = Math.max(2, Math.round((height - Margin.top - Margin.bottom) / 48));
  const plotWidth = width - Margin.left - right;
  const plotHeight = height - Margin.top - Margin.bottom;

  const path = line<Point>()
    .defined((p) => Number.isFinite(p.value))
    .x((p) => x(p.week))
    .y((p) => y(p.value));

  // svg has no z-index so the raised line is drawn last
  const ordered =
    hovered === null
      ? series
      : [
          ...series.filter((s) => s.rosterId !== hovered),
          ...series.filter((s) => s.rosterId === hovered),
        ];

  /** Nearest line to the pointer. Vertical distance at the snapped week in view. */
  function nearest(e: React.PointerEvent<SVGRectElement> | React.MouseEvent<SVGRectElement>) {
    const box = e.currentTarget.getBoundingClientRect();
    const px = e.clientX - box.left + Margin.left;
    const py = e.clientY - box.top + Margin.top;
    const week = Math.min(Math.max(Math.round(x.invert(px)), range[0]), range[1]);

    let best: { id: number; d: number } | null = null;
    for (const s of series) {
      const p = s.points.find((q) => q.week === week);
      if (!p) continue;

      const d = Math.abs(y(p.value) - py);
      if (best === null || d < best.d) best = { id: s.rosterId, d };
    }

    return best ? best.id : null;
  }

  // a touch that starts a scroll must not raise a line, so only a mouse hovers;
  // a tap is a click, which a scroll never produces
  function onMove(e: React.PointerEvent<SVGRectElement>) {
    pointer.current = e.pointerType;
    if (e.pointerType === 'mouse') onHover(nearest(e));
  }

  function onTap(e: React.MouseEvent<SVGRectElement>) {
    if (pointer.current === 'mouse') return;
    const id = nearest(e);
    onHover(id === hovered ? null : id);
  }

  return (
    <svg
      className="chart"
      viewBox={`0 0 ${width} ${height}`}
      width={width}
      height={height}
      role="img"
      aria-label={label}
    >
      <defs>
        <clipPath id={clipId}>
          {/* the stroke's half-width either side, so a line on the edge is not shaved */}
          <rect x={Margin.left - 2} y={0} width={plotWidth + 4} height={height} />
        </clipPath>
      </defs>

      <Gridlines y={y} width={width} right={right} count={tickCount} />

      {/* zero matters only when values cross it */}
      {(y.domain()[0] ?? 0) < 0 ? (
        <line className="zero" x1={Margin.left} x2={width - right} y1={y(0)} y2={y(0)} />
      ) : null}

      {yTitle ? (
        <text className="y-title" x={Margin.left} y={Margin.top - 4} textAnchor="start">
          {yTitle}
        </text>
      ) : null}

      <WeekAxis x={x} height={height} span={range[1] - range[0] + 1} />

      <g clipPath={`url(#${clipId})`}>
        <AnimatePresence initial={!reduced}>
          {ordered.map((s) => (
            <m.path
              key={s.rosterId}
              className="series"
              d={path(s.points) ?? undefined}
              stroke={s.colour}
              data-muted={hovered !== null && hovered !== s.rosterId}
              data-raised={hovered === s.rosterId}
              initial={{ pathLength: 0 }}
              animate={{ pathLength: 1 }}
              exit={{ pathLength: 0 }}
              transition={reduced ? { duration: 0 } : { duration: 0.8, ease: 'easeOut' }}
            />
          ))}
        </AnimatePresence>
      </g>

      {labelled ? <SeriesLabels series={inView} x={x} y={y} hovered={hovered} /> : null}

      {/* one transparent area takes the pointer: a line under 2px wide is too thin to aim at */}
      <rect
        className="hit"
        x={Margin.left}
        y={Margin.top}
        width={plotWidth}
        height={plotHeight}
        fill="transparent"
        onPointerDown={(e) => (pointer.current = e.pointerType)}
        onPointerMove={onMove}
        onPointerLeave={(e) => (e.pointerType === 'mouse' ? onHover(null) : undefined)}
        onClick={onTap}
      />
    </svg>
  );
}
