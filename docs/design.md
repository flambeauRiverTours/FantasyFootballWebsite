# Ideal Points — Design Document (v6)

> Originally converted from the published artifact:
> https://claude.ai/code/artifact/6191a761-1035-4684-947a-1f2fb063c645
>
> **Corrected in place on 2026-09-13 against what was actually built.** This file
> is now edited directly rather than re-synced; a re-sync would reinstate the
> errors marked **Corrected** below. The reasoning behind each change is in
> [`amendments.md`](amendments.md), keyed A1–A12.
>
> 11 decisions · 1 risk · 8 open questions, 5 of them now answered.

---

A live standings board for one Sleeper fantasy league, scoring the season on total points — beside how steadily those points arrived and how many were left on the bench.

- Data source · Sleeper API
- Stack · .NET 9 · Azure
- Steady-state cost · $0.00 / mo
- Sleeper budget · ≈ 12.2 req/min as built, against a 50/min bucket — see §07
- Status · **M0–M2 built and deployed.** M3 (persistence) is next
- League · **six rosters, five of them scoring** — one is excluded (A1)

## 01 · Scope

One public, read-only page for one league. A standings table and a line chart, both updating live during games.

The league is decided on **total season points** — the sum of every week's actual score, and nothing else. Consistency and ideal points sit beside it as commentary: consistency says whether those points arrived steadily or in two monster weeks, ideal says how many never left the bench. Neither decides anything, which is why they can be approximate where the actual column cannot.

Out of scope: authentication, writes of any kind, head-to-head or playoff views, trades, and anything requiring a Sleeper login. The site reads a league ID from configuration and nothing else.

> **Changed in v2 · projections removed**
>
> Sleeper's API does not expose fantasy projections. The projected-points column is cut, and a third of this design went with it: the scoring engine that turned raw stat lines into points, the `PlayerWeek` table that stored those stat lines for replay, and the lock-time snapshot job that froze projections before kickoff are all gone. Actual points arrive pre-computed in `matchup.players_points`, so nothing in this design multiplies a stat line by `scoring_settings` any more.
>

## 02 · The three metrics

Two of the three are season-to-date sums. The third is not, and the table should say so rather than imply a symmetry that does not exist.

| Column | Definition | Shape | Better |
|---|---|---|---|
| **Points** | Sum of `matchup.points` across all weeks played. | Running total | Higher |
| **Consistency σ** | Population standard deviation of finalized weekly scores. | Scalar, recomputed weekly | Lower |
| **Ideal** | Sum of the maximum obtainable from each week's full roster. | Running total | Higher |

Note that σ inverts: it is the one column where a smaller number is the better result. Do not let it inherit the visual treatment of the two columns around it — a shared bar-length or heat encoding across all three would read exactly backwards for σ.

Worth being clear-eyed about what σ is for. Under a total-points format, variance has *no effect whatsoever* on who wins — only the sum matters, and a season of 190/62 finishes level with a season of 126/126. Consistency is therefore descriptive colour, not a second scoreboard, and the page should present it that way. It answers "how did you get here", not "are you winning".

### D1 — Population σ over finalized weeks only, undefined below four

Chosen: divide by *n*, not *n*−1; include only weeks marked final; return null rather than a number when fewer than four weeks qualify.

**Population, not sample.** The weeks played are the complete population of interest, not a sample drawn from some larger set of hypothetical weeks. Bessel's correction answers a question nobody is asking here, and it inflates early-season σ when the count is smallest and the number is already least trustworthy.

**Null below four weeks.** σ over two data points is arithmetic, not information — it is entirely determined by the gap between them. Rendering it as a number invites the league to read meaning into noise, and rendering it as `0.0` in week one is actively false. The column shows an em dash until week four.

**Finalized weeks only.** See R1 — this is the constraint most likely to be got wrong.

> **Risk R1 · in-progress weeks poison σ**
>
> A partial week is merely incomplete for the two sum columns — a team's total is simply lower than it will be. For σ it is actively wrong. A team sitting on 41 points at 2pm Sunday with seven players still to play reads as a catastrophic outlier, and σ will jump by tens of points and then fall back over the afternoon. The standings would appear to churn violently while nothing was actually happening.
>
> Mitigation: σ is computed strictly over `isFinal` weeks and the in-progress week is excluded until Tuesday's finalize job runs. The consequence is a visible one and should be surfaced rather than hidden — the column header carries the week range it covers, so a reader can see that σ is "through week 6" while the points column already includes week 7.
>

### D2 — Ideal uses the roster as it stood that week

Chosen: solve against `matchup.players` for that week, not the roster's current contents.

Sleeper's matchup payload carries the roster as of that week. Solving against today's roster would silently rewrite history after every waiver claim — a player dropped in week 4 would stop counting toward the week 2 ideal. Weeks are finalized and frozen for the same reason.

## 03 · Sleeper data

Sleeper's read API is unauthenticated and free. Every field this design needs is in the documented `/v1` surface — there are no undocumented dependencies left.

