# Ideal Points — Task List

50 required tasks across 7 milestones, **108 h** estimated (**136 h** if bicep,
OIDC and D3-inside-React are all new), plus an optional eighth milestone worth
7.5 h. Ordered so the work most likely to be wrong happens first and needs no
cloud account to prove.

- Live tracker (shared, tickable): https://claude.ai/code/artifact/30817425-417f-460d-aedc-cfb5b91e2e65
- Step-by-step instructions per task: [`docs/field-manual.md`](../docs/field-manual.md)
- Rationale for every decision: [`docs/design.md`](../docs/design.md)

`±` marks a soft estimate that could run double. 28 of the 108 hours carry it,
clustered in first contact with Azure (T20, T21, T24, T26, T31) and first
contact with reality (T06, T16, T36, T49).

---

## M0 · Core domain — 13 h

One hour of curl, then pure functions over fixtures. The recon goes first so
everything after it is written against the real league rather than an assumption
about it — and this is where the bugs that matter live.

- [x] **T01** Solution scaffold — 1.5 h
      Five projects, `Directory.Build.props`, nullable enabled, warnings as errors, `.editorconfig`.
- [x] **T02** Sleeper recon — 1.5 h
      `curl` the real league: `roster_positions`, captured week fixtures, starters alignment, Q1–Q4 and Q7.
- [x] **T03** Domain types — 1 h
      `Slot`, `RosteredPlayer`, `TeamWeek`, `TeamSeason`. Records, no framework types.
- [x] **T04** Slot parsing and eligibility — 1.5 h
      `roster_positions` to startable slots; FLEX, SUPER_FLEX, REC_FLEX, WRRB_FLEX, IDP_FLEX.
      Code done and covered by 28 tests in `SlotTests.cs`. **Not closed:** A3's
      unknown-slot guard is still missing — its test sits skipped, ready to enable.
- [x] **T05** Bitmask DP solver — 2 h
      Exact ideal lineup over 2^11 slot states. `dp.Max()`, not `dp[full]`.
      Points and lineup both verified against exhaustive search — 48 tests plus a
      50,000-case fuzz, zero discrepancies.
- [~] **T06** Solver fixtures and tests — 3 h `±`
      All four named cases written and passing; fixtures captured and reproducible
      via `scripts/capture_fixtures.py`. **Blocked on data** — see B1–B4 below.
- [x] **T07** Consistency maths — 1 h
      Population sigma, CV, mean/min/max. Returns null below four final weeks.
- [x] **T08** Sigma edge-case tests — 1.5 h
      Zero, one and three weeks; a genuine 0.0 score; a non-final week excluded.
      20 tests in `SpreadCalculatorTests.cs`, all passing. Two edges the manual
      does not list are open — see below.

## M1 · Sleeper client — 16 h

Ends with a console app printing the real standings and asserting both
invariants across a whole season.

- [x] **T09** DTOs and JSON config — 2 h
      Six DTOs, shared `SleeperJson.Options`, and `SleeperMapper` as the single
      wire-to-domain boundary. 20 binding tests against the real payloads.
      **A7 (`custom_points`) still wants confirming against Sleeper's docs.**
- [x] **T10** Typed HttpClient — 2 h
      state, league, users, rosters, matchups, players, registered through
      `AddSleeperClient` so the client and DTOs stay internal. 24 tests over a
      stub handler — no network. **Open decision: F3, the client timeout.**
- [x] **T11** Token-bucket handler — 2 h
      `DelegatingHandler` at 50/min, shared by HTTP and timer paths. Assembled
      inside `AddSleeperClient` so the client cannot be registered without it.
- [x] **T12** Polly policies — 1 h
      `AddStandardResilienceHandler`, outermost so every retry spends its own
      token. Local rate-limit refusals are excluded from retry.
- [x] **T13** Single-flight cache — 2 h
      Per-key TTL, concurrent callers await one in-flight fetch.
