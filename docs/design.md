# Ideal Points — Design Document (v6)

> Source: the published artifact
> https://claude.ai/code/artifact/6191a761-1035-4684-947a-1f2fb063c645
>
> **Corrected in place on 2026-09-13 against the built system.** This file is edited directly; a re-sync would reinstate the errors marked **Corrected**. The reasoning for each change is in [`amendments.md`](amendments.md), keyed A1–A34.
>
> 11 decisions · 2 risks · 8 open questions, 5 answered.

---

A live standings board for one Sleeper fantasy league. The season is scored on total points. Two commentary metrics sit beside it: how steadily the points arrived, and how many stayed on the bench.

- Data source · Sleeper API
- Stack · .NET 10 · Azure
- Steady-state cost · ≈ $0.25 / mo, alert at $0.50 (A30)
- Sleeper budget · ≈ 4.4 req/min as built, against a 25/min bucket per instance — see §07
- Status · **M0–M7 built and deployed.** Deferred: T29–T30 (season chain, backfill), T56 (season picker)
- League · **six rosters, five of them scoring** — one is excluded (A1)

## 01 · Scope

One public, read-only page for one league: a standings table and a line chart, both updating live during games.

The league is decided on **total season points**: the sum of every week's actual score, nothing else. Consistency and ideal points are commentary. Consistency states whether points arrived steadily or in two large weeks. Ideal states how many points never left the bench. Neither decides the outcome, so both may be approximate; the actual column may not.

Out of scope: authentication, writes of any kind, head-to-head or playoff views, trades, anything requiring a Sleeper login. The site reads one league ID from configuration and nothing else.

> **Changed in v2 · projections removed**
>
> Sleeper's API exposes no fantasy projections. The projected-points column is cut, and a third of this design with it: the scoring engine (raw stat lines → points), the `PlayerWeek` table (stat lines stored for replay), and the lock-time snapshot job (projections frozen before kickoff). Actual points arrive pre-computed in `matchup.players_points`. Nothing multiplies a stat line by `scoring_settings`.

## 02 · The three metrics

Two metrics are season-to-date sums. The third is not, and the table states this rather than implying symmetry.

| Column | Definition | Shape | Better |
|---|---|---|---|
| **Points** | Sum of `matchup.points` across all weeks played. | Running total | Higher |
| **Consistency σ** | Population standard deviation of finalized weekly scores. | Scalar, recomputed weekly | Lower |
| **Ideal** | Sum of the maximum obtainable from each week's full roster. | Running total | Higher |

σ inverts: a smaller value is better. It must not share the visual treatment of the adjacent columns; a shared bar-length or heat encoding across all three reads backwards for σ.

Under a total-points format, variance has no effect on the winner; only the sum matters. A 190/62 season ties a 126/126 season. σ is descriptive, not a second scoreboard, and the page presents it as such. It answers "how did you get here", not "are you winning".

### D1 — Population σ over finalized weeks only, undefined below four

Chosen: divide by *n*, not *n*−1; include only weeks marked final; return null when fewer than four weeks qualify.

**Population, not sample.** The weeks played are the complete population of interest, not a sample of hypothetical weeks. Bessel's correction answers no question asked here, and it inflates σ early in the season, when *n* is smallest and the value least reliable.

**Null below four weeks.** σ over two data points is determined entirely by their gap; it carries no information. Displaying it invites reading noise as signal. Displaying `0.0` in week one is false. The column shows an em dash until week four.

**Finalized weeks only.** See R1. This is the constraint most likely to be implemented wrong.

> **Risk R1 · in-progress weeks poison σ**
>
> A partial week only lowers the two sum columns temporarily. For σ it is wrong. A team on 41 points at 2pm Sunday with seven players still to play reads as an extreme outlier; σ jumps by tens of points, then falls back over the afternoon. The standings churn while nothing has happened.
>
> Mitigation: σ is computed over `isFinal` weeks only; the in-progress week is excluded until Tuesday's finalize job runs. The consequence is surfaced, not hidden: the column header states its week range, so σ reads "through week 6" while the points column includes week 7.
>

### D2 — Ideal uses the roster as it stood that week

Chosen: solve against that week's `matchup.players`, not the roster's current contents.

Sleeper's matchup payload carries the roster as of that week. Solving against today's roster rewrites history after every waiver claim: a player dropped in week 4 stops counting toward the week 2 ideal. Weeks are finalized and frozen for the same reason.

## 03 · Sleeper data

Sleeper's read API is unauthenticated and free. Every required field is in the documented `/v1` surface; no undocumented dependencies remain.

| Endpoint | Gives | Cache |
|---|---|---|
| /v1/state/nfl | Current season, week, season type | 5 min |
| /v1/league/{id} | `roster_positions` — the startable slots | 24 h |
| /v1/league/{id}/users | `metadata.team_name`, `display_name` | 1 h |
| /v1/league/{id}/rosters | `roster_id` → `owner_id` | 1 h |
| /v1/league/{id}/matchups/{wk} | `points`, `starters`, `players`, `players_points` | 30 s |
| /v1/players/nfl | **14.65 MB** dump — `fantasy_positions` for the solver | 24 h |

