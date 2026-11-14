# Native Motion example: issues and fixes

Porting Ranvier.View's browser Motion example (`examples/Ranvier.Motion` in the View repository) to a native app
(`examples/Ranvier.Motion.Native`) exposed four defects in this repository and two in the upstream packages. The
page checks found every one. The app's `Smoke.fs` drives each page through the WebCore host's real mouse, wheel and
frame loop.

The fixes are in commit `80fe261`, and the app is in `ca54893`. Each fix has a regression test that failed before it
and passes after.

| # | Area | Symptom in the app | Fixed in |
|---|---|---|---|
| 1 | Composition | Leaderboard rows stopped partway to their new rank | `Composition.fs`, `EngineFrames.fs` |
| 2 | Composition | Leaving the graphs page threw `ObjectDisposedException: Timer` | `Composition.fs` |
| 3 | Draggable | The first click on a kanban card's delete button was lost | `DraggableTransformTarget.fs` |
| 4 | Motion | Story chapters failed with "Motion.scroll links a Timer, Timeline or ComposedAnimation" | `Motion.fs` |
| 5 | Upstream View | A `show` inside `<svg>` threw `InvalidCastException` to `SVGPathElement` | worked around in the app |
| 6 | Upstream View/Core | The routes tab indicator never moved | worked around in the app |

## 1. Replace ordered tweens of separate timelines by their local positions

**Symptom.** On the graphs page, "Next snapshot" moves each leaderboard row with its own timeline. Every row that moved
up stopped partway, for example at `translateY(100.4px)` on its way from 156 px to 0. Rows that moved down finished.

**Diagnosis.** Each row plays two timelines on the same `y` property. The first one comes from `animateOn`'s first
value and is completed at once. The second plays when the snapshot changes. Replace composition keeps one list per
target and property, ordered by each tween's `AbsoluteStart`. A later tween takes over from an earlier one, and an
earlier tween is skipped once a later one has begun.

`AbsoluteStart` was the tween's position plus the animation's `offset`. That offset was 0 for a standalone animation
and only the position within the timeline for a timeline child. The timeline's own start time was not included. So
the new tween at position 30 sorted before the completed tween at position 60. When the new timeline's clock reached
60 ms, the old tween counted as having begun, and it overrode the new one. That matches the observed stalls exactly:
each row stopped when the old timeline's child would have started.

anime.js anchors these times to the engine. In `timer.js`, a top-level timer's `_offset` is
`(engine._lastTickTime - engine._startTime) * timeScale`, and a child's absolute time adds its parent's `_offset`
(`animation.js`, `absoluteOffsetTime`). If the engine isn't running, anime ticks it first (`engine.requestTick(now())`),
so a timer created after a pause doesn't read a stale tick.

**Fix.**
- `EngineFrameQueue.OffsetTick()` (`src/Ranvier.Anime/EngineFrames.fs:153`) returns the engine's last tick. When no
  frame is pending, it first requests the host's current time, as anime does.
- A standalone `ComposedAnimation` starts its `offset` at `OffsetTick()`, scaled to the engine's time unit. A timeline
  child starts at its timeline's `AbsoluteOffset` plus its position (`src/Ranvier.Anime/Composition.fs:632`).
- `ComposedTimeline.AbsoluteOffset` (`Composition.fs:1245`) records `OffsetTick()` when the timeline is constructed.

Tween absolute times and the render-time absolute time both derive from the same `offset`, so every Replace comparison
uses the same clock.

**Test.** "composition Replace orders the tweens of timelines started at different times on the engine clock"
(`tests/Ranvier.Anime.Tests/CompositionAndTransformTests.fs:111`). It completes a timeline whose tween sits at position
60, advances the manual clock 100 ms, then plays a new timeline whose tween on the same property sits at position 30.
The new tween must reach its target. With only the timeline-relative offset it stopped at 88. With the anchored offset
but no idle tick, it also stopped at 88, which is why `OffsetTick` requests the current time.

