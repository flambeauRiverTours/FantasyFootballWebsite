# Ideal Points — Field Manual

> **This is now the living document.** It began as a conversion of the published
> artifact, but M0–M2 have been built and a dozen of its instructions have been
> corrected against reality. A re-sync from the artifact would be destructive.
> Edit this file directly; `docs/amendments.md` still holds the *reasoning*
> behind every decision made after design v6.
>
> Updated 2026-09-13. Task IDs match [`tasks/todo.md`](../tasks/todo.md).

---

This manual assumes no prior familiarity with Azure, bicep, EF Core, D3, CSS or
GitHub Actions. Where a step needs background, the background is here rather
than assumed.

**Completed tasks have been cut.** M0-M2, T24's build steps, T25-T28 and T32 are
built; what survived of them is the *Standing traps* section below, and the
reasoning lives in [`amendments.md`](amendments.md) and
[`lessons.md`](../tasks/lessons.md). This file is now what is left to build. Where a command can fail in a way whose error message points
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
| .NET SDK 10 | Builds everything. Projects target `net10.0` | `dotnet --list-sdks` |
| .NET 10 runtime | `dotnet test` runs on it; ships with the SDK | `dotnet --list-runtimes` |
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

# Standing traps

M0–M2's step-by-step has been cut; the code exists and `amendments.md` holds the
reasoning. These are the few things that will still bite during work that is
*not* finished, so they stay.

**`dotnet format` and line endings.** `.editorconfig` requires CRLF, the repo
stores LF, and `.gitattributes` pins `* text=auto eol=crlf` with
`.github/** text eol=lf`. A wall of `ENDOFLINE` errors in CI and none locally
means that file.

**The three gates**, which CI runs and you should run before pushing:

```bash
dotnet build -warnaserror
```

```bash
dotnet test
```

```bash
dotnet format --verify-no-changes
```

**A3 is unfinished.** An unrecognised slot code is accepted silently.
`Parse_rejects_an_unrecognised_slot_code` exists and is skipped, ready to enable.

**A singleton cache is per-instance.** Flex Consumption scales to zero and out to
forty, so anything held in memory starts empty on every new instance and any
invalidation reaches only the instance that ran it. This took the first deploy
down (A12) and shapes T27's cache TTL. Assume it again for anything you cache.

**Bicep only infers dependencies from symbolic references.** A resource that
interpolates a parent's property does not depend on that parent's *children*.

**`record` equality is reference equality for collection members.** `TeamWeek`
and `RosteredPlayer` are records whose list members compare by reference, so two
identical values are never `==`. Use `BeEquivalentTo` in tests.

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

# M3 · Persistence · built, bar three deferred tasks

T25 to T28 are done: the database is deployed in **westus2** on the free
serverless offer, the `Initial` migration is applied, and `FinalizeWeek` writes
every finalized week each Tuesday. **T29, T30 and T31 are deferred to the end**
of this manual — none is blocked on effort, each waits on something that does
not exist yet.

Until now every request rebuilds the whole season from Sleeper. That works
because the league is six teams and one season. It stops working when you want
seasons Sleeper no longer serves cheaply, and it wastes calls re-deriving weeks
that can never change again.

M3 adds a database holding **finalized weeks only**. The current, still-playing
week continues to come from Sleeper on every request. Two tasks here — the
password-less database connection and the migration step — are fiddlier than
their hours suggest, and both fail with error messages that point elsewhere.

# M4 · The page · built

T32 to T40 are done. The page renders the live standings, an interactive chart
and a gated radar panel, in both themes, with no horizontal scrollbar at 375 px
and 22 passing tests. The task instructions are cut; what follows is the shape
that resulted and the traps that cost real time.

```
src/
  App.tsx                 the fetch, its guards, the masthead, the panel order
  index.css               every token and every rule. there is no second stylesheet
  api/
    client.ts             fetchSeason (ETag-aware), fetchSeasons, ApiError
    types.ts              the payload interfaces, ConsistentTotals, hasConsistency
  hooks/
    useSeason.ts          polling, visibility gating, game windows, 304 handling
    useQueryState.ts      one query-string parameter, mirrored from React state
    useRelativeTime.ts    "updated 2 minutes ago", on its own clock
  components/
    Panel.tsx             section.panel > .panel-inner > h2, id + aria-labelledby
    StandingsPanel.tsx    rangeScale, the table
    SigmaDisplay.tsx      the inline range mark
    ChartPanel.tsx        series building, chips, metric, hover state, measurement
    Chart.tsx             pure: props in, SVG out
    RadarPanel.tsx        built and gated; revealed by T48
tests/
  semantics.test.tsx      landmarks, aria-sort, column tagging, axe
  contrast.test.ts        WCAG ratios computed from the OKLCH tokens
```

## The rules that came out of it

### Layout and CSS

**`box-sizing: border-box` is the first line of any stylesheet.** Its absence
made every `width: 100%` element with padding overflow by exactly its own
padding, and the symptom was a page-wide horizontal scrollbar that looked like a
table-width bug. The table had been collapsing correctly the whole time.

**`display: contents` deletes the element's box and everything attached to it** —
`max-width`, `margin-inline`, padding, background. Used to flatten a wrapper into
a flex row, it silently unpinned the masthead from the 64 rem column. Put the row
on the wrapper instead of removing the wrapper.

