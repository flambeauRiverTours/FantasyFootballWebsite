# CLAUDE.md — Ideal Points

## Claude's role in this repo: coach, not implementer

**Jack writes all the production code by hand.** That is the point of the
project — the field manual exists so a person can do the work, not so an agent
can execute it. The 108-hour estimate is a feature.

Claude's job here is to **explain, review, unblock and answer questions.**

**Do not** write or edit files under `src/`, `tests/`, `web/`, `infra/` or
`.github/workflows/` unless Jack explicitly asks for that file to be written.
"How do I…", "why isn't this working", "is this right", "what does this error
mean" and "show me an example" are all requests for **explanation**, not for a
patch. Answer in the conversation. Put example code in a fenced block in the
reply, not in the repo.

When something is wrong, say what is wrong and why, point at the file and line,
and let Jack make the change. If a fix is genuinely mechanical and Jack asks for
it, apply it — but ask first rather than assuming.

**Freely writable without asking:** `tasks/todo.md`, `tasks/lessons.md`, and
`docs/` (which are notes, not the build).

### What good help looks like here

- Explain the *mechanism*, not just the incantation. "Add `id-token: write`"
  is less useful than "without that permission GitHub mints no token at all,
  so `azure/login` fails for a different reason than a subject mismatch."
- Point at the design document. Most "should I…" questions are already
  answered by a D-numbered decision in [`docs/design.md`](docs/design.md);
  cite it (`D4`, `R1`, `Q3`) rather than re-deriving the answer.
- Say when the design is wrong. It is a document, not scripture. If reality
  disagrees with it — and T16 exists because it will — flag the conflict and
  propose the amendment.
- Prefer the smallest question that unblocks. Don't survey four approaches
  when the design already picked one.

---

## The project in one paragraph

A public, read-only page for one Sleeper fantasy league, decided on **total
season points**. Two commentary columns sit beside it: **consistency σ** (how
steadily the points arrived) and **ideal** (how many never left the bench).
A third view, the **positional radar**, plots one team's average at each of its
own starting slots as a ratio to the league average at that slot. .NET 9 on
Azure Functions behind Static Web Apps, Azure SQL for finalized weeks,
Vite/React/TypeScript/D3 for the page. $0.00/month steady state, ≤ 2.3 Sleeper
requests per minute regardless of viewer count.

## Where things are

| Path | What |
|---|---|
| [`docs/design.md`](docs/design.md) | Design v6 — sections 01–15, decisions D1–D11, risks R1–R2, open questions Q1–Q8. Corrected in place; **Corrected** marks where it differs from the artifact |
| [`docs/field-manual.md`](docs/field-manual.md) | A **Built** reference for M0–M2, then step-by-step instructions for T24 onward |
| [`tasks/todo.md`](tasks/todo.md) | The task list. Tick tasks here as they land. |
| [`docs/amendments.md`](docs/amendments.md) | **A1–A12: the reasoning behind every decision made after v6, and why each correction to the other two documents is right.** |
| [`tasks/lessons.md`](tasks/lessons.md) | Rules written after a correction |

Task IDs are stable across all three files and the published tracker: T21 in the
manual is T21 in the todo is T21 in the artifact.

**All three documents are now living and edited directly.** `design.md` and
`field-manual.md` began as conversions of published artifacts, but both have been
corrected against what was built — **re-syncing either would reinstate errors
that took real debugging to find**, and would undo the M0–M2 compaction.

Where to put a new decision: the conclusion goes in whichever document states the
thing, and the **reasoning** goes in `amendments.md` as the next `A` number. A
correction to the design or the manual is applied in place *and* recorded there,
so the record of what was wrong survives even though the text no longer is.

## Things that are settled — don't relitigate them

These have decision records. Cite them; don't re-argue them unasked.

- **D1** σ is *population* (÷n, not ÷n−1), over **finalized weeks only**, and
  returns `null` — not `0.0` — below four weeks.
- **D2** Ideal solves against `matchup.players` for *that week*, never today's roster.
- **D4** The solver is an exact bitmask DP, not greedy. Greedy is wrong whenever
  a FLEX slot exists, and it undercounts — which is the one direction a
  "points you left on the bench" metric must not be wrong in.
- **D5** Static Web Apps + Functions, not a container app. Timer triggers decide it.
- **D6** Polling at 30 s, not websockets.
- **D7** Azure SQL serverless free offer, pause-on-limit. No blob snapshot layer on day one.
- **D8** One configured league ID; prior seasons resolved by walking `previous_league_id`.
- **D9** React owns the DOM; D3 is a maths library. Never `d3.select().append()`.
- **D10** Every radar axis is a **ratio to the league average**, centred on 1.0 —
  never raw points. Raw points draw the same QB-shaped spike for every team,
  because that shape is a property of football rather than of the roster.
- **D11** The radar shows one team at a time (two for a head-to-head, no more),
  with a diverging bar chart on the same data as the honest alternative.
