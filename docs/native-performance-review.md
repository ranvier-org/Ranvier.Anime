# Native performance review

A review of Ranvier.Anime on the native WebCore host, measured in the Motion example (`examples/Ranvier.Motion.Native`)
on Windows x64 with the GPU runtime, in a hidden window, Release build.

## Where a frame goes

During a drag on the physics page, a frame takes about 18.5 ms in a hidden window:

| Phase | ms per frame |
|---|---|
| Native composite, inside `rv_host_acquire_d3d11_frame` | 17–18 |
| Page frame callbacks, where Anime runs | 0.15–0.2 |
| Input dispatch and host cycle | under 0.3 |

A CPU sample trace puts 85% of the wall time in the native frame acquire, and all of Ranvier.Anime at about 2%. A
visible window composited the same drag in about 3.5 ms, so the hidden window overstates the native cost. The number of
mouse moves per frame (1, 4 or 8) no longer changes the frame time, since the drag listener fix in `c5bbca3`.

## Allocation per drag frame

| | Frame callbacks | Sampled per 3 s drag |
|---|---|---|
| Before `496350e` | ~104 KB | — |
| After `496350e` | ~39 KB | 29.5 MB |
| After the second pass | ~12 KB | 16.7 MB, about half in the bridge |
| After the third pass | ~6.5 KB | — |

Measured side by side after the third pass, the whole drag frame (callbacks plus input dispatch) allocates:

| Mouse moves per frame | Second pass | Third pass |
|---|---|---|
| 1 | 13.7–13.9 KB | 7.2–7.4 KB |
| 4 | 19.8–19.9 KB | 8.3–8.7 KB |
| 8 | 25–33 KB | 8.8–10.6 KB |

Frame time in that run was 20–25 ms for both builds, the native composite having slowed on the host since the first
pass; the managed callbacks stayed at about 0.2 ms.

What is left in Anime is one output and a few strings per sampled value, and the transform text written once per
element per frame. The rest is Ranvier.View.WebCore: style access, `setProperty`'s pinned strings, DOM point objects
and event wrappers.

## First pass (`496350e`)

- **`Motion.follow` retargets one track.** On a transform component (`x`, `y`, `scaleX`, `rotate` and the rest) the
  native `follow` keeps one Animatable track for the part's life and retargets it on each value, writing through the
  element's transform cache. Before, every value started a new composed animation: tween building, a computed-style
  read through the bridge, regex value parsing and an inline transform parse. The physics page's five followers did
  that ten times a frame, 62% of all allocation.
- **Weak-table lookups.** `ElementTransforms.stateFor`, `EngineFrames.forScheduler`, `Engine.forScheduler` and
  `CompositionManager.forScheduler` try the table first, so a hit no longer allocates a factory delegate.
- **Engine queue.** A root's pending frame callback is a nullable field, not an option allocated every frame.
  `Timer.Generation` packs its two counters into one `int64` instead of a tuple.
