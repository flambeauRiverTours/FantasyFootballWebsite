import type { CSSProperties } from 'react';
import { m, useReducedMotion } from 'motion/react';

export interface RadarProps {
  slots: string[];
  ratios: (number | null)[];
  size: number;
  label: string;
  /** The shape's colour, so it matches the team's chip. Falls back to the accent. */
  colour?: string;
}

/** One axis: its slot, its direction, and where the team's value lands on it. */
interface Vertex {
  code: string;
  angle: number;
  point: readonly [number, number];
  measured: boolean;
}

// geometry, in a fixed viewBox so nothing clips at any container width
const ViewBox = 460;
const Center = ViewBox / 2;
const R = 90;
const ClampMax = 2;
const LabelR = R * 1.7;

/** -90deg is twelve o'clock; angle increases clockwise (SVG's y axis points down). */
function angleFor(i: number, count: number): number {
  return -Math.PI / 2 + (2 * Math.PI * i) / count;
}

function pointAt(angle: number, radius: number): readonly [number, number] {
  return [Center + radius * Math.cos(angle), Center + radius * Math.sin(angle)] as const;
}

/** One vertex per slot. A negative ratio clamps to the centre, never mirrors. */
function computeVertices(slots: string[], ratios: (number | null)[]): Vertex[] {
  return slots.map((code, i) => {
    const angle = angleFor(i, slots.length);
    const ratio = ratios[i] ?? null;
    const radius = R * Math.max(0, Math.min(ratio ?? 1, ClampMax));
    return { code, angle, point: pointAt(angle, radius), measured: ratio !== null };
  });
}

/** Right of centre reads start, left reads end, top and bottom read middle. */
function anchorFor(cosA: number): 'start' | 'end' | 'middle' {
  if (Math.abs(cosA) < 0.15) return 'middle';
  return cosA > 0 ? 'start' : 'end';
}

// parts

function Spokes({ vertices }: { vertices: Vertex[] }) {
  return (
    <>
      {vertices.map(({ code, angle }) => {
        const [x2, y2] = pointAt(angle, LabelR);
        return <line key={code} className="spoke" x1={Center} y1={Center} x2={x2} y2={y2} />;
      })}
    </>
  );
}

/** An unmeasured axis is pinned to the reference and marked, never plotted as 1.0. */
function UnmeasuredMarkers({ vertices }: { vertices: Vertex[] }) {
  return (
    <>
      {vertices.map((v) =>
        v.measured ? null : (
          <circle key={v.code} className="unmeasured" cx={v.point[0]} cy={v.point[1]} r={4} />
        ),
      )}
    </>
  );
}

function AxisLabels({ vertices }: { vertices: Vertex[] }) {
  return (
    <>
      {vertices.map(({ code, angle }) => {
        const [x, y] = pointAt(angle, LabelR);
        return (
          <text
            key={code}
            className="axis-label"
            x={x}
            y={y}
            dy="0.32em"
            textAnchor={anchorFor(Math.cos(angle))}
          >
            {code}
          </text>
        );
      })}
    </>
  );
}

/**
 * One team's slots as ratios to the league average, with the league as the
 * reference circle (D10). Axis order is the caller's and is never re-sorted (R2).
 */
export default function Radar({ slots, ratios, size, label, colour }: RadarProps) {
  const reduced = useReducedMotion();

  if (slots.length === 0 || size <= 0) return null;

  const vertices = computeVertices(slots, ratios);
  const polygon = vertices.map((v) => v.point.join(',')).join(' ');

  return (
    <svg
      className="radar"
      viewBox={`0 0 ${ViewBox} ${ViewBox}`}
      width={size}
      height={size}
      role="img"
      aria-label={label}
      style={colour ? ({ '--team': colour } as CSSProperties) : undefined}
    >
      {/* spokes, drawn first so the shape sits above them */}
      <Spokes vertices={vertices} />

      {/* the only thing on the drawing with a fixed meaning - never animated */}
      <circle className="reference" cx={Center} cy={Center} r={R} />

      <m.polygon
        className="shape"
        points={polygon}
        style={{ originX: '50%', originY: '50%' }}
        initial={{ scale: reduced ? 1 : 0 }}
        animate={{ scale: 1 }}
        transition={reduced ? { duration: 0 } : { type: 'spring', stiffness: 300, damping: 30 }}
      />

      <UnmeasuredMarkers vertices={vertices} />
      <AxisLabels vertices={vertices} />
    </svg>
  );
}
