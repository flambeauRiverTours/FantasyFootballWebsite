# Ideal Points — Field Manual

> Converted from the published artifact:
> https://claude.ai/code/artifact/bae9d64b-1fe8-4f4a-a47c-c733adc901d7
> Updated 2026-09-10 · step-by-step instructions for 50 required + 5 optional tasks, in build order.
> Task IDs match `tasks/todo.md`.

---

Step-by-step instructions for every task in the build plan, in build order — fifty required and five optional. Task IDs match the tracker, so T21 here is T21 there.

### Before you begin

Install .NET 9 SDK, Azure Functions Core Tools v4, Azure CLI, and Node 20 or newer. You will also need an Azure subscription and a GitHub repository.

**Find your league ID** by opening the league on the Sleeper web app — the URL is `sleeper.com/leagues/<league_id>/…` and the numeric segment is what every API call needs.

Links below point at documentation roots and stable section paths rather than deep pages, because deep documentation links rot faster than the docs themselves. Where a link lands on a landing page, the thing you want is one search box away.

- **Sleeper** — [Sleeper read API documentation](https://docs.sleeper.com/)
- **.NET** — [.NET documentation](https://learn.microsoft.com/dotnet/)
- **Azure** — [Azure Functions](https://learn.microsoft.com/azure/azure-functions/)
- **Azure** — [Azure Static Web Apps](https://learn.microsoft.com/azure/static-web-apps/)

## M0 · Core domain · 13.0 h

One hour of curl, then pure functions over fixtures. No cloud account, no database. The recon goes first so that everything after it is written against the real league rather than an assumption about it — and this is where the bugs that matter live.

### T01 · Solution scaffold — 1.5 h

Create the five projects and wire the shared build settings once, at the root, so every project inherits them.

```bash
dotnet new sln -n FantasyLeague
dotnet new classlib -o src/FantasyLeague.Core
dotnet new classlib -o src/FantasyLeague.Sleeper
dotnet new classlib -o src/FantasyLeague.Data
dotnet new xunit   -o tests/FantasyLeague.Core.Tests

# Functions templates are a separate install
dotnet new install Microsoft.Azure.Functions.Worker.ProjectTemplates
dotnet new func -F net9.0 -o src/FantasyLeague.Functions

find . -name "*.csproj" -exec dotnet sln add {} +
```

> **Which shell**
>
> **Run every shell block in this manual under Git Bash.** They are POSIX
> scripts: this one uses Unix `find`, and T02 uses `curl`, `jq`, `mkdir -p`,
> `for w in 1 2 3` and `$L`. In `cmd`, `find` resolves to Windows' `find.exe`,
> a text-search tool, and you get `File not found - -NAME`.

If you would rather stay in your own shell, the last line only — the rest of the
block is `dotnet` and runs anywhere:

```powershell
# PowerShell
Get-ChildItem -Recurse -Filter *.csproj | ForEach-Object { dotnet sln add $_.FullName }
```

```text
:: cmd, at the prompt
for /r %f in (*.csproj) do dotnet sln add "%f"

:: cmd, inside a .bat/.cmd file — the percent must be doubled
for /r %%f in (*.csproj) do dotnet sln add "%%f"
```

`dotnet sln add **/*.csproj` does *not* work: the CLI does no globbing of its
own, so the expansion has to come from the shell. In bash that also needs
`shopt -s globstar`, which is why the `find` form is the one written above.

> **`.slnx`, not `.sln`, on the .NET 10 SDK**
>
> `dotnet new sln` now defaults to the XML solution format, so you get
> `FantasyLeague.slnx`. Everything in this manual works with it. If you want the
> classic format — for an older Visual Studio, or for a CI step that globs
> `*.sln` — ask for it explicitly:
>
> ```bash
> dotnet new sln -n FantasyLeague -f sln
> ```

> **Watch out · two traps in the `func` line**
>
> **`--worker-runtime dotnet-isolated` is not a `dotnet new` option.** It belongs
> to `func init`, from Azure Functions Core Tools — a different tool. Passing it
> here fails with `'--worker-runtime' is not a valid option`. It is also
> unnecessary: the `func` template that ships in
> `Microsoft.Azure.Functions.Worker.ProjectTemplates` *is* the isolated-worker
> template. You can confirm it from the generated `.csproj` — it references
> `Microsoft.Azure.Functions.Worker` and `Microsoft.Azure.Functions.Worker.Sdk`
> and sets `<OutputType>Exe</OutputType>`, because an isolated worker is a
> standalone process. The in-process model is a class library and a different
> template package (`Microsoft.Azure.WebJobs.ProjectTemplates`).
>
> **`-F net9.0` is load-bearing.** The template defaults to `net8.0` and writes
> an explicit `<TargetFramework>` into the generated `.csproj`.
> `Directory.Build.props` is imported *before* the project's own
> `PropertyGroup`, so the project's line wins — the Functions project would
> silently build as net8.0 while the other four are net9.0, and nothing would
> warn you. If you leave the flag off, delete the `<TargetFramework>` line from
> the generated `.csproj` so it inherits instead.

Then a `Directory.Build.props` at the repository root:

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
  </PropertyGroup>
</Project>
```

`Nullable` plus `TreatWarningsAsErrors` is the pairing that matters. Sleeper's payload is full of fields that are legitimately absent, and the compiler complaining about every unguarded dereference is the cheapest defence you will get all project.

#### Reference

- **.NET** — [dotnet CLI reference](https://learn.microsoft.com/dotnet/core/tools/)
- **.NET** — [Nullable reference types](https://learn.microsoft.com/dotnet/csharp/nullable-references)

### T02 · Sleeper recon — 1.5 h

An hour with `curl`, before a line of domain code. Everything else in M0 is otherwise written against assumptions about an API this project has never actually called — and those assumptions shape the eligibility map, the fixtures and the radar.

Find the league ID first: open the league on the Sleeper web app and take the numeric segment from `sleeper.com/leagues/<league_id>/…`.

```bash
L=<your-league-id>

# Q1 — the slot list that shapes T04 and every solver fixture
curl -s "https://api.sleeper.app/v1/league/$L" | jq '.roster_positions'

# Q2 — SUPER_FLEX or IDP present?
curl -s "https://api.sleeper.app/v1/league/$L" | jq '.roster_positions | unique'

# Q7 — how far back does the season chain reach?
curl -s "https://api.sleeper.app/v1/league/$L" | jq '{season, previous_league_id}'

# fixtures for T06 — capture real weeks while you are here
mkdir -p tests/fixtures
for w in 1 2 3; do
  curl -s "https://api.sleeper.app/v1/league/$L/matchups/$w" > tests/fixtures/wk$w.json
done

# does starters[i] line up with the i-th startable slot?  (§11 depends on it)
jq '.[0] | {starters, starters_points, points}' tests/fixtures/wk1.json
```

#### Write the answers down

- **Q1** — the exact `roster_positions` array. Paste it into the repo; T04 is built from it.
- **Q2** — whether `SUPER_FLEX` or IDP slots appear, which decides how much of the eligibility map is live code.
- **Q3** — whether IR-slotted players show up in `players`, and whether the league wants them counted toward ideal.
- **Q4** — what a playoff-week matchup looks like when only some teams play.
- **Q7** — follow `previous_league_id` back and count the hops against how many seasons the league has run.
- **The `starters` alignment** — confirm `starters[i]` is a player eligible for the *i*th startable slot. The entire positional radar rests on this and it is an observation, not a documented guarantee.
- **The published rate-limit guidance.** The design assumes 100/min and runs at about 2.3, so a looser real figure changes nothing — but inherit the number knowingly.

> **Watch out**
>
> **This is recon, not verification.** You cannot assert `ideal >= actual` or `sum(starters_points) == points` here, because neither the solver nor the client exists yet. Those assertions land in T16, against working code and a whole season. What T02 buys is that everything between here and there is written against the real league instead of a guess.
>
> Commit the captured weeks. Real payloads catch things invented ones never will, and they cost nothing to keep.
>

#### Reference

- **Sleeper** — [Read API documentation](https://docs.sleeper.com/)

### T03 · Domain types — 1.0 h

Records in `FantasyLeague.Core`, with no reference to HTTP, JSON, or EF. These are the shapes the solver and the consistency maths operate on — deliberately not the shapes Sleeper returns.

```csharp
public readonly record struct PlayerId(string Value);

public sealed record RosteredPlayer(
    PlayerId Id,
    string Name,
    IReadOnlyList<string> FantasyPositions,
    decimal Points);

public sealed record TeamWeek(
    int RosterId, int Week,
    decimal Actual, decimal Ideal,
    IReadOnlyList<PlayerId> Starters,
    IReadOnlyList<PlayerId> IdealLineup,
    bool IsFinal);

public sealed record TeamSeason(
    int RosterId, string Name,
    IReadOnlyList<TeamWeek> Weeks);
```

Keeping these separate from the wire DTOs (T09) means a Sleeper schema change touches one mapping function rather than the solver, the tests, and the API surface.

#### Reference

- **.NET** — [Records in C#](https://learn.microsoft.com/dotnet/csharp/language-reference/builtin-types/record)

### T04 · Slot parsing and eligibility — 1.5 h

Turn `league.roster_positions` — a flat array like `["QB","RB","RB","WR","WR","TE","FLEX","K","DEF","BN","BN",…]` — into the list of startable slots, and encode which positions each accepts.

```csharp
public sealed record Slot(string Code)
{
    private static readonly Dictionary<string, string[]> Flex = new()
    {
        ["FLEX"]       = ["RB", "WR", "TE"],
        ["SUPER_FLEX"] = ["QB", "RB", "WR", "TE"],
        ["REC_FLEX"]   = ["WR", "TE"],
        ["WRRB_FLEX"]  = ["RB", "WR"],
        ["IDP_FLEX"]   = ["DL", "LB", "DB"],
    };

    private static readonly HashSet<string> NotStartable = ["BN", "IR", "TAXI"];

    public static IReadOnlyList<Slot> Parse(IEnumerable<string> rosterPositions) =>
        rosterPositions.Where(c => !NotStartable.Contains(c))
                       .Select(c => new Slot(c))
                       .ToList();

    public bool Accepts(IReadOnlyList<string> positions) =>
        Flex.TryGetValue(Code, out var allowed)
            ? positions.Any(allowed.Contains)
            : positions.Contains(Code);
}
```

Note that `roster_positions` repeats a code once per slot — two `RB` entries means two RB slots — so the list is already the right length and needs no expansion.

> **Watch out**
>
> Match against `fantasy_positions`, not `position`. A player Sleeper lists at `position: "RB"` may carry `fantasy_positions: ["RB","WR"]`, and the second entry is what makes him legal in a `WRRB_FLEX`. Using the singular field silently undercounts ideal points for exactly the multi-eligible players who make flex slots interesting.
>

### T05 · Bitmask DP solver — 2.0 h

Exact maximum-weight assignment of players to slots. Each player is used at most once; each slot is filled at most once.

```csharp
public static (decimal Points, IReadOnlyList<PlayerId> Lineup) Solve(
    IReadOnlyList<RosteredPlayer> players,
    IReadOnlyList<Slot> slots)
{
    var n = slots.Count;
    var dp   = new decimal[1 << n];
    var from = new (int Mask, PlayerId Id)?[1 << n];
    Array.Fill(dp, -1m);
    dp[0] = 0m;

    foreach (var p in players)
    {
        var next     = (decimal[])dp.Clone();
        var nextFrom = ((int, PlayerId)?[])from.Clone();

        for (var mask = 0; mask < dp.Length; mask++)
        {
            if (dp[mask] < 0) continue;
            for (var s = 0; s < n; s++)
            {
                if ((mask & (1 << s)) != 0) continue;
                if (!slots[s].Accepts(p.FantasyPositions)) continue;

                var m2 = mask | (1 << s);
                if (dp[mask] + p.Points > next[m2])
                {
                    next[m2]     = dp[mask] + p.Points;
                    nextFrom[m2] = (mask, p.Id);
                }
            }
        }
        dp = next; from = nextFrom;
    }

    var best = Array.IndexOf(dp, dp.Max());
    var lineup = new List<PlayerId>();
    for (var m = best; from[m] is (var prev, var id); m = prev) lineup.Add(id);
    return (dp[best], lineup);
}
```

The `from` array is what lets you reconstruct which players made up the ideal lineup, which you will store and display. Take `dp.Max()` rather than `dp[(1 << n) - 1]`: on a bye-heavy week a roster may be unable to fill every slot, and the full mask is then unreachable.

### T06 · Solver fixtures and tests — 3.0 h  ±

Four tests carry the weight. The first is the one that justifies the DP existing at all.

#### 1 · Greedy loses

Slots `[WR, FLEX]`. Players: a 20-point WR and an 18-point RB. A greedy pass that sorts by points and takes the first eligible open slot puts the WR in `WR` or in `FLEX` depending on slot order — and if it lands in `FLEX`, the RB has nowhere to go and the total is 20. The correct answer is 38.

```csharp
[Fact]
public void Flex_ordering_does_not_strand_a_player()
{
    var slots = Slot.Parse(["WR", "FLEX"]);
    var players = new[]
    {
        new RosteredPlayer(new("wr1"), "WR One", ["WR"], 20m),
        new RosteredPlayer(new("rb1"), "RB One", ["RB"], 18m),
    };
    Assert.Equal(38m, Solver.Solve(players, slots).Points);
}
```

#### 2 · Bye-week hole

Fewer eligible players than slots at some position. Assert the solver returns the best partial fill rather than throwing or returning zero.

#### 3 · Ideal is never below actual

Over a real week's fixture, for every roster: `ideal >= actual`. The started lineup is one of the assignments the solver considers, so any violation is a bug — almost always an eligibility rule.

#### 4 · Multi-eligible player

A player with `fantasy_positions: ["RB","WR"]` filling a `WRRB_FLEX`, proving T04’s warning is handled.

Use the weeks captured in T02 as fixtures — that is what the recon bought you. Write the constructed cases above by hand, and pin the real payloads alongside them so the eligibility map is exercised against your league’s actual slot list rather than an assumed one.

#### Reference

- **Testing** — [xUnit getting started](https://xunit.net/docs/getting-started/v3/getting-started)

### T07 · Consistency maths — 1.0 h

Population standard deviation over finalized weeks, plus the summary figures the table's range mark needs.

```csharp
public sealed record Spread(
    double Sigma, double Cv, decimal Mean, decimal Min, decimal Max, int Weeks);

public static Spread? Compute(IReadOnlyList<decimal> finalWeeks, int minWeeks = 4)
{
    if (finalWeeks.Count < minWeeks) return null;

    var mean     = finalWeeks.Average();
    var meanD    = (double)mean;
    var variance = finalWeeks.Sum(p => Math.Pow((double)p - meanD, 2)) / finalWeeks.Count;
    var sigma    = Math.Sqrt(variance);

    return new Spread(sigma, meanD == 0 ? 0 : sigma / meanD,
                      mean, finalWeeks.Min(), finalWeeks.Max(), finalWeeks.Count);
}
```

Divide by `Count`, not `Count - 1`: the weeks played are the complete population, not a sample. Return `null`, never a zero — a method that answers `0.0` for a two-week season produces a table where the least-known teams look the steadiest.

### T08 · Sigma edge-case tests — 1.5 h

Almost all of these are about absence. Each should return a defined result for a stated reason, and none should throw.

| Input | Expected | Why it matters |
|---|---|---|
| [] | null | Week zero, before any games |
| [118.2] | null | σ of one point is undefined, not zero |
| 3 weeks | null | The minimum-weeks boundary, from below |
| 4 weeks | a number | The same boundary, from above |
| [100,100,100,100] | 0.0 | A genuine zero — perfect consistency exists |
| [0,120,110,115] | a number | A real 0.0 score is data, not a missing week |
| 5 weeks, 1 not final | σ over 4 | The non-final week is excluded, not zeroed |

That last row is the one that will actually break in production. Write it now.

## M1 · Sleeper client · 16.0 h

Ends with a console app printing the real standings and asserting both invariants across a whole season. This is the milestone where the project first does something.

### T09 · DTOs and JSON configuration — 2.0 h

Wire types in `FantasyLeague.Sleeper`, mapped to the domain records from T03 by an explicit function.

```csharp
internal sealed record MatchupDto(
    int RosterId,
    decimal? Points,
    List<string>? Starters,
    List<string>? Players,
    Dictionary<string, decimal>? PlayersPoints,
    int? MatchupId);

internal static readonly JsonSerializerOptions Json = new()
{
    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    PropertyNameCaseInsensitive = true,
    NumberHandling = JsonNumberHandling.AllowReadingFromString,
};
```

> **Three quirks worth knowing before you debug them**
>
> **Empty starter slots are the string `"0"`.** A manager who left a slot empty produces a `"0"` in the `starters` array, not a null and not a missing entry. Filter it before looking anything up, or you will get a phantom player.
>
> **Defence player IDs are team abbreviations.** Skill players have numeric string IDs; a defence is `"DEN"`. Any code assuming an integer-parsable ID breaks on the first DEF.
>
> **Fields are absent, not null, for future weeks.** Request a week that has not happened and you get entries with no points at all. Make every numeric nullable in the DTO and decide the default at the mapping boundary.
>

#### Reference

- **.NET** — [System.Text.Json overview](https://learn.microsoft.com/dotnet/standard/serialization/system-text-json/)
- **Sleeper** — [Endpoint reference](https://docs.sleeper.com/)

### T10 · Typed HttpClient — 2.0 h

Register one typed client, with the handlers from T11 and T12 attached to it. Every Sleeper call in the system goes through this and only this.

```csharp
services.AddHttpClient<SleeperClient>(c =>
       {
           c.BaseAddress = new Uri("https://api.sleeper.app/v1/");
           c.Timeout = TimeSpan.FromSeconds(20);
           c.DefaultRequestHeaders.UserAgent.ParseAdd("ideal-points/1.0");
       })
       .AddHttpMessageHandler<RateLimitHandler>()
       .AddStandardResilienceHandler();
```

Six methods: `GetStateAsync`, `GetLeagueAsync`, `GetUsersAsync`, `GetRostersAsync`, `GetMatchupsAsync(week)`, `GetPlayersAsync`. Keep them thin — deserialize and return, no business logic.

#### Reference

- **.NET** — [IHttpClientFactory and typed clients](https://learn.microsoft.com/dotnet/core/extensions/httpclient-factory)

### T11 · Token-bucket handler — 2.0 h

The structural guarantee that nothing can exceed the rate limit, because nothing can reach Sleeper except through here. Add the `System.Threading.RateLimiting` package.

```csharp
public sealed class RateLimitHandler(RateLimiter limiter) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        using var lease = await limiter.AcquireAsync(1, ct);
        if (!lease.IsAcquired)
            throw new HttpRequestException("Local Sleeper rate limit exceeded");
        return await base.SendAsync(request, ct);
    }
}

// Registered as a singleton — one bucket for the whole process
services.AddSingleton<RateLimiter>(_ => new TokenBucketRateLimiter(
    new TokenBucketRateLimiterOptions
    {
        TokenLimit           = 50,
        TokensPerPeriod      = 50,
        ReplenishmentPeriod  = TimeSpan.FromMinutes(1),
        QueueLimit           = 20,
        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        AutoReplenishment    = true,
    }));
services.AddTransient<RateLimitHandler>();
```

Singleton is not optional. A per-request limiter is not a limiter.

#### Reference

- **.NET** — [System.Threading.RateLimiting](https://learn.microsoft.com/dotnet/api/system.threading.ratelimiting)

### T12 · Resilience policies — 1.0 h

`AddStandardResilienceHandler()` from `Microsoft.Extensions.Http.Resilience` gives you retry with exponential backoff and jitter, a circuit breaker, and a total timeout in one line, and it is built on Polly. Configure it rather than hand-rolling:

```
.AddStandardResilienceHandler(o =>
{
    o.Retry.MaxRetryAttempts = 3;
    o.Retry.UseJitter        = true;
    o.Retry.ShouldHandle     = args => ValueTask.FromResult(
        args.Outcome.Result?.StatusCode is HttpStatusCode.TooManyRequests
            or HttpStatusCode.ServiceUnavailable
        || args.Outcome.Exception is HttpRequestException);
});
```

Bound it. An unbounded retry against a rate limit is how you turn a slow afternoon into a ban.

#### Reference

- **.NET** — [HTTP resilience](https://learn.microsoft.com/dotnet/core/resilience/http-resilience)
- **Polly** — [Polly documentation](https://www.pollydocs.org/)

### T13 · Single-flight cache — 2.0 h

`HybridCache` does stampede protection for you — concurrent callers for the same key await one underlying fetch, which is precisely the property you need.

```csharp
services.AddHybridCache();

var matchups = await cache.GetOrCreateAsync(
    $"matchups:{leagueId}:{week}",
    async ct => await sleeper.GetMatchupsAsync(leagueId, week, ct),
    new HybridCacheEntryOptions { Expiration = TimeSpan.FromSeconds(30) });
```

Per-key TTLs from the design: 30 s for the current week's matchups, 5 min for state, 1 h for users and rosters, 24 h for league and players.

> **Watch out**
>
> Without stampede protection this design fails exactly when it matters. At the moment a 30-second entry expires under Sunday load, every concurrent request misses simultaneously and each issues its own fetch — a 2-per-minute design becomes a 200-per-minute one for the length of one round trip. If you hand-roll instead of using `HybridCache`, the mechanism you need is a per-key `SemaphoreSlim` with a double-check after acquiring.
>

#### Reference

- **.NET** — [HybridCache](https://learn.microsoft.com/aspnet/core/performance/caching/hybrid)

### T14 · Player dump job — 1.5 h

`GET /v1/players/nfl` returns roughly 5 MB covering every player in the league. Fetch it at most once a day and immediately throw most of it away.

```csharp
var raw = await sleeper.GetPlayersAsync(ct);      // ~5 MB
var slim = raw
    .Where(kv => kv.Value.FantasyPositions is { Count: > 0 })
    .ToDictionary(
        kv => kv.Key,
        kv => new PlayerRef(kv.Value.FullName ?? kv.Key,
                            kv.Value.Team,
                            kv.Value.FantasyPositions!));
```

Stream the response rather than buffering the whole string: `JsonSerializer.DeserializeAsync` over the response stream, not `GetStringAsync` followed by a parse. On a Consumption plan the memory ceiling is real.

> **Watch out**
>
> This call must never sit on a request path. A cold Function serving its first request should read the slim map from storage, not go and fetch five megabytes while a viewer waits.
>

### T15 · Season assembly — 2.5 h

The glue: for each week, fetch matchups, join to the player map, run the solver, produce `TeamWeek` records.

1. Resolve team names — `users[].metadata.team_name`, falling back to `display_name`, joined to rosters via `owner_id`.
2. For each week, for each matchup entry: build `RosteredPlayer` list from `players` × `players_points` × the slim player map.
3. Actual is `matchup.points` directly. Ideal is `Solver.Solve(...)`.
4. Assert both invariants as you go: `sum(starters_points) == points` and `ideal >= actual`.

A player in `players` with no entry in `players_points` scored zero — treat missing as `0m`, not as an error, or a bye week throws.

> **Watch out**
>
> Use `matchup.players` for the roster, never the current `/rosters` response. The matchup payload carries the roster *as it stood that week*; the rosters endpoint carries it as it stands today. Get this wrong and every waiver claim silently rewrites your league's history.
>

### T16 · Console harness and live verification — 3.0 h  ±

A `dotnet new console` project that takes a league ID, runs T15 across every week to date, and prints the table sorted by points.

```
wk 1-7   season 2026   final through wk 6

TEAM                    POINTS      s    IDEAL   EFF
--------------------------------------------------
Bijan Mustard            812.7   24.7    941.2   86%
Hurts Donut              798.2   31.4    905.6   88%
Waddle I Do              771.0   18.9    889.3   87%
...
--------------------------------------------------
invariants: starters==points OK   ideal>=actual OK
```

This is worth more than it looks. It is the first artefact that answers the actual question, it runs in a second with no cloud account, and it stays useful for the rest of the project as the fastest way to check whether a change broke the numbers.

#### Now assert the invariants over a real season

T02 confirmed the shapes; this is where they meet working code. Run both checks from §03 across every roster of every week and fail loudly on either:

- `sum(starters_points) == points`, within a cent. Catches a misaligned `starters` array and confirms you are reading the payload the way Sleeper writes it.
- `ideal >= actual`, always, with no exceptions. The started lineup is one of the assignments the solver considers, so a violation is a solver bug — almost always an eligibility rule from T04.

> **Watch out**
>
> The `ideal >= actual` invariant catches **restrictive** eligibility errors — a `FLEX` that accepts too little makes ideal fall below actual and the assertion fires. It does **not** catch permissive ones: a `FLEX` that wrongly accepts QB inflates ideal, the invariant still holds, and the number is quietly wrong forever. Read your slot map against the Q1 answer by eye as well; the assertion only covers one direction.
>
> Soft-estimated because this is where every assumption from T02 meets code that actually runs. Whatever the API does that nobody documented, it surfaces here.
>

## M2 · API and cloud foundation · 18.5 h

Deployed, reachable, still no database. Two of these tasks — the bicep template and the OIDC setup — are where a first-timer loses a weekend, so they are worth reading through before starting either.

### T17 · Functions host — 2.0 h

.NET isolated worker. `Program.cs` is where every service from M1 gets registered.

```csharp
var builder = FunctionsApplication.CreateBuilder(args);
builder.ConfigureFunctionsWebApplication();

builder.Services.AddApplicationInsightsTelemetryWorkerService();
builder.Services.ConfigureFunctionsApplicationInsights();

builder.Services.Configure<LeagueOptions>(
    builder.Configuration.GetSection("League"));
builder.Services.AddHybridCache();
builder.Services.AddSingleton<RateLimiter>(/* T11 */);
builder.Services.AddHttpClient<SleeperClient>(/* T10 */);
builder.Services.AddScoped<SeasonService>();

builder.Build().Run();
```

Configuration in `local.settings.json` for local runs, App Settings in Azure. The league ID binds as `League:CurrentLeagueId` — in Azure App Settings that is written `League__CurrentLeagueId` with a double underscore.

#### Reference

- **Azure** — [Isolated worker guide](https://learn.microsoft.com/azure/azure-functions/dotnet-isolated-process-guide)

### T18 · Season endpoints — 2.0 h

Two HTTP triggers, anonymous, GET only.

```csharp
[Function("GetSeason")]
public async Task<HttpResponseData> GetSeason(
    [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "season")]
    HttpRequestData req,
    CancellationToken ct)
{
    var q = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
    var season = int.TryParse(q["season"], out var s) ? s : (int?)null;
    // …
}
```

Serve from memory for now — T27 adds the database behind the same shape. The response is the payload from §09 of the design: per-team weekly arrays plus a server-computed `totals` object.

### T19 · Caching semantics — 1.5 h

Three behaviours, and the difference between the first two is the whole point of the season parameter.

```csharp
// live season
res.Headers.Add("Cache-Control", "public, max-age=30");
// completed season — never changes again
res.Headers.Add("Cache-Control", "public, max-age=31536000, immutable");

var etag = $"\"{season}-wk{currentWeek}-{asOf:s}\"";
res.Headers.Add("ETag", etag);
if (req.Headers.TryGetValues("If-None-Match", out var inm) && inm.Contains(etag))
    return req.CreateResponse(HttpStatusCode.NotModified);
```

An unknown or un-backfilled season returns **404 with the available list** in the body — never an empty payload, and never a synchronous backfill.

#### Reference

- **MDN** — [HTTP caching](https://developer.mozilla.org/docs/Web/HTTP/Caching)

### T20 · Bicep template — 4.0 h  ±

One `infra/main.bicep` at resource-group scope covering the storage account, Function App with a Flex Consumption or Consumption plan, Static Web App on Free, Log Analytics workspace, and Application Insights.

```bash
az group create -n rg-idealpoints -l eastus

az deployment group create \
  -g rg-idealpoints \
  -f infra/main.bicep \
  -p leagueId=<your-league-id> \
  --what-if          # inspect before committing

az deployment group create -g rg-idealpoints -f infra/main.bicep -p leagueId=…
```

Give the Function App a **system-assigned managed identity** in the template from the start — T26 needs it, and adding it later means a second deployment and a second SQL grant.

> **Why this estimate is soft**
>
> Bicep resource schemas are version-specific and the errors are unhelpful — a wrong `apiVersion` or a missing nested property produces a validation failure that names a path rather than a fix. Budget the time, use `--what-if` constantly, and start from the Azure Quickstart Templates rather than a blank file.
>

#### Reference

- **Azure** — [Bicep documentation](https://learn.microsoft.com/azure/azure-resource-manager/bicep/)
- **Azure** — [Deployment what-if](https://learn.microsoft.com/azure/azure-resource-manager/bicep/deploy-what-if)
- **Azure** — [Quickstart templates](https://github.com/Azure/azure-quickstart-templates)

### T21 · GitHub to Entra OIDC — 3.0 h  ±

Federated credentials let GitHub Actions authenticate to Azure with a short-lived token instead of a stored secret. Do this before writing any deploy workflow — retrofitting means rotating whatever you used first.

```bash
APP_ID=$(az ad app create --display-name "ideal-points-deploy" --query appId -o tsv)
OBJ_ID=$(az ad app show --id "$APP_ID" --query id -o tsv)
az ad sp create --id "$APP_ID"

SUB=$(az account show --query id -o tsv)
az role assignment create \
  --assignee "$APP_ID" --role Contributor \
  --scope "/subscriptions/$SUB/resourceGroups/rg-idealpoints"

az ad app federated-credential create --id "$OBJ_ID" --parameters '{
  "name": "gh-main",
  "issuer": "https://token.actions.githubusercontent.com",
  "subject": "repo:OWNER/REPO:ref:refs/heads/main",
  "audiences": ["api://AzureADTokenExchange"]
}'
```

Then in every workflow that touches Azure:

```yaml
permissions:
  id-token: write
  contents: read

