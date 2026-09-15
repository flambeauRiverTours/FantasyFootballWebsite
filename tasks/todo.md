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

| | Task | State |
|---|---|---|
| T01–T03 | Scaffold, recon, domain types | done |
| T04 | Slot parsing and eligibility | done — **A3's unknown-slot guard still missing**, its test sits skipped |
| T05 | Bitmask DP solver | done |
| T06 | Solver fixtures and tests | **blocked on data** — B1–B4 |
| T07–T08 | Consistency maths and edge cases | done — F1, F2 open |
| T09–T13 | DTOs, client, rate limit, resilience, cache | done — F4 open |
| T14 | Player dump job | done, **and extended**: blob-backed store and daily timer (A12) |
| T15–T16 | Season assembly, console harness | done |
| T17–T19 | Functions host, endpoints, caching semantics | done |
| T20 | Bicep template | done — Flex Consumption, not the design's Consumption plan |
| T21 | GitHub to Entra OIDC | done — needed immutable-ID subjects |
| T22 | `ci.yml` | done — build, test, format, vulnerable packages, plus a bicep what-if job. No vite build; `web/` arrives at T32 |
| T23 | `deploy.yml` | done — infra, then functions with SBOM and scan, then a smoke test. `web` job stubbed until T32 |
| T24 | CORS and first deploy shakeout | **in progress** |

### T24 · what is left

CORS is verified working in Azure: `allowedOrigins` is exactly the SWA hostname,
and a foreign origin is refused. Preview environments are deliberately **not**
allowed — production origin only, decided rather than discovered. `devOrigins`
exists as a bicep parameter, empty by default, for front-end development against
the deployed API when T32 needs it.

- [ ] Run the full verification pass after the next green deploy. The runbook is
      in the field manual under **T24**.
- [ ] `AZURE_STATIC_WEB_APPS_API_TOKEN` is still not set. One command, and it
      only matters once T32 gives the `web` job something to deploy.
- [ ] Confirm on a **second** cold start that the log says "Adopted the stored
      player map" rather than "Refreshing player map from Sleeper API". This is
      the only evidence A12 solved the problem rather than moving it.

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

**The remaining three are deferred to the end** — see *Deferred* below. None is
blocked on effort; each is waiting on something that does not exist yet.

## M4 · The page — 25.5 h

First milestone anyone else in the league can look at, and the first that will
generate opinions.

- [x] **T32** Frontend scaffold — 2 h
      Vite 8, React 19, TypeScript. Types written from the live response, ETag-aware
      fetch layer, `useSeason` with visibility-gated polling, dev proxy past CORS.
      `web` jobs restored in ci.yml and wired to SWA in deploy.yml.
- [ ] **T33** Design system — 3 h
      OKLCH tokens, four type sizes, one spacing scale, dark designed rather than inverted.
- [ ] **T33b** Panel layout — 1.5 h
      One visualisation per screen, sticky-stacked like ellipsus.com. Native CSS,
      no scroll-jacking library. `100svh`, `proximity` snap, and it must still
      read as a plain document under `prefers-reduced-motion`. **Three panels
      built, the radar hidden** until T48 fills `leagueSlotAverages` — the gate is
      derived from the payload, so there is no flag to flip later.
- [ ] **T34** Standings table — 3 h
      Sort on points, tabular figures, sigma em-dash, week-range column header.
- [ ] **T35** Inline range mark — 2.5 h
      Min to max rule with a mean tick and a shaded band, on one shared scale.
- [ ] **T36** D3 line chart — 4 h `±`
      Scales, axes and path generators; React renders every node.
- [ ] **T37** Chart controls — 3 h
      Season, team multi-select, metric, display mode. State in the query string.
- [ ] **T38** Hover interaction — 2 h
      Raise one team, mute the rest, with a keyboard equivalent.
- [ ] **T39** Polling — 2 h
      Gated on tab visibility and game windows; stops on a past season.
- [ ] **T40** Responsive and a11y — 2.5 h
      Keyboard paths, focus states, table that scrolls in its own container.

## M5 · Modes, ops and docs — 9.5 h

Deliberately last: cheap, taste-driven, easier to judge against real data.

- [ ] **T41** Display modes — 2 h · per-week and vs-league-median
- [ ] **T42** Sigma variants — 2 h · expanding vs rolling window, sigma vs CV
- [ ] **T43** Loading and error states — 2 h · including the SQL cold resume
- [ ] **T44** Cost guardrails — 2 h · ingest cap, budget alert, SQL pause verified
- [ ] **T45** README and runbook — 1.5 h · chiefly the August rollover checklist

## M6 · Positional radar — 10 h

Improves most with a full season to look at.

- [ ] **T46** Slot attribution — 2.5 h · `starters[i]` maps to slot `i`, plus the mid-season rules-change guard
- [ ] **T47** League reference and ratios — 1.5 h · average first, divide second
- [ ] **T48** Slot block in the API — 1 h · fills the two empty objects §09 already reserves, **and thereby reveals the radar panel**
- [ ] **T49** Radar chart — 3.5 h `±` · axis order fixed and never re-sorted
- [ ] **T50** Diverging bar alternative — 1.5 h · the same data on a linear axis

## M7 · Motion and polish — 7.5 h · **optional**

Cut the entire milestone and the site still does its job — listed separately so
that cutting it is a decision rather than an omission.

- [ ] **T51** Motion setup — 1 h · `LazyMotion`, `useReducedMotion` everywhere
- [ ] **T52** Table layout animation — 1 h · a Sunday overtake physically slides
- [ ] **T53** Chart reveal and transitions — 2 h `±`
- [ ] **T54** Section transitions — 1 h
- [ ] **T55** Football loader — 2.5 h

