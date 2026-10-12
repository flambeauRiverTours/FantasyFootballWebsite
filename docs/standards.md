# Engineering standards

This document states the standards for each programming domain for this project. 

## 1 · C# and .NET

**Build**
- Warnings are build errors
  - why: absent Sleeper fields surface at compile time not runtime
- Code style analysers run in the build
  - why: style drift fails CI instead of code review
- Every `!` operator has a comment explaining why

**Architecture**
- Functional core with an imperative shell
  - why: solver and sigma test offline in milliseconds
- `FantasyLeague.Core` stays pure
  - No file or network access
  - No Azure or HTTP types
  - No clock or randomness
  - No logging
- Project references point inward only
  - Hosts reference `FantasyLeague.Data` and `FantasyLeague.Sleeper`
  - Those two reference `FantasyLeague.Core`
  - Core references nothing in the solution
- Wire DTOs never reach Core
  - why: Sleeper can change shape without touching domain code
- One explicit mapper turns DTOs into domain records
  - why: absent fields and overrides are decided in one place
- Types are `internal` unless they form a project seam
  - why: a small public surface keeps refactors local
- Each project registers its own services via extension methods

**Types and values**
- Data types are immutable records
- Signatures use `IReadOnlyList` and `IReadOnlyDictionary`
- Points are `decimal` never `double`
  - why: both invariants compare totals to the cent
- Time comes from an injected `TimeProvider`
  - why: tests control the clock across week boundaries
- NFL schedule logic names `America/New_York` explicitly
  - why: game windows follow Eastern time not server time

**Naming**
- Private fields use `camelCase` with no underscore prefix
  - why: matches the primary constructor parameters used throughout

**Errors**
- Options are validated at startup with `ValidateOnStart`
  - why: a bad setting fails the deploy not a visitor
- Unknown slot codes throw per A3
  - why: a silently dropped slot undercounts ideal undetectably
- Only transient failures may fall back
  - why: a silent schema fallback once hid a bug for days
- Production code asserts both invariants
  - Starter points sum to the roster total
  - Ideal is never below actual per A9
  - why: tests never see live Sleeper data

**Logging**
- Never log league or user IDs even inside URLs
  - why: they identify a real league and its members

## 2 · Front end

**TypeScript**
- `noUncheckedIndexedAccess` is on
  - why: lookups by week or roster can be undefined
- Every `!` assertion has a comment explaining why

**React**
- D3 never touches the DOM per D9
  - why: two libraries mutating one DOM fight each other
- View options live in the URL query string
  - why: every view becomes a shareable link
- Controls without data yet are hidden not disabled
  - why: a control that does nothing reads as broken

**Data**
- All requests go through `api/client.ts`
  - why: one place owns ETags and the API origin
- Requests send ETags and a 304 keeps the screen
  - why: an unchanged poll costs a 304 not a payload
- Polling runs only during game windows per D6
  - why: between games nothing changes so nothing is fetched
- Calculations live in `pointCalculation.ts` not in components
  - why: pure functions test without rendering anything

**Styling**
- One stylesheet serves the whole site
  - why: a single page needs no CSS tooling
- The dark theme is designed not inverted
  - why: inverted colours lose contrast and the team hues
- Layouts work from 375 pixels wide
  - why: the narrowest common phone width
- Only the data table may scroll horizontally

**Accessibility**
- Chart labels describe data not shape per R2
  - why: a radar silhouette misleads so labels state the facts
- The reduced motion preference turns motion off entirely
  - why: decorative motion has nothing worth keeping

**Motion and performance**
- Motion is decorative and never carries information
  - why: the data must read the same with motion off
- Motion never replays on a poll
  - why: a 30 second poll would flash every row
- Initial JavaScript stays under 125 KB gzipped
  - why: a budget catches dependency bloat before it ships
- The loading state paints before the bundle runs
  - why: a database cold start can take a minute

## 3 · Data and integrations

**Sleeper API**
- One rate limited `HttpClient` makes every Sleeper call
  - Its token bucket is a singleton
  - why: a per request limiter is not a limiter
- The request budget is documented and kept true
- The league ID is configuration never a request parameter
  - why: otherwise the API relays traffic for any league
- Ideal points use the exact solver per D4
  - why: greedy undercounts whenever a flex slot exists
- Ideal points use historical rosters per D2
  - why: current rosters rewrite history after every waiver claim

