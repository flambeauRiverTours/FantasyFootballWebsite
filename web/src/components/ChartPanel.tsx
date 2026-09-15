import { useEffect, useRef, useState } from 'react';
import type { SeasonPayload, TeamPayload, WeekPayload } from '../api/types';
import Chart from './Chart';
import type { Point, Series } from './Chart';
import Panel from './Panel';

interface ChartPanelProps {
  payload: SeasonPayload;
  hovered?: number | null;
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
  return weeks
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
    (s) => s.points.length >= 2 && s.points.some((p) => p.value !== s.points[0].value),
  );
}
/*
 * Gets the react fragement for the chart panel (wrapper around the chart)
 * @param param0 ChartPanelProps - data for chart and state info
 * @returns react fragement for the chart panel
 */
export default function ChartPanel({ payload, hovered = null }: ChartPanelProps) {
  const wrapper = useRef<HTMLDivElement>(null);
  const [width, setWidth] = useState(0);
  const useIdeal = false; //to do - decide where the user would select this and implement
  //responds to resive events
  useEffect(() => {
    const element = wrapper.current;
    if (!element) return;

    const observer = new ResizeObserver(([entry]) => setWidth(entry.contentRect.width));
    observer.observe(element);
    return () => observer.disconnect();
  }, []);

  const series = toSeries(payload.teams, useIdeal);

  return (
    <Panel id="chart" title="Season Movement">
      <div className="chart-wrap" ref={wrapper}>
        { isPlottable(series) ?
        <Chart
          series={series}
          width={width}
          height={Math.round(width * AspectRatio)}
          hovered={hovered}
        /> : <p className="muted">Point chart will appear here once multiple weeks are final. Stay tuned!</p>}
      </div> 
    </Panel>
  );
}
