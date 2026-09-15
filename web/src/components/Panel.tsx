import type { ReactNode } from 'react';

interface PanelProps {
  id: string; //orders the panels
  title: string;
  children: ReactNode;
}

/**
 * The shell for every panel
 */
export default function Panel({ id, title, children }: PanelProps) {
  const headingId = `${id}-h`;
  return (
    <section className="panel" id={id} aria-labelledby={headingId}>
      <div className="panel-inner">
        <h2 id={headingId}>{title}</h2>
        {children}
      </div>
    </section>
  );
}