- [x] **T14** Player dump job — 1.5 h
      `PlayerMapRefresher`, a singleton holding the projected map. The
      `[TimerTrigger]` that calls it and the `Player` table it should persist to
      both belong to M3.
- [x] **T15** Season assembly — 2.5 h
      Matchups to per-team weekly actual and ideal. 19 tests. Both invariants
      compare against Sleeper's scored points rather than a commissioner
      override — see **A9**.
- [x] **T16** Console harness and live verification — 3 h `±`
      Prints the real table, then asserts `starters==points` and `ideal>=actual`
      across every week. **Both hold against the live league.** Exit code is
      non-zero on a violation or an unresolved player.

## M2 · API and cloud foundation — 18.5 h

Deployed and reachable, still no database.

- [x] **T17** Functions host — 2 h
      Isolated worker, DI, options binding, `local.settings.json`.
- [x] **T18** Season endpoints — 2 h
      `GET /api/season` and `/api/seasons`, served from memory.
- [x] **T19** Caching semantics — 1.5 h
      ETag, 30 s live vs immutable historical, 404 listing available seasons.
- [x] **T20** Bicep template — 4 h `±`
      SWA, Function App, storage, App Insights, resource group scope.
- [x] **T21** GitHub to Entra OIDC — 3 h `±`
      App registration, federated credential, RBAC. No secrets in the repo.
- [x] **T22** `ci.yml` — 1.5 h
      Restore, build, test, `dotnet format` check, vite build.
- [ ] **T23** `deploy.yml` — 2.5 h
      Bicep what-if then deploy, functions deploy, SWA deploy.
- [ ] **T24** CORS and first deploy shakeout — 2 h `±`
      The gap between what works locally and what works deployed.

## M3 · Persistence — 17 h

Finalized weeks become permanent; history becomes reachable.

- [ ] **T25** EF Core model — 3 h
      Context, four entities, initial migration.
- [ ] **T26** Azure SQL in bicep — 3 h `±`
      Free-tier serverless, pause-on-limit, managed-identity connection.
- [ ] **T27** Read path — 2 h
      Hydrate the season on cold start; SQL off the hot path.
- [ ] **T28** Finalize timer — 2 h
      Tuesday morning: freeze the completed week, recompute sigma.
- [ ] **T29** Season chain walk — 2 h
      Follow `previous_league_id` back, upsert a `League` row per hop.
- [ ] **T30** Historical backfill job — 2.5 h
      One-shot per prior season, roughly 21 calls, never on a request path.
- [ ] **T31** Migrations in the pipeline — 2.5 h `±`
      Firewall open, migrate, close in an `always()` step.

## M4 · The page — 24 h

First milestone anyone else in the league can look at.

- [ ] **T32** Frontend scaffold — 2 h
      Vite, React, TypeScript, SWA config, typed fetch layer.
- [ ] **T33** Design system — 3 h
      Tokens, type scale, light and dark, both designed rather than inverted.
- [ ] **T34** Standings table — 3 h
      Sort on points, tabular figures, sigma em-dash, week-range column header.
- [ ] **T35** Inline range mark — 2.5 h
      Min to max rule with a mean tick and a shaded ±σ band.
- [ ] **T36** D3 line chart — 4 h `±`
      Scales, axes and path generators; React renders every node.
- [ ] **T37** Chart controls — 3 h
      Season, team multi-select, metric, display mode.
- [ ] **T38** Hover interaction — 2 h
      Raise one team, mute the rest. Twelve colours is not a legend.
- [ ] **T39** Polling — 2 h
      30 s, gated on tab visibility and game windows; stops on a past season.
- [ ] **T40** Responsive and a11y — 2.5 h
      Keyboard paths, focus states, table that scrolls in its own container.

## M5 · Modes, ops and docs — 9.5 h

Deliberately last: cheap, taste-driven, easier to judge against real data.

- [ ] **T41** Display modes — 2 h
      Per-week and vs-league-median. Four lines of maths, large legibility win.