| Endpoint | Gives | Cache |
|---|---|---|
| /v1/state/nfl | Current season, week, season type | 5 min |
| /v1/league/{id} | `roster_positions` — the startable slots | 24 h |
| /v1/league/{id}/users | `metadata.team_name`, `display_name` | 1 h |
| /v1/league/{id}/rosters | `roster_id` → `owner_id` | 1 h |
| /v1/league/{id}/matchups/{wk} | `points`, `starters`, `players`, `players_points` | 30 s |
| /v1/players/nfl | **14.65 MB** dump — `fantasy_positions` for the solver | 24 h |

Team name resolves as `users[].metadata.team_name` falling back to `display_name`. **Corrected (A6): an unset team name is an absent key, not an empty string** — `metadata` itself may be absent, and treating absence as `""` produced blank rows rather than the fallback.

`scoring_settings` is no longer read. It was needed only to convert raw stat lines into fantasy points, and with projections gone, every point value this site displays comes from Sleeper already scored.

`players/nfl` is still required, and it is the one heavy call. **Corrected: it is 14.65 MB across 12,227 players, not ≈5 MB** — measured 2026-09-13. Fetch it once a day on a timer, project it down to `{ playerId → { name, team, fantasyPositions } }`, and cache that. It must never be on a request path.

The projection keeps 11,902 of those players — everything with a non-empty `fantasy_positions` — and discards every other field, giving a 17× saving in bytes by dropping *fields*, not rows. **Corrected: "cache that" was not sufficient.** The map is per-instance memory, and Flex Consumption scales to zero and out to forty, so it is persisted to blob storage and adopted by each cold instance (A12). An in-memory cache alone meant every deployed request returned 500.

#### Two free invariants

With no scoring engine there is nothing to unit-test against ground truth, but the payload is self-checking in two ways worth asserting in production, not just in tests:

- `sum(starters_points) == points` for every roster, within a cent. Catches a misaligned `starters` array and confirms you are reading the payload the way Sleeper writes it.
- `ideal >= actual` for every team-week. **Corrected (A7, A9): not "always, with no exceptions".** `actual` is `custom_points ?? points`, and a commissioner override can be any value at all, so both invariants compare against Sleeper's own scored `points` instead. Against a scored total the reasoning holds: the started lineup is by definition one of the candidate assignments the solver considers, so a violation is a solver bug — most likely a slot eligibility rule. The visible consequence is that after a generous override the page can show an ideal below the actual, which is correct rather than a bug.

Both invariants are collected and reported, never thrown — as are players the map cannot explain (A8). Dropping an unknown player silently would lower that team's ideal, and `ideal >= actual` cannot see undercounting.

## 04 · Consistency

Population standard deviation over the finalized weekly scores:

```csharp
static double? Sigma(IReadOnlyList<decimal> finalWeeks, int minWeeks = 4)
{
    if (finalWeeks.Count < minWeeks) return null;      // null, never 0
    var mean = (double)finalWeeks.Average();
    var variance = finalWeeks
        .Sum(p => Math.Pow((double)p - mean, 2)) / finalWeeks.Count;
    return Math.Sqrt(variance);
}
```

The nullable return is load-bearing. A method returning `0.0` for a two-week season would silently produce a table where the newest, least-known teams appear to be the most consistent — the worst possible reading of the data. Making absence a type the caller must handle forces the em dash into the UI.

### Plotting a scalar week to week

σ is a single season-to-date number, so it has no natural weekly value to plot. The chart needs an explicit choice, and there are two reasonable ones:

| Series | Value at week *w* | Reads as |
|---|---|---|
| **Expanding σ** (default) | σ over weeks 1…*w* | Converges as the season settles — matches the season-to-date semantics of the other two lines |
| **Rolling σ** | σ over weeks *w*−3…*w* | Whether a team is becoming steadier or wilder right now; noisier, needs four weeks of runway |

Default to expanding. Rolling is the more interesting series in the back half of a season but it is close to meaningless before week eight, and a chart that is empty or jagged on first load teaches the reader to distrust it.

### D3 — Raw σ in the table, coefficient of variation as a chart option

Chosen: display σ in points; offer CV = σ ÷ mean as a chart toggle.

σ is scale-dependent, which makes it quietly unfair across teams with different scoring levels: a team averaging 140 with σ = 25 is *relatively* steadier than one averaging 90 with σ = 22, but the raw numbers say the opposite. CV is the statistically correct comparison and it is one division away.

It is also less intuitive. "I swing about twenty-five points a week" is a sentence a league-mate will say out loud; "my coefficient of variation is 0.18" is not. The table gets the number people will actually discuss, and the chart carries the one that is technically right for anyone who wants it.

### Showing σ in a table cell

A bare standard deviation is hard to interpret at a glance even for readers who know what it is. Pair the figure with a small inline range mark — a horizontal rule spanning the team's minimum to maximum weekly score, with a tick at the mean and a shaded band at ±σ, drawn on a scale shared by every row. The number stays for precision; the mark makes every row comparable without reading any of them.

It costs nothing extra: minimum, maximum and mean come from the same weekly array σ is computed over.

## 05 · Ideal lineup

Given a set of rostered players with points and a list of starting slots, find the assignment maximising total points. Slots come from `league.roster_positions` with `BN`, `IR` and `TAXI` removed; eligibility comes from each player's `fantasy_positions`.

