# Ideal Points — Task List

50 required tasks across 7 milestones, **108 h** estimated, plus an optional
eighth milestone worth 7.5 h. Ordered so the work most likely to be wrong
happens first and needs no cloud account to prove.

**Where things stand (2026-09-14).** M0, M1 and M2 are built and deployed. M3 is
built as far as it usefully can be: the database exists, the schema is applied,
and weeks persist as they finalize. Its last three tasks are **deferred** — each
waits on a prior season, on data that accumulates on its own, or on a second
migration. **M4, the page, is next**, and it is the first milestone anyone else
in the league can look at.

- Step-by-step instructions per task: [`docs/field-manual.md`](../docs/field-manual.md)
- Rationale for every decision: [`docs/design.md`](../docs/design.md)
- Decisions made after the design was written: [`docs/amendments.md`](../docs/amendments.md)
- Rules written after a mistake: [`lessons.md`](lessons.md)

`±` marks a soft estimate that could run double.

---

## Done · M0–M2

Per-task instructions for these have been compacted in the field manual into a
**Built** reference — what exists, where it lives, and the traps that still
bite. The step-by-step is no longer useful now the code exists.

**M0 · Core domain** — `Slot` parsing and eligibility, the bitmask DP solver
(exact, verified against exhaustive search over a 50,000-case fuzz), population
sigma with its four-week floor.

**M1 · Sleeper client** — six DTOs behind one `SleeperJson.Options`, a typed
`HttpClient` with resilience outermost and a 50/min token bucket inside it,
single-flight caching, the player dump projection, whole-season assembly, and a
console harness that asserts both invariants against the live league.

**M2 · API and cloud** — two read-only endpoints with ETags and conditional
requests, a Flex Consumption Function App and Static Web App in bicep, OIDC from
GitHub to Entra with no secret in the repo, and three workflows.

|         | Task                                         | State                                                                                                           |
| ------- | -------------------------------------------- | --------------------------------------------------------------------------------------------------------------- |
| T01–T03 | Scaffold, recon, domain types                | done                                                                                                            |
| T04     | Slot parsing and eligibility                 | done — **A3's unknown-slot guard still missing**, its test sits skipped                                         |
| T05     | Bitmask DP solver                            | done                                                                                                            |
| T06     | Solver fixtures and tests                    | **blocked on data** — B1–B4                                                                                     |
| T07–T08 | Consistency maths and edge cases             | done — F1, F2 fixed 2026-10-12                                                                                  |
| T09–T13 | DTOs, client, rate limit, resilience, cache  | done — F4 open                                                                                                  |
| T14     | Player dump job                              | done, **and extended**: blob-backed store and daily timer (A12)                                                 |
| T15–T16 | Season assembly, console harness             | done                                                                                                            |
| T17–T19 | Functions host, endpoints, caching semantics | done                                                                                                            |
| T20     | Bicep template                               | done — Flex Consumption, not the design's Consumption plan                                                      |
| T21     | GitHub to Entra OIDC                         | done — needed immutable-ID subjects                                                                             |
| T22     | `ci.yml`                                     | done — build, test, format, vulnerable packages, plus a bicep what-if job. No vite build; `web/` arrives at T32 |
| T23     | `deploy.yml`                                 | done — infra, then functions with SBOM and scan, then a smoke test. `web` job stubbed until T32                 |
| T24     | CORS and first deploy shakeout               | done — verification pass run 2026-10-05, after the #58 deploy                                                  |

### T24 · what is left

CORS is verified working in Azure: `allowedOrigins` is exactly the SWA hostname,
and a foreign origin is refused. Preview environments are deliberately **not**
allowed — production origin only, decided rather than discovered. The
`devOrigins` bicep parameter was removed unused in T61 4b; the Vite dev proxy
made it unnecessary.

- [x] Run the full verification pass after the next green deploy. **Done 2026-10-05**,
      after the #58 deploy. The runbook was cut from the field manual with the
      other finished tasks; it survives in `git show f54bacb:docs/field-manual.md`.
      All passed: the API answers (week 4, five teams), A1 holds, `max-age=5` and a
      weak ETag, a conditional request gets 304, CORS allows the SWA and the custom
      domain and refuses anything else, the stored map is 840,735 bytes, no
      exceptions in seven days, and no failed requests bar one `favicon.ico` 404.
      Spend: $0.02 of the $1 budget five days into October (see F8).
- [x] `AZURE_STATIC_WEB_APPS_API_TOKEN` is set and the `web` job deploys with
      it. The first value stored was the literal `$(az ...)` command string,
      because `$( )` is not substitution in cmd.
- [x] Confirm on a **second** cold start that the log says "Adopted the stored
      player map" rather than "Refreshing player map from Sleeper API". This is
      the only evidence A12 solved the problem rather than moving it.
      **Confirmed 2026-10-05, without forcing a restart:** since 2026-09-13, starts
      adopted the stored map 280 times. Sleeper was fetched 24 times: 23 by the daily
      09:30 timer, and once, almost certainly the very first start, before anything
      was stored. Two traps hit on the way: `az monitor app-insights query` only
      looks back 24 hours whatever the query says (use the Log Analytics REST API,
      as the runbook does), and `kind` is a reserved word in KQL.

---

## M3 · Persistence — 10 of 17 h done

Finalized weeks become permanent. The database is deployed, the schema is
applied, and the read and write paths both exist.

- [x] **T25** EF Core model — 3 h
      Four entities, `Initial` migration applied. PK leads on `Season` (**A14**).
- [x] **T26** Azure SQL in bicep — 3 h `±`
      `GP_S_Gen5_2` serverless in **westus2**, free limit with auto-pause, Entra-only auth.
- [x] **T27** Read path — 2 h
      `IFinalizedWeekStore` in Core, SQL adapter and cache decorator in Data,
      consumed by `SeasonService`. 24 tests.
- [x] **T28** Finalize timer — 2 h
      `FinalizeWeek`, Tuesdays 12:00 UTC. Writes **every** finalized week, not just
      the last, so the current season backfills itself.

**The remaining three are deferred to the end** — see _Deferred_ below. None is
blocked on effort; each is waiting on something that does not exist yet.

## M4 · The page — 24.5 h · built

First milestone anyone else in the league can look at, and the first that will
generate opinions.

**M4 is built.** The page renders the live standings, an interactive chart and a
gated radar panel, in both themes, with no horizontal scrollbar at 375 px and 22
passing tests. The field manual's M4 section is now a built reference rather than
instructions.

- [x] **T32** Frontend scaffold — 2 h
      Vite 8, React 19, TypeScript. Types written from the live response, ETag-aware
      fetch layer, `useSeason` with visibility-gated polling, dev proxy past CORS.
      `web` jobs restored in ci.yml and wired to SWA in deploy.yml.
- [x] **T33** Design system — 3 h
      OKLCH tokens, four type sizes, one spacing scale, dark designed rather than
      inverted. Global `box-sizing: border-box` added later, in T33b, once its
      absence produced a page-wide horizontal scrollbar.
