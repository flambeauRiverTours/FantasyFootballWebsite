import '@testing-library/jest-dom/vitest';
import { afterEach, vi } from 'vitest';
import { cleanup } from '@testing-library/react';

// jsdom has no layout engine and so no ResizeObserver. The real one reports a
// size as soon as it observes; a stub that stays silent leaves width at 0 and
// every measured component renders nothing.
vi.stubGlobal(
  'ResizeObserver',
  class {
    callback: ResizeObserverCallback;

    constructor(callback: ResizeObserverCallback) {
      this.callback = callback;
    }

    observe(target: Element) {
      this.callback(
        [{ target, contentRect: { width: 800, height: 400 } } as ResizeObserverEntry],
        this as unknown as ResizeObserver,
      );
    }

    unobserve() {}
    disconnect() {}
  },
);

afterEach(cleanup);
