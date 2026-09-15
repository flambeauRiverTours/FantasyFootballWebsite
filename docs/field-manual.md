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

# M4 · The page · 25.5 h · 13.5 h remaining

The first milestone anyone else in the league can look at, and the first that
will generate opinions. The table is the primary object; the chart is the thing
people will play with.

**T32 to T35 are built** — the design system, the panel layout, the standings
table and the range mark. What they cost is recorded below; the tasks themselves
are cut. T36 onward is what remains, and T36 is where D3 enters the project.

Everything in this milestone is built against an API that is **already live and
already correct**. Before writing a line of TypeScript, fetch it and read it:

```bash
curl -fsS "https://$(az functionapp show -n func-idealpoints -g rg-fantsybosty --query defaultHostName -o tsv)/api/season" | python -m json.tool
```

## The contract, as it actually responds

This is the real shape, captured from the deployed endpoint — not the spec.
Write your types from this.

```ts
export interface SeasonPayload {
  season: number;
  isCurrent: boolean;
  currentWeek: number;
  finalThroughWeek: number;
  asOfUtc: string;                              // ISO 8601
  slotOrder: string[];                          // radar axis order (R2) - never re-sort
  leagueSlotAverages: Record<string, number>;   // empty object until M6
  teams: TeamPayload[];
}

export interface TeamPayload {
  rosterId: number;
  name: string;
  totals: TotalsPayload;
  slotAverages: Record<string, number>;         // empty object until M6
  weeks: WeekPayload[];
}

export interface TotalsPayload {
  actual: number;
  ideal: number;
  sigma: number | null;                         // null below four final weeks (D1)
  cv: number | null;
  mean: number | null;
  min: number | null;
  max: number | null;
}

export interface WeekPayload {
  week: number;
  actual: number;
  ideal: number;
  isFinal: boolean;
}

export interface SeasonsPayload {
  current: number;
  available: number[];                          // [2026] until T29 runs
}
```

Three things about that shape that will shape your components:

**The five `totals` fields are null together or present together.** They are all
null below four finalized weeks. Do not write five separate null checks — one
guard decides whether the consistency column renders at all.

**`leagueSlotAverages` and `slotAverages` are empty objects, not missing keys.**
That is deliberate so M6 can fill them without a contract change. Bind to them
now and render nothing.

**`available` is `[2026]` and will stay that way** until T29 runs, which is
deferred. The season picker in T37 must still be built against the list rather
than hardcoding a year, but expect one option for a long time.

## What makes this look professional

Most of the difference between an amateur page and a professional one comes from
about six decisions, none of which require taste to execute — only discipline to
follow. If you do nothing else in T33, do these.

**One accent colour, used sparingly.** Everything else is a neutral grey. The
moment a second accent appears, the eye stops knowing what matters. Your accent
is for the mean tick, the hovered line, and focus rings. Nothing else.

**Few type sizes, few weights.** Four sizes and two weights is plenty for this
page. Amateur pages have nine sizes because each element was decided separately.

**Space is the design.** Generous, *consistent* space does more for perceived
quality than any colour or font choice. Every gap on the page should come from
one scale, never an arbitrary `13px`.

**Alignment beats decoration.** Numbers right-aligned with tabular figures, text
left-aligned, one consistent left edge down the page. Borders, shadows and
background fills are what people reach for when alignment is doing no work.

**Hairlines, not boxes.** One-pixel rules at low contrast separate things
adequately. Card borders, drop shadows and rounded boxes around everything is the
Bootstrap look, and it reads as dated.

**Restraint in motion.** One or two transitions at 120–180ms. Anything that
bounces, spins or slides across the page reads as amateur, and R1's reduced-motion
requirement means you need the no-motion version to look finished anyway.

The single biggest trap: **do not install a component library**. A five-row table
and one chart do not need one, and the result will look like every other
Tailwind/MUI page. The whole visual identity here is type, space and one accent.

## T33, T33b, T34, T35 · built

The design system, the panel layout, the standings table and the range mark are
done. `web/src/index.css` holds every token and every rule; there is no second
stylesheet and no component library. The page is four components:

