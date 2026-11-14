# Ranvier.View.Anime

Motion parts for `Ranvier.View` templates, over two engines: [anime.js](https://animejs.com) 4.5.0 through
`Fable.Ranvier.Anime` in the browser, and the native F# port `Ranvier.Anime` on .NET hosts such as WebCore. Both
packages expose the same portable API in the namespace `Ranvier.Anime`, and the parts are one source written against
it: a template, its parameter records and its play functions compile unchanged for either engine. See
[one API, two engines](#one-api-two-engines) and [native hosts](#native-hosts).

Add a `Motion.*` part to a DSL tag beside its attributes and events. It creates anime objects when
the element is placed and normally reverts them on disposal; [exits](#enter-and-exit-rules) preserve
the current visual state until the leaving animation finishes.

Enter and exit follow list and arm updates; FLIP animates container reflow; reactive parts follow
signals. The server omits all motion parts, so the template renders the same HTML with or without them.
See the [motion guide](https://ranvier-org.github.io/Ranvier/view/motion/) for application use, the
[design spec](https://github.com/ranvier-org/Ranvier/blob/master/docs/superpowers/specs/2026-10-21-motion-design.md)
for rationale, and the [seam note](https://github.com/ranvier-org/Ranvier/blob/master/docs/research/2026-10-21-motion-seam.md)
for the runtime hooks.

Install the JavaScript package beside it:

```shell
npm install animejs@4.5.0
```

## Opening the parts

```fsharp
open global.Ranvier
open Ranvier.Anime
open Ranvier.View
open Ranvier.View.Dsl
open Ranvier.View.Anime
```

Every sample on this page assumes these lines; the [view DSL reference](https://github.com/ranvier-org/Ranvier.View/blob/main/src/Ranvier.View/README.md) covers `Ranvier.View` and `Ranvier.View.Dsl`, and `Ranvier.Anime` holds the portable API of both engines. `Motion` requires qualified access: its `exit` would otherwise shadow FSharp.Core's.

**A play function receives the element as `Element`,** `Ranvier.Anime.Dom.Element`: the engine's DOM element, `Fable.Core.TS.Dom.Element` in the browser and `Ranvier.Dom.Element` on .NET. `Anime.animate el specs options` takes it as its target on both.

**Animations are created inside play functions.** A template is built where its definition is bound; `Anime.animate`, `Anime.timeline` and `Ease.named` reach the engine, which on .NET is the host installed on the thread, so they belong inside a play function. The option records (`ComposedAnimationOptions`, `LayoutOptions` and the others) are plain data and may appear at definition time.

## Lifecycle parts

| Part | Plays |
|---|---|
| `Motion.on play` | on every placement, hydration included |
| `Motion.appear play` | on every placement of an element created on the client; an element adopted from server HTML stays as served |
| `Motion.enter play` | when the element's row or arm is created by a region pass after the region's first |
| `Motion.exit play` | when the element's row or arm is retired; the element leaves when the tickable completes |
| `Motion.presence enter exit` | `enter` and `exit` on one element, with the leaving element pinned out of flow |
| `Motion.fade`, `Motion.rise`, `Motion.scale` | presence presets: opacity, opacity with a 12 px rise, opacity with a 92 % scale; 240 ms in, 180 ms out |
| `Motion.collapse` | presence preset: the height unfolds from 0 and folds back to 0, in flow, with the overflow hidden while it plays |
| `Motion.flip parameters` | FLIP for the rows of every region under the element |

```fsharp
type Card = { Id: int; Title: string }

type Board =
    {
        Cards: Signal<Card array>
        Selected: Signal<bool>
    }

let board =
    Component.template
        (fun _ (cards: Card array) ->
            {
                Cards = Api.createSignal cards
                Selected = Api.createSignal false
            })
        (section () {
            ul (ul.className "cards", Motion.flip { LayoutOptions.Default.WithDuration 350u with Ease = fun t -> 1. - (1. - t) ** 4. }) {
                forEach
                    (fun card -> card.Id)
                    (fun b -> b.Cards.Value)
                    (li (
                        Motion.presence
                            (fun _ el -> Anime.animate el [| PropertySpec.Scalar ("opacity", 1., fromValue = 0.); PropertySpec.Scalar ("y", 0., fromValue = 12.) |] { ComposedAnimationOptions.Default with Duration = 240. })
                            (fun _ el -> Anime.animate el [| PropertySpec.Scalar ("opacity", 0.); PropertySpec.Scalar ("scale", 0.9) |] { ComposedAnimationOptions.Default with Duration = 180. })
                    ) {
                        fun current -> (current ()).Title
                    })
            }

            show (fun b -> b.Selected.Value) (p (Motion.fade) { "Selected" })
        })
```

`on` and `appear` take any `IRevertible`; `enter`, `exit` and `presence` take an `ITickable`: the `ComposedAnimation` of `Anime.animate`, a `ComposedTimeline`, or one of the engine's own tickables:

```fsharp
type Banner = { Text: string }

let banner =
    Component.template
        (fun _ (text: string) -> { Text = text })
        (header () {
            h1 (Motion.appear (fun _ el -> Anime.animate el [| PropertySpec.Scalar ("opacity", 1., fromValue = 0.) |] { ComposedAnimationOptions.Default with Duration = 400. })) { fun b -> b.Text }

            span (Motion.on (fun _ el -> Anime.animate el [| PropertySpec.Scalar ("rotate", 360.) |] { ComposedAnimationOptions.Default with Duration = 1200.; Ease = Ease.named "linear" })) { "*" }
        })
```

`enter` and `exit` are separate parts, and an element takes both, in flow:

```fsharp
type Toast = { Messages: Signal<string array> }

let toasts =
    Component.template
        (fun _ () -> { Messages = Api.createSignal [||] })
        (ol () {
            forEach
                id
                (fun (t: Toast) -> t.Messages.Value)
                (li (
                    Motion.enter (fun _ el -> Anime.animate el [| PropertySpec.Scalar ("x", 0., fromValue = 40.) |] { ComposedAnimationOptions.Default with Duration = 200. }),
                    Motion.exit (fun _ el -> Anime.animate el [| PropertySpec.Scalar ("x", 40.); PropertySpec.Scalar ("opacity", 0.) |] { ComposedAnimationOptions.Default with Duration = 200. })
                ) {
                    fun message -> message ()
                })
        })
```

## One API, two engines

The project is one source: `Motion.fs` compiles under Fable against `Fable.Ranvier.Anime` and on .NET against
`Ranvier.Anime`, and calls only the portable API the two share. A template that does the same compiles unchanged on
both, play functions included:

- **Animations.** `Anime.animate target specs options`, `Anime.animateTargets` and `Anime.timeline ()`, with
  `PropertySpec.Scalar` and `PropertySpec.Text` for the properties, `ComposedAnimationOptions` for the timing and
  `Ease.named`, `Ease.cubicBezier` and `Ease.steps` for the curves. They return a `ComposedAnimation` or a
  `ComposedTimeline`, both `ITickable`.
- **Parameter records.** `LayoutOptions` (`flip`), `ComposedAnimationOptions` (`follow`, `followCss`, `draw`, `morph`),
  `ScrollParams` with `ScrollOptions` (`scroll`), `NativeDraggableOptions` (`drag`) and `TextSplitterOptions`
  (`split`), whose play function receives the portable `TextSplitter` (`Lines`, `Words`, `Chars`).
- **Engine-only fields stay on the engine's record.** The browser records add `Extra`, further anime.js parameters
  merged last, for options only anime.js has (`enterFrom`, a selector `container` such as
  `{ NativeDraggableOptions.Default with Extra = DraggableParams (container = "article") }`, a per-property entry), and
  `ComposedAnimationOptions.Unit`, the shared unit of a follower. The native records add `Computed`, `Playback` and
  the rest. Portable code names neither.

Every sample on this page is portable unless it says otherwise.

**The bindings remain available in the browser.** `open Fable.Ranvier.Anime` brings the anime.js bindings, whose
objects are the portable contracts: a `JSAnimation` is a `ComposedAnimation`, a binding `Timeline` a
`ComposedTimeline`. A play function may return one where it needs what only anime.js has, such as a loop, a stagger or
a spring. With both namespaces open, `Anime.animate` is the portable function, so the binding's is written out:

```fsharp
type Spinner = { Glyph: string }

let spinner =
    Component.template
        (fun _ (glyph: string) -> { Glyph = glyph })
        (span (
            Motion.on (fun _ el ->
                Fable.Ranvier.Anime.Anime.animate (el, Anim (rotate = 360, duration = 1200, loop = Loop.forever, ease = Ease.linear)))
        ) {
            fun s -> s.Glyph
        })
```

## Native hosts

On .NET the portable functions, and so the parts, read the host's frame scheduler and computed styles from
`AnimeHost`, part of `Ranvier.Anime`, installed on the host thread before mounting; `drag` and `scroll` also need the
host's input, resize and viewport services, passed to the `AnimeHost.Install` overload taking
`AnimeInteractionServices`. The browser needs no host.

```fsharp
let installMotion (host: WebCoreHost) =
    AnimeHost.Install { Scheduler = Frames.forHost host; Computed = ComputedStyles.forHost host }
```

`Reactive.policy ()` is a per-graph signal seeded `Full` on .NET, where no media query exists. `scroll` links a
`ComposedAnimation`, `ComposedTimeline`, `Timer` or `Timeline`; an interrupted native FLIP pass restarts from the rows' laid-out boxes.

Coverage: the parts run under Fable and jsdom (`tests/Ranvier.View.Anime.Browser`, `npm test`) and, on .NET, on a
WebCore page in the native smoke host (`MotionViewProbe.fs`, `MotionPartsProbe.fs`); both compile this page's portable
samples (`Fixtures.fs`, `MotionFixtures.fs`).

## Enter and exit rules

- **Enter plays for rows and arms created after the region's first pass.** Rows of the first pass, hydrated rows and rows nested inside an entering row stay still; an arm shown by `show` turning true enters.
- **An exit applies to a row or arm root element.** The retired row's element stays in place, with its content, until the exit's tickable completes, and then leaves. Reorders and inserts while it plays place the live rows around it. A Debug build raises "An exit hook requires a row or arm root element" for an exit elsewhere.
- **The exit starts from the current values.** The instance is disposed before its row's nodes leave. On an element whose exit follows, and on every element inside it, `on`, `appear`, `enter` and the reactive parts pause their animations instead of reverting them; anime's default composition, `replace`, then hands each animated property to the exit. `drag` disables its draggable and `split` and `scroll` stop their observers, so nothing of the row stays live once it leaves.
- **Disposing the mount ends every running exit** at once: the exit's tickable is cancelled and the element leaves. An exit that has not completed after 10 s is cancelled and its element leaves.
- **`presence` pins the leaving element** with `position: absolute` at its current box, its offset box, with no margin, so the rows after it close the gap while it plays. The box is relative to the element's offset parent: give the list's container `position: relative`. `collapse` and a separate `exit` stay in flow.

## FLIP

`Motion.flip parameters` on a list's container wraps anime's `createLayout` (Ranvier.Anime's `AutoLayout` on .NET), with the timing of a `LayoutOptions`. Before the first region pass under the container in a flush it records the layout of the container's element children, and once the pass's nodes and commits are in place it animates them from the recorded layout to the new one. A pass with no recorded layout before it, such as the first, does not animate.

- **The rows are the container's direct element children,** measured at both moments; they replace anime's `children` (the native layout's `Targets`). A row whose Motion exit has started, or whose Motion enter started in the pass, is left out of that pass's measurements.
- **Motion parts keep the visual properties.** In a pass where an element under the container carries a Motion part, the layout animates position and size only, and the opacity, colours, borders, filters, clip paths and font size stay with the parts (an opacity `enter` or `presence` under `flip` ends at its last frame). In a pass with no such element, anime's default fades apply. A property in `parameters.Properties` beyond the default set, or named in the browser `Extra`'s `enterFrom`, `leaveTo` or `swapAt`, is always the layout's.
- **The container is positioned.** anime animates moved rows with `position: absolute` and a translation through the Web Animations API during the animation.
- **The layout is created on the first recording,** and reverted with the instance.
- **A dropped row starts from where it was dropped.** `Motion.flipFrom element` starts the element's next FLIP from its current visual box, drag transform included, in place of the transform-free box the recording measures. Call it before the change that moves the row and before the drag position is reset. The box stays on the element until the next pass that measures the element as a row, which starts its FLIP there, with the recorded size, and drops the box; under `Reduced`, where flip stays still, the call returns at once. The element comes from the handler's event or from a `mounted` part.

## Reactive parts

| Part | Drives |
|---|---|
| `Motion.follow property parameters read` | an `Animatable` property of the element towards `read`'s value whenever it changes; the first value is set at once |
| `Motion.animateOn read play` | a new tickable from `play` with `read`'s value whenever it changes, replacing the running one's tweens of the same properties; the first plays to its end at once |
| `Motion.draw parameters read` | the stroke of an SVG geometry element, drawn from its start to the fraction `read` returns |
| `Motion.morph parameters read` | the `d` (path) or `points` (polygon, polyline) of an SVG shape towards the shape `read` returns; each completed morph leaves exactly that shape |
| `Motion.scroll parameters bind play` | `play`'s tickable linked to an `onScroll` observer of the element (or of `parameters`' target); the observer's progress and in-view state written into the signals of a `ScrollBinding` |
| `Motion.drag parameters bind` | a draggable element, two-way through a `DragBinding`: dragging, and the end of a release's motion, write `Position`, and a write to `Position` from elsewhere moves the element with its update callback muted, so the write is not echoed; a grab cancels the running animation of the binding's `Slot` |
| `Motion.split parameters play` | anime's `splitText` over the element's static text, and `play` over its lines, words or characters |

```fsharp
type Gauge =
    {
        Load: Signal<float>
        Label: Signal<string>
    }

let gauge =
    Component.template
        (fun _ (load: float) ->
            {
                Load = Api.createSignal load
                Label = Api.createSignal "idle"
            })
        (div () {
            div (div.className "bar", Motion.follow "scaleX" { ComposedAnimationOptions.Default with Duration = 400.; Ease = fun t -> 1. - (1. - t) ** 4. } (fun g -> g.Load.Value)) {
                ()
            }

            span (Motion.animateOn (fun g -> g.Label.Value) (fun _ el _ -> Anime.animate el [| PropertySpec.Scalar ("opacity", 1., fromValue = 0.) |] { ComposedAnimationOptions.Default with Duration = 200. })) {
                fun g -> g.Label.Value
            }
        })
```

`follow`, `followCss`, `draw` and `morph` take the timing of a `ComposedAnimationOptions`. In the browser, `follow`'s shared `Unit` applies to `property` unless an entry for it in `Extra` sets its own `unit`: `Motion.follow "x" { ComposedAnimationOptions.Default with Unit = Some "%"; Duration = 420. }` moves the element by percentages of its width. On .NET a value keeps the property's default unit.

```fsharp
type Story =
    {
        Progress: Signal<float>
        Seen: Signal<bool>
        Puck: Signal<struct (float * float)>
    }

let story =
    Component.template
        (fun _ () ->
            {
                Progress = Api.createSignal 0.
                Seen = Api.createSignal false
                Puck = Api.createSignal (struct (0., 0.))
            })
        (article () {
            h2 (
                Motion.split { TextSplitterOptions.Default with Chars = SplitMode.Enabled } (fun _ split ->
                    Anime.animateTargets (Array.map box split.Chars) [| PropertySpec.Scalar ("opacity", 1., fromValue = 0.) |] { ComposedAnimationOptions.Default with Duration = 400. })
            ) {
                "Ranvier"
            }

            section (
                Motion.scroll
                    { ScrollParams.Default with Options = { ScrollOptions.Default with Enter = { Container = "bottom"; Target = "top" }; Leave = { Container = "top"; Target = "bottom" }; Sync = ScrollSync.Smooth 1. } }
                    (fun s ->
                        {
                            Progress = ValueSome s.Progress
                            InView = ValueSome s.Seen
                        })
                    (fun _ el -> Anime.animate el [| PropertySpec.Scalar ("opacity", 1., fromValue = 0.2) |] { ComposedAnimationOptions.Default with Autoplay = false })
            ) {
                fun s -> string (round (s.Progress.Value * 100.)) + " %"
            }

            div (
                div.className "puck",
                Motion.drag NativeDraggableOptions.Default (fun s ->
                    {
                        Position = s.Puck
                        Dragging = ValueNone
                        Slot = ValueNone
                    })
            ) {
                "drag"
            }
        })
```

`split`'s play function animates the splitter's `Chars`, `Words` or `Lines`; in the browser, where a splitter is anime's own, the bindings add a stagger: `Fable.Ranvier.Anime.Anime.animate (Targets.object split.Chars, Anim (y = Value.fromTo ("100%", "0%"), delay = Value.stagger 20))`.

**`split` takes static text.** A text slot or region inside the split element stops updating once its text is split. `splitText`, `onScroll` and `createDraggable` require `ResizeObserver`, and the draggable `DOMPoint` and `DOMMatrix`.

**`draw` and `morph` need SVG elements.** The SVG tags of the DSL (`svg`, `g`, `path`, `circle` and the others listed in the [DSL reference](https://github.com/ranvier-org/Ranvier.View/blob/main/src/Ranvier.View/README.md#tags)) create SVG elements under both commit modes:

```fsharp
type Chart =
    {
        Fraction: Signal<float>
        Line: Signal<string>
    }

let chart =
    Component.template
        (fun _ () ->
            {
                Fraction = Api.createSignal 0.
                Line = Api.createSignal "M0 0 L100 0"
            })
        (svg () {
            path (Motion.draw { ComposedAnimationOptions.Default with Duration = 300. } (fun c -> c.Fraction.Value)) { () }
            path (Motion.morph { ComposedAnimationOptions.Default with Duration = 300. } (fun c -> c.Line.Value)) { () }
        })
```

A list row or arm rooted at an SVG tag is an SVG element too, under both commit modes, and SVG names such as `viewBox` are written as given.

## Animations from handlers

An animation started by a handler or an effect, such as a spring home on a click or after a release, belongs to a `MotionSlot`. The sample below uses the browser bindings for the spring and its update callback. `Motion.slot ()`, called in the instance's setup, returns one owned by the instance:

- **`slot.Play (fun () -> animation)`** cancels the running animation, then plays the one the function creates; a cancelled animation stops at its current values, its callbacks muted. The slot holds the animation the function returns, and cancels one the function plays through the same slot.
- **`slot.Stop ()`** cancels the running animation; `slot.Running` is the latest animation played (null after a stop), and `slot.Playing` is true while it is live, played and not yet completed or cancelled (a WAAPI animation reports completion only).
- **The instance's disposal cancels the running animation** and closes the slot: a later `Play` only cancels.
- **A grab cancels it.** Given as a `DragBinding`'s `Slot`, a grab cancels the slot's running animation, such as a spring returning the element home.

```fsharp
/// <summary>A point anime tweens in place.</summary>
type Point = { mutable x: float; mutable y: float }

type Tile =
    {
        Offset: Signal<struct (float * float)>
        Spring: MotionSlot
    }

let springHome (tile: Tile) =
    let graph = Graph.Current
    let struct (x, y) = Api.untrack (fun () -> tile.Offset.Value)
    let point = { x = x; y = y }

    tile.Spring.Play (fun () ->
        Fable.Ranvier.Anime.Anime.animate (
            Targets.object point,
            Anim (
                x = 0,
                y = 0,
                ease = Ease.spring (stiffness = 170., damping = 11.),
                onUpdate = fun _ -> graph.Batch (fun () -> tile.Offset.Value <- struct (point.x, point.y))
            )
        ))

let tile =
    Component.template
        (fun _ () ->
            {
                Offset = Api.createSignal (struct (0., 0.))
                Spring = Motion.slot ()
            })
        (div () {
            div (
                div.className "tile",
                Motion.drag NativeDraggableOptions.Default (fun t ->
                    {
                        Position = t.Offset
                        Dragging = ValueNone
                        Slot = ValueSome t.Spring
                    })
            ) {
                "drag"
            }

            button (button.onClick (fun (t: Tile) _ -> springHome t)) { "Home" }
        })
```

## Reduced motion

`Reactive.policy ()` is the graph's motion policy, seeded from `prefers-reduced-motion` and writable. Under `Reduced`, `enter` and the presets' enters do not play, an exit finishes at once (its element leaves in the pass that retires its row), and `flip` neither records nor animates. `on`, `appear` and the reactive parts play unchanged; a play function that should honour the policy reads `Reactive.policy ()` itself.

## Server rendering and hydration

The three hook kinds behind the parts (motion, exit and reflow) add no markup, and `Server.render` omits them under both `Reject` and `Omit`: the server HTML of a template with Motion parts equals that of its twin without them (`tests/Ranvier.Anime.NativeSmoke/MotionServerProbe.fs`, part of the native smoke run). Hydration adopts the served elements; `appear` and `enter` leave them as served, and `on` plays.

## Costs

**Bundle,** rolldown (minified) and gzip level 9, each entry mounting one keyed list (`artifacts/browser/motion-bundle.json`, regenerated by `tests/Ranvier.View.Anime.Browser/motion-size.mjs`):

| Entry | Against | Δ gzip | Budget |
|---|---|--:|--:|
| rows with `Motion.presence` | the same rows with `li.motion` and `li.exit` calling `animate` by hand | +706 B | 900 B |
| a list with `Motion.flip` | the same list with `ul.reflow` calling `createLayout` by hand | +1071 B | 1100 B |

`flip`'s glue includes the `LayoutOptions` record and its conversion to anime's layout parameters, the price of the
parameter record both engines share, and the `FlipLayout` object through which the one `Motion.flip` reaches either
engine.

An entry using one Motion part declares no anime feature class (`AutoLayout`, `Draggable`, `ScrollObserver`, `TextSplitter`, `WAAPIAnimation`, `Timeline`) beyond those of the anime functions that part calls. The emitted module holds no `curry`, `uncurry`, `partialApply` or `createObj`.

**Per instance:** each part adds one placed hook and the anime objects it creates; `follow`, `animateOn`, `draw`, `morph` and `drag` add one core effect; a slot adds one cleanup.