- [x] **T33b** Panel layout — 1.5 h
      Three sticky `100svh` panels under `proximity` snap, a sticky masthead at
      `--masthead-h` with the panels offset beneath it, and a reduced-motion
      block that returns the page to an ordinary document. The radar is gated on
      `leagueSlotAverages` being non-empty — no flag to flip when T48 ships — with
      `?radar` to force it open while building.
- [x] **T34** Standings table — 3 h
      Fixed sort on points, tabular figures, hairline rules, `aria-sort` on the
      sorted column, em dash for absent values and a real U+2212 in the gap.
      Split into `Panel` / `StandingsPanel` so each panel owns its own state.
- [x] **T35** Inline range mark — 2.5 h
      Min-to-max rule, mean tick and one-sigma band, on a single league-wide
      scale built by `rangeScale`. Returns `null` when no team is consistent, so
      holding the scale _is_ the fact that there is a column to draw.
- [x] **T36** D3 line chart — 4 h `±`
      Scales, axes and path generators; React renders every node.
- [x] **T37** Chart controls — 3 h
      Season, team multi-select, metric, display mode. State in the query string.
- [x] **T38** Hover interaction — 1 h
      Raise the nearest line, mute the rest, with focus on T37's chips as the
      keyboard equivalent. **Chart panel only** — the panels are never on screen
      together, so `hovered` stays in `ChartPanel` and the table is not involved.
- [x] **T39** Polling — 2 h
      Gated on tab visibility and game windows; stops on a past season.
- [x] **T40** Responsive and a11y — 2.5 h
      Keyboard paths, focus states, table that scrolls in its own container.

## M5 · Modes, ops and docs — 10.5 h · built

Deliberately last: cheap, taste-driven, easier to judge against real data.

- [x] **T41** Display modes — 2 h · per-week and vs-league-median
- [x] **T42** Sigma variants — 2 h · expanding vs rolling window, sigma vs CV
- [x] **T43** Loading and error states — 2 h · including the SQL cold resume
- [x] **T44** Cost guardrails — 2 h · ingest cap, budget alert, SQL pause verified
- [x] **T45** README and runbook — 1.5 h · chiefly the August rollover checklist
- [x] **T57** Custom domain — 1 h · `ff.jacktreadwell.com` · **live**
      DNS, the SWA hostname, the managed certificate and the bicep change are
      all committed and deployed; the site serves over https at the custom
      domain. No cost and no tier change, so the $0.00/month claim stands, and
      the apex GitHub Pages site is untouched.

## M6 · Positional radar — 10 h · built

- [x] **T46** Slot attribution — 2.5 h · `starters[i]` maps to slot `i`, plus the mid-season rules-change guard
- [x] **T47** League reference and ratios — 1.5 h · average first, divide second
- [x] **T48** Slot block in the API — 1 h · fills the two empty objects §09 already reserves, **and thereby reveals the radar panel**
- [x] **T49** Radar chart — 3.5 h `±` · axis order fixed and never re-sorted
- [x] **T50** Diverging bar alternative — 1.5 h · the same data on a linear axis

## M7 · Motion and polish — 7.5 h · **optional**

Cut the entire milestone and the site still does its job — listed separately so
that cutting it is a decision rather than an omission.

- [x] **T51** Motion setup — 1 h · `LazyMotion`, `useReducedMotion` everywhere
- [x] **T52** Table layout animation — 1 h · a Sunday overtake physically slides
- [x] **T53** Chart reveal and transitions — 2 h `±`
- [x] **T54** Radar/Bars transition — 1 h · retargeted from a section router that never existed; see field-manual.md
- [x] **T55** Football loader — 2.5 h

---

## Now · make the repo presentable as a portfolio piece

Started 2026-10-12. Goal: a prospective employer can land on the repo or its
site and quickly see what was built, how, and to what standard. In order; each
one feeds the next.

- [ ] **T60** Engineering standards — `docs/standards.md`. A terse, bulleted
      list of coding and design standards, one section per domain. Grounded in
      what the repo already enforces (`Directory.Build.props`, `.editorconfig`,
      `.oxlintrc.json`, CI) and the design's D-decisions; where they disagree with
      each other or with the code, the conflict is resolved before it is written
      down. **Reviewed by Jack before T61.**
