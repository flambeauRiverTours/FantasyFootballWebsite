# Local amendments

Decisions and answers settled **after** design v6, plus the answers to the open
questions.

**As of 2026-09-13, both [`design.md`](design.md) and
[`field-manual.md`](field-manual.md) are living documents**, corrected in place
against what was actually built. Neither is re-synced from its published
artifact any more, and a re-sync of either would reinstate errors that took real
debugging to find.

That changes what this file is for. It was the place things went to *survive* a
sync; it is now the place that holds the **reasoning** — what was decided after
design v6, what the design got wrong, and why each correction is the right one.
The corrected documents state the conclusion; this one says how it was reached
and what it cost to learn.

Amendments are numbered `A`*n* so they never collide with the artifact's own
`D`/`R`/`Q` numbering.

---

## Answers to the open questions

Settled 2026-09-10 against the live league. Q1, Q2, Q3, Q7 and Q8 are closed.

| | Question | Answer |
|---|---|---|
| **Q1** | League ID and `roster_positions` | Answered. 6 rosters. 11 startable slots, in order: `QB, QB, RB, RB, WR, WR, TE, FLEX, SUPER_FLEX, K, DEF`, plus 8 `BN`. The league ID itself is a secret — see **A2**. |
| **Q2** | SUPER_FLEX or IDP? | **SUPER_FLEX yes, IDP no.** Effectively a three-QB league: two `QB` slots *and* a `SUPER_FLEX`. But see **A3** — the eligibility map stays general. |
| **Q3** | Should IR players count toward ideal? | **Moot.** The league has no `IR` or `TAXI` slots at all — `roster_positions` is 11 startable plus 8 `BN` and nothing else. The design's default (exclude) stands if a slot is ever added. |
| **Q4** | Regular season only, or playoffs? | **Open.** `settings.playoff_week_start` reads `0`, which is unset rather than meaningful. Check the league settings directly. |
| **Q5** | Minimum weeks before σ — four or five? | **Four**, per D1. `SpreadCalculator.MinWeeks` is a constant (A27). |
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

### Resource identifiers are not credentials, and still do not belong in the repo

2026-09-13. The A2 gate failed on `field-manual.md` after the documentation pass.
The manual had acquired three literals written for convenience: the Log Analytics
workspace ID, the storage account name, and the Function App hostname. Every
command in that runbook now derives what it needs from `az` instead.

None of the three is a credential. A workspace ID alone reads nothing. But the
repository is intended to go public, and A2's purpose was never only "no
passwords" — it is **not publishing identifiers that tie a public repository to
personal infrastructure**. A workspace ID plus any leaked token is enough to
query telemetry; a storage account name is a target to try keys against.

**The gate also had to change.** It reported only *that* a secret appeared, not
*which*, and GitHub masks the values — so the error named a file and nothing
else. It now checks each secret by name and says which one matched, and skips
any value under twelve characters with a warning, because a short secret
substring-matches ordinary prose and produces a false alarm rather than a
finding. This is the same masked-value debugging problem that T21's OIDC failure
ran into: a gate that cannot say what it found costs more than it saves.

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

## A12 · The player map is stored, because instance memory is not storage

The first deployed request returned 500, six times over:

```
System.InvalidOperationException: The player map has not been refreshed.
   at FantasyLeague.Sleeper.SeasonService.BuildAsync
```

Nothing in the Functions host called `RefreshAsync`. The console harness called
it explicitly on line 29 of its `Program.cs`, which is why every local run
worked and the deployed one never could.

**The fix is not a startup hook.** `PlayerMapRefresher` held the map in a
`volatile` field — per instance, in memory. Flex Consumption scales to zero and
out to forty, so a startup hook or a timer trigger populates the instance that
ran it and no other. `HybridCache` does not help either: with no
`IDistributedCache` registered it is L1-only, so it is per-instance too.

And the obvious workaround is the one T14 forbids in as many words: *"This call
must never sit on a request path. A cold Function serving its first request
should read the slim map from storage, not go and fetch five megabytes while a
viewer waits."* The storage half was designed and never built.

**What exists now:**

- `IPlayerMapStore`, with a blob implementation over the storage account the
  Function App already has, and a null implementation for hosts with no
  connection string. The console runs on the null one and fetches every run,
  exactly as before.
- `IPlayerMapRefresher.EnsureCurrentAsync` — safe on a request path. It adopts
  a stored map inside `PlayerMapOptions.MaxAge` (24 h), and only falls back to
  the dump when the store is empty or stale. `RefreshAsync` still always
  refetches; that is what the timer calls.
- A `SemaphoreSlim` so a cold-start stampede fetches once, not once per caller.
- `RefreshPlayerMap`, a daily timer trigger, deliberately **not**
  `RunOnStartup` — that would refetch on every cold instance, which is the cost
  the store exists to remove.
- `SeasonService.BuildAsync` calls `EnsureCurrentAsync` before its A8 guard, so
  the guard now means "the store and Sleeper both failed" rather than "nobody
  called the refresher".

**Store failures are warnings, never throws.** An unreachable store — no
Azurite locally, a transport fault in Azure — makes a request slower, not
failed. The catch is deliberately broad for that reason, excluding only
cancellation.

**No bicep change.** The container is created on demand, and
`AzureWebJobsStorage` is already an App Setting.

