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

# M4 · The page · 25.5 h

The first milestone anyone else in the league can look at, and the first that
will generate opinions. The table is the primary object; the chart is the thing
people will play with.

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

## T33 · Design system — 3.0 h

T32 is done: Vite, React, the typed fetch layer, `useSeason`, the dev proxy and
the CI wiring all exist. `src/index.css` is a bare reset, deliberately, so this
task starts from nothing.

Everything on the page pulls from the tokens below. No literal colour, size or
spacing value appears anywhere else.

### 1 · Colour

Use **OKLCH**, not hex. It is perceptually uniform, which means lightening a
colour by 5% actually looks 5% lighter, and a neutral ramp built by varying one
number stays neutral. Browser support is universal now.

```css
:root {
  color-scheme: light dark;

  /* one hue for every neutral: 250 is a barely-blue grey that reads as
     "considered" where pure grey reads as "unstyled" */
  --bg:        oklch(99% 0.002 250);
  --bg-subtle: oklch(96.5% 0.004 250);
  --fg:        oklch(22% 0.01 250);
  --fg-muted:  oklch(52% 0.012 250);
  --rule:      oklch(90% 0.006 250);
  --rule-soft: oklch(94% 0.005 250);

  /* the one accent */
  --accent:    oklch(52% 0.13 165);
  --accent-weak: oklch(52% 0.13 165 / 0.12);

  /* five teams, evenly spaced in hue at constant lightness and chroma, so no
     line looks louder than another - the usual failure of a hand-picked palette */
  --team-1: oklch(58% 0.14 25);
  --team-2: oklch(58% 0.14 95);
  --team-3: oklch(58% 0.14 165);
  --team-4: oklch(58% 0.14 245);
  --team-5: oklch(58% 0.14 315);
}

@media (prefers-color-scheme: dark) {
  :root {
    --bg:        oklch(17% 0.008 250);
    --bg-subtle: oklch(21% 0.009 250);
    --fg:        oklch(94% 0.005 250);
    --fg-muted:  oklch(68% 0.011 250);
    --rule:      oklch(30% 0.01 250);
    --rule-soft: oklch(25% 0.009 250);

    --accent:    oklch(72% 0.13 165);
    --accent-weak: oklch(72% 0.13 165 / 0.16);

    /* lighter and slightly less saturated: the same values on a dark ground
       look muddy, which is why inverting a light theme never works */
    --team-1: oklch(70% 0.13 25);
    --team-2: oklch(70% 0.13 95);
    --team-3: oklch(70% 0.13 165);
    --team-4: oklch(70% 0.13 245);
    --team-5: oklch(70% 0.13 315);
  }
}
```

**Why this works and hand-picking does not.** Every neutral shares one hue, so
nothing clashes. Every team colour shares one lightness and one chroma, so no
line dominates. The dark theme raises lightness and drops chroma rather than
inverting, which is D9's "designed rather than inverted" made concrete.

### 2 · Type

```css
:root {
  --font: ui-sans-serif, system-ui, -apple-system, "Segoe UI", Roboto, sans-serif;

  /* four sizes. not five, not nine */
  --text-xs: 0.75rem;    /* column headers, axis labels */
  --text-sm: 0.875rem;   /* table body, controls */
  --text-md: 1rem;       /* body copy */
  --text-lg: 1.375rem;   /* the one page title */

  --weight-normal: 400;
  --weight-medium: 550;  /* the only emphasis you need */
}
```

The system stack costs nothing and looks native. If you want the page to feel
designed rather than default, one variable font from Google Fonts — **Inter** is
the safe choice — changes more than any other single decision. Self-host it or
accept the extra request; do not load six weights when you use two.

**Three type rules that matter more than the typeface:**

```css
/* every figure, everywhere. non-negotiable for a table of numbers */
.numeric { font-variant-numeric: tabular-nums; }

/* column headers: small, uppercase, letterspaced, muted. this single treatment
   is most of what makes a table look professional rather than default */
th {
  font-size: var(--text-xs);
  font-weight: var(--weight-medium);
  letter-spacing: 0.06em;
  text-transform: uppercase;
  color: var(--fg-muted);
}

/* headings do not need to be huge. one step up and a weight change is enough */
h1 { font-size: var(--text-lg); font-weight: var(--weight-medium); }
```

