import type { CSSProperties } from 'react';
import { useMemo, useState } from 'react';
import type { SeasonPayload, TeamPayload, WeekPayload } from '../api/types';
import type { Metric, Point } from '../api/pointCalculation';
import {
  cumulative,
  perWeek,
  sigmaSeries,
  vsMedian,
  weeklyMedians,
} from '../api/pointCalculation';
import { useEnumQuery } from '../hooks/useEnumQuery';
import { useMeasuredWidth } from '../hooks/useMeasuredWidth';
import { useTeamFilter } from '../hooks/useTeamFilter';
import Chart from './Chart';
import type { Series } from './Chart';
import Panel from './Panel';
import SegmentedRow from './SegmentedRow';

interface ChartPanelProps {
  payload: SeasonPayload;
}

type Mode = 'cumulative' | 'perWeek' | 'vsMedian';
type SigmaWindow = 'expanding' | 'rolling';
type Scale = 'sigma' | 'cv';

const AspectRatio = 0.5;
const Sigma = 'σ';

// options. first is the default and is dropped from the url
const Metrics = [
  ['actual', 'Points'],
  ['ideal', 'Ideal'],
  ['sigma', Sigma],
] as const satisfies readonly (readonly [Metric, string])[];

const Modes = [
  ['cumulative', 'Total'],
  ['perWeek', 'Per week'],
  ['vsMedian', 'vs median'],
] as const satisfies readonly (readonly [Mode, string])[];

const Windows = [
  ['expanding', 'Expanding'],
  ['rolling', 'Rolling 4'],
] as const satisfies readonly (readonly [SigmaWindow, string])[];

const Scales = [
  ['sigma', Sigma],
  ['cv', 'CV'],
] as const satisfies readonly (readonly [Scale, string])[];

const ModeWords: Record<Mode, string> = {
  cumulative: 'Cumulative',
  perWeek: 'Weekly',
  vsMedian: 'Cumulative points above the league median for',
};

// series building

function toSeries(teams: TeamPayload[], points: (weeks: WeekPayload[]) => Point[]): Series[] {
  return [...teams]
    .sort((a, b) => b.totals.actual - a.totals.actual)
    .map((team, i) => ({
      rosterId: team.rosterId,
      name: team.name,
      colour: `var(--team-${i + 1})`,
      points: points(team.weeks),
    }));
}

function pointsFor(
  mode: Mode,
  metric: Metric,
  weeks: WeekPayload[],
  medians: Map<number, number>,
): Point[] {
  switch (mode) {
    case 'perWeek':
      return perWeek(weeks, metric);
    case 'vsMedian':
      return vsMedian(weeks, medians, metric);
    default:
      return cumulative(weeks, metric);
  }
}

/** One point draws nothing. */
function isPlottable(series: Series[]): boolean {
  return series.some((s) => s.points.length > 1);
}

/** aria spoken description */
function describe(
  metric: Metric,
  mode: Mode,
  sigmaWindow: SigmaWindow,
  scale: Scale,
  names: string,
): string {
  if (metric === 'sigma') {
    const what = scale === 'cv' ? 'Coefficient of variation' : 'Consistency sigma';
    return `${what}, ${sigmaWindow} window, by week for ${names}`;
  }

  return `${ModeWords[mode]} ${metric === 'ideal' ? 'ideal points' : 'points'} by week for ${names}`;
}

export default function ChartPanel({ payload }: ChartPanelProps) {
  // controls
  const [metric, setMetric] = useEnumQuery('metric', Metrics);
  const [mode, setMode] = useEnumQuery('mode', Modes);
  const [sigmaWindow, setWindow] = useEnumQuery('window', Windows);
  const [scale, setScale] = useEnumQuery('scale', Scales);
  const { shown, toggle } = useTeamFilter(payload.teams);

  // chart state
  const [wrapper, width] = useMeasuredWidth<HTMLDivElement>();
  const [hovered, setHovered] = useState<number | null>(null);

  // sigma replaces the modes rather than composing with them
  const showSigma = metric === 'sigma';
  const zeroBased = !showSigma && mode !== 'vsMedian';

  // data
  const medians = useMemo(() => weeklyMedians(payload.teams, metric), [payload.teams, metric]);
  const series = toSeries(payload.teams, (weeks) =>
    showSigma
      ? sigmaSeries(weeks, sigmaWindow === 'rolling', scale === 'cv')
      : pointsFor(mode, metric, weeks, medians),
  );
  const visible = series.filter((s) => shown.has(s.rosterId));

  return (
    <Panel id="chart" title="Season Movement">
      <SegmentedRow
        name="metric"
        label="Metric"
        options={Metrics}
        value={metric}
        onChange={setMetric}
      />

      {showSigma ? (
        <>
          <SegmentedRow
            name="window"
            label="Window"
            options={Windows}
            value={sigmaWindow}
            onChange={setWindow}
          />
          <SegmentedRow
            name="scale"
            label="Scale"
            options={Scales}
            value={scale}
            onChange={setScale}
          />
        </>
      ) : (
        <SegmentedRow name="mode" label="Mode" options={Modes} value={mode} onChange={setMode} />
      )}

      {/* teams */}
      <div className="control-row">
        <span className="control-label" id="teams-label">
          Teams
        </span>
        <div className="chips" role="group" aria-labelledby="teams-label">
          {series.map((s) => (
            <button
              key={s.rosterId}
              type="button"
              className="chip"
              aria-pressed={shown.has(s.rosterId)}
              style={{ '--chip': s.colour } as CSSProperties}
              onClick={() => toggle(s.rosterId)}
              onFocus={() => setHovered(s.rosterId)}
              onBlur={() => setHovered(null)}
            >
              <span className="swatch" aria-hidden="true" />
              {s.name}
            </button>
          ))}
        </div>
      </div>

      {/* chart. the wrapper always mounts so the observer has something to watch */}
      <div className="chart-wrap" ref={wrapper}>
        {isPlottable(visible) ? (
          <Chart
            series={visible}
            width={width}
            height={Math.round(width * AspectRatio)}
            hovered={hovered}
            onHover={setHovered}
            zeroBased={zeroBased}
            label={describe(
              metric,
              mode,
              sigmaWindow,
              scale,
              visible.map((s) => s.name).join(', '),
            )}
            yTitle={showSigma ? (scale === 'cv' ? 'CV' : Sigma) : ''}
          />
        ) : (
          <p className="muted">
            {showSigma
              ? 'Available with four weeks of data. Stay tuned!'
              : 'Point chart will appear here once multiple weeks are final. Stay tuned!'}
          </p>
        )}
      </div>
    </Panel>
  );
}