This is now the only non-trivial computation in the project, and the only place a real bug can hide.

### D4 — Exact assignment, not greedy

Chosen: bitmask dynamic programming over the set of filled slots.

Greedy — sort by points, place each player in the best open slot — is wrong whenever flex slots exist. Placing the highest-scoring RB into `FLEX` can strand a second RB with no home, producing a total below the true maximum. Since the metric exists precisely to say "here is what you left on the bench", an approximation that undercounts is worse than useless.

The problem is small enough that exactness is free: ≤ 11 startable slots and ≈ 20 rostered players give 2^11 states and roughly 450k operations per team-week. Min-cost flow or Hungarian would also be exact; the DP is twenty lines and needs no dependency.

```csharp
// slots: startable slots, after dropping BN/IR/TAXI
// dp[mask] = best total using exactly the slots in mask
var dp = new decimal[1 << slots.Count];
Array.Fill(dp, -1m); dp[0] = 0m;

foreach (var p in players)                      // each player used at most once
{
    var next = (decimal[])dp.Clone();
    for (var mask = 0; mask < dp.Length; mask++)
    {
        if (dp[mask] < 0) continue;
        for (var s = 0; s < slots.Count; s++)
        {
            if ((mask & (1 << s)) != 0) continue;
            if (!slots[s].Accepts(p.FantasyPositions)) continue;
            var m2 = mask | (1 << s);
            if (dp[mask] + p.Points > next[m2]) next[m2] = dp[mask] + p.Points;
        }
    }
    dp = next;
}
var ideal = dp.Max();   // partial fills allowed: a bye week can leave a slot empty
```

`dp.Max()` rather than `dp[full]` matters: on a bye-heavy week a roster may genuinely be unable to fill every slot, and taking the maximum over all reachable states handles that without a special case.

#### Slot eligibility map

| Slot | Accepts |
|---|---|
| QB / RB / WR / TE / K / DEF | Exact position match |
| FLEX | RB, WR, TE |
| SUPER_FLEX | QB, RB, WR, TE |
| REC_FLEX | WR, TE |
| WRRB_FLEX | RB, WR |
| IDP_FLEX | DL, LB, DB |

Store the solved lineup alongside the number. The first time someone disputes their ideal score you will want to show them the exact eleven players rather than re-derive it under pressure.

Test it with pinned JSON fixtures from two or three real weeks — offline, no network, milliseconds — plus the `ideal >= actual` invariant from §03 asserted on every computation in production. Include at least one fixture with a bye-week hole and one where the greedy answer differs from the optimal, or the DP is untested against the exact case that motivated it.

## 06 · Architecture

### D5 — Static Web Apps + Azure Functions, not a single container app

Chosen: SWA Free for the SPA, Functions Consumption (.NET 9 isolated) for the API and scheduled jobs.

> **Corrected · the plan is Flex Consumption (FC1), not Consumption (Y1).**
> Y1 could not be provisioned at all — `SubscriptionIsOverQuotaForSku`, current
> limit for Y1 VMs is zero. FC1 scales to zero the same way, has its own free
> monthly grant, and the deployed app uses `instanceMemoryMB: 512` with
> `maximumInstanceCount: 40`, which is the platform minimum. Everything the
> decision argues for still holds; the SKU name in it does not.

Scheduling still decides it, though less emphatically than in v1. Weeks must be finalized after Monday night and the player dump refreshed daily — Timer triggers, which Functions gives for free and a Container App does not. Consumption also scales to zero without configuration, and SWA Free includes a custom domain with managed TLS, which App Service Free does not.

Cost of the split: CORS configuration and two deploy targets. A linked backend removes the CORS step but needs SWA Standard at $9/month — most of this project's budget for a config convenience.

> **Diagram** (see the published artifact for the rendered SVG)
> Viewer count never reaches Sleeper. Finalized weeks are served from SQL, and the current week sits behind a single-flight cache, so one viewer and a thousand viewers generate the same handful of requests per minute. (The cache TTL as built is 5 s, not 30 s — see §07.)

### D6 — Polling, not websockets

Chosen: client polls every 30 s, gated on tab visibility and on being inside an NFL game window.

The data cannot change faster than the 30-second server cache, so a push channel would deliver the same information on the same cadence while adding a SignalR resource, a connection lifecycle and a reconnect path. Polling is also trivially correct across sleep, network changes and mobile backgrounding.

## 07 · Request budget

Steady state during a Sunday afternoon with the site under load. The ceiling is not a real constraint once reads are funnelled through the server.

**Corrected: the TTLs as built differ from the ones designed here**, and the
headline is 12.2 req/min rather than 2.3. Both columns are shown; the open
question of which is right is **F4** in the task list.

| Call | TTL designed | TTL built | Req/min built |
|---|---|---|---|
| matchups/{current week} | 30 s | **5 s** | 12.0 |
| state/nfl | 5 min | **1 h** | 0.02 |
| users + rosters | 1 h | **5 min** | 0.20 |
| league/{id} | 24 h | 24 h | <0.01 |
| players/nfl (14.65 MB) | 24 h | 24 h | <0.01 |
| weeks 1…n−1 | SQL, permanent | not built yet (M3) | — |
| prior seasons | SQL, immutable | not built yet (M3) | — |
| Peak steady state | ≈ 2.3 | | **≈ 12.2** |

