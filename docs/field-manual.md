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
- **No resource name appears in this file.** Set `RG` to your resource group
  once and every block below discovers the rest from it. The repository is
  going public and a name is an identifier (**A2**).
- `±` marks a soft estimate that could run double.
- Anything marked **Watch out** has already cost someone a day.

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

### Wired in

[`App.tsx`](../web/src/App.tsx) is the only place `LazyMotion` and `useReducedMotion` belong — it is the sole component that ever imports `motion/react`, and every other panel receives `m.*` elements only because they sit inside its tree.

The import and the `useReducedMotion()` call (`App.tsx:11,21`) are already there, but the `<LazyMotion>` wrapper is only around the two no-payload branches — the loading skeleton and the error state (`App.tsx:25–34`). The branch that actually renders `<main className="panels">` (`App.tsx:43–56`, holding `StandingsPanel`, `ChartPanel`, `RadarPanel`) is **not** inside it. That is fine today because nothing downstream uses `m.*` yet. The moment T52 turns `StandingsPanel`'s `<tr>` into `<m.tr>`, that changes: `m.tr` renders a plain `<tr>` with no animation capability unless a `LazyMotion` ancestor is mounted, and it will fail silently rather than error, which is the annoying way to find out.

Fix by moving the provider up to wrap the whole `return`, once, rather than duplicating it in each branch:

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

That also resolves the existing `if (reduced && !!m)` guard, which currently only intercepts the *reduced-motion* case for the loading/error branches and falls through unconditionally otherwise — reversed from what you want once `m.*` elements exist downstream. `LazyMotion` should wrap the tree regardless of `reduced`; `reduced` is consumed per-animation (zero duration), not used to decide whether the provider mounts at all.

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

### Wired in

The row is [`StandingsPanel.tsx:54`](../web/src/components/StandingsPanel.tsx), inside the `ranked.map(...)` in the `<tbody>`:

```tsx
{ranked.map((team) => (
  <m.tr layout key={team.rosterId}
        transition={{ type: "spring", stiffness: 400, damping: 40 }}>
```

`ranked` (`StandingsPanel.tsx:25`, `[...teams].sort((a, b) => b.totals.actual - a.totals.actual)`) is re-sorted every render — that line alone is what gives `m.tr layout` a reorder to animate. `teams` is a prop straight from `payload.teams`, and `payload` is a fresh object every time [`useSeason.ts`](../web/src/hooks/useSeason.ts)’s `load()` resolves: the 30 s `poll()` timer (`useSeason.ts:71–80`, gated on `isGameWindow` so it is inert outside game hours) and tab-focus (`onVisible`, `useSeason.ts:76–78`) both call it. Either fires `setLoaded`, `App.tsx` re-renders, `StandingsPanel` re-sorts, and `m.tr`’s FLIP measurement is what turns "the array changed order" into rows sliding past each other.

There is no `hasLoadedOnce` flag in the codebase yet — the watch-out above pre-empts one T52 needs to add, not one already there. `layout` alone is safe without it, because nothing here mounts or unmounts on a poll, rows only reorder. But the instant *any* `initial={...}` is added anywhere under `<main className="panels">` — in `ChartPanel` or `RadarPanel`, not only this table — it needs the same gate or the poll replays it. The natural home for the flag is a `useRef(false)` set on the first non-null `payload` in `App.tsx`, next to the existing `coldStartHandler` (`App.tsx:19`).

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

### Wired in

**Line chart.** The path is [`Chart.tsx:180–188`](../web/src/components/Chart.tsx), inside `ordered.map((s) => ...)`:

```tsx
<m.path key={s.rosterId} className="series" d={path(s.points) ?? undefined}
        initial={{ pathLength: 0 }} animate={{ pathLength: 1 }}
        transition={{ duration: 0.8, ease: "easeOut" }}
        stroke={s.colour} ... />
```