**Scope.** This changes every composed animation's absolute times, so the full managed suite (2,475 tests) and the
native and browser suites were rerun. All pass.

## 2. Revert after Complete threw on the cancelled clock

**Symptom.** Leaving the graphs page reported `ObjectDisposedException: Cannot access a disposed object. Object name:
'Timer'`. The stack ran from `Motion.animateOn`'s cleanup through `ComposedAnimation.Revert` to `Timer.Seek`.

**Diagnosis.** `animateOn` plays its first value to its end with `Complete()`. `Timer.Complete` completes the clock and
then cancels it, as anime's `complete()` does. Disposing the part reverts the latest animation, which was still that
first one. `ComposedAnimation.Revert` checked only the animation's own `cancelled` flag, then seeked and paused the
clock. A cancelled clock throws on both, so the values were never restored. anime's `revert()` still restores after
`complete()`.

**Fix.** `Revert` skips the seek when its clock is cancelled and still restores the values, and `RevertValues` skips
the pause (`Composition.fs:1108` and `:1042`).

**Test.** "animateOn reverts a completed first value on disposal"
(`tests/Ranvier.Anime.NativeSmoke/MotionPartsProbe.fs:227`). The existing animateOn probe always changed the value
before disposing, so it never reverted a completed first animation.

## 3. The native draggable rewrote transforms from its creation snapshot

**Symptom.** On the kanban board, the first click on a card's delete button did nothing, and the second worked. A
capture-phase trace showed `mousedown` on the button but `mouseup` and `click` on the card. Measuring the card around
the press showed what moved it. Its inline style changed from `transform: scale(1)` to
`transform: translate(0px) scale(0.85)` on grab, so the card shrank under the pointer.

**Diagnosis.** `DraggableTransformTarget` parsed the element's inline transform once, when the draggable was created,
and rebuilt every write from that private copy. The draggable was created when the card was placed, on the presence
enter animation's first frame, `scale(0.85)`. The enter animation later settled at `scale(1)`, but the grab rewrote
the stale scale. anime's draggable writes through the element's shared transform cache, which other animations keep
current. The `0.85` restyle shrank the card under the pointer, so the click target moved. anime's own click
suppression only applies after a move while grabbed, and there was none.

**Fix.** Each write re-reads the element's inline transform. It keeps the components other writers own and overrides
only the axes this draggable owns (`src/Ranvier.Anime/DraggableTransformTarget.fs:46`). `TransformCache.build` emits
components in a fixed order, so merging doesn't reorder them.

**Test.** "drag keeps transform components other animations wrote"
(`tests/Ranvier.Anime.NativeSmoke/MotionViewProbe.fs:213`). After the draggable exists, it writes `rotate(5deg)` to the
handle, then drags with real mouse input. The result must keep the rotation.

## 4. Motion.scroll could not link a ComposedTimeline

**Symptom.** The story page's chapters threw "Motion.scroll links a Timer, Timeline or ComposedAnimation." (parameter
`play`).

**Diagnosis.** Anime's `timeline()` maps to `Composed.createTimeline` on .NET. The native `Motion.scroll`'s `linkOf`
accepted the engine's `Timer`, `Timeline` and `ComposedAnimation` only.

**Fix.** `linkOf` adapts a `ComposedTimeline` (`src/Ranvier.View.Anime/Motion.fs:1079`):
- duration, current time, progress, completion and seeking map directly;
- Restart and Reset rewind before playing or pausing;
- Complete seeks to the end;
- Alternate reverses.

The `scroll` docs and the README list the new link.

**Test.** "scroll scrubs a linked composed timeline" (`tests/Ranvier.Anime.NativeSmoke/MotionViewProbe.fs:196`). A
second scrolled element links a paused `ComposedTimeline`. A real native scroll must scrub it between 0.2 and 0.8
opacity.

## 5. Upstream: a `show` inside `<svg>` creates HTML elements on WebCore

**Symptom.** On the graphs page, the worker comparison line, a `path` inside `show` inside the chart's `<svg>`, threw
`Unable to cast object of type 'Ranvier.View.WebCore.HTMLElementObject' to type 'Ranvier.Dom.SVGPathElement'` from
`Ranvier.View.Template.motion`.

