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

Callbacks stay callbacks. The guards and lifecycle callbacks are internal invariants of the playback core; routing
them through the Ranvier graph would add a notification per write without changing what the user sees. The reactive
boundary stays where it is: `Motion` parts read signals with effects and drive the playback core.

## Left

- **The bridge.** About half of a drag frame's allocation is in Ranvier.View.WebCore, in another repository.
- **Layer promotion.** Whether `will-change: transform` on moving elements turns their repaint into compositing
  depends on WebCore's compositing in the GPU runtime. It is unmeasured.
- **Per-value strings.** Each sampled transform value is still formatted to a string for the transform cache, which
  stores text.
