# Local amendments

Decisions and answers settled **after** design v6, plus the answers to the open
questions.

This file exists because [`design.md`](design.md) and
[`field-manual.md`](field-manual.md) are converted wholesale from published
artifacts — a re-sync **overwrites them**. Anything recorded only there is lost.
Anything that must survive goes here.

Amendments are numbered `A`*n* so they never collide with the artifact's own
`D`/`R`/`Q` numbering. Where an amendment changes a decision in the design, it
says so and the design's own text is now out of date.

---

## Answers to the open questions

Settled 2026-09-10 against the live league. Q1, Q2, Q3, Q7 and Q8 are closed.

| | Question | Answer |
|---|---|---|
| **Q1** | League ID and `roster_positions` | Answered. 6 rosters. 11 startable slots, in order: `QB, QB, RB, RB, WR, WR, TE, FLEX, SUPER_FLEX, K, DEF`, plus 8 `BN`. The league ID itself is a secret — see **A2**. |
| **Q2** | SUPER_FLEX or IDP? | **SUPER_FLEX yes, IDP no.** Effectively a three-QB league: two `QB` slots *and* a `SUPER_FLEX`. But see **A3** — the eligibility map stays general. |
| **Q3** | Should IR players count toward ideal? | **Moot.** The league has no `IR` or `TAXI` slots at all — `roster_positions` is 11 startable plus 8 `BN` and nothing else. The design's default (exclude) stands if a slot is ever added. |
| **Q4** | Regular season only, or playoffs? | **Open.** `settings.playoff_week_start` reads `0`, which is unset rather than meaningful. Check the league settings directly. |
| **Q5** | Minimum weeks before σ — four or five? | **Open.** Four remains the assumption. |
| **Q6** | Table shows σ or CV? | **Open.** Revisit once a few weeks of real scores exist and the spread between team means is visible. |
| **Q7** | Is the season chain intact? | **2026 is the league's first season.** `previous_league_id` is null because there is no predecessor, not because a rollover was missed. See **A4**. |
| **Q8** | How many distinct starting slots? | **8** — `QB, RB, WR, TE, FLEX, SUPER_FLEX, K, DEF`. Comfortably readable as a radar; no need to merge axes or default to the diverging bars. |

11 startable slots is exactly 2^11 = 2048 DP states — the ceiling §05 assumed.
Fine, but with no slack if a slot is ever added.

---

## A5 · The `starters` alignment holds — verified, not assumed

Checked against the live league on 2026-09-10, week 1: **63 of 63 non-empty
starters were eligible for the slot at their own index. Zero violations.**

So `starters[i]` does correspond to the *i*th startable slot, and the positional
radar (§11, M6) rests on solid ground. Fixtures are in
[`tests/fixtures/`](../tests/fixtures/README.md).

This does **not** retire the guard §11 specifies. One week of one league is
evidence, not a contract — Sleeper documents no such promise, and a mid-season
rules change would break it silently. Keep the finalize-time assertion that
`starters.Count == startableSlots.Count`, and skip slot attribution for any week
that fails it.

Also confirmed in the same capture, all of which the design predicted:

- **Empty starter slots are the string `"0"`** — three of them across two
  rosters in a single week, so this is routine rather than theoretical.
- **DEF ids are team abbreviations** (`"BAL"`, `"DEN"`), never numeric.
- **`sum(starters_points) == points`** held for all six rosters, including the
  four still on zero.
- **Every `players` entry had a `players_points` entry.** Still treat a missing
  one as `0m` — week 1 has no byes, so this case has not actually been exercised.

### What is not yet available

2026 is the league's first season (A4) and week 1 was mid-flight when this was
captured, so there is **no finalized week anywhere in the data**. Consequences:

- T06's fixtures are structural for now. `ideal >= actual` can be tested; nothing
  that depends on a final score can.
- σ needs four finalized weeks (D1), so the earliest it returns anything but
  null is after week 4.
- There is no history to backfill. T30 has nothing to do until August 2027.

Re-capture after each week finalizes.

## A1 · One dummy team is excluded from every metric

**"POON SQUAD" is a placeholder, not a competitor.** It is excluded from the
standings and from every league-wide reference value.

**Identify it by owner `user_id`, never by team name or `roster_id`.** Team names
change whenever a manager feels like it, and `roster_id` is reassigned between
seasons — §08 already establishes `user_id` as the only stable identifier, which
is exactly what it is for here. Keep the team name in a config comment for
humans; match on the ID.

```jsonc
// configuration, not committed — see A2
"League": {
  "ExcludedOwnerIds": [ "…" ]   // POON SQUAD — dummy team
}
```

Empty by default, so a league without a dummy team needs no configuration.

### This is not just a hidden row

