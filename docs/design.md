# Ideal Points — Design

A live standings board for my Sleeper fantasy league. We compete on total points over the season, so that's what the board ranks on. Beside it sit two extra stats: how steady each team's scoring is, and how many points each team left on the bench.

This document describes the system as it's built. The history of how it got here (what I got wrong, and why it changed) is in [`amendments.md`](amendments.md), A1–A35. Section numbers and the D/R/Q labels are referenced from the code and the other docs, so they stay fixed.

- **Data:** the Sleeper API
- **Stack:** .NET 10 Azure Functions, React, Azure SQL and blob storage
- **Cost:** about $0.25 a month, with an alert at $0.50 (§13)
- **Sleeper load:** about 4.4 requests a minute per instance, capped at 25 (§07)
- **League:** six rosters, five of them scoring. One is a placeholder team and is excluded from everything (A1)
- **Status:** built and deployed, including past seasons and the season picker (A34). Next up: counting the regular season only (T64)

## 01 · Scope

One public, read-only page for one league: a standings table and some charts, updating live during games.

The league is decided on **total season points**, the sum of every week's actual score. Consistency and ideal points are just commentary; neither decides anything.

Out of scope: logins, writes of any kind, head-to-head matchups, playoffs (fantasy or NFL, A33), trades, and anything needing a Sleeper account. The site reads one league ID from configuration and nothing else.

Sleeper's API has no projections, so there's no projected-points column and no scoring engine. Sleeper's own scored points are used as-is.

## 02 · The three metrics

| Column | Definition | Better |
|---|---|---|
| **Points** | Sum of each week's score, live week included | Higher |
| **Consistency σ** | Population standard deviation of finalized weekly scores | Lower |
| **Ideal** | Sum of the best legal lineup from each week's roster | Higher |

σ runs the other way to the other two (lower is better), so it gets its own panel rather than sharing a column style with them (A17). It doesn't affect who wins: a 190/62 season ties a 126/126 one. It answers "how did you get here", not "are you winning".

### D1 — Population σ over finalized weeks only, blank below four

- **Population, not sample.** The weeks played are the whole population I care about, not a sample of some larger set. Dividing by *n*−1 would just inflate σ early in the season, when it's least reliable anyway.
- **Blank below four weeks.** σ over two weeks is just the gap between them. Showing `0.0` in week one would be false, so the code returns null and the page shows a dash.
- **Finalized weeks only.** See R1.

> **Risk R1 · the live week would poison σ.** A team sitting on 41 points at 2pm on Sunday looks like a huge outlier, and σ would swing all afternoon while nothing real happened. So σ only counts weeks the Tuesday finalize job has marked final, and the panel says which weeks it covers ("Weeks 1–6") while the points include week 7.

### D2 — Ideal uses the roster as it stood that week

Each week is solved against that week's `matchup.players`, not today's roster. Otherwise every waiver claim would rewrite history: a player dropped in week 4 would vanish from the week 2 ideal.

## 03 · Sleeper data

Sleeper's read API is free and needs no key. Everything I use is in the documented `/v1` endpoints.

| Endpoint | What I use from it | Cache |
|---|---|---|
| /v1/state/nfl | Current season, week and season type | 5 min |
| /v1/league/{id} | `roster_positions` (the lineup slots), `previous_league_id` | 24 h |
| /v1/league/{id}/users | Team names | 1 h |
| /v1/league/{id}/rosters | Roster → owner | 5 min |
| /v1/league/{id}/matchups/{wk} | `points`, `starters`, `players`, `players_points` | 15 s |
| /v1/players/nfl | Each player's `fantasy_positions`, for the solver | 24 h |

- **Team names** come from `users[].metadata.team_name`, falling back to `display_name`. An unset name is a missing key, not an empty string (A6).
- **The player list is 14.65 MB.** A daily timer trims it down to an 840 KB map of `{ playerId → name, team, positions }` and saves that to blob storage. Every new instance loads the map from there, because instance memory doesn't survive scale-out (A12). It's never fetched on a request.

#### Two checks that come for free

There's no scoring engine to test against, so the data checks itself on every load:

- **The starters add up to the team's score**, to within a cent. This catches a misaligned `starters` array.
- **Ideal ≥ actual** for every team-week. The lineup the manager actually started is one of the options the solver considers, so a violation means a solver bug, most likely an eligibility rule. Both checks compare against Sleeper's scored `points`, not a commissioner override, since an override can be any number (A7, A9).

Failures are reported on the page, never thrown. So are players the map doesn't recognise (A8): dropping an unknown player would quietly lower that team's ideal, and ideal ≥ actual can't catch an undercount.

