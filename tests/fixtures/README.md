# Fixtures

Real Sleeper payloads, captured for offline tests. No network, no emulator,
milliseconds.

| File | What |
|---|---|
| `wk{N}-matchups.json` | `/v1/league/{id}/matchups/{N}` verbatim, weeks 1–5 — 6 rosters, 11 starters, 19 or 20 players each |
| `roster-positions.json` | The `roster_positions` array lifted out of the league object |
| `player-positions.json` | `{ playerId → { name, team, fantasyPositions } }` for the 137 players rostered across every captured week |

`player-positions.json` is a projection of the 5 MB `/v1/players/nfl` dump down to
just the players these rosters actually contain. It exists so the fixture set is
self-contained: the solver needs `fantasyPositions` to run, and no test should
pull five megabytes to get it.

## Captured 2026-10-13: weeks 1–5, all final

Weeks 4 and 5 joined on 2026-10-13; weeks 1–3 were re-fetched unchanged. Weeks
1–3 were first captured on 2026-10-05, after each had finalized, replacing the original
mid-week-1 capture (2026-09-10), in which only the Thursday game had been played
and four of six teams sat on 0.0. That capture made three of the six
`ideal >= actual` checks "0 >= 0"; every roster now has real points in every week.

Three checks run once per `wk*-matchups.json` file they find, so a new capture
is covered without a code change: `ideal >= actual`, which also requires at
least one roster per week with `ideal > actual` (otherwise a solver that just
returned the started lineup would pass); `sum(starters_points) == points`; and
every starter eligible for the slot at its index. `LeagueSocket.Captured()`
also serves these weeks to the season service's ordinary-path tests, so the
whole read path runs over them.

Still to come:

- **A missing `players_points` entry (B4).** Weeks 4 and 5 included byes, and
  Sleeper listed every player on bye with an explicit `0`, not a missing entry.
  The `0m` default for an absent entry is still only tested against hand-built
  data, because Sleeper has not produced the case.

Capture with `League__CurrentLeagueId` set, the name the app and the CLI read.
A week still in progress is not a fixture: delete it after capturing.

2026 is the league's first season (A4), so there is no historical data to
backfill from — these fixtures can only grow as the season does.

## Why no league ID or user IDs

Per **A2** in [`docs/amendments.md`](../../docs/amendments.md), neither enters
this repository.

A `/matchups` payload is safe: its keys are `roster_id`, `matchup_id`,
`players`, `starters`, `points`, `players_points`, `starters_points` and
`custom_points`. The league ID appears only in the request URL, never in the
body.

The full league object **is not safe** — it carries `league_id` — which is why
only the `roster_positions` array was lifted out of it. A capture of
`/league/{id}/users` would carry `user_id` and `display_name` for every manager
and must not be committed at all.

## What this capture established

First confirmed against the live league on 2026-09-10, and re-checked against the three full weeks on 2026-10-05:

- **`starters[i]` is positionally aligned with the *i*th startable slot.**
  191 of 191 non-empty starters across weeks 1–3 were eligible for the slot at their index, zero
  violations. This is the assumption the whole positional radar rests on (§11,
  M6) and it is an observation, not a documented guarantee — the finalize job
  should still assert `starters.Count == startableSlots.Count` each week.
- **Empty starter slots really are the string `"0"`.** Seven across weeks 1–3, three of them in week 1 alone,
  so this is not a theoretical quirk.
- **DEF player IDs are team abbreviations** — `"BAL"`, `"DEN"`, not numeric.
- **`sum(starters_points) == points`** held for every roster in every week, to the
  cent. As doubles it does not: week 1 roster 2 sums to 122.82000000000001, which is why points bind as `decimal`.
- **Every player in `players` had an entry in `players_points`** in all three weeks —
  but treat a missing entry as `0m` anyway, because a bye week is a different
  situation from week 1.
