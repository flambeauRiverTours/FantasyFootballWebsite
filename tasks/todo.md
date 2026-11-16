# Ideal Points — Tasks

Reasons live in [`docs/amendments.md`](../docs/amendments.md); finished work's detail is in
git history.
`±` marks an estimate that could run double.

**Status (2026-11-16).** M0–M7 built and deployed. T64, T66, T67 and T68a merged; T68b built on
its branch; T73 (cleanup) next; T69–T72 planned. One T61 check left.

## Open

- [ ] **T73 · Repo-wide simplification and standards cleanup** — 3 h `±`. **Next**, on its own
      `chore/` branch once T68b merges, before T71. From the repo-wide passes of 2026-11-16;
      verify each against the code first. No behaviour changes.
  1. **Code.** `PlayerMapOptions.MaxAge` and the three `BlobStorageOptions` names become
     constants (nothing configures them). One helper builds both `SeasonSnapshot`s in
     `SeasonService`. `ToSeasonsPayload` stops re-adding and re-sorting what
     `GetAvailableSeasonsAsync` already returns.
  2. **Tests.**
     - One shared fake Sleeper handler (`CountingSocket`, `RecordingHandler`, `CountingHandler`),
       and one DbContext factory fake.
     - The axe `violations` helper shared from one file.
     - Duplicate tests dropped: the panel-level axe tests the page test covers, the second
       Enter/Space check, the template-literal test.
     - The URL reset moved into `tests/setup.ts`.
  3. **Web.**
     - `index.css`: merge the two `th[aria-sort]` rules; drop `td { color }` and the orphan
       `font-variant-numeric: normal` (or restore `tabular-nums`, which §10 promises); fold
       `.reset-zoom` into `.chip`.
     - Delete `web/public/icons.svg`.
     - Inline `computeRowHeight` and `slotDeviations`.
     - Stop exporting types used only in their own file.
     - Share the row spring between StandingsPanel and ConsistencyPanel.
  4. **Standards.**
     - Reword or delete the C# `<param>` tags that only restate a name (about 15; keep the tags
       on positional records, reworded).
     - `capture_fixtures.py` writes CRLF.
     - Fixtures README: weeks 1–9, `divisions` only when the league has them, and no
       `previous-season.json` yet.
     - T72 step 1: only the `metadata.division_N` names are left to capture.
  5. **Docs.** Ticked T64/T66/T67 move to the Done table; design §14 and §15 trimmed to what
     is still open.
  - Not taken: dropping the server's points order (§09 contract, the CLI); removing the
    always-true `IsFinal` column (a migration for nothing); replacing `FinalizeSchedule.NextRunAfter`
    (deliberate: the timer's own `Next` reports the run in progress).
  - Jack's call: `deploy.yml` pins `dotnet-ef` 10.0.12 against EF 10.0.13.

- [ ] **T74 · The standings rows never slide** — 1 h `±`. Its own `fix/` branch, any time.
      `App.tsx` loads `LazyMotion features={domAnimation}`, which has no layout feature, so
      `m.tr layout` in StandingsPanel and ConsistencyPanel is inert and §10's re-sort animation
      has never run. Confirm in the preview first, then the smallest fix: `domMax` (check the
      bundle against its budget), `domMax` lazily for the tables only, or drop `layout` and
      correct §10. Reduced motion is already honoured by `MotionConfig`.

- [ ] **T66–T72 · Zoom, head-to-head, matchups, divisions, playoffs, a champion** (A36,
      planned 2026-11-15, revised after design review). Reverses §01's "out of scope:
      head-to-head, playoffs" for leagues configured that way. Order: T66 → T64 (both merged)
      → T67 → T68 → T73 (cleanup) → T71 → T72 → T69 → T70.
  - [x] **T66** Chart zoom and the mobile flash. Merged 2026-11-15 (A36); the flash fix
       confirmed on a real phone.
  - [x] **T64** Regular season only. Merged 2026-11-15 (A37).
  - [x] **T67** `League:Format` = `TotalPoints` (default) | `HeadToHead`, sent as `format`; past
       seasons cached a day; head-to-head refused at start until T72. Merged 2026-11-15 (A38).
  - [ ] **T68** Head-to-head record (A39). **T68a** merged 2026-11-15 and verified live: our
       records match Sleeper's. **T68b** built 2026-11-16 on its branch: the record on the page,
       gated off until T72. Left for T72: lifting the gate, and the CLI, which still ranks by points.
  - [ ] **T71** This week's matchups, H2H only — 3 h `±`. Current week only (Q13): no week
       picker and no past results beyond the table's W-L.
    1. Payload `matchups`: the current week's games, `[{ matchupId, teams: [{ rosterId, name,
       points, isFinal }] }]`, paired on T68's matchup id. The current week is the live one, or
       the last counted week once the season is complete. A game against an excluded roster is
       shown with its name (it has no table row). A bye has no game. Null in `TotalPoints`.
    2. A Matchups panel under the table: one card per game, the two scores, the leader marked
       once the week is final and "leading" before then, polling with the rest of the page.
       In the playoffs it gives way to the bracket (T69), which shows the same games.
    3. Tests: an invented week with a bye, a game against the placeholder, a live and a final
       week; axe.
  - [ ] **T72** Division standings, H2H only (Q14) — 4 h `±`.
    1. Capture first, as T64 did: add `divisions` to `SEASON_SETTINGS`, each roster's
       `settings.division` and the league's `metadata.division_N` names (names only; no ids).
       No league with divisions has been observed, so the fixture is invented to match.
    2. Sleeper: `LeagueSettingsDto.Divisions`, `RosterDto` settings' `Division`, the names
       from metadata, mapped to Core. Read per season like names and slots (A34), so a past
       season shows that year's divisions. Nothing stored.
    3. Payload: `divisions: [{ number, name }] | null` and `division` on each team. Null, and
       one flat table, for a league without divisions, and always in `TotalPoints`.
    4. The table grouped by division, each ranked by `rankByRecord`, the leader marked, a
       division with no name shown as "Division N". An excluded roster's division is not shown.
    5. Tests: two divisions, a league with none, an unnamed division, a past season.
    6. **Lift the gate (A38)**, only once the logs say "matchups complete" for every season and
       the record check (A39) has run clean: `[AllowedValues(TotalPoints)]` on `LeagueOptions.Format` becomes
       `[EnumDataType(typeof(LeagueFormat))]`, and `Anything_but_total_points_is_refused_until_T72_is_built`
       becomes a test that `HeadToHead` binds. Then plumb `League__Format` into `infra/` (A38, last bullet)
       and add the format switch to the runbook.
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
    4. Tests: an invented bracket for each `playoff_round_type`, placement games and a bye.
  - [ ] **T70** The champion — 2 h `±`. A server-side `champion` (roster id or null):
       `TotalPoints`: the points leader once every regular-season week is final (ties crown
       everyone tied). `HeadToHead`: the winner of the `p = 1` match once final. Past seasons
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

Settled questions: Q1–Q15, in design §14. Fixtures B1–B3, B5 captured.
