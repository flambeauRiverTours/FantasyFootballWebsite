import { isSeasonComplete } from '../api/types';
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
 * A past season shows its final week instead, since it no longer updates, and
 * the current one says it is complete once its last counted week is final.
 * Before there is a payload it holds a placeholder; a failure is the page's to report.
 */
export default function Masthead({ payload, updatedAgo, seasons = [], onSeason }: MastheadProps) {
  const picker = payload && onSeason && seasons.length > 1;
  const status = payload
    ? !payload.isCurrent
      ? `Final · Week ${payload.finalThroughWeek}`
      : isSeasonComplete(payload)
        ? 'Season completed'
        : `Week ${payload.currentWeek} · Updated ${updatedAgo}`
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