A dummy team is auto-drafted and its lineup is never set, so its weekly scores
are both low and erratic. Leaving it in any denominator would quietly bias every
other number on the page:

- The **league median** display mode (§10) shifts down, so every real team's
  "vs. median" line reads better than it should.
- **`leagueSlotAverages`** (§11, T47) deflates at every slot, which inflates
  every real team's radar ratio. The reference circle stops meaning "the league".
- **σ and ideal** are per-team, so they are unaffected — but the team must still
  not appear as a row.

So "excluded from all metrics" means excluded as a row *and* excluded from every
league-wide aggregate. Those are two separate filters and it is easy to write
only the first.

### Store its rows anyway; filter at the read boundary

Fetch and persist `WeekScore` rows for all six rosters, and apply the exclusion
when assembling the response, not when writing the database. Rows are cheap —
§08 puts a whole season at a few hundred kilobytes — and if the slot is ever
taken by a real manager, the history is already there rather than needing a
backfill.

**Touches:** T15 (season assembly), T18 (the endpoint payload), T47 (league
reference means). Not numbered as a task here, because task IDs are owned by the
published tracker and inventing one locally would desync it — add it there.

**Six rosters, minus one, leaves five scoring teams.**

---

## A2 · League IDs and user IDs are secrets

Neither the league ID nor any Sleeper user ID is committed to this repository.

**What this does and does not buy.** The Sleeper read API is public and
unauthenticated, and once the site is live, anyone with its URL sees the league's
data anyway — so this is not access control. What it does buy is not publishing
identifiers that tie a public GitHub repository to personal Sleeper accounts, and
not handing a scraper a working league ID. That is worth the small effort, and
it is worth being clear that it is the actual goal.

| Where | How |
|---|---|
| Local development | .NET Secret Manager — `dotnet user-secrets set "League:CurrentLeagueId" "…"`. Needs a `<UserSecretsId>` in the csproj; that GUID is committed and is not itself a secret. |
| Azure | App Settings, as `League__CurrentLeagueId` (double underscore). |
| GitHub Actions | Repository secret, passed into the bicep deployment. Never a literal in a workflow file or a `.bicepparam`. |

### Mark the bicep parameter `@secure()`

T20's `az deployment group create -p leagueId=…` records parameter values in the
resource group's **deployment history in plain text**, readable by anyone with
Reader on the subscription — and it stays there. `@secure()` on the parameter is
what keeps it out:

```bicep
@secure()
param leagueId string
```

### Fixtures are safe; the users endpoint is not

T02 says to commit the captured weeks, and that is fine: a `/matchups` payload
carries `roster_id`, player IDs and points, and no user IDs. The league ID
appears only in the request URL, never in the response body.

A capture of `/league/{id}/users` is different — it carries `user_id`,
`display_name` and avatar IDs for every manager. Do not commit that one, or
scrub the IDs first. And do not name any fixture file after the league ID.

### The league id leaked into a test, and was expunged

Recorded 2026-09-13, while preparing to make the repository public.
`HostRegistrationTests.cs` used the **live** league id as its fixture value,
introduced in the T17 commit and carried forward by five more. The A2 gate in
`security.yml` caught it.

- **It never reached the remote.** The commits lived only on the local
  `feat/M2-azure-setup` branch; no origin ref contained the id, so no
  force-push and no GitHub-side cleanup were needed.
- Six commits were rewritten with `git filter-branch`, `refs/original` deleted,
  the reflog expired and `git gc --prune=now` run. A scan of every object in the
  database then found zero blobs containing it.
- The placeholder is `1234567890123456789`.
  `A_nineteen_digit_id_survives_as_a_string` asserts `HaveLength(19)`, so any
  replacement has to stay nineteen digits.

**A real id in a test does not look like a leak.** It looks like the value the
code will actually see, which is exactly why it survived review. Fixture values
are placeholders even when a real one is at hand.

---

---

## A3 · Keep the eligibility map general, not pruned to 2026

The league uses `SUPER_FLEX` and no IDP **this season**. Do not treat that as
permanent: the slot configuration can change between seasons, and the design
already anticipates it — each season is its own `League` row with its own
`RosterPositionsJson` (§11), so the slot list is per-season data, not a constant.

Concretely:

- Keep `IDP_FLEX`, `REC_FLEX` and `WRRB_FLEX` in T04's map and keep them tested,
  even though 2026 exercises none of them. They are cheap to carry and expensive
  to add back under pressure mid-season.
- Never hardcode 11 slots, 8 axes, or this year's slot order anywhere. Derive all
  three from that season's `roster_positions` at read time.
- An unrecognised slot code should fail loudly rather than being silently dropped
  as unstartable — a new flex type Sleeper adds would otherwise vanish from the
  ideal calculation with no signal.