**`text-transform: uppercase` changes what a symbol means.** It turned the
consistency header's σ (U+03C3) into Σ (U+03A3) — standard deviation into
summation. Greek letters and units need `.symbol { text-transform: none }`.

**A long column header sets the column width.** `σ · through wk 1` on one line
gave a two-character column 94 px. The qualifier belongs on a second line in a
smaller, unletterspaced span.

**`width: 99%` on the team column is what aligns the numbers.** It absorbs the
slack so the numeric columns size to their content and sit in a block on the
right.

**Sticky panels and a sticky masthead fight.** Later siblings paint over earlier
ones, so the masthead needs `z-index`, the panels need `top: var(--masthead-h)`,
and their height must be `calc(100svh - var(--masthead-h))` — otherwise panel one
is a full viewport tall *below* the masthead and opens with a band of dead space.

**Panels need an opaque background and a leading edge.** They overlap as they
pin; without a `border-top` the covered panel looks clipped rather than covered.

### One visualisation per screen is a constraint

No two panels are ever on screen together, so **no interaction may span them**.
That ruled out the standings table as the chart's filter, and it is why hover
lives in `ChartPanel` rather than being lifted to `App`.

**State two panels both need belongs in `App`; state one panel owns stays in that
panel.** Season is the first kind (T56). Hover, metric and the team filter are
the second.

### TypeScript and React

**`Function` as a prop type disables type checking.** `scalingFunction: Function`
accepts any arguments and returns `any`, so a prop-name mismatch was the only
error the compiler could still see. Type a callback by its signature.

**Truthiness is not a null check on a number.** `totals.min ? totals.min : -1`
replaces a legitimate `0` with a sentinel. Better than `??`: make the null
unreachable by exporting the narrowed type — `ConsistentTotals` — and demanding
it at the prop.

**A nullable value is a better guard than a boolean beside a value.**
`rangeScale` returns `ScaleLinear | null`, and holding the scale *is* the fact
that there is a column to draw. There is no second condition to fall out of step.

**Hooks must be called before any conditional return.** This broke the page twice
— once in `App` with `useRelativeTime` called inside the JSX below the loading
guard, once in the plan for `ChartPanel`. React throws "Rendered more hooks than
during the previous render" and the whole tree unmounts, which presents as a
blank page with a clean typecheck.

**Derive from the URL, do not mirror it into a second state.** `shown` is
computed from the `teams` parameter during render. And the setter must accept an
updater: two toggles in one React batch both read the pre-update value, so the
first is lost.

**Validate every query parameter on read.** `?teams=1,999` must drop the 999, and
`?metric=nonsense` must fall back rather than leaving no segment selected.

### D3, per D9

Install the submodules — `d3-scale`, `d3-shape`, `d3-array` — never the `d3`
meta-package, which brings the DOM layer this project forbids. **D3 computes
numbers; React renders elements.** There is no `.append(` anywhere.

**`fill: none` on series paths.** An SVG path fills by default, so a line chart
without it renders as five overlapping blobs.

**`fill="transparent"`, not `fill="none"`, on a hit area.** `none` means
unpainted, and an unpainted shape receives no pointer events at all.

**Paint order is document order.** SVG has no `z-index`, so the raised series is
sorted last in the array — never moved in the DOM.

**A fixed pixel proximity threshold creates dead zones.** `best.d < 40` made an
isolated line unhoverable through a 100 px stripe, while clustered lines were
unaffected. Nearest-wins has no dead zones at any viewport size.

**Pad the y domain by a fraction of the data range, not of the minimum.**
`min − (min/max)·min` is non-monotonic and inverts: the tighter the data, the
more it zooms *out*. `(max − min) * 0.08` holds 75–86 % of the axis in every
case. A truncated magnitude axis then owes the reader an axis-break mark.

**The measured wrapper must always be mounted.** `ResizeObserver` is attached in
an effect with `[]` deps; if the wrapper only renders in the plottable branch,
the observer never attaches and `width` stays `0` forever — so the chart would
have appeared blank after the first Tuesday finalize until a manual reload.

### Time and polling

**`Intl.DateTimeFormat.formatToParts` returns only the fields you request.**
Asking for `month` without configuring it gives `undefined` and a `TypeError` on
the next tick.

**The viewer's clock is not the league's clock.** Game windows are decided in
`America/New_York` via `formatToParts`, never by adding an offset to local time —
the US and Europe change clocks on different dates.

**The finalize job runs 12:00 UTC Tuesday, which is 07:00/08:00 in New York.** A
poll window closing at 02:00 would shut five hours before the data it exists to
collect.

**Payload wins over error.** `useSeason` keeps the last good payload when a later
fetch throws; `App` must check `!payload` *before* `error`, or one failed poll
during a live Sunday replaces the scoreboard with an error message.

**The freshness label needs its own clock.** A 304 leaves `asOfUtc` unchanged
while "1 minute ago" goes stale anyway.

### Tooling

**A stale Vite transform can contradict a passing typecheck.** `cat > file`
truncates before writing, and the file watcher can cache the empty state; the dev
server then serves an empty module while the disk file is correct. `touch`
invalidates it. If an import error disagrees with `tsc`, check what the server is
serving.

