# Ideal Points — Field Manual

> **Living document.** It began as a conversion of the published artifact. M0–M2 are built and about a dozen of its instructions have been corrected against reality, so re-syncing from the artifact is destructive. Edit this file directly. `docs/amendments.md` holds the *reasoning* for every decision made after design v6.
>
> Updated 2026-09-13. Task IDs match [`tasks/todo.md`](../tasks/todo.md).

---

Assumes no prior familiarity with Azure, bicep, EF Core, D3, CSS or GitHub Actions. Background a step needs is given inline.

**Completed tasks are cut.** M0-M2, T24's build steps, T25-T28 and T32 are built. What remains of them is the Sleeper API traps in [`CLAUDE.md`](../CLAUDE.md); the reasoning is in [`amendments.md`](amendments.md) and [`lessons.md`](../tasks/lessons.md). This file lists what is left to build. Commands whose error message points somewhere unhelpful are flagged.

### Conventions

- Shell blocks are **bash**. On Windows use **Git Bash**, not `cmd` or PowerShell; `find`, `grep` and `$VAR` mean different things there.
- **No resource name appears in this file.** Set `RG` to the resource group once; every block derives the rest from it. The repository is going public, and a name is an identifier (**A2**).
- `±` marks a soft estimate; the actual can be 2×.
- **Watch out** marks a trap that has already cost a day.

# M7 · Motion and polish — optional · 7.5 h

Optional throughout. Without this milestone the site still does its job. Apply that test to each task before spending an hour on it.

## T51 · Motion setup — 1.0 h

`npm i motion`: the package formerly published as `framer-motion`, imported from `motion/react`. Chosen because it is declarative, so D9 holds: React owns every node; Motion animates props.

```
import { LazyMotion, domAnimation, m, useReducedMotion } from "motion/react";

<LazyMotion features={domAnimation}>
  <App />
</LazyMotion>
```

`m` with `LazyMotion` cuts the bundle to ≈ 1/5 of a plain `motion` import. The gain is first paint, not Static Web Apps' free bandwidth.

> **Watch out**
>
> `useReducedMotion()` is mandatory; T40 commits to honouring it. Read it once near the root and pass a duration of zero, rather than scattering conditionals. One opt-out is maintainable; a hundred are not.
>

### Wired in

[`App.tsx`](../web/src/App.tsx) is the only place for `LazyMotion` and `useReducedMotion`. It is the sole component that imports `motion/react`; other panels receive `m.*` elements only because they sit inside its tree.

The import and the `useReducedMotion()` call exist (`App.tsx:11,21`). `<LazyMotion>` wraps only the two no-payload branches: the loading skeleton and the error state (`App.tsx:25–34`). The branch rendering `<main className="panels">` (`App.tsx:43–56`: `StandingsPanel`, `ChartPanel`, `RadarPanel`) is **not** wrapped. This is harmless while nothing downstream uses `m.*`. Once T52 turns `StandingsPanel`'s `<tr>` into `<m.tr>`, it breaks: without a `LazyMotion` ancestor, `m.tr` renders a plain `<tr>` with no animation, and fails silently rather than with an error.

Fix: move the provider up to wrap the whole `return` once, instead of duplicating it per branch:

```tsx
return (
  <LazyMotion features={domAnimation}>
    {mastHead}
    {!payload ? (
      loading ? <Skeleton coldStart={coldStartHandler} /> : <LoadFailed error={error} onRetry={refetch} />
    ) : (
      <>
        {error ? <StaleBanner asOfUtc={payload.asOfUtc} onRetry={refetch} /> : null}
        <main className="panels">
          <StandingsPanel teams={payload.teams} finalThroughWeek={payload.finalThroughWeek} />
          <ChartPanel payload={payload} />
          {hasRadar ? <RadarPanel payload={payload} /> : null}
        </main>
      </>
    )}
  </LazyMotion>
);
```

This also resolves the existing `if (reduced && !!m)` guard. It intercepts only the *reduced-motion* case for the loading/error branches and falls through unconditionally otherwise: the reverse of what is needed once `m.*` elements exist downstream. `LazyMotion` wraps the tree regardless of `reduced`. `reduced` is consumed per animation (zero duration); it does not decide whether the provider mounts.

