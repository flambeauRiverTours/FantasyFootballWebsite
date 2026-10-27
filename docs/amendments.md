# Amendments

A decision log. [`design.md`](design.md) describes what's built; this file says why, and what changed along the way. Entries are numbered A1–A35 so they don't clash with the design's D/R/Q labels, and the code cites them.

## Open questions, answered

| | Question | Answer |
|---|---|---|
| **Q1** | League shape | 6 rosters; 11 startable slots (`QB, QB, RB, RB, WR, WR, TE, FLEX, SUPER_FLEX, K, DEF`) plus 8 bench. 2¹¹ = 2,048 DP states, exactly what §05 assumed. |
| **Q2** | SUPER_FLEX or IDP? | SUPER_FLEX yes, IDP no. The eligibility map stays general anyway (A3). |
| **Q3** | Do IR players count? | Moot: the league has no IR or TAXI slots. |
| **Q4** | Playoffs? | Regular season only (A33). T64 enforces it. |
| **Q5** | Weeks before σ shows | Four (D1), in `Spread.MinWeeks`. |
| **Q6** | σ or CV by default? | Open. |
| **Q7** | Is the season chain intact? | 2026 is the first season, so there's nothing to walk yet (A4). |
| **Q8** | Distinct slots | 8, which reads fine as a radar. |

## A1 · One placeholder team is excluded from everything

2026-09-10. One roster is an auto-drafted placeholder, not a competitor.

- It's matched by owner `user_id` from configuration, never by team name or `roster_id`, which both change.
- It's excluded as a row **and** from every league-wide number (the median line, the radar's league averages). Leaving it in a denominator would quietly flatter every real team. Those are two separate filters, and it's easy to write only the first.
- Six rosters minus one leaves five scoring teams. Since A27 the season service drops it, so nothing downstream can count it by accident.

## A2 · League IDs and user IDs are secrets

2026-09-10. Neither the league ID nor any Sleeper user ID is ever committed or logged.

- **This isn't access control.** The data is public once the site is live. The point is not tying a public repo to personal Sleeper accounts, and not handing a scraper a working league ID.
- **Where they live:** user-secrets locally, App Settings in Azure, GitHub secrets in CI. The Bicep parameters are `@secure()`, or the value would sit in the deployment history in plain text.
- **Captured matchups are safe to commit** (no user IDs). A `/users` capture is not.
- **Resource names are kept out too.** Docs look them up with `az ... list` rather than naming them.
- **A real league ID once slipped into a test fixture.** It never reached GitHub: the CI gate caught it on a local branch, and the commits were rewritten. Fixture values are placeholders, even when a real one is to hand.

## A3 · The eligibility map stays general

2026-09-10. This league uses SUPER_FLEX and no IDP, but the slot list can change each season.

- `IDP_FLEX`, `REC_FLEX` and `WRRB_FLEX` stay in the map and stay tested.
- Never hardcode 11 slots, 8 axes or this year's slot order; derive them from that season's `roster_positions`.
- An unknown slot code fails loudly rather than silently dropping out of the ideal.

## A4 · No season chain to walk yet

2026-09-10. 2026 is the league's first season, so a null `previous_league_id` is expected. The first real test of the chain walk is the 2027 rollover. Built anyway (A34), since it costs almost nothing.

## A5 · The `starters` alignment holds

2026-09-10. On the live league, 63 of 63 non-empty starters were eligible for the slot at their own index. That supports the radar (§11), but one league's week isn't a contract, so the finalize job still checks starter count against slot count.

Also confirmed: empty slots are the string `"0"`, defences are team abbreviations, and the starters added up to the team's score for all six rosters.

## A6 · An unset team name is missing, not empty

2026-09-10. For two of six managers, `team_name` is absent from `metadata`, and `metadata` itself can be null. Resolution treats absent, null and blank alike, falling back to `display_name` and then `Roster {n}`. Code following the design's original wording would have shown two blank team names.

## A7 · A commissioner override wins over the scored points

2026-09-10. Matchups carry `custom_points`, which Sleeper shows once a commissioner corrects a score. Actual is `custom_points ?? points`; ignoring it would leave this site disagreeing with the app forever after one correction.

## A8 · An unknown player is reported, never dropped

2026-09-10. The player map refreshes daily, so a newly signed player can be missing for a few hours. Dropping him would lower his team's ideal, and ideal ≥ actual can't catch an undercount. `RosterWeek.UnresolvedPlayerIds` carries him instead, and the page reports it.

## A9 · Both checks compare against Sleeper's scored points

2026-09-11. Since an override can be any number (A7), both invariants use `points`, not the override. Otherwise one correction would raise a violation forever. A generous override can therefore show ideal below actual. That's correct.

