# Native performance review

A review of Ranvier.Anime on the native WebCore host, measured in the Motion example (`examples/Ranvier.Motion.Native`)
on Windows x64 with the GPU runtime, in a hidden window, Release build.

## Where a frame goes

During a drag on the physics page, a frame takes about 18.5 ms in a hidden window:

| Phase | ms per frame |
|---|---|
| Native composite, inside `rv_host_acquire_d3d11_frame` | 17–18 |
| Page frame callbacks, where Anime runs | 0.2–0.3 |
| Input dispatch and host cycle | under 0.3 |

A CPU sample trace puts 85% of the wall time in the native frame acquire, and all of Ranvier.Anime at about 2%. A
visible window composited the same drag in about 3.5 ms, so the hidden window overstates the native cost. The number of
mouse moves per frame (1, 4 or 8) no longer changes the frame time, since the drag listener fix in `c5bbca3`.

## What changed

Anime's frame callbacks allocated about 104 KB per drag frame, a gen0 collection every one to three seconds. Now they
allocate about 39 KB.

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

## Semantics

Ranvier.Anime no longer tracks the JavaScript library's behaviour where a native design is better. The native `follow`
on a transform component owns that component. Another animation of the same component on the same element does not
take it over through Replace composition, as a composed animation per value did. `follow` takes its duration, delay,
ease, modifier and precision from the parameters; the target-aware `DelayBy` and `DurationBy` providers apply only to
the composed path that other properties still use. The browser `follow` is unchanged.

## Left

- **Per-render guards.** Most of the remaining 39 KB is the write-guard closures `Timer` and `AnimatableTrack` build
  on every render, and option-wrapped outputs. Removing them means replacing the guard closures with generation checks
  across the playback core.
- **The bridge.** About a third of a drag frame's allocation is in Ranvier.View.WebCore: style access, `setProperty`
  and DOM point objects.
- **Layer promotion.** Whether `will-change: transform` on moving elements turns their repaint into compositing
  depends on WebCore's compositing in the GPU runtime. It is unmeasured.
- **Two writes per follower.** A follower of `x` and `y` writes the element's transform twice a frame, once per track.
