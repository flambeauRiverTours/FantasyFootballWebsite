import { ApiError } from '../api/client';
import Panel from './Panel';

interface LoadFailedProps {
  error: Error | null;
  onRetry: () => void;
}

/**
 * The page when the first request fails, laid out like any other panel. A 404
 * is navigation, not failure: it lists the seasons that do exist.
 */
export default function LoadFailed({ error, onRetry }: LoadFailedProps) {
  const missing = error instanceof ApiError && error.status === 404;

  return (
    <main className="panels">
      {missing ? (
        <Panel id="loaderror" title="No data for that season">
          <AvailableSeasons years={error.available ?? []} />
        </Panel>
      ) : (
        <Panel id="loaderror" title="Could not load the season">
          <div role="alert">
            <p className="muted">{error?.message ?? 'The request did not complete.'}</p>
            <button type="button" className="chip" onClick={onRetry}>
              Try again
            </button>
          </div>
        </Panel>
      )}
    </main>
  );
}

/** Links to every season the API says it has. */
function AvailableSeasons({ years }: { years: number[] }) {
  if (years.length === 0) return <p className="muted">No seasons have been backfilled yet.</p>;

  return (
    <p>
      Seasons with data:{' '}
      {years.map((year, i) => (
        <span key={year}>
          {i > 0 ? ' · ' : ''}
          <a href={`?season=${year}`}>{year}</a>
        </span>
      ))}
    </p>
  );
}
