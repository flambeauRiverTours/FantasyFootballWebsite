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

## A fixture value is a placeholder, even when the real one is to hand

**What happened:** `HostRegistrationTests.cs` bound the live Sleeper league id as
its test value. It read as perfectly ordinary — it is the value the code will
actually see — and survived into six commits before a grep for the configured
ids caught it.

**Rule:** Never put a real id, key or endpoint into a test, even one whose
exposure seems harmless. If the test constrains the shape (nineteen digits
here), satisfy the shape with an obviously synthetic value. Gate it in CI by
matching the configured secrets exactly, rather than by a pattern that a
placeholder would also trip.

## A harness that primes state hides a host that does not

**What happened:** `SeasonService` required the player map to have been
refreshed, and threw otherwise. The console harness refreshed it explicitly
before building, so every local run passed. The Functions host never did, and
the first deployed request — and every one after it — returned 500.

**Rule:** When a service depends on state something else must prime, check every
host that resolves it, not just the one being run. Better still, make the
service acquire what it needs itself: a precondition that only one caller
satisfies is a bug waiting for a second caller.

## A shell construct in the wrong shell succeeds at the wrong thing

**What happened:** `gh secret set NAME --body "$(az ... )"` was run in `cmd`,
where `$( )` is literal text rather than command substitution. The secret was set
to the 105-character command string instead of the API key. Nothing failed —
`gh` reported success — and the mistake surfaced days later as a secret-scanning
hit on the document that contained the same command.

**Rule:** A command whose correctness depends on the shell must say so, and must
carry a check that fails loudly in the wrong one. Capture into a variable, assert
something about it (`[ "${#TOKEN}" -ge 100 ]`), and only then write it. The same
applies to a trailing carriage return: `$( )` strips a trailing newline but not a
``, so pipe through `tr -d '
'` on Windows before storing anything.

**Corollary:** this is the third failure in this project caused by a masked value
making an error unactionable. Any gate that compares against a secret should
report what it can safely say — length, line numbers, the variable's name — and
never just "something matched".


## CSS that removes a box removes everything attached to the box

**What happened:** I proposed `display: contents` on `.masthead .panel-inner` to
flatten a wrapper into a flex row. It worked — and silently discarded that
element's `max-width: 64rem`, `margin-inline: auto` and side padding, so the
masthead ran full-bleed with a flush-left title while every panel below it stayed
centred at 64 rem. The reported symptom was "padding in the masthead is off".

**Rule:** `display: contents` is not a layout adjustment, it is a deletion of the
element's box. Anything that box was carrying — width constraints, padding,
background, border — goes with it. To make a wrapper's children into a row, put
the row on the wrapper. Reach for `display: contents` only when the wrapper is
purely structural and styles nothing.

## Measure before diagnosing a layout bug

**What happened:** A horizontal scrollbar was reported as "width computation of
table is off". Two plausible table-level causes came to mind. Measuring in the
browser showed `panel-inner w=807` against `clientWidth 775`, while the table was
already at 775 — the table had been collapsing correctly the whole time, and the
overflow was `width: 100%` plus padding under a missing `box-sizing` reset.

**Rule:** For any overflow, list the elements whose `getBoundingClientRect().right`
exceeds `documentElement.clientWidth` before forming a theory. The first
overflowing element in document order is the cause; everything after it is a
symptom. Guessing from a screenshot picks the most visually prominent element,
which is usually one of the symptoms.

**Corollary:** a stylesheet with no `box-sizing: border-box` reset has a latent
overflow in every `width: 100%` element that also has padding. Check for the
reset before debugging any individual one.

## `Function` as a type is an opt-out from type checking

**What happened:** A prop typed `scalingFunction: Function` accepted any
arguments and returned `any`. The only error the compiler could still report was
a prop-name mismatch; the arithmetic built on its `any` return was unchecked.

**Rule:** Never `Function`. Type a callback by its signature — `(v: number) =>
number` — or by the library's own type. The same applies to `object` and to a
bare `any` in a prop: a single loose type at a component boundary silently
unchecks everything downstream of it.

## Truthiness is not a null check on a number

**What happened:** `totals.min ? totals.min : -1` was written to handle a
nullable figure. `0` is falsy, so a legitimate zero score would have been
replaced by a sentinel and drawn off the edge of the chart.

**Rule:** For a nullable number use `??`, never `?:` or `||`. Better, make the
null unreachable: if a type guard already exists, export the narrowed type and
have the component demand it, so there is no fallback to get wrong.

## `! cmd` under `set -e` is not a guard

**What happened:** `! grep -q "/src/" dist/index.html` was used to assert the
built bundle does not reference source. POSIX exempts a command prefixed with
`!` from `errexit`, so when the grep *succeeds* — source is present, the exact
regression the line exists to catch — the script continues and the step exits 0.
It only works when the `!` command is the last statement, whose status becomes
the step's status. Two of the four such guards in this repo were inert;
shellcheck flags it as SC2251 and had never been run.