The mode/metric switch this watch-out warns about is real and already wired to reach this exact element: [`ChartPanel.tsx`](../web/src/components/ChartPanel.tsx) holds `mode`/`metric` state from `useEnumQuery` (`ChartPanel.tsx:123–124`, backed by `useQueryState`’s plain `useState`), recomputes `series` on every change (`ChartPanel.tsx:139–143`) and passes it straight through to `<Chart>` (`ChartPanel.tsx:204–219`) with no key or remount in between. Because `key={s.rosterId}` (`Chart.tsx:181`) does not change when `mode` or `metric` does, React reuses the same `<path>` DOM node and only patches its `d` attribute — which is precisely the interpolation this watch-out says not to trust.

The crossfade the watch-out recommends needs the node to actually remount on a mode/metric change, which means the key has to include them:

```tsx
key={`${s.rosterId}-${mode}-${metric}`}
```

passed down from `ChartPanel` (or built inside `Chart` itself, since it already receives `series` per mode/metric — either is fine as long as the key changes with the display mode, not only the roster). Pair that with `initial={{ opacity: 0 }} animate={{ opacity: 1 }} exit={{ opacity: 0 }}` and wrap the `<g>` of lines in `<AnimatePresence>`, since a plain key change alone would pop the old path out instantly rather than cross-fade it — `AnimatePresence` is what holds the outgoing node around for its exit animation.

**Radar.** The polygon is [`Radar.tsx:117`](../web/src/components/Radar.tsx):

```tsx
<m.polygon className="shape" points={polygon}
           style={{ originX: "50%", originY: "50%" }}
           initial={{ scale: 0 }} animate={{ scale: 1 }}
           transition={{ type: "spring", stiffness: 300, damping: 30 }} />
```

`points` is precomputed screen coordinates (`computeVertices`, `Radar.tsx:31–39`, centred on the fixed `Center = ViewBox / 2`), so a CSS `scale` transform from that same centre is exactly the "scale from the centre" the manual text describes — no extra geometry needed, `originX/originY` just has to match `Center`. The reference circle one line above (`Radar.tsx:115`, `<circle className="reference">`) is deliberately left a plain `<circle>`, not `<m.circle>` — that is the "should not animate at all" instruction made concrete: nothing in this file should ever add motion props to it.

Both of these render inside `RadarPanel`’s `Drawing` component (`RadarPanel.tsx:89–97`), which is also what T54 below hooks into.

## T54 · Radar/Bars transition — 1.0 h

**Correction, 2026-09-21: there is no section router to wire this into.** The original artifact text assumed a `Section` type and a `setSection` state switch — nothing like that exists. This site is one scrolling page: `App.tsx:49–53` mounts `StandingsPanel`, `ChartPanel` and `RadarPanel` as permanent siblings inside `<main className="panels">`, and [`index.css:163`](../web/src/index.css) (`.panels { scroll-snap-type: y proximity; }`) plus each `.panel`'s `scroll-snap-align: start` (`index.css:179–185`) is what makes scrolling past one settle on the next. There is nothing to transition *between*, because nothing is ever unmounted or swapped for a sibling — the reader just scrolls.

There is exactly one place in the app where a View Transition has anything real to do: [`RadarPanel.tsx`](../web/src/components/RadarPanel.tsx)'s `view` toggle. `Drawing` (`RadarPanel.tsx:89–97`) renders either `<SlotBars>` or `<Radar>` depending on `view`, and those are structurally unrelated subtrees (an SVG polygon-and-spokes drawing versus a list of horizontal bar rows) — not a prop change on the same element the way the chart's mode switch is. That is exactly the case View Transitions is for: a DOM subtree that genuinely replaces itself, where a native crossfade beats hand-building one.

### Wired in

`view` is `useEnumQuery('view', Views)` (`RadarPanel.tsx:106`), which resolves to the same plain `useState` setter as everything else that reads a query param (`useQueryState.ts:10`, `setValue`). That setter is a normal synchronous state update, so it is exactly what `flushSync` needs to be wrapped around — `startViewTransition`'s callback has to have already committed the DOM change by the time it returns, and a bare `setState` doesn't guarantee that on its own.

