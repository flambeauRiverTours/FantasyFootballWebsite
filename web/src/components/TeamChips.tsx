import type { CSSProperties } from 'react';

interface TeamChipsProps {
  /** Also the id stem for the label the group points at. */
  name: string;
  label: string;
  /** In display order. */
  teams: readonly { rosterId: number; name: string }[];
  colours: ReadonlyMap<number, string>;
  isPressed: (rosterId: number) => boolean;
  onPress: (rosterId: number) => void;
  /** Called with the team a chip's focus moves to, or null when it leaves. */
  onFocusTeam?: (rosterId: number | null) => void;
}

/**
 * A labelled row of team buttons, each wearing its team's colour. Whether a
 * press selects one team or toggles several is the caller's choice.
 */
export default function TeamChips({
  name,
  label,
  teams,
  colours,
  isPressed,
  onPress,
  onFocusTeam,
}: TeamChipsProps) {
  const labelId = `${name}-label`;

  return (
    <div className="control-row">
      <span className="control-label" id={labelId}>
        {label}
      </span>
      <div className="chips" role="group" aria-labelledby={labelId}>
        {teams.map((t) => (
          <button
            key={t.rosterId}
            type="button"
            className="chip"
            aria-pressed={isPressed(t.rosterId)}
            style={{ '--chip': colours.get(t.rosterId) } as CSSProperties}
            onClick={() => onPress(t.rosterId)}
            onFocus={onFocusTeam ? () => onFocusTeam(t.rosterId) : undefined}
            onBlur={onFocusTeam ? () => onFocusTeam(null) : undefined}
          >
            <span className="swatch" aria-hidden="true" />
            {t.name}
          </button>
        ))}
      </div>
    </div>
  );
}
