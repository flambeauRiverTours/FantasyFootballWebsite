import type { SeasonPayload } from '../api/types';
import Panel from './Panel';

interface ChartPanelProps {
  payload: SeasonPayload;
}

/** A real scroll stop with placeholder content until T37 draws the chart. */
export default function ChartPanel({ payload }: ChartPanelProps) {
  return (
    <Panel id="chart" title="Season Shape">
      <p className="muted">
        {payload.finalThroughWeek} finalized{' '}
        {payload.finalThroughWeek === 1 ? 'week' : 'weeks'}.
      </p>
    </Panel>
  );
}