```tsx
const [view, setViewRaw] = useEnumQuery('view', Views);

function setView(next: View) {
  if (!document.startViewTransition) return setViewRaw(next);
  document.startViewTransition(() => flushSync(() => setViewRaw(next)));
}
```

`setView` (the wrapped one, not `setViewRaw`) is what gets passed to `<SegmentedRow name="view" ... onChange={setView} />` at `RadarPanel.tsx:125` — the existing line, unchanged except for which function it points at.

Scope the transition to the drawing itself, or the whole viewport crossfades — including the masthead, the standings table above it, anything on screen — which reads as a page reload, not a control toggling:

```css
.radar-wrap { view-transition-name: radar-drawing; }
```

on the wrapper div at `RadarPanel.tsx:128` (`<div className="radar-wrap" ref={wrapper}>`). Only that element participates in the transition; everything else updates instantly as it always did.

Supported in Chromium and Safari; check current Firefox support before leaning on it, and note that the guard in `setView` above means you never have to care — without `startViewTransition`, it degrades to calling `setViewRaw` directly, same as today.

Motion's `AnimatePresence` does the same job with more control and more code, and would live in `Drawing` itself rather than at the call site. Start with the native API above; reach for `AnimatePresence` only if the crossfade needs exit animations the native API can't express (it can't stagger the bars in the Bars view, for instance).

### Reference

- **MDN** — [View Transition API](https://developer.mozilla.org/docs/Web/API/View_Transition_API)

## T55 · Football loader — 2.5 h

A football arcing back and forth while the site loads. Charming for four seconds, maddening for forty — so the whole design is about which wait it is bound to.

### Three timing bands

| Wait | What shows | Why |
|---|---|---|
| 0 – 400 ms | Nothing at all | The warm path is this fast. A loader that flashes for 80 ms is worse than none. |
| 0.4 – 4 s | The football | The band where a mascot is genuinely delightful. |
| 4 s + | Ball keeps going; the Skeleton's title explains | The cold-start case from D7. Past ten seconds people want an explanation, not a loop. `Skeleton` already switches its title to "Waking the database…" after 3 s (`useElapsedSince`), so `#boot` carries no copy of its own — see below. |

### It goes in index.html, not in React

The loader has to paint before the bundle has loaded, so it cannot be a component — a React loader can only appear once React is mounted, which is precisely the part you are waiting for. Inline the markup and CSS in `index.html`; the app removes `#boot` once the season fetch settles (payload arrived, or an error), not on mount — see **Wired in** below for why mount is the wrong trigger.

**Correction, 2026-09-21: `#boot` is not a full-screen overlay.** The first version gave it an opaque background and centred the ball and copy in the middle of the viewport — which meant it sat on top of, and hid, `Skeleton` (`src/components/Skeleton.tsx`), React's own loading state, for as long as it was up. `#boot` is transparent instead: a `position: fixed` positioning context with no fill, so `Skeleton` — and eventually the real page — shows through normally underneath it, and only the ball is actually painted.

**Correction, 2026-09-24: no copy in `#boot`.** It used to carry a `<p class="why">Waking the database. This can take up to a minute.</p>` that faded in at 4 s. Once `#boot` stopped hiding `Skeleton`, that line sat a few pixels above Skeleton's own title, which says "Waking the database…" on a slow load, so the page said the same thing twice. Skeleton's title does the explaining now. The visually hidden `.sr` span stays, so screen readers still hear something while `#boot` is up.

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

`pointer-events: none` matters once `#boot` stops being opaque: there is real, eventually-clickable content underneath it now (`Skeleton`, then the page itself), and `#boot` must never be a dead click zone sitting in front of it.

### A throw from edge to edge, in the band under the masthead

The ball travels the full width of the viewport and back, in the gap between the masthead and whatever is loading underneath. Two constraints come from that: it has to actually reach both edges (not just gesture toward them with a fixed-width arc), and its highest point has to stay clear of the masthead, which is a known, fixed height (`--masthead-h: 4rem` in `src/index.css` — duplicated here rather than imported, per the note at the top of this section).

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
> `left` carries the horizontal travel (percentages on a `position: fixed`/`absolute` box resolve against the viewport, which is what makes “edge to edge” track the actual window width rather than a fixed pixel arc like the first version's `offset-path` did). `translate` carries the vertical bulge. `scale` carries the flip. Three separate CSS properties, three separate `animation`s, running concurrently on the same element without stepping on each other — the same reasoning the manual already used for the narrow-screen toss variant this replaces (see below).
>
> **The flip has to land exactly on an edge.** `steps(1)` on the `flip` animation holds each keyframe's value until the next one, so the mirror happens in a single instant at 0%/50%/100% — precisely when `arc-x` also reaches 0%/50%/100%, i.e. exactly when the ball is momentarily at rest at an edge. Interpolating the flip smoothly, or landing it off those points, would show the ball moving backwards or turning inside-out mid-flight.
>
> **The arc is centred between the masthead and the Skeleton's title, not pinned under the masthead.** `--boot-mid` is the point halfway between the masthead's bottom edge and the top of the Skeleton's `<h2>`. The ball's centre swings 18px either side of it (19.2px is half the 64px ball's height), so the arc's midpoint sits exactly on that point. The first version pinned the arc a fixed 44px under the masthead, which left it hugging the header with a big empty gap below. That looked sloppy.
>
> The title is centred with its panel, so where it lands depends on the viewport. `App.tsx` measures both edges in a `useLayoutEffect` while `loading` is true, re-measures on resize, and writes `--boot-mid` onto `#boot`. Before React has mounted there is nothing to measure, so `index.html` estimates it: `calc(4rem + (100svh - 4rem - 283px) / 4)`, with 283px as the Skeleton's measured height. That matched the measurement exactly at both 375×812 and desktop, so the ball doesn't jump when the measurement takes over. If the Skeleton's height ever changes, update the 283.
>
> `max()` is the floor for short viewports, where the midpoint would come too close to the masthead. At the peak, the ball's top edge is never higher than `4rem + 8px`.
>

This replaces the original recipe's `offset-path` arc entirely — no SVG path string, no `offset-rotate`, no separate narrow-screen `rotate: -90deg` toss variant. Edge-to-edge via `left` is already responsive by construction, on any viewport width, so one animation now covers every screen size.

### Spin: roll the laces over the ball

A real spiral turns the ball about its long axis, which barely changes the silhouette. A CSS `rotate` would spin it end over end and read as a fumble instead. So the laces carry the spin. They roll up from the bottom rim, across the face, and away over the top, then spend the other half-turn on the far side, out of sight.

**Changed, 2026-09-24: this replaces the comic-book spin rings.** The first version drew arcs clipped inside the ball and slid them sideways, a cartoonist's way of showing spin. Even after moving them off the poles, so they no longer looked like a ring around the rim, they read as markings on the ball rather than as rotation. Moving the laces themselves is both simpler and more literal.

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
> **The keyframes sample a real rotation.** At angle *t* the laces sit at `y = −6.5 sin t` and are foreshortened to `scaleY(cos t)`, sampled every 30°. So they move fastest and look fullest across the middle, and flatten to nothing as they reach the rim. Plain linear motion would glide at a constant speed and look like the laces were sliding over the ball rather than turning with it.
>
> **Put the clip on a parent that never moves.** `clip-path` on the animated `.laces` path itself would move the clip with the laces, and they would never pass under the ball's edge. Clipping the untransformed parent `<g>` keeps the ball's outline fixed while the laces roll beneath it.
>
> **The far side is a jump made while invisible.** At 50% the laces are flat (`scaleY(.02)`) at the top rim. At 50.01% they jump to the bottom rim, still flat, and stay there for the rest of the cycle. That is the half-turn they spend behind the ball. The jump can't be seen, because there is nothing to see.
>
> **Keep the stitches small.** The original stitches ran nearly the full height of the ball (4.8–5.6 units of a 13.6-unit ball) at a heavier stroke, which looked comical. Four 2.4-unit stitches at a 0.9 stroke read as laces at 64 px.
>

The laces are hidden for half of every 0.6 s turn, like a real ball's. If that reads as flicker rather than spin, slowing `roll` is the first knob to try. Don't add a second set of laces 180° round: a football only has laces on one side.

The `scale: -1 1` flip (see the previous section) mirrors the speed lines so they stay behind the ball. It doesn't affect the roll, which is vertical, so the spin keeps the same direction both ways.

One sizing note: at 34 px none of this detail reads. Take the loader ball to about 64 px. It is the only thing on screen, so it can afford to be the hero.

> **Watch out**
>
> **First load only.** Like every other animation here, this must not reappear on the 30-second poll. Tie it to `#boot`, which is removed once and never recreated.
>

### Wired in

[`web/index.html`](../web/index.html) carries `#boot` as a sibling of `#root`, before the module script, so it is in the initial HTML payload and paints without waiting on any JS:

```html
<body>
  <div id="boot" role="status" aria-live="polite"> ... </div>
  <div id="root"></div>
  <script type="module" src="/src/main.tsx"></script>
</body>
```

`#root` is what [`main.tsx:6`](../web/src/main.tsx) calls `createRoot` on — empty until the bundle has downloaded, parsed and run.

**Removal is tied to `!loading`, not to mount.** An earlier version of this wiring removed `#boot` in a `useEffect(() => {...}, [])` — unconditionally, on React's first commit. That commit happens the moment the bundle has parsed and run, which is almost always well under a second, regardless of whether the season fetch has resolved. Since the fetch is the actual slow thing — the SQL cold start this whole loader exists for — removing `#boot` on mount tore it down before its own 0.4 s fade-in had even finished, on every load. The football was, in effect, never seen: confirmed live, with a fetch mocked to hang for several seconds, before and after the fix.

```tsx
useEffect(() => {
  if (!loading) document.getElementById('boot')?.remove();
}, [loading]);
```

placed in `App.tsx`. `App.tsx` already distinguishes "first paint" from everything after via `coldStartHandler = useElapsedSince(loading, 3000)` (`App.tsx:19`) and already renders a *second*, React-owned loading state, [`Skeleton`](../web/src/components/Skeleton.tsx), for as long as `loading` is true and `payload` is null. Because `#boot` no longer paints an opaque background (see the correction above), `Skeleton` is visible underneath it the whole time `#boot` is up — there is no flash and no gap between the two: `#boot` fades in over the top of whatever React has already rendered, and fades out (by being removed) once real data or an error has arrived, at which point `App` has already rendered that real content underneath, ready to be revealed.

On the warm path this still fires within the same fraction of a second as before — `loading` flips false as soon as the fast fetch resolves — so band one of the timing table ("nothing at all") is unaffected.

### Reference

- **MDN** — [individual transform properties (translate, scale)](https://developer.mozilla.org/docs/Web/CSS/translate)
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
    SQL_SERVER=$(az sql server list -g "$RESOURCE_GROUP" --query "[0].name" -o tsv | tr -d '
')
    echo "SQL_SERVER=$SQL_SERVER" >> "$GITHUB_ENV"
    az sql server firewall-rule create -g "$RESOURCE_GROUP" -s "$SQL_SERVER" -n "gh-${{ github.run_id }}" --start-ip-address "$IP" --end-ip-address "$IP"

- name: Apply migrations
  run: dotnet ef database update --project src/FantasyLeague.Data --startup-project src/FantasyLeague.Functions

- name: Close firewall
  if: always()
  run: az sql server firewall-rule delete -g "$RESOURCE_GROUP" -s "$SQL_SERVER" -n "gh-${{ github.run_id }}"
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