# Ranvier.View.Anime (native)

`Motion.*` template parts for Ranvier.View on .NET hosts such as native WebCore, powered by Ranvier.Anime instead of
anime.js. It mirrors the browser package in `Ranvier.View/src/Ranvier.View.Anime`: same namespace, module and part
names, and the same lifecycle (enter, exit and presence follow region passes, `flip` wraps the reflow hook, and exits pin
and hold the element until their animation completes).

```fsharp
open Ranvier.View.Anime

use motion = MotionHost.Install { Scheduler = Frames.forHost host; Computed = ComputedStyles.forHost host }

let row = Component.define (fun _ (current: unit -> int) -> current) (fun t -> t.li [| Motion.fade |] [| t.text (fun c -> string (c ())) |])
let list = Component.define (fun _ items -> items) (fun t ->
    t.ul [| Motion.flip LayoutOptions.Default |] [| t.each id (fun (items: Signal<int array>) -> items.Value) row |])
```

`scroll` and `drag` also need input, resize and viewport services: install with
`MotionHost.Install(services, { Draggable = draggableServices; Viewport = Viewport.provider host })`, where
`draggableServices` is the `DraggableNativeServices` record built from the host's `forHost` providers.

Install the host services on the host thread before mounting; parts read them when their element is placed.
`Reactive.policy ()` (in Ranvier.Anime) is a per-graph signal seeded `Full`; write `Reduced` to skip enter and FLIP and
finish exits at once.

## Parts

| Part | Native status |
|---|---|
| `on`, `appear`, `enter`, `exit`, `presence` | implemented |
| `fade`, `rise`, `scale`, `collapse` | implemented, same timings and eases |
| `flip` | implemented over `AutoLayout`; takes `LayoutOptions` |
| `slot` / `MotionSlot` | implemented |
| `follow` | implemented over `Composed.animate`; takes `ComposedAnimationOptions` |
| `animateOn` | implemented |
| `scroll` / `ScrollBinding` | implemented over `ScrollContainerRegistry`; takes `ScrollParams` (container, target, `ScrollOptions`) and links a `Timer`, `Timeline` or `ComposedAnimation` |
| `drag` / `DragBinding` | implemented over `NativeDraggable`; takes `NativeDraggableOptions` |
| `flipFrom`, `followCss`, `draw`, `morph`, `split` | not yet |

## Differences from the browser package

- Play functions receive a `Ranvier.Dom.Element` and return a `Ranvier.Anime.ITickable` (`on`/`appear`: `IRevertible`).
  `Timer`, `Timeline`, `ComposedAnimation` and `ComposedTimeline` implement it; `AutoLayout` is an `IRevertible`.
- Parameter types are Ranvier.Anime's (`LayoutOptions`, `ComposedAnimationOptions`) rather than anime.js parameter objects.
- An interrupted FLIP pass restarts from the rows' laid-out boxes instead of their in-flight visual boxes.

These are the points where the browser package is expected to move toward this one.

Coverage: `tests/Ranvier.Anime.NativeSmoke/MotionViewProbe.fs` mounts a real View list on WebCore and checks first pass,
enter, exit pinning and removal, FLIP on reorder and the reduced policy; `runInteraction` drives a real window scroll
and native mouse drag through `scroll` and `drag`, both signal directions included.
