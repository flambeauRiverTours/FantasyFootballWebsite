import type { ScaleLinear } from 'd3-scale';
import type { ConsistentTotals } from '../api/types';

interface SigmaDisplayProps {
  /** Narrowed by `hasConsistency` at the call site, so no figure here is null. */
  totals: ConsistentTotals;
  /** The league-wide scale, shared by every row so the marks are comparable. */
  scaleFunction: ScaleLinear<number, number>;
}

/**
 * The weekly range as a shape: a rule from min to max, a band one sigma either
 * side of the mean, and a tick at the mean.
 */
export default function SigmaDisplay({ totals, scaleFunction }: SigmaDisplayProps) {
  const { sigma, mean, min, max } = totals;
  const bandStart = scaleFunction(mean - sigma);

  return (
    <svg
      viewBox="0 0 140 16"
      width="140"
      height="16"
      role="img"
      aria-label={`${min.toFixed(1)} to ${max.toFixed(1)}, mean ${mean.toFixed(1)}, sigma ${sigma.toFixed(1)}`}
    >
      <line
        x1={scaleFunction(min)}
        x2={scaleFunction(max)}
        y1="8"
        y2="8"
        stroke="currentColor"
        opacity=".35"
      />
      <rect
        x={bandStart}
        width={scaleFunction(mean + sigma) - bandStart}
        y="4"
        height="8"
        fill="var(--accent)"
        opacity=".22"
      />
      <line
        x1={scaleFunction(mean)}
        x2={scaleFunction(mean)}
        y1="2"
        y2="14"
        stroke="var(--accent)"
        strokeWidth="2"
      />
    </svg>
  );
}