steps:
  - uses: azure/login@v2
    with:
      client-id:       ${{ vars.AZURE_CLIENT_ID }}
      tenant-id:       ${{ vars.AZURE_TENANT_ID }}
      subscription-id: ${{ vars.AZURE_SUBSCRIPTION_ID }}
```

> **The failure you will hit**
>
> **The `subject` must match the workflow's trigger exactly.** A push to main is `repo:OWNER/REPO:ref:refs/heads/main`; a pull request is `repo:OWNER/REPO:pull_request`; a job using a GitHub environment is `repo:OWNER/REPO:environment:NAME`. These are different subjects and each needs its own federated credential. A mismatch produces an `AADSTS70021` "no matching federated identity record found" error that says nothing about which of the three you got wrong.
>
> Also easy to miss: without `id-token: write` in the workflow's `permissions`, no token is minted at all and the login fails for an entirely different reason.
>

#### Reference

- **GitHub** — [Configuring OIDC in Azure](https://docs.github.com/actions/deployment/security-hardening-your-deployments/configuring-openid-connect-in-azure)
- **Entra** — [Workload identity federation](https://learn.microsoft.com/entra/workload-id/workload-identity-federation)
- **Action** — [azure/login](https://github.com/Azure/login)

### T22 · ci.yml — 1.5 h

Runs on every pull request. No Azure credentials needed — this workflow never touches the cloud.

```yaml
name: ci
on: [pull_request]
jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with: { dotnet-version: '9.0.x' }
      - run: dotnet restore
      - run: dotnet build --no-restore -warnaserror
      - run: dotnet test --no-build --verbosity normal
      - run: dotnet format --verify-no-changes
      - uses: actions/setup-node@v4
        with: { node-version: '20', cache: 'npm', cache-dependency-path: web/package-lock.json }
      - run: npm ci --prefix web
      - run: npm run build --prefix web