---

## A13 · The league does not use commissioner overrides, and only the actual is stored

Settled 2026-09-14. `custom_points` is a scoring override the commissioner can
set on any roster's week. **This league treats using it as cheating**, so it will
not be set.

**What that changes: nothing in the reading code.** `SleeperMapper.ToRosterWeek`
still maps `custom_points ?? points`, `RosterWeek` still carries both, and both
invariants still compare against `points` (A7, A9). A policy that something will
not happen is not a guarantee that it cannot — the payload can still carry the
field, and if it ever does, the existing invariant check reports it as a
violation rather than absorbing it silently. Removing that machinery would trade
a working tripwire for a saved column.

**What it does change: the schema.** `WeekScore` stores `ActualPts` and not
Sleeper's raw `points` as a separate column, matching §08 as written. With no
overrides in play the two values are identical, so a second column would hold a
duplicate of the first in every row this league will ever produce.

**The cost, stated plainly.** Invariants can be checked when a week is assembled
but **not re-checked from stored rows afterwards**. If an override ever did
appear, the violation would be logged at the time and the stored row would carry
the overridden figure with no record of what Sleeper actually scored. That is an
acceptable trade for a league that has decided not to use the feature; it would
not be for one that does.

---

## A14 · The stored key leads on Season

Settled 2026-09-14. §08 specified `PK (LeagueId, Season, Week, RosterId)`; the
implementation uses `PK (Season, LeagueId, RosterId, Week)` and the design has
been corrected to match.

A composite primary key is the clustered index, so its leading column decides
what can be sought rather than scanned. Every read this site performs is scoped
to one season: T27 hydrates with `WHERE Season = @s AND IsFinal`, and the API's
only parameter is a year. `LeagueId` leads correctly for a multi-league service
— this one serves exactly one league per year, which makes that column
near-constant and close to useless as a leading key.

---

## A15 · Two Microsoft natives are exempt from the licence allowlist

2026-09-14. Adding EF Core pulled two packages into the graph that carry
**proprietary Microsoft Software License Terms** as a licence *file* rather than
an SPDX expression:

| Package | Reached via |
|---|---|
| `Microsoft.Data.SqlClient.SNI.runtime` | `Microsoft.Data.SqlClient` |
| `Microsoft.Identity.Client.NativeInterop` | `Azure.Identity` → `Microsoft.Identity.Client.Broker` |

`nuget-license` reports the filename (`LICENSE.txt`, `LICENSE`) as the licence,
and their `licenseUrl` is NuGet's `aka.ms/deprecateLicenseUrl` placeholder, so
neither the allowlist nor the URL mapping can resolve them. **The gate was
right**: these are not MIT, Apache-2.0 or BSD-3-Clause, and it caught them the
first time they appeared.

**Why they are allowed anyway.** Both are Microsoft first-party, both arrive
transitively as a consequence of connecting to Azure SQL with a managed identity
(T26), and neither can be removed without giving that up. Their terms permit use
and redistribution with an application, which is the only thing this project
does with them. Neither is redistributed by the repository itself.

**Both are also Windows-only in effect.** `SNI.runtime` is the native SQL Server
Network Interface library used on Windows, and `NativeInterop` backs the Windows
broker authentication path. The Function App runs Linux, so neither is loaded at
runtime there — they are in the graph, not on the hot path.

**Recorded as an ignore, not a widened allowlist.** Adding `LICENSE.txt` to the
allowed set would admit *any* package that ships a licence file, whatever it
says, which would gut the gate. The exception names the two packages and no
others. It ignores by **package name rather than version**, so a routine bump
does not silently re-break CI — and a genuinely new file-licensed package still
fails, which is the behaviour worth keeping.

---

## A16 · Finalized weeks will be mirrored to blob, for the same reason the player map is (decided, not yet built)

2026-09-18. The SQL free tier's auto-pause (**D7**) is the site's one disruptive
cold start: a paused database takes 30–60 seconds to resume, and whoever's first
after 60 idle minutes eats it in full. Three alternatives were weighed before
landing here.

**Cache harder — rejected, doesn't touch the cause.** `CachedFinalizedWeekStore`
already wraps SQL in a 6-hour `HybridCache`, and it was tempting to lean on that
further. Two problems. First, `HybridCache` has no `IDistributedCache`
registered, so it is L1-only — per instance, exactly A12's failure mode. A cold
Function instance starts with an empty cache regardless of TTL. Second, and
counter-intuitively, a *more effective* cache makes this worse, not better:
Azure SQL serverless pauses on its own idle timer, counted from the last query
it actually saw. A cache that successfully absorbs reads for six hours is six
hours the database looks idle from the inside — it pushes toward the pause
firing sooner, not later.

**Drop SQL for blob or table storage entirely — rejected for now, too big a
swing.** `WeekScore` (§08) is genuinely key-value shaped — no joins, PK lookup
only (**A14**) — and blob storage never pauses, so this would remove the cold
start outright. Deferred anyway: it means retiring `SqlFinalizedWeekStore`, the
EF Core migration, and the SQL bicep resource, which is a real rewrite, not a
config change, for a problem that has a smaller fix available first.

