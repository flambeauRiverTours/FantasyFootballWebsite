# Ideal Points — Tasks

Reasons live in [`docs/amendments.md`](../docs/amendments.md); finished work's detail is in
git history.
`±` marks an estimate that could run double.

**Status (2026-11-16).** M0–M7 built and deployed. T64, T66–T68, T71 and T73 merged, with
head-to-head still gated; T72a (lift the gate) next, then T72b, T69, T70. One T61 check left.

## Open

- [ ] **T74 · The standings rows never slide** — 1 h `±`. Its own `fix/` branch, any time.
      `App.tsx` loads `LazyMotion features={domAnimation}`, which has no layout feature, so
      `m.tr layout` in StandingsPanel and ConsistencyPanel is inert and §10's re-sort animation
      has never run. Confirm in the preview first, then the smallest fix: `domMax` (check the
      bundle against its budget), `domMax` lazily for the tables only, or drop `layout` and
      correct §10. Reduced motion is already honoured by `MotionConfig`.

- [ ] **T66–T72 · Zoom, head-to-head, matchups, divisions, playoffs, a champion** (A36,
      planned 2026-11-15, revised after design review). Reverses §01's "out of scope:
      head-to-head, playoffs" for leagues configured that way. T64, T66, T67 and T68 are merged
      (Done table). Next: T72a → T72b → T69 → T70. The real league has no divisions and no
      playoffs (captured settings), so T72a goes first: it is the part that league sees.
  - [ ] **T72a** Lift the head-to-head gate — 2 h `±`. Split from T72 (A44). Preconditions met
       2026-11-16 (A44): the backfill read 0 weeks on its last two runs, logged "Matchups
       complete" for every season with stored weeks and no "Could not backfill" error; no record
       mismatch logged in 30 days. Design reviewed: green with changes, folded in below.
    1. **The gate.** `LeagueOptions.Format` takes `[AllowedValues(TotalPoints, HeadToHead)]`, not
       `[EnumDataType]` (both refuse `"7"`; the list makes a later format opt-in). `Anything_but_total_points_is_refused_until_T72_is_built` becomes
       `Head_to_head_binds` (by name, any case) plus `An_undefined_format_is_refused` ("7").
       `"1"` and `"TotalPoints,HeadToHead"` parse to `HeadToHead`: accepted, as the binder
       accepts any enum this way; a test pins it so it is a decision, not an accident.
    2. **One ranking rule, in Core.** `Standings.RankByRecord(teams, record, name)` and
       `GamesBack`: win rate (a tie as half a win), then points for, then name compared with
       `InvariantCulture` as `localeCompare` does; games back from the best margin. Tested
       against the same cases as `calculations.test.ts`, plus "alpha"/"Bravo" in both.
    3. **The CLI in `HeadToHead`.** Ranked by record, with RECORD (no median split: the page has
       it), GB, PF and PA in place of POINTS, SPREAD and GAP, once every record is known
       (else the points table, as the page falls back); `TotalPoints` unchanged. `RecordCheck`
       counts compared and skipped rosters, so the footer says `OK` only when it compared some,
       `not run` otherwise, lists mismatches, and a mismatch fails the exit code. Run against the real league before
       the switch (Jack runs it; the league id stays in user-secrets).
    4. **`infra/`** (asked for in A38's last bullet): `@allowed(['TotalPoints', 'HeadToHead'])
       param leagueFormat string = 'TotalPoints'` and the `League__Format` app setting.
       **`.github/workflows/deploy.yml`** passes `leagueFormat="${LEAGUE_FORMAT:-TotalPoints}"` from
       a repository variable, as `OVERRIDE_LEAGUE_NAME` is. Merging changes nothing; setting the
       variable and redeploying is the switch, and clearing it switches back.
    5. **Runbook:** "Switching to head-to-head" under Ongoing maintenance: the CLI check first,
       the exact spelling, running the deploy by hand (a variable starts none), every past season
       checked for "still loading", and switching back; `LEAGUE_FORMAT` in Step 5.
    6. **Docs:** A44 (the split, the preconditions and their evidence, `AllowedValues`); design
       status line and §01 scope; A38's gate bullet points to A44.
    7. **Tests, red first:** the binding cases; `RankByRecord` (win rate, tie as half, points
       for, name, no records); the CLI table in both formats from a captured-shape snapshot.
       Then the page against the invented season with `HeadToHead` bound for real (the local
       Functions host, not the static file), at 360 px and desktop.
  - [ ] **T72b** Division standings, H2H only (Q14) — 4 h `±`. After T72a; the rest of the old
       T72, minus its gate step.
    1. Capture first: the script already keeps `divisions` and each roster's `division`; add
       the league's `metadata.division_N` names (names only; no ids). No league with divisions
       has been observed, so the fixture is invented to match.
    2. Sleeper: `LeagueSettingsDto.Divisions`, `RosterDto` settings' `Division`, the names
       from metadata, mapped to Core. Read per season like names and slots (A34), so a past
       season shows that year's divisions. Nothing stored.
    3. Payload: `divisions: [{ number, name }] | null` and `division` on each team. Null, and
       one flat table, for a league without divisions, and always in `TotalPoints`.
    4. The table grouped by division, each ranked by `rankByRecord`, the leader marked, a
       division with no name shown as "Division N". An excluded roster's division is not shown.
    5. Tests: two divisions, a league with none, an unnamed division, a past season.
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
       From `playoff_week_start` on it replaces T71's matchups panel, which until then shows the
       last regular week.
    4. Tests: an invented bracket for each `playoff_round_type`, placement games and a bye.
  - [ ] **T70** The champion — 2 h `±`. A server-side `champion` (roster id or null):
       `TotalPoints`: the points leader once every regular-season week is final (ties crown
       everyone tied). `HeadToHead`: the winner of the `p = 1` match once final; **with no
       playoffs** (`playoff_week_start` 0, as the real league has), the record leader by the
       ranking rule once every regular-season week is final (A43). Past seasons
       too. A crown before the name with the text alternative "Champion", a gold tint on the
       row, and a few gold sparkles (`aria-hidden`) that twinkle; under reduced motion they
       are still and the crown stays. The bracket's final is crowned the same way.
       An excluded roster that wins **is crowned** (Q9): it has no table row, so the crown
       goes on the bracket's final and on a line above the table naming it. Capture `state/nfl` in January to
       check it does not reset after the NFL season and empty the current season and its crown.
       Tests: the champion in both formats and in a past season.
- [ ] **T65** `League:SeasonOverrides` (D8, deferred by A34): a year → league ID map merged over
      the chain, for a league recreated from scratch. Not needed while the chain is intact.
- [ ] **T61** Domain 4b, item 16: try dropping the queue and table storage roles now that identity
      storage is live. The runbook's role list changes with it.

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
| Past seasons | T29, T56 | Chain walk, season picker (A34) |
| Portfolio | T59–T60 | Consistency panel (A33); standards and seven domain reviews (A19–A33) |
| Formats | T64, T66–T68, T71 | Regular season only (A37), chart zoom (A36), `League:Format` (A38), the head-to-head record (A39), this week's matchups (A41–A43), gated until T72a |
| Cleanup | T73 | Repo-wide simplification and standards pass; the deploy's migrate sign-in (A40) |

Settled questions: Q1–Q15, in design §14. Fixtures B1–B3, B5 captured.