---

## Deferred · the rest of M3 — 7 h

Moved behind M4 on 2026-09-14. Each was written into the plan before it was
clear what would and would not have input.

- [ ] **T29** Season chain walk — 2 h · **waiting on August 2027**
      Follows `previous_league_id` backwards from the configured league, upserting
      a `League` row per hop, and is what would populate `available` on
      `/api/seasons`. 2026 is the first season (**A4**), so the chain is one hop
      and the loop runs exactly once. It also cannot discover a new season on its
      own — the walk runs *backwards* from `League__CurrentLeagueId`, so nothing
      changes until that app setting is updated at rollover (T45's checklist).
      Needs a `Core` record for a league season before `ILeagueStore` can be
      defined the way `IFinalizedWeekStore` was, and a `SeasonOverrides` property
      that `LeagueOptions` does not yet have.
- [ ] **T30** Historical backfill job — 2.5 h · **no input, and largely redundant**
      T28 writes every finalized week rather than only the most recent, and
      `WriteAsync` upserts, so **the current season already backfills itself**.
      That leaves prior seasons, of which there are none. When it is needed, the
      console harness is the right home rather than a deployed function — it
      already builds a whole season, the local Entra auth path is proven, and a
      once-per-season job does not need a permanent trigger surface in Azure.
      Register `AddFantasyData` behind a flag so the harness stays database-free
      by default.
- [ ] **T31** Migrations in the pipeline — 2.5 h `±` · **nothing to migrate yet**
      Not blocked, just not yet earning its keep: there is one migration and it is
      applied. The risk of deferring is that the *next* schema change gets applied
      by hand and the step is never built. Worth doing the moment a second
      migration exists — which M6 does not require, since the radar adds no
      columns (§08).

---

## Open follow-ups

Nothing here blocks anything.

- [ ] **F1 · `Cv` reports 0.0 when the mean is zero but sigma is not.**
      `[-10, 10, -10, 10]` gives sigma 10 and CV 0.0, claiming perfect
      consistency for a team swinging ten points a week. It needs weekly totals
      that go negative, so it takes scoring settings this league may not have.
      The fix is one line: make `Cv` a `double?` and return null when the mean is
      zero, exactly as sigma returns null below four weeks.
- [ ] **F2 · `Calculate([], 0)` throws "Sequence contains no elements".**
      Unreachable at the default `minWeeks: 4`, so it is only a caller error with
      a confusing message. `finalWeeks.Count == 0` in the guard settles it.
- [ ] **F4 · The cache TTLs move the request budget from 2.2 to 12.2 req/min.**
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

F3 (the client timeout) was resolved and is recorded in `amendments.md`.

## Blocked on the season

Written, understood, waiting for games. Dates are the Tuesday each NFL week
finalizes, so the data is settled by then.

| | Take on | What | Why it has to wait |
|---|---|---|---|
| **B1** | **2026-09-15** | Re-capture week 1 | The committed fixture is mid-week-1: only the Thursday game had been played, and four of six rosters sit on 0.00. Three of the six `ideal >= actual` assertions are currently `0 >= 0`. |
| **B2** | **2026-09-29** | Capture weeks 2 and 3 | T06 asks for two or three real weeks. Also the first point at which waivers will have moved players, which is what `player-positions.json` staleness protection exists for. |
| **B3** | **2026-10-06** | First non-null sigma | D1 returns null below four finalized weeks, so T07/T08's boundary case cannot be exercised against real data until week 4 is in. |
| **B4** | **week 5 or 6** — finalize dates 2026-10-13 and 2026-10-20 | Capture a bye week | The only way to exercise a `players_points` entry being genuinely absent (the `0m` default) against real data. Week 1 has neither. |

Each is one command once the date passes:

```bash
export SLEEPER_LEAGUE_ID=...          # never committed; see A2
python scripts/capture_fixtures.py    # every completed week
```

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
- [ ] **Q5** Minimum weeks before sigma appears — four, or five? (Assumed: four.)
- [ ] **Q6** Should the table show sigma or CV? Revisit once real spread is
      visible after B3.

## Amendments

Recorded in [`docs/amendments.md`](../docs/amendments.md), with the reasoning.
Listed here only so their implementation state is visible in one place.

| | What | State |
|---|---|---|
| **A1** | Exclude "POON SQUAD" from every metric, matched on owner `user_id` | done — filtered at the read boundary, verified in Azure |
| **A2** | League and user IDs stay out of the repo | done — see F7 for the one mismatch |
| **A3** | Keep the slot eligibility map general, not pruned to 2026 | **partly** — the map is general, the unknown-slot guard is still missing |
| **A4** | No season chain to walk yet | done — first exercised August 2027 |
| **A5** | The `starters` alignment holds, verified not assumed | done |
| **A6** | An unset team name is absent, not an empty string | done |
| **A7** | A commissioner override wins over scored points | done |
| **A8** | An unresolved player is reported, never dropped | done |
| **A9** | Both invariants compare against Sleeper's scored points | done |
| **A10** | Excluded owners are a list, carried as one comma-separated secret | done — proven on the deployed app |
| **A11** | The storage firewall stays open, exception expires 2027-03-01 | done — see F6 |
| **A12** | The player map is stored in blob, because instance memory is not storage | done |
| **A13** | No commissioner overrides; `WeekScore` stores the actual only | decided — T25 |
| **A14** | The stored key leads on `Season`, not `LeagueId` | decided — T25 |
| **A15** | Two Microsoft natives exempt from the licence allowlist | done |

---

## Review

_One entry per work session: what changed, what was verified, what is still
open. Detail that outlived the session has moved to `amendments.md` or
`lessons.md`; these are pointers, not a transcript._

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