## A10 · Excluded owners are one comma-separated secret

2026-09-13. Bicep has no secure array, and a GitHub secret is a single string. So `EXCLUDED_OWNER_IDS` is `id,id,…`, which the template splits into indexed App Settings (`League__ExcludedOwnerIds__0`, `__1`, …). .NET binds those to a list with no custom code. Empty writes nothing, and an ID matching no roster is logged as a count, never the ID itself.

## A11 · The storage firewall stays open

2026-09-13. Trivy flags the storage account's open network rule. Closing it on Flex Consumption needs private endpoints at about $7 a month each, several times the whole budget. The account has no public containers, requires TLS 1.2, and since A30 accepts no keys at all. The exception expires on 2027-03-01, so it has to be argued again rather than forgotten.

## A12 · The player map is stored, because instance memory isn't storage

2026-09-13. The first deployed request returned 500: nothing in the Functions host had loaded the player map. The console did it explicitly, which is why every local run worked.

- A startup hook would only load the map on the instance that ran it, and Flex scales out to forty.
- So the map is saved to blob storage, and any instance adopts a stored map under 24 hours old. A daily timer refreshes it, and a lock stops a burst of cold requests from all fetching the 14.65 MB dump.
- Storage failures are warnings: they make a request slower, never failed.

## A13 · The league doesn't use overrides, so only the actual is stored

2026-09-14. The league treats `custom_points` as cheating. The reading code still handles it (A7, A9), but `WeekScore` stores only `ActualPts`, since a second column would duplicate it in every row. The cost: the invariants can't be re-checked from stored rows later.

## A14 · The stored key leads on Season

2026-09-14. The primary key is the clustered index, and every read is for one season, so it leads on `Season`. `LeagueId` would only help a multi-league service.

## A15 · Two Microsoft packages are exempt from the licence check

2026-09-14. `Microsoft.Data.SqlClient.SNI.runtime` and `Microsoft.Identity.Client.NativeInterop` ship proprietary Microsoft terms as a file. They come with managed-identity access to Azure SQL, and they're Windows-only, so they never load on the Linux host. They're ignored by name, not by widening the allowlist to "any licence file", which would gut the check.

## A16 · Finished weeks are mirrored to blob storage

2026-09-18. The free SQL tier has to auto-pause, and resuming takes 30–60 s, which the first visitor after an idle hour had to wait through.

- **Rejected: caching harder.** The cache is per instance, so a new instance starts cold. And a cache that absorbs every read makes the database look idle, so it pauses sooner.
- **Rejected: a keep-warm ping.** Each warm hour spends the free allowance, and visitors outside the window still wait.
- **Rejected for now: dropping SQL.** It would work, but it's a rewrite, and a smaller fix existed.
- **Chosen:** the Tuesday finalize job writes the season to SQL and to one blob, and requests read the blob first. Nothing a visitor waits for touches SQL.
- **Bug after shipping:** only the Tuesday job wrote the blob, so after every deploy requests fell through to SQL until the following Tuesday. Fix: a read that falls back to SQL now writes the blob, so it repairs itself from the first request.
- The finalize timer is off from March to August. September stays in, because week 1 falls in it.

## A17 · σ leaves the standings table

2026-10-07. In a ~140 px table cell the range mark looked like decoration, and it shipped without the number beside it. Rather than cram it or drop it, σ moved to its own panel (A33). D3's choice of σ over CV still stands, just in that panel.

## A18 · A licence shipped as a file is declared, not exempted

2026-10-08. Apache.Arrow arrived with a licence file the tool couldn't read. Reading it showed Apache-2.0, which is already allowed, so it's declared in `.github/license-overrides.json`, pinned to the version. The next bump fails until someone reads the new file.

## A19 · Three of the four tables are dropped

2026-10-12. `Player`, `Team` and `League` were created and never used. The player map lives in blob storage (A12), team names come from Sleeper, and the season chain turned out not to need a table (A34). Code nothing calls is removed, and the task that needs it rebuilds it.

## A20 · The blob caches the current season; SQL keeps every season

2026-10-12. Should SQL go entirely? No.

- The blob holds one season, with the season stamped inside it. It only answers for that season and only accepts the same or a newer one, so a backfill can't evict the live season.
- A past season is read rarely and cached for a year, so it can afford SQL's resume. The current season is polled every 30 s and can't.
- Cost doesn't decide it, since both are free. SQL also makes cross-season questions a query.
- Known weakness: `WeekScore` stores most of a week as JSON columns. That's right for a store read whole by key, but it's the first thing a reviewer would question.

