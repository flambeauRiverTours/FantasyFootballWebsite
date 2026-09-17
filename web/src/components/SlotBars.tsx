import { scaleLinear } from 'd3-scale';
import type { ScaleLinear } from 'd3-scale';

export interface SlotBarsProps {
  slots: string[];
  ratios: (number | null)[];
  width: number;
  label: string;
}

interface Row {
  code: string;
  mid: number;
  deviation: number | null;
}

type Scale = ScaleLinear<number, number>;

const RowHeight = 28;
const MinDomain = 0.1; // a flat week must not divide by a near-zero domain
const m = { top: 8, right: 56, bottom: 8, left: 96 };

/** One row per slot, mid-height for vertical centring. */
function computeRows(slots: string[], deviations: (number | null)[]): Row[] {
  return slots.map((code, i) => ({
    code,
    mid: m.top + i * RowHeight + RowHeight / 2,
    deviation: deviations[i],
  }));
}

function BarRow({ row, x, zero }: { row: Row; x: Scale; zero: number }) {
  const { code, mid, deviation: d } = row;

  return (
    <g className="row">
      <text className="axis-label" x={m.left - 8} y={mid} dy="0.32em" textAnchor="end">
        {code}
      </text>

      {d === null ? (
        <text className="unmeasured" x={zero + 8} y={mid} dy="0.32em">
          no data
        </text>
      ) : (
        <>
          <rect
            className="bar"
            data-above={d >= 0}
            x={Math.min(zero, x(d))}
            y={mid - RowHeight / 2 + 4}
            width={Math.abs(x(d) - zero)}
            height={RowHeight - 8}
          />
          <text
            className="value"
            x={x(d) + (d >= 0 ? 6 : -6)}
            y={mid}
            dy="0.32em"
            textAnchor={d >= 0 ? 'start' : 'end'}
          >
            {d >= 0 ? '+' : ''}
            {Math.round(d * 100)}%
          </text>
        </>
      )}
    </g>
  );
}

/* SlotBars control. Same data as Radar, on a linear axis */
export default function SlotBars({ slots, ratios, width, label }: SlotBarsProps) {
  if (slots.length === 0 || width <= m.left + m.right) return null;

  const deviations = ratios.map((r) => (r === null ? null : r - 1));
  const measured = deviations.filter((d): d is number => d !== null);
  const maxAbs = Math.max(MinDomain, ...measured.map(Math.abs));
  const height = slots.length * RowHeight + m.top + m.bottom;

  const x = scaleLinear().domain([-maxAbs, maxAbs]).range([m.left, width - m.right]);
  const zero = x(0);
  const rows = computeRows(slots, deviations);

  return (
    <svg
      className="bars"
      viewBox={`0 0 ${width} ${height}`}
      width={width}
      height={height}
      role="img"
      aria-label={label}
    >
      <line className="zero" x1={zero} x2={zero} y1={m.top} y2={height - m.bottom} />

      {rows.map((row) => (
        <BarRow key={row.code} row={row} x={x} zero={zero} />
      ))}
    </svg>
  );
}
