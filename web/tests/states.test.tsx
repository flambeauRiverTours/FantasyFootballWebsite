import { describe, expect, it, vi } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import { ApiError } from '../src/api/client';
import Skeleton from '../src/components/Skeleton';
import LoadFailed from '../src/components/LoadFailed';
import StaleBanner from '../src/components/StaleBanner';
import Masthead from '../src/components/Masthead';
import StandingsPanel from '../src/components/StandingsPanel';
import { teams } from './fixture';

describe('Skeleton', () => {
  it('announces itself as busy rather than reading empty rows', () => {
    const { container } = render(<Skeleton />);
    expect(container.querySelector('.table-wrap[aria-busy="true"]')).not.toBeNull();
  });

  it('occupies the space the real table will', () => {
    const { container } = render(<Skeleton />);
    expect(container.querySelectorAll('thead th')).toHaveLength(4);
    expect(container.querySelectorAll('tbody tr')).toHaveLength(5);
    expect(container.querySelector('.table-wrap > table')).not.toBeNull();
  });

  it("has the real table's columns, so nothing moves when the data arrives", () => {
    const header = (c: HTMLElement) =>
      [...c.querySelectorAll('thead th')].map((th) => `${th.textContent}|${th.className}`);

    const skeleton = render(<Skeleton />);
    const loading = header(skeleton.container);
    skeleton.unmount();

    const real = render(<StandingsPanel teams={teams} />);
    expect(loading).toEqual(header(real.container));
  });

  it('only mentions the database once the wait is long', () => {
    const { rerender } = render(<Skeleton />);
    expect(screen.queryByText(/database/i)).toBeNull();

    rerender(<Skeleton coldStart />);
    expect(screen.getByRole('heading', { level: 2 })).toHaveTextContent(/waking the database/i);
  });

  it('keeps the heading to one line so the table never moves', () => {
    const { rerender, container } = render(<Skeleton />);
    const loading = container.querySelector('h2')!.textContent!;

    rerender(<Skeleton coldStart />);
    const cold = container.querySelector('h2')!.textContent!;

    //both are short enough to stay on one line at 375px
    expect(loading.length).toBeLessThan(28);
    expect(cold.length).toBeLessThan(28);
  });
});

describe('LoadFailed', () => {
  it('treats a 404 as navigation and never says error', () => {
    const notBackfilled = new ApiError(404, 'GET /api/season?season=1999 returned 404', [2026]);
    const { container } = render(<LoadFailed error={notBackfilled} onRetry={() => {}} />);

    expect(screen.getByRole('link', { name: '2026' })).toHaveAttribute('href', '?season=2026');
    expect(container.textContent).not.toMatch(/error/i);
    expect(container.querySelector('[role="alert"]')).toBeNull();
  });

  it('explains a 404 with no seasons at all', () => {
    const empty = new ApiError(404, 'no seasons', []);
    render(<LoadFailed error={empty} onRetry={() => {}} />);
    expect(screen.getByText(/backfilled/i)).toBeInTheDocument();
    expect(screen.queryByRole('link')).toBeNull();
  });

  it('offers a retry for a real failure instead of telling anyone to reload', () => {
    const onRetry = vi.fn();
    render(<LoadFailed error={new ApiError(503, 'unavailable')} onRetry={onRetry} />);

    const region = screen.getByRole('alert');
    within(region)
      .getByRole('button', { name: /try again/i })
      .click();
    expect(onRetry).toHaveBeenCalledOnce();
    expect(screen.queryByText(/reload/i)).toBeNull();
  });

  it('lays a failure out as a panel, like every other state of the page', () => {
    const { container } = render(<LoadFailed error={new ApiError(503, 'x')} onRetry={() => {}} />);
    const panel = container.querySelector('main > section.panel');
    expect(panel).not.toBeNull();
    expect(within(panel as HTMLElement).getByRole('heading', { level: 2 })).toHaveTextContent(
      'Could not load the season',
    );
  });

  it('leaves the masthead quiet, so the failure is stated once', () => {
    const { container } = render(<Masthead payload={null} updatedAgo="" />);
    expect(container.querySelector('.masthead p')).toHaveTextContent('');
  });

  it('survives a null error', () => {
    render(<LoadFailed error={null} onRetry={() => {}} />);
    expect(screen.getByRole('alert')).toBeInTheDocument();
  });
});

describe('StaleBanner', () => {
  it('is a status rather than an alert so it does not steal focus', () => {
    render(<StaleBanner asOfUtc={new Date().toISOString()} onRetry={() => {}} />);
    expect(screen.getByRole('status')).toBeInTheDocument();
    expect(screen.queryByRole('alert')).toBeNull();
  });

  it('says when the data it is showing came from', () => {
    const anHourAgo = new Date(Date.now() - 3_600_000).toISOString();
    render(<StaleBanner asOfUtc={anHourAgo} onRetry={() => {}} />);
    expect(screen.getByRole('status')).toHaveTextContent(/hour ago/i);
  });

  it('retries in place', () => {
    const onRetry = vi.fn();
    render(<StaleBanner asOfUtc={new Date().toISOString()} onRetry={onRetry} />);
    screen.getByRole('button', { name: /try again/i }).click();
    expect(onRetry).toHaveBeenCalledOnce();
  });
});