- **R2** A radar's shape is partly an artefact of the drawing — area grows with
  the square of the radius, and the silhouette depends on axis order. So fix the
  axis order to `roster_positions` order and never re-sort it, and never describe
  enclosed area as if it meant anything.

The league ID is configuration (`League:CurrentLeagueId`), **never** a request
parameter — a league ID in the query string turns this into an open relay
against Sleeper's rate limit, attached to Jack's subscription.

## Two local rules that override the design

- **A1 · The dummy team is excluded from everything.** "POON SQUAD" is a
  placeholder. Exclude it as a standings row *and* from every league-wide
  aggregate — the median line in §10 and `leagueSlotAverages` in §11. Those are
  two separate filters and it is easy to write only the first; a dummy team left
  in a denominator biases every real team's number. Match on owner `user_id`,
  never team name or `roster_id`. The league is **six rosters, five scoring** —
  not the twelve §08, §10 and D11 assume.
- **A2 · League IDs and Sleeper user IDs never enter the repository.** Not in
  code, not in docs, not in a fixture filename, not in a workflow, not in a
  `.bicepparam`, not in a commit message. `dotnet user-secrets` locally, App
  Settings in Azure, a GitHub secret in CI, and `@secure()` on the bicep
  `leagueId` parameter — without it the value is written to the resource group's
  deployment history in plain text and stays there. Captured `/matchups`
  fixtures are safe to commit (no user IDs); a `/league/{id}/users` capture is
  not.

If asked to put a real league or user ID into a tracked file, say no and point
here. Placeholders (`<your-league-id>`, `$L`) are fine.

## The two invariants

Assert both in production, not only in tests:

1. `sum(starters_points) == points` per roster, within a cent.
2. `ideal >= actual` for every team-week, always. A violation is a solver bug,
   almost always a slot eligibility rule.

## Sleeper API traps

Worth naming before they cost an afternoon:

- Empty starter slots are the **string `"0"`**, not null and not absent.
- Defence player IDs are **team abbreviations** (`"DEN"`), not numeric strings.
- Fields are **absent, not null**, for weeks that have not happened.
- Match eligibility on **`fantasy_positions`**, not `position` — the plural
  field is what makes a multi-eligible player legal in a flex slot.
- The league's slot list is **per-season data, not a constant** (A3). It uses
  SUPER_FLEX and no IDP in 2026; keep `IDP_FLEX`, `REC_FLEX` and `WRRB_FLEX` in
  the map and tested anyway, and never hardcode 11 slots, 8 axes or this year's
  slot order. An unrecognised slot code should fail loudly, not be silently
  dropped as unstartable.
- A player in `players` with no `players_points` entry scored **zero**, not an error.
- Sleeper exposes **no projections**. Anything suggesting a projected-points
  column is pre-v2 thinking.
- `starters[i]` is **positionally aligned** with the *i*th startable slot. The
  whole positional radar rests on this, and it is an observation rather than a
  documented guarantee — T02 checks it, and the finalize job asserts
  `starters.Count == startableSlots.Count` and skips slot attribution for the
  week if it ever stops holding.

### Averaging, for the radar

Average first, then divide. A ratio computed per week and then averaged blows up
on the week the whole league's kickers scored two points between them — a
near-zero denominator turns one bad Sunday into a permanent distortion. And
divide each slot's points by the number of such slots, or a league with two RB
slots and one QB slot makes every team's RB axis twice its QB axis.

## Conventions

- .NET 9, `Nullable` enabled, `TreatWarningsAsErrors` — Sleeper's payload is full
  of legitimately absent fields and the compiler is the cheapest defence available.
- `FantasyLeague.Core` takes **no** Azure dependency. Solver and σ are pure
  functions over fixtures, testable offline in milliseconds.
- Wire DTOs (`FantasyLeague.Sleeper`) stay separate from domain records
  (`FantasyLeague.Core`), joined by one explicit mapping function.
- Every Sleeper call goes through the one typed `HttpClient` with the token-bucket
  handler attached. That handler is the structural rate-limit guarantee — a
  singleton, because a per-request limiter is not a limiter.

## T02 comes before the domain code

An hour of `curl` against the real league, before a line of `Core`. It settles
Q1–Q4 and Q7, captures real weeks as fixtures, and checks the `starters`
alignment. Everything after it should be written against what Sleeper actually
returns rather than an assumption about it — so if a question can be answered by
T02, the answer is "run T02", not "assume and correct later".

T02 is recon, **not** verification: neither invariant can be asserted there
because neither the solver nor the client exists yet. Those land in T16.

## Working agreement

- Plan before non-trivial work; write the plan to `tasks/todo.md` and check in.
- Mark tasks complete only once they're proven — tests run, output shown.
- After any correction, add the rule to `tasks/lessons.md`.
- Don't push or open PRs unless asked.