- [ ] **T42** Sigma variants — 2 h
      Expanding vs rolling window, raw sigma vs coefficient of variation.
- [ ] **T43** Loading and error states — 2 h
      Including the SQL cold-resume wait and an un-backfilled season.
- [ ] **T44** Cost guardrails — 2 h
      App Insights ingest cap, budget alert, SQL pause-on-limit verified.
- [ ] **T45** README and runbook — 1.5 h
      Chiefly the August rollover checklist, which is one config value.

## M6 · Positional radar — 10 h

Depends on an unverified property of Sleeper's payload (the `starters[i]` ↔
slot `i` alignment T02 checks), and improves most with a full season to look at.

- [ ] **T46** Slot attribution — 2.5 h
      `starters[i]` maps to slot `i`. Per-slot weekly averages, plus the mid-season rules-change guard.
- [ ] **T47** League reference and ratios — 1.5 h
      Season means per slot, league means, then the ratio. Average first, divide second.
- [ ] **T48** Slot block in the API — 1 h
      `slotOrder`, `leagueSlotAverages`, and per-team `slotAverages` on `/api/season`.
- [ ] **T49** Radar chart — 3.5 h `±`
      Axes from league rules, reference circle at 1.0, axis order fixed and never re-sorted.
- [ ] **T50** Diverging bar alternative — 1.5 h
      The same data on a linear axis, for anyone who wants to read exact values.

## M7 · Motion and polish — 7.5 h · **optional**

Optional throughout. Cut the entire milestone and the site still does its job —
listed separately so that cutting it is a decision rather than an omission.

- [ ] **T51** Motion setup — 1 h
      `LazyMotion` with the `m` component, plus a `useReducedMotion` pass over every animated element.
- [ ] **T52** Table layout animation — 1 h
      The `layout` prop on standings rows, so a Sunday overtake physically slides past.
- [ ] **T53** Chart reveal and transitions — 2 h `±`
      `pathLength` draw-on; crossfade rather than tween between display modes.
- [ ] **T54** Section transitions — 1 h
      `startViewTransition` where supported, instant swap where not.
- [ ] **T55** Football loader — 2.5 h
      Vertical toss on phones, punt arc on wider screens, comic-book spin arcs, explanatory copy after 4 s.

---

## Open follow-ups from T08

Neither blocks anything. Both are the same shape as D1's argument that absence
should be a type the caller handles rather than a fabricated zero.

- [ ] **F1 · `Cv` reports 0.0 when the mean is zero but sigma is not.**
      `SpreadCalculator` guards the division with `meanD == 0 ? 0 : sigma / meanD`.
      For four all-zero weeks that is honest — sigma is zero too. But
      `[-10, 10, -10, 10]` gives sigma 10 and **CV 0.0**, which claims perfect
      consistency for a team swinging ten points a week. That needs weekly totals
      that go negative, so it takes scoring settings this league may not have —
      but the fix is one line: make `Cv` a `double?` and return null when the mean
      is zero, exactly as sigma returns null below four weeks. A gap in the D3
      chart toggle is honest; a zero is not.
- [ ] **F2 · `Calculate([], 0)` throws `InvalidOperationException`.**
      `Average()` on an empty sequence, surfacing as "Sequence contains no
      elements" rather than anything about weeks. Unreachable at the default
      `minWeeks: 4`, so this is only a caller error with a confusing message.
      `finalWeeks.Count == 0` in the guard would settle it.

- [x] **F3 · Resolved: the resilience pipeline owns the timeout.**
      `AddStandardResilienceHandler` sets `HttpClient.Timeout` to
      `InfiniteTimeSpan` and **overrides any value configured on the client** —
      verified: a client with an explicit 20 second timeout still comes out
      infinite once resilience is added. That is deliberate, because
      `HttpClient.Timeout` covers the whole operation including retries and would
      otherwise cut the pipeline off part way through, surfacing as a bare
      `TaskCanceledException` naming no cause. The real budget is the pipeline's
      `TotalRequestTimeout`, 30 seconds, with a 10 second `AttemptTimeout` inside
      it. Nothing to change; the arrangement is now asserted by
      `The_pipeline_owns_the_timeout_not_the_client`.

