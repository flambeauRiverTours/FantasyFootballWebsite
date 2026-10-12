import { ApiError } from '../api/client';
import Panel from './Panel';
interface LoadFailedProps {
  error: Error | null;
  onRetry: () => void;
}

/** No payload at all. A 404 is navigation rather than failure. */
export default function LoadFailed({ error, onRetry }: LoadFailedProps) {
  const missing = error instanceof ApiError && error.status === 404;
  const available = missing ? (error.available ?? []) : [];

  if (missing) {
    return (
      <main className="panels">
        <Panel id="loaderror" title="No data for that season">
          {available.length > 0 ? (
            <p>
              Seasons with data:{' '}
              {available.map((year, i) => (
                <span key={year}>
                  {i > 0 ? ' · ' : ''}
                  <a href={`?season=${year}`}>{year}</a>
                </span>
              ))}
            </p>
          ) : (
            <p className="muted">No seasons have been backfilled yet.</p>
          )}
        </Panel>
      </main>
    );
  }

  return (
    <div className="panel-inner" role="alert">
      <h2>Could not load the season</h2>
      <p className="muted">{error?.message ?? 'The request did not complete.'}</p>
      <button type="button" className="chip" onClick={onRetry}>
        Try again
      </button>
    </div>
  );
}