**A scheduled keep-warm ping — rejected, doesn't fit how SQL serverless
works.** Auto-pause/resume is purely reactive (resumes on connection, pauses
after N idle minutes); there is no notion of "stay up 1pm–11pm Sundays" to
configure. The only way to simulate that is an active timer pinging the
database through a chosen window, which trades disruption for cost on a dial —
every warm hour spends against the free tier's **100k vCore-seconds/month**
(≈55 hours at 0.5 vCore) — and still leaves every visitor outside the window
exposed to the exact same wait.

**Chosen: mirror A12's pattern.** `FinalizeWeek` (the Tuesday-noon timer, the
only writer `WeekScore` has) writes the season's finalized weeks to SQL *and* to
one JSON blob in the storage account the Function App already has, replacing it
wholesale — same shape as `IPlayerMapStore`. The read path serves from that
blob first and stops touching SQL on every request. This works here for a
reason that doesn't generalize to every SQL-cold-start problem: **once this
ships, nothing on the request path calls SQL at all.** The only remaining SQL
traffic is the weekly finalize timer, a background job nobody is waiting on
synchronously, so its own 30–60 second resume becomes invisible rather than
disruptive.

**What this does not fix.** A cold or missing blob — first deploy, or the blob
deleted out of band — still falls through to SQL and still pays the full resume
cost. That path is expected to be rare to never post-ship rather than the
routine case it is today, but it is not eliminated.

**Not yet built.** Tracked as **T58**.

### Implementation settled, 2026-09-20

Three details sharpened once T58 was picked up, all in
[`field-manual.md`](field-manual.md):

- **Scope is one blob, not one per season.** `FinalizeWeek` only ever builds
  the current season, so that is the only season this task ever mirrors — which
  is also the only season the live site's polling actually requests. Older
  seasons (once T29/T30 produce them) stay SQL-only and keep paying the cold
  start on the rare read; that is an acceptable, much smaller version of the
  same trade the design already made, not a new one.
- **The open "block and wait, or serve stale" question resolves by default,
  not by design.** A cold or missing blob falls through to
  `SqlFinalizedWeekStore.ReadAsync` — which is exactly every request's
  behaviour today, with nothing built. Block-and-wait needs no new code because
  it already exists; there was never a decision to make here, only a fallback
  to keep.
- **The cache invalidation this task needs already exists.** `CachedFinalizedWeekStore`
  calls `RemoveByTagAsync` after every `WriteAsync`, regardless of what it
  wraps. Pointing it at a store that writes SQL *and* blob, instead of SQL
  alone, makes "invalidate on the Tuesday write" free — it was already
  invalidating on every write, for a cache that mattered less before there was
  a blob copy to keep in step.

