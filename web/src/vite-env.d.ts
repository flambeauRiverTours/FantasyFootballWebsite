/// <reference types="vite/client" />

interface ImportMetaEnv {
  /** Origin the browser calls for the API. Empty in development; the dev proxy handles it. */
  readonly VITE_API_ORIGIN?: string;
}

interface ImportMeta {
  readonly env: ImportMetaEnv;
}