```
App.tsx               the fetch, its three states, the masthead, the panel order
components/Panel.tsx  section.panel > .panel-inner > h2, id and aria-labelledby
  StandingsPanel.tsx  the league-wide scale, the table
  ChartPanel.tsx      a real scroll stop, empty until T36
  RadarPanel.tsx      built and gated; revealed by T48
  SigmaDisplay.tsx    the inline range mark
```

**Nine things this cost that the tasks did not predict.** Each is a rule now.

**`box-sizing: border-box` was never set.** `.panel-inner` is `width: 100%` plus
`padding: … var(--space-4)`, which under the default `content-box` is 100% + 32 px.
Every panel overflowed by exactly its own side padding and the page grew a
horizontal scrollbar that looked like a table-width bug. The table had been
collapsing correctly the whole time. Put the reset at the top of any stylesheet
before writing a single `width: 100%`.

**`display: contents` deletes the box, and everything attached to it.** It was
used to flatten `.masthead .panel-inner` into a flex row; it also discarded that
element's `max-width: 64rem`, `margin-inline: auto` and side padding, so the
masthead ran full-bleed with a flush-left title while the panel below stayed
centred at 64 rem. Put the row on the wrapper instead of removing the wrapper.

**`text-transform: uppercase` changes what a symbol means.** The `th` treatment
converted the sigma in the consistency header from σ (U+03C3) to Σ (U+03A3) —
standard deviation into summation. Greek letters and unit symbols need
`.symbol { text-transform: none }`. This is not a styling preference; the
uppercased header is simply wrong.

**A long column header sets the column width.** `σ · through wk 1` on one line
gave a two-character column 94 px. Split it: the symbol, then the qualifier in a
smaller, unletterspaced `.th-note` span underneath.

**`width: 99%` on the team column is what makes the numbers line up.** It absorbs
all the slack, so the numeric columns size to their content and sit in a block on
the right. It is the whole difference between this and a table whose columns
drift about with the data.

**A sticky masthead and sticky panels fight.** Later siblings paint over earlier
ones, so the masthead needs `z-index`, the panels need `top: var(--masthead-h)`,
and their height needs to be `calc(100svh - var(--masthead-h))` or panel one is a
full viewport tall *below* the masthead and opens with a band of dead space.

**Panels need an opaque background and a leading edge.** They overlap as they
pin. Without a background you see through them; with the same background on both
and no `border-top`, the covered panel looks clipped rather than covered.

**`Function` as a prop type disables type checking.** `scalingFunction: Function`
accepted any arguments and returned `any`, so a prop-name mismatch was the only
error the compiler could still see. Type the scale as `ScaleLinear<number, number>`
or as `(v: number) => number`.

**Truthiness is not a null check on a number.** `totals.min ? totals.min : -1`
substitutes a fallback for a legitimate `0`. The fix was not `??` but making the
nulls unreachable: `hasConsistency` narrows to the exported `ConsistentTotals`,
`SigmaDisplay` takes that type, and there is nothing left to guard.

### The one structural idea worth carrying into T36

`rangeScale` returns `ScaleLinear | null`, and `null` means "no team has four
finalized weeks yet". The column renders when the scale exists. There is no
second boolean that can disagree with it:

```tsx
function rangeScale(teams: TeamPayload[]) {
  const consistent = teams.map((t) => t.totals).filter(hasConsistency);
  const [lo, hi] = extent(consistent.flatMap((t) => [t.min, t.max]));
  return lo === undefined || lo === hi ? null : scaleLinear().domain([lo, hi]).range([2, 138]);
}
```

`extent` from `d3-array` returns `[undefined, undefined]` for an empty input
rather than `Math.min`'s `Infinity`, so the empty case is the function's
documented return and TypeScript narrows on it. Reach for the same shape in T36:
**a nullable scale is a better guard than a boolean beside a scale.**

## T36 · D3 line chart — 4.0 h  ±

This is the first task in the project that needs D3, so it starts with what D3
is, and — more usefully — what it is not.

### D3 is not a charting library

There is no `d3.lineChart()`. D3 is about thirty small, independent npm packages,
most of which you will never install. The famous ones do two very different jobs:

