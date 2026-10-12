import { scaleLinear } from 'd3-scale';
import type { LinearScale } from './drawing';
import { slotDeviations } from '../api/calculations';

export interface SlotBarsProps {
  slots: string[];
  ratios: (number | null)[];
  width: number;
  height: number;
  label: string;
}

interface Row {
  code: string;
  mid: number;
  deviation: number | null;
}

const MinDomain = 0.1; // a flat week must not divide by a near-zero domain
const Margin = { top: 8, right: 56, bottom: 8, left: 96 };

function computeRowHeight(slots: string[], height: number): number {
  return height / slots.length;
}

/** One row per slot, mid-height for vertical centring. */
function computeRows(slots: string[], rowHeight: number, deviations: (number | null)[]): Row[] {
  return slots.map((code, i) => ({
    code,
    mid: Margin.top + i * rowHeight + rowHeight / 2,
    deviation: deviations[i] ?? null,
  }));
}

function BarRow({
  row,
  x,
  zero,
  rowHeight,
}: {
  row: Row;
  x: LinearScale;
  zero: number;
  rowHeight: number;
}) {
  const { code, mid, deviation } = row;

  return (
    <g className="row">
      <text className="axis-label" x={Margin.left - 40} y={mid} dy="0.32em" textAnchor="end">
        {code}
      </text>

      {deviation === null ? (
        <text className="unmeasured" x={zero + 8} y={mid} dy="0.32em">
          no data
        </text>
      ) : (
        <>
          <rect
            className="bar"
            data-above={deviation >= 0}
            x={Math.min(zero, x(deviation))}
            y={mid - rowHeight / 2 + 4}
            width={Math.abs(x(deviation) - zero)}
            height={rowHeight - 8}
          />
          <text
            className="value"
            x={x(deviation) + (deviation >= 0 ? 6 : -6)}
            y={mid}
            dy="0.32em"
            textAnchor={deviation >= 0 ? 'start' : 'end'}
          >
            {deviation >= 0 ? '+' : ''}
            {Math.round(deviation * 100)}%
          </text>
        </>
      )}
    </g>
  );
}

/**
 * The radar's data as diverging bars: how far each slot sits above or below
 * the league average, on a linear axis that cannot exaggerate (D11).
 */
export default function SlotBars({ slots, ratios, width, label, height }: SlotBarsProps) {
  if (slots.length === 0 || width <= Margin.left + Margin.right) return null;

  const deviations = slotDeviations(ratios);
  const measured = deviations.filter((d): d is number => d !== null);
  const maxAbs = Math.max(MinDomain, ...measured.map(Math.abs));

  const x = scaleLinear()
    .domain([-maxAbs, maxAbs])
    .range([Margin.left, width - Margin.right]);
  const zero = x(0);
  const rowHeight = computeRowHeight(slots, height);
  const rows = computeRows(slots, rowHeight, deviations);

  return (
    <svg
      className="bars"
      viewBox={`0 0 ${width} ${height}`}
      width={width}
      height={height}
      role="img"
      aria-label={label}
    >
      <line className="zero" x1={zero} x2={zero} y1={Margin.top} y2={height - Margin.bottom} />

      {rows.map((row) => (
        <BarRow key={row.code} row={row} x={x} zero={zero} rowHeight={rowHeight} />
      ))}
    </svg>
  );
}
