# Ideal Points — Field Manual

> **This is now the living document.** It began as a conversion of the published
> artifact, but M0–M2 have been built and a dozen of its instructions have been
> corrected against reality. A re-sync from the artifact would be destructive.
> Edit this file directly; `docs/amendments.md` still holds the *reasoning*
> behind every decision made after design v6.
>
> Updated 2026-09-13. Task IDs match [`tasks/todo.md`](../tasks/todo.md).

---

This manual assumes no prior familiarity with Azure, bicep, EF Core, D3 or
GitHub Actions. Where a step needs background, the background is here rather
than assumed. Where a command can fail in a way whose error message points
somewhere unhelpful, that is called out.

### Conventions

- Shell blocks are **bash**. On Windows use **Git Bash**, not `cmd` or
  PowerShell — `find`, `grep` and `$VAR` all mean different things there.
- `rg-fantsybosty` is the resource group. The design and the original artifact
  say `rg-fantsybosty`; that name was never used. Resource *names* inside it
  (`func-idealpoints`, `stapp-idealpoints`) do use the `idealpoints` prefix.
- `±` marks a soft estimate that could run double.
- Anything marked **Watch out** has already cost someone a day.

### What you need installed

| Tool | Why | Check it works |
|---|---|---|
| .NET SDK 10 | Builds everything. Projects target `net9.0`; SDK 10 builds that fine | `dotnet --list-sdks` |
| .NET 9 runtime | `dotnet test` runs on it | `dotnet --list-runtimes` |
| Azure CLI | Every `az` command | `az account show` |
| GitHub CLI | Secrets and workflow runs | `gh auth status` |
| Python 3 | `scripts/capture_fixtures.py`, and JSON on the command line since there is no `jq` | `python --version` |
| Node 20+ | Not needed until T32 | `node --version` |

You also need an Azure subscription and this GitHub repository.

**Find your league ID** by opening the league on the Sleeper web app — the URL is
`sleeper.com/leagues/<league_id>/…` and the numeric segment is what every API
call needs. It is a **secret** (A2): it never goes in the repo.

### Reference roots

Links point at documentation roots rather than deep pages, because deep links
rot faster than the docs.

