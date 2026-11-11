# Ranvier.Anime

A .NET port of [anime.js](https://animejs.com) 4.5 for Ranvier hosts. It runs on a Ranvier `FrameScheduler`, writes
through `Ranvier.Dom` and `Ranvier.Css`, is reflection-free and compiles under NativeAOT.

The same API is available in the browser as `Fable.Ranvier.Anime`, over the anime.js npm package. Code written against
the portable surface below compiles unchanged for either.

## The portable surface

Install the host's services once, on the host thread, before anything animates:

```fsharp
open Ranvier.Anime

let install (host: WebCoreHost) =
    AnimeHost.Install { Scheduler = Frames.forHost host; Computed = ComputedStyles.forHost host }
```

Then animate. No scheduler appears in a portable call; the functions read the installed host.

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

## Parity

Behaviour is checked against anime.js v4.5.0 (commit `2c9cf8e`) with reference traces generated from the upstream
source. Named and modern colour interpolation, some pending-variable CSS shorthand snapshots and the Three.js adapters
are not implemented. `THIRD-PARTY-NOTICES.md`, shipped in the package, carries the upstream notices.
