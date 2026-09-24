import type { CSSProperties } from 'react';
import { flushSync } from 'react-dom';
import type { SeasonPayload, TeamPayload } from '../api/types';
import { useEnumQuery } from '../hooks/useEnumQuery';
import { useMeasuredWidth } from '../hooks/useMeasuredWidth';
import { useQueryState } from '../hooks/useQueryState';
import Panel from './Panel';
import Radar from './Radar';
import SegmentedRow from './SegmentedRow';
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

// ratios

/** !league catches both the missing key and T47's zero-denominator guard. */
function ratiosFor(payload: SeasonPayload, teamId: number | null): (number | null)[] {
  const team = payload.teams.find((t) => t.rosterId === teamId);
  return payload.slotOrder.map((code) => {
    const league = payload.leagueSlotAverages[code];
    const value = team?.slotAverages[code];
    return value === undefined || !league ? null : value / league;
  });
}

/** aria spoken description. Names the axes, never the shape (R2). */
function describe(name: string, slots: string[], ratios: (number | null)[]): string {
  const above = slots.filter((_, i) => ratios[i] !== null && ratios[i]! > 1);
  const below = slots.filter((_, i) => ratios[i] !== null && ratios[i]! < 1);
  const parts = [`${name}'s positional radar, axes at the league average.`];
  if (above.length) parts.push(`Above average: ${above.join(', ')}.`);
  if (below.length) parts.push(`Below average: ${below.join(', ')}.`);
  return parts.join(' ');
}

// parts

interface TeamChipsProps {
  teams: TeamPayload[];
  teamId: number | null;
  onSelect: (rosterId: number) => void;
}

/** Single select, unlike ChartPanel's team filter: clicking sets rather than toggles. */
function TeamChips({ teams, teamId, onSelect }: TeamChipsProps) {
  return (
    <div className="control-row">
      <span className="control-label" id="radar-team-label">
        Team
      </span>
      <div className="chips" role="group" aria-labelledby="radar-team-label">
        {teams.map((t, i) => (
          <button
            key={t.rosterId}
            type="button"
            className="chip"
            aria-pressed={t.rosterId === teamId}
            style={{ '--chip': `var(--team-${i + 1})` } as CSSProperties}
            onClick={() => onSelect(t.rosterId)}
          >
            <span className="swatch" aria-hidden="true" />
            {t.name}
          </button>
        ))}
      </div>
    </div>
  );
}

interface DrawingProps {
  team: TeamPayload | undefined;
  view: View;
  slots: string[];
  ratios: (number | null)[];
  width: number;
  label: string;
}

function Drawing({ team, view, slots, ratios, width, label }: DrawingProps) {
  if (!team) return <p className="muted">No teams to plot.</p>;

  return view === 'bars' ? (
    <SlotBars slots={slots} ratios={ratios} width={width} label={label} height={Math.min(width, MaxRadarSize)} />
  ) : (
    <Radar slots={slots} ratios={ratios} size={Math.min(width, MaxRadarSize)} label={label} />
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

  // Do flush sync to handle a cross fade
  function setView(next: View) {
    if (!document.startViewTransition) return setViewRaw(next);
    document.startViewTransition(() => flushSync(() => setViewRaw(next)));
  }

  // chart state
  const [wrapper, width] = useMeasuredWidth<HTMLDivElement>();

  // data. teams arrive ranked by points (§09); the leader is the default
  const teamId = payload.teams.some((t) => String(t.rosterId) === rawTeam)
    ? Number(rawTeam)
    : (payload.teams[0]?.rosterId ?? null);
  const team = payload.teams.find((t) => t.rosterId === teamId);
  const ratios = ratiosFor(payload, teamId);
  const label = team ? describe(team.name, payload.slotOrder, ratios) : '';
  const selectTeam = (rosterId: number) =>
    setTeam(rosterId === payload.teams[0]?.rosterId ? '' : String(rosterId));

  return (
    <Panel id="radar" title="Positional Radar">
      <TeamChips teams={payload.teams} teamId={teamId} onSelect={selectTeam} />
      <SegmentedRow name="view" label="View" options={Views} value={view} onChange={setView} />

      {/* the wrapper always mounts so the observer has something to watch */}
      <div className="radar-wrap" ref={wrapper}>
        <Drawing team={team} view={view} slots={payload.slotOrder} ratios={ratios} width={width} label={label} />
      </div>
    </Panel>
  );
}