- **Sleeper** — [read API documentation](https://docs.sleeper.com/)
- **.NET** — [.NET documentation](https://learn.microsoft.com/dotnet/)
- **Azure** — [Azure Functions](https://learn.microsoft.com/azure/azure-functions/)
- **Azure** — [Static Web Apps](https://learn.microsoft.com/azure/static-web-apps/)

---

# Built · M0–M2

These are done. The step-by-step that built them has been removed; what remains
is where things live, the decisions that are easy to undo by accident, and the
traps that still bite.

## The projects

| Project | Holds | Depends on |
|---|---|---|
| `FantasyLeague.Core` | `Slot`, `IdealLineupSolver`, `SpreadCalculator`. Pure functions, no framework types | nothing |
| `FantasyLeague.Sleeper` | DTOs, HTTP client, cache, player map, season assembly, payload mapping | Core |
| `FantasyLeague.Functions` | The two HTTP endpoints and the player-map timer | Sleeper |
| `FantasyLeague.Console` | Live harness that prints standings and asserts invariants | Sleeper |
| `FantasyLeague.Data` | Empty. M3 fills it | — |
| `FantasyLeague.Core.Tests` | All 268 tests | everything |

`Directory.Build.props` sets `net9.0`, `LangVersion=latest`, nullable enabled,
`TreatWarningsAsErrors` and `EnforceCodeStyleInBuild` for every project at once.

## The three gates

Run these before pushing. CI runs the same ones.

```bash
dotnet build -warnaserror
```

```bash
dotnet test
```

```bash
dotnet format --verify-no-changes
```

**Watch out — `dotnet format` and line endings.** `.editorconfig` requires CRLF.
The repo stores LF and `core.autocrlf` converts on checkout, so Windows always
passed while Linux always failed. `.gitattributes` now pins `* text=auto
eol=crlf` with `.github/** text eol=lf`, which makes the checkout identical
everywhere. If you ever see a wall of `ENDOFLINE` errors in CI and none locally,
that file is what to look at.

## The domain (M0)

**`Slot.Parse`** turns `roster_positions` into startable slots. It drops `BN`,
`IR` and `TAXI`, and **preserves order and duplicates** — two `QB` entries mean
two QB slots, and the order is the radar's axis order (R2), so it is never
sorted. `Slot.Accepts` matches against a player's `fantasy_positions`.

This league is `QB, QB, RB, RB, WR, WR, TE, FLEX, SUPER_FLEX, K, DEF` plus eight
`BN` — eleven startable slots, eight distinct codes.

**Watch out — A3 is not finished.** An unrecognised slot code is currently
accepted silently. The test `Parse_rejects_an_unrecognised_slot_code` exists and
is skipped, ready to enable once the guard is written.

**`IdealLineupSolver`** finds the best possible lineup by bitmask dynamic
programming over `2^11` slot states. Think of it as: for every possible *set of
filled slots*, remember the best total achievable. Add one player at a time; each
player can go into any slot that accepts them and is not yet filled.

Two things are load-bearing and were both wrong once:

- The answer is `dp.Max()`, **not** `dp[full]`. A roster short at a position
  cannot fill every slot, and `dp[full]` would be zero for it.
- Reconstructing *which players* were chosen needs a chosen-set bitmask carried
  alongside the score. The original used parent pointers into a rolling array;
  they went stale, so the total was right and the lineup was wrong in about 23%
  of cases. A 20,000-case fuzz against exhaustive search found it.

**`SpreadCalculator`** returns **population** sigma (divide by `n`, not `n-1`) —
these are all the weeks played, not a sample of them — and returns `null` below
four finalized weeks (D1). Null is a value the caller handles, never a zero.

## The Sleeper client (M1)

`AddSleeperClient()` registers everything as one unit so the client cannot exist
without its rate limit. The pipeline order matters:

```
HttpClient → resilience (retries) → rate limiter → network
```

Resilience is **outermost** so every retry spends its own token. Inside-out, a
retry storm would bypass the limiter entirely.

**Watch out — `AddStandardResilienceHandler` forces `HttpClient.Timeout` to
infinite** and overrides any value you configure. That is deliberate: the real
budget is the pipeline's `TotalRequestTimeout` (30 s) with a 10 s
`AttemptTimeout` inside it. Setting `HttpClient.Timeout` yourself would cut the
pipeline off mid-retry and surface as a bare `TaskCanceledException` naming no
cause.

**Watch out — `AllowReadingFromString` is required, not defensive.** Sleeper
returns `season` as the string `"2026"`. Binding that to an `int` throws without
it. All six DTOs share one `SleeperJson.Options` so they cannot drift.

**`SleeperReaderCache`** holds per-endpoint TTLs and gives single-flight
behaviour: ten concurrent callers for the same key produce one upstream fetch.
The TTLs currently differ from §07 of the design — see **F4**.

**`SeasonService.BuildAsync`** assembles a whole season and checks two invariants
per team-week, both against Sleeper's own `points` rather than a commissioner
override (A9):

- `sum(starters_points) == points`, within a cent
- `ideal >= points`

Violations are *collected and reported*, never thrown. Same for players the map
cannot explain (A8) — dropping one silently would lower that team's ideal, and
`ideal >= actual` cannot see undercounting.

## The player map (T14, extended by A12)

The dump at `/v1/players/nfl` is **14.65 MB across 12,227 players**. The design
and this manual both used to say 5 MB; that was wrong by 3×.

The projection keeps every player with a non-empty `fantasy_positions` — 11,902
of them — and throws away every field except name, team and positions. That is a
17× saving in bytes from dropping *fields*, not rows. Keeping the map general
rather than pruning it to this season's slots is A3.

Because Flex Consumption scales to zero and out to forty, and the map is
per-instance memory, the map is persisted to blob storage:

- `EnsureCurrentAsync` is safe on a request path. It adopts the stored map if it
  is under 24 h old, and only fetches the dump if the store is empty or stale.
- `RefreshAsync` always refetches. Only the daily timer calls it.
- A `SemaphoreSlim` collapses a cold-start stampede into one fetch.
- Store failures are logged and ignored — an unreachable store makes a request
  slower, never failed.

**Watch out — this is what took the first deploy down.** `SeasonService` used to
throw if the map had never been refreshed, and only the console harness ever
called the refresher. Every deployed request returned 500. A precondition that
one caller satisfies is a bug waiting for a second caller.

## The API and cloud (M2)

Two endpoints, both anonymous and read-only: `GET /api/season` (optional
`?season=`) and `GET /api/seasons`. The league ID comes from configuration and
**never** from the query string — that would make the site an open relay against
Sleeper's rate limit.

`SeasonPayloadMapper` produces the §09 body, applies A1's exclusion at the read
boundary, and computes a **weak ETag over every week's scores**. Hashing only
the season and week number would freeze the page mid-Sunday.

Infrastructure is one `infra/main.bicep`: Flex Consumption (FC1) Function App,
Static Web App on Free, storage with a `deployments` container, Log Analytics
capped at 0.16 GB/day, App Insights, and a conditional budget.

**Watch out — the design describes a Consumption (Y1) plan that cannot be
provisioned**; the subscription quota for it is zero. FC1 is what exists.

**Watch out — bicep only infers dependencies from symbolic references.** The
Function App referenced the storage *account*, so ARM was free to create the app
before the `deployments` *container* existed. An explicit `dependsOn:
[storage::blob::deployments]` fixes it.

Three workflows: `ci.yml` (build, test, format, vulnerable packages, bicep
what-if), `security.yml` (dependency, secret and Trivy scans, weekly and on every
PR), `deploy.yml` (infra, functions with SBOM and scan, smoke test).

---

# In progress · T24 · First deploy shakeout

CORS is already solved in bicep — `allowedOrigins` is the SWA hostname, with a
`devOrigins` array (empty by default) for local front-end work later. **Do not
run `az functionapp cors add`**: the next infrastructure deploy would wipe it.
Preview environments are deliberately not allowed.

What remains is verification. Run this after a green deploy.

No resource identifier is written out below — every command derives what it
needs from `az`. This repository is intended to go public, and a workspace ID
plus a token is enough to read your telemetry. A2's spirit is not just "no
credentials" but "no identifiers tying a public repo to your infrastructure".

### Set the two hostnames once

```bash
API=$(az functionapp show -n func-idealpoints -g rg-fantsybosty --query defaultHostName -o tsv) && SWA=$(az staticwebapp show -n stapp-idealpoints -g rg-fantsybosty --query defaultHostname -o tsv) && echo "$API / $SWA"
```

### 1 · The API answers

```bash
curl -fsS "https://$API/api/season" | python -c "import sys,json;d=json.load(sys.stdin);print('season',d['season'],'week',d['currentWeek'],'teams',len(d['teams']))"
```

### 2 · A1 holds end to end

```bash
curl -fsS "https://$API/api/season" | python -c "import sys,json;n=[t['name'] for t in json.load(sys.stdin)['teams']];print(n);print('POON SQUAD excluded:', 'POON SQUAD' not in n)"
```

### 3 · Cache headers

```bash
curl -sS -D - -o /dev/null "https://$API/api/season" | grep -iE '^(etag|cache-control)'
```

Expect `Cache-Control: public, max-age=5` and a `W/"…"` ETag.

### 4 · A conditional request returns 304

```bash
ETAG=$(curl -sS -D - -o /dev/null "https://$API/api/season" | tr -d '\r' | awk -F': ' 'tolower($1)=="etag"{print $2}') && curl -sS -o /dev/null -w '%{http_code}\n' -H "If-None-Match: $ETAG" "https://$API/api/season"
```

Run it as one line. Mid-game the ETag legitimately changes between two calls, and
a `200` then means scoring moved, not that ETags are broken.

### 5 · CORS allows the SWA and refuses everything else

```bash
curl -sS -D - -o /dev/null -X OPTIONS -H "Origin: https://$SWA" -H "Access-Control-Request-Method: GET" "https://$API/api/season" | grep -i access-control-allow-origin
```

```bash
curl -sS -D - -o /dev/null -X OPTIONS -H "Origin: https://not-your-site.example" -H "Access-Control-Request-Method: GET" "https://$API/api/season" | grep -i access-control-allow-origin || echo "correctly refused"
```

### 6 · The player map was stored

```bash
ST=$(az storage account list -g rg-fantsybosty --query "[0].name" -o tsv) && KEY=$(az storage account keys list -g rg-fantsybosty -n "$ST" --query "[0].value" -o tsv) && az storage blob show --account-name "$ST" --account-key "$KEY" --container-name player-map --name players.json --query "{bytes:properties.contentLength, modified:properties.lastModified}" -o json
```

Expect roughly 840 KB. Megabytes would mean the projection is not trimming.

### 7 · The store is actually used on a cold start

This is the only evidence A12 solved the problem. Force a cold start:

```bash
az functionapp restart -g rg-fantsybosty -n func-idealpoints
```

```bash
curl -sS -o /dev/null -w '%{http_code} in %{time_total}s\n' "https://$API/api/season"
```

Then read the log. There is no App Insights CLI extension installed, so query the
Log Analytics REST API directly:

```bash
WS=$(az monitor log-analytics workspace show -g rg-fantsybosty -n log-idealpoints --query customerId -o tsv) && TOKEN=$(az account get-access-token --resource https://api.loganalytics.io --query accessToken -o tsv) && curl -sS -X POST "https://api.loganalytics.io/v1/workspaces/$WS/query" -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" -d '{"query":"AppTraces | where TimeGenerated > ago(20m) | where Message has \"player map\" | project TimeGenerated, Message | order by TimeGenerated desc | take 10"}' | python -c "import sys,json;t=json.load(sys.stdin)['tables'][0];[print(r[0][:19], r[1][:120]) for r in t['rows']]"
```

**"Adopted the stored player map"** is success. "Refreshing player map from
Sleeper API" on a *second* cold start means the store is not being read.

If you would rather not paste bearer tokens, `az extension add --name
log-analytics` once gives you `az monitor log-analytics query` instead.

### 8 · Nothing is throwing

```bash
WS=$(az monitor log-analytics workspace show -g rg-fantsybosty -n log-idealpoints --query customerId -o tsv) && TOKEN=$(az account get-access-token --resource https://api.loganalytics.io --query accessToken -o tsv) && curl -sS -X POST "https://api.loganalytics.io/v1/workspaces/$WS/query" -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" -d '{"query":"AppExceptions | where TimeGenerated > ago(1h) | summarize n=count() by ExceptionType"}' | python -c "import sys,json;t=json.load(sys.stdin)['tables'][0];print('exceptions:', t['rows'] or 'none')"
```

### 9 · The Static Web Apps deploy token

Needed only once T32 gives the `web` job something to deploy. One command, so the
token never reaches your screen or shell history:

**Run this in Git Bash.** `$( )` is command substitution in bash and literal text
in `cmd` — run it there and the secret is silently set to the command itself
rather than its output. The length check below is what makes that loud instead of
silent, and the value reaches `gh` over stdin rather than through `--body`, which
removes a layer of quoting.

```bash
TOKEN=$(az staticwebapp secrets list -n stapp-idealpoints -g rg-fantsybosty --query 'properties.apiKey' -o tsv | tr -d '
') && echo "captured ${#TOKEN} chars" && [ "${#TOKEN}" -ge 100 ] && printf %s "$TOKEN" | gh secret set AZURE_STATIC_WEB_APPS_API_TOKEN && echo set
```

Expect `captured 119 chars` then `set`. The `tr -d '
'` matters on Windows:
`$( )` strips a trailing newline but not a trailing carriage return, and a token
stored with one fails SWA authentication later with an error that blames the
token rather than the whitespace.

### 10 · Cost

Check Cost Analysis on `rg-fantsybosty` in the portal after 24 h rather than by
CLI — usage records lag and the CLI view is fiddlier than it is worth. Expect
zero. `BUDGET_ALERT_EMAIL` is set, so the budget resource deploys and will tell
you first.

---

# M3 · Persistence · 17.0 h

Until now every request rebuilds the whole season from Sleeper. That works
because the league is six teams and one season. It stops working when you want
seasons Sleeper no longer serves cheaply, and it wastes calls re-deriving weeks
that can never change again.

M3 adds a database holding **finalized weeks only**. The current, still-playing
week continues to come from Sleeper on every request. Two tasks here — the
password-less database connection and the migration step — are fiddlier than
their hours suggest, and both fail with error messages that point elsewhere.

## T25 · EF Core model — 3.0 h

**What EF Core is, briefly.** An object-relational mapper: you describe C#
classes, it generates the SQL and the schema. A *migration* is a generated C#
file describing one schema change, so a database can be brought to the current
shape by replaying them in order.

### 1 · Add the packages

`FantasyLeague.Data` is currently empty.

```bash
dotnet add src/FantasyLeague.Data package Microsoft.EntityFrameworkCore.SqlServer
```

```bash
dotnet add src/FantasyLeague.Data package Microsoft.EntityFrameworkCore.Design
```

`Design` is what the `dotnet ef` tool needs at build time; it is not used at
runtime.

### 2 · The entities

Four, matching §08 of the design. The one that matters is `WeekScore`:

```csharp
modelBuilder.Entity<WeekScore>(e =>
{
    e.HasKey(x => new { x.LeagueId, x.Season, x.Week, x.RosterId });
    e.Property(x => x.ActualPts).HasPrecision(9, 2);
    e.Property(x => x.IdealPts).HasPrecision(9, 2);
    e.Property(x => x.PlayersPointsJson).HasColumnType("nvarchar(max)");
});

modelBuilder.Entity<League>(e =>
{
    e.HasKey(x => x.LeagueId);
    e.HasIndex(x => x.Season).IsUnique();      // one league per year
});
```

The composite key is what makes the backfill in T30 idempotent — re-running it
after a solver fix upserts rather than duplicates.

**Use `HasPrecision` on every decimal.** EF's default for `decimal` on SQL Server
is `decimal(18,2)`, which is fine here, but being explicit stops a later provider
change from silently rounding fantasy points.

### 3 · Install the tool and create the first migration

```bash
dotnet tool install --global dotnet-ef
```

```bash
dotnet ef migrations add Initial --project src/FantasyLeague.Data --startup-project src/FantasyLeague.Functions
```

`--project` is where migration files are written; `--startup-project` is where EF
looks for configuration and the DI container. They are different projects here,
and omitting either gives an error about not finding a `DbContext` that says
nothing about which flag was missing.

Commit the generated `Migrations/` folder. It is source, not build output.

**Reference** — [EF Core](https://learn.microsoft.com/ef/core/) ·
[Migrations](https://learn.microsoft.com/ef/core/managing-schemas/migrations/)

## T26 · Azure SQL and managed identity — 3.0 h  ±

The goal is a database the Function App reaches **with no password anywhere** —
no connection secret in configuration, nothing to rotate, nothing to leak.

**What a managed identity is.** Azure gives the Function App its own identity in
Entra (`identity: { type: 'SystemAssigned' }`, already in the bicep, and already
surfaced as the `functionAppPrincipalId` output). Azure SQL can be told to trust
that identity directly, so the app authenticates as itself rather than presenting
a credential.

### 1 · The database, in bicep

Add the server and database to `infra/main.bicep` with `useFreeLimit: true` and
`freeLimitExhaustionBehavior: 'AutoPause'`. An auto-pause delay is **required**
on the free offer. The design accepts the resulting cold resume — the first
request after a pause waits — rather than paying to avoid it.

Deploy the template before continuing; the next steps need the server to exist.

### 2 · Make yourself the server's Entra administrator

You cannot create the app's database user until the server has an Entra admin,
and that admin has to be you.

```bash
az sql server ad-admin create -g rg-fantsybosty -s sql-idealpoints --display-name "$(az account show --query user.name -o tsv)" --object-id "$(az ad signed-in-user show --query id -o tsv)"
```

### 3 · Grant the Function App's identity

Connect to the database **as that admin** — Azure Data Studio, SSMS, or the
portal query editor — and run the following. The principal name is the Function
App's *resource name*, not its identity GUID:

```sql
CREATE USER [func-idealpoints] FROM EXTERNAL PROVIDER;
ALTER ROLE db_datareader ADD MEMBER [func-idealpoints];
ALTER ROLE db_datawriter ADD MEMBER [func-idealpoints];
```

**Watch out — do not grant `db_ddladmin`.** Migrations run from the pipeline
(T31), so the *deploy* identity needs schema rights and the running app should
not have them. Handing the app schema-modification rights is a permanent
capability bought for a once-per-deploy need.

### 4 · The connection string

```
Server=tcp:sql-idealpoints.database.windows.net,1433;Database=idealpoints;Authentication=Active Directory Default;Encrypt=True;
```

No password, no secret. `Microsoft.Data.SqlClient` plus `Azure.Identity` resolves
the managed identity in Azure and your `az login` session locally — the same
string works in both places.

**Watch out — the failure modes look identical.** A firewall block and a missing
database user both surface as "cannot connect". Check in that order: is your IP
allowed on the server, does the contained user exist, does it have the role.

**Reference** — [free offer](https://learn.microsoft.com/azure/azure-sql/database/free-offer) ·
[serverless and auto-pause](https://learn.microsoft.com/azure/azure-sql/database/serverless-tier-overview) ·
[Entra authentication](https://learn.microsoft.com/azure/azure-sql/database/authentication-aad-overview)

## T27 · Read path — 2.0 h

Keep SQL off the hot path. On cold start, load every finalized week for the
requested season once; serve later requests from memory, merging the live current
week from the T13 cache.

```csharp
// once per cold start, per season
var finalized = await db.WeekScores
    .Where(w => w.Season == season && w.IsFinal)
    .AsNoTracking()
    .ToListAsync(ct);
```

`AsNoTracking` matters: nothing here is written back through the context, and
EF's change tracker is pure overhead on a read-only path.

A whole season is a few hundred kilobytes, so this is a small dictionary in a
singleton, invalidated when the finalize timer runs.

**Watch out — this is A12's shape again.** A singleton cache is per-instance, and
under Flex Consumption every new instance starts empty. That is fine here,
because the source of truth is SQL rather than a 14 MB download. But the
invalidation in T28 only clears *the instance that ran the timer* — other
instances keep stale data until their own restart. Decide deliberately: give the
cache a short TTL, or accept staleness until the next cold start.

## T28 · Finalize timer — 2.0 h

Tuesday morning, after Monday night football has settled: write the completed
week to SQL with `IsFinal = true`, recompute sigma, then drop the in-memory
season so the next request rehydrates.

```csharp
[Function("FinalizeWeek")]
public async Task Run([TimerTrigger("0 0 9 * * 2")] TimerInfo timer, CancellationToken ct)
{
    // 0 0 9 * * 2  =  09:00:00 UTC every Tuesday
}
```

**Watch out — Azure Functions NCRONTAB has six fields, not five.** The first is
**seconds**. A five-field expression copied from a Linux crontab is read with
your minutes in the seconds position, and the job runs once a minute instead of
once a week. This is the single most common Functions timer bug.

Timers run in UTC unless `WEBSITE_TIME_ZONE` is set. Leave it unset and think in
UTC — the NFL schedule crosses daylight-saving boundaries anyway, so a fixed UTC
time with an hour of slack beats an exact local one.

`RefreshPlayerMap` (`0 30 9 * * *`, added by A12) is the working example to copy
from, including why it is deliberately **not** `RunOnStartup`.

**Reference** — [Timer trigger](https://learn.microsoft.com/azure/azure-functions/functions-bindings-timer)

## T29 · Season chain walk — 2.0 h

Sleeper links each season's league to the one before it. Start at the configured
league ID and follow `previous_league_id` backwards, upserting a `League` row per
hop.

```csharp
var id = options.CurrentLeagueId;
var seen = new HashSet<string>();

while (!string.IsNullOrEmpty(id) && id != "0" && seen.Add(id))
{
    var league = await sleeper.GetLeagueAsync(id, ct);
    await UpsertLeagueAsync(league, ct);
    id = league.PreviousLeagueId;
}
```

The `seen` set is not paranoia — a malformed chain pointing at itself would loop
forever against a rate-limited API. Sleeper uses the string `"0"` as well as null
to mean "no previous league", so test both.

Merge `League:SeasonOverrides` from configuration after the walk, for seasons the
chain cannot reach.

**Watch out — this is unexercisable today.** 2026 is the first season (A4), so
`previous_league_id` is null and the loop runs once. Write it, test it against
fixtures, and expect its first real run at the August 2027 rollover.

## T30 · Historical backfill job — 2.5 h

An explicitly triggered function — not a timer, not a request path — that takes a
season, walks weeks 1 to 18, and writes every `WeekScore` with `IsFinal = true`.

Roughly 21 calls per season. It goes through the T11 token bucket like everything
else, so it self-throttles and takes about half a minute. Make it idempotent:
upsert on the composite key, so re-running after a solver fix is safe and
expected.

**Watch out — do not wire this to a missing-season request.** A visitor would
wait twenty seconds for a page, and a crawler hitting `?season=1998` repeatedly
would generate bursts against Sleeper for seasons that never existed. The API
already returns 404 with the available list; backfill is something you run
deliberately.

## T31 · Migrations in the pipeline — 2.5 h  ±

The GitHub runner has a different IP on every run, so the SQL firewall must be
opened for it and closed again — including when the migration fails.

```yaml
- name: Open firewall for runner
  run: |
    IP=$(curl -s https://api.ipify.org)
    az sql server firewall-rule create -g "$RESOURCE_GROUP" -s sql-idealpoints -n "gh-${{ github.run_id }}" --start-ip-address "$IP" --end-ip-address "$IP"

- name: Apply migrations
  run: dotnet ef database update --project src/FantasyLeague.Data --startup-project src/FantasyLeague.Functions

- name: Close firewall
  if: always()
  run: az sql server firewall-rule delete -g "$RESOURCE_GROUP" -s sql-idealpoints -n "gh-${{ github.run_id }}"
```

**Watch out — `if: always()` on the close step is the whole point.** Without it,
a failed migration leaves the database open to a public IP that will shortly
belong to someone else's CI run.

Name the rule after the run ID rather than a fixed `gh-runner`: two concurrent
deploys would otherwise collide on one rule name, and the second one's close step
would delete the first one's access mid-migration.

Soft-estimated because the deploy identity needs both the firewall grant and
schema rights, and a connection timeout and a permission denial look identical in
the log.

Remember `RESOURCE_GROUP` must be set as **job-level** `env:` from
`secrets.AZ_RESOURCE_GROUP_NAME` — the `secrets` context is not available in
workflow-level `env:`, and an unquoted empty value makes `-g` swallow the next
flag rather than failing cleanly.

---

# M4 · The page · 24.0 h

The first milestone anyone else in the league can look at, and the first that
will generate opinions. The table is the primary object; the chart is the thing
people will play with.

**Before starting, know what you are building against.** The API is live and its
shape is fixed (§09). Fetch it once and read the JSON before writing any
TypeScript — every field name below comes from a real response, not a plan.

```bash
curl -fsS "https://$(az functionapp show -n func-idealpoints -g rg-fantsybosty --query defaultHostName -o tsv)/api/season" | python -m json.tool | head -40
```

## T32 · Frontend scaffold — 2.0 h

This task also **unblocks three things that are currently stubbed**: the `web`
job in `deploy.yml`, the node steps that were removed from `ci.yml`, and the
`AZURE_STATIC_WEB_APPS_API_TOKEN` secret.

### 1 · Create the app

```bash
npm create vite@latest web -- --template react-ts
```

```bash
cd web && npm install
```

### 2 · Install only the D3 pieces you need

```bash
npm i d3-scale d3-shape d3-array
```

```bash
npm i -D @types/d3-scale @types/d3-shape @types/d3-array
```

Install the sub-packages, **not** the `d3` meta-package. You need scales, path
generators and `extent`. You do not need selections, transitions, geo projections
or force simulations, and pulling the whole bundle drags in the DOM layer you
have deliberately decided not to use.

### 3 · SPA fallback

Add `web/staticwebapp.config.json` so client-side routes survive a refresh:

```json
{ "navigationFallback": { "rewrite": "/index.html" } }
```

Without it, loading `/2025` directly returns 404 from the static host — it looks
for a file at that path. Works locally under the dev server, fails once deployed;
the classic SPA-on-static-hosting trap.

### 4 · A typed fetch layer

Write the response types by hand from §09 rather than generating them. There are
five records and they are stable.

Point the dev server at the deployed API, and remember it is a **different
origin** — CORS allows only the SWA hostname today. For local development add
your Vite origin to the bicep `devOrigins` parameter, or proxy through Vite so
the browser sees a same-origin request.

### 5 · Turn the stubs back on

Once `web/package-lock.json` exists, restore the node steps in `ci.yml` and
replace the `web` job's echo in `deploy.yml` with
`Azure/static-web-apps-deploy@v1`. Fetch the deploy token first — step 9 of the
T24 runbook.

**Reference** — [Vite guide](https://vite.dev/guide/) ·
[SWA configuration](https://learn.microsoft.com/azure/static-web-apps/configuration)

## T33 · Design system — 3.0 h

CSS custom properties for the whole palette on `:root`, redefined under
`@media (prefers-color-scheme: dark)`. Every colour used anywhere comes from a
token — no literal hex outside the token block.

Design the dark theme rather than inverting the light one. A naive inversion
produces a muddy accent and unreadable mid-tones. Check contrast on both grounds,
particularly for the team colours, which are the hardest part of the palette to
get right in two themes.

Set `font-variant-numeric: tabular-nums` on every cell containing a figure.
Points totals that do not line up column-wise are the difference between a table
you can scan and one you have to read.

**This league has five scoring teams, not twelve.** The design's colour-scale
worries were written for twelve. Five distinguishable colours is a much easier
problem — do not over-engineer it.

## T34 · Standings table — 3.0 h

Four columns: team, points, sigma, ideal. Sorted descending on points, because
that is what decides the league.

- Mark the sort column visually — it is not obvious that points decide this
  league rather than a win-loss record.
- Show the gap to first place. A standings table whose story requires arithmetic
  is not finished.
- Sigma renders as an em dash below four final weeks, and its column header
  carries the week range: **"σ · through wk 6"** while the points column already
  includes week 7. The API gives you both numbers; use them rather than
  inferring.
- Do not give sigma the same bar or heat encoding as the two point columns —
  lower is better there, and a shared encoding reads backwards.

Use a real `<table>` with `<th scope="col">`, inside a container with
`overflow-x: auto`. Screen readers and narrow phones both benefit, and neither is
served by a grid of divs.

## T35 · Inline range mark — 2.5 h

A small SVG in the sigma cell giving the number a shape: a rule from the team's
minimum to maximum weekly score, a shaded band at one sigma either side of the
mean, and a tick at the mean.

```jsx
<svg viewBox="0 0 140 16" width="140" height="16" role="img"
     aria-label={`${min} to ${max}, mean ${mean}, sigma ${sigma}`}>
  <line x1={s(min)} x2={s(max)} y1="8" y2="8" stroke="currentColor" opacity=".35" />
  <rect x={s(mean - sigma)} width={s(mean + sigma) - s(mean - sigma)}
        y="4" height="8" fill="var(--accent)" opacity=".22" />
  <line x1={s(mean)} x2={s(mean)} y1="2" y2="14" stroke="var(--accent)" strokeWidth="2" />
</svg>
```

**The scale `s` must be shared across every row** — computed from the
league-wide minimum and maximum, not per team. Per-team scales make every row
look identical, which defeats the only purpose the mark has.

`min`, `max`, `mean` and `sigma` all come straight from `totals` in the payload,
and are all `null` together below four final weeks. Render nothing rather than a
degenerate mark.

## T36 · D3 line chart — 4.0 h  ±

**The mental model.** D3 is usually taught as a library that manipulates the DOM
for you — `select`, `append`, `enter`, `exit`. Do not use any of that here. Use
D3 only as a **maths library**: it turns data into numbers and path strings, and
React renders the elements. Everything after this task — hover, filtering, mode
switching — then becomes ordinary React state rather than a second rendering
system fighting the first.

```jsx
import { scaleLinear } from "d3-scale";
import { line } from "d3-shape";

const m = { top: 16, right: 20, bottom: 28, left: 44 };
const x = scaleLinear().domain([1, maxWeek]).range([m.left, width - m.right]);
const y = scaleLinear().domain([0, maxValue]).nice().range([height - m.bottom, m.top]);

const path = line<Point>()
  .x(d => x(d.week))
  .y(d => y(d.value));

return (
  <svg viewBox={`0 0 ${width} ${height}`}>
    {series.map(s => (
      <path key={s.rosterId} d={path(s.points) ?? ""}
            fill="none" stroke={s.color} strokeWidth={1.75} />
    ))}
  </svg>
);
```

A scale is a function from data space to pixel space. `y`'s range is inverted
(`height - m.bottom` first) because SVG's y axis points down and charts point up.

Axes are the one awkward part, because D3's `axisBottom` wants a DOM node. **Do
not reach for a ref.** Call `x.ticks()`, get back an array of values, and map them
to your own `<line>` and `<text>` elements. It is a dozen lines and keeps every
node under React's control.

**Watch out — leave room in the `viewBox` for the outermost tick labels.** A
label at the left edge of the drawing area is clipped by the SVG boundary. That
is what the margin object is for: the scale's range starts at `m.left`, not at
zero.

Soft-estimated because D3-inside-React is a genuine mental shift if you have only
seen D3's own idiom.

**Reference** — [d3-scale](https://d3js.org/d3-scale) · [d3-shape](https://d3js.org/d3-shape)

## T37 · Chart controls — 3.0 h

- **Season** — populated from `/api/seasons`, never a hardcoded list, so a new
  year appears the day the chain walk finds it. Today that endpoint returns only
  2026; the control should still be built against the list.
- **Teams** — multi-select, all on by default, colour-coded to match the lines.
- **Metric** — single-select: cumulative points, cumulative ideal, or expanding
  sigma. Single rather than multi because sigma (roughly 25) and cumulative
  points (roughly 800) cannot share a y-axis without sigma flattening into a line
  along the bottom. Switching metric switches the axis.
- **Display mode** — cumulative, per-week, or vs league median, for the two point
  metrics.

Keep control state in the URL query string. It costs almost nothing and makes
"look at what happened to me in week 9" a link someone can paste into the league
chat, which is the whole social point of the site.

## T38 · Hover interaction — 2.0 h

Hovering a team — in the chart, the legend, or the table — raises that line and
mutes the rest.

Implement as a single `hoveredRosterId` state driving opacity and stroke width.
To raise the hovered path so it draws on top, **sort the series array with the
hovered one last** rather than manipulating the DOM: SVG has no z-index, and
paint order is document order.

Provide a keyboard equivalent — arrow keys through the legend with the same
highlight on focus — or the interaction only exists for mouse users.

## T39 · Polling — 2.0 h

```jsx
useEffect(() => {
  if (!isCurrentSeason) return;              // a finished season never changes

  const tick = () => {
    if (document.visibilityState === "visible" && inGameWindow()) refetch();
  };
  const id = setInterval(tick, 30_000);
  document.addEventListener("visibilitychange", tick);
  return () => {
    clearInterval(id);
    document.removeEventListener("visibilitychange", tick);
  };
}, [isCurrentSeason]);
```

Firing `tick` on `visibilitychange` as well as on the interval means returning to
the tab refreshes immediately rather than waiting up to thirty seconds — the
difference between a page that feels live and one that feels stale.

Send `If-None-Match` with the stored ETag. The server returns **304** with no
body when nothing has moved, which costs almost nothing on either side. The
`isCurrent` flag in the payload is what tells you whether to poll at all.

**Watch out — the API's `Cache-Control` is `max-age=5`, not 30.** The browser may
serve its own cached copy for five seconds; your 30-second interval is the real
polling rate. Do not also add a cache-busting query parameter — it would defeat
the ETag.

## T40 · Responsive and accessibility — 2.5 h

- The table scrolls horizontally in its own container; the page body never
  scrolls sideways.
- Every interactive control has a visible `:focus-visible` state and is reachable
  by keyboard.
- The chart carries `role="img"` and an `aria-label` summarising what it shows —
  an SVG of five paths is otherwise silent to a screen reader.
- Respect `prefers-reduced-motion` for any line transitions.
- Check contrast for the team palette in both themes.

**Reference** — [ARIA authoring practices](https://www.w3.org/WAI/ARIA/apg/)

---

# M5 · Modes, ops and docs · 9.5 h

Deliberately last. These are cheap, taste-driven, and much easier to judge against a season of real data than against three weeks of it.

## T41 · Display modes — 2.0 h

Three transforms over the same weekly arrays:

```
cumulative:  running sum of weekly values
perWeek:     the weekly values themselves
vsMedian:    running sum of (team[w] - median of all teams in week w)
```

The median mode changes no ranking — the median sum is an identical constant for every team — but it removes the league-wide trend that dominates the raw chart. Five near-parallel ascending lines are hard to read; the same five diverging from a zero baseline are legible at a glance. Draw a zero rule when this mode is active.

## T42 · Sigma variants — 2.0 h

**Expanding** (default): σ over weeks 1…w, converging as the season settles. **Rolling**: σ over the last four weeks, showing whether a team is becoming steadier or wilder — more interesting late, close to meaningless before week eight.

**Raw σ vs coefficient of variation.** σ is scale-dependent: a team averaging 140 with σ = 25 is relatively steadier than one averaging 90 with σ = 22, and the raw numbers say the opposite. CV is the fair comparison and is one division away. The table keeps σ because it is the number people say out loud; the chart offers both.

Both variants render as an em dash until enough final weeks exist — four for expanding, four for rolling.

## T43 · Loading and error states — 2.0 h

| State | What the reader sees |
|---|---|
| Cold start, SQL resuming | A skeleton table with an honest line: waking the database, this takes up to a minute. Not a spinner that implies seconds. |
| Season not backfilled | The 404's available-seasons list, rendered as links. Never an empty table. |
| Fewer than four final weeks | Em dashes in the σ column with the reason on hover, not a zero and not a blank. |
| Sleeper unreachable | Finalized weeks still render from SQL; the current week is marked stale with its last-known timestamp. |

That last row is the design paying off: because finalized weeks never depend on Sleeper, an outage degrades the site rather than breaking it. Make that visible instead of failing whole.

## T44 · Cost guardrails — 2.0 h

Three settings that are the difference between a free hobby project and an unpleasant invoice. Configure them; do not assume them.

1. **Application Insights daily cap.** Set a 1 GB/day ingest cap on the component. Telemetry is the likeliest thing to quietly cost money here.
2. **Azure SQL free-limit behaviour.** Confirm it is set to auto-pause on exhaustion, not to continue and bill. One runaway job should cost you a paused database.
3. **A budget alert** on the resource group at a threshold you would want to hear about — a few dollars is enough to be informative when the expected bill is zero.

### Reference

- **Azure** — [Daily cap on log ingestion](https://learn.microsoft.com/azure/azure-monitor/logs/daily-cap)
- **Azure** — [Create a budget alert](https://learn.microsoft.com/azure/cost-management-billing/costs/tutorial-acm-create-budgets)

## T45 · README and runbook — 1.5 h

Short, and mostly one procedure. Write it now, while you remember why things are the way they are.

### The August rollover

1. Create the new season in Sleeper using the roll-over flow, so `previous_league_id` is set.
2. Update `League__CurrentLeagueId` in the Function App's settings to the new league ID.
3. Wait for the daily chain walk, or trigger it manually.
4. Confirm the new year appears in `/api/seasons`.

That is the entire annual maintenance burden, which is the point of D8.

Also record: how to run the backfill job for a season, how to re-run it after a solver fix, and the three cost settings from T44 with a note to re-check them after any infrastructure change — a `main.bicep` deployment can quietly reset a portal-set value.

# M6 · Positional radar · 10.0 h

One team’s average output per starting slot against the league average at the same slot, on axes taken from the league’s own rules. Last because it rests on a property of Sleeper’s payload that T02 has to confirm first.

## T46 · Slot attribution — 2.5 h

`starters[i]` plays in the *i*th startable slot, so attribution is an index join and needs no position inference at all.

```csharp
public static IReadOnlyDictionary<string, decimal>? SlotAverages(
    IReadOnlyList<string> starters,
    IReadOnlyDictionary<string, decimal> playersPoints,
    IReadOnlyList<Slot> startableSlots)
{
    if (starters.Count != startableSlots.Count) return null;   // rules changed mid-season

    var byCode = new Dictionary<string, (decimal Total, int Slots)>();
    for (var i = 0; i < starters.Count; i++)
    {
        var code = startableSlots[i].Code;
        var pts  = starters[i] == "0"                          // an empty slot, not a missing key
                 ? 0m
                 : playersPoints.GetValueOrDefault(starters[i], 0m);

        var cur = byCode.GetValueOrDefault(code);
        byCode[code] = (cur.Total + pts, cur.Slots + 1);
    }

    return byCode.ToDictionary(kv => kv.Key, kv => kv.Value.Total / kv.Value.Slots);
}
```

Dividing by the slot count is what makes a two-RB league comparable across axes. Without it every team’s RB axis is roughly twice its QB axis, which describes the rules rather than the roster.

> **Watch out**
>
> **The nullable return is the mid-season rules guard.** The design assumes `roster_positions` does not change during a season, and Sleeper permits it anyway. If it happens, `starters` stops lining up with the stored slot list and every week is silently attributed to the wrong axis. Return null, log the league and week at error level, and let that week contribute nothing to the radar — the points and ideal columns do not depend on slot alignment and carry on unaffected.
>
> A started-nobody slot is `"0"` and is worth a real zero. Do not filter it out: leaving a slot empty is a managerial decision and should drag the average exactly as much as it drags the score.
>

## T47 · League reference and ratios — 1.5 h

1. For each team and slot, average the weekly per-slot values over regular-season weeks that produced an attribution.
2. For each slot, average those season values across all teams — that is the reference circle.
3. Divide. Team season value over league season value gives the plotted ratio, centred on 1.0.

> **Watch out**
>
> **Average first, then divide.** Computing a ratio per week and averaging the ratios looks equivalent and is not. On the week where the entire league’s kickers scored two points between them, the denominator is near zero and one ordinary Sunday becomes a permanent distortion on the K axis.
>
> Guard the degenerate denominator anyway: if a league season value is zero — every team started nobody at that slot all year — the ratio is undefined. Return null for that axis and draw it at the reference circle with a marker, rather than dividing by zero or quietly plotting 1.0 as though it were measured.
>

## T48 · Slot block in the API — 1.0 h

Three additions to the existing `/api/season` payload — no new endpoint, for the same reason the table and chart already share one.

```
"slotOrder": ["QB","RB","WR","TE","FLEX","K","DEF"],
"leagueSlotAverages": { "QB": 18.4, "RB": 12.1, ... },

"teams": [ { ..., "slotAverages": { "QB": 21.2, "RB": 11.8, ... } } ]
```

`slotOrder` comes from the season’s `roster_positions`, deduplicated while preserving first appearance:

```csharp
var slotOrder = startableSlots.Select(x => x.Code).Distinct().ToList();
```

LINQ’s `Distinct()` preserves first-occurrence order over in-memory sequences, which is what you want — the league’s own lineup order is the only non-arbitrary axis order available, and per R2 it must never be re-sorted.

Send raw averages rather than pre-computed ratios. The client can divide, and shipping both numbers means a tooltip can say “21.2 against a league average of 18.4” instead of only “1.15×”.

## T49 · Radar chart — 3.5 h  ±

Polar geometry, computed rather than eyeballed. One team, one reference circle, axis order fixed.

```
const R = 78;                       // radius of the 1.0 reference circle
const angle = (i: number) => -Math.PI / 2 + (2 * Math.PI * i) / slots.length;

const point = (i: number, ratio: number) => {
  const r = R * Math.min(ratio, 2);           // clamp: one freak axis must not escape
  return [cx + r * Math.cos(angle(i)), cy + r * Math.sin(angle(i))] as const;
};

const polygon = slots.map((s, i) => point(i, ratios[s]).join(",")).join(" ");
```

Label anchoring follows the label’s side of the circle — `start` to the right of centre, `end` to the left, `middle` at top and bottom — otherwise the left-hand labels overrun the drawing.

> **Watch out**
>
> **Leave room in the viewBox for the outermost labels.** The polygon extends to 1.5×R and the labels sit beyond that; size the viewBox to the labels, not to the chart. Place them at roughly 1.7×R and check the widest one — `SUPER_FLEX` is nine characters and will be the one that overflows.
>
> **Never describe or label the enclosed area.** Area grows with the square of the radius, so a team 20% better everywhere encloses about 44% more space. The polygon is a shape to recognise, not a quantity to read.
>
> Give the `<svg>` a `role="img"` and an `aria-label` naming the team and which axes sit outside the circle. Seven unlabelled paths are silent to a screen reader.
>

A single series needs no legend box — the title names the team. If you allow a second team for a head-to-head, a legend becomes mandatory, and two is the hard cap: three overlapping polygons is spaghetti.

## T50 · Diverging bar alternative — 1.5 h

The same numbers on a linear axis, as a toggle beside the radar. Horizontal bars from a zero centre, one row per slot, in `slotOrder`.

This is the form the data actually asks for — a signed deviation from a baseline across a handful of categories is a diverging bar chart. Bars share one linear scale, so values are directly comparable and nothing is distorted by area or by axis ordering. Use a two-hue diverging pair with a neutral midpoint at zero, and direct-label each bar with its value.

It exists so the radar does not have to be accurate about magnitude. Keep the radar as the default anyway: this is a page for a fantasy league, and “what shape is my team” is a question people genuinely want answered. Two views, one data set, and no argument about which one is correct.

# M7 · Motion and polish — optional · 7.5 h

Optional throughout. Cut the whole milestone and the site still does its job — which is the test every task here should pass before you spend an hour on it.

## T51 · Motion setup — 1.0 h

`npm i motion` — the package formerly published as `framer-motion`, imported from `motion/react`. It is the right choice here because it is declarative, which means D9 still holds: React owns every node and Motion animates props.

```
import { LazyMotion, domAnimation, m, useReducedMotion } from "motion/react";

<LazyMotion features={domAnimation}>
  <App />
</LazyMotion>
```

The `m` component with `LazyMotion` cuts the bundle to roughly a fifth of a plain `motion` import. Not that Static Web Apps’ free bandwidth will notice — it is first paint that matters.

> **Watch out**
>
> `useReducedMotion()` is not optional; T40 already commits you to honouring it. Read it once near the root and pass a duration of zero rather than scattering conditionals — a motion system with one opt-out is maintainable, a hundred is not.
>

### Reference

- **Motion** — [Motion for React](https://motion.dev/docs/react)

## T52 · Table layout animation — 1.0 h

The single most valuable animation on this site, and it is one prop.

```
<m.tr layout key={team.rosterId}
      transition={{ type: "spring", stiffness: 400, damping: 40 }}>
```

When the standings re-sort mid-afternoon, rows physically slide past one another. That makes the live-ness legible instead of merely claimed — a number quietly changing in place is invisible; a row overtaking another is not.

`key` must be `rosterId`, never the array index, or Motion animates the wrong rows into each other.

> **Watch out**
>
> **The 30-second poll will replay any entrance animation.** This is the trap that will actually bite. An `initial={{ opacity: 0 }}` anywhere inside the polled subtree flashes every thirty seconds and makes the page unusable during games. Gate it on first load:
>
> `initial={hasLoadedOnce ? false : { opacity: 0, y: 8 }}` — `false` tells Motion to jump straight to the target. `layout` is inherently safe, because it animates changes rather than mounts.
>

## T53 · Chart reveal and transitions — 2.0 h  ±

Draw-on for the line chart is a one-liner:

```
<m.path d={path} initial={{ pathLength: 0 }} animate={{ pathLength: 1 }}
        transition={{ duration: 0.8, ease: "easeOut" }} />
```

> **Watch out**
>
> **Do not try to tween the `d` string between display modes.** Switching cumulative to per-week to vs-median changes the path shape, and Motion cannot meaningfully interpolate arbitrary path data — you get a smear or nothing. Two workable options: animate the underlying *values* and recompute the path each frame, or simply crossfade the old path out and the new one in.
>
> Crossfade is what the estimate assumes and is almost always enough. Reach for a path-interpolation library only if you have seen the crossfade and specifically disliked it.
>

For the radar, animate the polygon in by scaling from the centre rather than morphing points — same reasoning, and the reference circle should not animate at all. It is the fixed thing the shape is read against.

## T54 · Section transitions — 1.0 h

The View Transitions API costs nothing and degrades to an instant swap:

```
function go(next: Section) {
  if (!document.startViewTransition) return setSection(next);
  document.startViewTransition(() => flushSync(() => setSection(next)));
}
```

Supported in Chromium and Safari; check current Firefox support before leaning on it, and note that the guard above means you never have to care. Give shared elements a `view-transition-name` and they morph between sections rather than crossfading.

Motion’s `AnimatePresence` does the same job with more control and more code. Start with the native API and reach for `AnimatePresence` only if you need exit animations it cannot express.

### Reference

- **MDN** — [View Transition API](https://developer.mozilla.org/docs/Web/API/View_Transition_API)

## T55 · Football loader — 2.5 h

A football arcing back and forth while the site loads. Charming for four seconds, maddening for forty — so the whole design is about which wait it is bound to.

### Three timing bands

| Wait | What shows | Why |
|---|---|---|
| 0 – 400 ms | Nothing at all | The warm path is this fast. A loader that flashes for 80 ms is worse than none. |
| 0.4 – 4 s | The football | The band where a mascot is genuinely delightful. |
| 4 s + | Ball settles, copy appears | The cold-start case from D7. Past ten seconds people want an explanation, not a loop. |

### It goes in index.html, not in React

The loader has to paint before the bundle has loaded, so it cannot be a component — a React loader can only appear once React is mounted, which is precisely the part you are waiting for. Inline the markup and CSS in `index.html`; the app removes `#boot` on its first real render.

```
<div id="boot" role="status" aria-live="polite">
  <svg class="ball" viewBox="0 0 24 15" aria-hidden="true">
    <ellipse cx="12" cy="7.5" rx="11" ry="6.6" fill="#7A4A24"/>
    <path d="M2.5 7.5h19M9 4.6v5.8M12 4.1v6.8M15 4.6v5.8"
          stroke="#F6F1E8" stroke-width="1.1" stroke-linecap="round"/>
  </svg>
  <p class="why">Waking the database. This can take up to a minute.</p>
  <span class="sr">Loading standings</span>
</div>
```

```
@keyframes punt {
  0%   { offset-distance: 0%;   transform: scaleX(1);  }
  48%  { offset-distance: 100%; transform: scaleX(1);  }
  52%  { offset-distance: 100%; transform: scaleX(-1); }
  100% { offset-distance: 0%;   transform: scaleX(-1); }
}
@keyframes fadein { to { opacity: 1 } }

#boot        { opacity: 0; animation: fadein .25s ease .4s forwards; }
#boot .why   { opacity: 0; animation: fadein .4s  ease 4s   forwards; }
#boot .ball  {
  width: 64px;
  offset-path: path("M 10 104 Q 120 -12 230 104");
  offset-rotate: auto;                       /* nose follows the trajectory */
  animation: punt 2.2s cubic-bezier(.37,0,.63,1) infinite;
}
/* narrow screens throw it straight up instead — see below */

@media (prefers-reduced-motion: reduce) {
  #boot .ball, #boot .spin, #boot .speed { animation: none; }
  #boot .ball { offset-distance: 50%; }
  #boot .speed { display: none; }
}
```

The two `scaleX` flips are what make the return trip work. `offset-rotate: auto` orients the ball to the path tangent, and that orientation does not reverse when the animation plays back toward 0% — so without the flip the ball flies tail-first on the way home. Flipping at the apex, where the ball is briefly side-on, hides the switch.

### Spin without rotating: the comic-book trick

A real spiral is rotation about the long axis, which barely alters the silhouette — so a CSS `rotate` spins the ball end over end and reads as a fumble. Cartoonists solve this with drawn arcs rather than rotation, and so should you. The two kinds say different things:

| Arcs | Placed | Read as |
|---|---|---|
| Speed lines | Outside the ball, trailing | Velocity — how fast it is travelling |
| Surface arcs | Clipped inside the ball | Spin — the skin turning under the laces |

Clipping is the whole trick. An arc drawn *on* the ball and slid sideways reads as a marking rotating away from you; the same arc drawn beside the ball reads as a motion streak. Both are cheap; only the clipped one produces spin.

```
<svg class="ball" viewBox="0 0 40 24">
  <defs><clipPath id="skin">
    <ellipse cx="20" cy="12" rx="11" ry="6.8"/>
  </clipPath></defs>

  <g class="speed">                        <!-- outside: velocity -->
    <path d="M6 8 q-5 4 0 8"/>
    <path d="M1 10 q-4 2 0 4"/>
  </g>

  <ellipse cx="20" cy="12" rx="11" ry="6.8" fill="#7A4A24"/>

  <g class="spin" clip-path="url(#skin)">  <!-- inside: rotation -->
    <path d="M12 3 q3.5 9 0 18"/>
    <path d="M22 3 q3.5 9 0 18"/>
    <path d="M32 3 q3.5 9 0 18"/>
    <path d="M42 3 q3.5 9 0 18"/>         <!-- the spare, see below -->
  </g>

  <path class="laces" d="M15 12h10M17 9.6v4.8M20 9.2v5.6M23 9.6v4.8"
        stroke="#F6F1E8" stroke-width="1.1" stroke-linecap="round"/>
</svg>
```

```
@keyframes turn  { to { transform: translateX(-10px) } }   /* == arc spacing */
@keyframes streak{ 0%,100% { opacity:.15 } 50% { opacity:.5 } }

.spin  { animation: turn .34s linear infinite; }
.spin path  { fill:none; stroke:#5C3417; stroke-width:1.1; opacity:.45; }
.speed { animation: streak .34s ease-in-out infinite; }
.speed path { fill:none; stroke:currentColor; stroke-width:1.2; opacity:.25; }
```

> **The two things that make or break it**
>
> **Translate by exactly one arc spacing, and draw one spare arc beyond the clip.** Arcs sit at x = 12, 22, 32 and the ball clips to roughly x ∈ [9, 31]. Translating by −10 moves each arc into its neighbour’s old position, so the end frame is identical to the start — an invisible loop. Without the fourth arc at x = 42 there is nothing to replace the one that left, and you get a visible hitch every third of a second.
>
> **Spin far faster than flight.** The arc takes 2.2 s; the spin cycle should be around 0.3 s. That contrast is what sells it — matched speeds read as a wobble, not a spiral.
>

The `scaleX(-1)` flip from the outbound-return keyframes handles both arc types for free: the speed lines stay behind the ball, and the surface arcs reverse their apparent direction, which is what genuinely happens when a spiralling ball flies back the other way.

One sizing note: at 34 px none of this detail reads. Take the loader ball to about 64 px — it is the only thing on screen, so it can afford to be the hero, and below roughly 48 px the arcs turn to mush and you may as well drop them.

### Narrow screens: throw it straight up

A 220 px horizontal arc is cramped on a 375 px phone, and it spends the one dimension a phone does not have. Throw the ball straight up instead — it suits the form factor, and it is the simpler animation of the two.

The vertical variant needs no `offset-path` at all, and no `scaleX` flip, because nothing reverses left to right:

```
@keyframes toss { from { translate: 0 62px } to { translate: 0 -62px } }

#boot .ball {                              /* mobile first */
  rotate: -90deg;                          /* nose up */
  animation: toss 1.05s cubic-bezier(0,0,.35,1) infinite alternate;
}

@media (min-width: 560px) {                /* the punt, on wider screens */
  #boot .ball {
    rotate: 0deg;
    offset-path: path("M 10 104 Q 120 -12 230 104");
    offset-rotate: auto;
    animation: punt 2.2s cubic-bezier(.37,0,.63,1) infinite;
  }
}
```

**Gravity is free here.** An ease-out curve decelerates toward its end, and `animation-direction: alternate` replays it reversed, which accelerates — so a single ease-out plus `alternate` produces genuine projectile motion: slowing as it rises, hanging at the apex, accelerating back down. No physics, no second keyframe set. Do not be tempted by `ease-in-out`, which hangs at *both* ends and reads as a float rather than a throw.

**The spin arcs need no changes at all.** Rotating the carrier by −90° rotates its children with it, so the clipped arcs still slide across the ball’s short axis in local coordinates — the same `translateX(-10px)`, the same spare arc, the same loop. Only the carrier's motion differs between the two layouts.

Using the individual `translate` and `rotate` properties rather than a combined `transform` is what keeps this clean: the toss animates `translate`, the orientation sits on `rotate`, and the punt keyframes still own `transform` for their `scaleX` flip. Three properties, no collisions.

> **Watch out**
>
> **Drop the speed lines on narrow screens.** Trailing lines must sit below the ball while it rises and above it while it falls, and with `alternate` there is no keyframe at which to flip them. Rather than build a second animation for a decoration, hide them: `#boot .speed { display: none }` below the breakpoint. The spin arcs carry the effect on their own, and a phone-sized loader is better with less in it anyway.
>

> **Watch out**
>
> **First load only.** Like every other animation here, this must not reappear on the 30-second poll. Tie it to `#boot`, which is removed once and never recreated.
>
> The easing above is symmetric in and out, which reads as a lob. For a flatter, harder throw use a linear horizontal traverse and let the arc height do the work.
>

`offset-path: path()` is supported across current Chromium, Safari and Firefox. Where it is not, the ball simply sits still at the start of the path and the fade still runs — an acceptable degradation for a loading state, and one more reason to keep the explanatory copy doing the real work.

### Reference

- **MDN** — [offset-path](https://developer.mozilla.org/docs/Web/CSS/offset-path)
- **MDN** — [prefers-reduced-motion](https://developer.mozilla.org/docs/Web/CSS/@media/prefers-reduced-motion)
