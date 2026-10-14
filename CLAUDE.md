# CLAUDE.md — Ideal Points

A public, read-only page for one Sleeper fantasy league, ranked on **total season points**,
with **consistency σ**, **ideal points** and a **positional radar** beside it. .NET 10 Azure
Functions behind Static Web Apps, blob + Azure SQL for finalized weeks, React/TypeScript/D3
for the page. ≈ $0.25/month; ≈ 4.4 Sleeper requests/min per instance regardless of viewers.

## How Claude works here

Jack owns the design and reviews every change before it is committed. Claude writes tests,
boilerplate, documentation and task updates, and implements features when asked.

- Plan non-trivial work in `tasks/todo.md` and check in before starting.
- Prove it before calling it done: build, tests, `dotnet format`, and the browser where it shows.
- After a correction, add a one-line rule to `tasks/lessons.md`.
- Edit `.github/workflows/` and `infra/` only when asked. Never push or open a PR unless asked.
- Line endings: `.cs` is CRLF with a BOM; docs are CRLF; `.github/**` is LF.
- Branches are `type/short-name`; PR titles are Conventional Commits (squash merge).

## Where things are

| Path | What |
|---|---|
| [`docs/design.md`](docs/design.md) | Design, decisions D1–D11. Corrected in place; **Corrected** marks the differences |
| [`docs/amendments.md`](docs/amendments.md) | A1–A34: the reasoning behind every later decision and correction |
| [`docs/standards.md`](docs/standards.md) | The standards, by domain |
| [`docs/runbook.md`](docs/runbook.md) | Operations, including the yearly season rollover |
| [`docs/field-manual.md`](docs/field-manual.md) | Step-by-step instructions for remaining tasks |
| [`tasks/todo.md`](tasks/todo.md) · [`tasks/lessons.md`](tasks/lessons.md) | Task list · rules learned from corrections |
| [`site/`](site/) | The GitHub Pages write-up (Markdown, built by Jekyll) |

A new decision: the conclusion goes where the thing is stated; the reasoning goes in
`amendments.md` as the next A number. Cite D/A numbers rather than re-arguing them.

## Rules that are never broken

- **A2 · League IDs and Sleeper user IDs never enter the repo or the logs.** Not in code, docs,
  fixtures, workflows, `.bicepparam` or commit messages. Use user-secrets, App Settings, GitHub
  secrets and `@secure()`. Read settings files with values redacted. Placeholders are fine.
- **The league ID is configuration, never a request parameter**, or the API becomes an open
  relay against Sleeper's rate limit.
- **A1 · The dummy team is excluded from everything**, standings and every league-wide
  aggregate. Match on owner `user_id`. Six rosters, five scoring.
- **Every Sleeper call goes through the one typed `HttpClient`** with the singleton token bucket.
- **Core takes no Azure dependency.** DTOs stay in Sleeper, mapped to Core records explicitly.

## Settled decisions (short form)

D1 σ is population, finalized weeks only, null below four · D2 ideal uses that week's roster ·
D4 exact bitmask DP, never greedy · D5 Static Web Apps + Functions · D6 polling at 30 s ·
D8 one configured league, older seasons by walking `previous_league_id` (A34) · D9 React owns
the DOM, D3 is maths only · D10 radar axes are ratios to the league average · D11 one team at
a time · R2 radar axis order is fixed to `roster_positions` · A33 regular season only.

## Sleeper traps

- An empty starter slot is the string `"0"`; defences are team abbreviations (`"DEN"`).
- Fields are absent, not null, for weeks that have not happened.
- Match eligibility on `fantasy_positions`, not `position`.
- The slot list is per-season data (A3): never hardcode slot counts or order; fail loudly on
  an unknown slot code.
- A player with no `players_points` entry scored zero. Sleeper has no projections.
- `starters[i]` aligns with the i-th startable slot, by observation rather than contract.
- Radar averages: average first, then divide, and divide by the number of such slots.
