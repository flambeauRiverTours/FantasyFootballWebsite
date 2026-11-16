# Amendments

A decision log. [`design.md`](design.md) describes what's built; this file says why, and what changed along the way. Entries are numbered A1–A43 so they don't clash with the design's D/R/Q labels, and the code cites them.

The open questions, Q1–Q15, and their answers are in [design §14](design.md#14--open-questions).

## A1 · One placeholder team is excluded from everything

2026-09-10. One roster is an auto-drafted placeholder, not a competitor.

- It's matched by owner `user_id` from configuration, never by team name or `roster_id`, which both change.
- It's excluded as a row **and** from every league-wide number (the median line, the radar's league averages). Leaving it in a denominator would quietly flatter every real team. Those are two separate filters, and it's easy to write only the first.
- Six rosters minus one leaves five scoring teams. Since A27 the season service keeps it out of the counted teams, so nothing downstream can count it by accident. Since A39 its weeks are stored too, in a list of their own, because a game against it counts in head-to-head, and its score sets the median game's median, as Sleeper's does.

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
- A past season is read rarely and cached for a year (a day since A38), so it can afford SQL's resume. The current season is polled every 30 s and can't.
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
- Calculations moved out of components into `pointCalculation.ts` (now `calculations.ts`).
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
- **Migrations open no firewall rule.** GitHub's runners are in Azure, so they're already allowed. Tested twice before removing the steps. Reversed by A40: from 2026-11-15 a runner can be on AWS.
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
- **Q4: regular season only.** Enforced in code by T64 (A37).

## A34 · Past seasons, and the season picker

2026-10-13.

- **The chain is walked on demand and cached for 24 h,** with loop and depth guards. No table, and past league IDs stay out of the database.
- **A past season is its stored weeks plus that year's league metadata,** so names read as they were. It's cached for a year (a day since A38). An unknown year returns 404 with the available list, never a backfill inside a request.
- **The rollover is a hand edit of the GitHub secret, then a redeploy.** The runbook used to say to edit the Azure setting, which the next deploy would silently overwrite.
- **A year → league ID override map is deferred** (T65); this league's chain is intact.
- **The picker is hidden until there are two seasons.** It stays visible on phones.

## A35 · The alert fires on failures, not on exceptions

2026-10-27. The exception alert emailed about a "crash" on a Tuesday when the finalize job had worked: the database was waking from pause, threw once, and EF's retry stored the week 13 s later. Over the previous 30 days it saw 34 exceptions across about two weeks' worth of days. Almost all were host shutdowns on scale-in, timeouts, cancellations and that wake-up, none of them a failure anyone could act on.

- **It now watches failed requests** (`requests/failed`), which covers HTTP calls and timer runs alike, so a run that really fails still emails. Same price: one metric, one time series.
- **404 and 499 are left out.** In those 30 days every failed request was one or the other: bots asking for `robots.txt` and `favicon.ico`, and browsers closing the page before the API answered.
- **Renamed** to `alert-idealpoints-failures`. The deploy doesn't remove the old exceptions alert, so it's deleted once by hand.
- **App Insights' own Failure Anomalies rule is declared, and off.** App Insights creates it automatically and sends it to Monitoring Reader and Contributor role holders, which here is nobody. It covers the same ground as the new alert, so the Bicep keeps it disabled, including in a rebuilt environment.

Found in the same logs:

- **The finalize log's "Next:" showed the run in progress.** The timer's `ScheduleStatus.Next` is that run's own start, so the log now asks `FinalizeSchedule.NextRunAfter`, which reads the same schedule as the timer.
- **The placeholder team (A1) logged an alignment error every live week.** Its lineup is never set, so its starters never match the slots; it was logged 53 times on one Sunday. It's still solved and checked by both invariants, but no longer reported, since it never reaches the radar.

## A36 · Zooming the chart, and the flash on phones

2026-11-15. Over a full season the weekly lines crossed so often they were hard to follow, and on a phone the lines flashed whenever a chart option changed. Head-to-head, this week's matchups, division standings, playoffs and a champion are planned as T67–T72, with their own entries (A38 onward).