## 04 · Consistency

σ is a single season-to-date number, so charting it week by week needs a choice:

| Series | Value at week *w* | Reads as |
|---|---|---|
| **Expanding** (default) | σ over weeks 1…*w* | Settles down as the season goes on |
| **Rolling 4** | σ over the last four weeks | Whether a team is getting steadier or wilder right now; noisy early on |

Expanding is the default because rolling is close to meaningless before about week eight, and a chart that's jagged on first load teaches people not to trust it.

### D3 — Raw σ by default, coefficient of variation as an option

σ is in points, which is what people actually talk about ("I swing about twenty-five a week"). But it isn't fair across teams that score at different levels: a team averaging 140 with σ 25 is relatively steadier than one averaging 90 with σ 22. CV (σ ÷ average) fixes that, so the chart offers it as a toggle.

#### The range mark

A bare σ is hard to read, so each row in the consistency panel has a mark: a line from the team's worst week to its best, a band one σ either side of its average, and a tick at the average. Every mark is centred on its own team's average, so the widths compare directly (A33). It costs nothing extra: min, max and mean come from the same weekly scores as σ.

## 05 · Ideal lineup

Given a week's rostered players and their points, find the legal lineup with the highest total. The slots are `roster_positions` minus `BN`, `IR` and `TAXI`. Eligibility comes from each player's `fantasy_positions`.

This is the one genuinely tricky calculation in the project, and the most likely place for a real bug.

### D4 — Exact assignment, not greedy

Greedy (best player into the best open slot) is wrong whenever there's a flex slot: putting the top RB in FLEX can strand the next RB with nowhere to go. The whole point of the metric is points left on the bench, so an answer that undercounts is worse than none.

I used a bitmask DP over the filled slots. With about 11 slots and 20 players, that's 2¹¹ states and roughly 450k operations per team-week, well under a millisecond. Brute force would be about 6.7 trillion assignments (20!/9!). Hungarian or min-cost flow would also be exact, but the DP is about twenty lines with no dependency.

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

`dp.Max()` rather than the full mask, because a bye-heavy week can leave a slot unfillable.

#### Slot eligibility

| Slot | Accepts |
|---|---|
| QB / RB / WR / TE / K / DEF | That position only |
| FLEX | RB, WR, TE |
| SUPER_FLEX | QB, RB, WR, TE |
| REC_FLEX | WR, TE |
| WRRB_FLEX | RB, WR |
| IDP_FLEX | DL, LB, DB |

This league uses SUPER_FLEX and no IDP, but the map stays general because the slot list can change each season (A3). The solved lineup is stored with the number, so a disputed ideal can be answered with the exact players.

The tests use real captured weeks, a constructed case where greedy loses, and a bye-week hole. The solver was also fuzzed against exhaustive search, which caught the build's one real solver bug.

## 06 · Architecture

### D5 — Static Web Apps + Azure Functions, not a container app

The page is on Static Web Apps (free tier). The API and the scheduled jobs are .NET 10 isolated Functions on **Flex Consumption**. I planned the classic Consumption plan, but my subscription had zero quota for it.

Timers decided it: weeks are finalized on Tuesdays and the player map refreshes daily, and Functions gives you timer triggers for free. Flex scales to zero on its own, and the free Static Web Apps tier includes a custom domain with TLS. The cost of the split is CORS and two deploy targets. Linking them would need the $9/month Static Web Apps tier, which is most of the budget.

### D6 — Polling, not websockets

The page polls every 30 s, but only while the tab is visible and an NFL game window is open. Scores can't change faster than the server's 15 s cache anyway, so a push channel would gain under 30 s, at the cost of a SignalR resource and a reconnect path. Polling also survives sleep, network changes and phones backgrounding the tab.

## 07 · Request budget

Steady state on a Sunday afternoon. Because every read goes through the server, the number of viewers doesn't change this.

| Call | Cache | Requests/min |
|---|---|---|
| matchups (current week) | 15 s | 4.0 |
| state/nfl | 5 min | 0.20 |
| rosters | 5 min | 0.20 |
| users | 1 h | 0.02 |
| league | 24 h | <0.01 |
| players/nfl | 24 h | <0.01 |
| finished weeks | memory 6 h, then blob, then SQL | 0 once stored |
| past seasons | weeks from SQL; league, users, rosters and the chain walk 24 h (A34) | <0.01 per season |
| **Total** | | **≈ 4.4** |

A 15 s matchup cache plus the 30 s poll means a score is at most 45 s old.