Still far inside the 50/min bucket, so nothing breaks. Two of the three are worth
revisiting: matchups at 5 s buys 25 seconds of worst-case staleness for six times
the traffic when the *client* only polls every 30 s (D6), and state at 1 h saves
0.18 req/min while making the one value that gates `isFinal` up to an hour stale
across Tuesday's rollover.

Two mechanisms hold it there, and the second is the one that matters:

- **Single-flight caching.** Concurrent requests for the same uncached key await one in-flight fetch rather than each issuing their own. Without it, a cache expiry under load produces a thundering herd — the classic way a 2/min design becomes a 200/min incident.
- **A token bucket in the HTTP pipeline.** A `DelegatingHandler` on the Sleeper `HttpClient`, set to 50/min, shared by the HTTP and Timer triggers. This is the structural guarantee: a retry-loop bug or a bad cache key cannot exceed the limit, because nothing reaches Sleeper except through the handler.

Pair it with a Polly policy that treats `429` as retryable with exponential backoff and jitter, and that gives up rather than queueing unboundedly.

Worth confirming against Sleeper's own guidance before launch: published advice is looser than the 100/min working assumption used here, possibly by an order of magnitude. It changes nothing — the design runs at 2.3 — but it is the kind of number worth knowing rather than inheriting.

## 08 · Data model

```
League   (LeagueId PK, Season UNIQUE, Name, RosterPositionsJson,
          PreviousLeagueId, UpdatedUtc)

Team     (LeagueId, RosterId, TeamName, OwnerDisplayName, OwnerUserId,
          PK (LeagueId, RosterId))

WeekScore(LeagueId, Season, Week, RosterId,
          ActualPts, IdealPts,
          StartersJson, PlayersPointsJson, IdealLineupJson,
          IsFinal, ComputedUtc,
          PK (LeagueId, Season, Week, RosterId))

Player   (PlayerId PK, FullName, Team, PositionsJson, UpdatedUtc)
```

Four tables, down from five. `PlayerWeek` is gone with the scoring engine, and its one surviving job — keeping enough raw data to recompute after a solver bug — is done by `PlayersPointsJson` on the row that needs it. **Corrected: this league has six rosters, five of them scoring (A1)** — so roughly 6 × 18 is 108 rows a season, each carrying a small map of player points. The whole season is a few hundred kilobytes either way.

**σ is never stored.** It is a function of every finalized week at once, so it changes for every team each time a week finalizes. Storing it would create a second source of truth that goes stale silently — precisely the failure mode that separate table and chart endpoints would have caused in §09. It is computed on read from the weekly array.

`ScoringSettingsJson` is dropped from `League` for the same reason it is no longer fetched.

**The positional radar (§11) needs no new storage.** Its axes come from the season's `RosterPositionsJson`, and its values come from `StartersJson` joined to `PlayersPointsJson` on rows that already exist. A feature that adds a chart and no columns is worth noticing.

Two columns exist only to make multiple seasons work. `PreviousLeagueId` is the link Sleeper puts on each league pointing at its prior incarnation, and it is what D8 walks. `Season` carries a unique index because this site serves exactly one league per year, which turns the year parameter into a single indexed lookup.

`OwnerUserId` is not needed by anything in this document, and is here anyway. Sleeper's `user_id` is the only identifier stable across seasons — `roster_id` is reassigned and team names change every August — so it is the sole means by which a future cross-season view could say "this is the same manager". One column now; a painful backfill later.

### D7 — Azure SQL serverless on the free offer; no blob layer on day one

Chosen: the free Azure SQL offer — 100k vCore-seconds and 32 GB a month — and nothing in front of it.

The free tier must auto-pause; that is how it stays free. Running 0.5 vCore continuously is roughly 1.3M vCore-seconds a month against a 100k allowance, so disabling pause is not an option, and a paused database takes 30–60 seconds to resume. On a Sunday, traffic keeps both the Function and the database warm and this never appears. On a Wednesday in March, the first visitor waits.

v1 specified a JSON snapshot in blob storage to hide that wait. Cut — it added a resource and a second write path to the finalize job to solve a problem that may never annoy anyone. Show a loading state instead, and add the snapshot only if the wait turns out to bother you. The one argument that would bring it back is not latency but availability: if the free tier hits its monthly limit and pauses until the next cycle, a snapshot keeps the site serving. Build it when that happens.

Set the free-tier behaviour to *pause on limit*, not *continue and bill*. One runaway job should cost a paused database, not an invoice.

## 09 · API contract

One endpoint per season. Table and chart derive from the same payload, which makes it structurally impossible for them to disagree — a real failure mode when totals and series are computed by separate handlers.

