# Ideal Points — Tasks

Reasons live in [`docs/amendments.md`](../docs/amendments.md); finished work's detail is in
git history.
`±` marks an estimate that could run double.

**Status (2026-10-13).** M0–M7 built and deployed. T59–T61 done bar two checks below.

## Open

- [ ] **T64** Count the regular season only (Q4, A33) — 1.5 h `±`. Stop at the week before
      `settings.playoff_week_start` when set, else the NFL's last regular week; ignore weeks while
      `season_type` is `post`. Needed before week 15. Test on the captured league plus an invented
      playoff week.
- [x] **T29 + T56** Past seasons and the season picker. Done 2026-10-13 (A34). Decided: the
      chain is walked on demand and cached a day, with no table; a past season's names and slots
      come from Sleeper for that season's league ID; the August rollover stays a hand edit of
      `League__CurrentLeagueId`; the picker is hidden until there are two seasons.
  1. [x] Sleeper: restore `LeagueDto.PreviousLeagueId` and `Season`; a cheap
         `ISleeperReader` link lookup (league object only, reusing its 24 h cache).
  2. [x] Seasons: `SeasonChain` maps year → league ID by walking `previous_league_id` from the
         configured league; cycle and depth guards; cached 24 h.
  3. [x] `SeasonService.BuildAsync(season?)`: the current season as now; a past one from the
         store's weeks (all final) plus that league's names and slots. No stored weeks → absent.
         `SeasonSnapshot.IsCurrent` replaces the hard-coded `true`.
  4. [x] Endpoints: `/api/season?season=Y` serves a known past year with
         `max-age=31536000, immutable`, else 404 with the list; `/api/seasons` lists the chain.
  5. [x] Page: restore `fetchSeasons`; a `<select>` in the masthead when two or more seasons
         exist, bound to `?season=`; a past season shows its final week instead of "updated".
  6. [x] Tests: an invented two-season chain (2026 is the first real season); a past season
         never calls Sleeper for matchups; cache headers; picker hidden at one season.
  7. [x] Docs: A34; D8 **Corrected** (no `League` table); runbook August checklist, which
         wrongly edited the Azure setting the deploy overwrites. T64 applies to past seasons too.
     Found on the way: below 40rem the masthead status line, picker included, was hidden; now
     only its text is.
- [ ] **T65** `League:SeasonOverrides` (D8, deferred by A34): a year → league ID map merged over
      the chain, for a league recreated from scratch. Not needed while the chain is intact.
- [ ] **T30** Historical backfill — largely redundant: the finalize job upserts every finalized
      week, so the current season backfills itself. Needed only for a season never deployed; the
      CLI is its home, not a function.
- [ ] **T62** Rewrite the design for an employer: problem, decisions, trade-offs, measurements;
      history one click away. Includes CLAUDE.md, which still lists A1–A12.
- [ ] **T63** A GitHub Pages site for the project, including the runbook.
      Stubbed: `site/index.md` (Jekyll, Mermaid) and `pages.yml`. Left: screenshots, code links,
      making the repo public, and enabling Pages (Settings > Pages > GitHub Actions).
- [ ] **T61 · one check left**
  - [x] Domain 3, item 10: the Tuesday 12:00 UTC finalize runs after Sleeper's week rolls over.
        Confirmed 2026-10-27: all seven runs (Sep 15 to Oct 27) stored the week just played.
  - [ ] Domain 4b, item 16: try dropping the queue and table storage roles now that identity
        storage is live.

## Watch

- [ ] **F5** The stored player map is 840 KB; gzip makes it 160 KB. Act only if cold starts show.
- [ ] **F6** The Trivy exception `AZU-0012` expires 2027-03-01, then CI goes red unprompted.
      Recheck its reason first: storage now accepts no keys at all (A30).
- [ ] **B4** A missing `players_points` entry has never been observed (byes arrive as `0`). Keep
      capturing weeks; replace the invented test when Sleeper produces one.

## Done

| Milestone | Tasks | Notes |
|---|---|---|
| M0 Core | T01–T08 | Exact solver (D4), σ (D1); F1, F2 fixed |
| M1 Sleeper client | T09–T16 | Rate limit, resilience, caches; F3, F4 closed (A27) |
| M2 API and cloud | T17–T24 | Functions, Bicep, OIDC, CI and deploy |
| M3 Persistence | T25–T28, T31, T58 | SQL, finalize timer, migrations in the pipeline, blob mirror (A16, A20) |
| M4 The page | T32–T40 | Table, chart, controls, polling, accessibility |
| M5 Modes and ops | T41–T45, T57 | Display modes, σ variants, states, cost guardrails, runbook, custom domain |
| M6 Radar | T46–T50 | Slot attribution, league reference (D10), radar and bars |
| M7 Motion | T51–T55 | Optional; built |
| Portfolio | T59–T61 | Consistency panel (A33); standards and seven domain reviews (A19–A33) |

Settled questions: Q1–Q8 (amendments, table at the top). Fixtures B1–B3, B5 captured.