**Every number is per instance.** Caches live in each instance's memory, and so does the rate limiter. Flex Consumption can't be capped below 40 instances, and 40 × 25 = 1,000 a minute, which is Sleeper's guidance. That's why the limit is 25 per instance (A28).

Two situations cost more:

- **Between Sunday night and Tuesday's job**, the just-finished week isn't stored yet, so it's fetched like the live one: about 8.4 a minute.
- **If the blob and the database both fail**, every finished week is rebuilt from Sleeper. By week 7 that exceeds the limit, which then starts refusing requests. Failed reads are never cached, so it recovers as soon as storage does.

Two things enforce the budget, and the second is the real guarantee:

- **Single-flight caching.** If several requests miss the same cache key at once, they share one fetch. Without this, a cache expiry under load turns 2 a minute into 200.
- **A token bucket on the Sleeper `HttpClient`.** Every Sleeper call goes through it, from the HTTP and timer triggers alike, so even a retry bug or a bad cache key can't exceed 25 a minute. A 429 gets retried with exponential backoff and jitter, and the policy gives up rather than queueing forever.

## 08 · Data model

One table:

```
WeekScore(LeagueId, Season, Week, RosterId,
          ActualPts, IdealPts,
          StartersJson, PlayersJson, IdealLineupJson, SlotAveragesJson,
          IsFinal, ComputedUtc,
          PK (Season, LeagueId, RosterId, Week))
```

- **Why one table (A19):** player info lives in the blob map, team names and owners come from Sleeper, and past league IDs come from walking the chain (A34). Nothing else needed storing.
- **`PlayersJson`** keeps each player's name, positions and points, so a week can be re-solved after a solver fix without going back to Sleeper.
- **The key leads on `Season`**, because every read is for one season. `LeagueId` is almost constant here.
- **σ is never stored.** It changes for every team whenever a week is finalized, so a stored copy would go stale silently. It's computed on read.
- **`SlotAveragesJson`** holds that week's points per slot for the radar (§11).

A season is about 6 × 18 = 108 rows, a few hundred KB.

### D7 — Azure SQL serverless on the free offer, with blob in front

The free SQL offer gives 100k vCore-seconds a month, so it has to auto-pause: running continuously would use about 1.3M. A paused database takes 30–60 s to resume, which the first visitor after an idle hour had to sit through. That wasn't acceptable.

So the Tuesday finalize job writes the season to SQL **and** to a blob, and requests read the blob first (A16). The two have different jobs (A20): the blob caches the current season, and SQL is the permanent record of every season. A read that falls back to SQL writes the blob, so it repairs itself after a deploy. Past seasons still come from SQL, and occasionally pay the resume.

The free tier is set to **pause when the limit is hit**, not to continue and bill.

## 09 · API contract

One endpoint per season. The table and the charts all come from the same payload, so they can't disagree.

```jsonc
GET /api/season             → current season
GET /api/season?season=2025 → a completed season

200 OK
Cache-Control: public, max-age=5                     # current season
Cache-Control: public, max-age=31536000, immutable   # completed season
ETag: W/"2026-wk7-…"

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
      "slotAverages": {
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

GET /api/seasons          (cached a day, with an ETag)

{ "current": 2026, "available": [2026, 2025] }
```

- **The league isn't a parameter.** It's one configured value, `League:CurrentLeagueId`. A league ID in the query string would turn the API into a free proxy for any league: an open relay against Sleeper's rate limit, on my subscription.
- **Totals are computed on the server**, even though the page could work them out from `weeks`. σ has one definition, and it should live in one place.
- **A matching `ETag` gets a 304**, so a poll that finds nothing new costs almost nothing.

### D8 — Configure one league ID and walk back for the rest

Sleeper gives a league a new ID every season, linked to the previous one by `previous_league_id`. So `?season=2025` needs a map from year to league ID.

I configure only the current league. `SeasonChain` follows `previous_league_id` back from it and caches the result for 24 h (A34), with guards against loops and runaway depth. That's one Sleeper call per season of history per day, and past league IDs never get stored. A completed season's names and slots come from Sleeper for that year's league ID, so names read as they were that year; its weeks come from SQL.

There's no `next_league_id`, so the August rollover is manual: update the GitHub secret and redeploy (runbook). The chain only exists if the commissioner used Sleeper's rollover. A league recreated from scratch would need a year → ID override map, which is deferred until it's needed (T65).

#### Serving a completed season

- A finished season can't change, so it's cached for a year with `immutable`. The live season gets 5 s, to stay close to the games.
- An unknown year, or one with nothing stored, returns **404 with the list of available seasons**. Never an empty payload, and never a backfill inside a request: a backfill is about 21 Sleeper calls and would leave a visitor waiting around 20 s.
- On a completed season, `finalThroughWeek` is the last week.