```

#### Reference

- **GitHub** — [GitHub Actions documentation](https://docs.github.com/actions)

### T23 · deploy.yml — 2.5 h

Runs on push to main. Infrastructure first, then code.

1. `azure/login@v2` with OIDC (T21).
2. `az deployment group create --what-if` on pull requests, real deploy on main.
3. `dotnet publish` the Functions project, then `Azure/functions-action@v1` with `app-name` and `package`.
4. `npm run build --prefix web`, then `Azure/static-web-apps-deploy@v1` with the deployment token from the SWA resource.

The Static Web Apps deploy token is a real secret and lives in GitHub Actions secrets; everything else uses OIDC. Azure creates the workflow for you when you link a repository in the portal — take that generated file as a starting point and fold it into yours.

#### Reference

- **Action** — [Azure/functions-action](https://github.com/Azure/functions-action)
- **Azure** — [SWA build configuration](https://learn.microsoft.com/azure/static-web-apps/build-configuration)

### T24 · CORS and first deploy shakeout — 2.0 h  ±

The SPA is served from the SWA domain and calls the Function App on a different origin, so the Function App must allow it explicitly.

```bash
az functionapp cors add \
  -g rg-idealpoints -n func-idealpoints \
  --allowed-origins https://<your-swa>.azurestaticapps.net
