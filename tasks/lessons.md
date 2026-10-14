# Lessons

One rule per correction, so the mistake does not recur. Add a line when one happens; the
incident itself belongs in the commit or the amendment.

## .NET and tooling

- Check a CLI's flags with `--help`; `dotnet new func` and `func init` do not share them.
- Templates write their own `TargetFramework`, which overrides `Directory.Build.props`; delete it and confirm with `dotnet msbuild -getProperty:TargetFramework`.
- On the .NET 10 SDK `dotnet new sln` writes `.slnx`; check what was written before naming the file anywhere.
- Read a test package's licence before adding it: FluentAssertions 8 is non-commercial, so it stays pinned to 7.x.
- Expression trees reject `is` patterns; use `==` and `||` inside FluentAssertions predicates.
- `async void` crashes the process on an exception; anything that awaits returns `Task`.
- Before deleting a `!`, find the warning it silences; the fix is usually the signature beneath it.
- A test stub builds a new `HttpResponseMessage` per call; script responses as status plus body.

## Algorithms and data

- A one-row DP cannot keep parent pointers across rows; store the chosen set per state.
- When a fixture test fails, check the fixture first: real data states facts nobody wrote down.
- Unresolved reference data in a fixture test is a failure, not something to filter out.
- Rebuild a derived fixture from every capture on disk, not only this run's.
- A rule phrased as "the week" or "the season" is league-wide; compute it once across all teams.

## HTTP and services

- Registering a handler is not adding it: `AddHttpMessageHandler` puts it in the pipeline; test the behaviour.
- Resilience outermost, rate limiter inside, so every real attempt spends a token.
- The standard resilience handler disables `HttpClient.Timeout`; tune its own timeouts, and check a library by running it.
- A service must acquire what it depends on itself; state primed by one host is missing in the next.
- When a fix that should work does not, check the layer below is reachable before fixing the same layer again.

## Front end

- Never type with `Function`, `object` or a bare `any` at a component boundary.
- For a nullable number use `??`, never `||` or `?:`; better, make the null unreachable by type.
- `display: contents` deletes the element's box and everything styling it.
- For an overflow, list elements wider than the viewport before theorising; the first is the cause.
- A sticky panel taller than its container hides its overflow; test by forcing tall content.
- UI whose job is timing must be watched with the slow case forced; tests cannot see it.
- Check a planned task's premise against the code before building it; unbuilt tasks keep stale premises longest.

## Shell and CI

- Run the runbook's shell blocks in Git Bash; a command that depends on its shell says so and checks it.
- On Windows, `$( )` keeps a trailing `\r`; strip it with `tr -d '\r'` before storing a value.
- `! cmd` under `set -e` is not a guard; write `if cmd; then …; exit 1; fi`, and prove a gate fails once.
- Under `pipefail`, never pipe a large download into an early-exit reader (`grep -q`, `head`); write a file.
- A post-deploy check waits for this run's artefact, identified by build, not whatever is live.
- Adding `environment:` or changing a trigger changes the OIDC subject; read the subject from the log first.

## Azure

- Verify a setting on the resource that enforces it: workspace-based App Insights caps on the workspace.
- A deploy-time value (`utcNow()`) must not set a property Azure cannot change after creation.
- A template that builds is not one Azure accepts; `infra/` changes are verified by a what-if.

## Secrets (A2)

- Never put a real ID in a test, even a harmless one; satisfy its shape with an obvious placeholder.
- Read a settings file as keys only, or with every value replaced; never by listing keys to hide.
