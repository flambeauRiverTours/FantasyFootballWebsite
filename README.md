# Ideal Points

Live standings for one Sleeper fantasy league, ranked on total points, with two
commentary measures: consistency (σ of finalized weekly scores) and ideal points
(the best legal lineup from each week's roster). Public, read-only, one league.

**Live:** <https://ff.jacktreadwell.com>

| Measure | Definition | Better |
|---|---|---|
| Points | Σ weekly points, live week included; decides the league | Higher |
| σ | Population standard deviation of finalized weeks; null below 4 (D1) | Lower |
| Ideal | Σ of the exact best lineup per week (D4) | Higher |

## Architecture

```
Sleeper API ─▶ Functions (.NET 10, Flex Consumption) ─▶ Static Web App (React, D3 for maths)
                  │  rate-limited client, 15 s cache
                  ├─ finalized weeks: memory → blob → Azure SQL (serverless, free offer)
                  └─ telemetry: App Insights, 0.16 GB/day cap
```

| Path | Contents |
|---|---|
| `src/FantasyLeague.Core` | Solver, σ, slot averages. No Azure dependency |
| `src/FantasyLeague.Sleeper` | Typed client, rate limit, DTOs, mapping |
| `src/FantasyLeague.Seasons` | Season assembly, payload, ETag |
| `src/FantasyLeague.Data` | SQL and blob stores, migrations |
| `src/FantasyLeague.Functions` | HTTP and timer triggers |
| `src/FantasyLeague.Cli` | Console standings, for checking against live data |
| `web/` | Vite, React, TypeScript |
| `infra/` | Bicep: `main.bicep` per deploy, `budget.bicep` once |

Cost ≈ $0.25/month. Sleeper load ≈ 4.4 req/min per instance, independent of viewers.

## Run locally

```bash
cd web && npm ci && npm run dev    # proxies /api to the deployed API; no backend needed
```

API: set `League__CurrentLeagueId` in `src/FantasyLeague.Functions/local.settings.json`
(git-ignored), start Azurite, then `func start` in that folder. The league ID and Sleeper
user IDs never enter the repository (A2).

Tests: `dotnet test` and `npm test --prefix web`.

## Documents

[Design](docs/design.md) · [Amendments](docs/amendments.md) · [Standards](docs/standards.md) ·
[Runbook](docs/runbook.md) · [Field manual](docs/field-manual.md) · [Tasks](tasks/todo.md)