**Diagnosis.** Printing namespaces showed the static sibling `.signal-line` as `http://www.w3.org/2000/svg path`, but the
conditional line as `http://www.w3.org/1999/xhtml PATH`. Ranvier.View 0.1.49-rc.1 creates a `show`'s children in the
XHTML namespace when the `show` is inside `<svg>`. This is upstream (`Ranvier.View`/`Ranvier.View.Dom`) and not fixed
here.

**Workaround.** The line is always placed, and `Motion.animateOn` fades its opacity to 0 or 1 with the comparison
toggle (`examples/Ranvier.Motion.Native/src/Graphs.fs:372`). The browser page keeps `show` with `Motion.fade`.

**Upstream fix needed.** A `show` (and presumably other conditional arms) inside SVG should create its children with
the parent's namespace.

**Fixed** in Ranvier.View 0.1.49-rc.2 ([Ranvier.View#99](https://github.com/ranvier-org/Ranvier.View/pull/99)). The page
uses `show` with `Motion.fade` again, and the smoke checks that the returning line is an SVG `path`.

## 6. Upstream: measuring layout inside a reactive read silently ends the effect

**Symptom.** The routes tab indicator never moved, and its inline style stayed empty. No mount or commit error was
reported.

**Diagnosis.** The indicator's `follow` read multiplied the route's index by the indicator's measured width
(`getBoundingClientRect` through `document.querySelector`). Instrumenting `Motion.follow` showed the read running on
each route change, but its effect callback never ran. A direct `Composed.animate` on the same element worked. With a
constant width in the read, the callback ran and the indicator moved. Forcing layout inside the effect's tracked read
ends the effect without a reported error. I suspect a layout flush re-entering the graph, but I didn't find the cause
upstream.

**Workaround.** The indicator measures its width once in a `mounted` part into a `TabWidth` signal, which the `follow`
read uses (`examples/Ranvier.Motion.Native/src/Routes.fs:304`).

**Upstream follow-up.** Either layout reads inside tracking should be safe, or the failure should be reported rather
than dropping the effect.

**Fixed.** The cause wasn't layout: any WebCore call that returns a node asked the core for the current owner, which
fails inside a pure read. Ranvier 0.1.1-preview.2 adds `Api.tryGetOwner`
([Ranvier#300](https://github.com/ranvier-org/Ranvier/pull/300)), and Ranvier.View.WebCore 0.1.0-rc.2 uses it
([Ranvier.View#100](https://github.com/ranvier-org/Ranvier.View/pull/100)). The indicator measures itself inside the
`follow` read again, and `TabWidth` is gone.

## App-only findings

These were mistakes in the port or in the checks, not engine defects. They're recorded here so the same traps can be
avoided:

- **Clicks after `scrollIntoView`.** The checks' click helper renders a frame and hovers before pressing, as a real
  pointer does.
- **Scroll position between pages.** The native window keeps its scroll position across page switches, where a browser
  page load starts at the top. `App.Open` scrolls to the top while it swaps the page's stylesheets.
- **Mouse events, not pointer events.** The native host sends mouse events, so the graphs page listens for those and
  follows the document's `mousemove`/`mouseup` while panning, instead of capturing the pointer.
- **Wheel direction at full span.** A positive wheel delta at the full 960-unit span zooms out and is clamped, so the
  check zooms in with a negative delta.
- **Motion policy.** The native graph outlives a page, so the physics page restores the `Full` motion policy when it
  leaves. In the browser, every page load starts from the media query.

## Verification

| Suite | Result |
|---|---|
| Managed (`build test`) | 2,475 passed, 0 failed, 2 ignored |
| Native smoke, traced Debug (`native-run -c Debug`) | 375 checks passed |
| Browser jsdom (`build browser`) | 68 of 68, bundle budgets within limits |
| Native Motion example (`build motion-example`) | 70 of 70, in Release (three runs) and traced Debug |
