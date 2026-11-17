# Ideal Points — Tasks

Reasons live in [`docs/amendments.md`](../docs/amendments.md); finished work's detail is in
git history.
`±` marks an estimate that could run double.

**Status (2026-11-17).** M0–M7 built and deployed. T64, T66–T71, T72a and T73 merged;
head-to-head can be switched on (A44). T74 fixed on its branch; T72b next. One T61 check left.

## Open

- [ ] **T66–T72 · Zoom, head-to-head, matchups, divisions, playoffs, a champion** (A36,
      planned 2026-11-15, revised after design review). Reverses §01's "out of scope:
      head-to-head, playoffs" for leagues configured that way. T64, T66, T67 and T68 are merged
      (Done table), and so are T69–T71 and T72a; T72b is left. The real league has no divisions
      and no playoffs (captured settings), so what it sees went first.
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
- [ ] **T65** `League:SeasonOverrides` (D8, deferred by A34): a year → league ID map merged over
      the chain, for a league recreated from scratch. Not needed while the chain is intact.
- [ ] **T61** Domain 4b, item 16: try dropping the queue and table storage roles now that identity
      storage is live. The runbook's role list changes with it.

## Watch

- [ ] **January** Capture `state/nfl` after the season to check Sleeper's reset does not empty
      the current season and its crown (T70), and that week 18 is read after it (A46).
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
| M7 Motion | T51–T55, T74 | Optional; built. T74: the row slide's layout feature loads lazily, for the tables alone, and the radar panel loads as its own chunk to pay for it (initial JS 124.3 KB; the budget re-set to 135 KB, A47) |
| Past seasons | T29, T56 | Chain walk, season picker (A34) |
| Portfolio | T59–T60 | Consistency panel (A33); standards and seven domain reviews (A19–A33) |
| Formats | T64, T66–T71, T72a | Regular season only (A37), chart zoom (A36), `League:Format` (A38), the head-to-head record (A39), this week's matchups (A41–A43), the gate lifted, switched by `LEAGUE_FORMAT` (A44); T70 the champion (A45), T69 the playoff bracket (A46) |
| Cleanup | T73 | Repo-wide simplification and standards pass; the deploy's migrate sign-in (A40) |

Settled questions: Q1–Q15, in design §14. Fixtures B1–B3, B5 captured.