- **Transform serialization.** `TransformCache.build` reuses one builder per thread and appends axis values directly.
- **Static lookups.** The fixed name sets (`ElementTransforms`, `DomTargetHelpers`, `WaapiHelpers`, and the native
  `Motion` module's transform and layout names) are `FrozenSet`s.

## Second pass

- **Guards without closures.** Write guards are checked only while the call that made them runs. So the track render,
  the track retarget, the composed render, `Timer.operate` and `AnimatableValue` each keep one guard closure over a
  generation snapshot, saved and restored around the call, instead of capturing a closure per call. A nested call
  still invalidates the outer one. `Timer.WriteToken` and `Timer.IsCurrent` check what `CaptureWriteGuard` captures.
- **Lease-free frame requests.** A timer requests frames from its engine root directly (`EngineRoot.Request`,
  `CancelRequest`) with one cached callback, so a retarget every frame no longer allocates a lease, an option and a
  closure. `FrameScheduler.Frame` still returns a lease for other callers.
- **One transform write per element per frame.** While an engine frame runs, `ElementTransforms.writeScalar` and the
  composed render stage components, and the element's `style.transform` is set once when the frame ends
  (`DeferredWrites`). A follower of `x` and `y` used to write twice. Reads through `ElementTransforms` apply staged
  components first; a follow cleanup discards them before restoring the inline declaration.
- **Lazy outputs.** `ValueInterpolation.Output` is a class whose text is formatted on first read, and whose `Number`
  is unboxed. A numeric writer never formats. `sampleOutput` and `sampleScalar` are the option-free cores of `sample`.
- **Retarget staging.** A track stages retargets into a reused spare array (fresh forks only for a nested
  retarget). Scalar endpoints live in fields, so a retarget copies no `Data` record. `RetargetScalarPrecise` and
  `AnimatableNumber.RetargetPrecise` take duration and ease unboxed.
- **Lazy group callbacks.** An `Animatable` without callbacks creates its callback animation only when `Callbacks` is
  read, so a follower's retargets no longer revive and play an animation nobody observes. Playing an already running
  forward callback animation is skipped.
- **Smaller fixes.** `PlaybackState`'s control members call their bodies directly instead of through a closure;
  a child sample's flag is a cached option; the draggable skips re-parsing its own inline transform when it is
  unchanged; scalar transform text is formatted in one allocation; the draggable's pointer reads return a struct
  option.

## Third pass

- **Cached closures that were methods.** An F# class `let` bound directly to a lambda compiles to a method, and each
  use of it as a value allocates a closure over `this`. The second pass's per-object guards (`Timer.operateCurrent`,
  `AnimatableValue`'s guard, the track's render and retarget guards, the composed render's guard) and the draggable's
  listeners were all such methods. They are bound through `Closure.keep`, which is not inlined, so each is a field
  holding one closure. `Timer.Pause` and `Complete` use cached actions too.
- **Numeric transform components.** `ElementTransforms.writeScalar` and the composed render's scalar transform outputs
  store a number and unit (`TransformCache.numeric` marks the component). The number is formatted straight into the
  frame's single transform text, so a sample no longer allocates a string per value. Paths that read or parse the cache
  materialize the text first. The declaration is built without the trailing space, so no second string is trimmed.
- **Draggable pointer mapping.** The inverse ancestor matrix's affine terms are cached when it updates, and a pointer
  move maps with arithmetic instead of a `DOMPointInit`, a bridge `DOMPoint` and its session entries.
- **Draggable reads.** `AnimatableNumber.Value` reads through `TryScalarValue`, without an option and two closures.
- **Draggable writes.** `DraggableTransformTarget` stages its axes during an engine frame and sets the transform once
  when the frame ends, with `setProperty` instead of building a CSS `Declaration`; values use shortest round-trip
  digits rather than `G17`. `setProperty`'s priority option is shared.

## Fourth pass: the native paint, and input rate

Measured in a visible window on a 164 Hz display, with SDL motion events pushed from another thread at 1 kHz:

| Page | Frame, rc.3 | Frame, rc.4 | Without blurred shadows |
|---|---|---|---|
| Dashboard | 20 ms | p50 6.2 ms, p95 19 ms | 6.1 ms |
| Graphs, mouse moving | 26 ms | p50 6.2 ms, p95 29 ms | 7 ms |
| Story | 6.1 ms | — | — |

- **Blurred box-shadows are the cost.** WebCore composites layers on the GPU but rasterizes their contents on the
  CPU, and re-blurs a `box-shadow` whenever the repainted area touches it. The pages' cards and tiles carry
  `0 6px 18px -8px` and `0 18px 36px -14px` shadows. Hiding a static header or changing the static body gradient also
  sped up a frame on rc.3, because rc.3 repainted the whole root layer each frame. rc.4 (Ranvier.WebCore#348) repaints
  only the dirty rectangle, which brings the median frame to the display's refresh. The fifth pass below removes the
  remaining blur cost.
- **Input rate.** The SDL window dispatches every motion event; a gaming mouse sends about 1,000 a second where a
  browser sends one `mousemove` per frame. A move costs 0.02 ms on a page without handlers and 0.22 ms and 14 KB on
  the graphs page, whose handler reads layout. Collapsing queued motion cut dispatched moves from 993 to 45 a second on
  the dashboard, but did not change frame time, which the paint dominates. Wheel scrolling shows the same pattern:
  the story page's frame grows from 14 ms at one wheel event per frame to 33 ms at sixteen.
- **Managed work stays small.** Kanban card drag, route switches (3 ms and 190 KB a click, a view mount), resize and
  scroll all spend under 1.5 ms a frame in managed code.
- **Budgets.** The Motion smoke run checks allocation per window turn for a four-move drag (under 36 KB, 8 KB
  measured), a live dashboard (under 24 KB) and sixteen hover moves (under 8 KB), so a return to the earlier costs fails.

## Fifth pass: the GPU-default runtime, against the browser

The app moved to Ranvier.WebCore.Native 0.1.1-rc.5, Ranvier.View.WebCore 0.1.0-rc.3 and Ranvier.View.WebCore.Sdl
0.1.0-rc.4. The default native package is now the GPU build, so the app no longer overrides its runtime.

Measured with the pointer moving, in a visible 1180x860 window on a 164 Hz display. The native run pushed SDL motion
at 1 kHz; the browser run was Ranvier.View's Motion example in Chromium, with one injected move per frame on the same
ellipse, at twice the speed:

| Page | Native frame p50 | Native p95 | Native worst | Chromium p50 | Chromium p95 | Chromium worst |
|---|---|---|---|---|---|---|
| Dashboard | 6.06 ms | 6.6–6.9 ms | 7–13 ms | 6.10 ms | 6.20 ms | 6.40 ms |
| Graphs | 6.0–6.1 ms | 6.9–7.5 ms | 29–63 ms | 6.10 ms | 6.20 ms | 12.1 ms |

- **Blur.** Removing every `box-shadow` no longer changes the graphs page's frame times.
- **Input rate.** The window dispatched 149–165 `mousemove`s a second from 1 kHz of motion, with no coalescing in the
  app.
- **Gaps that are not stalls.** The graphs page shows two gaps of about 420 ms per run, at fixed points of the path;
  halving the push rate stretches them to about 1,090 ms. The loop keeps turning at about 1 ms a step through them, and
  the page requests no frame. This is the pointer off the chart, and it inflates the native "worst" only if read from
  frame gaps, so the table takes the worst from single steps and gaps under 100 ms.

## Semantics

Ranvier.Anime no longer tracks the JavaScript library's behaviour where a native design is better:

- The native `follow` on a transform component owns that component. Another animation of the same component on the
  same element does not take it over through Replace composition, as a composed animation per value did. `follow`
  takes its duration, delay, ease, modifier and precision from the parameters; the target-aware `DelayBy` and
  `DurationBy` providers apply only to the composed path that other properties still use. The browser `follow` is
  unchanged.
- During an engine frame, transform components written through `ElementTransforms` reach `style.transform` when the
  frame ends, before the host paints. Code that reads the element's inline style or layout directly inside the same
  frame, rather than through `ElementTransforms`, sees the frame's earlier value.
- `Animatable.Callbacks` is created on first read when no callbacks were passed; read mid-flight, it starts playing.
- A native draggable's transform reaches `style.transform` when the engine frame ends, as the components above do.

Callbacks stay callbacks. The guards and lifecycle callbacks are internal invariants of the playback core; routing
them through the Ranvier graph would add a notification per write without changing what the user sees. The reactive
boundary stays where it is: `Motion` parts read signals with effects and drive the playback core.

## Left

- **First hover on a mounted graphs page.** One window step takes 29–63 ms, once per mount in most runs, where
  Chromium's worst frame is 12.1 ms. The page's frame callbacks take 0.1 ms of it, the step allocates 16–76 KB, and no
  GC runs. Hiding the tooltip, removing shadows, promoting the tooltip and lowering the host's idle wait do not change
  it. That leaves native work or the page's mouse handler reading layout; which one is not measured.
- **Frame consistency.** The native p95 is 0.4–1.3 ms above Chromium's on both pages, though the medians match.
- **The bridge.** About half of a drag frame's allocation is in Ranvier.View.WebCore, in another repository.
- **Layer promotion.** On rc.3, `will-change: transform` on the dashboard's animated elements brought the median
  frame from 20 ms to 6.6 ms, but the 95th percentile stayed at 21 ms. Putting each shadow on a promoted `::before` fixed
  the dashboard fully, but not a tooltip moving over shadowed cards. The rc.5 runtime makes both unnecessary.
- **One output per sample.** Each sample still allocates a `ValueInterpolation.Output`; a writer that reads only the
  number could take it unboxed.
- **Callback churn on drag.** Each drag frame retargets the axes with zero duration, which completes the group's
  callback animation and revives it on the next frame. It allocates little now, but does the work every frame.