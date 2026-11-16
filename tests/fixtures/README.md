# Fixtures

Real Sleeper payloads for offline tests.

| File | Contents |
|---|---|
| `wk{N}-matchups.json` | `/v1/league/{id}/matchups/{N}`, verbatim. Weeks 1–9, all final. 6 rosters, 11 starters, 19–20 players each |
| `roster-positions.json` | `roster_positions`, lifted out of the league object |
| `league-settings.json` | `start_week`, the four `playoff_*` settings, `league_average_match`, and `divisions` when the league has them (T64, T68). This league has playoffs disabled (`playoff_week_start: 0`) |
| `rosters-record.json` | Each roster's number and Sleeper's own W-L-T and points for and against, nothing else (T68) |
| `previous-season.json` | The same settings and records for the season before, when the league links one (T68). None yet: 2026 is the league's first season |
| `player-positions.json` | `{ playerId → { name, team, fantasyPositions } }` for the 155 players rostered in any captured week |

`player-positions.json` projects the 14.65 MB `/v1/players/nfl` dump down to the rostered players, so
the solver runs without the dump.

## Capture

```bash
export League__CurrentLeagueId=...    # the name the app and CLI read (A2)
python scripts/capture_fixtures.py    # every week up to the current one
```

Delete the week still in progress afterwards: a fixture is a final week. Captured weeks: 1–3 on
2026-10-05, 4–5 on 2026-10-13, 1–9 again on 2026-11-15 (week 5 picked up one stat correction).
2026 is the league's first season (A4); fixtures grow with it.

## Tests that read every captured week

Each runs once per `wk*-matchups.json`, so a new capture needs no code change.

- `ideal >= actual` for every roster, and `ideal > actual` for at least one per week. Without
  the second, a solver returning the started lineup passes.
- `sum(starters_points) == points` for every roster, to the cent.
- Our head-to-head records equal Sleeper's in `rosters-record.json`, for every roster (A39).
- Every starter is eligible for the slot at its index.

`LeagueSocket.Captured()` serves the same weeks to the season service's ordinary-path tests.

## Not yet observed

- **A missing `players_points` entry (B4).** Weeks 4–5 had byes; Sleeper listed each player on
  bye with an explicit `0`. The `0m` default for an absent entry is tested on invented data only.

## Excluded from capture (A2)

- `/matchups` is safe: its keys are `roster_id`, `matchup_id`, `players`, `starters`, `points`,
  `players_points`, `starters_points`, `custom_points`. The league ID is in the URL, not the body.
- The league object carries `league_id` and `previous_league_id`; only `roster_positions` and named settings are kept.
- `/league/{id}/rosters` carries `owner_id` and `co_owners`; only `roster_id` and named record settings are kept.
- `/league/{id}/users` carries `user_id` and `display_name`; it is never captured.

## Established by the captures

- `starters[i]` belongs to the *i*th startable slot: every non-empty starter in weeks 1–9 is
  eligible for its slot. The radar (§11) depends on this. It is observed, not documented.
- An empty starter slot is the string `"0"`. Seven occur in weeks 1–3.
- A defence's player ID is its team abbreviation: `"BAL"`, `"DEN"`.
- `sum(starters_points) == points` holds to the cent as `decimal`. As `double` it does not: week 1
  roster 2 sums to 122.82000000000001.
- Every player in `players` has a `players_points` entry in weeks 1–9.