- [ ] **F4 · The cache TTLs move the request budget from 2.2 to 12.2 req/min.**
      matchups 30s → 5s (2.0 → 12.0), rosters 1h → 5min (0.02 → 0.20), state 5min
      → 1h (0.20 → 0.02). Still far inside the 50/min bucket, so nothing breaks,
      but §07's table and the "≤ 2.3 req/min" headline in the design and README
      are now wrong. Two are worth revisiting: matchups at 5s buys 25 seconds of
      worst-case staleness for 6× the traffic, because the *client* only polls
      every 30s (D6); and state at 1h saves 0.18 req/min while making the one
      value that gates `isFinal` up to an hour stale across Tuesday's rollover.
      Either restore the design's numbers or amend §07 to match reality.

## Blocked on the season

Work that is written, understood and simply waiting for games to be played.
Dates are the Tuesday each NFL week finalizes, so the data is settled by then.

| | Take on | What | Why it has to wait |
|---|---|---|---|
| **B1** | **2026-09-15** | Re-capture week 1 | The committed fixture is mid-week-1: only the Thursday game had been played, and four of six rosters sit on 0.00. Three of the six `ideal >= actual` assertions are currently `0 >= 0`. |
| **B2** | **2026-09-29** | Capture weeks 2 and 3 | T06 asks for two or three real weeks. By this date all three are final. Also the first point at which waivers will have moved players, which is what `player-positions.json` staleness protection exists for. |
| **B3** | **2026-10-06** | First non-null σ | D1 returns null below four finalized weeks, so T07/T08's boundary case cannot be exercised against real data until week 4 is in. |
| **B4** | **week 5 or 6** — confirm from the NFL schedule; finalize dates are 2026-10-13 and 2026-10-20 | Capture a bye week | The only way to exercise a `players_points` entry being genuinely absent (the `0m` default) and a bye-shaped partial fill against real data. Week 1 has neither — every rostered player had a points entry. |

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

Q1, Q2, Q3, Q7 and Q8 were settled on 2026-09-10 against the live league.
Full answers and their consequences are in
[`docs/amendments.md`](../docs/amendments.md).

- [x] **Q1** League ID and `roster_positions` — 6 rosters; 11 startable slots:
      `QB, QB, RB, RB, WR, WR, TE, FLEX, SUPER_FLEX, K, DEF`, plus 8 `BN`.
      The league ID is a secret (**A2**).
- [x] **Q2** SUPER_FLEX or IDP? — **SUPER_FLEX yes, IDP no.** Two QB slots *and*
      a superflex. The eligibility map stays general anyway (**A3**).
- [x] **Q3** Should IR players count toward ideal? — **Moot**; the league has no
      `IR` or `TAXI` slots. The design's default (exclude) stands if one appears.
- [ ] **Q4** Regular season only, or through the playoffs? —
      `settings.playoff_week_start` is `0`, i.e. unset. Check league settings.
- [ ] **Q5** Minimum weeks before σ appears — four, or five? (Assumed: four.)
- [ ] **Q6** Should the table show σ or CV? Revisit once real spread is visible.
- [x] **Q7** Is the league's season chain intact? — **2026 is the first season.**
      Null `previous_league_id` is legitimate; no `SeasonOverrides` needed (**A4**).
- [x] **Q8** How many distinct starting slots? — **8**, comfortably readable as a
      radar. No axis merging needed this season.

## Local amendments

Recorded in [`docs/amendments.md`](../docs/amendments.md), which survives an
artifact re-sync where `docs/design.md` and `docs/field-manual.md` do not.