## 10 · Frontend

Vite, TypeScript, React and D3, with a firm line between React and D3.

### D9 — React owns the DOM; D3 is only for maths

I use D3's scales, line generators and tick logic, and render the results as JSX. Never `d3.select().append()`: running two libraries against the same DOM nodes is the classic source of stale-render bugs.

#### Chart controls

- **Season:** from `/api/seasons`, so a new year appears on its own. The picker is hidden until there are two seasons (A34). Choosing a past season stops polling and swaps "updated" for the season's final week.
- **Teams:** multi-select, all on by default, each team keeping its own colour everywhere (A25).
- **Metric:** points, ideal or σ. Only one at a time, because σ's scale would flatten into the bottom of a shared axis.
- **Mode** for points and ideal: running total, per week, or vs the league median. For σ: expanding or rolling, raw or CV (D3).

#### The median mode is worth it

Subtracting the league's weekly median doesn't change any ranking, but it removes the shared upward trend. Five near-parallel climbing lines are hard to read; the same five spreading out from zero aren't. About four lines of code.

#### The table comes first

The standings table works with no interaction: tabular figures, the sorted column marked, and the gap to first shown. Hovering a team in the chart highlights it and mutes the rest.

#### Motion

Optional by rule: the page has to work fine with motion off, and it respects reduced-motion settings. It uses Motion, which is declarative, so D9 holds. The one animation that earns its place is the table re-sorting: rows sliding past each other on a Sunday overtake make the live updates visible. Entrance animations only run on first load, or the 30 s poll would replay them constantly. The loading state changes to "Waking the database…" after 3 s.

A live week's ideal is provisional. It's the one number that can move both ways during the afternoon: a bench player's late points raise it, and a negative play lowers it.

## 11 · Positional radar

A third view: one team's average at each of its starting slots, compared with the league average at the same slot. The axes come from the league's own slot list, so a superflex league or one with no kicker just gets different axes.

### Slots, not positions

Sleeper's `starters` array lines up with the startable entries of `roster_positions`: `starters[0]` played the first slot, and so on. So the axes are **slots**, not positions, and nothing needs inferring. The FLEX axis measures whoever was put in the flex, which is the weekly decision that matters.

| Step | Value |
|---|---|
| Team, slot, week | Points from all slots of that type ÷ the number of those slots |
| Team, slot, season | Average of the weekly values |
| League, slot, season | Average of every team's season value |
| Plotted | Team value ÷ league value |

Divide by the slot count, or two RB slots would make every team's RB axis twice its QB axis. Average first, then divide: averaging weekly ratios blows up on the week the whole league's kickers scored two points between them.

### D10 — Plot the ratio to league average, never raw points

Every axis is a ratio centred on 1.0, so the league average is a perfect circle. Raw points would draw the same QB-shaped spike for every team, because that's how football scores, not something the roster did. The ratio leaves only the differences between managers, and inside versus outside the circle reads instantly.

What the ratio hides is spread: 1.15× at QB, where everyone is within 10% of each other, is a bigger edge than 1.15× at DEF.

> **Risk R2 · the shape is partly an artefact of the drawing.** Area grows with the square of the radius, so a team 20% better everywhere encloses about 44% more area. And the silhouette depends on axis order, which means nothing. So the axis order is fixed to `roster_positions` order and never re-sorted, enclosed area is never described as meaningful, and the bar chart below is there for reading exact values.

### D11 — One team at a time, with a bar chart as the honest alternative

Overlaid polygons are unreadable past two or three, so the radar shows one team against the league circle. The **Bars** toggle shows the same numbers as a diverging bar chart centred on 1.0, which is the statistically honest form: bars on a shared axis compare directly, with no area or ordering effects. The radar stays the default because "what shape is my team" is the fun question.

### League rules, season by season

Each season's radar uses that season's own slot list, so rule changes between seasons just work. Rule changes *during* a season are assumed not to happen. If they did, `starters` would stop lining up with the slots and every week would land on the wrong axis without any error. So the finalize job checks that the starter count matches the slot count, and if not, logs it and skips the radar data for that week. Points and ideal don't depend on slot alignment, so they're unaffected.

## 12 · Repo & CI/CD