```jsonc
GET /api/season             → current season
GET /api/season?season=2025 → a completed season

200 OK
Cache-Control: public, max-age=30                    # current season
Cache-Control: public, max-age=31536000, immutable   # completed season
ETag: "2026-wk7-1738012800"

{
  "season": 2026,
  "isCurrent": true,
  "currentWeek": 7,
  "finalThroughWeek": 6,          // σ covers 1..6; points covers 1..7
  "asOfUtc": "2026-10-18T18:42:03Z",
  "slotOrder": ["QB","RB","WR","TE","FLEX","K","DEF"],
  "leagueSlotAverages": {         // the radar's reference circle
    "QB": 18.4, "RB": 12.1, "WR": 13.2,
    "TE":  8.6, "FLEX": 11.9, "K": 8.0, "DEF": 7.1
  },
  "teams": [
    {
      "rosterId": 3,
      "name": "Bijan Mustard",
      "totals": {
        "actual": 812.66, "ideal": 941.20,
        "sigma":  24.7,           // null until 4 final weeks
        "cv":     0.182,          // null likewise
        "mean":  116.09, "min": 78.40, "max": 152.30
      },
      "slotAverages": {              // per-slot, per-week means (§11)
        "QB": 21.2, "RB": 11.8, "WR": 14.0,
        "TE":  8.1, "FLEX": 12.6, "K": 7.9, "DEF": 6.4
      },
      "weeks": [
        { "week": 1, "actual": 121.44, "ideal": 139.72, "isFinal": true },
        { "week": 7, "actual":  46.20, "ideal":  58.40, "isFinal": false }
      ]
    }
  ]
}

GET /api/seasons

{ "current": 2026, "available": [2026, 2025, 2024] }
```

The league is not a parameter. It is a single configured value, `League:CurrentLeagueId`, and nothing in the request can change which league is served. A league ID in the query string would turn this into a general-purpose proxy for anyone's league — an open relay against Sleeper's rate limit, attached to your subscription and your budget alarms.

### D8 — Configure one league ID; resolve the rest by walking the chain

Chosen: store the current season's league ID in configuration, and populate every prior season by following `previous_league_id` backwards on the daily timer.

The complication a year parameter runs straight into: **Sleeper league IDs are not stable across seasons.** A league that rolls over in August becomes a genuinely new league with a new ID, linked to its predecessor by `previous_league_id` on the league object. So `?season=2025` cannot be answered by the configured ID — it needs a map from year to ID.

The obvious version of that map is a config dictionary, and it is wrong in a slow way: it works until the August someone forgets to update it, and that is a season nobody notices is missing until December. Walking the chain makes the map derive from Sleeper itself. Start at the configured ID, follow `previous_league_id` until it is null, upsert a `League` row per hop. It costs one call per season of league history, once a day, and the only thing to change each August is the single configured ID — which the rollover forces you to notice anyway.

Where it breaks: the chain exists only if the commissioner used Sleeper's roll-over flow. A league recreated from scratch has a null `previous_league_id` and its history is unreachable. Support an optional `League:SeasonOverrides` map for exactly that case, empty by default, and let the walk fill in everything else.

#### Serving a completed season

A finished season is immutable and the response headers should say so outright — a year of `max-age` plus `immutable`, against thirty seconds for the live one. It is served entirely from SQL and reaches Sleeper zero times, no matter how many people open it.

An unknown or un-backfilled year returns **404 with the list of available seasons** — never an empty payload, and never a synchronous backfill. Backfilling a season is roughly twenty-one calls: eighteen weeks of matchups plus league, users and rosters. That fits the budget comfortably, but running it inside a request means a visitor waits twenty seconds for a page, and a stray URL becomes a burst. Backfill is an explicit job, run once per historical season; the API only ever serves what SQL already holds.

`totals` is computed server-side even though the client could derive it from `weeks`. Deliberate: σ has a definition — population, finalized-only, null below four weeks — and it should live in exactly one place. A client that recomputed it would eventually disagree with the server about an edge case, and the disagreement would be invisible until someone noticed two numbers that should match.

`finalThroughWeek` is what lets the UI honour R1 honestly: the table can label the σ column "through week 6" while the points column already shows week 7, instead of quietly mixing two horizons in one row. On a completed season it simply equals the final week.

Serve `304` on a matching `ETag`. Between the cache and conditional requests, the poll costs almost nothing on either side.

## 10 · Frontend

> **Corrected · this league has five scoring teams, not twelve.** Several
> legibility arguments in this section and in §11 were written for a twelve-team
> league. The decisions they reach are still right — hover-to-isolate, one radar
> at a time, the median mode — but they are less forced than the original text
> implies. Five distinguishable colours is a far easier palette problem than
> twelve (A1, and the note under §08).

Vite, TypeScript, React and D3 — with a firm boundary between the last two.

### D9 — React owns the DOM; D3 is used as a maths library

Chosen: import `scaleLinear`, `line`, `axisBottom` and friends; never call `d3.select().append()`.

D3's scales, path generators and tick logic are the reason to use it and are genuinely hard to replace. Its DOM layer duplicates what React already does, and running both against the same nodes is the standard source of stale-render bugs in D3-plus-React charts. Compute with D3, render the resulting path strings as JSX `<path>` elements.

#### Chart controls