Team name is `users[].metadata.team_name`, falling back to `display_name`. **Corrected (A6): an unset team name is an absent key, not an empty string.** `metadata` itself may be absent. Treating absence as `""` produced blank rows instead of the fallback.

`scoring_settings` is no longer read. It served only to convert raw stat lines to points; with projections gone, every displayed point value arrives already scored by Sleeper.

`players/nfl` remains required and is the one heavy call. **Corrected: it is 14.65 MB across 12,227 players, not ≈5 MB** (measured 2026-09-13). Fetch it once a day on a timer, project it to `{ playerId → { name, team, fantasyPositions } }`, and cache the projection. It is never on a request path.

The projection keeps the 11,902 players with a non-empty `fantasy_positions` and discards every other field: a 17× byte saving from dropping *fields*, not rows. **Corrected: "cache that" was not sufficient.** The map is per-instance memory, and Flex Consumption scales to zero and out to forty instances, so the map is persisted to blob storage and adopted by each cold instance (A12). With an in-memory cache alone, every deployed request returned 500.

#### Two free invariants

With no scoring engine there is no ground truth to unit-test against. The payload self-checks in two ways; assert both in production, not only in tests:

- `sum(starters_points) == points` for every roster, within a cent. Detects a misaligned `starters` array and confirms the payload is read as Sleeper writes it.
- `ideal >= actual` for every team-week. **Corrected (A7, A9): not "always, with no exceptions".** `actual` is `custom_points ?? points`, and a commissioner override can take any value, so both invariants compare against Sleeper's own scored `points`. Against a scored total the invariant holds: the started lineup is one of the assignments the solver considers, so a violation is a solver bug, most likely a slot eligibility rule. Consequence: after a generous override the page can show an ideal below the actual. This is correct, not a bug.

Both invariants are collected and reported, never thrown; so are players the map cannot explain (A8). Silently dropping an unknown player lowers that team's ideal, and `ideal >= actual` cannot detect undercounting.

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

The nullable return is load-bearing. Returning `0.0` for a two-week season makes the newest, least-known teams appear the most consistent. A nullable type forces the caller to handle absence, which forces the em dash into the UI.

### Plotting a scalar week to week

σ is one season-to-date number with no natural weekly value. The chart requires an explicit choice between two series:

| Series | Value at week *w* | Reads as |
|---|---|---|
| **Expanding σ** (default) | σ over weeks 1…*w* | Converges as the season settles — matches the season-to-date semantics of the other two lines |
| **Rolling σ** | σ over weeks *w*−3…*w* | Whether a team is becoming steadier or wilder right now; noisier, needs four weeks of runway |

Default to expanding. Rolling is more informative in the back half of a season but near-meaningless before week eight, and a chart that is empty or jagged on first load teaches the reader to distrust it.

### D3 — Raw σ in the table, coefficient of variation as a chart option

Chosen: display σ in points; offer CV = σ ÷ mean as a chart toggle.

**Corrected (A17): σ is no longer in the standings table.** The reasoning below still selects σ over CV, for σ's own panel (T59) rather than a table column.

σ is scale-dependent, so it is unfair across teams with different scoring levels. A team averaging 140 with σ = 25 is *relatively* steadier than one averaging 90 with σ = 22; the raw numbers say the opposite. CV is the statistically correct comparison and costs one division.

CV is less intuitive. A league-mate says "I swing about twenty-five points a week", not "my coefficient of variation is 0.18". The table shows the number people discuss; the chart offers the technically correct one.

### Showing σ in a table cell

**Corrected (A17): this did not work in a table cell.** At ≈ 140px the range mark was illegible, and it shipped without the figure. It moves, with the figure, to its own panel (T59), where the reasoning below still applies.

A bare standard deviation is hard to interpret at a glance. Pair the figure with an inline range mark: a horizontal rule from the team's minimum to maximum weekly score, a tick at the mean, and a shaded band at ±σ, on a scale shared by every row. The number gives precision; the mark makes rows comparable without reading them. **Corrected (A33):** each mark is centred on its team's mean, so the shared scale measures distance from the mean and widths compare spread directly.

No extra cost: minimum, maximum and mean come from the same weekly array as σ.

## 05 · Ideal lineup

Given rostered players with points and a list of starting slots, find the assignment that maximises total points. Slots: `league.roster_positions` with `BN`, `IR` and `TAXI` removed. Eligibility: each player's `fantasy_positions`.

This is the only non-trivial computation in the project and the only place a real bug can hide.

### D4 — Exact assignment, not greedy

Chosen: bitmask dynamic programming over the set of filled slots.

Greedy (sort by points, place each player in the best open slot) is wrong whenever flex slots exist. Placing the top RB in `FLEX` can strand a second RB, giving a total below the maximum. The metric exists to report points left on the bench, so an approximation that undercounts is worse than none.