| Package | What it does | Using it here? |
|---|---|---|
| `d3-scale` | maths: maps data values to pixel positions | **yes** |
| `d3-shape` | maths: turns an array of points into an SVG path string | **yes** |
| `d3-array` | maths: `extent`, `max`, `bisector` | **yes** |
| `d3-selection` | DOM: `select().append().attr()` | **no** |
| `d3-axis` | DOM: draws an axis by appending elements | **no** |
| `d3-transition` | DOM: animates attributes over time | **no** |

The bottom three are the ones every D3 tutorial on the internet teaches first,
and they are the ones **D9 forbids**. They work by taking a DOM node and mutating
it — appending children, setting attributes, removing elements. React also owns
that node and also believes it decides what is inside it. Two owners, one node,
and the resulting bugs appear only after a re-render.

So the division of labour is:

> **D3 computes numbers. React renders elements.**

Every `<path>`, `<line>`, `<text>` and `<g>` in your chart is written by you in
JSX, using numbers D3 gave you. If you ever type `.append(`, you have wandered
into the wrong half of the library.

You already did this once without noticing. `rangeScale` in T35 is D3 — one
`scaleLinear`, one `extent`, and the SVG hand-written in `SigmaDisplay`. T36 is
the same pattern at a larger size.

### The four ideas you need

**1 · A scale is a function from data to pixels.**

```ts
const y = scaleLinear().domain([0, 200]).range([400, 0]);
y(0);    // 400
y(200);  // 0
y(100);  // 200
```

`domain` is the input, in your units — points. `range` is the output, in pixels.
That is the entire concept. `scaleLinear()` returns a function you then
configure, and each configuration method returns the scale again, so they chain.

Two methods you will use beyond calling it:

```ts
y.ticks(5);        // ~5 round numbers inside the domain: [0, 50, 100, 150, 200]
y.invert(220);     // 90 — pixels back to points, for T38's pointer handling
```

`ticks()` is why you do not need `d3-axis`. It gives you the numbers; you render
them yourself.

**2 · SVG's y axis points down.** `y=0` is the top edge. This catches everyone
once. You do not fight it — you invert the scale's range and never think about
it again:

```ts
.range([height - m.bottom, m.top])   // big pixel value first
```

Now a high score maps to a small y, which is up the screen, which is what a
reader expects. If your chart comes out upside down, this line is why.

**3 · A shape generator turns points into a path string.** `d3-shape`'s `line()`
does not draw anything. It is a function that takes your array and returns the
string that belongs in a `<path d="…">`:

```ts
import { line } from 'd3-shape';

const path = line<Point>()
  .x((p) => x(p.week))
  .y((p) => y(p.value));

path(points);   // "M44,310L92,255L140,288…"
```

You tell it how to get an x and a y out of one element; it handles the `M` and
`L` commands. Then:

```tsx
<path className="series" d={path(points) ?? undefined} stroke={colour} />
```

**4 · The margin convention.** An SVG clips at its edges, so axis labels and the
direct labels at the right need room reserved before you scale anything:

```ts
const m = { top: 16, right: 84, bottom: 28, left: 44 };
```

`left: 44` holds three-digit y labels, `right: 84` holds the team names, and the
scales' ranges start and end at those insets rather than at `0` and `width`. That
is why a label at the edge of the drawing area is never clipped: nothing is ever
drawn at the edge.

### Install

```bash
npm i d3-scale d3-shape d3-array
npm i -D @types/d3-scale @types/d3-shape @types/d3-array
```

Submodules, never the `d3` meta-package — that pulls in selection, transition,
drag, zoom and geo, none of which you want anywhere near this. The types ship
separately; `d3-scale` has none of its own.

### Shape the data first

Get the arrays right before touching a scale. One series per team, one point per
week, in week order, with the colour resolved once:

```tsx
interface Point { week: number; value: number }

interface Series {
  rosterId: number;
  name: string;
  colour: string;
  points: Point[];
}

const series: Series[] = ranked.map((team, i) => ({
  rosterId: team.rosterId,
  name: team.name,
  colour: `var(--team-${i + 1})`,
  points: cumulative(team.weeks),
}));
```

`i + 1` indexes the team tokens you already defined. Assign the colour from
**rank**, not from `rosterId` — roster ids are not 1…5, and a gap would reach for
`--team-7`, a different hue from the one the reader is being shown in the legend.

