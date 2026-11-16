# Ideal Points — Tasks

Reasons live in [`docs/amendments.md`](../docs/amendments.md); finished work's detail is in
git history.
`±` marks an estimate that could run double.

**Status (2026-11-16).** M0–M7 built and deployed. T64 and T66–T68 merged; T73 (cleanup) on its
branch; T69–T72 planned. One T61 check left.

## Open

- [ ] **T73 · Repo-wide simplification and standards cleanup.** Built 2026-11-16 on its
      `chore/` branch, no behaviour change beyond `td` getting the tabular figures §10 promised:
      unconfigured options became constants, one snapshot builder, one fake Sleeper handler and
      one DbContext fake, the URL reset in `tests/setup.ts`, duplicate tests and CSS gone, local
      types unexported, C# doc tags that restated names reworded, fixtures written as CRLF.
      Not taken, after checking: the server's points order (§09, the CLI); the always-true
      `IsFinal` column (a migration for nothing); `FinalizeSchedule.NextRunAfter` (the timer's
      own `Next` reports the run in progress); inlining `slotDeviations` (calculations live in
      `calculations.ts`); folding `.reset-zoom` into `.chip` (it would restyle the button);
      trimming design §14–§15 (the Q numbers are cited).
      Also: the deploy's migrate step signs in with developer credentials only
      (`AZURE_TOKEN_CREDENTIALS: dev`); a runner's metadata service answering 401 had stopped
      `Active Directory Default` at managed identity and failed the T67 and T68b deploys.
      `dotnet-ef` is a local tool in `.config/dotnet-tools.json` (10.0.13, it had drifted to
      10.0.12 in the workflow), restored by the deploy and bumped by Dependabot's `ef-core` group.

- [ ] **T74 · The standings rows never slide** — 1 h `±`. Its own `fix/` branch, any time.
      `App.tsx` loads `LazyMotion features={domAnimation}`, which has no layout feature, so
      `m.tr layout` in StandingsPanel and ConsistencyPanel is inert and §10's re-sort animation
      has never run. Confirm in the preview first, then the smallest fix: `domMax` (check the
      bundle against its budget), `domMax` lazily for the tables only, or drop `layout` and
      correct §10. Reduced motion is already honoured by `MotionConfig`.

