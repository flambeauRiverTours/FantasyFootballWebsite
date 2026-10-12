import type { SeasonPayload } from '../api/types';

export interface MastheadProps {
  payload: SeasonPayload | null;
  updatedAgo: string;
}

/**
 * The page header: the league's name, the week, and how fresh the data is.
 * Before there is a payload it holds a placeholder; a failure is the page's to report.
 */
export default function Masthead({ payload, updatedAgo }: MastheadProps) {
  const text = payload
    ? `${payload.season} · Week ${payload.currentWeek} · Updated ${updatedAgo}`
    : '';
  return (
    <header className="masthead">
      <div className="panel-inner">
        {payload ? (
          <h1>{payload.leagueName}</h1>
        ) : (
          <span className="skeleton-title" aria-hidden="true" />
        )}
        <p className="muted">{text}</p>
      </div>
    </header>
  );
}
