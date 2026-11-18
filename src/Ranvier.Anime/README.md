# Ranvier.Anime

A .NET port of [anime.js](https://animejs.com) 4.5 for Ranvier hosts. It runs on a Ranvier `FrameScheduler`, writes
through `Ranvier.Dom` and `Ranvier.Css`, is reflection-free and compiles under NativeAOT.

The same API is available in the browser as `Fable.Ranvier.Anime`, over the anime.js npm package. Code written against
the portable surface below compiles unchanged for either.

## Host setup and native playback

Install the host's services once, on the host thread, before anything animates:

```fsharp
open Ranvier.Anime

let install (host: WebCoreHost) =
    AnimeHost.Install { Scheduler = Frames.forHost host; Computed = ComputedStyles.forHost host }
```

Then animate. No scheduler appears in a portable call; the functions read the installed host.

Use `Spring(bounce = 0.5).Stretch(600.)` to keep the spring's curve while fitting a playback interval in milliseconds.
Spring durations and layout options use physical milliseconds. `Animation.animateSpring` converts the settling duration
to the scheduler's time unit. When supplying a duration directly to a composed animation, pair `spring.Ease` with
`spring.SettlingDuration` in milliseconds mode or `spring.SettlingDuration / 1000.` in seconds mode.

The engine and playback controllers report `DeltaTime` in their engine's time unit. Draggable and scroll velocities
use CSS pixels per engine time unit. Pass a timer's delta to `Utils.dampInUnit engine.TimeUnit start finish delta factor`.
The standalone `Utils.damp` accepts milliseconds.
Use `ComposedAnimationOptions.Default.WithMuted(true)` to start an animation silently, or pass `muted = true`
to an individual property spec. Set `animation.Muted <- false` to resume output from its advancing playhead.
An endpoint reached while muted sets `Completed` and skips completion notification.

Colour animation endpoints accept modern CSS colour strings. To inspect the converted channels or compose work
after playback, use `Utils.parseColor` and `Completion`:

```fsharp
let channels = Utils.parseColor "oklch(65% 0.15 270 / 80%)"

let elapsed (animation: ComposedAnimation) =
    Completion.thenResult animation (fun finished -> finished.Duration)

let playNext (intro: ComposedAnimation) (outro: ComposedAnimation) =
    Completion.thenAsync intro (fun _ ->
        outro.Play() |> ignore
        Completion.instance outro)
```

Native completion returns a .NET task. Use `Revert(false)` to restore a composed animation's properties
without first seeking to the start. A property claimed by a newer animation keeps its current value.

## The portable surface

```fsharp
open Ranvier.Anime
open Ranvier.Anime.Dom

let appear (el: Element) : ComposedAnimation =
    Anime.animate
        (box el)
        [| PropertySpec.Scalar ("opacity", 1., fromValue = 0.); PropertySpec.Scalar ("y", 0., fromValue = 12.) |]
        { ComposedAnimationOptions.Default with Duration = 240.; Ease = Ease.named "outQuad" }
```

| Area | Names |
| --- | --- |
| Contracts | `IRevertible`, `ITickable` |
| Animations | `Anime.animate`, `Anime.animateTargets`, `Anime.timeline`, `PropertySpec`, `ComposedAnimationOptions`, `ComposedAnimation`, `ComposedTimeline`, `TimelinePosition` |
| Easing | `Ease.named`, `Ease.cubicBezier`, `Ease.steps` |
| Following a value | `Follow.property`, `Follow.css`, `Follow.draw`, `Follow.morph` |
| Layout | `Flip.create`, `Flip.keepBox`, `Flip.children`, `LayoutOptions` |
| Scroll | `Scroll.observe`, `ScrollOptions`, `ScrollSync`, `ScrollEvent` |
| Drag | `Drag.create`, `NativeDraggableOptions`, `NativeDraggable` |
| Text | `Text.split`, `TextSplitterOptions`, `TextSplitter` |
| Reactive | `Reactive.policy`, `Reactive.own` |
| DOM | `Ranvier.Anime.Dom` aliases, `DomKit`, `NodeSet` |

`Scroll.observe` and `Drag.create` need the host's input, resize and viewport services: use the `AnimeHost.Install`
overload taking `AnimeInteractionServices`.

Build option records from their defaults, naming only the fields you change:
`{ ComposedAnimationOptions.Default with Duration = 300. }`. Fields that exist on one engine only (`Computed`,
`Registry` and `Playback` here; `Unit` and `Extra` in the browser) are not portable.

## The native API

Everything the port has beyond the portable surface takes its scheduler explicitly and stays available:

- `Timer`, `Timeline`, `NumericAnimation` and `NumericProperty` handles, with `PlaybackOptions` for repeat, direction,
  speed and callbacks;
- `Keyframes`, `Keyframe` and `KeyframeSequence` for numeric and typed CSS keyframes, sparse rows and offsets;
- `CssAnimation` and `Animatable` for typed `Ranvier.Css` declarations and retargetable handles;
- `Composed.animate` and `Composed.createTimeline`, the scheduler-first forms of `Anime.*`;
- `Layout`, `ScrollContainerRegistry`, `NativeDraggable`, `Svg`, `Text`, `Waapi`, `Scope`, `Engine`, `Stagger`,
  `Spring` and `Utils`.

Objects created under a Ranvier owner are reverted when it is disposed; `Timer.own`, `Timeline.own` and
`Reactive.own` attach one explicitly.

Open `Ranvier.Anime.ComposedTimelineDsl` to build a paused `ComposedTimeline`.
Each `yield` adds a child or label in source order. Use `group` to animate several targets as one child.

```fsharp
let timeline =
    composedTimeline scheduler {
        yield label "intro"
        yield child title options titleProperties
        yield group cards options cardProperties |> at (TimelinePosition.Expression "intro")
    }
```

Open `Ranvier.Anime.KeyframeDsl` to author a `KeyframeSequence` with `keyframes { ... }`.
Yield `KeyframeRow.values` for sparse rows or `KeyframeOffset.at` for normalized positions.
Each sequence uses one kind of key and works with the existing numeric and typed CSS playback APIs.
An omitted property in a sparse row holds its previous endpoint. An offset key's ease applies to
the following segment for each property it contains. Use `timeline.Play()` after constructing a
composed timeline that should start immediately.

## Parity

Behaviour is checked against anime.js v4.5.0 (commit `2c9cf8e`) with reference traces generated from the upstream
source. Native interpolation also accepts named and modern CSS colours. Some pending-variable CSS shorthand
snapshots and the Three.js adapters remain unsupported. The package includes upstream notices in
`THIRD-PARTY-NOTICES.md`.