Weeks that have not happened are **absent from the array**, not zero. Do not
`fill(0)` them: zero is a real score a team could have had, and a line that dives
to the axis at week 15 is a claim about a game nobody has played.

### The scales

```tsx
const x = scaleLinear()
  .domain([1, maxWeek])
  .range([m.left, width - m.right]);

const y = scaleLinear()
  .domain([0, max(series, (s) => max(s.points, (p) => p.value)) ?? 0])
  .nice()
  .range([height - m.bottom, m.top]);
```

Three decisions are hiding in there:

**`.domain([0, …])` for a cumulative chart, not `extent`.** A running total is a
magnitude, and truncating a magnitude axis exaggerates differences — five teams
within 10 % of each other look like a rout. For T41's per-week and vs-median
modes the reverse holds: those are *differences*, zero means something in its own
right, and `extent` is correct. The axis baseline is a claim about the data, so
choose it per mode rather than once.

**`.nice()`** widens the domain out to round numbers, so the top tick reads
`1200` rather than `1187.4`. One method call, and it is most of what separates an
axis that looks designed from one that looks computed.

**`max` from `d3-array`** rather than `Math.max(...)`: it skips nulls, it does
not spread a large array onto the argument stack, and nested like this it reads
as "the largest value in any series".

### Axes, rendered by you

This is the part every tutorial does with `d3-axis` and a `.call()`. Here it is
twelve lines of JSX, and you keep control of every element:

```tsx
<g className="grid">
  {y.ticks(5).map((t) => (
    <g key={t}>
      <line x1={m.left} x2={width - m.right} y1={y(t)} y2={y(t)} />
      <text x={m.left - 8} y={y(t)} dy="0.32em" textAnchor="end">{t}</text>
    </g>
  ))}
</g>

<g className="x-axis">
  {x.ticks(Math.min(maxWeek, 9)).filter(Number.isInteger).map((t) => (
    <text key={t} x={x(t)} y={height - m.bottom + 18} textAnchor="middle">{t}</text>
  ))}
</g>
```

`dy="0.32em"` is the standard trick for vertically centring SVG text on its
anchor point. SVG aligns text on its baseline, not its middle, so a y-axis label
sits noticeably high without it.

`.filter(Number.isInteger)` matters because `ticks()` is free to hand you `2.5`,
and there is no week two-and-a-half. Capping the count keeps labels from
colliding on a narrow screen.

### What restraint looks like here

A correct chart that looks like a tutorial screenshot undoes the table's work.

- **No chart border, no plot-area background fill.** The page ground is the
  chart ground.
- **Horizontal gridlines only, and barely visible** — `--rule-soft`. Vertical
  gridlines almost never earn their place on a time axis.
- **No axis lines.** Tick labels alone imply the axis. If you keep one, keep the
  bottom and drop the left.
- **Line weight 1.75–2px.** Thinner reads as weak, thicker as clumsy.
- **`stroke-linecap: round` and `stroke-linejoin: round`.** A one-line change
  that makes every path look considered.
- **No dots on data points** unless there are few enough to be meaningful. At 18
  weeks, dots become noise.
- **Direct labels beat a legend.** With five teams you can put the team name at
  the right end of its own line, coloured to match. A reader never has to
  cross-reference. This is the single biggest upgrade over a default chart.

```css
.chart .grid line { stroke: var(--rule-soft); }
.chart .tick text { fill: var(--fg-muted); font-size: var(--text-xs); }
.chart path.series {
  fill: none;
  stroke-width: 1.75;
  stroke-linecap: round;
  stroke-linejoin: round;
}
```

**`fill: none` is not optional.** An SVG `<path>` fills by default, so a line
chart without it renders as five overlapping blobs. It is the most common
first-chart bug there is.

### Curves

`d3-shape` offers `.curve()` on the generator. Most of the options are wrong for
this chart:

- **`curveLinear`** (the default) — straight segments between real observations.
  Honest. Start here.
- **`curveMonotoneX`** — smooths without overshooting, and never invents a peak
  between two points. The only smoothing that is safe on data.