The problem is small enough for exactness at no cost: ≤ 11 startable slots and ≈ 20 rostered players give 2^11 states and ≈ 450k operations per team-week. Min-cost flow or Hungarian are also exact; the DP is twenty lines and needs no dependency.

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

`dp.Max()`, not `dp[full]`: on a bye-heavy week a roster may be unable to fill every slot, and the maximum over all reachable states handles this without a special case.

#### Slot eligibility map

| Slot | Accepts |
|---|---|
| QB / RB / WR / TE / K / DEF | Exact position match |
| FLEX | RB, WR, TE |
| SUPER_FLEX | QB, RB, WR, TE |
| REC_FLEX | WR, TE |
| WRRB_FLEX | RB, WR |
| IDP_FLEX | DL, LB, DB |

Store the solved lineup alongside the number, so a disputed ideal is answered with the exact eleven players rather than re-derived under pressure.

Test with pinned JSON fixtures from two or three real weeks (offline, no network, milliseconds), plus the `ideal >= actual` invariant from §03 asserted on every production computation. Include at least one fixture with a bye-week hole and one where greedy differs from optimal; otherwise the DP is untested against the case that motivated it.

## 06 · Architecture

### D5 — Static Web Apps + Azure Functions, not a single container app

Chosen: SWA Free for the SPA, Functions Consumption (.NET 10 isolated) for the API and scheduled jobs.

> **Corrected · the plan is Flex Consumption (FC1), not Consumption (Y1).**
> Y1 could not be provisioned: `SubscriptionIsOverQuotaForSku`, current Y1 VM limit zero. FC1 also scales to zero and has its own free monthly grant. The deployed app uses `instanceMemoryMB: 512` with `maximumInstanceCount: 40`, the platform minimum. The decision's arguments hold; its SKU name does not.

Scheduling decides it, less strongly than in v1. Weeks are finalized after Monday night and the player dump refreshed daily; both are Timer triggers, which Functions provides free and a Container App does not. Consumption scales to zero without configuration. SWA Free includes a custom domain with managed TLS; App Service Free does not.

Cost of the split: CORS configuration and two deploy targets. A linked backend removes the CORS step but requires SWA Standard at $9/month, most of the budget, for a configuration convenience.

> **Diagram** (see the published artifact): viewer count never reaches Sleeper; finalized weeks come from SQL, the current week from single-flight caches (built: 5 s for the season, 15 s for scores; designed: 30 s; §07).

### D6 — Polling, not websockets

Chosen: client polls every 30 s, gated on tab visibility and on being inside an NFL game window.

Data cannot change faster than the server's 15 s score cache (**corrected, A27**: not 30 s). A push channel would beat the 30 s poll by under 30 s, at the cost of a SignalR resource, a connection lifecycle and a reconnect path. Polling stays correct across sleep, network changes and mobile backgrounding.

## 07 · Request budget

Steady state on a Sunday afternoon under load. With reads funnelled through the server, the ceiling is not a binding constraint.

**Corrected: the built TTLs differ from the designed ones**, and the headline is ≈ 4.4 req/min, not 2.3. Both columns are shown. The TTLs live in `SleeperReader`, each with its reason.

| Call | TTL designed | TTL built | Req/min built |
|---|---|---|---|
| matchups/{current week} | 30 s | **15 s** | 4.0 |
| state/nfl | 5 min | 5 min | 0.20 |
| rosters | 1 h | **5 min** | 0.20 |
| users | 1 h | 1 h | 0.02 |
| league/{id} | 24 h | 24 h | <0.01 |
| players/nfl (14.65 MB) | 24 h | 24 h | <0.01 |
| weeks 1…n−1 | SQL, permanent | **blob first, SQL behind it, 6 h in memory** (A16) | 0 once stored |
| prior seasons | SQL, immutable | **weeks from SQL; league, users, rosters 24 h; chain walk 24 h** (A34) | < 0.01 per season |
| Peak steady state | ≈ 2.3 | | **≈ 4.4** |

Matchups at 15 s cost twice the designed traffic for a fresher page: with the 30 s client poll (D6), a score is at most 45 s old instead of 60 s.

**Every figure is per Function instance.** The cache is in memory and the token bucket is a singleton *per instance*; a Sunday scaled out to three instances costs three times the table. No cache is shared across instances (A12, A16). **The bucket is 25/min for that reason (A28):** Flex Consumption cannot be capped below 40 instances, and 40 × 25 keeps that worst case within Sleeper's guidance of 1,000/min.

The table assumes every finished week is stored. Two cases fetch more:

- **A finished week before Tuesday's job.** From Sleeper's move to the next week until `FinalizeWeek` stores the last one, that week is fetched from Sleeper like the live one: +4 req/min, **≈ 8.4** in all.
- **An empty blob and a database that will not wake.** With neither store answering, every finished week is rebuilt from Sleeper at 4 req/min each. By week 7 the total exceeds the 25/min bucket, which then refuses requests. A failed read is never cached, so this lasts only as long as the outage; the first read after the database wakes returns to the stored weeks. The blob usually answers first, so both stores must fail together.