### Reference

- **Motion** — [Motion for React](https://motion.dev/docs/react)

## T52 · Table layout animation — 1.0 h

The highest-value animation on the site. One prop.

```
<m.tr layout key={team.rosterId}
      transition={{ type: "spring", stiffness: 400, damping: 40 }}>
```

When the standings re-sort mid-afternoon, rows slide past one another. This makes live updates visible: a number changing in place goes unnoticed; a row overtaking another does not.

`key` must be `rosterId`, never the array index; otherwise Motion animates the wrong rows into each other.

> **Watch out**
>
> **The 30-second poll replays any entrance animation.** This is the main trap. Any `initial={{ opacity: 0 }}` inside the polled subtree flashes every 30 s and makes the page unusable during games. Gate it on first load:
>
> `initial={hasLoadedOnce ? false : { opacity: 0, y: 8 }}` — `false` makes Motion jump straight to the target. `layout` is safe, because it animates changes, not mounts.
>

### Wired in

The row is [`StandingsPanel.tsx:54`](../web/src/components/StandingsPanel.tsx), inside the `ranked.map(...)` in the `<tbody>`:

```tsx
{ranked.map((team) => (
  <m.tr layout key={team.rosterId}
        transition={{ type: "spring", stiffness: 400, damping: 40 }}>
```

`ranked` (`StandingsPanel.tsx:25`, `[...teams].sort((a, b) => b.totals.actual - a.totals.actual)`) is re-sorted every render; that line gives `m.tr layout` a reorder to animate. `teams` is a prop from `payload.teams`. `payload` is a new object each time [`useSeason.ts`](../web/src/hooks/useSeason.ts)'s `load()` resolves. Two callers trigger it: the 30 s `poll()` timer (`useSeason.ts:71–80`, gated on `isGameWindow`, so inert outside game hours) and tab focus (`onVisible`, `useSeason.ts:76–78`). Either fires `setLoaded`; `App.tsx` re-renders; `StandingsPanel` re-sorts; `m.tr`'s FLIP measurement turns the changed order into sliding rows.

No `hasLoadedOnce` flag exists yet; the watch-out above describes one T52 must add. `layout` alone is safe without it, because a poll only reorders rows; nothing mounts or unmounts. Any `initial={...}` added under `<main className="panels">` (in `ChartPanel` or `RadarPanel` too, not only this table) needs the same gate, or the poll replays it. Put the flag in `App.tsx` as a `useRef(false)` set on the first non-null `payload`, next to `coldStartHandler` (`App.tsx:19`).

## T53 · Chart reveal and transitions — 2.0 h  ±

Line-chart draw-on is one line:

```
<m.path d={path} initial={{ pathLength: 0 }} animate={{ pathLength: 1 }}
        transition={{ duration: 0.8, ease: "easeOut" }} />
```

> **Watch out**
>
> **Do not tween the `d` string between display modes.** Switching cumulative / per-week / vs-median changes the path shape. Motion cannot interpolate arbitrary path data; the result is a smear or nothing. Two options: animate the underlying *values* and recompute the path each frame, or crossfade the old path out and the new one in.
>
> The estimate assumes crossfade, which is almost always sufficient. Use a path-interpolation library only if the crossfade has been seen and rejected.
>

Radar: animate the polygon in by scaling from the centre, not by morphing points, for the same reason. The reference circle does not animate at all; it is the fixed baseline the shape is read against.

### Wired in

**Line chart.** The path is [`Chart.tsx:180–188`](../web/src/components/Chart.tsx), inside `ordered.map((s) => ...)`:

```tsx
<m.path key={s.rosterId} className="series" d={path(s.points) ?? undefined}
        initial={{ pathLength: 0 }} animate={{ pathLength: 1 }}
        transition={{ duration: 0.8, ease: "easeOut" }}
        stroke={s.colour} ... />
```

The mode/metric switch the watch-out warns about already reaches this element. [`ChartPanel.tsx`](../web/src/components/ChartPanel.tsx) holds `mode`/`metric` state from `useEnumQuery` (`ChartPanel.tsx:123–124`, backed by `useQueryState`'s plain `useState`), recomputes `series` on every change (`ChartPanel.tsx:139–143`) and passes it to `<Chart>` (`ChartPanel.tsx:204–219`) with no key change or remount. `key={s.rosterId}` (`Chart.tsx:181`) does not change with `mode` or `metric`, so React reuses the `<path>` DOM node and patches only `d`: the interpolation the watch-out rejects.

Crossfade requires the node to remount on a mode/metric change, so the key must include them:

```tsx
key={`${s.rosterId}-${mode}-${metric}`}
```

Pass it from `ChartPanel`, or build it inside `Chart`, which already receives `series` per mode/metric. Either works if the key changes with the display mode, not only the roster. Add `initial={{ opacity: 0 }} animate={{ opacity: 1 }} exit={{ opacity: 0 }}` and wrap the `<g>` of lines in `<AnimatePresence>`. A key change alone removes the old path instantly; `AnimatePresence` keeps the outgoing node mounted for its exit animation.

**Radar.** The polygon is [`Radar.tsx:117`](../web/src/components/Radar.tsx):

```tsx
<m.polygon className="shape" points={polygon}
           style={{ originX: "50%", originY: "50%" }}
           initial={{ scale: 0 }} animate={{ scale: 1 }}
           transition={{ type: "spring", stiffness: 300, damping: 30 }} />
```

`points` are precomputed screen coordinates (`computeVertices`, `Radar.tsx:31–39`, centred on the fixed `Center = ViewBox / 2`). A CSS `scale` transform from that centre implements "scale from the centre" with no extra geometry; `originX/originY` must match `Center`. The reference circle one line above (`Radar.tsx:115`, `<circle className="reference">`) stays a plain `<circle>`, not `<m.circle>`. That implements "does not animate at all": never add motion props to it.

Both render inside `RadarPanel`'s `Drawing` component (`RadarPanel.tsx:89–97`), which T54 also hooks into.

## T54 · Radar/Bars transition — 1.0 h

**Correction, 2026-09-21: there is no section router to wire this into.** The original artifact text assumed a `Section` type and a `setSection` state switch; neither exists. The site is one scrolling page. `App.tsx:49–53` mounts `StandingsPanel`, `ChartPanel` and `RadarPanel` as permanent siblings inside `<main className="panels">`. [`index.css:163`](../web/src/index.css) (`.panels { scroll-snap-type: y proximity; }`) plus each `.panel`'s `scroll-snap-align: start` (`index.css:179–185`) makes scrolling settle on the next panel. Nothing is unmounted or swapped for a sibling, so there is nothing to transition *between*.

One place has real work for a View Transition: [`RadarPanel.tsx`](../web/src/components/RadarPanel.tsx)'s `view` toggle. `Drawing` (`RadarPanel.tsx:89–97`) renders `<SlotBars>` or `<Radar>` depending on `view`. These are structurally unrelated subtrees (an SVG polygon-and-spokes drawing vs. a list of horizontal bar rows), not a prop change on one element like the chart's mode switch. View Transitions targets this case: a DOM subtree that replaces itself, where a native crossfade beats a hand-built one.

### Wired in

`view` is `useEnumQuery('view', Views)` (`RadarPanel.tsx:106`), which resolves to the same plain `useState` setter as every query-param reader (`useQueryState.ts:10`, `setValue`). It is a normal synchronous state update, so wrap it in `flushSync`: `startViewTransition`'s callback must have committed the DOM change by the time it returns, and a bare `setState` does not guarantee that.

```tsx
const [view, setViewRaw] = useEnumQuery('view', Views);

function setView(next: View) {
  if (!document.startViewTransition) return setViewRaw(next);
  document.startViewTransition(() => flushSync(() => setViewRaw(next)));
}
```

Pass the wrapped `setView`, not `setViewRaw`, to `<SegmentedRow name="view" ... onChange={setView} />` at `RadarPanel.tsx:125`. That line is otherwise unchanged.

Scope the transition to the drawing. Otherwise the whole viewport crossfades (masthead, standings table, everything on screen), which reads as a page reload, not a control toggle:

```css
.radar-wrap { view-transition-name: radar-drawing; }
```

on the wrapper div at `RadarPanel.tsx:128` (`<div className="radar-wrap" ref={wrapper}>`). Only that element transitions; everything else updates instantly, as before.

Supported in Chromium and Safari; check current Firefox support. The guard in `setView` makes support irrelevant: without `startViewTransition` it calls `setViewRaw` directly, as today.

Motion's `AnimatePresence` does the same job with more control and more code, and would live in `Drawing`, not at the call site. Start with the native API. Use `AnimatePresence` only if the crossfade needs exit animations the native API cannot express (e.g. staggering the bars in the Bars view).

### Reference

- **MDN** — [View Transition API](https://developer.mozilla.org/docs/Web/API/View_Transition_API)

## T55 · Football loader — 2.5 h

A football arcs back and forth while the site loads. Acceptable for 4 s, irritating for 40 s, so the design is defined by which wait it is bound to.

### Three timing bands

| Wait | What shows | Why |
|---|---|---|
| 0 – 400 ms | Nothing at all | The warm path finishes in this band. An 80 ms loader flash is worse than none. |
| 0.4 – 4 s | The football | The band where a mascot is welcome. |
| 4 s + | Ball keeps going; the Skeleton's title explains | The D7 cold start. Past 10 s readers want an explanation, not a loop. `Skeleton` switches its title to "Waking the database…" after 3 s (`useElapsedSince`), so `#boot` carries no copy (see below). |

### It goes in index.html, not in React

The loader must paint before the bundle loads, so it cannot be a component: a React loader appears only after React mounts, which is the thing being waited for. Inline the markup and CSS in `index.html`. The app removes `#boot` when the season fetch settles (payload arrived, or an error), not on mount; **Wired in** below explains why.

**Correction, 2026-09-21: `#boot` is not a full-screen overlay.** The first version had an opaque background with the ball and copy centred in the viewport, so it covered `Skeleton` (`src/components/Skeleton.tsx`), React's own loading state, while up. `#boot` is now transparent: a `position: fixed` positioning context with no fill. `Skeleton`, and then the real page, shows through underneath; only the ball paints.

**Correction, 2026-09-24: no copy in `#boot`.** It carried `<p class="why">Waking the database. This can take up to a minute.</p>`, fading in at 4 s. Once `#boot` stopped hiding `Skeleton`, that line sat a few pixels above Skeleton's title, which reads "Waking the database…" on a slow load, so the page said it twice. Skeleton's title now explains. The visually hidden `.sr` span stays, so screen readers announce something while `#boot` is up.

```
<div id="boot" role="status" aria-live="polite">
  <svg class="ball" viewBox="0 0 40 24" aria-hidden="true"> <!-- see next section --> </svg>
  <span class="sr">Loading standings</span>
</div>
```

```
@keyframes fadein { to { opacity: 1 } }

#boot { position: fixed; inset: 0; pointer-events: none; opacity: 0; animation: fadein .25s ease .4s forwards; }
```

`pointer-events: none` is required because `#boot` is not opaque: clickable content (`Skeleton`, then the page) sits beneath it, and `#boot` must never be a dead click zone in front of it.

### A throw from edge to edge, in the band under the masthead

The ball crosses the full viewport width and back, in the gap between the masthead and the content loading below. Two constraints follow. It must reach both edges, not approximate them with a fixed-width arc. Its peak must clear the masthead, which has a fixed height (`--masthead-h: 4rem` in `src/index.css`, duplicated here rather than imported, per the note at the top of this section).

```
#boot .ball {
  position: absolute;
  top: max(calc(4rem + 26px), calc(var(--boot-mid) - 19.2px));   /* see below */
  left: 0;
  width: 64px;
  animation:
    arc-x 2.2s cubic-bezier(.37,0,.63,1) infinite,
    arc-y 2.2s cubic-bezier(.37,0,.63,1) infinite,
    flip  2.2s steps(1) infinite;
}
@keyframes arc-x { 0% { left: 0 } 50% { left: calc(100% - 64px) } 100% { left: 0 } }
@keyframes arc-y { 0%, 50%, 100% { translate: 0 18px } 25%, 75% { translate: 0 -18px } }
@keyframes flip   { 0% { scale: 1 1 } 50% { scale: -1 1 } 100% { scale: 1 1 } }
```

> **Three properties, not one `transform`**
>
> `left` carries the horizontal travel. Percentages on a `position: fixed`/`absolute` box resolve against the viewport, so "edge to edge" tracks the actual window width, unlike the fixed-pixel `offset-path` arc of the first version. `translate` carries the vertical bulge. `scale` carries the flip. Three separate properties, three separate `animation`s, running concurrently on one element without interference: the same reasoning the manual used for the narrow-screen toss variant this replaces (see below).
>
> **The flip must land exactly on an edge.** `steps(1)` on `flip` holds each keyframe's value until the next, so the mirror happens in one instant at 0%/50%/100%. `arc-x` reaches 0%/50%/100% at the same instants, when the ball is momentarily at rest at an edge. A smooth flip, or one off those points, shows the ball moving backwards or turning inside-out mid-flight.
>
> **The arc is centred between the masthead and the Skeleton's title, not pinned under the masthead.** `--boot-mid` is the midpoint between the masthead's bottom edge and the top of the Skeleton's `<h2>`. The ball's centre swings 18px either side of it (19.2px is half the 64px ball's height), so the arc's midpoint sits exactly on that point. The first version pinned the arc a fixed 44px under the masthead: it hugged the header and left a large empty gap below.
>
> The title is centred with its panel, so its position depends on the viewport. `App.tsx` measures both edges in a `useLayoutEffect` while `loading` is true, re-measures on resize, and writes `--boot-mid` onto `#boot`. Before React mounts there is nothing to measure, so `index.html` estimates it: `calc(4rem + (100svh - 4rem - 283px) / 4)`, with 283px the Skeleton's measured height. The estimate matched the measurement exactly at 375×812 and at desktop, so the ball does not jump when the measurement takes over. If the Skeleton's height changes, update the 283.
>
> `max()` is the floor for short viewports, where the midpoint would come too close to the masthead. At the peak, the ball's top edge is never higher than `4rem + 8px`.
>

This replaces the original recipe's `offset-path` arc entirely: no SVG path string, no `offset-rotate`, no separate narrow-screen `rotate: -90deg` toss variant. Edge-to-edge via `left` is responsive at any viewport width, so one animation covers every screen size.

### Spin: roll the laces over the ball

A real spiral turns the ball about its long axis, which barely changes the silhouette. A CSS `rotate` spins it end over end, which reads as a fumble. So the laces carry the spin. They roll up from the bottom rim, across the face and over the top, then spend the other half-turn on the far side, hidden.

**Changed, 2026-09-24: this replaces the comic-book spin rings.** The first version slid arcs, clipped inside the ball, sideways. Even after they were moved off the poles, so they no longer looked like a ring around the rim, they read as markings on the ball, not rotation. Moving the laces is simpler and more literal.

```
<svg class="ball" viewBox="0 0 40 24">
  <defs>
    <!-- slightly pointed ends; an ellipse read too round -->
    <path id="shape" d="M9 12 C11.5 2.93 28.5 2.93 31 12 C28.5 21.07 11.5 21.07 9 12Z"/>
    <clipPath id="skin"><use href="#shape"/></clipPath>
  </defs>

  <g class="speed">                        <!-- outside: velocity -->
    <path d="M6 8 q-5 4 0 8"/>
    <path d="M1 10 q-4 2 0 4"/>
  </g>

  <use href="#shape" fill="#7A4A24"/>

  <g clip-path="url(#skin)">               <!-- the clip stays still -->
    <path class="laces" d="M15.5 12h9M17 10.8v2.4M19 10.8v2.4M21 10.8v2.4M23 10.8v2.4"
          stroke="#F6F1E8" stroke-width="0.9" stroke-linecap="round"/>
  </g>
</svg>
```

```
.laces { transform-box: fill-box; transform-origin: center; animation: roll .6s linear infinite; }

@keyframes roll {
  0%     { transform: translateY(6.5px)   scaleY(.02) }
  8.33%  { transform: translateY(5.63px)  scaleY(.5)  }
  16.67% { transform: translateY(3.25px)  scaleY(.87) }
  25%    { transform: translateY(0)       scaleY(1)   }
  33.33% { transform: translateY(-3.25px) scaleY(.87) }
  41.67% { transform: translateY(-5.63px) scaleY(.5)  }
  50%    { transform: translateY(-6.5px)  scaleY(.02) }
  50.01%, 100% { transform: translateY(6.5px) scaleY(.02) }   /* far side */
}
```

> **What makes it work**
>
> **The keyframes sample a real rotation.** At angle *t* the laces sit at `y = −6.5 sin t`, foreshortened to `scaleY(cos t)`, sampled every 30°. They move fastest and appear fullest across the middle, and flatten to nothing at the rim. Linear motion would glide at constant speed and look like the laces sliding over the ball, not turning with it.
>
> **Put the clip on a parent that never moves.** `clip-path` on the animated `.laces` path moves the clip with the laces, so they never pass under the ball's edge. Clipping the untransformed parent `<g>` keeps the ball's outline fixed while the laces roll beneath it.
>
> **The far side is a jump made while invisible.** At 50% the laces are flat (`scaleY(.02)`) at the top rim. At 50.01% they jump to the bottom rim, still flat, and stay there for the rest of the cycle: the half-turn behind the ball. The jump is not visible, because the laces are flat throughout it.
>
> **Keep the stitches small.** The original stitches spanned nearly the ball's full height (4.8–5.6 units of a 13.6-unit ball) at a heavier stroke and looked comical. Four 2.4-unit stitches at a 0.9 stroke read as laces at 64 px.
>

The laces are hidden for half of each 0.6 s turn, as on a real ball. If this reads as flicker, not spin, slow `roll` first. Do not add a second set of laces 180° round: a football has laces on one side only.

The `scale: -1 1` flip (previous section) mirrors the speed lines so they stay behind the ball. It does not affect the roll, which is vertical, so the spin direction is the same both ways.

Sizing: at 34 px none of the detail reads. Make the loader ball ≈ 64 px. It is the only thing on screen, so it can be the focal element.

> **Watch out**
>
> **First load only.** Like every animation here, it must not reappear on the 30-second poll. Tie it to `#boot`, which is removed once and never recreated.
>

### Wired in

[`web/index.html`](../web/index.html) places `#boot` as a sibling of `#root`, before the module script, so it is in the initial HTML payload and paints without waiting for JS:

```html
<body>
  <div id="boot" role="status" aria-live="polite"> ... </div>
  <div id="root"></div>
  <script type="module" src="/src/main.tsx"></script>
</body>
```

`#root` is what [`main.tsx:6`](../web/src/main.tsx) calls `createRoot` on. It stays empty until the bundle has downloaded, parsed and run.

**Removal is tied to `!loading`, not to mount.** An earlier version removed `#boot` in a `useEffect(() => {...}, [])`: unconditionally, on React's first commit. That commit happens once the bundle has parsed and run, almost always < 1 s, whether or not the season fetch has resolved. The fetch is the slow part (the SQL cold start this loader exists for), so removal on mount tore `#boot` down before its 0.4 s fade-in finished, on every load. The football was never seen. Confirmed live, with a fetch mocked to hang for several seconds, before and after the fix.

```tsx
useEffect(() => {
  if (!loading) document.getElementById('boot')?.remove();
}, [loading]);
```

placed in `App.tsx`. `App.tsx` already separates first paint from later renders via `coldStartHandler = useElapsedSince(loading, 3000)` (`App.tsx:19`). It also renders a *second*, React-owned loading state, [`Skeleton`](../web/src/components/Skeleton.tsx), while `loading` is true and `payload` is null. `#boot` has no opaque background (see the correction above), so `Skeleton` is visible beneath it the whole time, with no flash and no gap. `#boot` fades in over whatever React has rendered, and is removed once data or an error arrives; by then `App` has rendered the real content beneath it.

On the warm path removal still fires within the same fraction of a second: `loading` flips false when the fast fetch resolves, so timing band one ("nothing at all") is unaffected.

### Reference

- **MDN** — [individual transform properties (translate, scale)](https://developer.mozilla.org/docs/Web/CSS/translate)
- **MDN** — [prefers-reduced-motion](https://developer.mozilla.org/docs/Web/CSS/@media/prefers-reduced-motion)


---

# Deferred · the rest of M3

Moved behind M4 on 2026-09-14. None is blocked on effort; each waits on something that does not exist yet. Rationale for each: [`tasks/todo.md`](../tasks/todo.md) under *Deferred*.

## T29 · Season chain walk — 2.0 h

**Corrected (A34): built differently.** No `League` table and no upsert: `SeasonChain` walks the chain on demand and caches it for 24 h. A completed season's weeks come from SQL and its names and slots from Sleeper for its own league ID. `League:SeasonOverrides` is deferred. The text below is the original plan.

Sleeper links each season's league to the previous one. Start at the configured league ID, follow `previous_league_id` backwards, and upsert one `League` row per hop.

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

The `seen` set is required: a malformed chain pointing at itself would loop forever against a rate-limited API. Sleeper uses the string `"0"` as well as null for "no previous league"; test both.

After the walk, merge `League:SeasonOverrides` from configuration, for seasons the chain cannot reach.

**Watch out — this is unexercisable today.** 2026 is the first season (A4), so `previous_league_id` is null and the loop runs once. Write it and test it against fixtures. Its first real run is the August 2027 rollover.

## T30 · Historical backfill job — 2.5 h

An explicitly triggered function (not a timer, not a request path) that takes a season, walks weeks 1 to 18, and writes every `WeekScore` with `IsFinal = true`.

≈ 21 calls per season. It goes through the T11 token bucket like everything else, so it self-throttles and takes ≈ 30 s. Make it idempotent: upsert on the composite key, so re-running after a solver fix is safe and expected.

**Watch out — do not wire this to a missing-season request.** A visitor would wait 20 s for a page, and a crawler hitting `?season=1998` repeatedly would generate bursts against Sleeper for seasons that never existed. The API already returns 404 with the available list. Backfill is run deliberately.

## T31 · Migrations in the pipeline — 2.5 h  ±

The GitHub runner has a different IP on every run, so the SQL firewall must be opened for it and closed again, including when the migration fails.

**Corrected (A31):** the deploy no longer opens a firewall rule. Hosted runners reach SQL through the "allow Azure services" rule; the steps below are history.

```yaml
- name: Open firewall for runner
  run: |
    IP=$(curl -s https://api.ipify.org)
    SQL_SERVER=$(az sql server list -g "$RESOURCE_GROUP" --query "[0].name" -o tsv | tr -d '\r')
    echo "SQL_SERVER=$SQL_SERVER" >> "$GITHUB_ENV"
    az sql server firewall-rule create -g "$RESOURCE_GROUP" -s "$SQL_SERVER" -n "gh-${{ github.run_id }}" --start-ip-address "$IP" --end-ip-address "$IP"

- name: Apply migrations
  run: dotnet ef database update --project src/FantasyLeague.Data --startup-project src/FantasyLeague.Functions

- name: Close firewall
  if: always()
  run: az sql server firewall-rule delete -g "$RESOURCE_GROUP" -s "$SQL_SERVER" -n "gh-${{ github.run_id }}"
```

**Watch out — `if: always()` on the close step is the whole point.** Without it, a failed migration leaves the database open to a public IP that will soon belong to another CI run.

Name the rule after the run ID, not a fixed `gh-runner`. Otherwise two concurrent deploys collide on one rule name, and the second one's close step deletes the first one's access mid-migration.

Soft-estimated, because the deploy identity needs both the firewall grant and schema rights, and a connection timeout and a permission denial look identical in the log.

`RESOURCE_GROUP` must be set as **job-level** `env:` from `secrets.AZ_RESOURCE_GROUP_NAME`. The `secrets` context is not available in workflow-level `env:`, and an unquoted empty value makes `-g` swallow the next flag instead of failing cleanly.