- **Season** — defaults to current, populated from `/api/seasons` rather than a hardcoded list, so a new year appears the day the chain walk finds it.
- **Team filter** — multi-select, all on by default. Teams are encoded by colour.
- **Metric** — cumulative points, cumulative ideal, expanding σ. Single-select rather than multi, because σ is on a completely different scale from the two point totals and sharing an axis with them would flatten it into a line along the bottom. Switching metric switches the y-axis.
- **Display mode** for the point metrics — cumulative, per-week, or *vs. league median*.
- **σ variant** — raw points or coefficient of variation, per D3.

**Stop polling when a past season is selected.** Nothing about 2025 will ever change, so a 30-second timer against it is pure waste — and worse, it makes the page look live when it is not. Switching to a historical year should visibly settle: the poll stops, the "as of" timestamp gives way to the season's final week, and every live-updating affordance disappears. Selecting a season is the one control that changes what kind of page this is.

#### The median mode is worth building

Plotting cumulative points minus the cumulative weekly median changes no ranking — the median sum is an identical constant for every team — but it removes the league-wide trend that dominates the raw chart. Five near-parallel ascending lines are hard to read; the same five diverging from a zero baseline are legible at a glance. Same data, better chart, about four lines of code.

#### The table is the primary object

It should be fully readable without interaction: tabular figures, the sort column visually marked, the gap to first place shown, and the inline range mark from §04 giving σ a shape rather than only a value. Hovering a team in the chart should raise it and mute the rest — asking a reader to trace one colour out of a crowd is not a design.

#### Motion, if any

Optional, and gated on one test: cut it entirely and the site still does its job. If it is built, the library is **Motion** — declarative, so D9 still holds and React keeps ownership of every node — supplemented by the native View Transition API for section changes, which costs nothing and degrades to an instant swap.

The animation actually worth having is the standings table re-sorting: rows sliding past one another when someone overtakes on a Sunday afternoon makes the live-ness legible, where a number quietly changing in place is invisible. Everything else is decoration.

Two constraints on all of it. Every entrance animation must be gated on first load, or the 30-second poll replays it every 30 seconds and the page becomes unusable during games. And a loading animation belongs to the short wait only — the cold-start case from D7 runs to a minute, and past about four seconds a reader wants an explanation rather than a loop.

Render σ as an em dash before week four and label the column with its week range. An in-progress week's ideal is provisional and should read as such; it is the one number on the page that can move downward as the afternoon goes on, since a bench player's late points can raise the ideal a team never reached.

## 11 · Positional radar

A third view: one team's average output at each of its starting slots, against what the league averages at the same slot. Axes come from the league's own rules, so the chart reconfigures itself for a superflex league or one without kickers.

> **Diagram** (see the published artifact for the rendered SVG)
> One team against the league at each of its own starting slots. Outside the dashed circle is better than the league at that slot; inside it is worse, and the circle itself is the whole league. Plotting the ratio rather than raw points is what turns the reference into a circle — and what stops every team’s chart from being the same QB-shaped spike.

### Slots, not positions

Sleeper's `starters` array is positionally aligned with the startable entries of `roster_positions` — `starters[0]` plays in the first slot, `starters[1]` in the second, and so on. That single fact does most of the work here.

It means the axes are **slots** rather than positions, and no position inference is needed. A `FLEX` axis measures whoever you chose to put in the flex, which is the more interesting question anyway: not "how are your running backs" but "how is the decision you make every week at that slot". A superflex league gets a `SUPER_FLEX` axis for free.

#### The value on each axis

| Step | Definition |
|---|---|
| Team, slot, week | Points from all slots of that type ÷ the number of such slots |
| Team, slot, season | Mean of the weekly values over regular-season weeks played |
| League, slot, season | Mean of every team's season value at that slot |
| Plotted | Team season value ÷ league season value |

Dividing by the slot count matters: a league with two RB slots and one QB slot would otherwise make every team's RB axis roughly twice its QB axis, which is a fact about the rules rather than the roster.

Average first, then divide. Computing a ratio per week and averaging the ratios blows up on the week where the whole league's kickers scored two points between them — a near-zero denominator turns one bad Sunday into a permanent distortion.

### D10 — Plot the ratio to league average, never raw points

Chosen: every axis is a ratio centred on 1.0, where the league average is a perfect circle.

Raw points would make this chart useless, and in a way that is easy to miss because the result still *looks* like data. QB slots average around 20 points a week; kickers and defences around 7. On shared radii every team in the league draws the same QB-shaped spike with a dent at K and DEF, because that shape is a property of football, not of the team.

The ratio removes the positional baseline and leaves only the part that varies between managers. It also produces the best property this chart has: the league average is a circle, so "inside the circle" and "outside the circle" are legible instantly and need no legend.

**What the ratio loses** is spread. Being 1.15× at QB, where every team sits within ten percent of the others, is a far larger edge than 1.15× at DEF, where weekly variance is enormous. Offer percentile-rank-within-league as a toggle for anyone who wants the fairer comparison; keep the ratio as the default because it is the one people can explain to each other.

