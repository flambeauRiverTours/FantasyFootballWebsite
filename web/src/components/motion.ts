import { domAnimation } from 'motion/react';

/** How a table row slides to its new place when a poll changes the order (§10), by position only: a scale would squash its text. */
export const RowSlide = { type: 'spring', stiffness: 400, damping: 40 } as const;

/**
 * The layout feature the row slide needs, fetched after first paint for the tables alone:
 * `domMax` up front would break the 125 KB initial budget (T74), and a table's first order
 * needs no slide. Wrap only the rows: until it loads, an `m` element inside has no renderer.
 * A failed load falls back to `domAnimation`; the rows then just jump.
 */
export const loadLayout = () =>
  import('./layoutFeatures').then((module) => module.default).catch(() => domAnimation);
