---
title: Animation
order: 1
description: "Typed anime.js 4.5.0 bindings for Fable: parameters, values, eases, staggers and timelines."
---

:::info Alpha
The API may change before 1.0.
See [installation and the dependency feed](../guide/introduction.md#installation).
:::

`Fable.Ranvier.Anime` is typed F# over [anime.js](https://animejs.com) 4.5.0, with ownership and
signals from the Ranvier core. The namespace is `Fable.Ranvier.Anime`. Install the JavaScript engine:

```shell
npm install animejs@4.5.0
```

[Owning and driving animations](reactive.md) covers `Reactive`, and
[Testing and costs](testing.md) the manual clock and the bundle size. The template parts of
`Ranvier.View.Anime` are documented with
[Ranvier.View](https://ranvier-org.github.io/Ranvier.View/view/motion-parts/).

## Animating an element

```fsharp
open Fable.Core
open Fable.Ranvier.Anime

let reveal (card: TS.Dom.Element) =
    Anime.animate (card, Anim (x = 100, opacity = Value.fromTo (0, 1), duration = 400, ease = Ease.outQuart))
```

The card moves to `x = 100` and fades from opacity 0 to 1 over 400 ms with `outQuart` easing.
The emitted JavaScript is:

```js
animate(card, { x: 100, opacity: [0, 1], duration: 400, ease: "outQuart" });
```

- **Parameters are constructors with named optional arguments**: `Anim`, `TimelineParams`,
  `AnimatableParams`, `LayoutParams`, `DraggableParams`, `ScrollParams` and the others. The emitted
  object literal holds exactly the arguments given, in the constructor's declaration order. anime
  composes transforms in a fixed order: perspective, translations, rotations, scales, skews.
- **`.With (name, value)` adds a property the constructor lacks**, such as a CSS variable:
  `Anim(duration = 300).With ("--hue", 120)`.
- **Every function is imported from `animejs` on its own**, so a bundle holds exactly the anime features it
  calls.
- **Members carry anime's JavaScript names**: `play`, `pause`, `seek`, `progress`, `revert`, and
  `setX` on a draggable.
- **`Targets` converts from `TS.Dom.Element` and `TS.Dom.Element array`.** `Targets.selector ".card"`
  selects by CSS selector, and `Targets.object state` tweens the numeric properties of a plain JS
  object.

The binding files write the DOM types as `TS.Dom.Element` and open `Fable.Core`, not
`Fable.Core.TS.Dom`: that namespace's `Animatable` and `Text` shadow the bindings' types of the same
names.

## Values

A property or timing argument is a `Value`. Numbers and strings convert at a method argument, so
`x = 100`, `y = "50%"` and `scale = 0.9` type-check without a wrapper.

| Value | Animates |
| --- | --- |
| `100`, `0.5`, `"50%"`, `"1rem"`, `"#f0f"` | to the value |
| `"+=20"`, `"-=20"`, `"*=2"` | relative to the current value |
| `Value.fromTo (0, 1)` | from the first value to the second |
| `Value.from 0`, ``Value.``to`` 1`` | from a value to the current one, or from the current one to a value |
| `Value.keys [| ... |]` | through keyframes, each taking an equal share of the duration |
| `Value.tween (``to`` = 1, duration = 200, ease = Ease.outQuad)` | with its own timing, overriding the animation's for this property |
| `Value.per (fun target index targets -> ...)` | with a value computed per target |
| `Value.stagger 40` | with a value growing by 40 from target to target |

```fsharp
let settle (card: TS.Dom.Element) (cards: TS.Dom.Element array) =
    Anime.animate (card, Anim (x = "+=20", rotate = Value.from 45, opacity = Value.keys [| Value.op_Implicit 0; Value.op_Implicit 1; Value.op_Implicit 0.5 |]))
    |> ignore

    Anime.animate (cards, Anim (y = Value.tween (``to`` = 0, duration = 200, ease = Ease.outQuad), x = Value.per (fun _ index _ -> Value.op_Implicit (index * 10))))
```

F# converts implicitly at method arguments. An element of `Value.keys` and the result of a
`Value.per` function convert with `Value.op_Implicit`, as above; an array element left to convert
implicitly raises warning FS3391, and a function's result fails to type-check. `modifier` parameters
take a `float -> float`.

## Eases

```fsharp
let eases () =
    [|
        Ease.outQuart
        Ease.inOutSine
        Ease.cubicBezier (0.2, 0., 0., 1.)
        Ease.steps 4
        Ease.linearStops (0., 0.4, 1.)
        Ease.fn (fun t -> t * t)
        Ease.named "cubic-bezier(.2,0,0,1)"
    |]

let springy (card: TS.Dom.Element) =
    Anime.animate (card, Anim (y = 0, ease = Ease.spring (stiffness = 200, damping = 12)))

let overshoot (card: TS.Dom.Element) =
    Anime.animate (card, Anim (scale = Value.fromTo (0.7, 1), duration = 420, ease = Ease.outBackBy 2.))
```

Every named curve of anime is a member (from `Ease.linear` through `Ease.outInElastic`).
Parameterised curves are functions: `Ease.outPower 2.`, `Ease.outBackBy 2.`,
`Ease.outElasticBy (1., 0.3)`. `Ease.spring` as an animation's `ease` also sets the animation's
duration to the spring's settling time; given `duration` or `bounce`, the spring derives its
stiffness and damping from them.

## Staggers

```fsharp
let cascade (cards: TS.Dom.Element array) =
    Anime.animate (cards, Anim (opacity = Value.fromTo (0, 1), delay = Value.stagger (40, start = 100, from = StaggerFrom.center)))

let ripple (cells: TS.Dom.Element array) =
    Anime.animate (
        cells,
        Anim (scale = Value.fromTo (0.5, 1), delay = Value.stagger (20, grid = Grid.size (8, 4), from = StaggerFrom.at (0.5, 0.5)))
    )
```

`Value.stagger each` takes its options as named arguments: `start`, `from`, `reversed`, `grid`,
`axis`, `use`, `total`, `ease`, `modifier`, `jitter` and `seed`. `from` is `StaggerFrom.first`, `center`,
`last`, `random`, `index n`, or, on a grid, `StaggerFrom.at (x, y)`, with `x` and `y` fractions of
the grid's width and height in 0..1. `Grid.size (columns, rows)` gives the grid; `Grid.auto` reads it
from the targets' layout. `each` given as `Value.fromTo (0, 400)` distributes the range across the
targets.

## Timelines

```fsharp
let intro (title: TS.Dom.Element) (cards: TS.Dom.Element array) (finished: unit -> unit) =
    Anime
        .timeline(TimelineParams (defaults = Anim (duration = 300, ease = Ease.outQuad)))
        .add(title, Anim (opacity = Value.fromTo (0, 1)))
        .label("cards")
        .add(cards, Anim (y = Value.fromTo (12, 0)), At.stagger (Value.stagger 40))
        .add(title, Anim (scale = Value.fromTo (0.96, 1)), At.withPrevious)
        .call ((fun _ -> finished ()), At.after 100)
```

`add`, `set`, `call`, `label`, `sync` and `remove` return the timeline. A child without a position
starts at the end of the timeline. `TimelineParams`' `defaults` supplies each parameter a child leaves
unset.

| Position | anime | Starts the child |
| --- | --- | --- |
| `At.ms 200` | `200` | 200 ms from the timeline's start |
| `At.after 50` | `"+=50"` | 50 ms after the end of the last child |
| `At.before 50` | `"-=50"` | 50 ms before the end of the last child |
| `At.times 2.` | `"*=2"` | at the timeline's current duration times 2 |
| `At.afterPrevious` | `"<"` | at the end of the previous child |
| `At.withPrevious` | `"<<"` | with the previous child, at its start |
| `At.afterPreviousStart 100` | `"<<+=100"` | 100 ms after the start of the previous child |
| `At.label "cards"` | `"cards"` | at the label |
| `At.stagger (Value.stagger 40)` | a stagger | at a per-target offset, for a child animating several targets |

`.call` places a callback on the timeline's clock. Steps sequenced on one timeline, callbacks
included, keep their order under every playback rate, pause with the timeline, and are reverted with
it.

## Other constructors

```fsharp
let follower (puck: TS.Dom.Element) =
    let animatable = Anime.animatable (puck, AnimatableParams (x = 400, y = 400, ease = Ease.outQuart))

    fun (x: float, y: float) ->
        animatable.set ("x", x)
        animatable.set ("y", y)

let travel (dot: TS.Dom.Element) (edge: TS.Dom.Element) =
    let path = Svg.motionPath edge
    Anime.animate (dot, Anim (translateX = path.translateX, translateY = path.translateY, duration = 460, ease = Ease.inOutSine))
```

| Member | Creates |
| --- | --- |
| `Anime.animatable (targets, parameters)` | properties that animate to each value set on them, from wherever they are |
| `Anime.timer parameters` | callbacks on anime's clock |
| `Anime.draggable (target, parameters)` | a draggable element |
| `Anime.onScroll parameters` | a scroll observer; given as a tickable's `autoplay`, it drives the tickable |
| `Anime.layout (root, parameters)` | a layout recorder: `record ()`, then `animate ()` from the recorded layout to the current one |
| `Anime.splitText (target, parameters)`, `Anime.scramble parameters` | split text, and a value scrambling text towards its final text |
| `Anime.scope parameters` | a scope with a root, defaults and media queries for the objects created in it |
| `Anime.completion tickable` | a promise resolved when the tickable completes |
| `Svg.morphTo`, `Svg.drawable`, `Svg.motionPath` | a shape-morphing value, line-drawable elements, and the translations and rotation along a path |
| `Waapi.animate (targets, parameters)` | an animation run by the Web Animations API |
| `Utils.set`, `get`, `getNumber`, `remove`, `random`, `clamp`, `mapRange`, `snap`, `lerp`, `round` and others | anime's utilities |
| `Engine.settings`, `Engine.update ()` | the engine's main loop, speed, frame rate and time unit, and a manual tick |

`AnimatableParams (x = 400)` is the duration of the `x` property alone; `AnimatableProp (unit = "px",
duration = 400, ease = Ease.outQuad)` gives a property its own unit and ease. anime appends a unit only
from a property's own entry.

## On .NET

The bindings compile on .NET, so portable code can reference them. The parameter constructors
and the inline conversions are inert placeholders on .NET; `[<Emit>]` and `[<Import>]` members
(`Anime.animate`, `Value.stagger`, `Value.from`, `Ease.spring`, `Ease.outBackBy`, `At.after` and the
other functions above) raise when evaluated there. Portable code creates anime objects inside the
functions that run in the browser: play functions, hooks and event handlers.