This also protects Q8: 8 axes is comfortable now, but a superflex-plus-IDP season
would reach eleven or twelve and need D11's diverging bars as the default.

---

## A4 · No season chain to walk yet

2026 is the league's first season, so `previous_league_id` is null legitimately.

**D8 still stands unchanged** — the chain walk is simply a no-op that terminates
on the first hop, and `League:SeasonOverrides` stays empty because there is no
history to reach. The first real test of D8 arrives at the August 2027 rollover,
which is also the first time T45's runbook gets used in anger.

Worth building the walk anyway rather than deferring it: it costs one call a day
against a 2.3/min budget, and the alternative is remembering to write it in a
year.

---

## A6 · An unset team name is absent, not an empty string

§03 says team name resolves as `users[].metadata.team_name` falling back to
`display_name`, and that *"a manager who never set a team name has an empty
string, not a missing key."*

**That is not what this league returns.** Checked against `/league/{id}/users` on
2026-09-10: for two of six managers the `team_name` key is **absent from
`metadata` altogether**, and `metadata` itself can be null on a user.

So resolution must collapse three cases, not one: key absent, value null, value
empty or whitespace. `SleeperMapper.ResolveTeamName` uses
`string.IsNullOrWhiteSpace` for exactly that, with a final `Roster {n}` fallback
so an orphaned roster or a manager with neither name never renders as a blank row.

Code written to the design's wording — testing `team_name == ""` — would have
produced two blank team names in the standings.

---

## A7 · A commissioner override wins over the scored points

The matchup payload carries **`custom_points`** alongside `points`. It is null on
every entry in the captured week, and it is the value Sleeper displays once a
commissioner has corrected a score.

`SleeperMapper.ToRosterWeek` therefore maps actual as `custom_points ?? points`.
The failure mode of ignoring it is quiet and nasty: a single stat correction
would leave this site's standings permanently disagreeing with the Sleeper app,
with nothing to indicate which was right and no invariant that could catch it —
`sum(starters_points) == points` compares against `points`, not the override.

**Worth confirming against Sleeper's documentation before relying on it.** The
mapping is written the safe way round; if it turns out `custom_points` is
additive rather than a replacement, one line changes.

---

## A8 · An unresolved player is reported, never dropped

`RosterWeek` carries `UnresolvedPlayerIds` — ids on a roster that the player map
could not explain.

This exists because the map is refreshed daily (§03), so a player signed and
rostered between refreshes is genuinely absent for a few hours. Dropping him
quietly would lower that team's ideal points, and **`ideal >= actual` cannot
catch it**: undercounting is the restrictive direction the invariant is blind to,
the same blind spot T16's warning describes for slot eligibility.

Callers decide what to do — the finalize timer should refuse to freeze a week
with unresolved ids rather than write numbers it knows are low. What must not
happen is the list being ignored.

---

## A9 · Both invariants compare against Sleeper's scored points, never the override

§03 states `ideal >= actual` holds "always, with no exceptions". With A7 in
place that is no longer true, because `actual` is `custom_points ?? points` and a
commissioner may set `custom_points` to anything at all.

Both checks therefore compare against **`points`**, Sleeper's own computed total:

- `sum(starters_points) == points` — comparing the sum against an override would
  fire a violation every time that week was assembled, forever, from one
  correction.
- `ideal >= points` — §03's rationale is that "the started lineup is by
  definition one of the candidate assignments the solver considers, so a
  violation is a solver bug". That reasoning holds against a scored total and
  not against an arbitrary adjustment.

`RosterWeek` carries both: `Actual` is what the site displays, `Points` is what
the invariants test.

**The visible consequence:** after a generous override the page can show an ideal
below the actual. That is correct, not a bug — the manager really did score more
than their roster could have produced, because someone decided so.

---

## A10 · Excluded owners are a list, carried as one comma-separated secret

A1 excludes owners by user id, and `LeagueOptions.ExcludedOwnerIds` has always
been an `IList<string>`. The deployment path did not match: `main.bicep` took a
single `@secure() string` and wrote it straight to
`League__ExcludedOwnerIds__0`, so a second excluded owner was unreachable
without editing the template, and an empty value still wrote index 0 as an
empty string.

**The secret is a comma-separated list.** `EXCLUDED_OWNER_IDS` holds
`id` or `id,id,id`; whitespace around each entry is trimmed and empty entries
are dropped, so `a, b,` and `a,b` are the same input. The template expands it:

```bicep
var excludedOwnerIdList = filter(map(split(excludedOwnerIds, ','), id => trim(id)), id => !empty(id))

var excludedOwnerSettings = [
  for (ownerId, index) in excludedOwnerIdList: {
    name: 'League__ExcludedOwnerIds__${index}'
    value: ownerId
  }
]
```

