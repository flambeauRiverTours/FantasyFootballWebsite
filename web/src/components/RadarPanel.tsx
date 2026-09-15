import type { SeasonPayload } from '../api/types';
import Panel from './Panel';

interface RadarPanelProps {
  payload: SeasonPayload;
}

/**
 * Every axis is a ratio to the league average at that slot, centred on 1.0 (D10),
 * in `slotOrder` order, which is never re-sorted (R2).
 */
export default function RadarPanel({ payload }: RadarPanelProps) {
  return (
    <Panel id="radar" title="Positional Radar">
      <p className="muted">
        {payload.slotOrder.length} axes, one team at a time (D11). Drawn in M6.
      </p>
    </Panel>
  );
}
