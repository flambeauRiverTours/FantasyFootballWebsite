export interface RadarProps {
  slots: string[];
  ratios: (number | null)[];
  size: number;
  label: string;
}

interface Vertex {
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
  return slots.map((_, i) => {
    const angle = angleFor(i, slots.length);
    const ratio = ratios[i];
    const measured = ratio !== null;
    const radius = R * Math.max(0, Math.min(measured ? ratio : 1, ClampMax));
    return { angle, point: pointAt(angle, radius), measured };
  });
}

/** Right of centre reads start, left reads end, top and bottom read middle. */
function anchorFor(cosA: number): 'start' | 'end' | 'middle' {
  if (Math.abs(cosA) < 0.15) return 'middle';
  return cosA > 0 ? 'start' : 'end';
}

// parts

function Spokes({ slots, vertices }: { slots: string[]; vertices: Vertex[] }) {
  return (
    <>
      {slots.map((code, i) => {
        const [x2, y2] = pointAt(vertices[i].angle, LabelR);
        return <line key={code} className="spoke" x1={Center} y1={Center} x2={x2} y2={y2} />;
      })}
    </>
  );
}

/** An unmeasured axis is pinned to the reference and marked, never plotted as 1.0. */
function UnmeasuredMarkers({ slots, vertices }: { slots: string[]; vertices: Vertex[] }) {
  return (
    <>
      {vertices.map((v, i) =>
        v.measured ? null : (
          <circle key={slots[i]} className="unmeasured" cx={v.point[0]} cy={v.point[1]} r={4} />
        ),
      )}
    </>
  );
}

function AxisLabels({ slots, vertices }: { slots: string[]; vertices: Vertex[] }) {
  return (
    <>
      {slots.map((code, i) => {
        const [x, y] = pointAt(vertices[i].angle, LabelR);
        return (
          <text
            key={code}
            className="axis-label"
            x={x}
            y={y}
            dy="0.32em"
            textAnchor={anchorFor(Math.cos(vertices[i].angle))}
          >
            {code}
          </text>
        );
      })}
    </>
  );
}

/* Radar control */
export default function Radar({ slots, ratios, size, label }: RadarProps) {
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
    >
      {/* spokes, drawn first so the shape sits above them */}
      <Spokes slots={slots} vertices={vertices} />

      {/* the only thing on the drawing with a fixed meaning */}
      <circle className="reference" cx={Center} cy={Center} r={R} />

      <polygon className="shape" points={polygon} />

      <UnmeasuredMarkers slots={slots} vertices={vertices} />
      <AxisLabels slots={slots} vertices={vertices} />
    </svg>
  );
}