**jsdom has no layout engine.** The `ResizeObserver` stub must report a size the
way the real one does, and axe's `color-contrast` and `target-size` rules are
disabled there — so contrast is tested by converting the OKLCH tokens to sRGB and
computing the ratio directly, and touch targets need a real browser.


---

# M5 · Modes, ops and docs · 10.5 h · 6.5 h remaining

Deliberately last. These are cheap, taste-driven, and much easier to judge against a season of real data than against three weeks of it.

## T41, T42 · built

Display modes and σ variants are done. The transforms live in
`web/src/api/pointCalculation.ts` as pure functions; `ChartPanel` chooses between
them and `Chart` renders whatever `Point[]` it is handed.

```
pointCalculation.ts     valueOf, ordered, cumulative, perWeek, weeklyMedians,
                        vsMedian, populationSigma, sigmaSeries
ChartPanel.tsx          metric / mode / window / scale state, pointsFor, the
                        four control rows
Chart.tsx               zeroBased, label, yTitle, the zero rule
```

### The rules that came out of it

**`toSeries` takes a transform, not a metric.** It receives
`(weeks: WeekPayload[]) => Point[]` and never learns which mode is running. Every
mode is a closure built once in the panel.

**Validate every parameter, narrow every union.** `metric`, `mode`, `window` and
`scale` are each read as a string, checked, and assigned to a union type. Nothing
downstream compares against a bare `string`.

**Never name a local `window`.** It shadows the global for the whole component,
so `window.location` silently stops resolving. The *parameter* can still be
`window`; the variable is `sigmaWindow`.

**Mode and σ are mutually exclusive.** "Cumulative σ" is not a thing, so the mode
row is absent — not disabled — while σ is selected, and its parameter is ignored.
Three control rows maximum in either state.

**Every control row needs a visible label.** Two adjacent `.segmented` controls
share a background and read as one six-option control; `aria-label` is invisible
and does not help a sighted reader. `.control-row` + `.control-label` +
`aria-labelledby` gives both readers the same word from one source.

**Memoise `weeklyMedians` on the payload and metric, not the mode** — or you
recompute the whole league when switching to a mode that does not use it.

**`zeroBased` is a property of the data, not the chart.** A cumulative magnitude
starts at zero; `vsMedian` is a signed difference and must not, or every negative
value clips to the axis while the arithmetic stays perfect.

**The accessible label follows the state.** `Chart` takes a finished `label`
string rather than composing one, so it never learns about modes and stays a pure
renderer. A hardcoded "Cumulative points by week" is wrong eight times out of
nine once there are three metrics and three modes.

**An arrow function with a brace body returns `undefined`.**
`(s) => { s.points.length >= 1 }` made `isPlottable` always false and the chart
never rendered. `oxlint`'s `no-unused-expressions` catches it; the typecheck does
not.

**`weeklyMedians` must skip unplayed weeks.** Including them contributes a zero
from every team, and mid-Sunday — when some teams have played and some have not —
the median collapses and every played team looks like it is running away with it.

## T43 · Loading and error states — 2.0 h

| State | What the reader sees |
|---|---|
| Cold start, SQL resuming | A skeleton table with an honest line: waking the database, this takes up to a minute. Not a spinner that implies seconds. |
| Season not backfilled | The 404's available-seasons list, rendered as links. Never an empty table. |
| Fewer than four final weeks | Em dashes in the σ column with the reason, not a zero and not a blank. |
| Sleeper unreachable | Finalized weeks still render from SQL; the current week is marked stale with its last-known timestamp. |

That last row is the design paying off: because finalized weeks never depend on
Sleeper, an outage degrades the site rather than breaking it. Make that visible
instead of failing whole.

### What exists, and what is half-done

`App.tsx` currently reads:

```tsx
if (loading) return <p>Loading…</p>;
if (!payload) return loading ? <Skeleton /> : <LoadFailed error={error} />;
```

Neither component exists, so this does not compile. The first line also makes the
second unreachable — `loading` has already returned. The correct shape has **one**
guard, because `loading` and `error` are both only interesting while there is no
payload:

```tsx
if (!payload) return loading ? <Skeleton /> : <LoadFailed error={error} onRetry={refetch} />;
```

`refetch` has been on `SeasonState` since T32 and has never been called. Destructure
it here; this is the task it exists for.

### Four new files

```
web/src/components/Skeleton.tsx     the loading table
web/src/components/LoadFailed.tsx   no payload at all
web/src/components/StaleBanner.tsx  a payload plus a later failure
web/src/hooks/useElapsedSince.ts    "this is taking a while"
```

Components, not branches inside `App` — `App` stays the fetch, its one guard, the
masthead and the panel order.

### `Skeleton`

The real `<table>` with the real headers and five rows of muted blocks, so
nothing jumps when data replaces it. It takes no props; the column set is fixed
and the row count is a constant.

```tsx
<div className="panel-inner" aria-busy="true" aria-live="polite">
  <table className="skeleton">
    …
    <td><span /></td>
  </table>
</div>
```

```css
.skeleton td > span {
  display: block;
  height: 1em;
  border-radius: 3px;
  background: var(--bg-subtle);
  animation: pulse 1.6s ease-in-out infinite;
}

@keyframes pulse { 50% { opacity: 0.45; } }

@media (prefers-reduced-motion: reduce) { .skeleton td > span { animation: none; } }
```