Two mechanisms enforce the budget; the second is decisive:

- **Single-flight caching.** Concurrent requests for the same uncached key await one in-flight fetch. Without it, a cache expiry under load causes a thundering herd: a 2/min design becomes a 200/min incident.
- **A token bucket in the HTTP pipeline.** A `DelegatingHandler` on the Sleeper `HttpClient`, set to 50/min (**corrected, A28: 25/min per instance**), shared by the HTTP and Timer triggers. This is the structural guarantee: nothing reaches Sleeper except through the handler, so a retry-loop bug or a bad cache key cannot exceed the limit.

Pair it with a Polly policy: `429` is retryable with exponential backoff and jitter, and the policy gives up rather than queueing without bound.

Confirm Sleeper's own guidance before launch. Published advice may be looser than the 100/min working assumption, by up to an order of magnitude. This changes nothing (the design runs at 2.3), but the number should be known, not inherited.

## 08 · Data model

```
WeekScore(LeagueId, Season, Week, RosterId,
          ActualPts, IdealPts,
          StartersJson, PlayersJson, IdealLineupJson, SlotAveragesJson,
          IsFinal, ComputedUtc,
          PK (Season, LeagueId, RosterId, Week))

League   (LeagueId PK, Season UNIQUE, Name, RosterPositionsJson,
          PreviousLeagueId, UpdatedUtc)                 -- arrives with T29
```

**Corrected (A19): one table, not four.** `Player` is replaced by the player map in blob storage (A12), which every cold instance needs anyway. `Team` was never written: names and owners come from Sleeper's users and rosters, cached 1 h and 5 min (§07), and A1 matches on `user_id` from the same data. `League` belongs to the season-chain walk (D8) and is created by T29, not left empty until then.

