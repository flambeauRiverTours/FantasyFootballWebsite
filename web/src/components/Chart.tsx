import { max, min } from 'd3-array';
import { scaleLinear } from 'd3-scale';
import { line } from 'd3-shape';

export interface Point {
  week: number;
  value: number;
}

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
}

const m = { top: 16, right: 84, bottom: 28, left: 44 };

/*
 * Gets the react fragement for the chart 
 * @param param0  ChartProps - holds data and state infoe
 * @returns React fragment for the chart
 */
export default function Chart({ series, width, height, hovered = null, onHover }: ChartProps) {
  if (width <= m.left + m.right || series.length === 0) return null;

  //Compute maxes for axes
  const maxWeek = max(series, (s) => max(s.points, (p) => p.week)) ?? 1;
  const maxValue = max(series, (s) => max(s.points, (p) => p.value)) ?? 0;
  const minValue = min(series, (s) => min(s.points, (p) => p.value)) ?? 0;

  //handles focus on hover over
  function onMove(e: React.PointerEvent<SVGRectElement>) {
    const box = e.currentTarget.getBoundingClientRect();
    const px = e.clientX - box.left + m.left;
    const py = e.clientY - box.top + m.top;

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
  //Comput scale for axes
  const x = scaleLinear()
    .domain([1, Math.max(maxWeek, 2)])
    .range([m.left, width - m.right]);

  const pad = (maxValue - minValue) * 0.08 || maxValue * 0.08 || 1; //padding off the minimum val to preserve
  const y = scaleLinear()
    .domain([Math.max(0, minValue - pad), maxValue + pad])
    .nice()
    .range([height - m.bottom, m.top]);

  //compute paths
  const path = line<Point>()
    .defined((p) => Number.isFinite(p.value))
    .x((p) => x(p.week))
    .y((p) => y(p.value));

  const ordered =
    hovered === null
      ? series
      : [
          ...series.filter((s) => s.rosterId !== hovered),
          ...series.filter((s) => s.rosterId === hovered),
        ];

  //determine number of ticks (numbers on side of y axis) and format
  const tickCount = Math.max(2, Math.round((height - m.top - m.bottom) / 48));
  const tickFormat = y.tickFormat(tickCount);
  return (
    <svg
      className="chart"
      viewBox={`0 0 ${width} ${height}`}
      width={width}
      height={height}
      role="img"
      aria-label={`Cumulative points by week for ${series
        .map((s) => s.name)
        .join(', ')}, weeks 1 to ${maxWeek}`}
    >
      <g className="grid">
        {y.ticks(tickCount).map((t) => (
          <g key={t} className="tick">
            <line x1={m.left} x2={width - m.right} y1={y(t)} y2={y(t)} />
            <text x={m.left - 8} y={y(t)} dy="0.32em" textAnchor="end">
              {tickFormat(t)}
            </text>
          </g>
        ))}
      </g>
      <g className="x-axis">
        {x
          .ticks(Math.min(maxWeek, 9))
          .filter(Number.isInteger)
          .map((t) => (
            <text
              key={t}
              className="tick"
              x={x(t)}
              y={height - m.bottom + 18}
              textAnchor="middle"
            >
              {t}
            </text>
          ))}
      </g>

      {ordered.map((s) => (
        <path
          key={s.rosterId}
          className="series"
          d={path(s.points) ?? undefined}
          stroke={s.colour}
          data-muted={hovered !== null && hovered !== s.rosterId}
          data-raised={hovered === s.rosterId}
        />
      ))}

      {series.map((s) => {
        const last = s.points.at(-1);
        return last ? (
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
        ) : null;
      })}
      <rect className="hit" x={m.left} y={m.top} width={width - m.left - m.right}
        height={height - m.top - m.bottom} fill="transparent" onPointerMove={onMove}
        onPointerLeave={() => onHover(null)}/>
    </svg>
  );
}