`aria-busy` and `aria-live="polite"` tell a screen reader the region is loading
rather than reading out five empty rows. A slow opacity pulse or nothing at all;
a sweeping gradient shimmer is the dated version.

### `useElapsedSince`, and the honest cold-start message

D7's database auto-pauses after 60 minutes idle, and the first request after that
waits for a resume. **It looks like a slow request, not an error**, so there is no
status code to branch on — only elapsed time.

```ts
export function useElapsedSince(active: boolean, ms: number): boolean {
  const [passed, setPassed] = useState(false);

  useEffect(() => {
    if (!active) {
      setPassed(false);
      return;
    }
    const id = window.setTimeout(() => setPassed(true), ms);
    return () => window.clearTimeout(id);
  }, [active, ms]);

  return passed;
}
```

`Skeleton` takes the result as a prop and swaps its caption:

```tsx
const slow = useElapsedSince(loading, 3000);
…
<Skeleton slow={slow} />
```

> Waking the database — this can take up to a minute.

A spinner implies seconds, and a reader concludes the site is broken at about the
fifteen-second mark. Saying what is happening costs one sentence.

### `ApiError` has to carry the 404 body

`client.ts` currently throws away the response body on failure:

```ts
if (!response.ok) {
  throw new ApiError(response.status, `GET /api/season${query} returned ${response.status}`);
}
```

A 404 from `/api/season` carries the available seasons, and that is the whole
reason this state is navigational rather than an error. Widen the class rather
than adding a second code path:

```ts
export class ApiError extends Error {
  readonly status: number;
  readonly available: number[] | null;

  constructor(status: number, message: string, available: number[] | null = null) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
    this.available = available;
  }
}
```

and read it before throwing, tolerating a body that is not JSON:

```ts
if (!response.ok) {
  const body = await response.json().catch(() => null);
  throw new ApiError(response.status, `GET /api/season${query} returned ${response.status}`,
    Array.isArray(body?.available) ? body.available : null);
}
```

`.catch(() => null)` matters: a 500 from the platform is an HTML error page, and
an unguarded `.json()` turns a useful status into a parse error.

### `LoadFailed` splits the two failures

```tsx
interface LoadFailedProps {
  error: Error | null;
  onRetry: () => void;
}
```

- **`error instanceof ApiError && error.status === 404`** — the season is not
  backfilled. Render `error.available` as links (`?season=2025`). This is a
  *navigational* state: the word "error" appears nowhere on it.
- **Anything else** — say so plainly and render a retry button calling `onRetry`.
  Never tell the reader to reload the page; you have `refetch`.

### `StaleBanner` is the one that protects a live Sunday

`useSeason` deliberately keeps the last good payload when a later fetch throws, so
`error` and `payload` can both be set. That combination is a **banner**, never a
replacement:

```tsx
{error ? <StaleBanner asOfUtc={payload.asOfUtc} onRetry={refetch} /> : null}
```

Render it between the masthead and `<main>`, with `role="status"` so it is
announced without stealing focus. Reuse `useRelativeTime` for "last updated"
rather than formatting a second time.

### Two invariants for this task

**Never an empty table with no explanation.** Every zero-row state has a reason
and the reason is renderable: no seasons, season not backfilled, no finalized
weeks yet.

**Never a number you do not have.** `0.0` is a score somebody could have posted.
An em dash — `—`, U+2014, muted — is the only honest rendering of an absent
figure.

### Done when

- Killing the network mid-session leaves the page up with a banner and a working
  retry, not an error screen.
- `?season=1999` renders year links and the word "error" appears nowhere.
- The skeleton occupies the same space the table will, so nothing jumps.
- Holding the first request for five seconds shows the database message.
- Reduced motion stops the pulse and the page still looks finished.

## T44 · Cost guardrails — 2.0 h

Three settings that are the difference between a free hobby project and an
unpleasant invoice. **Configure them in bicep, verify them from the CLI, and
re-verify after every infrastructure change** — a portal-set value does not
survive the next `az deployment group create`, which is the whole argument for
not setting one.

All three already exist as template parameters. This task is about **proving**
they are live, and leaving behind something that proves it again next time.

### Where the work lives

```
infra/main.bicep        already parameterised; confirm, do not re-add
scripts/verify-cost.sh  new — the three checks, runnable by anyone
docs/runbook.md         new in T45; this task supplies its cost section
```

A script rather than a checklist, because a checklist is read once and a script
is run. Keep it in `scripts/` beside `capture_fixtures.py` and have it exit
non-zero on any surprise.

### 1 · Telemetry ingestion cap

Telemetry is the likeliest thing here to cost money, because it scales with
traffic rather than with your code. `dailyIngestCapGb` defaults to `0.16`, which
keeps a month inside the 5 GB free allocation.

**The cap lives on the Log Analytics workspace, not on the App Insights
component.** This component is workspace-based, so the bicep sets
`workspaceCapping.dailyQuotaGb` and the component's own `DailyCapGb` — which
reads back as `100` — is the classic-mode default and controls nothing:

```bash
az monitor log-analytics workspace show -g "$RG" -n "$WS" \
  --query "workspaceCapping.dailyQuotaGb"
```

The cap **drops** data once reached; it does not bill the overage. Losing a day's
telemetry on a hobby site is the correct trade.

### 2 · Azure SQL free-limit behaviour