**Persistence**
- Only finalized weeks are stored
  - why: finalized weeks never change so storing them is safe
- The pipeline applies migrations before dependent code deploys
  - why: code shipped ahead of its schema broke reads
- Stored blobs carry their own timestamp
  - why: a reader judges freshness without a metadata call

**Correctness**
- The placeholder team is excluded at the read boundary
  - From standings rows
  - From every league aggregate per A1
  - why: a dummy team skews every league average
- Sigma is population sigma over finalized weeks per D1
  - why: the season is the whole population not a sample
- Sigma is null below four finalized weeks
  - why: three weeks of spread is noise not consistency
- Radar axes are ratios to league averages per D10
  - why: raw points draw the same QB spike for everyone
- Radar axes keep roster order per R2
  - why: reordering axes changes the shape and its reading
- Every cache TTL has a documented reason

## 4 · Infrastructure and operations

**Infrastructure as code**
- Values computed at deploy time never set immutable properties
  - why: a monthly budget date broke every deploy in October
- Docs never name an Azure resource per A2
  - why: the repository is public and names are identifiers

**Identity and secrets**
- No league or user IDs in git per A2
  - Not in code or docs
  - Not in fixtures or workflows
  - Not in commit messages
  - why: they identify a real league and its members
- Azure SQL accepts managed identity through Entra only
  - why: no password exists to leak or rotate
- CI reaches Azure through OIDC with no stored credentials

**Cost**
- Steady state costs nothing each month
  - why: a hobby site should never produce a bill
- A budget alert fires at one dollar
- New resources state their monthly cost up front
- A daily telemetry cap stays inside the free grant

**Observability**
- SQL dependency calls are traced
  - why: a failing query once hid behind a silent fallback

## 5 · CI/CD and supply chain

**Pipeline**
- A bicep `what-if` runs on every pull request
  - why: infrastructure changes get reviewed as a diff
- `actionlint` checks every workflow on every pull request
  - why: workflow mistakes otherwise surface only after merging
- Deploys run infra then migrations then functions
  - why: code never deploys ahead of its schema
- A deploy is never cancelled midway
  - why: a half applied deploy is worse than a queue
- Post deploy checks verify the artefact this run built
  - why: the live site may still serve the previous build

**Workflows**
- Never pipe large output into an early exit reader
  - Examples are `grep -q` and `head`
  - why: pipefail turns an early exit into a false failure

**Dependencies**
- Deprecated packages are reported without failing the build
  - why: deprecation alone is not a defect
- Licences come from an allow list
  - `MIT`
  - `Apache-2.0`
  - `BSD-3-Clause`
- Every licence exception is recorded as an amendment
- Licence overrides pin a package version per A18
  - why: a new version means rereading its licence

## 6 · Testing

**Shape**
- Boundaries are tested against real captured Sleeper payloads
  - why: real payloads catch shapes invented ones never will
- Invented data covers only cases reality has not produced
  - A bye week
  - A commissioner override
- Axe checks every panel for accessibility
- Invariants run against every captured week automatically
  - why: each new capture tests the solver for free

**Discipline**
- Test names are sentences about behaviour
  - why: a failing name states the broken behaviour
- A regression test is shown failing without its fix
  - why: a test that never failed proves nothing

## 7 · Documentation and git

**Documents**
- The README fits on one screen
  - What this is
  - A link to the live site
  - The architecture
  - How to run it locally
  - why: a reviewer decides in seconds whether to read on
- Decisions are recorded with a date and reasoning
  - D numbers live in the design
  - A numbers live in the amendments
  - why: the reasoning outlives the code that implemented it
- Corrections happen in place marked **Corrected**
  - why: readers see current truth and how it changed
- Every incident adds a rule to the lessons log
  - why: a rule prevents the repeat a fix alone does not

**Comments**
- Every TODO names the task that resolves it
  - why: tracked tasks keep TODOs from rotting
- Internal backend APIs carry XML doc summaries too
  - why: most of the code is internal by design

**Git**
- Commits follow Conventional Commits
  - `feat` and `fix`
  - `docs` and `test`
  - `refactor` and `build`
  - `ci` and `chore`
  - why: history reads as a changelog
- Branches are named `type/short-description` using commit types
- Pull requests say how the change was verified
  - why: reviewers can check evidence rather than trust claims
- Pull requests are squash merged
  - why: one commit per change keeps `main` bisectable
