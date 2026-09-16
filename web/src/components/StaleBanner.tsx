import { useRelativeTime } from '../hooks/useRelativeTime';

interface StaleBannerProps {
  asOfUtc: string;
  onRetry: () => void;
}

/** A payload plus a later failure. Never replaces the page. */
export default function StaleBanner({ asOfUtc, onRetry }: StaleBannerProps) {
  const updated = useRelativeTime(asOfUtc);

  return (
    <div className="banner" role="status">
      <div className="panel-inner">
        <span>Showing the last good data from {updated}.</span>
        <button type="button" className="chip" onClick={onRetry}>
          Try again
        </button>
      </div>
    </div>
  );
}
