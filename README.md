# Ideal Points

A live standings board for one Sleeper fantasy league, scoring the season on
**total points** — beside how steadily those points arrived (σ) and how many were
left on the bench (ideal).

One public, read-only page for one league: a standings table and a line chart,
both updating live during games, plus a positional radar showing how each team
does at each of its own starting slots against the league. No auth, no writes,
no head-to-head views.

| | |
|---|---|
| Data source | Sleeper read API (unauthenticated, free) |
| Stack | .NET 10 · Azure Functions · Azure SQL · Vite/React/TypeScript/D3 |
| Hosting | Static Web Apps (Free) + Functions (Consumption) |
| Steady-state cost | $0.00 / month |
| Sleeper request budget | ≈ 4.4 req/min per instance, regardless of viewer count |
| Status | M0-M4 built and deployed. M5 in progress, M6 (radar) next |

## The three metrics

| Column | Definition | Shape | Better |
|---|---|---|---|
| **Points** | Sum of `matchup.points` across all weeks played | Running total | Higher |
| **Consistency σ** | Population standard deviation of *finalized* weekly scores | Scalar, recomputed weekly | **Lower** |
| **Ideal** | Sum of the maximum obtainable from each week's full roster | Running total | Higher |

Only **Points** decides the league. σ and Ideal are commentary — which is why
they can be approximate where the actual column cannot.

## Docs

| File | What it is |
|---|---|
| [`docs/design.md`](docs/design.md) | Design document v6 — 15 sections, 11 decisions (D1–D11), 2 risks (R1–R2), 8 open questions (Q1–Q8). The *why* behind every choice. |
| [`docs/field-manual.md`](docs/field-manual.md) | Step-by-step implementation instructions for all 55 tasks, in build order, with code and reference links. |
| [`docs/amendments.md`](docs/amendments.md) | Decisions made after v6 and the answers to Q1–Q8. Survives an artifact re-sync; where it disagrees with the design, it wins. |
| [`tasks/todo.md`](tasks/todo.md) | The task list — 50 tasks over 7 milestones, 108 h estimated, plus 5 optional. |
| [`docs/runbook.md`](docs/runbook.md) | Operating procedures. The August rollover, the cost checks, what to do when something breaks. |
| [`tasks/lessons.md`](tasks/lessons.md) | Rules written after corrections. |
| [`CLAUDE.md`](CLAUDE.md) | How Claude works in this repo. |

Published (live, shareable) versions of the first three:

- [Design document](https://claude.ai/code/artifact/6191a761-1035-4684-947a-1f2fb063c645)
- [Build plan / tracker](https://claude.ai/code/artifact/30817425-417f-460d-aedc-cfb5b91e2e65)
- [Field manual](https://claude.ai/code/artifact/bae9d64b-1fe8-4f4a-a47c-c733adc901d7)

## Layout

Three names, one project: the repository is **FantasyFootballWebsite**, the
solution is **FantasyLeague**, and the product is **Ideal Points**.

`src/`, `tests/`, `Directory.Build.props` and `FantasyLeague.slnx` exist as of
T01. The rest is planned.

```
src/
  FantasyLeague.Core/          ideal-lineup solver, consistency maths — no Azure deps
  FantasyLeague.Sleeper/       typed client, rate-limit handler, DTOs
  FantasyLeague.Data/          EF Core context, migrations
  FantasyLeague.Functions/     HTTP + Timer triggers, DI wiring
web/                           Vite · React · TypeScript · D3
tests/
  FantasyLeague.Core.Tests/    solver fixtures + σ edge cases
infra/
  main.bicep                   SWA, Function App, SQL, App Insights
.github/workflows/
  ci.yml                       build · test · dotnet format · vite build
  deploy.yml                   bicep what-if → deploy → functions → SWA
```

`Core` having no Azure dependency is the point of the layout, not an aesthetic
preference: every interesting bug lives in the solver or in σ's edge cases, and
both are pure functions over fixtures — testable offline in milliseconds.

## Two invariants

Assert these in production, not just in tests. They are free, and between them
they catch nearly every way the pipeline can be wrong:

1. `sum(starters_points) == points` for every roster, within a cent.
2. `ideal >= actual` for every team-week, always. The started lineup is one of
   the assignments the solver considers, so a violation is a solver bug —
   almost always a slot eligibility rule.

## Build order

| | Milestone | Hours | Ends when |
|---|---|---|---|
| M0 | Core domain | 13 | An hour of curl settles Q1–Q4 and Q7, then the solver beats greedy on a flex case, handles a bye-week hole, and σ returns null below four weeks |
| M1 | Sleeper client | 16 | A console app prints the real league's standings and asserts both invariants across a season |
| M2 | API and cloud foundation | 18.5 | `/api/season` live and correct, deployed by bicep over OIDC, no database yet |
| M3 | Persistence | 17 | SQL, migrations, finalize timer, player refresh, season-chain walk, backfill |
| M4 | The page | 24 | Table then chart — the first milestone anyone else in the league can look at |
| M5 | Modes, ops and docs | 9.5 | Per-week / vs-median / CV toggles, cost guardrails, runbook |
| M6 | Positional radar | 10 | Slot attribution, league reference, radar and the diverging-bar alternative |
| M7 | Motion and polish | +7.5 | **Optional.** Cut it entirely and the site still does its job |

**T02 goes first for a reason.** An hour of `curl` against the real league —
before a line of domain code — so the eligibility map, the fixtures and the
radar are all built against what Sleeper actually returns.

> **Thirty hours in, the problem is solved.** M0 and M1 together are 29 hours and
> end with a console app that prints the real table for the real league.
> Everything after that — seventy-nine hours of it — buys a live website rather
> than a better answer. Worth knowing before starting, in case a scheduled job
> posting a table to the league chat turns out to be the actual requirement.

## Getting started

Prerequisites:

```bash
dotnet --version    # 10.x
node --version      # 24+
az --version
func --version      # Azure Functions Core Tools v4
```

### The front end on its own

The dev server proxies `/api` to the deployed Function App, so **no local
backend is needed** and there is nothing to configure:

```bash
cd web
npm ci
npm run dev
```

`npm test` runs the Vitest suite. `npm run lint` runs oxlint. Both run in CI on
every pull request, along with `tsc --noEmit` and a check that the built bundle
references `/assets/` rather than source.

To point the proxy somewhere else, set `VITE_DEV_PROXY_TARGET`. It is
deliberately not called `VITE_API_ORIGIN`: anything prefixed `VITE_` is inlined
into the bundle, and a browser given an absolute origin would bypass the proxy
and meet CORS.

### The API locally

Needs the league ID in Secret Manager and Azurite for the timer triggers.

Find the league ID by opening the league on the Sleeper web app — the URL is
`sleeper.com/leagues/<league_id>/…` and the numeric segment is what every API
call needs. It is configuration (`League:CurrentLeagueId`), never a request
parameter.

**The league ID and any Sleeper user ID are secrets and never enter this
repository** — see [A2](docs/amendments.md). Locally they live in .NET Secret
Manager:

```bash
dotnet user-secrets set "League:CurrentLeagueId" "<your-league-id>"
```

Run the manual's shell blocks in **Git Bash**: they are POSIX scripts, and T02
needs `curl` and `jq`. In PowerShell, `curl` is an alias for
`Invoke-WebRequest` and takes different arguments.