One new constraint this surfaced: `FinalizeWeek`'s Tuesday timer should not run
during the NFL offseason, both to stop spending the SQL free tier's
vCore-seconds on a wake nothing needs and to stop writing a blob that would
just be overwritten unchanged the next active Tuesday. Expressed as a cron
month restriction rather than an in-function guard, so the offseason costs
zero invocations rather than one short one. The boundary is **March–August**,
not March–September as first framed: the season opener usually falls inside
September (2026's is 2026-09-10), and excluding all of September from the
cron's active months would have skipped week 1's own finalize run.

(T58's step-by-step has since been cut from `field-manual.md`, per that
document's own stated convention of removing what has shipped — this section
is now where that reasoning lives.)

### The blob only had one writer, and that was the bug — fixed 2026-09-21

Reported after ship: the cold start A16 was meant to remove was still
happening. Cause was structural, not a typo — `BlobFirstFinalizedWeekStore`
only ever *wrote* the blob from `FinalizeWeek`'s Tuesday timer. Between a
deploy and the next Tuesday, every request's `ReadAsync` found the blob empty,
fell through to SQL exactly as designed, and then stopped — nothing about a
read ever primed the blob, so every request paid the cold-start cost until
Tuesday arrived, which is the routine case this task exists to remove.

**`ReadAsync` now writes what it fell back to**, under the same
`SanityCheckSeasonForBlob` gate the write path already used: a cold blob for
the season in progress is populated from the SQL answer before it's returned,
so the second request onward is a blob hit. A cold blob for a season outside
that window (an old season, or the same season read from off-season code)
still falls through without being cached — there is no "in-season Tuesday
write" to mirror for it, so caching it would mean inventing a staleness
story this task never needed. The result: the blob now self-heals from the
first live request after a deploy, instead of sitting empty until the next
scheduled write.

---

## A17 · σ leaves the standings table for a panel of its own

2026-10-07. The first week σ had real data behind it (week 4, after **D1**'s
four-week minimum), the column went live and looked badly out of place.

The design asked for the σ figure *plus* a small inline range mark (§04,
"Showing σ in a table cell"): a rule from the team's lowest to highest week,
a tick at the mean, a band at ±σ, on one scale shared by every row. What
shipped was the mark without the number. In a ~140px cell, beside four
right-aligned figures, the mark read as decoration rather than data, and with
no figure next to it there was nothing to read precisely either.

**Showing just the number was considered and not taken.** It would fit the
table, and σ is the figure people say out loud (**D3**). But the mark is the
part that carries information a number cannot: floor, ceiling and spread,
comparable across teams at a glance. Rather than throw that away or keep it
cramped, σ comes out of the table entirely and gets a panel (**T59**) where
the range mark has room to be legible and the number can sit beside it.

**What changes:** the standings table is Team, Points, Gap and Ideal.
`SigmaDisplay` and its tests are kept, unused, for T59 to build on. σ and CV
are still on the chart (T42), so the figure has not gone from the site.

**D3 is affected, not reversed.** It chose σ over CV for the place people
look first. That place is now T59's panel rather than a table column.

## A18 · A licence shipped as a file is declared, not exempted

2026-10-08. Azure.Storage.Blobs 12.30.0 (Dependabot, #61) brought in
**Apache.Arrow 22.1.0** as a new transitive dependency. Its nuspec gives the
licence as a file, `LICENSE.txt`, not an SPDX expression, so `nuget-license`
reports "Unable to determine license" and the `dependencies` job fails.

The file was read rather than assumed: it is the Apache License 2.0 text, plus
a note that the package bundles FlatBuffers code, also under Apache-2.0.
Apache-2.0 is already on the allowed list, so there is nothing to exempt, only
something the tool cannot see for itself.

**So it is declared, not ignored.** A15's Microsoft natives are ignored by name
because their licence is proprietary and could never be on the list.
Apache.Arrow is the opposite: an allowed licence the tool cannot read. It goes
in `.github/license-overrides.json`, passed with `-override`, which the tool
documents for exactly this case.

**Pinned to a version on purpose.** Unlike A15's ignores, an override names
the version. When Dependabot next bumps Apache.Arrow, the job fails again until
someone has read that version's licence file and updated the entry. That is a
small cost for not waving through a licence change unread.

## A19 · Three of the four tables are dropped

2026-10-12. The T61 review found `League`, `Player` and `Team` defined in
`FantasyDataContext`, created by the M3 migration, and never read or written.
§08 still listed all four tables as if each held data.

**`Player` was overtaken by A12.** The design gave the projected player map a
table. What shipped keeps it in blob storage, because every cold instance has
to load it before serving and blob storage never pauses. A table copy would be
a second, slower source for the same map.

**`Team` was overtaken by the request path.** Team names and owners are read
from Sleeper's users and rosters, cached for an hour and five minutes, because
names change whenever a manager feels like it. A1's exclusion matches on
`user_id` from that same live data. §08 kept `OwnerUserId` to spare a future
cross-season view a backfill, but there is nothing to backfill: Sleeper serves
`user_id` with every season's users for as long as the chain exists.

**`League` is not wrong, only early.** D8's season-chain walk upserts one row
per season, and that walk is T29, waiting on August 2027. An empty table for a
year is a schema whose shape nobody has tested. T29 creates it with its own
migration, alongside the `Core` record and `SeasonOverrides` it also needs.

**The same rule removes the completed-season branch in `GetSeason`.** It sets
a year-long `immutable` cache header, but every request for a season other
than the current one returns 404 before reaching it, because nothing serves
prior seasons yet. T29 adds it back with data behind it.

**What changes:** §08 shows `WeekScore`, with `League` marked as arriving with
T29. One migration drops the three tables.

## A20 · The blob caches the current season; SQL keeps every season

2026-10-12. The T61 review asked whether SQL should go entirely, since A16's
blob copy already serves the request path and a lost blob can be rebuilt from
Sleeper. Decided: **no. SQL stays as the record of every season, and the blob
holds exactly one season, the current one.**

**This is A16's scope, restated as a rule.** A16 already said older seasons
stay SQL-only. What the code did not have was a way to enforce it. It gated
blob writes on a calendar window (September to March), which guesses at the
season from today's date, and per-season blob names would let a past season
land in the blob anyway.

**The rule the code enforces instead:** one blob per league, with the season
stamped inside it. A read is a hit only when the stored season is the one
asked for. A write or a read-through replaces the blob only with the same or a
newer season, so a backfill of an older one can never evict the live one.
Every write still goes to SQL, so a season is archived the moment its last week
finalizes, with no separate job.

**Cost is not what decides it.** Both options are $0.00. A season is a few
hundred kilobytes, and blob storage for all of them would cost a fraction of a
cent a year. SQL's weekly wake for `FinalizeWeek` uses a few thousand of the
free tier's 100k vCore-seconds a month.

**What decides it:** a past season is immutable and read rarely, so it can
afford SQL's 30 to 60 second resume, and its response is cached for a year
(§09). The current season is polled every 30 seconds and cannot. SQL also
makes cross-season questions, such as an all-time high week, a query rather
than a walk over every blob.

**The known weakness.** `WeekScore` keeps most of each week as JSON columns, so
the table holds documents rather than relations. That is right for a store
read whole by key (A14). It is also the first thing a reviewer reading the
schema would question, and a cross-season feature is what would justify
promoting any of it to real columns.

## A21 · Hosts compose, Data owns storage, policy is applied once

2026-10-12. Three decisions from the T61 review (items 11 to 16), each
replacing something that worked only by accident or by repetition.

**Hosts compose; no library registers a fallback for another's interface.**
`AddSleeperClient` used to register the client, the season service, the
player map store and a null finalized-week store with `TryAdd`, so the
Functions host worked only because it called `AddFantasyData` first. Now
`AddSleeperClient` registers the client and its pipeline, `AddSeasonService`
registers the season assembly and the player map, and `AddFantasyData`
registers both stores. The console registers the two null stores itself. A
host that forgets a store fails `ValidateOnBuild` at startup, not at the
first request.

**Core owns the store interfaces; Data owns every implementation.**
`PlayerRef`, `StoredPlayerMap` and `IPlayerMapStore` moved to Core beside
`IFinalizedWeekStore`, which already followed this pattern. Both blob stores
now sit in Data and share one set of read, write and transient-failure rules
and one storage configuration section (`BlobStorage`). Sleeper no longer
references Azure Storage at all. Container and blob names are unchanged, so
the stored data carries over.

**League policy is applied when the season is built, and the ETag hashes
the payload.** The name override and the excluded rosters (A1) were applied
by each caller, and the ETag was built from a hand-kept list of fields that
left both out, so changing either served a stale 304. `SeasonService` now
applies both, once per build, and logs an unmatched owner once per build
rather than once per request. The ETag is a hash of the response body minus
`asOfUtc`, so anything the page can see changes the tag. `BuildAsync` builds
the configured league and takes no league id, since the override belongs to
that league alone. T29 adds a per-season variant when there is one.

## A22 · The Sleeper project splits in two, and names settle

2026-10-12. `FantasyLeague.Sleeper` held the Sleeper client and its DTOs, and
also the application layer built on them: the season service, the player map
refresher, the payload and the ETag. Its name undersold it, and the season
service read wire DTOs directly.

**Split along that line.** `FantasyLeague.Sleeper` keeps the client, the rate
limit, the DTOs and the mapper, behind one public `ISleeperReader` that
returns domain shapes: the NFL state, the league with its slots and teams,
mapped roster-weeks and the projected player map. The DTOs stay internal to
it. `FantasyLeague.Seasons` holds everything built on that reader. References
run Core ← Sleeper ← Seasons ← hosts, with Data beside Sleeper.

**Names that changed with it.**
- Domain point totals end in `Pts`, matching the `WeekScore` columns:
  `TeamWeek.ActualPts` and `IdealPts`, and `RosterWeek.ActualPts` beside
  `ScoredPts`, Sleeper's own total before any override. The §09 payload keeps
  `actual` and `ideal`, since it is the contract the page reads.
- The current-season blob's name carries a schema number, so renaming a
  `TeamWeek` field turns an old blob into a miss rather than a read of zeros.
- The console harness is `FantasyLeague.Cli`, which also ends its namespace's
  clash with `System.Console`, and its column for σ is headed `SPREAD`.
- `NFL` stays capitalised in identifiers, cancellation tokens are always
  `cancellationToken`, and one concept goes in a file named after it. All
  three are now in the standards.

## A23 · Two C# standards narrowed to what they were for

2026-10-12. A final check of the code against the C# standards found two rules
that, read literally, asked for changes that would make the code worse. Both
were narrowed, and approved, rather than followed to the letter.

**`Pts` covers roster point totals, not every number of points.** The rule
came from naming `TeamWeek.ActualPts` and `IdealPts` after the `WeekScore`
columns, and `RosterWeek.ScoredPts` beside them. Written as "point totals", it
also reached `RosteredPlayer.Points`, one player's score in one week, where a
bare `Pts` reads worse than the word it shortens. It now names roster totals
only. The solver's result was renamed `IdealPts` under it, since that is a
roster total.

**Tests may use `!` without a comment.** Production code has none, and every
one there needs a reason. Tests had 45, almost all straight after a
`NotBeNull()` assertion or on a fixture the test controls. There, a wrong null
fails the very test that relies on it, which is the protection the comment
exists to provide; 45 comments would restate the assertion above each one. It
is recorded under the standards' accepted exceptions.

**The same check fixed four things in the code.** An unmatched excluded owner
was logged by its Sleeper user id, which the logging rule and A2 forbid; it now
logs a count. Build and refresh durations were timed with `Stopwatch` rather
than the injected `TimeProvider`. `SleeperReader` built two domain records
itself rather than through `SleeperMapper`. `SlotAverager` lacked a summary.

## A24 · The front end meets its standards

2026-10-12. The domain 2 check found the front end close to its standards but
short on a few that matter, and four decisions were made along the way.

**What was out of line.** `noUncheckedIndexedAccess` was off, and turning it on
surfaced nine unguarded indexes. Six `!` assertions had no reason given. The
ranking, the gap to the leader, the radar ratios and the bar deviations were
computed inside components rather than in `pointCalculation.ts`. The standings
rows still slid with reduced motion on, because Motion's `layout` animation
ignores the preference unless `MotionConfig` says otherwise, and the radar's
crossfade ignored it too. The line chart's label said what was drawn but no
figure from it. And `?season` was never read, so the 404 page's season links
went nowhere.

**Decisions.**
- **Tests may use `!` without a comment,** extending A23 to TypeScript for the
  same reason: a wrong null fails the test that relies on it.
- **Prettier formats the front end,** with 2-space indents, single quotes,
  semicolons and a 100-character line, which is what most files already used.
  Three files had drifted to a different style.
- **The 125 KB budget is enforced in CI,** by summing the gzipped size of every
  script `index.html` loads before first paint. It stood at 112 KB, close
  enough that one dependency could breach it unnoticed.
- **The tab is titled with the league's name** once the season loads, and
  "Fantasy Standings" until then.
- **A narrow chart drops its line-end names.** Checking 375 px in a browser
  showed them colliding where totals are close and clipping at the edge. Below
  a 600 px chart the team chips above it are the legend, and the plot takes the
  freed margin. The spoken label still names every team.

**A consequence of F1.** The CV can now be null while sigma is not, at a zero
mean, so `ConsistentTotals` no longer promises a CV.

## A25 · A team keeps its colour, and polling knows the whole NFL week

2026-10-12. Two decisions from the front end's high-level review.

**Colour follows the team, not its rank.** Each panel coloured teams by
position: the chart by its own ranking, the radar by the payload's order. They
agreed only because the server sends teams ranked, so a tie could give one team
two colours, and every team changed colour when it was overtaken. The design
says teams are encoded by colour (§10), which only works if a team's colour is
stable. Colours are now assigned once, in roster id order, by one helper both
panels use.

**The game windows cover the games the NFL actually plays.** D6 gates polling
on game windows, and the rule knew Thursday, Sunday and Monday nights and
Thanksgiving. It missed the London and Europe games at 09:30 Eastern on
Sundays, December and January Saturdays, Christmas on whatever weekday it
falls, Black Friday's afternoon game, and Friday night openers abroad. It also
treated every Thursday afternoon in November as Thanksgiving, and on some
engines read midnight as hour 24. It now covers all of them, Thanksgiving is
the fourth Thursday by date, and the rule has its own tests: 24 Eastern-time
moments either side of each window.

## A26 · Code waits for its task, and a failure is a panel

2026-10-12. Three decisions from the rest of the front end's high-level review.

**Code nothing calls is removed, and its task recreates it.** The same rule
A19 applied to the unused tables. `fetchSeasons` waited for T29's season chain,
and `SigmaDisplay` with `ConsistentTotals` and `hasConsistency` waited for
T59's consistency panel. Both tasks now say what to rebuild, and the git
history keeps the old versions. Two CSS rules that matched nothing went too.

**The `?radar` switch is gone.** It forced the radar panel open with no slot
data, a debugging aid in production code that read the URL behind
`useQueryState`'s back. The panel shows when the data has something to plot;
the tests render it directly with hand-written data.

**A failure is laid out like every other state.** The first load's failure
rendered outside the page's panels, while the masthead also said "Error
loading", so the failure was announced twice and looked out of place. It is now
a panel, as the 404 page already was, and the masthead says nothing until there
is data.

## A27 · Data and integrations meet their standards

2026-10-13. Decisions from the domain 3 review (T61 3, items 4–9).

**The stored-season blob carries its write time, and the reader acts on it.**
A blob written before the latest scheduled `FinalizeWeek` run is a miss, so
the reader goes to SQL and rewrites the blob. That is the failure it exists
for: Tuesday's job reaches SQL, its blob write fails, and without the check the
blob hides the new week until the following Tuesday. The schedule lives in
`FinalizeSchedule`, which the timer reads its expression from, so the two
cannot drift. In the offseason the last run is February's, so a blob stays
current until September. A fixed maximum age was the alternative; it left a
failed Tuesday stale for days. The blob moved to `current.v3.json`; v2 is
orphaned. The C# review had removed the old `FinalizedUtc` because nothing
read it; this one is read on every miss.

**The store refuses an unfinished week.** `SqlFinalizedWeekStore.WriteAsync`
throws `ArgumentException` and writes nothing, rather than relying on
`FinalizeWeek`'s filter. A stored week is served as settled, so a live one
would freeze mid-game. The `IsFinal` column and its read filter stay as a
backstop; dropping them would need a migration for no behaviour.

**`SeasonService` drops the placeholder team.** The snapshot never contains
it, so the payload, the CLI and the stored rows cannot count it, and
`SeasonSnapshot.ExcludedRosterIds` is gone. Both invariants and the
unresolved-player report still run over it first: a solver bug shows on its
lineup as well as any other. Rows it already has in SQL are dropped on read.

**HTTP cache lifetimes have reasons.** `/api/season` stays at `max-age=5` on
purpose, to stay close to live during games; the 5 s season cache behind it
makes a request inside the window free. `/api/seasons` moves from 300 s to a
day, because a new season appears once a year.

## A28 · The league ID leaves blob names, the bucket halves, a column is renamed

2026-10-13. The last three items of the domain 3 review (T61 3, items 11–13).

**Blob names carry a hash of the league, not its ID.** Blob names show in the
Azure portal and in storage logs, and A2 makes the ID a secret. The season blob
is now `{first 16 hex of SHA-256(league)}/current.v3.json`; the blob under the
plain ID is orphaned. A hash still keeps two leagues apart. It hides the ID
from a casual reader, not from someone who already suspects which league it
is, and that is all it is for. Cache keys keep the plain ID, because they never
leave memory; so does SQL's `LeagueId` column, because the database is read only
by the app's own identity and hashing it would mean a migration that rewrites
every row and a database that is harder to debug.

**The Sleeper token bucket is 25 a minute per instance, not 50.** The bucket is a
singleton per instance, not per app, and Flex Consumption cannot be capped below
40 instances, so 50 allowed 2,000 a minute in the worst case against Sleeper's
guidance of 1,000. At 25 the worst case is 1,000. Normal traffic is about 4.4 a
minute and 8.4 before Tuesday's job, so nothing ordinary comes near it. The cost
is the outage case in §07: with both stores failing, refusals now start around
week 7 rather than week 13. A limiter shared across instances (blob leases or
Redis) was the alternative: a true limit, for a network call on every request.

**`WeekScore.PlayersPointsJson` is `PlayersJson`.** It holds each player's ID,
name, positions and points, not a map of points. Migration `RenamePlayersJson`
renames the column in place and keeps its data. The `migrate` job runs before the
new code deploys, so for those few minutes the old code reads a column that is
gone. A read that misses the blob and the memory cache fails with a 500 until
the deploy finishes. Expanding and contracting across two deploys would avoid
that; the window was judged too short to be worth it.

## A29 · Stored weeks keep their lineups, in one shape

2026-10-13. Two decisions from the domain 3 high-level review (T61 3b, items
12 and 13).

**Every stored week keeps its starters, ideal lineup and players.** Nothing
reads them back today, and A26's rule would remove them. They stay because §08
asks for them: they are what lets a solver fix recompute a finished season
without asking Sleeper for it again. That makes them the one exception to A26
in the store, and it is recorded so the next review does not raise it again.

**The blob and the SQL row store a week the same way.** A player id is written
as a bare string everywhere, through a converter on `PlayerId`, and a player is
`RosteredPlayer` as it is, so `StoredPlayer` and six JSON helpers are gone. The
existing rows said `Positions` where `RosteredPlayer` says `FantasyPositions`;
migration `RenamePlayersJson`, not yet deployed, rewrites that key as it renames
the column, and its `Down` puts it back. The blob moved to schema v4.

## A30 · Infrastructure meets its standards: no stored keys, a fifty-cent alert

2026-10-13. Decisions from the domain 4 review (T61 4, items 1–11).

**No credential is stored anywhere.** The Static Web Apps upload token was the
last GitHub secret that was a credential rather than an identifier. The `web`
job now signs in through OIDC like the others and reads the token for that run,
masked. The storage account's keys went too: the Function App reaches storage,
for the host, the deployment package and its own blobs, through its system
identity, and the account has shared keys turned off. That needs three role
assignments, and Contributor cannot make them, so the CI principal also holds
Role Based Access Control Administrator, with a condition that lets it assign
only those three storage roles. A broader grant would let a compromised pipeline
make itself Owner. The roles stay in the Bicep rather than in a one-off command,
so a rebuilt environment gets them without anyone remembering.

**The budget alert fires at fifty cents, and the steady state is about a
quarter.** September's bill was fifteen cents: storage, which has no free grant
and which the Functions host uses continuously. A standard that said "costs
nothing" was false, and an alert at one cent would fire every month and be
ignored. The budget stays at $1 with its actual-spend alert at 50%. The budget's
start date is still the one value an Azure resource refuses to change, so the
deploy reads the existing budget's date and fails loudly if it cannot, rather
than falling back to the current month as it did in October.

**One alert on the app's own failures, the cheap kind.** A metric alert on the
exception count costs about ten cents a month and catches a failed timer job or
a request that 500s. A log alert would also catch invariant breaches, which are
logged rather than thrown, for about fifty cents, which on its own would pass
the budget alert every month. Breaches stay in App Insights to be looked for.

**Accepted: SQL allows connections from any Azure service.** Flex Consumption has
no fixed outbound address to allow instead, and a private endpoint costs about
$7 a month. Entra-only sign-in is the control; reaching the server is not
signing in to it.

## A31 · The budget leaves the pipeline; the cost script and firewall dance go

2026-10-13. Decisions from the domain 4 high-level review (T61 4b, items 6, 7,
11 and 15).

**The budget is deployed once, by hand.** It was the one resource the pipeline
could not simply redeploy: its start date cannot change after creation, which
cost a required parameter, a lookup duplicated across `deploy.yml` and `ci.yml`,
and the October outage that A30 was still guarding against. It now lives in
`infra/budget.bicep`, which the runbook deploys once with this month's date, and
again with the original date only to change it. The trade-off is that a budget
deleted by hand is not put back by the next deploy. The deploy still requires
the alert email, which the exception alert uses.

**`scripts/verify-cost.sh` is removed.** It read back the telemetry cap, SQL's
free limit, the budget and the exception alert, all of which the template sets
on every deploy and the CI what-if shows as a diff if they drift. It was 80
lines of script for a check the pipeline already makes.

**The migrate job opens no firewall rule.** A throwaway workflow, run twice
from a GitHub-hosted runner with no rule for its address, reached the database
through the "allow Azure services" rule alone: the runners are in Azure. The
open and close steps, the `api.ipify.org` lookup and the `sqlServerName` output
are gone. The first of the two runs failed with a transient error because the
database was paused, which showed that the design-time factory the migrate job
uses had no retry; it now retries as the app does.

**The template no longer restates Azure's defaults.** Soft-delete switched off,
`zoneRedundant: false`, `alwaysReady: []`, public network access on and HTTPS
only are what Azure does anyway. The security settings that are also defaults
today (TLS 1.2, no public blob access) stay, so a change in a default cannot
loosen them.

## A32 · The supply chain covers the web app too

2026-10-13. Decisions from the domain 5 review (T61 5, items 1–8).

**The licence allow list grows to what the web app actually uses.** The NuGet
gate allowed MIT, Apache-2.0 and BSD-3-Clause, and nothing checked npm at all.
The page itself ships ISC (d3) and 0BSD (tslib), both as permissive as MIT, and
the dev tools add BSD-2-Clause and MIT-0. Those join the list for everything.
Three more are allowed for build and test tools only: MPL-2.0 (lightningcss,
axe-core), whose copyleft applies to changes to those files and never reaches
the page; BlueOak-1.0.0 and CC0-1.0, both permissive. Development dependencies
are checked as well as production ones, by choice, so a new licence surfaces
when Dependabot proposes it rather than when it ships. The check is
`scripts/check-npm-licences.mjs`, forty lines over the lockfile, rather than
another npm package to vet.

**npm vulnerabilities fail on production packages only**, matching the NuGet
gate's intent. The one high finding at the time, `source-map-js` in the build
tooling, was fixed rather than excepted.

**The API names its build.** Every season response carries `X-Build`, the
commit it was built from, and the deploy's smoke test waits for this run's
commit. The previous build answering the old check was a pass that proved
nothing.

**The web deploy stays parallel with migrations and functions.** Ordering it
after functions would stop a new page briefly calling an old API, at the cost of
a slower deploy; with the page and the API changed together this rarely, it is
not worth it.

## Corrections applied to the design

**All of these are already applied** to `design.md`, each marked **Corrected**
where it appears. Kept here for the reasoning, and because several are mistakes
that are easy to make again.

- **§03 and §13 understated the player dump by about 3×.** Both quoted ≈5 MB.
  Measured against the live endpoint on 2026-09-13: **14,654,498 bytes across
  12,227 players** — which is what `SleeperPlayerDto` and `PlayerRef` had said in
  their own doc comments all along. The cost conclusion survives (14 MB once a
  day is still under a penny); the figure matters for **memory and timeout
  sizing**, which is where a 3× error actually bites.
- **§03 said "cache that", and caching alone was not enough.** The map is
  per-instance memory under a plan that scales to zero. See **A12** — this is the
  one design gap that reached production.
- **§03 was wrong about team names** — an unset name is an absent key, not an
  empty string. See **A6**.
- **§03's "ideal >= actual, always, with no exceptions" does not hold** once a
  commissioner override exists. Both invariants now compare against Sleeper's
  scored `points`. See **A7** and **A9**.
- **§06's D5 chose a Consumption (Y1) plan that cannot be provisioned** — the
  subscription quota for Y1 VMs is zero. Flex Consumption (FC1) is what exists.
  Every argument D5 makes still holds; only the SKU name was wrong.
- **§07's TTLs and its "≤ 2.3 req/min" headline do not match what was built.**
  Both columns are now shown in the design. The built TTLs stand, at about 4.4
  req/min (F4, closed 2026-10-13); the table was itself stale until then, still
  showing 12.2 after the C# review had changed three of the TTLs. §07 now also
  says what it leaves out: the figures are per instance, a finished week costs
  the live week's traffic again until Tuesday's job stores it, and a store that
  fails to read sends every finished week to Sleeper while the outage lasts.
- **§13's cost table was wrong in three ways**: the Functions row named the wrong
  plan, the storage account was missing entirely, and the App Insights ingest cap
  of "1 GB/day" permits **6× the free monthly allocation** — 31 GB against 5.
  The deployed cap is 0.16 GB/day.
- **§08, §10 and D11 assumed twelve teams.** The league has **six rosters, five
  of them scoring** (A1). §08's "roughly 12 teams × 18 weeks is 216 rows" becomes
  ~108. More substantively the legibility arguments soften: five lines are
  readable without hover-to-isolate, and D11's cap of two teams on the radar is
  more conservative than five teams needs. The decisions are still right; the
  reasoning behind them was stronger than the facts warranted.
- **§14's open questions were five-eighths answered** on 2026-09-10, and **§15's
  build order is now partly history** — M0–M2 are built. Both sections say so.

---

- **§08 listed four tables, and only `WeekScore` was ever used.** `Player`
  became the blob player map, `Team` is read from Sleeper, and `League` waits
  for T29. See **A19**.
- **D7 said there was no blob layer, and §07 and §13 said SQL was not built.**
  Both were overtaken by M3 and A16: SQL is provisioned, and the request path
  reads a blob copy first.
- **§04 put σ in the standings table, with an inline range mark.** At table
  size the mark was illegible, and it shipped without the figure it was meant
  to accompany. σ is out of the table pending its own panel. See **A17**.

## Corrections applied to the manual

Kept as a record of what was wrong in the published artifact and why. **All of
these are already applied** to `field-manual.md`, which is now edited directly —
there is nothing to re-apply. Several cover M0–M2 tasks whose step-by-step has
since been compacted away; the reasoning is retained here because the same
mistakes are easy to repeat.

- **T14** — "returns roughly 5 MB" and the `// ~5 MB` comment are both wrong.
  The dump measured 14.65 MB over 12,227 players on 2026-09-13. The watch-out
  beneath it is right, and its "five megabytes" should read fourteen.
- **T14** — the projection drops fields, not players: 11,902 of 12,227 entries
  carry `fantasy_positions` and are kept (A3 keeps the map general rather than
  pruned to one season). "Immediately throw most of it away" describes the byte
  count, not the row count — the stored map is 840 KB, 5.7% of the dump.
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
