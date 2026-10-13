# Web

The page: React 19, TypeScript, Vite. D3 is used for maths only; React owns the DOM (D9).

| Command                | Effect                                            |
| ---------------------- | ------------------------------------------------- |
| `npm run dev`          | Dev server. `/api` is proxied to the deployed API |
| `npm test`             | Vitest, jsdom, Testing Library, axe               |
| `npm run lint`         | oxlint                                            |
| `npm run format:check` | Prettier                                          |
| `npm run build`        | `tsc -b`, then the bundle in `dist/`              |

| Variable                | Read by        | Purpose                                             |
| ----------------------- | -------------- | --------------------------------------------------- |
| `VITE_API_ORIGIN`       | the build      | API origin baked into the bundle. Set by the deploy |
| `VITE_DEV_PROXY_TARGET` | the dev server | Proxy target. Default: the deployed API             |

The proxy does not reuse `VITE_API_ORIGIN`: that value is inlined into the bundle, so the browser
would call the absolute origin, bypass the proxy and fail CORS.

CI limits initial JavaScript to 125 KB gzipped (standards, domain 2).
