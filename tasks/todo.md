# Ideal Points — Tasks

Reasons live in [`docs/amendments.md`](../docs/amendments.md); finished work's detail is in
git history.
`±` marks an estimate that could run double.

**Status (2026-10-13).** M0–M7 built and deployed. T59–T61 done bar two checks below.

## Open

- [ ] **T66–T70 · Zoom, head-to-head, playoffs, a champion** (A36, planned 2026-11-15, revised
      after design review). Reverses §01's "out of scope: head-to-head, playoffs" for leagues
      configured that way. Order: T66 (merged) → T64 (built, needed by 2026-12-15) → T67 →
      T68 → T69 → T70.
  - [x] **T66** Chart zoom and the mobile flash — 5 h `±`. Front end only. Merged
       2026-11-15 (A36).
    1. The flash, first. Two suspects:
       - every option change gives every line a new key (`displayKey`), so the old paths fade
         while the new ones draw from `pathLength: 0`;
       - on a phone, a scroll that starts on the hit rect (`touch-action: pan-y`) fires
         `pointermove`, which sets `hovered` (muting and reordering the lines), then
         `pointercancel` clears it.
       Also, Motion leaves an inline `opacity: 1` that beats the `[data-muted]` 0.18 rule and
       fights the CSS opacity transition, so check whether muting works today. Reproduce with
       touch emulation, not just a narrow window.
       Confirmed: the keyed remount (old lines held on the new axes for ~1 s, new ones hidden),
       and muting never worked. Fix: key paths by `rosterId` only; set `d` directly with no
       tween (the point counts differ between modes); draw in on mount only; let CSS own the
       opacity; take hover only from `pointerType === 'mouse'`. Then confirm on a real phone
       (lesson: timing UI).
    2. A brush strip under the chart: mini lines for every visible team plus a window with two
       handles. Drag across the strip to choose weeks, drag the window to pan. Pointer events
       in React (D9). `touch-action: none` on the strip only.
    3. Accessibility: the handles are `role="slider"` labelled "First week" and "Last week", with
       `aria-valuemin/max/now`, `aria-valuetext="Week 6"`, arrows and Home/End, and they cannot
       cross. A visible **Reset zoom** button whenever zoomed; no double-tap, since it has no
       keyboard equivalent and clashes with iOS zoom.
    4. The range is `?weeks=6-12`: parsed, clamped to the weeks played, at least two weeks wide
       (`weekScale`'s floor). It lives in state while dragging and goes to the URL on pointerup
       (Safari throws after about 100 `replaceState` calls in 10 s).
    5. The main chart takes the range: x domain, y rescaled to the points inside it, lines
       clipped (`useId()` for the clip id), and the pointer snapping, ticks, line-end names
       and the aria label's `latestExtremes` all limited to the range. A range with no points
       (σ zoomed to weeks 1–3) shows the not-plottable message.
  - [ ] **T64** (below) is rewritten by the review, and built that way (A37). Keep reading
       **and storing** playoff weeks, marked as playoff weeks from `playoff_week_start`, so T69
       and past-season crowns have them. Leave them out of totals, σ, the chart, the radar and
       the record, in both modes.
  - [ ] **T67** `League:Mode` = `TotalPoints` (default) | `HeadToHead` — 1 h. Bound and validated
       in `LeagueOptions`, sent as `mode` in the payload. Nothing changes in `TotalPoints`.
       Past seasons render in the deployment's mode (Q10).
  - [ ] **T68** Head-to-head record — 6 h `±`.
    1. Store every roster, excluded ones included. Finalize writes `snapshot.Teams`, which
       `GroupTeams` has already filtered (A1), so a stored week cannot score a game against the
       placeholder today. A1 moves to the read side only; correct its wording to say so.
    2. Sleeper → store: `MatchupDto.MatchupId` → `RosterWeek` → `TeamWeek.MatchupId`. A bye is
       stored as `0`, so null means only "not filled yet". An additive nullable
       `WeekScore.MatchupId` column, migration first. Bump the blob `Schema` from 4 to 5 so an
       old blob is a cache miss.
    3. Backfill, from the finalize job and never on a request: for stored weeks with a null
       `MatchupId`, read that week's matchups (about 10 requests, once, through the token
       bucket) and update **only** `MatchupId` and any missing excluded rows. Ideal points are
       not recomputed against today's player map (D2). Then rewrite the whole season to the
       blob, never a subset. Runbook: run Finalize by hand once, then switch the mode.
    4. Core: `HeadToHead.Records(weeks)`, a pure function. It pairs on (week, matchup id) over
       final regular-season weeks only (R1) and returns W, L, T, points for and points against.
       A game against an excluded roster counts. When the league plays a median game
       (`league_average_match`), each final week adds a second result against that week's median
       score, as Sleeper counts it (Q12). The record is null while any final regular
       week lacks matchup data (a mode switched on mid-backfill shows nothing rather than an
       undercount). Cross-check it against the rosters call's `settings.wins/losses/ties`,
       already cached at 5 min, as a third invariant, reported like the other two.
    5. Payload: `record: { wins, losses, ties, pointsAgainst } | null` on each team, one
       nullable object rather than four nullable fields.
    6. Table in H2H: Team · W-L(-T) · Points · PA (hidden on narrow screens) · Ideal, ranked by
       `rankByRecord` in calculations.ts (win %, then points for, Sleeper's tiebreak). Chips
       keep the table's order. The Skeleton cannot know the mode, so its columns stay
       points-mode and swap on load (accepted).
  - [ ] **T69** Playoff bracket, H2H only — 6 h `±`.
    1. Sleeper: `/v1/league/{id}/winners_bracket`, fetched only in H2H once
       `week ≥ playoff_week_start`; cached 5 min live, a day past (≤ 0.2 req/min on §07).
       Plus `settings.playoff_week_start` and `playoff_round_type` (0: a week per round;
       1: two-week final; 2: two weeks per round).
    2. Payload `playoffs`: rounds → matches with `p` (placement: 1 final, 3, 5…) and each side's
       `from` (winner or loser of match m; none for a round-1 bye), the teams, points summed
       over the round's weeks and a winner shown only when all of them are final (R1). No
       seeds (Sleeper's bracket has none; Q11). The losers bracket is out of scope.
    3. A Playoffs panel, a round per column, connected by `from`; a stacked list on phones.
  - [ ] **T70** The champion — 2 h `±`. A server-side `champion` (roster id or null):
       `TotalPoints`: the points leader once every regular-season week is final (ties crown
       everyone tied). `HeadToHead`: the winner of the `p = 1` match once final. Past seasons
       too. A crown before the name with the text alternative "Champion", a gold tint on the
       row, and a few gold sparkles (`aria-hidden`) that twinkle; under reduced motion they
       are still and the crown stays. The bracket's final is crowned the same way.
       An excluded roster that wins **is crowned** (Q9): it has no table row, so the crown
       goes on the bracket's final and on a line above the table naming it. Capture `state/nfl` in January to
       check it does not reset after the NFL season and empty the current season and its crown.
  - [ ] **Caching** Past seasons drop from `max-age=31536000, immutable` to a day, so `mode`,
       `record`, `playoffs` and `champion` can reach a past season already cached in a browser.
  - [ ] Docs: A36 (why, including this review), §01, §03 and §07 corrected in place, A1's wording,
       Q4 updated, runbook mode switch and backfill. Tests: an invented H2H season (a bye, a tie,
       a game against the placeholder, a mode switch mid-backfill), an invented bracket with each
       `playoff_round_type`, placement games and a bye, champion in both modes and a past
       season, brush keyboard and URL clamping, axe.
  - **Answered 2026-11-15:** Q9 an excluded roster in the bracket is shown, and crowned if it
       wins. Q10 every season renders in the deployment's mode. Q11 no seeds. Q12 a median game
       counts in W-L, as in Sleeper.
- [ ] **T64** Count the regular season only (Q4, A33). Built 2026-11-15 (A37). The payload stops
      at the week before `settings.playoff_week_start` (else week 18); playoff weeks are still read
      and stored. After design review: a week that goes backwards cannot shrink the season,
      `season_type` is logged, the masthead says "Season completed", and the playoff settings are
      captured: this league sends `playoff_week_start: 0` (no playoffs), so it counts all 18
      weeks less any unplayed ones at the end (Sleeper does not say when it ends); a binding
      test pins the capture.
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
