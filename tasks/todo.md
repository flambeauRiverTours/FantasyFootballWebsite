# Ideal Points — Tasks

Reasons live in [`docs/amendments.md`](../docs/amendments.md); finished work's detail is in
git history.
`±` marks an estimate that could run double.

**Status (2026-11-17).** M0–M7 built and deployed. T64, T66–T68, T71, T72a and T73 merged;
head-to-head can be switched on (A44). T70 and T69 built together on one branch; T72b next. One T61 check left.

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
      (Done table). T70 and T69 built together; next T72b. The real league has no divisions and
      no playoffs (captured settings), so what it sees went first: T72a, then the champion.
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
  - [ ] **T69** The playoff bracket, H2H only — 6 h `±`. Designed 2026-11-17, built on the T70
       branch at Jack's request (one PR). Design-reviewed three times: not green, then Jack decided
       the two questions (below), then green with changes, folded in. The reasoning is A46.
    0. **A playoff league's season ends at its final** (Jack): `SeasonSnapshot.SeasonEndWeek` and
       `IsComplete`, sent as `seasonEndWeek` and `isComplete`; the page polls through the
       playoffs, the masthead says "Playoffs · Week N", and T70's champion waits. With playoffs
       but no bracket the season ends at week 18 and nobody is crowned. A past season is complete.
    1. **Sleeper:** `winners_bracket` read tolerantly (`t1`/`t2` an id or `{w|l: m}`, `p` optional,
       `null`/`[]` empty), `playoff_round_type` (0 a week per round, 1 a two-week final, 2 two
       weeks per round: an assumption, A46; unknown shows no bracket).
    2. **Kept** on the snapshot as `Bracket`, fetched in H2H with playoffs once the snapshot's
       week reaches the start (a past season always), cached 5 min / a day; a failure held a
       minute; a past build never caches a failed read.
    3. **Core `Playoffs`:** rounds to weeks; a round past week 18 drops the bracket; a match is
       settled when its weeks are final and Sleeper set `w`, or, **for week 18 (Jack), once `w`
       is set**. After a reset week 18 is always read, any format (§07 notes the cost).
    4. **Payload `playoffs`:** rounds (named "Final" for the round with `p = 1`, "Semifinals"
       before it, else "Round N") of matches (placement, settled, two sides: roster, name, from,
       points summed over the round's weeks, won). The bracket's champion is the final's `w`;
       an excluded winner is crowned (Q9) and named from the bracket.
    5. **Page:** a Playoffs panel in the matchups panel's place; a column per round on desktop,
       stacked on phones; cards with visible labels, reusing the matchup side; unknown sides as
       "Winner of match 2"; the crown on the final; no connectors.
    6. **CLI:** a line per match.
    7. **Tests, red first;** the browser against an invented season with a bracket.
  - [ ] **T70** The champion, without a bracket — 3 h `±`. Revised 2026-11-16: the bracket
       does not exist until T69, and the real league has no playoffs, so T70 crowns the table's
       leader once the regular season is final; T69 adds the bracket's final.
    1. **When:** the season is complete (with playoffs in head-to-head, at its final: A46, T69),
       `FinalThroughWeek ≥ LastCountedWeek`, the rule the
       page's `isSeasonComplete` already uses (A37), and the leader has scored (an empty league
       read in January is complete by the calendar). Taken from the trimmed weeks only. Current and
       past seasons alike; a past season is settled to its end.
    2. **Who, in Core (`Standings`):**
       - `TotalPoints`: the points leader; a tie crowns everyone tied (as planned).
       - `HeadToHead` **without playoffs** (`SeasonSnapshot.HasPlayoffs`, read from the cut-off: a
         `playoff_week_start` of 0, 1, none, or past 18 schedules no playoff week): the record leader by
         `RankByRecord`'s win rate, then points for. A tie on both crowns everyone tied: the name
         orders the table but never decides a title.
       - `HeadToHead` **with playoffs:** none until T69 crowns its final.
       - `HeadToHead` with records unknown (A39): none.
       - **Only the table's teams.** The placeholder never leads the table (A1); Q9's "an
         excluded roster that wins is crowned" is about the bracket, so it moves to T69.
    3. **Payload:** `SeasonPayload` gains `[JsonIgnore(WhenWritingNull)] IReadOnlyList<int>?
       Champions`, roster ids, absent until there is one. A `TotalPoints` body changes only once
       a season is complete; the pinned-ETag test (a live week) stays as it is, and a test pins
       that an incomplete season carries no key.
    4. **Page:** in the standings row of each champion, a crown (inline SVG, `role="img"`,
       accessible name "Champion") before the name, a gold tint on the row (new `--gold` and
       `--gold-weak` tokens, light and dark: the crown ≥ 3:1 and text ≥ 4.5:1 on the tinted row),
       and three small star sparkles (`aria-hidden`) that twinkle in CSS. The tint is a background image on
       the cells, so the sorted and hovered cells keep it; the crown keeps to the first word. Under `prefers-reduced-motion` they
       are still and the crown stays. In both tables (points and record). No `aria-live`.
       Under the table, in words: "Season complete. Champion: X." or "Co-champions: X and Y.",
       since a phone hides the masthead's status; on a tie the crown reads "Co-champion". Teams
       level on what decides a place show T1, T1 and the next place is 3 (Jack, 2026-11-16).
       The CLI adds a footer line `champion     Name`.
    5. **Docs:** A45; design §09 (`champions`), §10 (the crown), §14 Q9 pointing to T69;
       `api-contract.md`: `champions` optional, ids of rows in `teams`.
    6. **Tests, red first:** Core: points leader, tied points, record leader, tie on win rate and
       points for, records unknown, with playoffs, not complete, placeholder with the best score
       (not crowned). Mapper: a complete past season, a current one at its last week final and
       one week short, `HeadToHead` with playoffs. Web: the crown and its name, the tint, the
       sparkles hidden, none when absent, co-champions and the line in words, T1 places; axe;
       text on the tint (contrast). Reduced motion checked in the browser (jsdom cannot
       evaluate the media query). CLI: the footer line. Browser: the invented season, made complete, at
       360 px and desktop, light and dark.
    7. **Not here:** capturing `state/nfl` in January to check it does not reset and empty the
       current season and its crown; that check stays on the list for January.
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
| Formats | T64, T66–T68, T71, T72a | Regular season only (A37), chart zoom (A36), `League:Format` (A38), the head-to-head record (A39), this week's matchups (A41–A43), the gate lifted, switched by `LEAGUE_FORMAT` (A44) |
| Cleanup | T73 | Repo-wide simplification and standards pass; the deploy's migrate sign-in (A40) |

Settled questions: Q1–Q15, in design §14. Fixtures B1–B3, B5 captured.