Without `tabular-nums`, `167.5` and `111.1` occupy different widths and the
column visibly wobbles. It is the single most noticeable difference between a
data table that looks built and one that looks thrown together.

### 3 · Space

One scale, every gap drawn from it. A `13px` anywhere means you guessed.

```css
:root {
  --space-1: 0.25rem;
  --space-2: 0.5rem;
  --space-3: 0.75rem;
  --space-4: 1rem;
  --space-6: 1.5rem;
  --space-8: 2rem;
  --space-12: 3rem;
}
```

**Be more generous than feels right.** The commonest amateur mistake is
cramming. Section gaps of `--space-12`, table cell padding of `--space-3`
vertical, and the page breathing inside a `max-width` will look sparse in your
editor and correct in a browser.

### 4 · The page frame

```css
body {
  margin: 0;
  background: var(--bg);
  color: var(--fg);
  font: var(--weight-normal) var(--text-md)/1.5 var(--font);
  -webkit-font-smoothing: antialiased;
}

/* main is the scroll container; the max-width lives on each panel's inner
   wrapper instead - see T33b */
main { display: block; }

.panel-inner {
  margin-inline: auto;
  max-width: 64rem;
  padding: var(--space-8) var(--space-4);
  width: 100%;
}
```

A `max-width` is not optional. Full-bleed text on a wide monitor is the clearest
possible signal that nobody thought about the layout. With panels it belongs on
the inner wrapper rather than on `main`, so each panel's background can still run
edge to edge.

### 5 · Focus, once, globally

```css
:where(a, button, select, [tabindex]):focus-visible {
  outline: 2px solid var(--accent);
  outline-offset: 2px;
  border-radius: 2px;
}
```

`:focus-visible` rather than `:focus` shows the ring for keyboard users and not
on mouse clicks. Never `outline: none` without a replacement — that is T40's
requirement and an accessibility failure, not a style choice.

## T33b · Panel layout — 1.5 h

Each visualisation gets its own screen, and you scroll between them. Not for
fashion: **the chart wants to be big**. Stacked as an ordinary document the table
takes the top of the viewport and the chart gets whatever is left. One panel each
gives both the room they need, and M6's radar becomes a natural third.

### What the reference sites actually do

Two sites, two different techniques, and only one is worth copying:

| Site | Technique |
|---|---|
| b-egg.farm | `gsap` + `CustomEase` + `lenis` + a `scrolldisable.js` — scroll-jacked with a smooth-scroll library |
| ellipsus.com | No animation library at all. `position: sticky` with `top: 64px` per section — native scrolling, sections pin as you pass them |

Copy **ellipsus**. It is plain CSS, it does not fight the browser, and it
degrades to ordinary scrolling when anything goes wrong.

**Do not install Lenis or GSAP.** b-egg needs them because it runs custom
animation timelines off scroll position. Here they would be roughly 70 KB and a
scroll hijack to reproduce an effect that is four CSS properties — and
smooth-scroll libraries are precisely what `prefers-reduced-motion` users turn
off.

### The panels

```
1  Standings table      the data, immediately
2  Season chart         the thing people play with
3  Positional radar     built now, hidden until M6 fills its data
```

**Panel one is the table, not a title card.** This is a live scoreboard: during a
Sunday someone opens it to check one number. Making them scroll past a hero to
reach the standings is a real cost, and the reason this pattern usually annoys
people on marketing sites.

### Revealing the radar panel

Build the third panel now and gate it, so the layout is designed for three from
the start rather than retrofitted. **Derive the gate from the data rather than a
literal flag** — the payload already says whether the radar has anything to show:

```tsx
// leagueSlotAverages is an empty object until T48 fills it, deliberately, so
// that M6 needs no contract change (see "The contract, as it actually responds")
const radarReady = Object.keys(payload.leagueSlotAverages).length > 0;

// ?radar lets you work on the panel before the API serves the data
const forced = new URLSearchParams(location.search).has('radar');

const showRadar = radarReady || forced;
```

Why this rather than `VITE_ENABLE_RADAR`:

- **Nothing to remember.** The panel appears the moment T48 ships, with no second
  deploy and no checklist item that gets skipped.
- **Nothing to remove.** A build-time flag leaves dead branches and a cleanup
  task; this expression stays correct forever and reads as a guard rather than a
  leftover.
- **It cannot lie.** A flag can be on while the data is absent, which renders an
  empty chart. This condition is true exactly when there is something to draw.