```

Set this in bicep too, or the next infrastructure deployment removes it.

> **Watch out**
>
> Allowed origins must not have a trailing slash, and the SWA preview environments used by pull requests each get their own hostname. Either add a wildcard origin for previews or accept that the API only works from the production origin — which is fine, and arguably better, but decide rather than discover.
>
> This estimate is soft because "works locally, fails deployed" is its own category of problem: app settings that were in `local.settings.json` and never made it to Azure, a managed identity with no role assignment, a Functions runtime version mismatch. Budget the time for a first deployment; subsequent ones are minutes.
>

## M3 · Persistence · 17.0 h

Finalized weeks become permanent, and prior seasons become reachable. Two tasks here — the managed-identity connection and the migration step — are fiddlier than their hours suggest.

### T25 · EF Core model — 3.0 h

Four entities in `FantasyLeague.Data`, matching §08 of the design.

```
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

Use `HasPrecision` on every decimal. EF's default for `decimal` on SQL Server is `decimal(18,2)`, which is fine here, but being explicit stops a later provider change from silently rounding fantasy points.

```bash
dotnet tool install --global dotnet-ef
dotnet ef migrations add Initial --project src/FantasyLeague.Data \
                                 --startup-project src/FantasyLeague.Functions
```

#### Reference

- **.NET** — [EF Core documentation](https://learn.microsoft.com/ef/core/)
- **.NET** — [Migrations](https://learn.microsoft.com/ef/core/managing-schemas/migrations/)

### T26 · Azure SQL and managed identity — 3.0 h  ±

Provision the database on the free serverless offer, then connect to it without a password.

#### 1 · The database

Add the server and database to `main.bicep` with `useFreeLimit: true` and `freeLimitExhaustionBehavior: 'AutoPause'`. Auto-pause delay is required on the free offer; the design accepts the cold resume rather than working around it.

#### 2 · An Entra admin on the server

You cannot create the contained user until the server has an Entra administrator, and that administrator has to be you.

```bash
az sql server ad-admin create \
  -g rg-idealpoints -s sql-idealpoints \
  --display-name "$(az account show --query user.name -o tsv)" \
  --object-id "$(az ad signed-in-user show --query id -o tsv)"
```

#### 3 · Grant the Function App's identity

Connect to the database as that admin and run, using the Function App's *resource name* as the principal:

```
CREATE USER [func-idealpoints] FROM EXTERNAL PROVIDER;
ALTER ROLE db_datareader ADD MEMBER [func-idealpoints];
ALTER ROLE db_datawriter ADD MEMBER [func-idealpoints];
ALTER ROLE db_ddladmin   ADD MEMBER [func-idealpoints];  — only if the app runs migrations
```

#### 4 · The connection string

```
Server=tcp:sql-idealpoints.database.windows.net,1433;
Database=idealpoints;
Authentication=Active Directory Default;
Encrypt=True;
```

No password, no secret in configuration. `Microsoft.Data.SqlClient` plus `Azure.Identity` resolves the managed identity in Azure and your `az login` session locally.

> **Watch out**
>
> Grant `db_ddladmin` only if migrations run as the application. The design runs them from the pipeline instead (T31), in which case the *deploy identity* needs schema rights and the app itself should stay on reader and writer. Handing the runtime app schema-modification rights is a permanent capability bought for a once-per-deploy need.
>

#### Reference

- **Azure** — [Azure SQL free offer](https://learn.microsoft.com/azure/azure-sql/database/free-offer)
- **Azure** — [Serverless tier and auto-pause](https://learn.microsoft.com/azure/azure-sql/database/serverless-tier-overview)
- **Azure** — [Entra authentication for Azure SQL](https://learn.microsoft.com/azure/azure-sql/database/authentication-aad-overview)

### T27 · Read path — 2.0 h

Keep SQL off the hot path. On cold start, load every finalized week for the requested season into memory once; serve subsequent requests from there, merging the live current week from the T13 cache.

```csharp
// once per cold start, per season
var finalized = await db.WeekScores
    .Where(w => w.Season == season && w.IsFinal)
    .AsNoTracking()
    .ToListAsync(ct);
```

`AsNoTracking` matters: nothing here is ever written back through the context, and the change tracker is pure overhead on a read-only path.

The whole season is a few hundred kilobytes, so this is a small dictionary held in a singleton, invalidated when the finalize timer runs.

### T28 · Finalize timer — 2.0 h

Tuesday morning, after Monday night football has settled: write the completed week to SQL with `IsFinal = true`, then drop the in-memory season so the next request rehydrates.

```csharp
[Function("FinalizeWeek")]
public async Task Run([TimerTrigger("0 0 9 * * 2")] TimerInfo timer, CancellationToken ct)
{
    // 0 0 9 * * 2  =  09:00:00 UTC every Tuesday
}
```

> **Watch out**
>
> **Azure Functions NCRONTAB has six fields, not five.** The first is seconds. A five-field expression copied from a Linux crontab is interpreted with your minutes in the seconds position, and the job runs once a minute instead of once a week. This is the single most common Functions timer bug.
>
> Timers run in UTC unless `WEBSITE_TIME_ZONE` is set. Leave it unset and think in UTC — the NFL schedule shifts across daylight-saving boundaries anyway, so a fixed UTC time with an hour of slack beats an exact local one.
>

#### Reference

- **Azure** — [Timer trigger](https://learn.microsoft.com/azure/azure-functions/functions-bindings-timer)

### T29 · Season chain walk — 2.0 h

On the daily timer, start at the configured league ID and follow `previous_league_id` backwards, upserting a `League` row for each hop.

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

The `seen` set is not paranoia — a malformed chain that points at itself would otherwise loop forever against a rate-limited API. Sleeper also uses the string `"0"` as well as null to mean "no previous league", so test both.

Merge `League:SeasonOverrides` from configuration after the walk, for seasons the chain cannot reach.

### T30 · Historical backfill job — 2.5 h

An explicitly-triggered function — not a timer, not a request path — that takes a season, walks weeks 1 through 18, and writes every `WeekScore` with `IsFinal = true`.

Roughly 21 calls per season. It goes through the T11 token bucket like everything else, so it self-throttles and takes about half a minute. Make it idempotent: upsert on the composite key so re-running after a solver fix is safe and expected.

> **Watch out**
>
> Do not wire this to run automatically when someone requests a missing season. A visitor would wait twenty seconds for a page, and a crawler hitting `?season=1998` repeatedly would generate bursts against Sleeper for seasons that do not exist. The API returns 404 with the available list; backfill is something you run deliberately.
>

### T31 · Migrations in the pipeline — 2.5 h  ±

The GitHub runner has a different IP on every run, so the SQL firewall has to be opened for it and closed again afterwards — including when the migration fails.

```bash
- name: Open firewall for runner
  run: |
    IP=$(curl -s https://api.ipify.org)
    echo "RUNNER_IP=$IP" >> $GITHUB_ENV
    az sql server firewall-rule create \
      -g rg-idealpoints -s sql-idealpoints -n gh-runner \
      --start-ip-address "$IP" --end-ip-address "$IP"

- name: Apply migrations
  run: |
    dotnet ef database update \
      --project src/FantasyLeague.Data \
      --startup-project src/FantasyLeague.Functions

- name: Close firewall
  if: always()
  run: az sql server firewall-rule delete -g rg-idealpoints -s sql-idealpoints -n gh-runner
```

> **Watch out**
>
> `if: always()` on the close step is the whole point. Without it, a failed migration leaves the database open to a public IP that will shortly belong to someone else's CI run.
>
> This is soft-estimated because the identity running `dotnet ef` needs both the firewall grant and schema rights on the database, and the failure modes look identical from the log — a connection timeout and a permission denial both surface as "cannot connect".
>

## M4 · The page · 24.0 h

The first milestone anyone else in the league can look at, and the first that will generate opinions. The table is the primary object; the chart is the thing people will play with.

### T32 · Frontend scaffold — 2.0 h

```bash
npm create vite@latest web — --template react-ts
cd web && npm i
npm i d3-scale d3-shape d3-array
npm i -D @types/d3-scale @types/d3-shape @types/d3-array
```

Install the D3 sub-packages, not the `d3` meta-package. You need scales, path generators and `extent`; you do not need selections, transitions, geo projections or force simulations, and pulling the whole bundle drags in the DOM layer you have decided not to use.

Add `staticwebapp.config.json` with a SPA fallback so client-side routes survive a refresh:

```jsonc
{ "navigationFallback": { "rewrite": "/index.html" } }
```

#### Reference

- **Vite** — [Vite guide](https://vite.dev/guide/)
- **Azure** — [SWA configuration](https://learn.microsoft.com/azure/static-web-apps/configuration)

### T33 · Design system — 3.0 h

CSS custom properties for the whole palette on `:root`, redefined under `@media (prefers-color-scheme: dark)`. Every colour used anywhere comes from a token.

Design the dark theme rather than inverting the light one — a naive inversion produces a muddy accent and unreadable mid-tones. Check contrast on both grounds, particularly for the chart's twelve team colours, which is the hardest part of the palette to get right in two themes.

Set `font-variant-numeric: tabular-nums` on every cell containing a figure. Points totals that do not line up column-wise are the difference between a table you can scan and one you have to read.

### T34 · Standings table — 3.0 h

Four columns: team, points, σ, ideal. Sorted descending on points, because that is what decides the league.

- Mark the sort column visually — it is not obvious that points decides this league rather than a win-loss record.
- Show the gap to first place. A standings table whose story requires arithmetic is not finished.
- σ renders as an em dash below four final weeks, and its column header carries the week range: **"σ · through wk 6"** while the points column already includes week 7.
- Do not give σ the same bar or heat encoding as the two point columns — lower is better there and the shared encoding would read backwards.

Use a real `<table>` with `<th scope="col">`, inside a container with `overflow-x: auto`. Screen readers and narrow phones both benefit, and neither is served by a grid of divs.

### T35 · Inline range mark — 2.5 h

A small SVG in the σ cell giving the number a shape: a rule from the team's minimum to maximum weekly score, a shaded band at ±σ around the mean, and a tick at the mean itself.

```
<svg viewBox="0 0 140 16" width="140" height="16" role="img"
     aria-label={`${min} to ${max}, mean ${mean}, sigma ${sigma}`}>
  <line x1={s(min)} x2={s(max)} y1="8" y2="8" stroke="currentColor" opacity=".35" />
  <rect x={s(mean - sigma)} width={s(mean + sigma) - s(mean - sigma)}
        y="4" height="8" fill="var(--accent)" opacity=".22" />
  <line x1={s(mean)} x2={s(mean)} y1="2" y2="14" stroke="var(--accent)" strokeWidth="2" />
</svg>
```

The scale `s` must be shared across every row — computed from the league-wide minimum and maximum, not per team — or the marks are not comparable, which is their only purpose.

### T36 · D3 line chart — 4.0 h  ±

D3 computes; React renders. No `select`, no `append`, no `enter`.

```
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

Axes are the one place the split gets awkward, because `axisBottom` wants a DOM node. Do not reach for a ref — call `x.ticks()` and map the values to your own `<line>` and `<text>` elements. It is a dozen lines and keeps every node under React's control.

> **Watch out**
>
> Leave room in the `viewBox` for the outermost tick labels. An axis label at the left edge of the drawing area is clipped by the SVG boundary, which is why the margin object exists — the scale's range starts at `m.left`, not at zero.
>
> Soft-estimated because D3-inside-React is a genuine mental shift if you have only seen D3's own idiom. The payoff is that everything afterwards — hover, filtering, mode switching — is ordinary React state.
>

#### Reference

- **D3** — [d3-scale](https://d3js.org/d3-scale)
- **D3** — [d3-shape](https://d3js.org/d3-shape)

### T37 · Chart controls — 3.0 h

- **Season** — populated from `/api/seasons`, never a hardcoded list, so a new year appears the day the chain walk finds it.
- **Teams** — multi-select, all on by default, colour-coded to match the lines.
- **Metric** — single-select: cumulative points, cumulative ideal, or expanding σ. Single rather than multi because σ (≈25) and cumulative points (≈800) cannot share a y-axis without σ flattening into a line along the bottom. Switching metric switches the axis.
- **Display mode** — cumulative, per-week, or vs. league median, for the two point metrics.

Keep control state in the URL query string. It costs almost nothing and makes "look at what happened to me in week 9" a link someone can paste into the league chat, which is the whole social point of the site.

### T38 · Hover interaction — 2.0 h

Hovering a team — in the chart or in the legend or in the table — raises that line and mutes the rest. Twelve colours is not a legend; asking a reader to trace one out of twelve is not a design.

Implement as a single `hoveredRosterId` state driving opacity and stroke width. Raise the hovered path to the end of the SVG's child list so it draws on top; in React that means sorting the series array with the hovered one last, not manipulating the DOM.

Provide a keyboard equivalent — arrow keys through the legend with the same highlight on focus — or the interaction only exists for mouse users.

### T39 · Polling — 2.0 h

```
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

Firing `tick` on `visibilitychange` as well as on the interval means returning to the tab refreshes immediately rather than waiting up to thirty seconds — the difference between a page that feels live and one that feels stale.

Send `If-None-Match` with the stored ETag; a 304 costs almost nothing on either side.

### T40 · Responsive and accessibility — 2.5 h

- The table scrolls horizontally in its own container; the page body never scrolls sideways.
- Every interactive control has a visible `:focus-visible` state and is reachable by keyboard.
- The chart carries a `role="img"` and an `aria-label` summarising what it shows, because an SVG of twelve paths is otherwise silent to a screen reader.
- Respect `prefers-reduced-motion` for any line transitions.
- Check contrast for the team palette in both themes — this is where a twelve-colour scale usually fails.

#### Reference

- **W3C** — [ARIA authoring practices](https://www.w3.org/WAI/ARIA/apg/)

## M5 · Modes, ops and docs · 9.5 h

Deliberately last. These are cheap, taste-driven, and much easier to judge against a season of real data than against three weeks of it.

### T41 · Display modes — 2.0 h

Three transforms over the same weekly arrays:

```
cumulative:  running sum of weekly values
perWeek:     the weekly values themselves
vsMedian:    running sum of (team[w] - median of all teams in week w)
```

The median mode changes no ranking — the median sum is an identical constant for every team — but it removes the league-wide trend that dominates the raw chart. Twelve near-parallel ascending lines are unreadable; the same twelve diverging from a zero baseline are legible at a glance. Draw a zero rule when this mode is active.

### T42 · Sigma variants — 2.0 h

**Expanding** (default): σ over weeks 1…w, converging as the season settles. **Rolling**: σ over the last four weeks, showing whether a team is becoming steadier or wilder — more interesting late, close to meaningless before week eight.

**Raw σ vs coefficient of variation.** σ is scale-dependent: a team averaging 140 with σ = 25 is relatively steadier than one averaging 90 with σ = 22, and the raw numbers say the opposite. CV is the fair comparison and is one division away. The table keeps σ because it is the number people say out loud; the chart offers both.

Both variants render as an em dash until enough final weeks exist — four for expanding, four for rolling.

### T43 · Loading and error states — 2.0 h

| State | What the reader sees |
|---|---|
| Cold start, SQL resuming | A skeleton table with an honest line: waking the database, this takes up to a minute. Not a spinner that implies seconds. |
| Season not backfilled | The 404's available-seasons list, rendered as links. Never an empty table. |
| Fewer than four final weeks | Em dashes in the σ column with the reason on hover, not a zero and not a blank. |
| Sleeper unreachable | Finalized weeks still render from SQL; the current week is marked stale with its last-known timestamp. |

That last row is the design paying off: because finalized weeks never depend on Sleeper, an outage degrades the site rather than breaking it. Make that visible instead of failing whole.

### T44 · Cost guardrails — 2.0 h

Three settings that are the difference between a free hobby project and an unpleasant invoice. Configure them; do not assume them.

1. **Application Insights daily cap.** Set a 1 GB/day ingest cap on the component. Telemetry is the likeliest thing to quietly cost money here.
2. **Azure SQL free-limit behaviour.** Confirm it is set to auto-pause on exhaustion, not to continue and bill. One runaway job should cost you a paused database.
3. **A budget alert** on the resource group at a threshold you would want to hear about — a few dollars is enough to be informative when the expected bill is zero.

#### Reference

- **Azure** — [Daily cap on log ingestion](https://learn.microsoft.com/azure/azure-monitor/logs/daily-cap)
- **Azure** — [Create a budget alert](https://learn.microsoft.com/azure/cost-management-billing/costs/tutorial-acm-create-budgets)

### T45 · README and runbook — 1.5 h

Short, and mostly one procedure. Write it now, while you remember why things are the way they are.

#### The August rollover

1. Create the new season in Sleeper using the roll-over flow, so `previous_league_id` is set.
2. Update `League__CurrentLeagueId` in the Function App's settings to the new league ID.
3. Wait for the daily chain walk, or trigger it manually.
4. Confirm the new year appears in `/api/seasons`.

That is the entire annual maintenance burden, which is the point of D8.

Also record: how to run the backfill job for a season, how to re-run it after a solver fix, and the three cost settings from T44 with a note to re-check them after any infrastructure change — a `main.bicep` deployment can quietly reset a portal-set value.

## M6 · Positional radar · 10.0 h

One team’s average output per starting slot against the league average at the same slot, on axes taken from the league’s own rules. Last because it rests on a property of Sleeper’s payload that T02 has to confirm first.

### T46 · Slot attribution — 2.5 h

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

### T47 · League reference and ratios — 1.5 h

1. For each team and slot, average the weekly per-slot values over regular-season weeks that produced an attribution.
2. For each slot, average those season values across all teams — that is the reference circle.
3. Divide. Team season value over league season value gives the plotted ratio, centred on 1.0.

> **Watch out**
>
> **Average first, then divide.** Computing a ratio per week and averaging the ratios looks equivalent and is not. On the week where the entire league’s kickers scored two points between them, the denominator is near zero and one ordinary Sunday becomes a permanent distortion on the K axis.
>
> Guard the degenerate denominator anyway: if a league season value is zero — every team started nobody at that slot all year — the ratio is undefined. Return null for that axis and draw it at the reference circle with a marker, rather than dividing by zero or quietly plotting 1.0 as though it were measured.
>

### T48 · Slot block in the API — 1.0 h

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

### T49 · Radar chart — 3.5 h  ±

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

### T50 · Diverging bar alternative — 1.5 h

The same numbers on a linear axis, as a toggle beside the radar. Horizontal bars from a zero centre, one row per slot, in `slotOrder`.

This is the form the data actually asks for — a signed deviation from a baseline across a handful of categories is a diverging bar chart. Bars share one linear scale, so values are directly comparable and nothing is distorted by area or by axis ordering. Use a two-hue diverging pair with a neutral midpoint at zero, and direct-label each bar with its value.

It exists so the radar does not have to be accurate about magnitude. Keep the radar as the default anyway: this is a page for a fantasy league, and “what shape is my team” is a question people genuinely want answered. Two views, one data set, and no argument about which one is correct.

## M7 · Motion and polish — optional · 7.5 h

Optional throughout. Cut the whole milestone and the site still does its job — which is the test every task here should pass before you spend an hour on it.

### T51 · Motion setup — 1.0 h

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

#### Reference

- **Motion** — [Motion for React](https://motion.dev/docs/react)

### T52 · Table layout animation — 1.0 h

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

### T53 · Chart reveal and transitions — 2.0 h  ±

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

### T54 · Section transitions — 1.0 h

The View Transitions API costs nothing and degrades to an instant swap:

```
function go(next: Section) {
  if (!document.startViewTransition) return setSection(next);
  document.startViewTransition(() => flushSync(() => setSection(next)));
}
```

Supported in Chromium and Safari; check current Firefox support before leaning on it, and note that the guard above means you never have to care. Give shared elements a `view-transition-name` and they morph between sections rather than crossfading.

Motion’s `AnimatePresence` does the same job with more control and more code. Start with the native API and reach for `AnimatePresence` only if you need exit animations it cannot express.

#### Reference

- **MDN** — [View Transition API](https://developer.mozilla.org/docs/Web/API/View_Transition_API)

### T55 · Football loader — 2.5 h

A football arcing back and forth while the site loads. Charming for four seconds, maddening for forty — so the whole design is about which wait it is bound to.

#### Three timing bands

| Wait | What shows | Why |
|---|---|---|
| 0 – 400 ms | Nothing at all | The warm path is this fast. A loader that flashes for 80 ms is worse than none. |
| 0.4 – 4 s | The football | The band where a mascot is genuinely delightful. |
| 4 s + | Ball settles, copy appears | The cold-start case from D7. Past ten seconds people want an explanation, not a loop. |

#### It goes in index.html, not in React

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

#### Spin without rotating: the comic-book trick

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

#### Narrow screens: throw it straight up

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

#### Reference

- **MDN** — [offset-path](https://developer.mozilla.org/docs/Web/CSS/offset-path)
- **MDN** — [prefers-reduced-motion](https://developer.mozilla.org/docs/Web/CSS/@media/prefers-reduced-motion)