- [ ] **A1** Exclude the dummy team "POON SQUAD" from every metric — as a row
      *and* from every league-wide aggregate (median, `leagueSlotAverages`).
      Match on owner `user_id`, never team name or `roster_id`. Store its rows;
      filter at the read boundary. **Touches T15, T18, T47 — needs its own task
      in the published tracker.**
- [ ] **A2** Keep league IDs and user IDs out of the repo — user-secrets locally,
      App Settings in Azure, a GitHub secret in CI, and `@secure()` on the bicep
      `leagueId` parameter so it stays out of deployment history. **Touches T01
      (`<UserSecretsId>`), T17, T20, T21, T23.**
- [ ] **A3** Keep the slot eligibility map general rather than pruning it to
      2026's slots; derive slot list, count and axis order per season. **Touches
      T04, T46, T49.**
- [x] **A4** No season chain to walk yet — D8 unchanged, first exercised at the
      August 2027 rollover.

---

## Review

_Add a section here per work session: what changed, what was verified, what is
still open._

### 2026-09-11 — M1 closed

`FantasyLeague.Console` runs end to end against the live league and both
invariants hold:

    season 2026   weeks 1-1   final through wk 0

    TEAM                         POINTS     SD    IDEAL   EFF     GAP
    -----------------------------------------------------------------
    kuzma4prez                     48.2      -     48.2  100%       -
    Aaron Donald Relief Fund       35.2      -     42.0   84%   -13.0
    START TOMMY CUTLETS            16.9      -     44.8   38%   -31.3
    sullynator9                    11.3      -     11.3  100%   -36.9
    LilDab                          9.9      -     14.0   71%   -38.3
    POON SQUAD                      0.0      -      0.0     -   -48.2

    invariants   starters==points  OK   (6 team-weeks)
                 ideal>=points     OK   (6 team-weeks)
    unresolved   none

Three blockers cleared on the way. `int.TryParse` on the league id rejected every
valid input — a Sleeper id is 1.4e18 against an `int.MaxValue` of 2.1e9, and it
is a string that must never be parsed at all. The csproj targeted net10.0,
shadowed `Directory.Build.props`, had no project reference and was not in the
solution, which is why the broken file never failed a build. `AddUserSecrets`
would have thrown without a `UserSecretsId`; the harness prompts instead.

`SD` is `-` for every team because D1 needs four finalized weeks and there are
zero. It populates after B3, 2026-10-06.

**This is the milestone the design calls out: thirty hours in, the problem is
solved.** Everything after this buys a live website rather than a better answer.

### 2026-09-11 — T13 and T14

`SleeperReaderCache` (T13) and `PlayerMapRefresher` (T14). Suite at 140 passing.

T13's tests go through the real pipeline, so a cache miss traverses cache →
client → resilience → rate limiter → socket. The one that matters is
`Twenty_simultaneous_callers_produce_one_fetch`: twenty concurrent misses, one
request. That is the entire reason for `HybridCache` over `IMemoryCache`.

T14 started as `async void` with a static mutable map. Demonstrated that an
`async void` exception is uncatchable by the caller and **terminates the
process** — the timer invocation would have reported success while the worker
died. Now `async Task` on an injected singleton with a volatile backing field.

Removed `SleeperReaderCache.GetPlayersAsync`: it cached the 14 MB dump for 24
hours on the request path, giving two independent copies with different
lifetimes and no rule about which won. The refresher is the single source of
player data, which is what T14's "never on a request path" requires.

Tightened `SleeperClient.GetPlayersAsync` from
`Dictionary<string, SleeperPlayerDto?>` to non-nullable values. The `players!`
at the call site was not redundant as I had claimed — it was suppressing CS8620
from that inaccurate signature, and removing the suppression surfaced it.

**Still open:** F4 (the TTL budget), and the `[TimerTrigger]` plus `Player`
table persistence, both of which belong with M3.

### 2026-09-10 — T11 and T12 wired and tested