The `?radar` escape hatch is what makes it workable during M6: you can build and
style the panel against hand-written data locally while the deployed API still
returns `{}`.

**Two panels is thin for this pattern and three is where it earns its keep.** In
the meantime the page is standings then chart, which still reads fine — the
sticky stack degrades to an ordinary two-section scroll. Do not add a filler
panel to pad the count.

### The CSS

```css
.panels {
  /* proximity, never mandatory - see below */
  scroll-snap-type: y proximity;
}

.panel {
  position: sticky;
  top: 0;
  min-height: 100svh;
  scroll-snap-align: start;
  display: grid;
  align-content: center;
  background: var(--bg);
}

.panel > .panel-inner {
  margin-inline: auto;
  max-width: 64rem;
  padding: var(--space-8) var(--space-4);
  width: 100%;
}
```

**`100svh`, not `100vh`.** `svh` is the *small* viewport height, which excludes
the mobile browser chrome that collapses as you scroll. `100vh` on iOS Safari
gives a panel taller than the visible area, with content hidden under the
toolbar. That single unit is the difference between this working on a phone and
not.

**`proximity`, never `mandatory`.** Mandatory snap traps the user on any panel
taller than the viewport, breaks find-in-page, and makes keyboard scrolling
unpredictable. Proximity snaps when you are already close and otherwise leaves
scrolling alone.

**Each panel needs an opaque `background`.** They overlap as they pin; without a
background you see the previous panel through the current one.

### The chart panel must not fight the pointer

Scrolling over an interactive chart while trying to hover a line is the classic
failure of this pattern. Either give the chart panel enough padding that there is
dead space to scroll through, or size the chart shorter than the panel:

```css
.panel-chart .chart { max-height: 70svh; }
```

### Reduced motion turns it back into a document

```css
@media (prefers-reduced-motion: reduce) {
  .panels { scroll-snap-type: none; }
  .panel { position: static; min-height: 0; }
}
```

R1 requires this anyway, and it doubles as the test that matters: **if the page
does not look finished in this state, the scrolling was carrying the design
rather than the design carrying itself.** Check it before calling the milestone
done.

### Give each panel a heading and an id

```tsx
<section className="panel" id="standings" aria-labelledby="standings-h">
  <div className="panel-inner">
    <h2 id="standings-h">Standings</h2>
```

Ids make each panel linkable, which fits the query-string state from T37 — a
paste-able link to a specific view is the social point of the site. Headings keep
the document navigable for a screen reader, which a set of unlabelled
full-viewport `div`s is not.


## T34 · Standings table — 3.0 h

The primary object on the page. Five rows, five columns, and it has to be
readable without a single interaction.

### The columns

| Column | Align | Notes |
|---|---|---|
| Team | left | the only left-aligned column |
| Points | right | what decides the league; mark it as the sort |
| σ | right | em dash below four final weeks |
| Ideal | right | |
| Gap | right | signed, muted, em dash for the leader |

### The CSS that does the work

```css
table {
  width: 100%;
  border-collapse: collapse;
  font-size: var(--text-sm);
}

/* hairline rules, no zebra striping. striping is the dated look; a 1px rule at
   low contrast separates rows perfectly well and stays out of the way */
tbody tr { border-top: 1px solid var(--rule-soft); }
thead tr { border-bottom: 1px solid var(--rule); }

th, td {
  padding: var(--space-3) var(--space-3);
  text-align: right;
}

th:first-child, td:first-child {
  text-align: left;
  font-weight: var(--weight-medium);
}

td:not(:first-child) { font-variant-numeric: tabular-nums; }

/* the sort column, marked by weight and ground rather than a coloured header */
th[aria-sort], td.is-sorted { background: var(--bg-subtle); }
td.is-sorted { font-weight: var(--weight-medium); }

tbody tr:hover { background: var(--bg-subtle); }

.muted { color: var(--fg-muted); }
```

**Mark the sort column with `aria-sort`, not just colour.** It is the semantic
attribute a screen reader announces, and it gives you a free CSS hook:

```tsx
<th scope="col" aria-sort="descending">Points</th>
```

### Details that separate this from a default table

- **No vertical rules.** Column separators are almost never needed; alignment
  already separates the columns.
- **No borders around the table.** The header rule and row rules are enough.
- **Right-align every number including the header.** A right-aligned column with
  a left-aligned header looks broken.
