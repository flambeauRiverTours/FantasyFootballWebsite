# Ideal Points — Tasks

Reasons live in [`docs/amendments.md`](../docs/amendments.md); finished work's detail is in
git history.
`±` marks an estimate that could run double.

**Status (2026-11-15).** M0–M7 built and deployed. T64 and T66 merged; T67 built on its branch;
T68–T72 planned. One T61 check left.

## Open

- [ ] **T66–T72 · Zoom, head-to-head, matchups, divisions, playoffs, a champion** (A36,
      planned 2026-11-15, revised after design review). Reverses §01's "out of scope:
      head-to-head, playoffs" for leagues configured that way. Order: T66 → T64 (both merged)
      → T67 → T68 → T71 → T72 → T69 → T70.
  - [x] **T66** Chart zoom and the mobile flash. Merged 2026-11-15 (A36); the flash fix
       confirmed on a real phone.
  - [x] **T64** Regular season only. Merged 2026-11-15 (A37).
  - [ ] **T67** `League:Format` = `TotalPoints` (default) | `HeadToHead`, sent as `format`; past
       seasons cached a day; head-to-head refused at start until T72. Built 2026-11-15 on its branch,
       not merged (A38).
  - [ ] **T68** Head-to-head record — 12–14 h `±`. On T67's branch, as two
       commits: **T68a** has real effects in production (storage, backfill, a logged check) and
       is verified in the logs; **T68b** is gated off until T72 and proven by tests. Designed and
       reviewed 2026-11-15.
    **T68a · the data**
    1. **Capture first** (Jack runs it). Add `league_average_match` to `SEASON_SETTINGS`. Write each
       roster's `settings.wins/losses/ties/fpts/fpts_decimal/fpts_against/fpts_against_decimal`,
       keyed by roster number only, to `rosters-record.json` beside the weeks, so the check in
       step 8 is tested on real data. Also capture last season's rosters, to settle whether
       Sleeper's W-L includes playoff games. Known from the fixtures: every roster has a
       `matchup_id` in weeks 1–5, three games a week, one against the placeholder (roster 5),
       who scores 1–12, never zero. Assumed, until a capture shows otherwise: a bye sends a null
       `matchup_id`; Sleeper's W-L includes median results and excludes playoff games; its median
       game is "top half wins", which matches above/below the median except when the middle two
       tie.
    2. **The week knows its game.** `MatchupDto.MatchupId` (`int?`) → `RosterWeek.MatchupId` →
       `TeamWeek.MatchupId`, where **0 is a bye and null means "never read"**, so a row stored
       before T68 is told apart from a bye. `ToTeamWeek` maps Sleeper's null to 0.
       `league_average_match` joins `LeagueSettingsDto` → `SleeperLeague.HasMedianGame`, per
       season, like `playoff_week_start`.
    3. **Store every roster (A1 moves to the read side).** Finalize writes `snapshot.Teams`, which
       `GroupTeams` has already filtered, so a stored week cannot score a game against the
       placeholder. `SeasonSnapshot` gains `ExcludedTeams`, a separate list rather than a flag,
       so nothing that counts can pick it up by accident, and T70/T71 have the placeholder's
       name. Finalize writes both. Everything that counts reads `Teams` only (checked: totals, σ,
       the radar, `LastCountedWeek`, the CLI). A1 is corrected to say the placeholder is stored
       and left out when read.
    4. **Storage.** An additive nullable `WeekScore.MatchupId` column, migration first. The SQL
       upsert **never overwrites an id with null** (`row.MatchupId = week.MatchupId ??
       row.MatchupId`): Finalize rewrites every final week from a snapshot that may predate the
       backfill, and without this it would null the ids again every Tuesday. The blob `Schema`
       goes 4 → 5, so an old blob is a cache miss and the next read primes v5 from SQL.
    5. **Backfill: `ISeasonService.BackfillMatchupsAsync`, in Seasons,** where `ToTeamWeek` lives.
       Run by its own timer function, daily all year (Finalize only fires September to
       February), and runnable by hand. Whatever the format: it is invisible in `TotalPoints`,
       and the data is ready when T72 lifts the gate. Per season the chain reaches, with
       anything stored: for each **stored** week through `FinalThroughWeek` where a row has a
       null `MatchupId`, or a roster in that season's league has no row, read that week's
       matchups once. Set `MatchupId` on the stored rows and leave everything else alone (D2:
       ideal points are never recomputed against today's map). Add the missing excluded rows
       from that read; their ideal points use today's map, which is harmless because they count
       in nothing but the pairing. Write the whole season back (the blob holds a season, and
       each past season has its own blob, named by its league's hash).
       - **Budget:** at most 12 Sleeper calls a run, so live requests keep the token bucket
         (25/min, queue 20); it finishes over a few days. A row in design §07.
       - **Isolation:** a try/catch per season, rethrowing only cancellation, so one season's
         failure cannot stop another, and it never touches Finalize.
       - **Convergence:** if Sleeper returns nothing for a week, or no entry for a roster, that
         week is skipped for the run with one warning and stays null, and so do that season's
         records. Logged as counts per season, plus **"matchups complete for season N"**, which
         T72's gate-lift checklist requires.
       - The 24 h `past:{season}` cache is not dropped; acceptable while gated.
    6. **Core: `HeadToHead.Records(weeks, hasMedianGame)`**, a pure function over every roster's
       final weeks up to the trimmed last regular week (`LastCountedWeek`; trimmed weeks are
       0-0 matchups and would count as ties). Pairs on (week, matchup id): the higher score wins,
       equal ties, the opponent's points add to points against. A game against the placeholder
       counts; a bye (0) is no game. With the median game each final week adds a result against
       that week's median over every roster that played: above wins, below loses, equal ties.
       Returns `{ wins, losses, ties, pointsFor, pointsAgainst }`, `pointsFor` over the same
       weeks, so the tiebreak matches Sleeper's, which counts no live week. **All null** if any
       counted week has a null matchup id, or a matchup id shared by other than two rosters
       (reported once): a partial record would rank teams wrongly.
    7. **The records are computed once, in `SeasonService`**, into `SeasonSnapshot.Records`
       (by roster, the placeholder included), so the check and the payload read the same
       answer. Computed in both formats.
    8. **The third check: the record matches Sleeper's.** `RosterDto` gains `settings.wins`,
       `losses`, `ties` (already fetched, cached 5 min). A separate `RecordMismatch` type and
       message, since `InvariantViolation` is per-week points. Checked for every roster when the
       records are not null and both sides count the same number of games (W + L + T), since
       Sleeper updates rosters on its own schedule. Reported once per roster and game count.
       **It runs in `TotalPoints` too, logging only**, so T68 is proven against the real league
       while the gate is on. A stat correction after Tuesday can make it fire; that is real.
    **T68b · the record, gated**
    9. **Payload:** `record: { wins, losses, ties, pointsFor, pointsAgainst }` on each team, only
       in `HeadToHead` and only when complete, and **left out of the JSON when null**
       (`JsonIgnore(WhenWritingNull)`), so the `TotalPoints` body and its ETag are byte for byte
       what they are today. The server keeps ordering teams by points; the page ranks.
    10. **Web, in `HeadToHead` only** (a missing `format` reads as `TotalPoints`, A38):
       - Columns follow the format: Team · Record · Points · PA (hidden on narrow screens) ·
         Ideal. `StandingsColumns` becomes a function of the format; `aria-sort` moves to Record.
         Design §10's "gap to first" becomes `TotalPoints` only, corrected in place.
       - The record reads "3-2", or "3-2-1" only when any team has a tie, with text for screen
         readers ("3 wins, 2 losses").
       - `rankByRecord` in calculations.ts: win % with ties as half, then points for, then name,
         so the order never shuffles between polls. The chart's chips (`rankByPoints`) and the
         radar's (the server's order) both follow it.
       - While the records are null, the table stays in its `TotalPoints` form with a line
         saying the record is not available yet. The Skeleton cannot know the format, so it
         stays points-format and swaps on load (accepted).
    11. **Tests, red first,** for every step above, and especially: pairing against
       `rosters-record.json`; **a snapshot with the placeholder's weeks gives a byte-identical
       `TotalPoints` payload, including a week only the placeholder scored in**; the upsert keeps
       an id over a null; the backfill stops at 12 calls, survives a season that fails or
       returns nothing, and calls nothing once complete; a format switch mid-backfill shows
       null records; the migration round-trips; blob v5; axe.
    12. **Docs:** A39 (the record, A1 on the read side, the backfill, the check), A1 corrected,
       design §03, §07, §08, §09, §10. Runbook: nothing to run; watch for "matchups complete"
       before T72.
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
       the record check (T68 step 8) has run clean: `[AllowedValues(TotalPoints)]` on `LeagueOptions.Format` becomes
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