- **`curveBasis`** — looks lovely and **does not pass through your data points**.
  It is an approximating spline. Never use it for a data chart.

### Direct labels

```tsx
{series.map((s) => {
  const last = s.points.at(-1);
  return last ? (
    <text key={s.rosterId} x={x(last.week) + 8} y={y(last.value)} dy="0.32em"
          fill={s.colour} fontSize="var(--text-xs)">{s.name}</text>
  ) : null;
})}
```

With five teams they will occasionally collide. Do not reach for a
label-placement library — sort by final value and push any label within 12 px of
its neighbour. Ten lines, and it only has to be right for five.

### Gaps in the data

If a week can be missing from the middle of a series — a team with no matchup —
`.defined()` tells the generator to break the path rather than draw across the
gap:

```ts
line<Point>().defined((p) => Number.isFinite(p.value)).x(…).y(…)
```

A line drawn straight across a gap asserts data you do not have.

### Inside a panel

T33b gives the chart its own screen. Keep it shorter than the panel so there is
dead space to scroll through — `#chart .chart { max-height: 70svh }` — or
scrolling over the chart while trying to hover a line fights the reader.

### Make it responsive without a library

```tsx
const ref = useRef<HTMLDivElement>(null);
const [width, setWidth] = useState(720);

useEffect(() => {
  if (!ref.current) return;
  const observer = new ResizeObserver(([entry]) => setWidth(entry.contentRect.width));
  observer.observe(ref.current);
  return () => observer.disconnect();
}, []);
```

Then draw at that width with a fixed aspect. `ResizeObserver` is a dozen lines
and removes any reason to reach for a charting library.

Measure the **wrapper div**, never the SVG. Sizing an SVG from its own measured
width is a feedback loop that either oscillates or settles at zero.

### Done when

- No `d3-selection`, `d3-axis` or `d3-transition` in `package.json`.
- No `.append(`, no `.attr(`, no `useRef` pointing at an SVG node.
- `fill: none` on the series paths.
- The chart redraws correctly as the panel goes from 1400 px to 375 px.
- A season with one finalized week renders one point per team rather than
  crashing on an empty path string.

## T37 · Chart controls — 3.0 h

Controls are where pages most often start looking cheap. Three rules:

**Use a segmented control for small exclusive choices**, not a dropdown. Metric
and display mode have three or four options each — showing them is faster to
read and to click than hiding them behind a select.

```css
.segmented {
  display: inline-flex;
  gap: 1px;
  padding: 2px;
  background: var(--bg-subtle);
  border-radius: 6px;
}

.segmented button {
  border: 0;
  background: transparent;
  padding: var(--space-1) var(--space-3);
  font: inherit;
  font-size: var(--text-sm);
  color: var(--fg-muted);
  border-radius: 4px;
  cursor: pointer;
}

.segmented button[aria-pressed='true'] {
  background: var(--bg);
  color: var(--fg);
  font-weight: var(--weight-medium);
  box-shadow: 0 1px 2px oklch(0% 0 0 / 0.08);
}
```

That single soft shadow on the *selected* segment only is the whole effect. Do
not add shadows anywhere else on the page.

**Team toggles carry their colour.** A small swatch in each toggle, matching the
line. Deselected goes to `--fg-muted` with the swatch outlined rather than filled.

**Radius: 4–6px, everywhere or nowhere.** Mixing 4, 8 and 12 across a page is a
tell. One value, used consistently.

### Query-string state, without a router

State in the URL is D6 and the social point of the site: `?season=&teams=&metric=`
makes any view a link someone can paste into the league chat. You have no router
installed and you do not need one — but you do need to pick the right browser API.

```tsx
function useQueryState(key: string, fallback: string) {
  const [value, setValue] = useState(
    () => new URLSearchParams(window.location.search).get(key) ?? fallback,
  );

  const set = useCallback((next: string) => {
    setValue(next);
    const params = new URLSearchParams(window.location.search);
    if (next === fallback) params.delete(key);
    else params.set(key, next);
    const query = params.toString();
    window.history.replaceState(null, '', query ? `?${query}` : window.location.pathname);
  }, [key, fallback]);

  return [value, set] as const;
}
```