The whole outbound pipeline now lives in `AddSleeperClient`, so the host cannot
register the client without its rate limit. 12 new tests, suite at 121 passing.

The defect worth recording: the handler had been registered with
`AddTransient` but never attached with `AddHttpMessageHandler`, so nothing was
in the pipeline and every Sleeper call bypassed the bucket. DI knowing how to
build a thing is not the same as the chain using it, and the returned
`IHttpClientBuilder` being discarded is what hid it.
`The_rate_limiter_is_actually_in_the_pipeline` now fails if that recurs.

Order was chosen rather than inherited. Handlers run first-added-outermost —
proven, not assumed — so resilience goes first and the rate limiter second, and
every retry passes back down through the bucket and spends its own token. The
reverse order lets one token cover four network requests, which is exactly the
retry-loop case the limit exists to contain.

Local rate-limit refusals now throw `SleeperRateLimitExceededException`. It
derives from `HttpRequestException` so existing handling is unsurprised, but is a
distinct type so `ShouldRetry` can exclude it — the manual's `ShouldHandle` would
otherwise have retried a purely local refusal that no amount of backoff can help.

**A note on the circuit breaker.** Its default `MinimumThroughput` is 100 calls
within a 30 second sampling window. This design makes about one call in 30
seconds, so the breaker can never open. It is inert here — worth knowing so
nobody reasons about behaviour it will never exhibit.

### 2026-09-10 — T10 reviewed and covered

`SleeperClientTests.cs`: 24 tests across URL construction, response handling and
registration, all against a recording `HttpMessageHandler` — no network, runs in
milliseconds.

The URL theory pins all six endpoints to their absolute addresses. That is the
base-address trap made permanent: the base ends with a slash and every relative
path begins without one, and swapping either makes `Uri` resolution drop the
`/v1` segment and fail as a 404 that names nothing.

Two of my own tests were wrong and got corrected rather than the code:

- I asserted a cancelled call never reaches the handler. It does — `HttpClient`
  enters the handler pipeline before observing the token. The throw is the real
  assertion; the extra one was testing my stub.
- I asserted a timeout under 60 seconds. There is none — see **F3**.

Also confirmed against the live API and worth remembering at T15: a week that
has not been played returns a **complete payload of zeroes**, not absent fields.
Week 18 gives `points: 0.0`, eleven zero `starters_points` and nineteen zero
`players_points`. It is byte-identical in shape to a week where everyone was
blanked, and three of six rosters really did score 0.00 in week 1. So `isFinal`
can only come from `/state/nfl`. An out-of-range week (99) returns `[]`.

That contradicts the manual's stated reason for making numerics nullable
("entries with no points at all"). The advice stands; the reason does not.

### 2026-09-10 — T09 framed

`FantasyLeague.Sleeper` now has six DTOs under `Dtos/`, a shared
`SleeperJson.Options`, a public `PlayerRef`/`RosterWeek`, and `SleeperMapper` as
the one place Sleeper's shapes become the domain's. DTOs stay `internal` per the
manual; `InternalsVisibleTo` lets the test project bind them.

20 tests, passing first run, against payloads Sleeper actually returned rather
than the shapes the design assumed — including that `16.92` survives the parse as
a decimal, that `season` binds from the string `"2026"`, and that three `"0"`
empty starter slots appear in a single real week.

Three findings recorded as amendments: **A6** the design is wrong about unset
team names, **A7** `custom_points` is a commissioner override that must win, and
**A8** unresolved player ids are reported rather than dropped.

Left deliberately off `RosterDto`: its own `players` and `starters`. They are the
roster as it stands today, and D2 requires the week's roster from the matchup
payload instead — omitting them means the wrong one cannot be reached for.

### 2026-09-10 — T07 verified, T08 done

`SpreadCalculatorTests.cs`: 20 tests over four classes, all passing first run.
T07's implementation is correct as written.