## A21 · Hosts compose, Data owns storage, policy is applied once

2026-10-12.

- **No library registers a fallback for another library's interface.** Each host registers what it needs, and a missing store fails at startup, not on the first request.
- **Core owns the store interfaces; Data owns every implementation**, with one set of blob rules and one config section.
- **League policy is applied once, when the season is built.** The ETag hashes the response body (minus the timestamp), so any visible change produces a new tag. Before, a hand-kept field list missed two settings and served stale 304s.

## A22 · The Sleeper project splits in two

2026-10-12. `FantasyLeague.Sleeper` keeps the client, rate limit, DTOs and mapper behind one `ISleeperReader` that returns domain types. Everything built on top moved to `FantasyLeague.Seasons`. Dependencies run Core ← Sleeper ← Seasons ← hosts, with Data beside them. Point totals are named `…Pts` to match the table columns, and the blob name carries a schema version so a renamed field becomes a miss rather than a read of zeros.

## A23 · Two C# standards narrowed to what they were for

2026-10-12.

- `Pts` applies to roster totals only. On a single player's score, `Points` reads better.
- Tests may use `!` without a comment, because a wrong null fails the very test that relies on it.

The same pass also stopped logging a Sleeper user ID, switched timing to the injected `TimeProvider`, and routed two records through the mapper.

## A24 · The front end meets its standards

2026-10-12.

- `noUncheckedIndexedAccess` is on, which exposed nine unguarded indexes.
- Calculations moved out of components into `pointCalculation.ts`.
- Reduced motion is respected everywhere.
- Prettier formats the web code, with the style most files already used.
- The 125 KB gzipped budget for initial JavaScript is enforced in CI. It stood at 112 KB.
- The tab title is the league's name.
- Below a 600 px chart the line-end names are dropped, since the team chips above it are the legend.

## A25 · A team keeps its colour, and polling knows the whole NFL week

2026-10-12.

- **Colour is assigned once, by roster ID.** It used to follow rank, so a team changed colour when overtaken, and a tie could give it two.
- **Game windows cover every slot the NFL actually plays:** London mornings, December Saturdays, Christmas, Black Friday and Thanksgiving, which is now worked out by date. 24 tests sit either side of the window edges.

## A26 · Unused code waits for its task, and a failure is a panel

2026-10-12.

- Code waiting for a future task is removed; git history keeps it, and the task says what to rebuild.
- The `?radar` debug switch is gone.
- A failed first load renders as a panel like every other state, instead of being announced twice outside the layout.

## A27 · Data and integrations meet their standards

2026-10-13.

- **The season blob carries its write time.** A blob older than the latest scheduled finalize counts as a miss, so a failed Tuesday blob write can't hide the new week for seven days. The timer and the check share one schedule definition.
- **The store refuses an unfinished week**, so a live week can't be frozen mid-game.
- **The season service drops the placeholder team** after running the invariants over it.
- **Cache lifetimes:** `/api/season` stays at 5 s on purpose, to stay close to live; `/api/seasons` caches for a day.

## A28 · Hashed blob names, a smaller bucket, a renamed column

2026-10-13.

- **Blob names use a hash of the league ID**, because names show up in the portal and in logs. It hides the ID from a casual reader, which is all it's for. SQL keeps the plain ID, since only the app's identity reads it.
- **The token bucket is 25 a minute per instance, not 50.** Flex can't be capped below 40 instances, and 40 × 25 = 1,000, which is Sleeper's guidance. Normal load is about 4.4. A limiter shared across instances would add a network call to every request.
- **`PlayersPointsJson` is now `PlayersJson`**, renamed in place. For the few minutes between migration and deploy, a request that missed both caches could 500. That was judged too short to warrant a two-step migration.

## A29 · Stored weeks keep their lineups, in one shape

2026-10-13. Starters, ideal lineup and players stay in every stored week even though nothing reads them today, so a solver fix can recompute a finished season without asking Sleeper again. The blob and SQL store a week the same way, through one converter.

## A30 · No stored keys, and a fifty-cent alert

2026-10-13.

- **No credential is stored anywhere.** The web deploy signs in through OIDC, and the Function App reaches storage through its managed identity, with shared keys turned off. The CI identity can assign only those three storage roles; a broader grant would let a compromised pipeline make itself Owner.
- **The alert fires at $0.50 of a $1 budget.** September's bill was $0.15, all storage, so "costs nothing" was false, and a one-cent alert would fire monthly and be ignored.
- **One metric alert on exceptions,** at about $0.10 a month. A log alert on invariant breaches would cost about $0.50 a month on its own.
- **Accepted:** SQL allows connections from Azure services. Flex has no fixed outbound address, and Entra-only sign-in is the actual control.