**React state is the source of truth; the URL is a mirror.** Making the URL
itself the state means re-reading `location.search` every render and inventing a
way to be notified when it changes — which is most of what a router is for. Hold
the value in `useState` and write it out on change.

**`replaceState`, not `pushState`.** `pushState` adds a history entry, so
toggling four teams on and off buries the previous page under eight presses of
Back. Use `pushState` only for a change a reader would expect Back to undo, and
on this page none of them are.

**Drop the parameter when it equals the default.** Otherwise a shared link reads
`?metric=cumulative&mode=total&season=2026`, restating defaults — and a default
you change later silently keeps the old behaviour for everyone holding an old
link.

**Encode the team set as a sorted, comma-joined list of roster ids** —
`?teams=2,4,7` — and validate it against the payload on read. A roster id from a
stale link that no longer exists must be dropped, not passed into a colour
lookup.

This hook does not react to Back and Forward. That is fine while every write is
`replaceState`, because there are no entries to go back to. The moment you add a
`pushState`, you owe a `popstate` listener as well.

## T38 · Hover interaction — 2.0 h

One state, three surfaces, concrete values:

```css
.chart path.series { transition: opacity 140ms ease, stroke-width 140ms ease; }
.chart path.series[data-muted='true'] { opacity: 0.18; }
.chart path.series[data-raised='true'] { stroke-width: 2.5; }

@media (prefers-reduced-motion: reduce) {
  .chart path.series { transition: none; }
}
```

**0.18, not 0.5.** A timid mute does not separate the raised line from the rest;
the point is that one line becomes obvious. **140ms, not 300ms** — long enough to
read as a transition, short enough not to feel sluggish while moving across rows.

**Sort the hovered series last rather than manipulating the DOM.** SVG has no
z-index; paint order is document order.

```tsx
const ordered = hovered === null
  ? series
  : [...series.filter(s => s.rosterId !== hovered),
     ...series.filter(s => s.rosterId === hovered)];
```

Hovering a **table row** should raise the same line. Same state, set from
`onMouseEnter` on `<tr>`, and a keyboard equivalent on the legend so the feature
exists for more than mouse users.

### Finding what the pointer is over

Do not put a mouse handler on each `<path>`. A 1.75 px line is a hard target, the
gaps between lines are dead, and a moving pointer fires a stream of enter and
leave events as it crosses them.

Instead put one transparent `<rect>` over the plot area and work the answer out
arithmetically. Every tutorial does this with `pointer` from `d3-selection`,
which D9 forbids — and which you do not need, because it is four lines of DOM
maths:

```tsx
function onMove(e: React.PointerEvent<SVGRectElement>) {
  const box = e.currentTarget.getBoundingClientRect();
  const week = Math.round(x.invert(e.clientX - box.left + m.left));
  setActiveWeek(Math.min(Math.max(week, 1), maxWeek));
}
```

`x.invert` is the scale run backwards — pixels to weeks — and it is the reason to
keep the scale in a variable rather than inlining it. `Math.round` snaps to the
nearest week, which is what makes a readout feel like it locks on rather than
drifting.

**Attach the handler to the rect, not to the SVG.** The SVG includes the margins,
so a pointer over the y-axis labels would otherwise report week 0. The `+ m.left`
above is because the rect starts at the margin, not at the SVG origin.

For the nearest *series* at that week, compare values directly — with five teams
a linear scan beats any index:

```tsx
const nearest = series.reduce<{ s: Series; d: number } | null>((best, s) => {
  const p = s.points.find((q) => q.week === week);
  if (!p) return best;
  const d = Math.abs(y(p.value) - pointerY);
  return best === null || d < best.d ? { s, d } : best;
}, null);
```

`d3-array`'s `bisector` is the right tool when the array is long enough that a
scan hurts. Eighteen weeks is not that, and `bisector` is the kind of
optimisation that costs a reader more than it saves the machine.

## T39 · Polling — 2.0 h

The UI question is what a refresh *looks* like. The answer is: almost nothing.

**Never return to a spinner on a poll.** `loading` is already derived so it is
true only before the first response — a poll replaces data in place. A page that
flashes a skeleton every thirty seconds is worse than a static one.

**A quiet freshness indicator instead.** One muted line, updated from `asOfUtc`:

```tsx
<p className="muted">Updated {formatRelative(payload.asOfUtc)}</p>
```

Use `Intl.RelativeTimeFormat` rather than writing "3 minutes ago" by hand, and
re-render it on a timer of its own — the string goes stale even when the data
does not, and a page that reads "Updated 0 minutes ago" for an hour is worse than
no indicator at all.

If you want to signal the moment of update, a 400ms background fade on changed
rows is the ceiling. Anything more is distracting during a live Sunday, which is
precisely when people are watching.

The mechanics — `If-None-Match`, visibility gating, `max-age=5` and why no
cache-busting parameter — are already implemented in `useSeason`.

## T40 · Responsive and accessibility — 2.5 h

**One breakpoint is enough.** This page is a table and a chart.

```css
@media (max-width: 40rem) {
  th, td { padding: var(--space-2); }
  /* drop the least important column rather than shrinking everything */
  .col-ideal { display: none; }
  /* the context line loses to the title on a phone */
  .masthead p { display: none; }
}
```

`box-sizing: border-box` and the `width: 99%` team column already do most of the
responsive work — the table collapses to 375 px unaided. Verify that before
adding rules for a problem you no longer have.

**The table scrolls in its own container**, and the page body never scrolls
sideways:

```css
.table-wrap { overflow-x: auto; }
```

**The chart needs a text equivalent.** An SVG of five paths is silent to a screen
reader:

```tsx
<svg role="img" aria-label={`Cumulative points by week for ${names.join(', ')}, weeks 1 to ${maxWeek}`}>
```

**Check contrast in both themes.** `--fg-muted` against `--bg` is the pair most
likely to fail. Target 4.5:1 for body text, 3:1 for large text and UI borders.

**Respect `prefers-reduced-motion`** for every transition, not just the chart —
that is R1, and the reduced-motion version has to look finished on its own.

### A finishing pass worth 20 minutes

- A real `<title>` — "Ideal Points" beats Vite's default `web`.
- A favicon that is not the Vite logo.
- `<meta name="theme-color">` for both schemes.
- `::selection` using `--accent-weak`.
- Check the page at 200% zoom; nothing should overlap.


---

# M5 · Modes, ops and docs · 9.5 h

Deliberately last. These are cheap, taste-driven, and much easier to judge against a season of real data than against three weeks of it.

## T41 · Display modes — 2.0 h

Three transforms over the same weekly arrays. All three are pure functions from
`WeekPayload[]` to `Point[]`, which is what makes this task two hours instead of
a rewrite — the chart already renders whatever `points` it is handed.

```ts
type Mode = 'cumulative' | 'perWeek' | 'vsMedian';
```

```ts
function cumulative(weeks: WeekPayload[]): Point[] {
  let total = 0;
  return weeks.map((w) => ({ week: w.week, value: (total += w.actual) }));
}

function perWeek(weeks: WeekPayload[]): Point[] {
  return weeks.map((w) => ({ week: w.week, value: w.actual }));
}
```

`vsMedian` is the only one that needs the other teams, because the median is a
league-wide figure per week:

```ts
function weeklyMedians(teams: TeamPayload[]): Map<number, number> {
  const byWeek = new Map<number, number[]>();
  for (const t of teams)
    for (const w of t.weeks)
      (byWeek.get(w.week) ?? byWeek.set(w.week, []).get(w.week)!).push(w.actual);

  return new Map([...byWeek].map(([week, vals]) => [week, median(vals) ?? 0]));
}
```

`median` comes from `d3-array` — it sorts a copy, handles the even-length average
and ignores nulls, which is three chances to get it subtly wrong by hand.

**A1 applies here and it is easy to miss.** The median must be taken over the
five scoring teams only. The dummy team is excluded from the standings by the
API, but if you ever compute a median from a list that includes it, every team's
line moves. The payload you receive is already filtered — the trap is recomputing
anything from a source that is not.

**What each mode is for:**

| Mode | Reads as | Axis baseline |
|---|---|---|
| `cumulative` | the standings, drawn | `0` — it is a magnitude |
| `perWeek` | volatility, week to week | `0`, or `extent` if you prefer the detail |
| `vsMedian` | who is beating the league, and when | `extent`, with a zero rule drawn |

