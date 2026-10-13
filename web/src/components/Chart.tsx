import { max, min } from 'd3-array';
import { scaleLinear } from 'd3-scale';
import type { LinearScale } from './drawing';
import { line } from 'd3-shape';
import { AnimatePresence, m, useReducedMotion } from 'motion/react';
import type { Point } from '../api/calculations';

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
  displayKey?: string;
}

const Margin = { top: 16, right: 84, bottom: 28, left: 44 };

/** The narrowest chart that keeps its line-end names; roughly the 40rem breakpoint less panel padding. */
const LabelledMinWidth = 600;

/** The right margin once the names are dropped: room for the last point, nothing more. */
const UnlabelledRight = 16;

// scales

/** Weeks across. Always at least two wide so a single week is not a zero domain. */
function weekScale(width: number, right: number, maxWeek: number): LinearScale {
  return scaleLinear()
    .domain([1, Math.max(maxWeek, 2)])
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

function WeekAxis({ x, height, maxWeek }: { x: LinearScale; height: number; maxWeek: number }) {
  return (
    <g className="x-axis">
      {x
        .ticks(Math.min(maxWeek, 9))
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
 * Every visible team's line over the weeks played. Hovering or focusing a team
 * raises its line and mutes the rest.
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
  displayKey = '',
}: ChartProps) {
  const reduced = useReducedMotion();

  // below this the line-end names collide and clip; the team chips are the legend instead
  const labelled = width >= LabelledMinWidth;
  const right = labelled ? Margin.right : UnlabelledRight;

  if (width <= Margin.left + right || series.length === 0) return null;

  // extents
  const maxWeek = max(series, (s) => max(s.points, (p) => p.week)) ?? 1;
  const maxValue = max(series, (s) => max(s.points, (p) => p.value)) ?? 0;
  const minValue = min(series, (s) => min(s.points, (p) => p.value)) ?? 0;

  const x = weekScale(width, right, maxWeek);
  const y = valueScale(minValue, maxValue, height, zeroBased);
  const tickCount = Math.max(2, Math.round((height - Margin.top - Margin.bottom) / 48));

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

  /** Nearest line to the pointer. Vertical distance at the snapped week. */
  function onMove(e: React.PointerEvent<SVGRectElement>) {
    const box = e.currentTarget.getBoundingClientRect();
    const px = e.clientX - box.left + Margin.left;
    const py = e.clientY - box.top + Margin.top;
    const week = Math.min(Math.max(Math.round(x.invert(px)), 1), maxWeek);

    let best: { id: number; d: number } | null = null;
    for (const s of series) {
      const p = s.points.find((q) => q.week === week);
      if (!p) continue;

      const d = Math.abs(y(p.value) - py);
      if (best === null || d < best.d) best = { id: s.rosterId, d };
    }

    onHover(best ? best.id : null);
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

      <WeekAxis x={x} height={height} maxWeek={maxWeek} />

      {/* a metric change gives every line a new key, so the old lines fade out as the new ones draw */}
      <AnimatePresence>
        {ordered.map((s) => (
          <m.path
            key={`${s.rosterId}-${displayKey}`}
            className="series"
            d={path(s.points) ?? undefined}
            stroke={s.colour}
            data-muted={hovered !== null && hovered !== s.rosterId}
            data-raised={hovered === s.rosterId}
            initial={{ opacity: 0, pathLength: reduced ? 1 : 0 }}
            animate={{ opacity: 1, pathLength: 1 }}
            exit={{ opacity: 0 }}
            transition={reduced ? { duration: 0 } : { duration: 0.8, ease: 'easeOut' }}
          />
        ))}
      </AnimatePresence>

      {labelled ? <SeriesLabels series={series} x={x} y={y} hovered={hovered} /> : null}

      {/* one transparent area takes the pointer: a line under 2px wide is too thin to aim at */}
      <rect
        className="hit"
        x={Margin.left}
        y={Margin.top}
        width={width - Margin.left - right}
        height={height - Margin.top - Margin.bottom}
        fill="transparent"
        onPointerMove={onMove}
        onPointerLeave={() => onHover(null)}
      />
    </svg>
  );
}