```
src/
  FantasyLeague.Core/          solver, σ, slot averages, store interfaces — no Azure dependency
  FantasyLeague.Sleeper/       typed client, rate limiter, DTOs and the mapping to Core (A22)
  FantasyLeague.Seasons/       season assembly, season chain, payload and ETag (A22)
  FantasyLeague.Data/          EF Core, migrations, every blob store (A21)
  FantasyLeague.Cli/           console standings, for checking against live data
  FantasyLeague.Functions/     HTTP and timer triggers, wiring
web/                           Vite · React · TypeScript · D3
infra/                         main.bicep per deploy; budget.bicep once by hand
.github/workflows/             ci · deploy · security · pages
```

Core has no Azure dependency, and that's the point of the layout: the bugs that matter live in the solver and σ's edge cases, and both are pure functions I can test offline in milliseconds. The σ tests cover zero, one, three and four weeks, a true zero score, and a week excluded for not being final.

#### Pipeline

- **OIDC** between GitHub and Azure, so there are no stored deploy keys or secrets to rotate.
- **Bicep what-if runs on every PR**, so infrastructure changes get reviewed like code.
- **Deploys run in order:** Bicep, then EF migrations, then Functions, then the web app.
- **Squash merges only**, with Conventional Commit titles checked in CI, so main reads like a changelog.

## 13 · Cost

| Resource | Tier | Notes | Monthly |
|---|---|---|---|
| Static Web Apps | Free | Custom domain and TLS included | $0.00 |
| Functions | Flex Consumption | Uses about 14% of the free execution grant | $0.00 |
| Storage account | Standard LRS | Functions needs it regardless; it also holds the player map and the current season. No free grant, so the host's own traffic is the bill | ≈ $0.15, measured |
| Azure SQL | Serverless, free offer | Off the request path since A16 | $0.00 |
| Log Analytics + App Insights | Pay-as-you-go | Ingest capped at 0.16 GB/day to stay inside the 5 GB free | $0.00 |
| Failure alert | Metric alert | Emails when a request or timer run fails; 404 and 499 don't count (A35) | ≈ $0.10 |
| **Total** | | | **≈ $0.25** |

- **The ingest cap is 0.16 GB a day**: 5 GB free ÷ 31 days. I originally planned 1 GB a day, which would have allowed 31 GB a month and billed 26 of them.
- **A $1 budget alerts at $0.50** of actual spend, and on a forecast breach. It's deployed once by hand from `infra/budget.bicep`, because a budget's start date can't change after it's created (A31).
- **Two settings are set explicitly**, never left to defaults: the ingest cap, and SQL's pause-on-limit (D7). Each is the difference between a free hobby project and a surprise bill.

## 14 · Open questions

All but one are answered; the reasoning is in [`amendments.md`](amendments.md).

- **Q1 answered:** six rosters, eleven startable slots (`QB, QB, RB, RB, WR, WR, TE, FLEX, SUPER_FLEX, K, DEF`) plus eight bench. The league ID is a secret (A2).
- **Q2 answered:** SUPER_FLEX yes, IDP no. The eligibility map stays general anyway (A3).
- **Q3 answered:** moot, because the league has no IR or TAXI slots. If one appears, players there don't count toward ideal.
- **Q4 answered:** regular season only (A33). Enforcing it in code is T64.
- **Q5 answered:** σ needs four final weeks, set in one place (`Spread.MinWeeks`).
- **Q6 open:** should the default be σ or CV? D3 picks σ for readability. If the gap between the highest- and lowest-scoring teams' averages passes about 30 points, CV is the fairer default.
- **Q7 answered:** 2026 is the league's first season, so a null `previous_league_id` is expected (A4).
- **Q8 answered:** eight distinct slots, which is readable as a radar.

## 15 · Build order

I ordered the work by risk, cheapest first. The solver was the most likely place for a real bug, and proving it needed no Azure account. That held up: the build's one real bug was in the solver, and fuzzing against exhaustive search caught it before anything was deployed.

- **M0 · Core, offline:** an hour of curl against the real league to capture fixtures, then the solver and σ.
- **M1 · Sleeper client:** typed client, rate limiter and single-flight cache, proven by a console app printing the real standings and checking both §03 invariants across a season.
- **M2 · API on Azure:** Functions over OIDC with an in-memory season; no database yet.
- **M3 · Persistence:** SQL, migrations, the finalize timer and the daily player refresh.
- **M4 · The page:** the table, then the chart. The first thing the league could see.
- **M5 · Chart modes:** per week, vs median and CV. Saved for later because they're the most a matter of taste.
- **M6 · Positional radar:** last, because it depends on the unverified `starters` alignment and benefits most from a full season of data.
- **M7 · Motion:** optional, and listed separately so cutting it would be a decision rather than something forgotten.
- **Since then:** σ's own panel (T59), past seasons and the season picker (T29, T56). Next: regular season only (T64).