- **A brush strip under the chart sets the weeks shown,** and the values rescale to them. It is drawn in React with pointer events (D9). Its handles are sliders with arrow, Home and End keys, and a **Reset zoom** button stands in for a double-tap, which has no keyboard equivalent and clashes with zooming on iOS.
- **The range goes into the URL only when a drag or key press ends.** Safari throws after about 100 history calls in ten seconds, and a drag writes on every frame. The commit reads the latest move from a ref, because a quick flick can lift the finger before the last move has rendered.
- **The flash was the lines being remounted.** Each option change gave every line a new key, so for about a second the old lines stayed on the new axes while the new ones waited invisible, then swapped. Each line now keeps one path for its life and changes shape in place. It does not tween between shapes: σ and vs-median plot fewer weeks than the total, so the point counts differ and a tween would distort.
- **Muting had never worked.** Motion left an inline `opacity: 1` on every line, which beat the stylesheet's 0.18 for a muted line. Opacity is now the stylesheet's alone.
- **Charts are no longer scroll anchors.** Chrome anchors scrolling to SVG text, so when the viewport's top edge cut through the chart, every zoom step moved a tick label and the page scrolled with it. Every `svg` now has `overflow-anchor: none`, which also covers the radar and bars.
- **A touch no longer hovers.** A scroll that started on the plot fired `pointermove`, raising and reordering lines mid-scroll. Only a mouse hovers now; a tap, which a scroll never produces, raises the nearest line and a second tap lowers it.

## A37 · The regular season only, enforced

2026-11-15. Q4 settled on the regular season (A33), and T64 makes the code agree before week 15 goes live.

