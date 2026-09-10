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
- [ ] **T06** Solver fixtures and tests — 3 h `±`
      Real weeks from T02, plus a constructed case where greedy loses and a bye-week hole.
- [ ] **T07** Consistency maths — 1 h
      Population sigma, CV, mean/min/max. Returns null below four final weeks.
- [ ] **T08** Sigma edge-case tests — 1.5 h
      Zero, one and three weeks; a genuine 0.0 score; a non-final week excluded.

## M1 · Sleeper client — 16 h

Ends with a console app printing the real standings and asserting both
invariants across a whole season.

- [ ] **T09** DTOs and JSON config — 2 h
      snake_case, nullable numerics, `players_points` as a string-keyed map.
- [ ] **T10** Typed HttpClient — 2 h
      state, league, users, rosters, matchups, players.
- [ ] **T11** Token-bucket handler — 2 h
      `DelegatingHandler` at 50/min, shared by HTTP and timer paths.
- [ ] **T12** Polly policies — 1 h
      429 and transient retry, exponential backoff with jitter, bounded queue.
- [ ] **T13** Single-flight cache — 2 h
      Per-key TTL, concurrent callers await one in-flight fetch.
- [ ] **T14** Player dump job — 1.5 h
      5 MB fetch, projected to id → name/team/fantasy_positions.
- [ ] **T15** Season assembly — 2.5 h
      Matchups to per-team weekly actual and ideal.
- [ ] **T16** Console harness and live verification — 3 h `±`
      Prints the real table, then asserts `starters==points` and `ideal>=actual` across every week.

## M2 · API and cloud foundation — 18.5 h

Deployed and reachable, still no database.

- [ ] **T17** Functions host — 2 h
      Isolated worker, DI, options binding, `local.settings.json`.
- [ ] **T18** Season endpoints — 2 h
      `GET /api/season` and `/api/seasons`, served from memory.
- [ ] **T19** Caching semantics — 1.5 h
      ETag, 30 s live vs immutable historical, 404 listing available seasons.
- [ ] **T20** Bicep template — 4 h `±`
      SWA, Function App, storage, App Insights, resource group scope.
- [ ] **T21** GitHub to Entra OIDC — 3 h `±`
      App registration, federated credential, RBAC. No secrets in the repo.
- [ ] **T22** `ci.yml` — 1.5 h
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