- [ ] **T66–T72 · Zoom, head-to-head, matchups, divisions, playoffs, a champion** (A36,
      planned 2026-11-15, revised after design review). Reverses §01's "out of scope:
      head-to-head, playoffs" for leagues configured that way. T64, T66, T67 and T68 are merged
      (Done table). Next: T73 (cleanup) → T71 → T72 → T69 → T70.
  - [ ] **T71** This week's matchups, H2H only — 3 h `±`. Current week only (Q13): no week
       picker and no past results beyond the table's W-L. Designed and reviewed 2026-11-16.
       Gated like T68b: nothing shows until T72 lifts `League:Format`, and the `TotalPoints` body
       and ETag stay byte for byte as they are.
    1. **Which week: `SeasonSnapshot.MatchupWeek => Math.Min(CurrentWeek, LastCountedWeek)`.**
       - Regular season: `LastCountedWeek` is the cut-off until the season is complete, so the
         live week is chosen.
       - Complete, or in the playoffs before T69: the last counted week, final.
       - Past season: `CurrentWeek` is the last stored week, possibly a playoff week, so the
         trimmed last counted week is chosen.
       - The NFL state going back (`CurrentWeek` ≥ 19): the last counted week.
       - Preseason (`CurrentWeek` 0) gives no week, so no matchups.
    2. **One pairing rule, in Core: `HeadToHead.Games(weeks)`** returns each game as its two
       `TeamWeek`s, ordered by week, then matchup id, sides in roster id order (stable between
       polls, so a change of lead never swaps the rows). Byes (0) are not games. **Null when a
       week has a null id or a game that does not pair two rosters.** `Records` calls it, so a
       game can never count in the record yet be missing from the panel, or the reverse.
       Refactored under the existing HeadToHead tests, run before and after.
    3. **Payload (Seasons).** `SeasonPayload` gains a last parameter
       `[property: JsonIgnore(Condition = WhenWritingNull)] MatchupsPayload? Matchups = null`:
       ```jsonc
       "matchups": {                  // HeadToHead only; absent otherwise
         "week": 11, "isFinal": false,
         "games": [{ "matchupId": 1, "teams": [
           { "rosterId": 3, "name": "…", "actual": 96.4 },
           { "rosterId": 5, "name": "…", "actual": 41.2 } ] }]
       }
       ```
       - The mapper takes `MatchupWeek`'s rows from `Teams` and `ExcludedTeams`, calls
         `HeadToHead.Games`, and adds names. The placeholder's side carries its name, since it has
         no table row (A1); its points are shown as scored and count in nothing new.
       - `actual`, as every score in the payload is named. `isFinal` is the week's:
         `week <= FinalThroughWeek`. Points are the live score while the week plays (R1 governs
         what counts, not what shows).
       - **Null, so absent, when there is no week, `Games` is null, or the week has no game.**
         `SeasonService` stores a missing id as 0, a bye (`MatchupId ?? 0`), so a week Sleeper has
         not scheduled reads as all byes; no game means no panel, never an empty one. This also
         covers a league with no playoffs whose cut-off (18) is past its last scheduled week. If
         Sleeper does schedule that week, it shows as a real week (accepted; the chart shows it too).
    4. **The median game is not a card.** The record counts it (A39) and the table shows the
       result. A median line on the panel is left for later.
    5. **Web.**
       - `types.ts`: `matchups?: MatchupsPayload`. A missing field means no panel.
       - **A Matchups panel** right under Standings: "Week 11 matchups" while live, "Week 14
         results" once final, so a playoff week's masthead ("Week 15") does not look wrong.
       - **One card per game** in a `<ul role="list">` (Safari drops list semantics on
         `list-style: none`), stacked on phones, two or three across when wide. Each card holds
         two lines, read as written: swatch (`aria-hidden`), name, points, status.
         - **Status as text** from `matchupStatus(game, isFinal)` in calculations.ts: final
           "Won" or "Tied" on the winner's line; live "Leading" or "Level"; nothing while both are
           0.0 before kick-off. The winner's line is also emphasised, never by colour alone.
         - The placeholder has no swatch (`teamColours` is keyed by `payload.teams`); an empty
           spacer of the same width keeps the names aligned.
         - Points in one decimal with tabular figures (the `td` rule does not reach cards). Long
           names wrap at 375 px; nothing scrolls sideways.
         - No `aria-live`: the 30 s poll must announce nothing.
       - Polls with the page; nothing new to fetch; no motion. Panels are a screen tall (`.panel`
         `min-height: 100svh`), so two or three cards fill one: accepted, said in §10.
       - The Skeleton is unchanged (it cannot know the format).
    6. **T69 interplay.** The bracket replaces this panel from `playoff_week_start` on; until then
       a playoff week shows the last regular week, final. Recorded in T69.
    7. **Tests, red first.**
       - Core: `HeadToHead.Games`: order, sides, a bye left out, a null id and a game of three
         (null); `Records` unchanged on the captured season.
       - Seasons: `MatchupWeek` mid-season, after the cut-off in a current-season playoff week
         (cut-off, final), a past season, the state going back, preseason (none).
       - Mapper: a game against the placeholder (name carried); a live week (`isFinal` false,
         live points); a final week; every id 0 (absent); week 0 (absent); a past season whose
         week has a null id (absent, the table still there). **`TotalPoints`: a new test whose
         chosen week does pair** (asserted, as the A1 test does for `Records`), with no
         `matchups` key in either serializer; the pinned-ETag test is left as it is.
       - Web: `matchupStatus` (leading, level, before kick-off, won, tied); the panel: one card
         per game, titles live and final, no swatch for an unlisted roster, "Won" only once final,
         new games between polls; absent in `TotalPoints`; axe on the `HeadToHead` page, whose
         fixture gains a game against an unlisted roster.
       - The layout verifier at 375 px, 360 px and desktop against the invented season, which
         gains a week of games.
    8. **Docs.** A41: the week chosen, one pairing rule, the placeholder's side, null rather than
       empty or partial, no median card, the no-playoffs week. A1–A41 in CLAUDE.md, design.md and
       amendments.md. Design §09 the field and its example body, §10 the panel.
       `.claude/agents/api-contract.md`: `matchups` optional, absent in `TotalPoints`. Runbook:
       nothing. New `.cs` files CRLF with a BOM; the `Matchups` mapping private, tested
       through `ToSeasonPayload`.
  - [ ] **T72** Division standings, H2H only (Q14) — 4 h `±`.
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
    6. **Lift the gate (A38)**, only once the logs say "matchups complete" for every season and
       the record check (A39) has run clean: `[AllowedValues(TotalPoints)]` on `LeagueOptions.Format` becomes
       `[EnumDataType(typeof(LeagueFormat))]`, and `Anything_but_total_points_is_refused_until_T72_is_built`
       becomes a test that `HeadToHead` binds. The CLI ranks by record in `HeadToHead`, as the page
       does (it still ranks by points). Then plumb `League__Format` into `infra/` (A38, last bullet)
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
       From `playoff_week_start` on it replaces T71's matchups panel, which until then shows the
       last regular week.
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
| Formats | T64, T66–T68 | Regular season only (A37), chart zoom (A36), `League:Format` (A38), the head-to-head record, gated until T72 (A39) |

Settled questions: Q1–Q15, in design §14. Fixtures B1–B3, B5 captured.
