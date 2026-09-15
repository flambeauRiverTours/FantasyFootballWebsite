import { useEffect, useRef, useState, useMemo } from 'react';
import type { CSSProperties } from 'react';
import type { SeasonPayload, TeamPayload, WeekPayload } from '../api/types';
import Chart from './Chart';
import type { Point, Series } from './Chart';
import Panel from './Panel';
import { useQueryState } from '../hooks/useQueryState';

interface ChartPanelProps {
  payload: SeasonPayload;
}

const AspectRatio = 0.5;

/*
 * Calculates the cumulative season points each week.
 * Deliberately acts on non-final weeks so data updates live-ish
 * @param weeks WeekPayloads to sum
 * @returns Series of point values representing the cumulative season points at each week of the season
 */
function cumulative(weeks: WeekPayload[], useIdeal: boolean = false): Point[] {
  let total = 0;
  return [...weeks] //copy - sort is in-place
    .sort((a, b) => a.week - b.week)
    .map((w) => ({ week: w.week, value: useIdeal ? (total += w.ideal) : (total += w.actual)}));
}

/*
 * Translates team payloads into data series for charts
 * @param teams List of team payloads
 * @param useIdeal whether to use the ideal points to populate the series
 * @returns Series data objects representing team data weekly
 */
function toSeries(teams: TeamPayload[], useIdeal: boolean = false): Series[] {
  return [...teams]
    .sort((a, b) => b.totals.actual - a.totals.actual)
    .map((team, i) => ({
      rosterId: team.rosterId,
      name: team.name,
      colour: `var(--team-${i + 1})`,
      points: cumulative(team.weeks, useIdeal),
    }));
}

/*
 * Determines whether a series is plottable 
 * @param series Series of data to analyze
 * @returns True if the series can be plotted, otherwise false
 */
function isPlottable(series: Series[]): boolean {
  return series.some(
    //(s) => s.points.length >= 2 && s.points.some((p) => p.value !== s.points[0].value),
    (s) => (s.points.length > 1)
  );
}

/*
 * Filters a set of ids down to only those present in valid
 * @param param Ids, in a comma-delimited string
 * @param valid Set of valid ids
 * @returns Ids in the set
 */
function toShown(param: string, valid: number[]): Set<number> {
  const ids = param.split(',').map(Number).filter((id) => valid.includes(id));
  return new Set(ids.length > 0 ? ids : valid);
}

/*
 * Gets the react fragement for the chart panel (wrapper around the chart)
 * @param param0 ChartPanelProps - data for chart and state info
 * @returns react fragement for the chart panel
 */
export default function ChartPanel({ payload }: ChartPanelProps) {
  const wrapper = useRef<HTMLDivElement>(null);
  const [width, setWidth] = useState(0);
  const [hovered, setHovered] = useState<number | null>(null);
  const [rawMetric, setMetric] = useQueryState('metric', 'actual');
  const metric = rawMetric === 'ideal' ? 'ideal' : 'actual';
  //responds to resive events
  useEffect(() => {
    const element = wrapper.current;
    if (!element) return;

    const observer = new ResizeObserver(([entry]) => setWidth(entry.contentRect.width));
    observer.observe(element);
    return () => observer.disconnect();
  }, []);

  const series = toSeries(payload.teams, metric === 'ideal');
  const [teamsParam, setTeamsParam] = useQueryState('teams', '');

  const validIds = useMemo(() => payload.teams.map((t) => t.rosterId), [payload.teams]);
  const shown = useMemo(() => toShown(teamsParam, validIds), [teamsParam, validIds]);

  //Handle team chip toggles. Do not allow all to be deselected
  const toggle = (id: number) => {
    setTeamsParam((prev) => {
      const current = toShown(prev, validIds);
      if (current.has(id) && current.size === 1) return prev;
      const next = new Set(current);
      if (!next.delete(id)) next.add(id);
      return next.size === validIds.length ? '' : [...next].sort((a, b) => a - b).join(',');
    });
  };

  //get the teams to show
  const visible = series.filter((s) => shown.has(s.rosterId));

  return (
    <Panel id="chart" title="Season Movement">
      <div className="chips" role="group" aria-label="Teams shown">
        {series.map((s) => (
          <button key={s.rosterId} type="button" className="chip"
          aria-pressed={shown.has(s.rosterId)} style={{ '--chip': s.colour } as CSSProperties}
          onClick={() => toggle(s.rosterId)}
          onFocus={() => setHovered(s.rosterId)} onBlur={() => setHovered(null)}>
          <span className="swatch" aria-hidden="true" />
          {s.name}
          </button>
        ))}
      </div>
      <div className="segmented" role="group" aria-label="Metric">
        {(['actual', 'ideal'] as const).map((m) => (
          <button key={m} type="button" aria-pressed={metric === m} onClick={() => setMetric(m)}>
          {m === 'actual' ? 'Points' : 'Ideal'}
          </button>
        ))}
      </div>
      <div className="chart-wrap" ref={wrapper}>
        {/* the wrapper always mounts so the ResizeObserver has something to observe */}
        {isPlottable(visible) ? (
          <Chart series={visible} width={width} height={Math.round(width * AspectRatio)} hovered={hovered} onHover={setHovered}/>
        ) : (
          <p className="muted">Point chart will appear here once multiple weeks are final. Stay tuned!</p>
        )}
      </div> 
    </Panel>
  );
}