**Rule:** Write negative assertions as `if cmd; then echo "::error::…"; exit 1; fi`.
Prove a new gate fails by feeding it the bad input once, before trusting it.

**Corollary:** run `actionlint` with `shellcheck` on `$PATH`. Without shellcheck
present, actionlint silently disables the rule and reports zero errors, so a
green run means less than it looks.

## `environment:` on a job changes its OIDC subject

**What happened:** Adding `environment: production` to the deploy's `infra` job
broke `azure/login` with `AADSTS700213`. GitHub replaces the ref segment of the
subject claim rather than appending to it — `…:ref:refs/heads/main` became
`…:environment:production` — and no federated credential matched. Jobs in the
same workflow with no `environment:` were unaffected, which is why only one of
three jobs failed.

**Rule:** A federated credential authorises one subject shape. Adding
`environment:`, changing a branch, or moving a trigger between `push` and
`pull_request` each mint a different subject and each need their own credential.
Before changing any of those, read the intended subject out of the workflow log —
`azure/login` prints it under "Federated token details" — and check it against
`az ad app federated-credential list` rather than assuming the existing one
covers it.

## Workspace-based App Insights caps on the workspace

**What happened:** A cost check read `properties.DailyCapGb` off the Application
Insights component and got `100`, which looked like the 0.16 GB cap had never
been applied. The bicep was correct: a workspace-based component ingests through
Log Analytics, so the cap is `workspaceCapping.dailyQuotaGb` on the workspace.
The component's own `DailyCapGb` is the classic-mode default and controls
nothing.

**Rule:** For any Azure setting that exists on two resources in a chain, verify
on the resource that actually enforces it, not the one that names it. Reading the
value back is not proof unless you read it from the right place.

**Corollary:** `az ... -o tsv` lowercases booleans and emits CRLF on Windows.
`useFreeLimit` comes back as `true\r`, so a comparison against `True` fails
against a correctly configured resource. Pipe through `tr -d '\r'` and compare
case-insensitively.

## A sticky panel taller than one screen hides its own overflow

**What happened:** `ChartPanel`'s content (sigma window/scale controls, a wider
team-chip row, a tall aspect-ratio chart) grew past one viewport's height.
`.panel` is `position: sticky; top: var(--masthead-h)` with no `max-height`, so
nothing clipped it and nothing looked wrong in the CSS. In the browser: the
panel pins at `top` the moment it's reached, and stays pinned — while pinned,
the page scrolling does not reveal any more of a sticky element's own box, only
the top slice that fits between `top` and the viewport bottom. Continued
scrolling doesn't creep through the rest of the panel; it does nothing
visually until the panel's bottom passes the pin point, at which moment it
releases all at once into the next panel. Net effect: the bottom portion of an
over-height sticky panel is never on screen at any scroll position — not
clipped, not letterboxed, genuinely unreachable.

Ruled out first, both wrongly suspected: `#chart .chart { max-height: 70svh }`
(confirmed via a `getBoundingClientRect` probe that the SVG's own
`preserveAspectRatio="xMidYMid meet"` default letterboxes rather than crops —
harmless), and sticky-panel overlap (confirmed via a plain two-panel repro that
normal document flow never overlaps a sticky element with a later sibling).
Neither produced the reported symptom; only a repro with content genuinely
taller than the viewport, scrolled and measured, showed the real mechanism.

**Rule:** `position: sticky` on an element whose height can exceed its
scrolling context does not make that overflow reachable by scrolling — sticky
freezes visually the instant it pins, and unpins in one jump. Test this class
of bug by making content deliberately taller than the container and scrolling
through it, not by reading the CSS and reasoning about a single screen size.

**First fix was wrong, and passed its own test anyway.** Capping the panel
with `max-height` plus its own `overflow-y: auto` does make the overflow
reachable — confirmed in an isolated repro of just `.panel` and its content.
Applied to the real page, nested inside `.panels`' own `scroll-snap-type: y
proximity`, it produced two visible scrollbars and the outer snap container
still won the race for wheel input, advancing to the next section before the
inner one finished — the same content-goes-missing bug, now with an ugly
double scrollbar on top. **An isolated repro proves a fix works in isolation,
not in the page it has to share a scroll container with.** The tell was in
the diagnosis, missed the first time: the bug was never really about the
`.panel` in isolation, it was about `position: sticky` specifically, and any
fix that kept sticky was always going to have to fight the ancestor's
scroll-snap for the scroll gesture.

**Actual fix: drop `position: sticky` from `.panel` entirely.** With no `top`
to pin to, the panel is an ordinary block that grows to fit its content —
nothing is ever hidden, no inner scroll container is needed, and
`scroll-snap-align: start` on its own (no sticky involved) still settles the
page on each section's start once scrolling stops. The scroll-snap was never
the problem; only the sticky pin was. Cost: panels no longer visually "stay
put" while the next one slides up over them — a decorative effect, not a
documented decision, so trading it for content that is always reachable was
the right call.

