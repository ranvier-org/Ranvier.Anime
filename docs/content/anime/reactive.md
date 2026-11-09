---
title: Owning and driving animations
order: 2
description: "Ranvier.Anime.Reactive: animations reverted by owners, playback as memos, signals that seek and drive, and the reduced-motion policy."
---

`Ranvier.Anime.Reactive` connects anime objects to owners and signals. It works with the core
without the view layer. Use the opens from [Animation](bindings.md#animating-an-element) plus
`open global.Ranvier`.

```fsharp
open global.Ranvier
open Fable.Core
open Ranvier.Anime

/// Fades the panel in, marks it shown once the fade completes, and scales the bar with the level.
let animatePanel (panel: TS.Dom.Element) (bar: TS.Dom.Element) (level: Signal<float>) (shown: Signal<bool>) =
    let fade =
        Reactive.own (Anime.animate (panel, Anim (opacity = Value.fromTo (0, 1), duration = 600)))

    let playback = Reactive.track fade
    Api.createEffect (fun () -> if playback.Completed.Value then shown.Value <- true)

    let fill =
        Reactive.own (Anime.animatable (bar, AnimatableParams (scaleX = 400, ease = Ease.outQuart)))

    Reactive.drive (fun () -> level.Value) fill "scaleX"
```

The panel fades from transparent to opaque over 600 ms; completion sets `shown` to `true`.
Changing `level` sends the bar towards its new scale. Disposing the owner restores both elements
and stops the reactive work.

## `own`

`Reactive.own revertible` registers `revertible.revert ()` as a cleanup of the current owner and
returns `revertible`. It takes any `IRevertible`: an animation, a timeline, an animatable, a
draggable, a scroll observer, a layout, a text splitter or a scope. Outside every root, the owner is
the graph's own scope, which reverts the object when the graph is disposed.

`revert` stops the object and restores the inline styles and attributes it changed.

## `track`

`Reactive.track tickable` returns the tickable's playback as memos:

| Field | Holds |
| --- | --- |
| `Progress: Memo<float>` | the overall progress in 0..1 |
| `Running: Memo<bool>` | whether the tickable is playing and not completed |
| `Completed: Memo<bool>` | whether the tickable has completed |

The memos are written from the tickable's `onBegin`, `onUpdate`, `onLoop`, `onPause` and
`onComplete` callbacks, in one batch per callback. A callback the tickable already had still runs,
first. When the owner is disposed, each callback slot that still holds `track`'s chain gets back its
earlier callback; a slot assigned after `track` keeps its new callback.

## `scrub`

```fsharp
let scrubbed (stage: TS.Dom.Element) (position: Signal<float>) =
    let tour =
        Reactive.own (
            Anime
                .timeline(TimelineParams (autoplay = false))
                .add(stage, Anim (x = Value.fromTo (0, 240), duration = 1000))
                .add(stage, Anim (rotate = 90, duration = 500))
        )

    Reactive.scrub (fun () -> position.Value) tour
```

`Reactive.scrub read tickable` is an effect that sets the tickable's `progress` to `read ()`, in
0..1, once with the current value and then whenever it changes. A tickable created with `autoplay = false` seeks to the first value and then follows each change of the
value, so a range input bound to the signal seeks the timeline.

## `drive`

`Reactive.drive read animatable property` is an effect that animates `property` of the animatable to
`read ()`, once with the current value and then whenever it changes, with the property's duration and ease. The animation starts from
the property's current value, so a change while a previous one plays continues from where it is.

## Typed CSS variables

`Reactive.driveCss read element variable parameters` animates a typed custom property to the value
of `read`, owned by the current Ranvier owner. The reader returns a numeric CSS token such as
`Css.px target.Value`; `AnimatableProp(duration = 300., ease = Ease.outQuart)` sets the timing. The
first value is set immediately, and subsequent changes animate from the current value. Disposing the
owner restores the original inline value and priority.

`Anim.With(variable, start, finish)` and `Anim.With(variable, frames)` give a typed custom property
explicit endpoints in any animation or timeline. Each endpoint keeps the custom property's grammar
marker; numeric tokens in an animation must share a unit suffix. In templates, `Motion.followCss`
from `Ranvier.View.Anime` is the view attribute for the same animation.

Let [typed CSS expressions](https://ranvier-org.github.io/Ranvier.Css/view/css/#numeric-expressions) consume the variable. The browser updates
every dependent declaration, including pseudo-elements, as Anime writes the variable. Frames
bypass signals. Compound expressions are CSS inputs rather than numeric animation endpoints.
See the [complete component example](https://github.com/ranvier-org/Ranvier.View/blob/main/docs/samples/css-anime/Example.fs).

## `policy`

```fsharp
let honoured (card: TS.Dom.Element) =
    let duration = if (Reactive.policy ()).Value = Reactive.Reduced then 0 else 400
    Anime.animate (card, Anim (opacity = Value.fromTo (0, 1), duration = duration))
```

`Reactive.policy ()` is the motion policy of the current graph, a `Signal<MotionPolicy>`:

- `Full` or `Reduced`, seeded from `(prefers-reduced-motion: reduce)` on the first call and written
  on each change of the media query.
- Every call on a graph returns the same signal. A write overrides the media query until its next
  change, which makes the policy a user setting as well as a system one.
- Disposing the graph detaches the media query listener.
- Without `matchMedia`, as on .NET and in jsdom, the seed is `Full`.
- The first call on a graph runs outside pure derivations, as `onCleanup` does.

The Motion parts read the policy: under `Reduced`, enters and FLIP do not play and exits finish at
once ([Reduced motion](https://ranvier-org.github.io/Ranvier.View/view/motion-parts/#reduced-motion)). Other animations read it themselves, as
`honoured` does.
