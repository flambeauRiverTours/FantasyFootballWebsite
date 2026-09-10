# Fixtures

Real Sleeper payloads, captured for offline tests. No network, no emulator,
milliseconds.

| File | What |
|---|---|
| `wk1-matchups.json` | `/v1/league/{id}/matchups/1` verbatim — 6 rosters, 11 starters and 19 players each |
| `roster-positions.json` | The `roster_positions` array lifted out of the league object |
| `player-positions.json` | `{ playerId → { name, team, fantasyPositions } }` for the 114 rostered players |

`player-positions.json` is a projection of the 5 MB `/v1/players/nfl` dump down to
just the players these rosters actually contain. It exists so the fixture set is
self-contained: the solver needs `fantasyPositions` to run, and no test should
pull five megabytes to get it.

## Captured 2026-09-10, mid-week-1

**This is a partial week.** Only the Thursday game had been played when these
were taken — four of six teams were still on 0.0. The shapes are real and
complete; the numbers are not final.

**Re-capture after week 1 finalizes** (Tuesday), and add weeks 2 and 3 as they
complete. Until then:

- Structural tests can run now — slot parsing, eligibility, `"0"` handling, the
  DEF-abbreviation case, `sum(starters_points) == points`.
- `ideal >= actual` can run now and is meaningful.
- Anything needing σ cannot: σ is defined over finalized weeks and returns null
  below four of them (D1). That is four weeks away.

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

Confirmed against the live league on 2026-09-10:

- **`starters[i]` is positionally aligned with the *i*th startable slot.**
  63 of 63 non-empty starters were eligible for the slot at their index, zero
  violations. This is the assumption the whole positional radar rests on (§11,
  M6) and it is an observation, not a documented guarantee — the finalize job
  should still assert `starters.Count == startableSlots.Count` each week.
- **Empty starter slots really are the string `"0"`.** Three of them across two
  rosters in this one week, so this is not a theoretical quirk.
- **DEF player IDs are team abbreviations** — `"BAL"`, `"DEN"`, not numeric.
- **`sum(starters_points) == points`** held for all six rosters, including the
  four sitting on zero.
- **Every player in `players` had an entry in `players_points`** this week —
  but treat a missing entry as `0m` anyway, because a bye week is a different
  situation from week 1.
