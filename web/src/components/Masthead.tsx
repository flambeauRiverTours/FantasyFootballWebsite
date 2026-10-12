import type { SeasonPayload } from '../api/types';

export interface MastheadProps {
  payload: SeasonPayload | null;
  updatedAgo: string;
  error: boolean;
}

/** The page header: the league's name, the week, and how fresh the data is. */
export default function Masthead({ payload, updatedAgo, error }: MastheadProps) {
  const text = payload
    ? `${payload.season} · Week ${payload.currentWeek} · Updated ${updatedAgo}`
    : error
      ? 'Error loading...'
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
