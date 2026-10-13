import { m } from 'motion/react';
import { scaleLinear } from 'd3-scale';
import { consistencyDomain } from '../api/calculations';
import type { ConsistencyRow } from '../api/calculations';
import { teamColours } from '../api/teamColours';
import type { ConsistentTotals, SeasonPayload } from '../api/types';
import type { LinearScale } from './drawing';
import Panel from './Panel';

interface ConsistencyPanelProps {
  payload: SeasonPayload;
  /** From `consistencyRows`; the page renders the panel only when there are any (D1). */
  rows: ConsistencyRow[];
}

/**
 * Sigma with room to read it (T59): one row per team, steadiest first, each
 * mark centred on its team's mean so widths compare spread, on one scale every
 * row shares (design §04, A33).
 */
export default function ConsistencyPanel({ payload, rows }: ConsistencyPanelProps) {
  const colours = teamColours(payload.teams);
  const scale = scaleLinear().domain(consistencyDomain(rows)).range([0, 100]);

  return (
    <Panel id="consistency" title="Consistency">
      <div className="table-wrap">
        <table className="consistency">
          <thead>
            <tr>
              <th scope="col">Team</th>
              <th scope="col" className="col-mark">
                Weekly range
              </th>
              <th scope="col" aria-sort="ascending">
                σ
              </th>
            </tr>
          </thead>
          <tbody>
            {rows.map(({ team, totals }) => (
              <m.tr
                layout
                key={team.rosterId}
                transition={{ type: 'spring', stiffness: 400, damping: 40 }}
              >
                <td>{team.name}</td>
                <td className="col-mark">
                  <RangeMark
                    name={team.name}
                    totals={totals}
                    scale={scale}
                    colour={colours.get(team.rosterId) ?? 'currentColor'}
                  />
                </td>
                <td className="is-sorted">{totals.sigma.toFixed(1)}</td>
              </m.tr>
            ))}
          </tbody>
        </table>
      </div>
    </Panel>
  );
}

interface RangeMarkProps {
  name: string;
  totals: ConsistentTotals;
  scale: LinearScale;
  colour: string;
}

/**
 * Lowest to highest week as a rule, mean ± σ as a band, the mean as a tick,
 * all measured from the team's own mean so every tick sits at the centre.
 */
function RangeMark({ name, totals, scale, colour }: RangeMarkProps) {
  const { sigma, mean, min, max } = totals;
  const bandStart = scale(-sigma);
  const bandEnd = scale(sigma);

  return (
    // stretched to the column's width; non-scaling strokes keep the lines crisp
    <svg
      viewBox="0 0 100 16"
      preserveAspectRatio="none"
      role="img"
      aria-label={`${name}: lowest week ${min.toFixed(1)}, highest ${max.toFixed(1)}, mean ${mean.toFixed(1)}, σ ${sigma.toFixed(1)}`}
    >
      <line
        x1={scale(min - mean)}
        x2={scale(max - mean)}
        y1="8"
        y2="8"
        stroke="currentColor"
        opacity=".6"
        vectorEffect="non-scaling-stroke"
      />
      {/* a soft fill reads as a band; the solid outline gives it 3:1 contrast in both themes */}
      <rect
        x={bandStart}
        width={bandEnd - bandStart}
        y="4"
        height="8"
        fill={colour}
        fillOpacity=".3"
        stroke={colour}
        vectorEffect="non-scaling-stroke"
      />
      <line
        x1={scale(0)}
        x2={scale(0)}
        y1="1"
        y2="15"
        stroke={colour}
        strokeWidth="2"
        vectorEffect="non-scaling-stroke"
      />
    </svg>
  );
}