D7 chose the serverless free offer with `freeLimitExhaustionBehavior:
'AutoPause'`. Confirm it is not `BillOverUsage`:

```bash
az sql db show -n "$DB" -s "$SQLSERVER" -g "$RG" \
  --query "{free:useFreeLimit, behavior:freeLimitExhaustionBehavior, pause:autoPauseDelay}"
```

One runaway job should cost you a paused database, not a bill. `autoPauseDelay`
is 60 minutes, the minimum the free offer allows.

### 3 · A budget alert on the resource group

`budgetAmount` and `budgetAlertEmail` are already parameters, and **the budget
resource is skipped entirely when the email is empty**. So the check that matters
is not "is the threshold right" but "does the budget exist at all":

```bash
az consumption budget list -g "$RG" \
  --query "[].{name:name, amount:amount, contacts:notifications.*.contactEmails}"
```

An empty array means `BUDGET_ALERT_EMAIL` is unset in the deploy workflow and you
have no alarm. That is the failure this task exists to catch.

### The guardrail that is not in Azure

The fourth is structural: the token-bucket handler on the Sleeper `HttpClient`.
It is the reason viewer count cannot drive request count, and **it is a singleton
because a per-request limiter is not a limiter**. Nothing in M5 changes it. Say
so in the runbook, so nobody later "optimises" it into a scoped service and
quietly removes the rate-limit guarantee.

### Done when

- `scripts/verify-cost.sh` runs clean and exits non-zero when any value drifts.
- The budget exists and has an email on it.
- The runbook says to run the script after any bicep deployment.

## T45 · README and runbook — 1.5 h

Two documents with different readers. Write them now, while you still remember
why things are the way they are.

```
README.md          for a stranger who found the repository
docs/runbook.md    new — for you, a year from now
```

`README.md` already exists and covers what the site is. This task finishes it and
adds the runbook beside the design and the manual.

### README — four things and no more

1. **What the site is**, which is already there.
2. **The three metrics**, one sentence each — particularly that ideal means
   "points left on the bench", because the name alone does not say it.
3. **How to run it locally**: `dotnet user-secrets` for the league id,
   `npm run dev` in `web/`, and the fact that the dev proxy forwards `/api` to
   the deployed Function App, so **no local backend is needed**.
4. **Links** to `docs/design.md` for why and `docs/field-manual.md` for how.

**A2 applies to the README**, and it is the file most likely to breach it. No
league id, no user ids, no resource group, no storage account. The deployed
hostname is public and fine; everything else is an `az` lookup.

### The runbook — the August rollover

The whole annual maintenance burden, which is the point of D8:

1. Create the new season in Sleeper using the roll-over flow, so
   `previous_league_id` is set.
2. Update `League__CurrentLeagueId` in the Function App's settings to the new
   league ID.
3. Wait for the daily chain walk, or trigger it manually.
4. Confirm the new year appears in `/api/seasons`.
5. Confirm the season picker (T56) now offers two options.

Say explicitly that **the chain walk runs backwards** from the configured league,
so step 2 is what makes step 3 possible. Nothing discovers a new season on its
own, and this is the single fact most likely to be forgotten in eleven months.

### Also in the runbook

- **Re-running the backfill** after a solver fix, and that `WriteAsync` upserts,
  so running it twice is safe.
- **The cost checks** — point at `scripts/verify-cost.sh` and say when to run it.
- **The `roster_positions` guard**: if the league changes its lineup mid-season,
  `SlotAverages` returns null, those weeks drop out of the radar, and an error is
  logged. Name the log and where to look.
- **Which secrets exist and where they live** — names only, never values: the
  GitHub secrets, the Function App settings, and that SQL uses Entra rather than
  a password.
- **How to roll the SWA deployment token**, since it is the one credential that
  is not federated and the one most likely to leak.
- **The three OIDC federated credentials** and which workflow each serves, since
  adding an `environment:` or changing a branch mints a different subject and
  needs a new one.

### Done when

- Someone who has never seen the repository can run the front end locally from
  the README alone.
- The rollover procedure has been read start to finish by someone who did not
  write it.
- Nothing in either document would need redacting if the repository went public
  tomorrow.

## T57 · Custom domain — 1.0 h

`*.azurestaticapps.net` is not a link anyone will paste into a league chat twice.
This is a subdomain of a domain that is already owned and already served by
GoDaddy's nameservers, so it is one DNS record, one Azure command, and one bicep
parameter — the last of which is the only part that can go wrong.

**It costs nothing.** No registration, no tier change: Static Web Apps includes
custom domains and managed TLS certificates on the Free tier, up to two per app.
The design's **$0.00/month steady state** survives this task intact, and there is
no amendment to write.

### What was confirmed before starting

```
NS       ns35/ns36.domaincontrol.com     DNS is managed at GoDaddy
apex A   185.199.108-111.153             GitHub Pages, and untouched by this
scores.  NXDOMAIN                        free to use
```

The apex being a live GitHub Pages site is the reason the subdomain route is the
easy one: nothing about the existing site changes, and no apex `A`/`ALIAS`
juggling is needed. A subdomain also validates off its `CNAME` alone, with no
separate `TXT` ownership record.

Pick the label first and use it consistently — `scores` below, but the choice is
free. It appears in DNS, in Azure, in the bicep parameter, and in the README.

### 1 · The DNS record