`PlayerWeek`, one of the original five, is gone with the scoring engine. Its one surviving job, keeping enough raw data to recompute after a solver bug, is done by `PlayersJson` on the row that needs it (named `PlayersPointsJson` until A28; it holds each player's name, positions and points, not a map of points). **Corrected: this league has six rosters, five of them scoring (A1)**, so ≈ 6 × 18 = 108 rows a season, each with a small map of player points. A season totals a few hundred kilobytes either way.

**Corrected · the key leads on `Season`, not `LeagueId`.** The composite is the same four columns; only the order changed. A composite primary key is the clustered index, so its leading column determines what is sought rather than scanned. Every read is scoped to one season: T27 hydrates with `WHERE Season = @s AND IsFinal`, and the API's only parameter is a year. Leading on `LeagueId` suits a multi-league service. This site serves one league per year, so that column is near-constant and adds almost no selectivity.

**σ is never stored.** It depends on every finalized week at once, so it changes for every team whenever a week finalizes. Storing it creates a second source of truth that goes stale silently, the same failure separate table and chart endpoints would cause in §09. It is computed on read from the weekly array.

`ScoringSettingsJson` is dropped from `League` for the same reason it is no longer fetched.

**The positional radar (§11) needs no new storage.** Its axes come from the season's `RosterPositionsJson`; its values from `StartersJson` joined to `PlayersJson` on existing rows.

**Corrected: it added one column.** Each row carries `SlotAveragesJson`, that week's points per slot, computed when the week is solved. The axes still come from `roster_positions`, which Sleeper serves with the league.

Two columns exist only for multiple seasons. `PreviousLeagueId` is Sleeper's link from each league to its prior incarnation; D8 walks it. `Season` carries a unique index because the site serves one league per year, which makes the year parameter a single indexed lookup.

`OwnerUserId` is needed by nothing in this document and is included anyway. Sleeper's `user_id` is the only identifier stable across seasons (`roster_id` is reassigned; team names change every August), so it is the only means for a future cross-season view to identify the same manager. One column now avoids a backfill later.

**Corrected (A19): there is no backfill to fear.** Sleeper serves every season's users with their `user_id` for as long as the league chain exists, so a cross-season view can read it then. Storing it now copies data nothing reads.

### D7 — Azure SQL serverless on the free offer; no blob layer on day one

Chosen: the free Azure SQL offer (100k vCore-seconds and 32 GB a month), with nothing in front of it.

The free tier must auto-pause to stay free. 0.5 vCore continuously is ≈ 1.3M vCore-seconds a month against a 100k allowance, so pause cannot be disabled. A paused database takes 30–60 s to resume. On Sundays, traffic keeps the Function and database warm and the delay never appears. On a Wednesday in March, the first visitor waits.

v1 specified a JSON snapshot in blob storage to hide that wait. Cut: it added a resource and a second write path in the finalize job for a problem that may never matter. Show a loading state instead; add the snapshot only if the wait proves bothersome. The one argument for restoring it is availability, not latency: if the free tier hits its monthly limit and pauses until the next cycle, a snapshot keeps the site serving. Build it then.

**Corrected (A16): it was built, for latency rather than availability.** The 30–60 s resume hit the first visitor after every idle hour, which proved intolerable. `FinalizeWeek` now writes the season to SQL and to one blob, and the request path reads the blob first, so SQL is off the path once the blob is warm. **The two have different jobs (A20):** the blob caches the current season only; SQL is the record of every season. A past season is read from SQL and pays the resume, acceptable for a once-a-season read.

Set the free-tier behaviour to *pause on limit*, not *continue and bill*. A runaway job then costs a paused database, not an invoice.

## 09 · API contract

One endpoint per season. Table and chart derive from the same payload, so they cannot disagree. Separate handlers for totals and series can.

```jsonc
GET /api/season             → current season
GET /api/season?season=2025 → a completed season

200 OK
Cache-Control: public, max-age=5                     # current season (A27)
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

The league is not a parameter. It is one configured value, `League:CurrentLeagueId`; no request input changes which league is served. A league ID in the query string would make the API a general-purpose proxy for any league: an open relay against Sleeper's rate limit, attached to your subscription and budget alarms.

### D8 — Configure one league ID; resolve the rest by walking the chain

Chosen: store the current season's league ID in configuration; populate every prior season by following `previous_league_id` backwards on the daily timer.

**Sleeper league IDs are not stable across seasons.** A league rolled over in August is a new league with a new ID, linked to its predecessor by `previous_league_id` on the league object. `?season=2025` therefore cannot use the configured ID; it needs a map from year to ID.

A config dictionary fails slowly: it works until the August someone forgets to update it, and the missing season goes unnoticed until December. Walking the chain derives the map from Sleeper. Start at the configured ID, follow `previous_league_id` until null, upsert one `League` row per hop. Cost: one call per season of league history, once a day. The only August change is the configured ID, which the rollover forces anyway.

**Corrected (A34): no `League` table.** A19 dropped it unused. The chain is walked on demand and cached for 24 h in memory: one Sleeper call per season of history per day per instance, and past league IDs never reach the database. A completed season's names and slots come from Sleeper for that season's own league ID; its weeks come from SQL.

Failure case: the chain exists only if the commissioner used Sleeper's roll-over flow. A league recreated from scratch has a null `previous_league_id`, and its history is unreachable. Support an optional `League:SeasonOverrides` map for that case, empty by default; the walk fills in the rest.

#### Serving a completed season

A finished season is immutable, and the headers state it: one year of `max-age` plus `immutable`, versus **five seconds** for the live season (corrected, A27: short on purpose, to stay close to live during games). `/api/seasons` is cached for a day, since a new season appears once a year. Completed seasons are served entirely from SQL and reach Sleeper zero times, regardless of traffic.

An unknown or un-backfilled year returns **404 with the list of available seasons**: never an empty payload, never a synchronous backfill. Backfilling a season is ≈ 21 calls: 18 weeks of matchups plus league, users and rosters. That fits the budget, but inside a request a visitor waits ≈ 20 s for a page, and a stray URL becomes a burst. Backfill is an explicit job, run once per historical season. The API serves only what SQL already holds.

`totals` is computed server-side, although the client could derive it from `weeks`. σ has one definition (population, finalized-only, null below four weeks), and it lives in one place. A client recomputation would eventually disagree with the server on an edge case, invisibly, until someone noticed two numbers that should match.

`finalThroughWeek` lets the UI honour R1: the table labels the σ column "through week 6" while the points column shows week 7, instead of mixing two horizons in one row. On a completed season it equals the final week.

Serve `304` on a matching `ETag`. With the cache and conditional requests, the poll costs almost nothing on either side.

## 10 · Frontend

> **Corrected · this league has five scoring teams, not twelve.** Several legibility arguments in this section and §11 assume a twelve-team league. Their decisions still hold (hover-to-isolate, one radar at a time, the median mode) but are less forced than the text implies. Five distinguishable colours is a far easier palette problem than twelve (A1, and the note under §08).

Vite, TypeScript, React and D3, with a firm boundary between React and D3.

### D9 — React owns the DOM; D3 is used as a maths library

Chosen: import `scaleLinear`, `line`, `axisBottom` and similar; never call `d3.select().append()`.

D3's scales, path generators and tick logic are the reason to use it and are hard to replace. Its DOM layer duplicates React; running both against the same nodes is the standard source of stale-render bugs in D3-plus-React charts. Compute with D3; render the resulting path strings as JSX `<path>` elements.

#### Chart controls

- **Season**: defaults to current; populated from `/api/seasons`, not a hardcoded list, so a new year appears the day the chain walk finds it.
- **Team filter**: multi-select, all on by default. Teams are encoded by colour.
- **Metric**: cumulative points, cumulative ideal, expanding σ. Single-select, because σ's scale differs from the two point totals; a shared axis flattens it into a line along the bottom. Switching metric switches the y-axis.
- **Display mode** for the point metrics: cumulative, per-week, or *vs. league median*.
- **σ variant**: raw points or coefficient of variation, per D3.

**Stop polling when a past season is selected.** 2025 never changes, so a 30 s timer against it is waste, and it makes the page look live when it is not. Selecting a historical year settles the page: polling stops, the "as of" timestamp gives way to the season's final week, and every live-updating affordance disappears. Season is the one control that changes what kind of page this is.

#### The median mode is worth building

Plotting cumulative points minus the cumulative weekly median changes no ranking (the median sum is the same constant for every team) but removes the league-wide trend that dominates the raw chart. Five near-parallel ascending lines are hard to read; the same five diverging from a zero baseline are legible at a glance. Same data, better chart, ≈ 4 lines of code.

#### The table is the primary object

It is fully readable without interaction: tabular figures, the sort column marked, the gap to first place shown, and the §04 inline range mark giving σ a shape as well as a value. Hovering a team in the chart raises it and mutes the rest; requiring a reader to trace one colour through a crowd is not acceptable.

#### Motion, if any

Optional, under one test: the site still does its job with motion removed entirely. If built, the library is **Motion**: declarative, so D9 holds and React keeps ownership of every node. Supplement it with the native View Transition API for section changes, which costs nothing and degrades to an instant swap.

The one animation worth having is the standings table re-sorting. Rows sliding past one another on a Sunday-afternoon overtake make the liveness visible; a number changing in place is invisible. Everything else is decoration.

Two constraints apply to all motion. Gate every entrance animation on first load; otherwise the 30 s poll replays it every 30 s and the page becomes unusable during games. A loading animation covers the short wait only: the D7 cold start runs to a minute, and past ≈ 4 s a reader needs an explanation, not a loop.

Render σ as an em dash before week four and label the column with its week range. An in-progress week's ideal is provisional and is displayed as such. It is the one number on the page that moves in both directions during the afternoon: a bench player's late points raise it, and a negative play (a fumble, an interception) lowers it.

## 11 · Positional radar

A third view: one team's average output at each of its starting slots, against the league average at the same slot. Axes come from the league's own rules, so the chart reconfigures for a superflex league or one without kickers.

> **Diagram** (see the published artifact): one team vs. the league per slot; the dashed circle is the league, outside is better, inside worse; plotting the ratio makes the reference a circle and prevents a uniform QB-shaped spike.

### Slots, not positions

Sleeper's `starters` array is positionally aligned with the startable entries of `roster_positions`: `starters[0]` plays in the first slot, `starters[1]` in the second, and so on. This fact does most of the work here.

The axes are therefore **slots**, not positions, and no position inference is needed. A `FLEX` axis measures whoever was placed in the flex: the weekly decision at that slot, not the running backs as a group. A superflex league gets a `SUPER_FLEX` axis at no extra cost.

#### The value on each axis

| Step | Definition |
|---|---|
| Team, slot, week | Points from all slots of that type ÷ the number of such slots |
| Team, slot, season | Mean of the weekly values over regular-season weeks played |
| League, slot, season | Mean of every team's season value at that slot |
| Plotted | Team season value ÷ league season value |

Divide by the slot count. Otherwise a league with two RB slots and one QB slot makes every team's RB axis ≈ 2× its QB axis, a fact about the rules, not the roster.

Average first, then divide. Averaging per-week ratios fails on a week when the whole league's kickers scored two points between them: a near-zero denominator turns one bad Sunday into a permanent distortion.

### D10 — Plot the ratio to league average, never raw points

Chosen: every axis is a ratio centred on 1.0; the league average is a perfect circle.

Raw points make the chart useless while the result still *looks* like data. QB slots average ≈ 20 points a week; kickers and defences ≈ 7. On shared radii every team draws the same QB-shaped spike with dents at K and DEF; that shape is a property of football, not of the team.

The ratio removes the positional baseline and leaves only the variation between managers. It also makes the league average a circle, so "inside" and "outside" read instantly with no legend.

**What the ratio loses** is spread. 1.15× at QB, where every team sits within 10% of the others, is a larger edge than 1.15× at DEF, where weekly variance is large. Offer percentile rank within the league as a toggle for the fairer comparison. Keep the ratio as default, because people can explain it to each other.

> **Risk R2 · the shape is partly an artefact of the drawing**
>
> Radar charts have two known distortions; both apply here. Area grows with the *square* of the radius, so a team 20% better across the board encloses ≈ 44% more area and reads as dominant. The silhouette depends on axis order: the same seven numbers in a different order produce a visibly different "personality", although the ordering carries no meaning.
>
> Mitigation: fix the axis order to `roster_positions` order and never re-sort it, so the shape is stable and comparable between teams and seasons. Never label or describe enclosed area as meaningful. Ship the alternative below for reading actual values.
>

### D11 — One team at a time, with a diverging bar chart as the honest alternative

Chosen: the radar shows a single team against the reference circle; a toggle switches the same data to a horizontal diverging bar chart.

A dozen overlaid polygons are unreadable; three are difficult. The radar's strength is a single team's memorable silhouette, lost once a second shape crosses it. Allow a second team for a head-to-head; cap it there.

The bar chart is the concession to accuracy. The data is a signed deviation from a baseline across seven categories; its canonical form is a diverging bar chart centred on zero, not a radar. Bars are read against a common linear axis, so values compare directly, with no distortion from area or ordering.

The radar stays the default: this is a page for a fantasy league, not an analytics console, and "what shape is my team" is a question people want answered. Both views use one data set.

### League rules, season by season

Existing storage covers this. Each season is its own `League` row with its own `RosterPositionsJson`, keyed by a unique `Season`, so a 2024 radar uses 2024's slots and a 2026 radar 2026's. Rules that change between seasons are handled by construction.

Rules that change *within* a season are assumed not to occur. Sleeper permits it, and the failure is silent: `starters` stops lining up with the stored slot list and every week is attributed to the wrong axis. Verify the assumption at finalize time:

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

A stated assumption gets a cheap assertion. The week loses its radar contribution and logs why; the points and ideal columns, which do not depend on slot alignment, are unaffected.

> **Verify at T02**
>
> The positional alignment of `starters` is load-bearing for this section and is observed Sleeper behaviour, not a documented guarantee. T02 confirms it against a real matchup response before anything depends on it: fetch a week, and check that `starters[i]` is a player eligible for `roster_positions[i]` for every startable slot.
>

## 12 · Repo & CI/CD

```
src/
  FantasyLeague.Core/          ideal-lineup solver, consistency maths, store interfaces — no Azure deps
  FantasyLeague.Sleeper/       typed client, rate-limit handler, DTOs, the reader that maps them (A22)
  FantasyLeague.Seasons/       season assembly, player map refresh, payload and ETag (A22)
  FantasyLeague.Data/          EF Core context, migrations, every blob store (A21)
  FantasyLeague.Cli/           the console harness
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

`Core` has no Azure dependency; this is the purpose of the layout. Every substantive bug in this project lives in the solver or in σ's edge cases. Both are pure functions over fixtures, testable offline in milliseconds with no emulator, connection string or network.

The σ tests cover absence: zero, one, three and four weeks; a week with a true zero score; a week excluded for not being final. Each returns null or a defined number for a stated reason. None throws.

#### Pipeline

- **OIDC federated credentials** between GitHub and Entra ID; no publish profiles or service-principal secrets in the repo. The highest-value day-one item, because retrofitting it requires rotating whatever was used instead.
- **Bicep `what-if` on pull requests**, posted as a comment. Infrastructure diffs are reviewed like code diffs.
- **EF migrations as a deploy step.** The SQL firewall opens to the runner IP and closes again in an `always()` step, so a failed migration cannot leave the database exposed.
- **SWA preview environments** on pull requests: free on the Free tier, and the fastest way to review a chart change before merging.

## 13 · Cost

**Corrected: the Functions row, the ingest cap, a missing resource, and the storage account's real cost (A30).**

| Resource | Tier | Headroom | Monthly |
|---|---|---|---|
| Static Web Apps | Free | 100 GB bandwidth, custom domain + TLS | $0.00 |
| Functions | **Flex Consumption (FC1)** | Free grant; this app uses ~14% of the execution allowance and ~4% of the GB-s | $0.00 |
| Storage account | Standard_LRS | Required by Functions for runtime state and the deployment container; also holds the 840 KB player map (A12) and the season's finalized weeks (A16). No free grant: the host's transactions are the bill | **≈ $0.15, measured** |
| Azure SQL | Serverless, free offer | 100k vCore-s, 32 GB — off the request path since A16 | $0.00 |
| Log Analytics + App Insights | Pay-as-you-go | 5 GB/mo free — **cap ingest at 0.16 GB/day, not 1 GB/day** | $0.00 |
| Exception alert | Metric alert, one time series | Emails on any exception the Function App throws | ≈ $0.10 |
| Steady state | | | **≈ $0.25** |

**The original 1 GB/day figure was wrong by 6×.** 5 GB/month free ÷ 31 days = 0.16 GB/day. A 1 GB/day cap permits 31 GB a month and bills 26 of them. The deployed workspace is capped at `0.16` with 30-day retention.

A budget alert fires at $0.50 of actual spend and on a forecast breach of the $1 budget. **Corrected (A31): it is deployed once by hand** from `infra/budget.bicep`, not by the pipeline, because a budget's start date cannot change after creation.

**Corrected: the bill is not zero.** The storage account has no free grant, and the Functions host reads and writes it continuously: ≈ $0.15 measured in September. Dropping the blob layer would not change this; Functions needs the account regardless. An optional custom domain adds ≈ $12/year; the TLS certificate is included.

Configure two settings explicitly rather than assuming them: the App Insights daily ingest cap, and the SQL free-tier limit behaviour from D7. Each separates a free hobby project from an unexpected bill.

## 14 · Open questions

**Five of the eight were settled on 2026-09-10 against the live league:** Q1, Q2, Q3, Q7 and Q8. Answers and consequences are in [`amendments.md`](amendments.md). Q5 was settled later, at four weeks, and Q4 on 2026-10-13: regular season only (A33). Q6 is tracked in [`tasks/todo.md`](../tasks/todo.md).

- **Q1 answered** — six rosters; eleven startable slots (`QB, QB, RB, RB, WR, WR, TE, FLEX, SUPER_FLEX, K, DEF`) plus eight `BN`. The league ID is a secret (A2).
- **Q2 answered** — SUPER_FLEX yes, IDP no. The eligibility map stays general anyway (A3).
- **Q3 answered** — moot; the league has no `IR` or `TAXI` slots. The default below stands if one appears.
- **Q7 answered** — 2026 is the first season. A null `previous_league_id` is legitimate; no `SeasonOverrides` are needed (A4).
- **Q8 answered** — eight distinct slots, readable as a radar. No axis merging needed.

Original text of all eight, retained for the reasoning behind the open ones:

- Q1 · **League ID and `roster_positions`.** Required to pin solver fixtures. All of §05 depends on the slot list.
- Q2 · **Does the league use SUPER_FLEX or IDP slots?** Determines which parts of the eligibility map are live code and which are dead branches. IDP raises the solver's slot count, not enough to threaten the DP.
- Q3 · **Should players in IR slots count toward ideal points?** Default: no; ideal is the best legally startable lineup. Settle it early; a league disputes exactly this in December.
- **Q4 answered (A33)** — regular season only; fantasy playoffs and the NFL postseason are out of scope. The question as asked: **Regular season only, or through the playoffs?** Sleeper's playoff weeks have a different matchup structure, and only some teams play. A league decided on total points is assumed to want regular season only. σ would be badly distorted by weeks where half the league does not play, and the chart's x-axis depends on the answer.
- **Q5 answered** — four, per D1; `SpreadCalculator.MinWeeks` is the one place it lives. The question as asked: **Minimum weeks before σ appears — four, or five?** Four is assumed throughout this document. Five buys another week of noise reduction at the cost of a longer empty column early in the season.
- Q6 · **Should the table show σ or CV?** D3 chooses σ for legibility, with CV on the chart. If the scoring spread is wide (a gap of ≥ 30 points between the highest and lowest team means), CV is the fairer default and D3 should be revisited.
- Q7 · **Is the league’s season chain intact?** Follow `previous_league_id` back from the current league and count the hops. A null before the league’s first season means those years need explicit `League:SeasonOverrides` entries, with the old IDs retrieved from Sleeper by hand. Cheap to check now; costly to discover later.
- Q8 · **How many distinct starting slots does the league have?** 7–8 axes make a readable radar; a superflex-plus-IDP league can reach 11–12, where labels collide and the shape becomes unreadable. If the count is high, merge the IDP slots into one axis, or make the D11 diverging bar chart that season's default view.

## 15 · Build order

Ordered highest-risk, lowest-cost first. The solver is the only place a real bug can hide, and proving it needs no Azure account.

**Status at 2026-09-13: M0, M1 and M2 are built and deployed.** The ordering held: the build's one real bug was in the solver, as predicted here, and fuzzing against exhaustive search caught it before any cloud account existed. M3 is next.

- M0 · Core, offline — One hour of curl against the real league first, settling Q1 to Q4 and Q7 and capturing fixtures; then the ideal-lineup solver and the σ function. Done when the solver beats greedy on a constructed flex case, handles a bye-week hole, and σ returns null for every count below four.
- M1 · Sleeper client — Typed client, token-bucket handler, single-flight cache. Proven by a console app that prints the real league's standings and asserts both §03 invariants across a whole season, where the assumptions M0 confirmed by eye meet running code.
- M2 · API on Azure — Functions HTTP with an in-memory season, deployed by bicep over OIDC. `/api/season` live and correct for the current week; no database yet.
- M3 · Persistence — SQL, EF migrations, the finalize timer, the daily player refresh and the season-chain walk. Backfill the current season, then each prior season as a one-shot job.
- M4 · The page — Table with the inline range mark, then the chart. The first milestone others in the league can see, and the first to generate opinions.
- M5 · Chart modes — Per-week, vs-median and CV toggles. Last on purpose: cheap, the most taste-dependent, and easier to judge against a season of real data than against three weeks.
- M6 · Positional radar — Slot attribution, the league reference, and the radar itself (§11). Last because it is the only feature that depends on an unverified property of Sleeper’s payload, and the one most improved by a full season of data.
- M7 · Motion — optional — Table layout animation, chart reveals, section transitions and a football loader. 6.5 h with no functional gain, listed separately so that cutting it is a decision, not an omission.
