# Lessons

Rules written after a correction, so the same mistake does not recur.
One entry per lesson: what happened, and the rule that prevents it.

<!--
Format:

## <short title>
**What happened:** …
**Rule:** …
-->

## `dotnet new` and `func init` are different tools

**What happened:** The T01 scaffold used
`dotnet new func --worker-runtime dotnet-isolated`, which fails with
`'--worker-runtime' is not a valid option`. That flag belongs to `func init`
from Azure Functions Core Tools. The `dotnet new func` template shipped by
`Microsoft.Azure.Functions.Worker.ProjectTemplates` is already the
isolated-worker template — the model is baked into the package, not selected by
a flag.

**Rule:** Before putting a CLI invocation in the docs, check its options with
`--help` rather than carrying flags across from a neighbouring tool. Azure has
several pairs like this (`dotnet new func` / `func init`, `az functionapp` /
`func azure functionapp publish`) where the flags are not interchangeable.

## A template's explicit property beats `Directory.Build.props`

**What happened:** `dotnet new func` defaults to `net8.0` and writes an explicit
`<TargetFramework>` into the generated `.csproj`. `Directory.Build.props` is
imported before the project's own `PropertyGroup`, so the project wins — the
Functions project would have built as net8.0 against four net9.0 siblings, with
no warning.

**Rule:** After scaffolding any project, check which properties the template
wrote locally and whether they shadow the root props file. Either pass the
right value at generation time (`-F net9.0`) or delete the generated line so it
inherits. Verify with
`dotnet msbuild <proj> -getProperty:TargetFramework`.

## The manual's shell blocks are POSIX — use Git Bash

**What happened:** T01's `find . -name "*.csproj" -exec dotnet sln add {} +` was
run in `cmd`, where `find` is Windows' text-search `find.exe`. It failed with
`File not found - -NAME`. The same applies to most of the manual: T02 alone uses
`curl`, `jq`, `mkdir -p`, `for w in 1 2 3` and `$L`.

**Rule:** Run the manual's shell blocks under Git Bash. When a command has to
work in another shell, give the actual equivalent and verify it, rather than
assuming a Unix idiom ports. `dotnet sln add **/*.csproj` is not a fallback —
the CLI does no globbing of its own.

## `dotnet new sln` produces `.slnx` on the .NET 10 SDK

**What happened:** SDK 10.0.400 defaults `dotnet new sln` to the XML solution
format, so the repo gets `FantasyLeague.slnx`. Harmless for the dotnet CLI, but
it would silently break anything that globs `*.sln` — a CI step, a deploy
script, an older Visual Studio.

**Rule:** When a CI or deploy step needs the classic format, create it with
`dotnet new sln -f sln`. Either way, check what the SDK actually wrote before
referencing the filename anywhere else.

## Every `dotnet new` template writes its own `TargetFramework`

**What happened:** With `Directory.Build.props` correctly set to `net9.0`, the
solution still ended up mixed — `dotnet new classlib` and `dotnet new xunit` on
the .NET 10 SDK each wrote `<TargetFramework>net10.0</TargetFramework>` into
their own csproj, shadowing the props file. Four projects at net10.0, the
Functions project at net9.0. It builds only while no project references another;
the first `ProjectReference` from net9.0 Functions to a net10.0 library fails
with NU1201.

**Rule:** `Directory.Build.props` sets defaults, not overrides — it is imported
*before* the project's own `PropertyGroup`, so any property the template writes
wins. After scaffolding, delete the `<TargetFramework>` line from every
generated csproj so they all inherit, and confirm with:

```bash
for p in src/*/ tests/*/; do n=$(basename "$p"); \
  printf '%-32s ' "$n"; dotnet msbuild "$p$n.csproj" -getProperty:TargetFramework; done
```

Check this *before* adding project references, not after the first NU1201.

## FluentAssertions 8.x is not free for commercial use

**What happened:** Reaching for FluentAssertions by reflex would have pulled 8.x,
which moved from Apache-2.0 to the Xceed Community Licence — free for
non-commercial use only. The nuspec shows it plainly: 7.2.0 declares
`<license type="expression">Apache-2.0</license>`, 8.10.0 declares
`<license type="file">`.

**Rule:** Pin **7.2.0** for a permissive licence, or switch to
**AwesomeAssertions** (an Apache-2.0 fork of FA 7, currently 9.x) if ongoing
updates matter. Check the licence field of any assertion, mocking or test
package before adding it — this class of package changed terms several times
across the ecosystem, and a licence is much cheaper to check than to unpick.

## Expression trees cannot contain `is` patterns

**What happened:** `startable.Should().NotContain(s => s.Code is "BN" or "IR")`
failed to compile with CS8122. FluentAssertions takes an
`Expression<Func<T,bool>>` for collection predicates so it can print the
predicate in the failure message, and expression trees do not support pattern
matching.

**Rule:** Inside a FluentAssertions collection predicate, use `==` and `||`
rather than `is`/`or`. The pattern syntax is fine everywhere a plain lambda is
accepted; it is only expression trees that reject it.

## Parent pointers go stale over a rolling-array DP

**What happened:** The ideal-lineup solver returned correct point totals but a
corrupt lineup — the same player listed twice, and the lineup's points not
summing to the total. `dp[m2]` was computed from the previous row's `dp[mask]`,
while the reconstruction walked `from[mask]` from the final row. A later player
improving `dp[mask]` silently rewrote `from[mask]`, so the chain described a
different history than the value was built from.