Every row of the manual's T08 table is covered. The population-versus-sample
distinction is pinned with `[8, 12, 8, 12]`, chosen because population sigma is
exactly 2.0 while Bessel's correction would give 2.3094 — far enough apart that
no rounding tolerance can blur them.

The last row, the one the manual says will actually break in production, is
tested through the real pipeline: five `TeamWeek` records, one with
`IsFinal: false`, filtered before the call. There is a companion test asserting
that *zeroing* the unfinished week rather than dropping it gives a different
answer, since that is the tempting wrong fix.

Two edges outside the manual's table are recorded as F1 and F2 above.

### 2026-09-10 — T06 unblocked work

Added `scripts/capture_fixtures.py`: reads the league ID from
`SLEEPER_LEAGUE_ID`, captures every completed week, and rebuilds
`player-positions.json` over the **union** of all captured weeks rather than
per-run — a map built from week 1 alone goes stale as soon as waivers move
anyone. It refuses to run without the env var, never echoes a URL containing the
league ID, and re-reads its own output to assert no league ID was written.
Verified it reproduces the committed fixtures byte-for-byte.

Hardened the fixture tests. They previously filtered rosters with
`positions.ContainsKey(id)`, so a player missing from the position map was
silently dropped — quietly lowering ideal with every test still green, the same
silent-undercount failure mode as A3, but in the harness. All three call sites
now go through a `Roster(...)` helper that asserts every player resolves and
names the fix in the failure message.

47 passing, 1 skipped, format gate clean.

Everything else in T06 is waiting on games being played — recorded as B1–B5
under **Blocked on the season**.

### 2026-09-10 — T05 tests, and a bug in the lineup reconstruction

`IdealLineupSolverTests.cs`: **48 tests, 43 passing, 1 skipped, 4 failing.**
The four failures are a real defect, not flaky tests.

**`Solve` returns the right number and the wrong names.** Points matched an
exhaustive search on all 2,000 randomised rosters and on the real captured week.
The `Lineup` is wrong in roughly a quarter of cases — it repeats players and its
points do not sum to the `Points` returned. Minimal reproduction:

    slots   [WR, FLEX]
    players wr_low 4, wr_high 17
    -> Points = 21 (correct), Lineup = [wr_high, wr_high] (wrong)

Cause: `dp[m2]` is computed from the *previous* row's `dp[mask]`, but the
reconstruction reads `from[mask]` out of the *final* row. When a later player
improves `dp[mask]`, `from[mask]` is overwritten and the parent chain points at a
different history than the one the value was built from. Classic stale parent
pointer over a rolling array.