> **Risk R2 · the shape is partly an artefact of the drawing**
>
> Radar charts have two well-known distortions and both apply here. Area grows with the *square* of the radius, so a team 20% better across the board encloses about 44% more area and reads as dominant. And the silhouette depends on the order of the axes — the same seven numbers arranged differently produce a visibly different "personality" — even though the ordering carries no meaning.
>
> Mitigation: fix the axis order to `roster_positions` order and never re-sort it, so the shape is at least stable and comparable between teams and seasons. Do not label or describe enclosed area as if it meant anything. And ship the alternative below, so anyone who wants to read actual values has somewhere to go.
>

### D11 — One team at a time, with a diverging bar chart as the honest alternative

Chosen: the radar shows a single team against the reference circle; a toggle switches the same data to a horizontal diverging bar chart.

A dozen overlaid polygons would be unreadable, and even three is difficult — the radar's strength is that a single team has a memorable silhouette, and that strength disappears the moment a second shape crosses it. Allow a second team for a head-to-head, cap it there.

The bar chart is the concession to accuracy. What this data actually is — a signed deviation from a baseline across seven categories — has a canonical form, and it is a diverging bar chart centred on zero, not a radar. Bars are read against a common linear axis, so values are directly comparable and nothing is distorted by area or ordering.

The radar stays the default anyway, because this is a page for a fantasy league rather than an analytics console, and "what shape is my team" is a question people actually want answered. Both views, one data set, no argument about which is correct.

### League rules, season by season

The design already stores what this needs. Each season is its own `League` row with its own `RosterPositionsJson`, keyed by a unique `Season`, so a 2024 radar is drawn on 2024's slots and a 2026 radar on 2026's. Rules that change between seasons are handled by construction.

Rules that change *within* a season are assumed not to happen — but Sleeper does permit it, and the failure would be silent: `starters` would stop lining up with the stored slot list and every week would be attributed to the wrong axis. So verify the assumption rather than trusting it. At finalize time:

```
if (starters.Count != startableSlots.Count)
{
    logger.LogError(
        "Roster positions changed mid-season for league {LeagueId} at week {Week}: " +
        "{StarterCount} starters against {SlotCount} slots. Slot attribution skipped.",
        leagueId, week, starters.Count, startableSlots.Count);
    return weekScore with { SlotPoints = null };   // points and ideal are unaffected
}
```

A stated assumption deserves a cheap assertion, not faith. The week loses its radar contribution and says so; the points and ideal columns, which do not depend on slot alignment, carry on unaffected.

> **Verify at T02**
>
> The positional alignment of `starters` is load-bearing for this entire section and is an observation about Sleeper's behaviour rather than a documented guarantee. T02 confirms it against a real matchup response before anything is built on it: fetch a week, and check that `starters[i]` is a player eligible for `roster_positions[i]` for every startable slot.
>

## 12 · Repo & CI/CD

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

`Core` having no Azure dependency is the point of the layout, not an aesthetic preference. Every interesting bug in this project now lives in the solver or in σ's edge cases, and both are pure functions over fixtures — testable offline in milliseconds, with no emulator, no connection string and no network.

The σ tests are mostly about absence: zero weeks, one week, three weeks, four weeks, a week with a zero score that is genuinely zero, and a week excluded for not being final. Every one of those should return null or a defined number for a stated reason, and none of them should throw.

#### Pipeline

- **OIDC federated credentials** between GitHub and Entra ID — no publish profiles or service-principal secrets in the repo. The highest-value thing to get right on day one, because retrofitting it means rotating whatever you used instead.
- **Bicep `what-if` on pull requests**, posted as a comment. Infrastructure diffs reviewed like code diffs.
- **EF migrations as a deploy step**, with the SQL firewall opened to the runner IP and closed again in an `always()` step so a failed migration cannot leave the database exposed.
- **SWA preview environments** on pull requests — free on the Free tier, and the fastest way to look at a chart change before merging.

## 13 · Cost

**Corrected: the Functions row, the ingest cap, and a missing resource.**

| Resource | Tier | Headroom | Monthly |
|---|---|---|---|
| Static Web Apps | Free | 100 GB bandwidth, custom domain + TLS | $0.00 |
| Functions | **Flex Consumption (FC1)** | Free grant; this app uses ~14% of the execution allowance and ~4% of the GB-s | $0.00 |
| Storage account | Standard_LRS | Required by Functions for runtime state and the deployment container; also holds the 840 KB player map (A12) | $0.00 |
| Azure SQL | Serverless, free offer | 100k vCore-s, 32 GB — **not provisioned yet (M3)** | $0.00 |
| Log Analytics + App Insights | Pay-as-you-go | 5 GB/mo free — **cap ingest at 0.16 GB/day, not 1 GB/day** | $0.00 |
| Steady state | | | $0.00 |

**The 1 GB/day figure in the original was wrong by 6×.** A 5 GB monthly free
allocation divided across 31 days is 0.16 GB/day; a 1 GB/day cap would permit
31 GB a month and bill for 26 of them. The deployed workspace is capped at
`0.16` with 30-day retention.

A budget alert is also deployed, firing on any actual spend at all as well as on
a forecast breach. It is conditional on `budgetAlertEmail` being supplied — with
that parameter empty, no budget resource is created and nothing warns you.