The median mode changes no ranking — the sum of medians is an identical constant
for every team — but it removes the league-wide trend that dominates the raw
chart. Five near-parallel ascending lines are hard to read; the same five
diverging from a zero baseline are legible at a glance.

**Draw an explicit zero rule when `vsMedian` is active**, heavier than the
gridlines and in `--fg-muted` rather than `--rule-soft`. A diverging chart whose
zero is just another gridline loses the only thing that makes it readable.

**Switching mode changes the y domain**, so recompute it from the transformed
points rather than from the payload. This is the bug that makes a mode switch
look broken while being arithmetically fine: the lines move, the axis does not.

## T42 · Sigma variants — 2.0 h

**Expanding** (default): σ over weeks 1…w, converging as the season settles.
**Rolling**: σ over the last four weeks, showing whether a team is becoming
steadier or wilder — more interesting late, close to meaningless before week
eight.

**Raw σ vs coefficient of variation.** σ is scale-dependent: a team averaging 140
with σ = 25 is relatively steadier than one averaging 90 with σ = 22, and the raw
numbers say the opposite. CV is the fair comparison and is one division away —
`cv = sigma / mean`. The table keeps σ because it is the number people say out
loud; the chart offers both.

Both variants render as an em dash until enough final weeks exist — four for
expanding, four for rolling.

**σ here is the population form, ÷n (D1).** If you compute a rolling σ on the
client, use the same divisor the server uses, or the table and the chart will
disagree by a few percent and you will spend an evening deciding which is
correct. Better still: reuse the server's number where one exists and only
compute what the API does not send.

**Do not chart a σ that does not exist yet.** Below four final weeks every
variant is null for every team, and the honest chart is the absent one — not a
flat line at zero. The `rangeScale` shape from T35 applies again: a function that
returns the series or `null`, and a panel that renders the explanation when it is
`null`.

## T43 · Loading and error states — 2.0 h

| State | What the reader sees |
|---|---|
| Cold start, SQL resuming | A skeleton table with an honest line: waking the database, this takes up to a minute. Not a spinner that implies seconds. |
| Season not backfilled | The 404's available-seasons list, rendered as links. Never an empty table. |
| Fewer than four final weeks | Em dashes in the σ column with the reason on hover, not a zero and not a blank. |
| Sleeper unreachable | Finalized weeks still render from SQL; the current week is marked stale with its last-known timestamp. |

That last row is the design paying off: because finalized weeks never depend on
Sleeper, an outage degrades the site rather than breaking it. Make that visible
instead of failing whole.

### The skeleton is worth building properly

`App` currently renders `<p>Loading…</p>`, which is honest but reads as unfinished.
A skeleton table — the real `<table>`, the real headers, five rows of muted
blocks at the real column widths — costs twenty lines and removes the layout jump
when data arrives. Animate it with a slow opacity pulse or not at all; a sweeping
gradient shimmer is the dated version.

```css
@media (prefers-reduced-motion: reduce) { .skeleton { animation: none; } }
```

### Distinguish the two failures

`ApiError` already carries `status`, which is the whole reason it exists:

- **404** — the season is not backfilled. Read `available` out of the body and
  render those years as links. This is a *navigational* state, not an error.
- **5xx or a network throw** — something is broken. Say so plainly, keep any
  payload already on screen, and offer `refetch` as a button rather than
  reloading the page.

A poll that fails **must not** replace good data with an error screen. `useSeason`
already keeps `payload` when a later fetch throws — the error state has to respect
that and render the stale data with a warning rather than instead of it.

### Two invariants for this task

**Never an empty table with no explanation.** Every zero-row state has a reason,
and the reason is renderable: no seasons, season not backfilled, no finalized
weeks yet.

**Never a number you do not have.** `0.0` is a score somebody could have posted.
An em dash is the only honest rendering of an absent figure, and it is `—`
(U+2014), muted, everywhere on the page.

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

**This task is what reveals the radar panel.** T33b gates panel three on
`leagueSlotAverages` being non-empty, so shipping this payload turns the panel on
with no second deploy and no flag to flip. Until then the panel is reachable
locally with `?radar`.

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
