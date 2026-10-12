import { flushSync } from 'react-dom';
import { useReducedMotion } from 'motion/react';
import { slotRatios } from '../api/calculations';
import { teamColours } from '../api/teamColours';
import type { SeasonPayload, TeamPayload } from '../api/types';
import { useEnumQuery } from '../hooks/useEnumQuery';
import { useMeasuredWidth } from '../hooks/useMeasuredWidth';
import { useQueryState } from '../hooks/useQueryState';
import Panel from './Panel';
import Radar from './Radar';
import SegmentedRow from './SegmentedRow';
import TeamChips from './TeamChips';
import SlotBars from './SlotBars';

interface RadarPanelProps {
  payload: SeasonPayload;
}

type View = 'radar' | 'bars';

const Views = [
  ['radar', 'Radar'],
  ['bars', 'Bars'],
] as const satisfies readonly (readonly [View, string])[];

const MaxRadarSize = 420;

/** The drawing in words for a screen reader. Names the axes, never the shape (R2). */
function describe(name: string, slots: string[], ratios: (number | null)[]): string {
  const above = slots.filter((_, i) => (ratios[i] ?? 1) > 1);
  const below = slots.filter((_, i) => (ratios[i] ?? 1) < 1);
  const parts = [`${name}'s positional radar, axes at the league average.`];
  if (above.length) parts.push(`Above average: ${above.join(', ')}.`);
  if (below.length) parts.push(`Below average: ${below.join(', ')}.`);
  return parts.join(' ');
}

// parts

interface DrawingProps {
  team: TeamPayload | undefined;
  colour: string | undefined;
  view: View;
  slots: string[];
  ratios: (number | null)[];
  width: number;
  label: string;
}

function Drawing({ team, colour, view, slots, ratios, width, label }: DrawingProps) {
  if (!team) return <p className="muted">No teams to plot.</p>;

  return view === 'bars' ? (
    <SlotBars
      slots={slots}
      ratios={ratios}
      width={width}
      label={label}
      height={Math.min(width, MaxRadarSize)}
    />
  ) : (
    <Radar
      slots={slots}
      ratios={ratios}
      size={Math.min(width, MaxRadarSize)}
      label={label}
      colour={colour}
    />
  );
}

/**
 * Every axis is a ratio to the league average at that slot, centred on 1.0 (D10),
 * in `slotOrder` order, which is never re-sorted (R2). Never on screen with the
 * table or the chart, so every control it needs lives inside it.
 */
export default function RadarPanel({ payload }: RadarPanelProps) {
  // controls
  const [view, setViewRaw] = useEnumQuery('view', Views);
  const [rawTeam, setTeam] = useQueryState('team', '');

  // crossfade between the two drawings, unless motion is off or the browser cannot
  const reduced = useReducedMotion();
  function setView(next: View) {
    if (reduced || !document.startViewTransition) return setViewRaw(next);
    document.startViewTransition(() => flushSync(() => setViewRaw(next)));
  }

  // chart state
  const [wrapper, width] = useMeasuredWidth<HTMLDivElement>();

  // data. teams arrive ranked by points (§09); the leader is the default
  const teamId = payload.teams.some((t) => String(t.rosterId) === rawTeam)
    ? Number(rawTeam)
    : (payload.teams[0]?.rosterId ?? null);
  const colours = teamColours(payload.teams);
  const team = payload.teams.find((t) => t.rosterId === teamId);
  const colour = teamId === null ? undefined : colours.get(teamId);
  const ratios = slotRatios(payload, teamId);
  const label = team ? describe(team.name, payload.slotOrder, ratios) : '';
  const selectTeam = (rosterId: number) =>
    setTeam(rosterId === payload.teams[0]?.rosterId ? '' : String(rosterId));

  return (
    <Panel id="radar" title="Positional Radar">
      {/* one team at a time: pressing selects rather than toggles */}
      <TeamChips
        name="radar-team"
        label="Team"
        teams={payload.teams}
        colours={colours}
        isPressed={(id) => id === teamId}
        onPress={selectTeam}
      />
      <SegmentedRow name="view" label="View" options={Views} value={view} onChange={setView} />

      {/* the wrapper always mounts so the observer has something to watch */}
      <div className="radar-wrap" ref={wrapper}>
        <Drawing
          team={team}
          colour={colour}
          view={view}
          slots={payload.slotOrder}
          ratios={ratios}
          width={width}
          label={label}
        />
      </div>
    </Panel>
  );
}