- [ ] **T61** Validate the code against T60. One pass per domain; every gap is
      either fixed or recorded as an accepted exception with its reason, so the
      standards stay true rather than aspirational.
      - [x] **1 · C# and .NET** — done 2026-10-12. Style rules now fail the
            build (21 raised to warning; XML doc output on so IDE0005 runs);
            no `!` left in `src`; every clock read goes through `TimeProvider`;
            all options validated at start-up; fallbacks narrowed to transient
            failures; single-project types made internal; the A3 test
            un-skipped; Sleeper request URLs (carrying the league ID) no longer
            logged. 346 tests pass. Exceptions recorded in the standards.
            Sleeper DTOs now expose read-only collections, and the tests moved
            from xunit 2 (deprecated) to xunit.v3, on the classic VSTest runner
            (`IsTestingPlatformApplication=false`, so CI's `dotnet test` is
            unchanged). Follow-ups: grep `TODO T61` for comments Jack is to
            evaluate; `CS1591` (missing XML docs) is suppressed pending domain 7.
      - [x] **1b · C# and .NET, high-level review** — approved and done 2026-10-12.
            Suggested order: 1–4, then 5–8, then 18–26 in one pass.
            **1–10 done 2026-10-12.** 358 tests pass, `dotnet format` clean,
            console run against the live league. The console now reads
            `League__CurrentLeagueId` (no longer `SLEEPER_LEAGUE_ID`) and
            applies `ExcludedOwnerIds`. The blob moved to
            `{league}/current.json`; the old `{league}/2026.json` is orphaned.
            **11–17 done 2026-10-12** (A21). 366 tests pass, `dotnet format`
            clean, console re-run live with identical output. Item 16 was a
            wrong finding: `GetPlayersAsync` does return null for a literal
            `null` body. The check stays and now throws `InvalidDataException`
            like the reader cache.
            **18–21 done 2026-10-12** (A22). The Sleeper project split into
            `Sleeper` and `Seasons`; the console project is `FantasyLeague.Cli`.
            366 tests pass, `dotnet format` clean, CLI re-run live with
            identical figures. The old `src/FantasyLeague.Console` folder holds
            only build output and could not be removed while locked; delete it.
            **22–27 done 2026-10-12.** Doc comments rewritten where they restated
            the signature or had drifted, implementations use `<inheritdoc/>`,
            typos fixed, redundant EF configuration removed, long one-line
            bodies wrapped, member order fixed. Migration `DropUnusedTables`
            drops `Leagues`, `Players` and `Teams` (A19); the next deploy's
            `migrate` job applies it. 366 tests pass, `dotnet format` clean, no
            pending model changes.
            **28–29 done 2026-10-12.** 28 needed nothing further (item 5 removed
            `FinalizedUtc`). F1 and F2 fixed below; 368 tests pass.
      - [x] **2 · Front end** — done 2026-10-12 (A24). `noUncheckedIndexedAccess`
            on, no uncommented `!` in `src`, calculations moved to
            `pointCalculation.ts`, reduced motion honoured by layout animations
            and the radar crossfade, the chart label states figures, `?season`
            read, Prettier added, the bundle budget checked in CI. 153 tests
            pass. Checked at 375 px in the browser against live data, which
            showed the chart's line-end names clipping; a narrow chart now drops
            them. Desktop keeps them.
      - [x] **2b · Front end, high-level review** — 18 items, done 2026-10-12.
            **5, 10 and 18 done (A25):** one colour per team, fixed by roster
            id and shared by every panel; the game windows cover London
            Sundays, late-season Saturdays, Christmas, Black Friday and Friday
            openers, with Thanksgiving found by date; `useSeason` and the game
            windows have tests. 188 tests pass.
            **1, 2, 3 and 7 done.** The loader is placed by CSS alone: two
            measured constants, one per breakpoint, matching the old JS to the
            pixel at 375, 800 and 1280 px (the old CSS was right only on narrow
            screens). `useSeason` no longer tracks a season that never changes,
            and new data no longer resets the poll timer. One `TeamChips` serves
            both panels; one `StandingsHead` serves the table and the Skeleton.
            15 (the false `#boot` comment) went with item 1. 190 tests pass.
            **The rest done (A26).** Unused code removed and noted in T29 and
            T59; `?radar` gone; a failure is a panel and the masthead stays
            quiet; `pointCalculation.ts` is `calculations.ts`; a `Measure` type
            for points calculations, so sigma can no longer reach them (the
            compiler found the chart passing it); one `LinearScale` type and a
            `SpreadScale` for the chart's sigma/CV control, which had shared
            the name `Scale` with the d3 type; `App` names fixed; cryptic CSS
            and code comments rewritten; the 404 season list is checked before
            it becomes links. 185 tests pass.
            - Defects
              1. Delete the custom `ShouldRetry`, so the standard predicate
                 retries attempt timeouts and 500s; keep the rate-limit
                 exclusion
              2. Remove the unreachable `immutable` branch and the `requested`
                 parameter of `ToIndividualSeasonPayload`; T29 restores it (A19)
              3. `GetSeasons` builds a whole season to read `Season`
              4. Log violations and unresolved players once per build, not on
                 every request
            - Simplify
              5. Blob holds the current season only, SQL every season (A20):
                 one blob per league with the season stamped inside; a read hits
                 only on a matching season; past seasons read straight from SQL
              6. Replace `SanityCheckSeasonForBlob`'s calendar window with a
                 season comparison: the blob is only replaced by the same or a
                 newer season, so a backfill cannot evict the live one
              7. Define week finality once, in Core
              8. `SlotAverager`: one code path, one meaning of null, no
                 knowledge of Sleeper's `"0"`
              9. The console reads the league ID through `LeagueOptions`
              10. The console renders the payload's totals instead of computing
                  its own
            - Band-aids
              11. Registration that depends on calling `AddFantasyData` before
                  `AddSleeperClient`
              12. `AddSleeperClient` registers far more than a client; split or
                  rename
              13. Duplicated: storage fallback factory, `IsTransient`, blob store
                  skeleton, options-binding pattern
              14. ETag from a hand-kept field list with a wrong comment;
                  ignores the league name override and excluded rosters
              15. Apply `OverrideLeagueName` when the snapshot is assembled
              16. Unreachable null check in `PlayerMapRefresher`
              17. Wrong "No timeout" comment on the `HttpClient`
            - Naming
              18. File and type mismatches, including `IBlobFinalziedWeekStore.cs`
              19. `ActualPts`/`Actual`/`Points`; `Spread`/`Sigma`/consistency/`SD`
              20. `ToIndividualSeasonPayload`/`ToSeasonsPayload`,
                  `MakeSlotAverages`, `FinalizeWeek.Run`
              21. `ct`/`cancellationToken`/`ct2`, `NFLState`, the Sleeper
                  project name, the user agent, the `FantasyLeague.Console`
                  namespace clash
            - AI smells
              22. Comments that drifted: cache TTLs, "per Scope above", "until
                  M3", "float drift" on a decimal
              23. Docs that restate the signature; use `<inheritdoc/>`
              24. Typos
              25. Null checks on non-nullable parameters, redundant EF calls,
                  very long one-line bodies
              26. Member order and indentation
            - Dead code
              27. Drop `League`, `Player` and `Team` with a migration (A19)
              28. `StoredSeason.FinalizedUtc` is written, never read (item 5
                  replaces it with `Season`, which is read)
              29. F1 and F2 `Spread` bugs, already tracked
      - [ ] **3 · Data and integrations** — 13 items, approved 2026-10-13.
            Ten of the sixteen standards already hold: one rate-limited client
            with a singleton bucket, the league ID only from configuration, the
            exact solver on each week's own roster, migrations before code,
            sigma per D1 on both sides, the radar per D10 and R2.
            **1–3 done 2026-10-13.** §07 recalculated (F4 closed); a failed
            database read is no longer cached. 371 tests pass, `dotnet format`
            clean.
            **4–9 done 2026-10-13 (A27).** The blob carries its write time
            and misses when older than the last `FinalizeWeek` run
            (`FinalizeSchedule`, shared with the timer); the blob is now
            `current.v3.json`, v2 orphaned. The SQL store refuses unfinished
            weeks. `SeasonService` drops the placeholder team; the invariants
            still check it. `SpreadCalculator.MinWeeks` is a constant (Q5 marked
            settled in the design). TTL reasons fixed; `/seasons` cached a day.
            377 tests pass, `dotnet format` clean. The CLI was not re-run live:
            it needs `League__CurrentLeagueId` in the environment.
            **11–13 done 2026-10-13 (A28).** Blob names use a hash of the
            league (the blob under the plain ID is orphaned); the Sleeper bucket
            is 25/min per instance, so 40 instances stay inside Sleeper's
            1,000/min; `PlayersPointsJson` renamed `PlayersJson` by migration
            `RenamePlayersJson`, which the next deploy's `migrate` job applies.
            379 tests pass, `dotnet format` clean, no pending model changes.
            Still open: 10, which needs App Insights.
            - Sleeper API
              1. ~~§07, the README and CLAUDE.md state a budget the code no
                 longer has; recalculate and close F4~~ — done, ≈ 4.4 req/min,
                 and §07 now says the figures are per instance
              2. ~~A failed SQL read returned empty and was cached for 6 h, so
                 every finished week was refetched from Sleeper long after the
                 database woke (past the 50/min bucket by week 13)~~ — done:
                 `SqlFinalizedWeekStore` throws, `CachedFinalizedWeekStore`
                 falls back outside the cache
              3. ~~§07 left out the finished-but-unstored week~~ — done, with
                 the store-outage case beside it
            - Persistence
              4. **Done (A27).** The stored-season blob carries no timestamp. Add the **write
                 time** (decided) and move to schema v3. The reader has to use
                 it this time: 1b item 28 removed `FinalizedUtc` because nothing
                 read it
              5. **Done (A27).** Only `FinalizeWeek`'s filter keeps unfinished weeks out of the
                 store. **Make the store refuse them** (decided)
            - Correctness
              6. **Done (A27).** The placeholder team is filtered in `SeasonPayloadMapper`, so
                 the snapshot, the CLI, the logs and the stored rows still see
                 it. **`SeasonService` drops it** (decided), and
                 `ExcludedRosterIds` goes
              7. **Done.** `SpreadCalculator.Calculate`'s `minWeeks` parameter has no
                 caller; make four a constant and drop the empty-list check
              8. **Done: reasons fixed, TTLs kept (decided).** The rosters and users TTL reasons look swapped: team names come
                 from users, and rosters only say who owns what. Fix the reasons,
                 perhaps the TTLs (which moves item 1's budget)
              9. **Done (A27): 5 s is deliberate; `/seasons` a day.** `/season`'s `max-age=5` differs from §09's 30 and D6's "30-second
                 server cache"; `/seasons`' `max-age=300` has no reason. Record
                 both or change them
            - Outside the standards
              10. Confirm `FinalizeWeek` (Tuesday 12:00 UTC) runs after Sleeper's
                  week rolls over: if it rolls on Wednesday every week is stored
                  a week late. Check "Finalized … through week X" in App Insights
              11. **Done (A28): hashed in blob names only.** The league ID is in blob names and cache keys. Outside A2's
                  letter (repo and logs); leave unless hashing is wanted
              12. **Done (A28).** `WeekScore.PlayersPointsJson` holds players, not points. Rename
                  only alongside another migration
              13. **Done (A28): 25/min per instance.** The token bucket is per instance, so the 50/min limit is too:
                  three instances could send 150/min. Found while recalculating
                  item 1; §07 now says so
      - [ ] **3b · Data and integrations, high-level review** — 25 items,
            2026-10-13.
            **1–4 done 2026-10-13.** No storage account is now a start-up
            failure (`BlobStorageOptions.ConnectionString` is required, with
            `AzureWebJobsStorage` as its fallback), and `NullBlobFinalizedWeekStore`
            is gone; the CLI already used the Core null stores explicitly.
            `Program.cs` comment corrected. `IPlayerMapRefresher.EnsureCurrentAsync`
            returns the map, held with its timestamp in one field; `PlayerMap`,
            `LastRefreshedUtc` and `SeasonService`'s unreachable check are gone.
            379 tests pass, `dotnet format` clean.
            **5 and 8–11 done 2026-10-13.** 5 confirmed first: a cached list came
            back as a new copy on every hit, the snapshot as the same instance.
            Cached Sleeper responses and stored seasons are now wrapped in a
            record marked immutable, and a test pins it. One `FinalizedWeekStore`
            (memory, then blob, then SQL) replaces `CachedFinalizedWeekStore`,
            `BlobFirstFinalizedWeekStore` and `IBlobFinalizedWeekStore`.
            `RosterWeek.Starters` is derived from `SlotStarters`; `PlayersPoints`
            stays, because it alone holds the points of players the map could
            not resolve. `SleeperLeague.Teams` holds one `SleeperTeam(Name,
            OwnerId)` per roster. `SeasonService` reads in steps
            (`ResolveExclusions`, `ReadWeeksAsync`, `ToTeamWeek`, `GroupTeams`)
            into one `SeasonWeeks`; its two log lines per build are one.
            377 tests pass, `dotnet format` clean.
            **12–21 done 2026-10-13.** 12: kept, for §08's recompute (A29).
            13: one serialized shape, `PlayerId` as a bare string, the
            `Positions` key rewritten by `RenamePlayersJson` (A29); blob schema
            v4. 14: DTOs hold only what the mapper reads. 15: `ToSeasonsPayload`
            takes the current season only. 16: `PlayerDump` gone; the startable
            count is the tripwire. 17: `SeasonETag.Compute` and `.Matches`.
            18: `RosterWeek.ScoredPts` is `SleeperPts` (`ActualPts` kept, as
            decided in 1b). 19: `InvariantViolation` carries `SleeperPts` and
            `ComparedPts`. 20: `SleeperTeam.FallbackName`. 21: went with 13.
            376 tests pass, `dotnet format` clean, no pending model changes.
            **24–25 done 2026-10-13.** Only `SlotAveragesJson` keeps an
            empty-column guard (rows from before `AddSlotAveragesJson`); an
            empty lineup or player column now throws. The per-build timing
            line is Debug. 378 tests pass, `dotnet format` clean.
            Left: 22–23, folded into domain 7.
            **Report logging fixed 2026-10-13** (found while doing 25): each
            unresolved player and invariant breach is logged once per instance,
            through a singleton `ReportedProblems`, instead of on every 5 s
            build. 379 tests pass; the new test fails with the check disabled.
            - Kluges
              1. ~~A missing storage account silently registers no-op blob stores~~
              2. ~~`Program.cs` comment names a catch that moved~~
              3. ~~`SeasonService` checks a "never refreshed" map that cannot be~~
              4. ~~`IPlayerMapRefresher` is ensure-then-read, with a torn pair of
                 properties~~
              5. ~~Done.~~ HybridCache may deserialize a fresh copy of the Sleeper DTO lists
                 and the stored season on every hit; only `SeasonSnapshot` is
                 marked immutable. Confirm with a test, then fix
              6. The blob write reads the blob first to protect against T30's
                 backfill, which is not built (A26's rule)
              7. Two catch blocks per blob write log one message at two levels
            - Overly complex
              8. ~~Done.~~ Four classes and two interfaces for the finalized-week store;
                 fold blob-first into the cached store
              9. ~~Done, in part: `Starters` derived; `PlayersPoints` kept.~~ `RosterWeek` carries the same facts several times (`Players`
                 and `PlayersPoints`, `Starters` and `SlotStarters`), and
                 `SeasonService` derives a third copy
              10. ~~Done.~~ `SleeperLeague` keeps two parallel dictionaries keyed by roster
              11. ~~Done.~~ `BuildUncachedAsync` is about 70 lines; `BuildWeekAsync` returns
                  three lists the caller appends to three more
              12. **Kept (A29).** Starters, ideal lineup and every player are stored for a
                  recompute that does not exist. **Decision needed:** keep for
                  §08, or drop under A26
              13. ~~Done (A29).~~ The SQL row and the blob serialize the same week in two shapes
            - Waiting for a task
              14. ~~Done.~~ DTO fields nothing reads, `LeagueDto.LeagueId` among them
              15. ~~Done.~~ `ToSeasonsPayload`'s `available` parameter is always null
              16. ~~Done.~~ `PlayerDump` exists for one log line
            - Naming and placement
              17. ~~Done.~~ ETag logic lives in `SeasonPayloadMapper`
              18. ~~Done.~~ `ActualPts` and `ScoredPts` side by side, not saying which has
                  the override
              19. ~~Done.~~ `InvariantViolation.Expected` is a target for one check and a
                  floor for the other
              20. ~~Done.~~ The `"Roster {id}"` fallback is written three times
              21. ~~Done.~~ `FromJson` and `ToJson` in `SqlFinalizedWeekStore` read and
                  write player ids
            - AI code smells
              22. Doc comments that restate the type (`CancellationToken.`,
                  `Logger.`)
              23. Doc comments that narrate history the amendments already hold
              24. ~~Done.~~ Empty-JSON guards on columns that are never empty
              25. ~~Done.~~ "Begin building season snapshot." logged every 5 s under load
      - [x] **4 · Infrastructure and operations** — 11 items, done 2026-10-13
            (A30). Passing already: no Azure names in the docs, no real IDs in
            git or commit messages, Entra-only SQL, the telemetry cap, SQL
            tracing.
            1. The SWA upload token is read per run through OIDC, not stored
            2. The budget start date has no deploy-time default; the lookup
               fails loudly instead of falling back to this month
            3. The budget alert fires at fifty cents (50% of $1); the standard
               says so
            4. `BUDGET_ALERT_EMAIL` is required, so the budget cannot be absent
            5. Cost table: storage about $0.15 measured, steady state about
               $0.25; the standard is "under fifty cents"
            6. Storage by managed identity, shared keys off; three role
               assignments in the Bicep; the app's blob stores take
               `AzureWebJobsStorage__accountName`; Azure.Identity 1.21.0
            7. `AllowAllWindowsAzureIps` recorded as an accepted exception
            8. SQL on the stable `2023-08-01` API
            9. A metric alert on exceptions, about $0.10 a month, with an action
               group
            10. `alertEmail` is `@secure()`
            11. `functions` and `web` run in the `production` environment
            381 tests pass, `dotnet format` clean, the Bicep builds and lints
            clean, the workflows parse. Not yet deployed.
            **Before merging, by hand:**
            - [ ] Grant the CI principal Role Based Access Control
                  Administrator on the resource group, with the condition in
                  runbook step 4. Without it the deploy turns shared keys off
                  and then fails to grant the roles that replace them, leaving
                  the app unable to reach storage.
            - [ ] After the first green deploy, delete the
                  `AZURE_STATIC_WEB_APPS_API_TOKEN` GitHub secret.
            - [ ] Deploy `infra/budget.bicep` once with the existing budget's
                  start date (runbook, step 6), so the existing budget's alert
                  moves to fifty cents. The pipeline no longer touches it (A31).
      - [ ] **4b · Infrastructure, high-level review** — 16 items, 2026-10-13.
            **Done: 1–5, 8–10, 12–14.** The template has 10 parameters, down
            from 21. Fixed values are constants with a one-line reason;
            `namePrefix` stays, because global resource names make it the one
            value a second deployment must change. SQL is no longer optional.
            Only the outputs the deploy reads remain, and the migrate job takes
            the SQL server from them instead of three `az` lookups. `devOrigins`,
            the excluded-owner check step, the redundant `az bicep build` steps
            and `tr -d '\r'` on Linux runners are gone. Descriptions say what a
            value is; the `Program.cs` comment is one line; 32 GB is written as
            such. The Bicep builds and lints clean, the workflows parse,
            `dotnet format` clean. Not yet deployed.
            **Done: 6, 11, 15 (A31).** The budget is `infra/budget.bicep`,
            deployed once from the runbook; the lookups in both workflows are
            gone. `verify-cost.sh` is removed. Defaults are no longer restated;
            security settings stay explicit.
            **Done: 7 (A31).** Tested from a throwaway branch: a hosted runner
            reaches SQL with no rule for its address. The firewall steps are
            gone. The first test run hit a paused database and showed the
            design-time factory had no retry; it now has the app's.
            - [ ] Delete the remote `test/sql-firewall` branch (my delete was
                  blocked): `git push origin --delete test/sql-firewall`
            - [ ] A portal-made rule, `ClientIPAddress_20260914052117`, is on
                  the SQL server outside the Bicep. Delete it if that address
                  no longer needs direct access.
            Open:
            16. The queue and table storage roles may be unneeded with timer
                triggers only; check after the first identity deploy
            Found while doing 2: `web/vite.config.ts` names the Function App's
            hostname as the dev proxy's default target. The standard bars
            Azure names from the docs, and this is code, but it is the same
            identifier.
      - [x] **5 · CI/CD and supply chain** — 9 items, done 2026-10-13 (A32).
            Passing already: what-if and actionlint on every PR, deploy order,
            no cancelled deploys, the web job's bundle check, SHA-pinned
            actions, CodeQL, Trivy, an SBOM, the NuGet licence gate.
            1. npm licences checked, dev dependencies included
               (`scripts/check-npm-licences.mjs`); the allow list grows to
               ISC, 0BSD, BSD-2-Clause, MIT-0, plus MPL-2.0, BlueOak-1.0.0 and
               CC0-1.0 for build tools only
            2. `X-Build` on every season response; the smoke test waits for
               this run's commit
            3. `nuget-license` pinned to 4.0.18
            4. `grep -m1` instead of `| head -1`
            5. Dependabot watches `/web`, with minor and patch grouped
            6. and the composite action
            7. `npm audit --omit=dev` fails the build; `source-map-js` 1.2.1 to
               1.2.2 fixed the one high finding, in the build tooling
            8. Stale comments in `security.yml`
            9. Web deploy stays parallel with functions (decided)
            381 tests pass, workflows parse, `dotnet format` clean, the
            licence script passes and refuses a planted GPL package.
      - [x] **5b · CI/CD, high-level review** — 10 items, done 2026-10-13.
            Removed: the deploy's second `dotnet test`; four of the web
            deploy's five checks (custom-domain CORS, "not source", "built
            against this API", the `/api` fallback), keeping the wait for this
            run's bundle; the deploy's Trivy vulnerability scan (the SBOM
            stays); CI's separate `tsc --noEmit` and "built, not source" step;
            `-warnaserror`, already a project setting; the advisory
            Sleeper-shaped-id step; the dependency-report artifact.
            Simplified: required secrets are `@minLength` on the Bicep
            parameters (the SQL admin id exactly a 36-character GUID), so the
            shell checks in both workflows are gone; narrative comments are one
            line each. The Bicep builds and lints clean, the workflows parse.
      - [x] **6 · Testing** — 8 items, done 2026-10-13.
            Passing already: every test name is a sentence (304 C#, all web).
            1. Axe runs over the whole page loaded (radar included), loading
               and failed (`web/tests/page.axe.test.tsx`)
            2. `sum(starters_points) == points` and slot alignment run over
               every captured week, as `ideal >= actual` already did
            3. (a) The season service's ordinary paths run on the captured
               league (`LeagueSocket.Captured()`); a new test assembles the
               real season with every player known and no breach. Invented
               data stays for constructed cases
            4. Weeks 4 and 5 captured (week 6 is in progress and was dropped);
               weeks 1–3 re-fetched unchanged; no secret values in the files.
               The byes came as explicit zeros, so a missing points entry is
               still only invented. The script now reads
               `League__CurrentLeagueId`
            5. Undo-and-show: eight regression tests from domains 3–4, each
               red with its fix undone and green with it restored — a failed
               read not cached, a stale blob missing, a live week refused, the
               placeholder dropped at read, a cached read the same instance, a
               player id as a string, an empty lineup column throwing, the
               identity fallback. (The log-once test and the licence script
               were shown failing when written.)
            6. The standard says the page's API payload stays invented, and why
            7. `coverlet.collector` removed; nothing collected coverage
            8. For the high-level review: `FantasyLeague.Core.Tests` holds every
               project's tests
            394 C# and 188 web tests pass; `dotnet format`, Prettier, lint and
            `tsc` clean.
- [ ] **T62** Rewrite the design for a prospective employer. Lead with the
      problem, the decisions and their trade-offs, and what was measured; keep
      the build-log history (corrections, amendments) one click away rather than
      inline.
- [ ] **T63** A GitHub Pages site for the project, including the runbook.

---

## Next · a panel for σ — 3 h `±`

σ came out of the standings table on 2026-10-07 (**A17**): its range mark was
illegible at table size, and it had shipped without the figure beside it.

- [ ] **T59** Consistency panel — 3 h `±` · σ with room to read it
      A panel of its own, one row per team, sharing a scale: the range mark
      from §04 (lowest to highest week, a tick at the mean, a ±σ band) drawn
      wide enough to read, with the σ figure beside it. The first draft of that
      mark, `SigmaDisplay`, with its `ConsistentTotals` type, `hasConsistency`
      guard and tests, was removed unused (A26); recover it from git history
      or rebuild it. Hidden, not empty, below
      four finalized weeks (**D1**). Decide whether CV belongs here too or
      stays on the chart (**D3**). Rank by σ, or keep the standings order?

---

## Deferred · season picker — 1 h

- [ ] **T56** Season picker in the masthead — 1 h · **waiting on August 2027**
      The masthead already prints the season; make that text the control. A
      `<select>` populated from `/api/seasons`, not a hardcoded list, so a new
      year appears the day T29 finds it. Season state lives in `App` because it
      is the argument to `useSeason`, which already takes it and is passed
      `null` today; `fetchSeasons` is already written and never called.
      Mirrored to `?season=`. Selecting a past season stops the poll with no new
      code — `useSeason` returns early when `payload.isCurrent` is false — but
      the page should also visibly settle: the "as of" line gives way to the
      final week, and the live affordances disappear.
      **`available` is `[2026]` until T29 runs, so this control has one option
      and changes nothing until the 2027 rollover.** Deliberately not part of
      T37.

## Deferred · the rest of M3 — 7 h

Moved behind M4 on 2026-09-14. Each was written into the plan before it was
clear what would and would not have input.

- [ ] **T29** Season chain walk — 2 h · **waiting on August 2027**
      Follows `previous_league_id` backwards from the configured league, upserting
      a `League` row per hop, and is what would populate `available` on
      `/api/seasons`. 2026 is the first season (**A4**), so the chain is one hop
      and the loop runs exactly once. It also cannot discover a new season on its
      own — the walk runs _backwards_ from `League__CurrentLeagueId`, so nothing
      changes until that app setting is updated at rollover (T45's checklist).
      Needs a `Core` record for a league season before `ILeagueStore` can be
      defined the way `IFinalizedWeekStore` was, and a `SeasonOverrides` property
      that `LeagueOptions` does not yet have. The page's `fetchSeasons` client
      call was removed unused (A26); add it back with the season picker (T56).
      `LeagueDto.PreviousLeagueId` and `ToSeasonsPayload`'s list of available
      seasons were removed unused too (T61 3b); add both back here.
- [ ] **T30** Historical backfill job — 2.5 h · **no input, and largely redundant**
      T28 writes every finalized week rather than only the most recent, and
      `WriteAsync` upserts, so **the current season already backfills itself**.
      That leaves prior seasons, of which there are none. When it is needed, the
      console harness is the right home rather than a deployed function — it
      already builds a whole season, the local Entra auth path is proven, and a
      once-per-season job does not need a permanent trigger surface in Azure.
      Register `AddFantasyData` behind a flag so the harness stays database-free
      by default.
- [x] **T31** Migrations in the pipeline — 2.5 h `±` · **done in #45** — the `migrate` job has passed on every deploy since, last on #58 (2026-10-05)
      Was deferred on "nothing to migrate yet, and M6 doesn't need a column
      (§08)." Both halves of that turned out wrong: M6 shipped
      `AddSlotAveragesJson` (`WeekScores.SlotAveragesJson`), and it sat
      **unapplied in production for four days** with nobody noticing, because
      nothing surfaced it as a deploy-blocking problem — it just made every
      `WeekScores` query throw `Invalid column name 'SlotAveragesJson'` and
      silently fall back to a live Sleeper rebuild on every request. Full
      story in `tasks/lessons.md`. This is exactly the risk the original
      deferral named; do this one next.

## Deferred · blob-mirrored finalized weeks — 2 h `±`

- [x] **T58** Mirror `WeekScore` to blob, same shape as A12's player map — 2 h `±`
      **Reasoning: A16.** SQL's free-tier auto-pause is the site's one
      disruptive cold start (D7); the fix is to stop the request path from
      touching SQL at all, not to cache around it harder (that makes the pause
      fire sooner, not less often) or migrate off SQL outright (a bigger
      rewrite than the problem currently needs). `FinalizeWeek` writes the
      current season's finalized weeks to blob alongside SQL — the current
      season is the only one it ever builds, which is also the only one the
      live site's 30 s poll actually requests; older seasons stay SQL-only.
      `IFinalizedWeekStore`'s read path serves from blob first and falls
      through to SQL on a cold or missing blob — the same thing every request
      does today, so that fallback needs no new design, and
      `CachedFinalizedWeekStore`'s existing `RemoveByTagAsync` on every write
      already covers invalidation once it wraps a store that writes blob too.
      Also restricts `FinalizeWeek`'s cron to active months (Sept–Feb, not
      Mar–Sept — the season opener usually falls inside September) so the
      offseason costs zero invocations rather than a weekly no-op. Full
      implementation in [`field-manual.md`](../docs/field-manual.md)'s T58
      section; reasoning updates in **A16**.

---

## Open follow-ups

Nothing here blocks anything.

- [x] **F1 · `Cv` reports 0.0 when the mean is zero but sigma is not.** Done 2026-10-12: `Cv` is null whenever the mean is zero, all-zero weeks included, matching what the chart already did client-side.
      `[-10, 10, -10, 10]` gives sigma 10 and CV 0.0, claiming perfect
      consistency for a team swinging ten points a week. It needs weekly totals
      that go negative, so it takes scoring settings this league may not have.
      The fix is one line: make `Cv` a `double?` and return null when the mean is
      zero, exactly as sigma returns null below four weeks.
- [x] **F2 · `Calculate([], 0)` throws "Sequence contains no elements".** Done 2026-10-12.
      Unreachable at the default `minWeeks: 4`, so it is only a caller error with
      a confusing message. `finalWeeks.Count == 0` in the guard settles it.
- [x] **F4 · The cache TTLs move the request budget from 2.2 to 12.2 req/min.**
      **Closed 2026-10-13 (T61 3, item 1).** The built TTLs stand: matchups
      15 s, state 5 min, rosters 5 min, users 1 h, about 4.4 req/min. §07, the
      README and CLAUDE.md now say so.
      matchups 30s to 5s, rosters 1h to 5min, state 5min to 1h. Far inside the
      50/min bucket, so nothing breaks, but §07's table and the "2.3 req/min"
      headline in the design and README are now wrong. Two are worth revisiting:
      matchups at 5s buys 25 seconds of staleness for six times the traffic when
      the client only polls every 30s (D6); and state at 1h saves 0.18 req/min
      while making the one value that gates `isFinal` up to an hour stale across
      Tuesday's rollover. Either restore the design's numbers or amend §07.
- [ ] **F5 · The stored player map is 840 KB, half of it property names.**
      Gzipping the blob takes it to 160 KB — measured, not estimated. Not worth
      doing while cold starts are invisible; worth knowing if they stop being.
- [ ] **F6 · The Trivy exception for `AZU-0012` expires 2027-03-01.**
      After that the storage-firewall finding returns and CI goes red with no
      code change behind it. The rationale is A11.
- [ ] **F7 · `<UserSecretsId>` was never added.** A2 describes user-secrets for
      local development; the projects use `local.settings.json` and an
      environment variable instead. Both keep the id out of the repo, so this is
      a documentation mismatch rather than a leak.

- [ ] **F8 · Spend is not quite $0.00.** $0.021 five days into October, so about
      $0.13 a month. Far under the $1 budget, but the design and README say
      $0.00 steady state. Find the meter in Cost Analysis (storage transactions or
      egress are the likely ones), then fix it or amend the claim.

F3 (the client timeout) was resolved and is recorded in `amendments.md`.

## Blocked on the season

Written, understood, waiting for games. Dates are the Tuesday each NFL week
finalizes, so the data is settled by then.

|        | Take on                                                    | What                  | Why it has to wait                                                                                                                                                                      |
| ------ | ---------------------------------------------------------- | --------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **B1** | **2026-09-15**                                             | Re-capture week 1     | The committed fixture is mid-week-1: only the Thursday game had been played, and four of six rosters sit on 0.00. Three of the six `ideal >= actual` assertions are currently `0 >= 0`. |
| **B2** | **2026-09-29**                                             | Capture weeks 2 and 3 | T06 asks for two or three real weeks. Also the first point at which waivers will have moved players, which is what `player-positions.json` staleness protection exists for.             |
| **B3** | **2026-10-06**                                             | First non-null sigma  | D1 returns null below four finalized weeks, so T07/T08's boundary case cannot be exercised against real data until week 4 is in.                                                        |
| **B4** | **week 5 or 6** — finalize dates 2026-10-13 and 2026-10-20 | Capture a bye week    | The only way to exercise a `players_points` entry being genuinely absent (the `0m` default) against real data. Week 1 has neither.                                                      |

Each is one command once the date passes:

```bash
export SLEEPER_LEAGUE_ID=...          # never committed; see A2
python scripts/capture_fixtures.py    # every completed week
```

**B1, B2 and B5 done 2026-10-05.** Weeks 1–3 captured after each had finalized,
replacing the mid-week-1 capture. Every roster has real points, and every
roster's starters sum to its total to the cent. Two binding tests had pinned
numbers from the partial week (19 players per roster, roster 1 on 16.92); they
now assert what always holds instead. The `ideal >= actual` test runs once per
captured week file and requires at least one genuine `ideal > actual` gap per
week (B5). 324 tests pass. `starters[i]` alignment re-checked: 191 of 191.
B3 and B4 remain.

Then re-run the suite. `A_roster_short_at_a_position_leaves_that_slot_empty`
pins per-roster fill counts from the week-1 capture and will need its expected
numbers refreshed when rosters change.

**B5 · Strengthen `ideal >= actual` once B1 and B2 land.** Today it passes with
almost no signal. With real scores it should also assert that at least one roster
has a genuine `ideal > actual` gap — otherwise the metric could silently be
returning `actual` and every test would still be green.

## Open questions

Q1, Q2, Q3, Q7 and Q8 were settled on 2026-09-10 against the live league; full
answers are in [`docs/amendments.md`](../docs/amendments.md).

- [ ] **Q4** Regular season only, or through the playoffs? —
      `settings.playoff_week_start` is `0`, i.e. unset. Check league settings.
- [x] **Q5** Minimum weeks before sigma appears — **four**, per D1, and
      implemented that way in `populationSigma`. Below four every consistency
      figure is null rather than zero.
- [x] **Q6** **Both, in different places.** The table keeps σ because it is the
      number people say out loud; T42 gives the chart a σ/CV toggle. σ is
      scale-dependent, so CV is the fair comparison across teams.

## Amendments

Recorded in [`docs/amendments.md`](../docs/amendments.md), with the reasoning.
Listed here only so their implementation state is visible in one place.

|         | What                                                                     | State                                                                    |
| ------- | ------------------------------------------------------------------------ | ------------------------------------------------------------------------ |
| **A1**  | Exclude "POON SQUAD" from every metric, matched on owner `user_id`       | done — filtered at the read boundary, verified in Azure                  |
| **A2**  | League and user IDs stay out of the repo                                 | done — see F7 for the one mismatch                                       |
| **A3**  | Keep the slot eligibility map general, not pruned to 2026                | done — the guard existed; its test was un-skipped on 2026-10-12          |
| **A4**  | No season chain to walk yet                                              | done — first exercised August 2027                                       |
| **A5**  | The `starters` alignment holds, verified not assumed                     | done                                                                     |
| **A6**  | An unset team name is absent, not an empty string                        | done                                                                     |
| **A7**  | A commissioner override wins over scored points                          | done                                                                     |
| **A8**  | An unresolved player is reported, never dropped                          | done                                                                     |
| **A9**  | Both invariants compare against Sleeper's scored points                  | done                                                                     |
| **A10** | Excluded owners are a list, carried as one comma-separated secret        | done — proven on the deployed app                                        |
| **A11** | The storage firewall stays open, exception expires 2027-03-01            | done — see F6                                                            |
| **A12** | The player map is stored in blob, because instance memory is not storage | done                                                                     |
| **A13** | No commissioner overrides; `WeekScore` stores the actual only            | decided — T25                                                            |
| **A14** | The stored key leads on `Season`, not `LeagueId`                         | decided — T25                                                            |
| **A15** | Two Microsoft natives exempt from the licence allowlist                  | done                                                                     |
| **A16** | Finalized weeks mirrored to blob, to remove SQL from the request path    | done — **T58**                                                          |
| **A17** | σ leaves the standings table for a panel of its own                      | table done — panel is **T59**                                           |
| **A18** | A licence shipped as a file is declared by version, not exempted         | done — Apache.Arrow, in `.github/license-overrides.json`                |
| **A19** | `League`, `Player` and `Team` dropped; `League` returns with T29         | done — migration `DropUnusedTables`, applied on next deploy             |
| **A20** | The blob caches the current season; SQL keeps every season               | done — T61 1b, items 5–6                                                |
| **A21** | Hosts compose, Data owns storage, policy is applied once                 | done — T61 1b, items 11–16                                              |
| **A22** | Sleeper splits into Sleeper and Seasons; naming rules settle             | done — T61 1b, items 18–21                                              |
| **A23** | `Pts` covers roster totals; tests may use `!` uncommented                | done — in `docs/standards.md`                                           |
| **A24** | The front end meets its standards; Prettier and a bundle check added     | done — T61 domain 2                                                     |
| **A25** | Colour follows the team; game windows cover the whole NFL week           | done — T61 2b, items 5 and 10                                           |
| **A26** | Unused code waits for its task; `?radar` removed; a failure is a panel   | done — T61 2b                                                           |
| **A27** | Blob judged by write time; store refuses live weeks; placeholder dropped early | done — T61 3, items 4–9                                           |
| **A28** | League hashed in blob names; bucket 25/min per instance; `PlayersJson` | done — T61 3, items 11–13                                               |
| **A29** | Stored weeks keep their lineups, in one shape for blob and SQL        | done — T61 3b, items 12–13                                              |
| **A30** | No stored keys; budget alert at fifty cents; an exception alert         | done — T61 4; two manual steps before merging                           |
| **A31** | The budget leaves the pipeline; the cost script and firewall dance go | done — T61 4b, items 6, 7, 11, 15                                       |
| **A32** | The supply chain covers the web app: npm licences, audit, Dependabot | done — T61 5                                                            |

---

## Review

_One entry per work session: what changed, what was verified, what is still
open. Detail that outlived the session has moved to `amendments.md` or
`lessons.md`; these are pointers, not a transcript._

### 2026-09-16 — M4 built, M5 built bar the domain

The page is real. Standings, an interactive chart, hover, four control rows and a
gated radar panel, in both themes, with no horizontal scrollbar at 375 px.

**M4 closed.** T36 to T40 landed on top of the design system: a D3 line chart
with React owning every node (D9), chips and segmented controls mirrored to the
query string, hover scoped to the chart panel, and the responsive and
accessibility pass. The manual's M4 section is now a built reference; the traps
it cost are recorded there rather than in the tasks.

**M5 closed bar T57.** T41's three display modes and T42's σ variants share one
pure module, `pointCalculation.ts`. T43 replaced the bare loading and error text
with `Skeleton`, `LoadFailed` and `StaleBanner`, and fixed an ordering bug in
`App` that let one failed poll wipe a live page. T44 shipped
`scripts/verify-cost.sh`, which found that the telemetry cap lives on the Log
Analytics workspace rather than the App Insights component. T45 rewrote the
runbook as a setup guide someone else could follow.

**Tests went from 22 to 106**, covering every hook, the fetch layer, the pure
maths and four components. Each new suite was mutation-tested: removing the
drop-at-default rule, the never-zero guard, the enum validation, the non-JSON
guard, or reversing the chart's paint order all fail the suite.

**Two bugs were structural rather than typos.** `App` called `useRelativeTime`
below a conditional return, which blanks the page under the Rules of Hooks. And
`Skeleton` reproduced part of `Panel` instead of using it, so the title and table
moved when data arrived — three separate causes, found only by measuring in a
browser rather than reading the CSS.

**T57 needs DNS**, not code. A subdomain of a domain already owned, so the
$0.00/month claim survives.

### 2026-09-14 — M3 built, then deliberately cut short

Framework bumped to **.NET 10** — .NET 9 leaves support on Flex Consumption on
2026-11-10, 57 days out and mid-season. Azure's own runtime list settled it.

M3's substance landed: EF Core model and `Initial` migration applied to a real
`GP_S_Gen5_2` database in **westus2** (eastus and eastus2 are both restricted for
General Purpose on this subscription), `IFinalizedWeekStore` in `Core` with a SQL
adapter and a caching decorator in `Data`, `SeasonService` reading stored weeks,
and `FinalizeWeek` writing every finalized week each Tuesday.

**`TeamWeek` gained `Players`** so a stored week is re-solvable on its own — the
player map is refreshed daily and overwritten, so a week rebuilt against today's
map would use the wrong eligibility.

Then **T29, T30 and T31 were deferred**. T29 needs a prior season and cannot find
one until the August 2027 rollover. T30 is largely redundant, because T28 writes
all finalized weeks rather than the most recent and `WriteAsync` upserts — the
current season backfills itself. T31 has one migration, already applied. M4 is
next because it is the first thing anyone else can look at.

Three deploy failures on the way, each a different lesson: a region restricted
per-subscription, a quoted variable name (`'sqlName'`), and `$SQL_ADMIN_ANME` —
a transposed pair of letters that made Azure reject `ExternalAdminProperties`
with no hint which field was empty. `set -u` now guards the deploy step.

Suite: 292 passing, 1 skipped.

### 2026-09-13 — deployed, broken, fixed

First real deploy. Infrastructure and the Flex Consumption deployment both
worked first time; the endpoint then returned **500 on every request**. App
Insights had it: nothing in the Functions host called `RefreshAsync`, because
only the console harness ever had. Fixed properly rather than patched — the
player map now lives in blob storage with a daily timer, so a cold instance
reads 840 KB instead of fetching 14.65 MB (**A12**).

Also this session: the excluded-owner secret became a list (**A10**); the
resolution logic moved out of a test's private copy into the mapper, so those
tests now exercise real code; a Trivy storage-firewall finding was suppressed
with a dated exception (**A11**); and three CI failures were traced — a
`v`-prefixed Trivy tag, a `.gitattributes` gap that made `dotnet format` fail
only on Linux, and OIDC subjects that now carry GitHub's immutable org and repo
IDs.

**The live league ID was found committed in `HostRegistrationTests.cs`** and
expunged from six local commits. It had never reached the remote. The A2 gate in
`security.yml` is what caught it.

Suite: 268 passing, 1 skipped.

### 2026-09-12 — M2 cloud foundation

Bicep, OIDC and the three workflows. Y1 Consumption could not be provisioned —
the subscription quota is zero — so the plan is Flex Consumption (FC1), which
the design does not describe. `what-if` clean, budget and ingest cap in place.

### 2026-09-11 — M1 closed, M2 endpoints

`FantasyLeague.Console` runs end to end against the live league and both
invariants hold across every week. **This is the milestone the design calls out:
thirty hours in, the problem is solved.** Everything after buys a live website
rather than a better answer.

Then the API: two endpoints, with weak ETags hashing every week's scores rather
than just the week number — the first version would have frozen the page
mid-Sunday.

### 2026-09-10 — M0 and the solver bug

Domain types, slot parsing, the bitmask DP solver, consistency maths, and the
Sleeper recon that answered Q1, Q2, Q3, Q7 and Q8 against the real league.

**The solver had a real bug**: parent pointers went stale across the rolling
array, so `Points` was correct but `Lineup` was wrong in roughly 23% of cases.
Found by fuzzing against exhaustive search — 20,000 cases, zero value mismatches
and 4,623 duplicate lineups. Replaced with a chosen-set bitmask.

The repo moved into `FantasyFootballWebsite`, the open questions were answered,
the secrets policy was set (**A2**), and the docs were synced from design v6.