GoDaddy → Domain Portfolio → the domain → **DNS** → Add New Record.

| Field | Value |
|---|---|
| Type | `CNAME` |
| Name | `scores` |
| Value | the Static Web App's default hostname |
| TTL | 600 seconds while setting up, default afterwards |

**Enter `scores`, not `scores.<domain>`.** GoDaddy appends the domain to whatever
is in the Name field, so the full name produces
`scores.<domain>.<domain>` — a record that resolves for nobody and looks correct
in the panel.

The default hostname is not written down anywhere in this repository on purpose
(A2's habit, applied to resource identifiers). Read it out of Azure:

```bash
az staticwebapp show -n "$SWA" -g "$RG" --query defaultHostname -o tsv
```

A short TTL matters only while you are iterating: get the record wrong at 1 hour
and you wait an hour to see the correction.

### 2 · Tell Azure about it

```bash
az staticwebapp hostname set -n "$SWA" -g "$RG" --hostname scores.<domain>
```

Then wait, and check with DNS rather than with a browser — browsers cache
resolution aggressively and will keep telling you it does not exist after it
does:

```bash
nslookup scores.<domain> 8.8.8.8
az staticwebapp hostname list -n "$SWA" -g "$RG" -o table
```

Wait for status `Ready`, not `Validating`. The certificate is issued and renewed
automatically once validation passes; there is nothing to install and nothing to
diarise.

### 3 · The part that actually breaks

**The API refuses the new origin.** `main.bicep` allows the Static Web App's
*default* hostname and nothing else:

```bicep
cors: {
  allowedOrigins: concat(['https://${site.properties.defaultHostname}'], devOrigins)
}
```

So the moment DNS resolves, the site loads perfectly and **every API call fails**
— a page with a masthead, no standings, and a CORS error in the console. It looks
nothing like a DNS problem and will send you back to the registrar for an hour.

Add it to the template, never to the portal, or the next deployment removes it:

```bicep
@description('Custom domain serving the site, without a scheme. Empty until T57.')
param customDomain string = ''

…
allowedOrigins: concat(
  ['https://${site.properties.defaultHostname}'],
  empty(customDomain) ? [] : ['https://${customDomain}'],
  devOrigins
)
```

Pass it from `deploy.yml` beside the existing parameters. **A repository
variable, not a secret** — `vars.CUSTOM_DOMAIN` — because a custom domain is
public by definition; it is the address people type. It is the one value in that
parameter list that is not sensitive, and treating it as a secret would only make
the workflow harder to read.

Keep the default empty so the template still deploys for anyone without a domain.

### 4 · Verify end to end

The three assertions in `deploy.yml`'s site check work unchanged against the new
host:

```bash
SITE=https://scores.<domain>
curl -fsS "$SITE" -o index.html && grep -q "/assets/" index.html
asset=$(grep -o "/assets/[^\"]*[.]js" index.html | head -1)
curl -fsS "$SITE$asset" | grep -qF "$API"
curl -s -o /dev/null -w '%{http_code}\n' "$SITE/api/season"    # 404, not 200
```

Those prove the bundle is served and knows its API origin. They do **not** prove
CORS, because `curl` does not enforce it — only a browser does. Open the site and
watch the network tab: a `200` on `/api/season` from the custom origin, with
standings on screen, is the only real confirmation.

### Done when

- `https://scores.<domain>` serves the site with a valid certificate and no
  warning.
- The standings render in a browser, which means CORS accepted the new origin.
- `customDomain` is a bicep parameter wired from `deploy.yml`, and survives a
  redeploy.
- The default `*.azurestaticapps.net` hostname still works — `deploy.yml`'s smoke
  test uses it, and it is the fallback if DNS ever goes wrong.
- The apex site is unaffected.
- The README quotes the new address.

### Reference

- **Azure** — [Set up a custom domain in Azure Static Web Apps](https://learn.microsoft.com/azure/static-web-apps/custom-domain)

---

# M6 · Positional radar · 10.0 h

One team's average output per starting slot against the league average at the
same slot, on axes taken from the league's own rules. Last because it rests on a
property of Sleeper's payload — that `starters[i]` occupies the *i*th startable
slot — which T02 confirmed and which the finalize job re-asserts every week.

### What is already in place

M4 built the front end against the contract this milestone fills, so three
things exist and do nothing:

| Already there | Where |
|---|---|
| `RadarPanel`, a real panel with an id and a heading | `components/RadarPanel.tsx` |
| The gate that reveals it | `App.tsx` — `Object.keys(payload.leagueSlotAverages).length > 0` |
| `slotOrder`, `leagueSlotAverages`, `slotAverages` in the types | `api/types.ts` |

So **T48 is the task that turns the panel on.** No flag, no second deploy, and
`?radar` forces it open locally while you build against hand-written data.

The panel constraint from M4 applies here too: the radar is never on screen with
the table or the chart, so **every control it needs lives inside it**.

## T46 · Slot attribution — 2.5 h

`starters[i]` plays in the *i*th startable slot, so attribution is an index join
and needs no position inference at all.

This is pure domain logic, so it belongs in `FantasyLeague.Core` beside the
solver, testable offline against the T02 fixtures with no Azure dependency.

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

Dividing by the slot count is what makes a two-RB league comparable across axes.
Without it every team's RB axis is roughly twice its QB axis, which describes the
rules rather than the roster.

### Where the result is stored

Per-week slot values have to survive, or every page load re-derives them from
Sleeper and the whole point of M3 is lost. `TeamWeek` already carries the full
`RosteredPlayer` list precisely so a week stays re-solvable — but re-deriving on
read is work you can avoid by persisting the attribution alongside it.

Widen the stored week rather than adding a table: one more column holding the
per-slot dictionary as JSON keeps the composite key and the upsert unchanged, and
a handful of slots per week is nowhere near a size worth normalising.

### Tests worth writing

The T02 fixtures make all of these offline and fast:

- A week where `starters.Count != startableSlots.Count` returns null.
- A `"0"` starter contributes a real zero and still counts toward the divisor.
- A player present in `players` but absent from `players_points` scores zero
  rather than throwing.
- A two-RB league produces an RB average, not an RB total.
- A defence starter — `"DEN"`, not a numeric id — resolves like any other.

> **Watch out**
>
> **The nullable return is the mid-season rules guard.** The design assumes
> `roster_positions` does not change during a season, and Sleeper permits it
> anyway. If it happens, `starters` stops lining up with the stored slot list and
> every week is silently attributed to the wrong axis. Return null, log the
> league and week at error level, and let that week contribute nothing to the
> radar — the points and ideal columns do not depend on slot alignment and carry
> on unaffected.
>
> A started-nobody slot is `"0"` and is worth a real zero. Do not filter it out:
> leaving a slot empty is a managerial decision and should drag the average
> exactly as much as it drags the score.

## T47 · League reference and ratios — 1.5 h

1. For each team and slot, average the weekly per-slot values over regular-season
   weeks that produced an attribution.
2. For each slot, average those season values across all teams — that is the
   reference circle.
3. Divide. Team season value over league season value gives the plotted ratio,
   centred on 1.0.

### A1 applies to step 2, and it is the easy one to miss

The dummy team is excluded from the standings by a filter you have already
written. **The league reference is a second, separate filter.** A placeholder
team left in this denominator biases every real team's ratio on every axis, and
nothing about the output looks wrong — the numbers are merely all slightly off.

Compute the reference from the same filtered roster list the standings use, not
from the raw league payload.

### Where it runs

In the finalize job, not in the read path. The reference only changes when a week
finalizes, it needs every team's season to date, and computing it per request
would mean each reader recomputing the whole league. Store the reference
alongside the season and serve it.

> **Watch out**
>
> **Average first, then divide.** Computing a ratio per week and averaging the
> ratios looks equivalent and is not. On the week where the entire league's
> kickers scored two points between them, the denominator is near zero and one
> ordinary Sunday becomes a permanent distortion on the K axis.
>
> Guard the degenerate denominator anyway: if a league season value is zero —
> every team started nobody at that slot all year — the ratio is undefined.
> Return null for that axis and draw it at the reference circle with a marker,
> rather than dividing by zero or quietly plotting 1.0 as though it were measured.

## T48 · Slot block in the API — 1.0 h

Three additions to the existing `/api/season` payload — no new endpoint, for the
same reason the table and chart already share one.

```
"slotOrder": ["QB","RB","WR","TE","FLEX","K","DEF"],
"leagueSlotAverages": { "QB": 18.4, "RB": 12.1, ... },

"teams": [ { ..., "slotAverages": { "QB": 21.2, "RB": 11.8, ... } } ]
```

`slotOrder` comes from the season's `roster_positions`, deduplicated while
preserving first appearance:

```csharp
var slotOrder = startableSlots.Select(x => x.Code).Distinct().ToList();
```

LINQ's `Distinct()` preserves first-occurrence order over in-memory sequences,
which is what you want — the league's own lineup order is the only non-arbitrary
axis order available, and per R2 it must never be re-sorted.

**Send raw averages rather than pre-computed ratios.** The client can divide, and
shipping both numbers means a tooltip can say "21.2 against a league average of
18.4" instead of only "1.15×".

### Two mechanical things that will bite

**The ETag must change.** It hashes the week scores; adding fields that are not
in the hash means every cached client keeps the old body and the radar never
appears for them. Include the slot block in whatever the ETag is computed over,
or bump a version component of it.

**The cache has to be invalidated.** `CachedFinalizedWeekStore` holds a six-hour
TTL keyed by league and season. A deploy that changes the payload shape does not
clear it, so the first readers after the deploy get the old shape from L1.
`RemoveByTagAsync` on write already covers the finalize path; a shape change
needs a manual eviction or a restart.

**This task is what reveals the radar panel.** T33b gates panel three on
`leagueSlotAverages` being non-empty, so shipping this payload turns the panel on
with no second deploy and no flag to flip. Until then the panel is reachable
locally with `?radar`.

### Done when

- `/api/season` returns all three fields populated for the current season.
- The radar panel appears on the deployed site with no front-end change.
- An old ETag does not serve a payload without the slot block.

## T49 · Radar chart — 3.5 h  ±

### The piping

`RadarPanel` follows `ChartPanel`'s shape exactly, and for the same reasons:

```
RadarPanel.tsx    the team selector, the view toggle, the measured wrapper
Radar.tsx         pure: slots + ratios + size in, SVG out
```

The panel owns three pieces of state. Which team is shown is the interesting one,
and per D11 it is a single selection, so it goes in the query string like the
metric did:

```tsx
const [rawTeam, setTeam] = useQueryState('team', '');
const teamId = payload.teams.some((t) => String(t.rosterId) === rawTeam)
  ? Number(rawTeam)
  : (ranked[0]?.rosterId ?? null);
```

Validate on read, exactly as the chips do — a stale link naming a roster that no
longer exists must fall back to the leader rather than rendering an empty radar.

**The team selector is the chips again, but single-select.** Reuse `.chip` and
`aria-pressed`; the only difference is that clicking sets rather than toggles, so
there is no never-zero guard to write. Do not import the chips from `ChartPanel`
— lift them into a shared component only when the third caller appears.

`slotOrder` comes from the payload and **is never re-sorted** (R2). Read the
ratios in that order:

```tsx
const ratios = payload.slotOrder.map((code) => {
  const league = payload.leagueSlotAverages[code];
  const team = payload.teams.find((t) => t.rosterId === teamId)?.slotAverages[code];
  return team === undefined || !league ? null : team / league;
});
```

`!league` catches both the missing key and the zero denominator T47 warns about.

### The polar maths

```tsx
const R = 78;                       // radius of the 1.0 reference circle
const angle = (i: number) => -Math.PI / 2 + (2 * Math.PI * i) / slots.length;

const point = (i: number, ratio: number) => {
  const r = R * Math.min(ratio, 2);           // clamp: one freak axis must not escape
  return [cx + r * Math.cos(angle(i)), cy + r * Math.sin(angle(i))] as const;
};

const polygon = slots.map((s, i) => point(i, ratios[s]).join(",")).join(" ");
```

`-Math.PI / 2` starts the first axis at twelve o'clock rather than at three,
which is what every reader expects. Angles increase clockwise from there because
SVG's y axis points down — the same fact that inverts the line chart's range.

**Draw the reference circle first**, at exactly `R`, so the polygon reads as a
deviation from it. A dashed circle in `--rule` is enough; it is the only thing on
the drawing that carries a fixed meaning.

**A null axis is not zero.** Render the polygon with a gap, or pin that vertex to
the reference circle and mark it — but never plot 1.0 as though it were measured.

Label anchoring follows the label's side of the circle — `start` to the right of
centre, `end` to the left, `middle` at top and bottom — otherwise the left-hand
labels overrun the drawing.

> **Watch out**
>
> **Leave room in the viewBox for the outermost labels.** The polygon extends to
> 1.5×R and the labels sit beyond that; size the viewBox to the labels, not to
> the chart. Place them at roughly 1.7×R and check the widest one —
> `SUPER_FLEX` is nine characters and will be the one that overflows.
>
> **Never describe or label the enclosed area.** Area grows with the square of
> the radius, so a team 20 % better everywhere encloses about 44 % more space.
> The polygon is a shape to recognise, not a quantity to read.
>
> Give the `<svg>` a `role="img"` and an `aria-label` naming the team and which
> axes sit outside the circle. Seven unlabelled paths are silent to a screen
> reader, and "shape" is not describable — the *list of axes above and below
> average* is.

A single series needs no legend box — the heading names the team. If you allow a
second team for a head-to-head, a legend becomes mandatory, and two is the hard
cap: three overlapping polygons is spaghetti.

### Done when

- The first axis is at twelve o'clock and the axis order matches `slotOrder`.
- A team with a missing slot renders a marked gap, not a 1.0.
- Nothing is clipped at any viewport width, `SUPER_FLEX` included.
- `?team=4` reproduces the view; `?team=999` falls back to the leader.
- The `aria-label` names the axes above and below average, not the shape.

## T50 · Diverging bar alternative — 1.5 h

The same numbers on a linear axis, as a toggle beside the radar. Horizontal bars
from a zero centre, one row per slot, in `slotOrder`.

This is the form the data actually asks for — a signed deviation from a baseline
across a handful of categories is a diverging bar chart. Bars share one linear
scale, so values are directly comparable and nothing is distorted by area or by
axis ordering. Use a two-hue diverging pair with a neutral midpoint at zero, and
direct-label each bar with its value.

### The wiring is deliberately trivial

Both views take the same `slots` and `ratios` arrays, so the toggle swaps a
component and nothing else:

```tsx
const [view] = useQueryState('view', 'radar');     // radar | bars
…
{view === 'bars' ? <SlotBars … /> : <Radar … />}
```

A segmented control, the same one T37 built. If adding the second view requires
touching how the ratios are computed, the computation is in the wrong place —
lift it into `RadarPanel` until both children are pure consumers of it.

**Plot `ratio − 1`, not `ratio`**, so the baseline is zero and the bar length is
the deviation. A shared symmetric domain — `[-max|d|, +max|d|]` — keeps the two
sides comparable; an asymmetric axis makes a small positive look like a large
one.

It exists so the radar does not have to be accurate about magnitude. Keep the
radar as the default anyway: this is a page for a fantasy league, and "what shape
is my team" is a question people genuinely want answered. Two views, one data
set, and no argument about which one is correct.


---

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


---

# Deferred · the rest of M3

Moved behind M4 on 2026-09-14. None is blocked on effort; each waits on
something that does not exist yet. Rationale for each is in
[`tasks/todo.md`](../tasks/todo.md) under *Deferred*.

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
