import { useMemo, useState } from 'react';
import type { SeasonPayload, TeamPayload, WeekPayload } from '../api/types';
import type { Measure, Point, PointMode } from '../api/calculations';
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
} from '../api/calculations';
import { teamColours } from '../api/teamColours';
import { useEnumQuery } from '../hooks/useEnumQuery';
import type { Options } from '../hooks/useEnumQuery';
import { useMeasuredWidth } from '../hooks/useMeasuredWidth';
import { useTeamFilter } from '../hooks/useTeamFilter';
import Chart from './Chart';
import type { Series } from './Chart';
import Panel from './Panel';
import SegmentedRow from './SegmentedRow';
import TeamChips from './TeamChips';

interface ChartPanelProps {
  payload: SeasonPayload;
}

/** What the chart shows: a points measure, or sigma, which replaces the modes. */
type Metric = Measure | 'sigma';
type SigmaWindow = 'expanding' | 'rolling';
/** How spread is shown: raw sigma, or sigma over the mean. */
type SpreadScale = 'sigma' | 'cv';

const AspectRatio = 0.5;
const Sigma = 'σ';

// the options for each control, as [value in the URL, label on screen]
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
] as const satisfies readonly (readonly [SpreadScale, string])[];

/** What each mode plots, in words, given what is being counted. */
const ModeWords: Record<PointMode, (what: string) => string> = {
  cumulative: (what) => `Cumulative ${what}`,
  perWeek: (what) => `Weekly ${what}`,
  vsMedian: (what) => `Cumulative ${what} above the league median`,
};

// series building

function toSeries(
  teams: TeamPayload[],
  colours: ReadonlyMap<number, string>,
  points: (weeks: WeekPayload[]) => Point[],
): Series[] {
  return rankByPoints(teams).map((team) => ({
    rosterId: team.rosterId,
    name: team.name,
    colour: colours.get(team.rosterId) ?? 'currentColor',
    points: points(team.weeks),
  }));
}

function pointsFor(
  mode: PointMode,
  measure: Measure,
  weeks: WeekPayload[],
  medians: Map<number, number>,
): Point[] {
  switch (mode) {
    case 'perWeek':
      return perWeek(weeks, measure);
    case 'vsMedian':
      return vsMedian(weeks, medians, measure);
    default:
      return cumulative(weeks, measure);
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
  scale: SpreadScale,
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
  // sigma is built from actual points, so it needs no measure of its own
  const measure: Measure = metric === 'ideal' ? 'ideal' : 'actual';
  const medians = useMemo(() => weeklyMedians(payload.teams, measure), [payload.teams, measure]);
  const started = useMemo(() => startedWeeks(payload.teams), [payload.teams]);
  const colours = teamColours(payload.teams);
  const series = toSeries(payload.teams, colours, (all) => {
    const weeks = all.filter((w) => started.has(w.week));
    return showSigma
      ? sigmaSeries(weeks, sigmaWindow === 'rolling', scale === 'cv')
      : pointsFor(mode, measure, weeks, medians);
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

      {/* several teams at once, in standings order; focusing a chip raises its line */}
      <TeamChips
        name="teams"
        label="Teams"
        teams={series}
        colours={colours}
        isPressed={(id) => shown.has(id)}
        onPress={toggle}
        onFocusTeam={setHovered}
      />

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