## A31 · The budget leaves the pipeline, and two scripts go

2026-10-13.

- **The budget is deployed once, by hand,** because its start date can't change after creation. That had cost a parameter, a duplicated lookup and one failed deploy. The trade-off: a budget deleted by hand isn't restored by the next deploy.
- **The cost-check script is gone.** The template sets every value on each deploy, and what-if shows any drift.
- **Migrations open no firewall rule.** GitHub's runners are in Azure, so they're already allowed. Tested twice before removing the steps.
- **The template stops restating Azure defaults,** except security ones, so a change in Azure's defaults can't loosen them.

## A32 · The supply chain covers the web app too

2026-10-13.

- **The licence allowlist covers npm,** including dev dependencies, so a new licence surfaces when Dependabot proposes it. Copyleft licences (MPL-2.0) are allowed for build tools only.
- **npm vulnerabilities fail on production packages only.**
- **Every API response names its build** (`X-Build`), and the deploy's smoke test waits for this commit. The old check could pass against the previous build.
- **The web deploy stays parallel** with migrations and functions.

## A33 · A consistency panel, and the regular season only

2026-10-13.

- **σ gets its own panel, steadiest first:** one row per team, with the weekly range, a ±σ band and the mean, beside the σ figure. It's absent, not empty, below four final weeks.
- **Each mark is centred on its team's mean,** so widths compare straight down the column. The scale reaches the widest range or ±σ, whichever is further, because a skewed team's band can pass its own extremes.
- **Contrast is measured:** a soft fill alone reached 1.4:1. The outlined band reaches at least 3.5:1 in both themes.
- **Q4: regular season only.** Not yet enforced in code (T64).

## A34 · Past seasons, and the season picker

2026-10-13.

- **The chain is walked on demand and cached for 24 h,** with loop and depth guards. No table, and past league IDs stay out of the database.
- **A past season is its stored weeks plus that year's league metadata,** so names read as they were. It's cached for a year. An unknown year returns 404 with the available list, never a backfill inside a request.
- **The rollover is a hand edit of the GitHub secret, then a redeploy.** The runbook used to say to edit the Azure setting, which the next deploy would silently overwrite.
- **A year → league ID override map is deferred** (T65); this league's chain is intact.
- **The picker is hidden until there are two seasons.** It stays visible on phones.

## A35 · The alert fires on failures, not on exceptions

2026-10-27. The exception alert emailed about a "crash" on a Tuesday when the finalize job had worked: the database was waking from pause, threw once, and EF's retry stored the week 13 s later. Over the previous 30 days it saw 34 exceptions across about two weeks' worth of days. Almost all were host shutdowns on scale-in, timeouts, cancellations and that wake-up, none of them a failure anyone could act on.

- **It now watches failed requests** (`requests/failed`), which covers HTTP calls and timer runs alike, so a run that really fails still emails. Same price: one metric, one time series.
- **404 and 499 are left out.** In those 30 days every failed request was one or the other: bots asking for `robots.txt` and `favicon.ico`, and browsers closing the page before the API answered.
- **Renamed** to `alert-idealpoints-failures`. The deploy doesn't remove the old exceptions alert, so it's deleted once by hand.

Found in the same logs:

- **The finalize log's "Next:" showed the run in progress.** The timer's `ScheduleStatus.Next` is that run's own start, so the log now asks `FinalizeSchedule.NextRunAfter`, which reads the same schedule as the timer.
- **The placeholder team (A1) logged an alignment error every live week.** Its lineup is never set, so its starters never match the slots; it was logged 53 times on one Sunday. It's still solved and checked by both invariants, but no longer reported, since it never reaches the radar.

## What the original design got wrong

The design now states what's true. These are the mistakes worth remembering.

- **The player dump is 14.65 MB, not about 5.** That matters for memory and timeouts, not cost.
- **"Cache it" wasn't enough.** Instance memory doesn't survive scale-out, and it was the one design gap that reached production (A12).
- **Team names and overrides** were described wrongly (A6, A7, A9).
- **The Consumption plan couldn't be provisioned**, so the app runs on Flex Consumption.
- **The request budget was ≈ 2.3/min as designed.** It's ≈ 4.4 as built, and every figure is per instance.
- **The cost table missed the storage account entirely,** and its 1 GB/day ingest cap was six times the free allowance.
- **Twelve teams were assumed;** there are five scoring.
- **Four tables were planned and one was used** (A19). **The blob layer cut from v1 came back** (A16), for latency.
- **σ in a table cell didn't work** (A17).
