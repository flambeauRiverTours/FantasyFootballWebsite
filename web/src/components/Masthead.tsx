import type { SeasonPayload } from '../api/types';

export interface MastheadProps {
  payload: SeasonPayload | null;
  updatedAgo: string;
  /** Seasons that can be shown, newest first. The picker appears only with two or more. */
  seasons?: number[];
  onSeason?: (season: number) => void;
}

/**
 * The page header: the league's name, the season, the week and how fresh the data is.
 * A completed season shows its final week instead, since it no longer updates.
 * Before there is a payload it holds a placeholder; a failure is the page's to report.
 */
export default function Masthead({ payload, updatedAgo, seasons = [], onSeason }: MastheadProps) {
  const picker = payload && onSeason && seasons.length > 1;
  const status = payload
    ? payload.isCurrent
      ? `Week ${payload.currentWeek} · Updated ${updatedAgo}`
      : `Final · Week ${payload.finalThroughWeek}`
    : '';

  return (
    <header className="masthead">
      <div className="panel-inner">
        {payload ? (
          <h1>{payload.leagueName}</h1>
        ) : (
          <span className="skeleton-title" aria-hidden="true" />
        )}
        <p className="muted">
          {payload ? (
            <>
              {picker ? (
                <select
                  aria-label="Season"
                  value={payload.season}
                  onChange={(e) => onSeason(Number(e.target.value))}
                >
                  {seasons.map((year) => (
                    <option key={year} value={year}>
                      {year}
                    </option>
                  ))}
                </select>
              ) : (
                <span className="masthead-text">{payload.season}</span>
              )}
              <span className="masthead-text">{` · ${status}`}</span>
            </>
          ) : null}
        </p>
      </div>
    </header>
  );
}