**Rule:** When a DP compresses to one row, parent pointers must not survive
across rows. Either keep the full `from[i][mask]` table and walk *i* downwards,
or store the accumulated answer itself per state — here a `long` bitmask of
chosen player indices, written in the same row the value came from. Never mix a
value from row *i−1* with a pointer read from row *n*.

**And test it:** the value being right hid this completely. Assert that the
reconstructed answer is *consistent with* the value — no duplicates, correct
sum, within bounds — not just that the number matches. A seeded fuzz against
brute force found it in seconds; the hand-written examples all passed.

## Fixtures catch wrong assumptions in tests, not just in code

**What happened:** A test asserted every roster could fill all eleven slots in
week 1, reasoning there are no byes in week 1. It failed — roster 4 rosters no
kicker at all, roster 5 no defence. The solver was right and the test was wrong.

**Rule:** When a fixture-based test fails, check the fixture before the code. Real
data encodes facts nobody thought to state — here that a six-team league is
thin enough for managers to be short at a position outright, which is also why
the `"0"` empty-starter case is routine rather than theoretical.

## A test harness can hide the same bug the code must not have

**What happened:** The fixture tests built rosters with
`.Where(id => id != "0" && positions.ContainsKey(id))`. Any player missing from
`player-positions.json` was silently skipped, which lowers ideal points with the
test still green — exactly the silent-undercount failure A3 forbids in the
solver, relocated into the tests. It would have bitten the first time a waiver
claim brought in a player the map did not cover.

**Rule:** In a fixture-driven test, unresolved reference data is a failure, not a
filter. Assert the lookup succeeds and say in the message how to refresh it.
Reserve `Where` for cases the data genuinely encodes — `"0"` really does mean an
empty starter slot, so filtering that is right.

## Regenerate fixtures over the union, not per run

**What happened:** `player-positions.json` was first built from the players on
week 1's rosters. Rosters change on waivers, so a map captured once goes stale
and later weeks reference players it does not contain.

**Rule:** A derived fixture must be rebuilt from every captured week on disk, not
only the ones fetched this run. `scripts/capture_fixtures.py` globs
`wk*-matchups.json` for exactly this reason.

## Registering a handler in DI does not put it in the HTTP pipeline

**What happened:** `AddTransient<RateLimitHandler>()` was called, but
`AddHttpMessageHandler<RateLimitHandler>()` never was, so the token bucket was
not in the chain and every Sleeper request bypassed it. Nothing failed: the
handler simply sat in the container unused. `AddSleeperClient()` returns the
`IHttpClientBuilder` that represents the pipeline, and the call site dropped it.

**Rule:** Both calls are needed and they mean different things —
`AddTransient` is the supplier, `AddHttpMessageHandler` is the customer. Assemble
the pipeline inside the extension method that owns the client, so it cannot be
registered without its handlers, and assert the behaviour rather than the
registration: a test with an empty bucket must see zero requests reach the
primary handler.

## Handler order decides whether retries are rate limited

**What happened:** The manual adds the rate limiter before the resilience
handler. Handlers run first-added-outermost, so that puts the retry loop *inside*
the bucket: one token, up to four network requests. Measured, not assumed.

**Rule:** Put the retrying strategy outermost and the limiter inside it, so every
real attempt spends a token. The trade-off is that token-wait time then counts
against the attempt timeout, which only matters if the bucket is routinely empty
— and if it is, the budget is wrong anyway.

## AddStandardResilienceHandler disables HttpClient.Timeout

**What happened:** Time was spent worrying about layering `HttpClient.Timeout`
against the pipeline's `TotalRequestTimeout`. The resilience handler settles it
itself: it sets `HttpClient.Timeout` to `InfiniteTimeSpan`, and **overrides an
explicit value** — a client configured with 20 seconds still comes out infinite.

**Rule:** With the standard resilience handler in the chain, do not configure
`HttpClient.Timeout`; it will be ignored. Tune `TotalRequestTimeout` and
`AttemptTimeout` instead, and check assumptions about a library's behaviour by
running it rather than reasoning from its documentation.

## `async void` kills the process, it does not just lose the exception

**What happened:** `PlayerMapRefresher.RefreshAsync` was declared `async void`.
Demonstrated: the caller's `try`/`catch` never fires, the caller continues
immediately, and the exception surfaces as an unhandled thread-pool exception
that **terminates the process** — the line after the call never ran. A timer
invocation would have reported success while the worker died.

**Rule:** `async void` is for UI event handlers and nothing else. Anything that
awaits returns `Task`. Check the return type of every `async` method that is
called rather than awaited.

## The null-forgiving operator is load-bearing more often than it looks

**What happened:** I called a `players!` redundant because it followed a null
check. It was not: it was suppressing CS8620 caused by
`Dictionary<string, SleeperPlayerDto?>` being passed where
`IReadOnlyDictionary<string, SleeperPlayerDto>` was expected. Removing it broke
the build.

**Rule:** Before deleting a `!`, work out which warning it silences. The right
fix is usually the signature it is papering over — here, tightening the client's
return type, which removed the need for it at every call site.

## A stub must hand out a fresh HttpResponseMessage per call

**What happened:** A scripted test handler returned the same
`HttpResponseMessage` instance twice and the second read threw
`ObjectDisposedException`: `HttpClient` disposes response content once it has
been read.

**Rule:** Script test responses as data — status plus body — and construct the
`HttpResponseMessage` inside `SendAsync`.