**Fixed.** The parent-pointer array is gone; each mask now carries the chosen
player *set* as a `long` bitmask, written in the same row the value came from, so
reconstruction is a bit scan rather than a backward walk. Guarded at 63 players.
Suite is 47 passing, 1 skipped (A3's guard), and an independent 50,000-case fuzz
against brute force reports zero discrepancies on points, duplicates, lineup
length, point sums, and whether the returned lineup can actually be seated.
§05 wants the solved lineup stored and displayable, so this mattered.

Also corrected a test of my own that was wrong: rosters 4 and 5 genuinely cannot
fill all eleven slots — roster 4 rosters no kicker, roster 5 no defence. Their
ideal lineups are 10 and 9. `filled == 11 - starters_zeros` for every roster in
the captured week, so the empty slots managers left were real shortages.

### 2026-09-10 — T04 test coverage

Wired `Core.Tests` to `Core`, added FluentAssertions **7.2.0** (the last
Apache-2.0 release — 8.x moved to the Xceed Community licence, free only for
non-commercial use), and replaced the `UnitTest1.cs` placeholder with
`SlotTests.cs`: **28 passing, 1 skipped.**

The fixtures from T02 are copied into the test output, so `Slot.Parse` is
exercised against this league's real 19-entry `roster_positions` rather than an
invented one — 11 startable slots in documented order, 8 distinct for the radar
axes.

The skipped test is `Parse_rejects_an_unrecognised_slot_code`. It is written and
correct; it stays skipped until A3's guard exists. Deleting the `Skip` argument
is the whole enabling step.

Build clean, `dotnet format --verify-no-changes` passes.

### 2026-09-10 — T02 recon captured, T01 tidied

**T02 (partial).** Captured week 1 to `tests/fixtures/` — the matchups payload,
the `roster_positions` array, and a slim `playerId → fantasyPositions` map for
the 114 rostered players so the fixture set is self-contained. Verified the
`starters[i]` ↔ slot alignment: 63/63, zero violations (**A5**). Confirmed the
`"0"` empty-slot quirk, DEF-as-abbreviation, and `sum(starters_points) ==
points`. Still open: the capture is mid-week-1, so re-capture after it finalizes,
add weeks 2–3, and answer Q4.

**T01 tidy.** Added `.editorconfig`; removed the leftover `Class1.cs` from
Sleeper and Data; deleted the `TargetFramework`, `ImplicitUsings` and `Nullable`
lines from all five csproj files so `Directory.Build.props` is finally
authoritative — all five now resolve to net9.0 from the root file alone. Build
is clean, 1 test passes, and `dotnet format --verify-no-changes` (the T22 CI
gate) passes.

Still outstanding from the T01–T04 review: `Core.Tests` has no `ProjectReference`
to `Core`, and T04's unknown-slot guard (**A3**) is not implemented.

### 2026-09-10 — moved into the FantasyFootballWebsite repo

Docs, task list and `CLAUDE.md` moved from the scratch `idealPoints` folder into
`FantasyFootballWebsite`, which already carried T01. Landed on
`feat/M0-initial-setup-and-proofs`. The secrets rules from **A2** were appended
to the existing Visual Studio `.gitignore` rather than replacing it.

Nothing under `src/`, `tests/`, `Directory.Build.props`, `FantasyLeague.slnx` or
`LICENSE` was touched.

**Open issue found on arrival:** the four `dotnet new classlib`/`xunit` projects
wrote `<TargetFramework>net10.0</TargetFramework>` into their own csproj files,
shadowing the `net9.0` in `Directory.Build.props` — the same trap as the
Functions template, in the opposite direction. The solution builds today only
because no project references another yet. See `tasks/lessons.md`.

### 2026-09-10 — questions answered, secrets policy set

Q1, Q2, Q3, Q7 and Q8 closed against the live league; Q4, Q5 and Q6 still open.
Four local amendments recorded in [`docs/amendments.md`](../docs/amendments.md):
the dummy-team exclusion (A1), the secrets policy (A2), keeping the eligibility
map general (A3), and the absent season chain (A4).

The league is **six rosters, five of them scoring** — not the twelve §08, §10 and
D11 assume. Those decisions still hold, but their reasoning is weaker than
written; noted in the amendments rather than silently left to surprise someone.

### 2026-09-10 — synced to design v6

Pulled the updated design, field manual and build plan from the planning
conversation. Changes since the first import:

- **Design v3 → v6.** New §11 *Positional radar* with decisions **D10** (plot
  the ratio to league average, never raw points) and **D11** (one team at a
  time, diverging bars as the honest alternative). §10 gains a *Motion, if any*
  subsection. §09's payload gains `slotOrder`, `leagueSlotAverages` and per-team
  `slotAverages`. New **Q8** on axis count. §08 notes the radar needs no new
  storage.
- **Build plan 45 → 50 tasks + 5 optional**, 98 h → 108 h, 6 → 8 milestones.
- **Task IDs renumbered.** Sleeper recon was pulled from the end of M1 (old T16,
  "reality check against live Sleeper") to the front of M0 as the new **T02**,
  so everything after it is written against the real league instead of an
  assumption. Old T02–T15 each shifted up by one; T17–T45 are unchanged.
  Old T16's invariant assertions now live in the new T16, against working code.
- **T01 remains the only completed task.**
