import type { CSSProperties } from 'react';
import { useMemo, useState } from 'react';
import type { SeasonPayload, TeamPayload, WeekPayload } from '../api/types';
import type { Metric, Point, PointMode } from '../api/pointCalculation';
import {
  canPlotMedian,
  canPlotSigma,
  cumulative,
  hasMovement,
  latestExtremes,
  perWeek,
  rankByPoints,
  sigmaSeries,
  startedWeeks,
  vsMedian,
  weeklyMedians,
} from '../api/pointCalculation';
import { useEnumQuery } from '../hooks/useEnumQuery';
import type { Options } from '../hooks/useEnumQuery';
import { useMeasuredWidth } from '../hooks/useMeasuredWidth';
import { useTeamFilter } from '../hooks/useTeamFilter';
import Chart from './Chart';
import type { Series } from './Chart';
import Panel from './Panel';
import SegmentedRow from './SegmentedRow';

interface ChartPanelProps {
  payload: SeasonPayload;
}

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
] as const satisfies readonly (readonly [PointMode, string])[];

const Windows = [
  ['expanding', 'Expanding'],
  ['rolling', 'Rolling 4'],
] as const satisfies readonly (readonly [SigmaWindow, string])[];

const Scales = [
  ['sigma', Sigma],
  ['cv', 'CV'],
] as const satisfies readonly (readonly [Scale, string])[];

/** What each mode plots, in words, given what is being counted. */
const ModeWords: Record<PointMode, (what: string) => string> = {
  cumulative: (what) => `Cumulative ${what}`,
  perWeek: (what) => `Weekly ${what}`,
  vsMedian: (what) => `Cumulative ${what} above the league median`,
};

// series building

function toSeries(teams: TeamPayload[], points: (weeks: WeekPayload[]) => Point[]): Series[] {
  return rankByPoints(teams).map((team, i) => ({
    rosterId: team.rosterId,
    name: team.name,
    colour: `var(--team-${i + 1})`,
    points: points(team.weeks),
  }));
}

function pointsFor(
  mode: PointMode,
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

/** True when at least one line has something to show. */
function isPlottable(series: Series[]): boolean {
  return series.some((s) => hasMovement(s.points));
}

/**
 * The chart in words for a screen reader: what is plotted, for whom, and the
 * highest and lowest values at the latest week.
 */
function describe(
  metric: Metric,
  mode: PointMode,
  sigmaWindow: SigmaWindow,
  scale: Scale,
  visible: Series[],
): string {
  const names = visible.map((s) => s.name).join(', ');
  const what =
    metric === 'sigma'
      ? `${scale === 'cv' ? 'Coefficient of variation' : 'Consistency sigma'}, ${sigmaWindow} window, by week for ${names}.`
      : `${ModeWords[mode](metric === 'ideal' ? 'ideal points' : 'points')} by week for ${names}.`;

  const extremes = latestExtremes(visible);
  if (extremes === null) return what;

  const digits = metric === 'sigma' && scale === 'cv' ? 2 : 1;
  const { week, high, low } = extremes;
  return `${what} At week ${week}, highest ${high.name} at ${high.value.toFixed(digits)}, lowest ${low.name} at ${low.value.toFixed(digits)}.`;
}

export default function ChartPanel({ payload }: ChartPanelProps) {
  // options that need data the season doesn't have yet are hidden; the first is always available
  const hasSigma = canPlotSigma(payload.finalThroughWeek);
  const hasMedian = canPlotMedian(payload.finalThroughWeek);
  const availableMetrics: Options<Metric> = [
    Metrics[0],
    ...Metrics.slice(1).filter(([key]) => key !== 'sigma' || hasSigma),
  ];
  const availableModes: Options<PointMode> = [
    Modes[0],
    ...Modes.slice(1).filter(([key]) => key !== 'vsMedian' || hasMedian),
  ];

  // controls
  const [metric, setMetric] = useEnumQuery('metric', availableMetrics);
  const [mode, setMode] = useEnumQuery('mode', availableModes);
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
  const started = useMemo(() => startedWeeks(payload.teams), [payload.teams]);
  const series = toSeries(payload.teams, (all) => {
    const weeks = all.filter((w) => started.has(w.week));
    return showSigma
      ? sigmaSeries(weeks, sigmaWindow === 'rolling', scale === 'cv')
      : pointsFor(mode, metric, weeks, medians);
  });
  const visible = series.filter((s) => shown.has(s.rosterId));

  return (
    <Panel id="chart" title="Season Movement">
      <SegmentedRow
        name="metric"
        label="Metric"
        options={availableMetrics}
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
        <SegmentedRow
          name="mode"
          label="Mode"
          options={availableModes}
          value={mode}
          onChange={setMode}
        />
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
            displayKey={showSigma ? `sigma-${sigmaWindow}-${scale}` : `${mode}-${metric}`}
            label={describe(metric, mode, sigmaWindow, scale, visible)}
            yTitle={showSigma ? (scale === 'cv' ? 'CV' : Sigma) : ''}
          />
        ) : (
          <p className="muted">
            {showSigma
              ? 'Available with five weeks of data. Stay tuned!'
              : 'Point chart will appear here once multiple weeks are final. Stay tuned!'}
          </p>
        )}
      </div>
    </Panel>
  );
}