- **The em dash is `—` (U+2014), not a hyphen.** Render it muted. A blank cell
  reads as a bug; a dash reads as "not yet".
- **The gap column is signed and muted:** `−17.3`, using a real minus sign
  (U+2212) rather than a hyphen so it aligns with the digits.
- **Do not colour the leader row.** Weight on the points cell is enough.

```tsx
const ranked = [...payload.teams].sort((a, b) => b.totals.actual - a.totals.actual);
const leader = ranked[0]?.totals.actual ?? 0;

const gap = (v: number) =>
  v === leader ? '—' : `\u2212${(leader - v).toFixed(1)}`;
```

### The header carries the week range

```tsx
<th scope="col">σ · through wk {payload.finalThroughWeek}</th>
```

Points already include `currentWeek`; sigma does not (R1). Saying so in the
header is what stops the column looking wrong during a live Sunday.

## T35 · Inline range mark — 2.5 h

A small SVG in the sigma cell giving the number a shape: a rule from the team's
minimum to maximum weekly score, a shaded band one sigma either side of the mean,
and a tick at the mean.

```tsx
<svg viewBox="0 0 140 16" width="140" height="16" role="img"
     aria-label={`${min} to ${max}, mean ${mean}, sigma ${sigma}`}>
  <line x1={s(min)} x2={s(max)} y1="8" y2="8" stroke="currentColor" opacity=".35" />
  <rect x={s(mean - sigma)} width={s(mean + sigma) - s(mean - sigma)}
        y="4" height="8" fill="var(--accent)" opacity=".22" />
  <line x1={s(mean)} x2={s(mean)} y1="2" y2="14" stroke="var(--accent)" strokeWidth="2" />
</svg>
```

**The scale `s` must be shared across every row**, computed from the league-wide
min and max rather than per team:

```tsx
const lo = Math.min(...teams.map(t => t.totals.min!).filter(Number.isFinite));
const hi = Math.max(...teams.map(t => t.totals.max!).filter(Number.isFinite));
const s = scaleLinear().domain([lo, hi]).range([2, 138]);
```

Per-team scales make every row look identical, which defeats the only purpose the
mark has. Render nothing when `sigma` is null — a degenerate mark is worse than
no mark.


## T36 · D3 line chart — 4.0 h  ±

The chart's visual craft matters as much as the maths. A correct chart that looks
like a tutorial screenshot undoes the table's work.

### What restraint looks like here

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

### Inside a panel

T33b gives the chart its own screen. Keep it shorter than the panel so there is
dead space to scroll through — `max-height: 70svh` — or scrolling over the chart
while trying to hover a line fights the reader.

### Margins are for labels, not decoration

```tsx
const m = { top: 16, right: 84, bottom: 28, left: 44 };
```

`right: 84` is space for the direct labels; `left: 44` is space for y-axis
numbers. A label at the edge of the drawing area is clipped by the SVG boundary —
the scale's range starts at `m.left`, not at zero, for exactly this reason.

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

Then draw at that width with a fixed aspect. `ResizeObserver` is a dozen lines and
removes any reason to reach for a charting library.

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

**State in the query string** (D6, and the social point of the site): reading and
writing `?season=&teams=&metric=` makes any view a link someone can paste into
the league chat.

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

## T39 · Polling — 2.0 h

The UI question is what a refresh *looks* like. The answer is: almost nothing.

**Never return to a spinner on a poll.** `loading` is already derived so it is
true only before the first response — a poll replaces data in place. A page that
flashes a skeleton every thirty seconds is worse than a static one.

**A quiet freshness indicator instead.** One muted line, updated from `asOfUtc`:

```tsx
<p className="muted">Updated {formatRelative(payload.asOfUtc)}</p>
```

If you want to signal the moment of update, a 400ms background fade on changed
rows is the ceiling. Anything more is distracting during a live Sunday, which is
precisely when people are watching.

The mechanics — `If-None-Match`, visibility gating, `max-age=5` and why no
cache-busting parameter — are already implemented in `useSeason`.

## T40 · Responsive and accessibility — 2.5 h

**One breakpoint is enough.** This page is a table and a chart.

```css
@media (max-width: 40rem) {
  main { padding-inline: var(--space-3); }
  th, td { padding: var(--space-2); }
  /* drop the least important column rather than shrinking everything */
  .col-ideal { display: none; }
}
```

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