- **The cut-off is the week before `settings.playoff_week_start`.** A league with no playoffs sends zero, or no settings at all, and counts the NFL's 18 regular weeks. A start past week 19 is capped at 18. One function, `RegularSeason.LastWeek`, decides it for every caller, and `LastCountedWeek` trims unplayed weeks off its end (below).
- **Playoff weeks are still read and stored.** The design review caught that stopping the read at the playoffs would starve the bracket and a past season's champion (T69, T70). The snapshot keeps every week, so the finalize job stores them; only the payload stops at the cut-off.
- **Whether a week is a playoff week is derived, not stored.** It follows from its number and that season's league settings, which a past season already reads. No column, no migration.
- **The cut-off is applied once, in the payload mapper,** so totals, σ, the chart, the radar and the "weeks 1–N" label all stop at the same week. `finalThroughWeek` is capped at it too; `currentWeek` is not, because the masthead still says what week it is.
- **`season_type` is read and logged, not acted on.** The plan said to ignore weeks while it is `post`, but what Sleeper's state reports after the NFL regular season has never been observed. Each value is logged the first time an instance sees it, so the logs record the answer.
- **A week that goes backwards cannot shrink the season.** Only final weeks are stored, so in a season moving forward Sleeper's week is always past them. If it falls behind them (a January reset to week 1, or 0 in the offseason), the stored season stands, every stored week counts as final, and a warning is logged once. The design review found this would otherwise have shown one week, or none, on the public page about a week after the fantasy season ended.
- **The page says when the season is over.** The payload carries `lastRegularWeek`; once `finalThroughWeek` reaches it, the masthead says "Season completed" and the page stops polling, because nothing on it can change. The season meant is the fantasy one.
- **A league with no playoffs counts 18 weeks, less any it never played.** Jack's league has playoffs disabled (`playoff_week_start: 0`, captured). Sleeper's settings do not say when such a league ends: `last_scored_leg` and `leg` only count the weeks played so far. So 18 is assumed, and settled weeks at the end where no roster scored are taken off (`RegularSeason.LastCountedWeek`): a league that ends at 17 would otherwise get a week 18 of zeros counted in σ and the chart. A real week never scores zero for everyone. Only the end is trimmed, and only once settled, so a season in progress is never shortened. A past season, and a current one whose state has reset, count as settled to their end. The snapshot's field is `RegularSeasonCutoff`, the settings' answer; the payload's `lastRegularWeek` is the trimmed one.
- **The real league's settings are captured,** by `capture_fixtures.py` into `league-settings.json` (`start_week` and the playoff fields only), and a test pins the captured value. Every other test uses invented starts.
- **Found in the same review:** the slot-alignment error logged every 5 s for a live week whose lineup never lines up (an eliminated team's playoff week could), so it is now reported once per roster-week like every other problem. The CLI header now names the weeks counted. A mid-season change to `playoff_week_start` reaches the page within a day (the league cache) and applies to every week, which is consistent because nothing about it is stored.

## A38 · The league's format is a setting

2026-11-15. T67, the first step towards head-to-head, playoffs and a champion (A36, T67–T72).

- **`League:Format` is `TotalPoints` (the default) or `HeadToHead`.** A setting, not something read from Sleeper: Sleeper scores matchups for every league, whether or not the league cares about them, so only the owner knows which one decides the winner. The default keeps every deployment as it was with no new setting.
- **"Format", not "mode".** The chart already has `?mode=` and `PointMode`, and the head-to-head table would read both side by side.
- **Bound by name, ignoring case.** An unknown name stops the app at start.
- **Head-to-head stays off until T72 is complete,** at Jack's request. `[AllowedValues(TotalPoints)]` makes the app refuse to start with anything else. T68, T71 and T72 all key off the format, so this one gate keeps them from running or reaching the page while they are merged piece by piece, and none needs its own flag. The playoffs (T69) and the champion (T70) are built after it lifts, and appear once built. T72 replaces it with `[EnumDataType]`. Tests that need `HeadToHead` supply the options directly.
- **It rides on the snapshot,** set by `SeasonService` for the current season and past ones alike (Q10), as `OverrideLeagueName` is, so the mapper stays a function of the snapshot and the CLI gets it too (its header names the format). Snapshots are cached in memory only and a setting change restarts the app, so none outlives a switch. The payload sends it by name, `"format": "TotalPoints"`, and the ETag, a hash of the body, changes with it.
- **A completed season is now cached for a day, not a year as `immutable`.** Its scores still cannot change, but its body now depends on configuration. The cost: bodies browsers already hold as `immutable` cannot be reached and may live up to a year with no `format`, so the page reads a missing one as `TotalPoints`. Nothing else holds them: no CDN, Front Door or service worker.
- **Not plumbed into `infra/` yet.** `main.bicep` declares the whole app-settings list, so a value set by hand in the portal would be wiped by the next deploy. `League__Format` goes in as a bicep parameter once T72 lifts the gate, after T68's backfill, since switching before it would show records as missing.

## A39 · Head-to-head records, from the data up

2026-11-15. T68a: everything a head-to-head record needs, live in production and checked against Sleeper while the page still shows total points (A38). T68b puts the record on the page.

- **The captured league confirmed the rules before any code.** Nine weeks of real matchups reproduce Sleeper's own record for every roster exactly: wins, losses, ties, points for and against. So: pair on `matchup_id`; the higher score wins; this league plays a median game (`league_average_match: 1`), each week adding a second result against the median of every roster, the placeholder included; and a game against the placeholder counts. A test now pins that agreement on the captured weeks. What a bye sends is still unobserved (this league has none); a null `matchup_id` is assumed.
- **A week knows its game.** `TeamWeek.MatchupId` is 0 for a bye and null for a week stored before matchups were read, so the two cannot be confused. It is a new nullable SQL column, and the blob schema went to 5.
- **A1 moved to the read side.** The placeholder's weeks were never stored, so a stored week could not score a game against it. The snapshot now keeps excluded rosters in `ExcludedTeams`, apart from `Teams`, and the finalize job stores both. A test proves a total-points payload is byte for byte the same with them present, even in a week only the placeholder scored in.
- **A stored id is never overwritten with null.** The finalize job rewrites every final week from a snapshot that may predate the backfill; without this it would erase the backfill every Tuesday. The design review caught it in SQL, and the standards pass in the blob, which reads come from: a write now refreshes the blob from SQL rather than from the caller's weeks.
- **The backfill is its own daily timer,** all year, because finalizing only runs from September to February, and it can be run by hand from the portal. It reads only stored weeks, newest season first, at most 12 a run, so the live site keeps the token bucket; it finishes in a few days, after which a run reads the store and the cached chain and calls Sleeper for nothing else. It sets only the game on a stored week, never re-solving it (D2). The placeholder's missing weeks are built from the read, with today's player map, which is harmless because they count in nothing but the pairing. A season that fails is logged and the others carry on; a week Sleeper no longer serves stays unfilled and is read again each run, inside the budget, and that season's records stay unknown. Each season logs "Matchups complete" once done, which T72 waits for.
- **Records are null rather than partial.** Any counted week without its games, or a game that does not pair two rosters, leaves every record unknown, since a partial record would rank teams wrongly. Only settled weeks up to the trimmed last week count (R1, A37); a trimmed week is a 0-0 game and would otherwise be a tie.
- **A third check, beside the two invariants:** our record against the one Sleeper keeps on each roster. It runs in either format, logging only, so the record is proven on the real league before anyone sees it. It compares wins, losses and ties only when both sides have counted the same number of results, because Sleeper updates rosters on its own schedule. Points are not compared: a stat correction after a week is stored moves Sleeper's and not ours. It runs on the current season; a past season's records are checked only by the captured-season test.
- **T68b puts the record on the page, gated until T72.**
  - **All or nothing.** In `HeadToHead` each team gets a `record` once every counted week has its games; before any week settles, every team is 0-0. Otherwise the field is absent from the JSON, not null, so the `TotalPoints` body and its ETag never change; a test pins the tag.
  - **The ranking (§10) is stable between polls.** A record moves only when a week settles, so win rate and points for never reshuffle the rows mid-game, and name makes the order total. The chart's chips and the radar's default team follow it, so every panel names the same leader.
  - **PF, PA and Ideal count settled weeks only,** as Sleeper's table does, so no column moves mid-game and none disagrees with the order. The live week shows in the chart.
  - **Points for counts every counted week, a bye included.** It had counted only paired games, so a bye's points went missing, while the Ideal column beside it counted them; the design review caught this. Points against counts games only.
  - **Records are computed per request, not cached on the snapshot.** A field set at construction is copied stale by `with`, and the pairing is one pass over about a hundred rows, next to serializing and hashing the whole body. The mapper reads it only in `HeadToHead`; the record check builds it once per snapshot build in either format.

## A40 · A hosted runner can be on AWS

2026-11-16. The deploy's migrate job failed three times in a row (T67, T68b and its rerun, T73) after passing on every deploy before.

- **The cause is where the runner runs.** Each job's log names its cloud. All 19 migrate jobs from 2026-10-08 to the morning of 2026-11-15 ran on Azure; from that afternoon some ran on AWS, and each AWS run failed while each Azure one passed. Nothing in the repo changed.
- **Two symptoms, one cause.**
  - **Sign-in:** `Active Directory Default` probes managed identity before the Azure CLI. On AWS the metadata service answers 401 rather than not answering, which ends the chain before the `azure/login` session is tried. `AZURE_TOKEN_CREDENTIALS: dev` restricts it to developer credentials, so managed identity is never tried (T73).
  - **Firewall:** the SQL server admits Azure-hosted clients only (`AllowAllWindowsAzureIps`), so an AWS address is refused (error 40615). This reverses A31's third bullet: the migrate job opens a rule for the runner's own IPv4 address, named for the run and attempt, and removes it afterwards, even when the migration fails.
- **The rule needs no new permission.** The deploy identity's Contributor role on the resource group covers firewall rules. A rule left behind by a cancelled run is harmless and named for the run that made it.

## A41 · This week's matchups

2026-11-16. T71: a head-to-head league's games for one week, under the standings. Gated with T68b until T72 lifts `League:Format`.

- **One week, chosen by the server.** `MatchupWeek` is the current week, held to the last counted week (A37): the live week in the regular season, the last regular week once the season is complete, during the playoffs, or for a past season. The preseason (week 0) has none. No week picker (Q13): past weeks show only in the table's W-L.
- **One pairing rule.** `HeadToHead.Games` pairs both the records and the panel, so a game cannot count in a record yet be missing from the panel, or the reverse. Games come by week then matchup id, each game's sides by roster id, so a change of lead never swaps a card's lines between polls.
- **The placeholder's side carries its name,** because it has no table row to take one from (A1). Its points show as scored and count in nothing new; it has no team colour, so its card has an empty swatch.
- **Null rather than empty or partial.** `matchups` is absent in `TotalPoints`, in the preseason, for a week stored before matchups were read, and for a week with no game. The last case matters because a missing `matchup_id` is stored as 0, a bye, so a week Sleeper has not scheduled reads as all byes. For a league with no playoffs this covers a cut-off (18) past its last scheduled week; a week Sleeper does schedule shows as a real one, as the chart shows it. A test pins that a `TotalPoints` body with games in its chosen week still carries no `matchups` key.
- **Live points while the week plays.** R1 governs what counts, not what shows; the title says "matchups" while live and "results" once final, so a final week is never shown as current.
- **Readable without colour or announcements.** Each side is one line of text, name, points and status: "Leading" or "Level" live, "Won" or "Tied" once final, nothing before kick-off. The winner is also in a heavier weight. No `aria-live`, so the 30 s poll announces nothing, and no motion.
- **The median game is not a card.** The record counts it (A39) and the table shows the result; a median line on the panel is left for later.

## A42 · The higher score in bold, not in words

2026-11-16. Corrects A41's "readable without colour" bullet before the panel was ever shown (it is still gated until T72). "Leading", "Level", "Won" and "Tied" beside each score read as clutter on a two-line card. A side's points are now the whole line after its name, and the higher score is bold, live or final; a tie, 0-0 before kick-off included, bolds neither.

- **Still not colour alone.** Weight carries it visually, and the two numbers carry it for a screen reader, which reads both in order.
- **Live or final is the title's job.** "Week 7 matchups" or "Week 7 results" (A41) says whether the bold score is a lead or a win, so the line no longer has to.
- `matchupLeader(game)` replaces `matchupStatus(game, isFinal)`: the index of the side ahead, or null.

## A43 · Head-to-head, read by a manager

2026-11-16. A UI review and a fantasy-manager review of the gated head-to-head section (T68b, T71) agreed the layout held and the meaning did not: a visitor had to work out the median game, whether a score was live, and who the unranked team was. Six changes, still gated until T72.

- **The median game is shown, not just counted.** It is half of every record (A39) and was invisible, so a 6-0 team read "9-3" and looked wrong. Each record now carries its median share (`record.median`, absent in a league without a median game); the table shows "games · median" under the record, labelled so under the Record header, and a screen reader hears the total, the games and the median share. The median share never shows ties: one needs two equal decimal scores. The matchups carry the week's median over every roster, the placeholder's included, as the record counts it (`matchups.median`), and a line under the cards names who is above it and who is on it, the placeholder tagged. It prints two decimals because the list compares exact scores: at one, a side shown level with the median could be listed above it. Sleeper keeps no split, so the record check compares totals only.
- **A rank and games back.** In head-to-head the order is the answer, so the table leads with "#" and shows games back beside the record, to one decimal so halves line up. Games back is measured from the best wins less losses, not the top row: a bye leaves teams on different game counts, so win rate and margin can disagree, and measuring from the top row could go negative. On a phone, games back stays and points against and ideal give way: ideal is the points format's story. The rank is a lead column, so the team column after it stays the wide, left one.
- **Live or final in the title.** "Week 7 · upcoming" before any side has scored, "Week 7 · live" until the week settles, "Week 7 results" after; "matchups" did not say in progress, and "live" over a row of 0.0s would mislead. While live, a line says the table adds these scores once the week is final, since points for counts settled weeks only (A39) and the cards sit right under it.
- **The placeholder named as such.** Its empty swatch was invisible, a blank gap; it is now a ring in the muted text colour, which reads in both themes, and "(not ranked)" follows its name (A1).
- **The winner's whole line in bold once final,** not just the score (A42), so "who won" does not rest on comparing two digit weights on a phone. Live, only the higher score is bold.
- **A screen reader hears the result.** Bold carries it on screen; a screen reader now hears "versus" between the sides and "leading", "level", "won" or "tied" after a score, nothing at 0-0 before kick-off.
- **The waiting line says what it means.** "Records are still loading, so the table is ranked by points for now", not "once every week's games are in", which read as "after Sunday" when it meant the backfill.

Not done here: the champion. T70 crowns the winner of the final playoff game, but a league with no playoffs (A37) has none, so it needs a rule for that case before T72 lifts the gate.

## What the original design got wrong

The design now states what's true. Most of its mistakes are recorded where they were fixed (D5, A6, A7, A9, A12, A16, A17, A19, §13); these three are worth keeping here.

- **The request budget was ≈ 2.3 Sleeper requests a minute as designed.** It's ≈ 4.4 as built, and every figure is per instance (§07).
- **The cost table missed the storage account entirely.** It is now the main charge (§13).
- **Twelve teams were assumed;** there are five scoring.