**Why a delimited string rather than an array parameter.** Bicep does support
`param excludedOwnerIds array`, but `@secure()` applies to `secureString` and
`secureObject` only — there is no secure array. A GitHub secret is also a
single opaque string, so an array parameter would mean encoding JSON into the
secret and `json()`-parsing it in the template, which buys nothing and fails
noisily on a stray quote.

**Why indexed App Settings rather than one delimited setting.** `League__ExcludedOwnerIds__0`,
`__1`, `__2` is how .NET's configuration binder reads a collection from
environment variables; the double underscore becomes `:`. Binding an
`IList<string>` from a single comma-separated value would need a custom
converter. The generated form needs no application code at all, which is why
`LeagueOptions` is unchanged by this amendment.

**The empty case now writes nothing.** A league with no placeholder team
produces an empty array and no `League__ExcludedOwnerIds__*` settings, rather
than one setting holding an empty string.

**A typo is reported.** An owner id matching no roster used to be ignored in
silence, which a longer list makes likelier — the excluded team would simply
stay in the standings with nothing to explain why.
`SeasonFunctions.ExcludedRosterIds` now logs a warning naming each unmatched
id. Two caveats worth knowing: the warning carries a Sleeper user id into
Application Insights, which A2 treats as a secret — acceptable because that
telemetry is private to the subscription, and useless without the id in it; and
it fires on **every request** while the misconfiguration stands, like the
existing violation and unresolved-player warnings, which matters against the
0.16 GB/day ingest cap.

---

## A11 · The storage firewall stays open, with an expiry on the exception

Trivy's `AZU-0012` fails the storage account for leaving
`networkAcls.defaultAction` unset, which means `Allow`. The finding is correct;
acting on it is not free.

That account is the Function App's runtime store **and** its Flex Consumption
deployment container. Setting `Deny` cuts the app off from both. On Flex
Consumption without a virtual network there is no cheap way back in:
`bypass: AzureServices` does not cover the Functions runtime's own access, so
the supported route is VNet integration plus service or private endpoints.
Service endpoints are free; **private endpoints are around $7 a month each**,
and more than one would be wanted — several times the entire budget in §13, for
an account holding a deployment zip and some queue state.

**What the account already has:** `allowBlobPublicAccess: false`,
`minimumTlsVersion: TLS1_2`, `supportsHttpsTrafficOnly: true`, no public
containers. The residual exposure is an account reachable over the network but
requiring a key.

**The exception is dated, not permanent.** `.trivyignore.yaml` carries
`expired_at: 2027-03-01`; after that the finding returns and has to be argued
again. The alternative — dropping `misconfig` from the scanner list, or setting
`exit-code: 0` — would also discard the seven checks that currently pass and
every future one that catches something real. A permanently red gate gets
ignored, which is worse than no gate.

**Revisit when** the app gains a VNet for any other reason, or if D-level
decisions ever put anything but runtime state in that account.

---

## Where the design is now out of date

- **§03 is wrong about team names** — see A6.
- **§03's "ideal >= actual, always" no longer holds** once A7's override exists — see A9.
- **§08, §10, D11 assume twelve teams.** The league has **six rosters, five of
  them scoring** (A1). §08's "roughly 12 teams × 18 weeks is 216 rows" becomes
  ~108. More substantively, the legibility arguments soften: five lines on a
  chart are readable without the hover-to-isolate interaction (§10), and D11's
  cap of two teams on the radar is more conservative than five teams needs. The
  decisions are still fine; the reasoning behind them is weaker than written.

---

## Corrections applied to the converted manual

These live in `field-manual.md`, which a re-sync overwrites. **Re-apply them
after any sync**, or fix them upstream in the artifact.

- **T01** — `dotnet new func --worker-runtime dotnet-isolated` is wrong.
  `--worker-runtime` belongs to `func init` from Core Tools, and is unnecessary:
  the `func` template in `Microsoft.Azure.Functions.Worker.ProjectTemplates` is
  already the isolated-worker one. Correct command:
  `dotnet new func -F net9.0 -o src/FantasyLeague.Functions`.
- **T01** — `-F net9.0` is load-bearing. The template defaults to `net8.0` and
  writes an explicit `<TargetFramework>` into the csproj, which shadows
  `Directory.Build.props` and would leave the Functions project a framework
  behind its siblings, silently.
- **T01** — the shell blocks are POSIX and need Git Bash; added verified
  PowerShell and cmd equivalents for the `dotnet sln add` line, and noted that
  `dotnet sln add **/*.csproj` does not work because the CLI does no globbing.
- **T01** — `dotnet new sln` produces `.slnx` on the .NET 10 SDK; `-f sln` forces
  the classic format.