Dropping the blob layer takes the bill to a genuine zero. A custom domain adds roughly $12 a year if you want one; the TLS certificate is included.

Two things to actually configure rather than assume: the App Insights daily ingest cap, and the SQL free-tier limit behaviour from D7. Both are the difference between a free hobby project and an unpleasant surprise.

## 14 · Open questions

**Five of the eight were settled on 2026-09-10 against the live league** — Q1,
Q2, Q3, Q7 and Q8. Their answers and consequences are in
[`amendments.md`](amendments.md); the three still open are Q4, Q5 and Q6, tracked
in [`tasks/todo.md`](../tasks/todo.md).

- **Q1 answered** — six rosters; eleven startable slots (`QB, QB, RB, RB, WR, WR, TE, FLEX, SUPER_FLEX, K, DEF`) plus eight `BN`. The league ID is a secret (A2).
- **Q2 answered** — SUPER_FLEX yes, IDP no. The eligibility map stays general anyway (A3).
- **Q3 answered** — moot; the league has no `IR` or `TAXI` slots. The default below stands if one appears.
- **Q7 answered** — 2026 is the first season. A null `previous_league_id` is legitimate, and no `SeasonOverrides` are needed (A4).
- **Q8 answered** — eight distinct slots, comfortably readable as a radar. No axis merging needed.

The original text of all eight follows, for the reasoning behind the ones still open.

- Q1 · **League ID and `roster_positions`.** Needed to pin solver fixtures. Everything in §05 is shaped by the slot list.
- Q2 · **Does the league use SUPER_FLEX or IDP slots?** Determines how much of the eligibility map is live code versus dead branches — and IDP means the solver's slot count rises, though nowhere near enough to threaten the DP.
- Q3 · **Should players in IR slots count toward ideal points?** Default assumption: no — ideal means the best lineup that was legally startable. Worth settling now, since it is exactly the kind of thing a league argues about in December.
- Q4 · **Regular season only, or through the playoffs?** Sleeper's playoff weeks have a different matchup structure and only some teams play. A league decided on total points probably wants regular season only — but σ in particular would be badly distorted by weeks where half the league does not play, and the chart's x-axis needs to know.
- Q5 · **Minimum weeks before σ appears — four, or five?** Four is the assumption throughout this document. Five is defensible and buys another week of noise reduction at the cost of a longer empty column early in the season.
- Q6 · **Should the table show σ or CV?** D3 chooses σ for legibility with CV on the chart. If the league's scoring spread is wide — a gap of thirty or more points between the highest- and lowest-scoring teams' means — CV is the fairer default and the decision is worth revisiting.
- Q7 · **Is the league’s season chain intact?** Follow `previous_league_id` back from the current league and count the hops. If it hits a null before reaching the league’s first season, those earlier years need explicit entries in `League:SeasonOverrides` and someone has to dig the old IDs out of Sleeper by hand. Cheap to check now, tedious to discover later.
- Q8 · **How many distinct starting slots does the league have?** Seven or eight axes is a comfortable radar; a superflex-plus-IDP league can reach eleven or twelve, at which point the labels collide and the shape stops being readable. If the count is high, either merge the IDP slots into a single axis or make the diverging bar chart from D11 the default view for that season.

## 15 · Build order

Ordered so the highest-risk, lowest-cost work happens first. The solver is the only place a real bug can hide, and it needs no Azure account to prove.

**Status at 2026-09-13: M0, M1 and M2 are built and deployed.** The ordering held
— the one genuine bug found in the whole build was in the solver, exactly where
this section predicts, and it was caught by fuzzing against exhaustive search
before any cloud account existed. M3 is next.

- M0 · Core, offline — An hour of curl against the real league first — settling Q1 to Q4 and Q7 and capturing fixtures — then the ideal-lineup solver and the σ function. Ship when the solver beats greedy on a constructed flex case, handles a bye-week hole, and σ returns null for every count below four.
- M1 · Sleeper client — Typed client, token-bucket handler, single-flight cache. Prove it with a console app that prints the real league's standings and asserts both §03 invariants across a whole season — where the assumptions M0 confirmed by eye meet code that runs.
- M2 · API on Azure — Functions HTTP with an in-memory season, deployed by bicep over OIDC. `/api/season` live and correct for the current week, no database yet.
- M3 · Persistence — SQL, EF migrations, the finalize timer, the daily player refresh and the season-chain walk. Backfill the current season, then each prior season as a one-shot job.
- M4 · The page — Table with the inline range mark, then the chart. First milestone anyone else in the league can look at — and the first that will generate opinions.
- M5 · Chart modes — Per-week, vs-median and CV toggles. Deliberately last: they are cheap, they are where taste matters most, and they are much easier to judge against a season of real data than against three weeks of it.
- M6 · Positional radar — Slot attribution, the league reference, and the radar itself (§11). Last because it is the only feature that depends on an unverified property of Sleeper’s payload, and because it is the one most improved by having a full season to look at.
- M7 · Motion — optional — Table layout animation, chart reveals, section transitions and a football loader. Six and a half hours that buy nothing functional, listed separately so that cutting them is a decision rather than an omission.
