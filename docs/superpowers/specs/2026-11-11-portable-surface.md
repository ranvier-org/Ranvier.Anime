# The portable surface: contract for both engines

Companion to `2026-11-11-unified-anime-api-design.md` (approved). Decisions taken with the owner:

- The raw anime.js bindings move to namespace **`Fable.Ranvier.Anime`** (assembly and package keep that name).
- The first release covers the **Motion-level tier** below. Everything else native stays native-only.
- Native is the reference: where a portable name already exists natively, its native signature is the contract and
  native code does not change. The Fable package mirrors it.

Everything here lives in namespace `Ranvier.Anime` in both packages. "Portable code" is F# source that compiles
unchanged against either package. It may use only what this document lists, plus Ranvier core (`Api`, `Signal`,
`Graph`) and `Ranvier.Css`.

The executable form of this contract is `tests/Portable/Surface.fs`: one file, compiled into
`tests/Ranvier.Anime.Tests` (native) and `tests/Fable.Ranvier.Anime.Tests` (Fable). It must use every portable name
with an explicit type annotation, and contain no `#if`.

## DOM

```fsharp
module Dom =                    // open Ranvier.Anime.Dom
    type Node        = <engine DOM Node>
    type Element     = <engine DOM Element>
    type HTMLElement = <engine DOM HTMLElement>
```

Native: `Ranvier.Dom`. Fable: `Fable.Core.TS.Dom`. Portable code takes and passes these but calls no members on them;
DOM work goes through `DomKit`.

## Contracts

```fsharp
type IRevertible = abstract Revert: unit -> unit
type ITickable =
    inherit IRevertible
    abstract Completed: bool
    abstract Cancelled: bool
    abstract Pause: unit -> unit
    abstract Cancel: unit -> unit
    abstract Complete: unit -> unit
    abstract WhenCompleted: (unit -> unit) -> unit
```

Native: as today (`Tickable.fs`). Fable: interfaces whose members are `[<Emit>]` over the anime object itself, so no
wrapper object is allocated; `WhenCompleted` chains onto `onComplete`.

## Easing

`Ease.named : string -> (float -> float)`, `Ease.cubicBezier : float -> float -> float -> float -> (float -> float)`,
`Ease.steps : uint -> bool -> (float -> float)`. Fable: anime's own easing functions. An unknown name raises on both.

## Animations

```fsharp
type PropertySpec with                       // native record; Fable: erased, same two factories
    static member Scalar: name: string * toValue: float * ?fromValue: float * ?duration: float * ?delay: float * ?ease: (float -> float) -> PropertySpec
    static member Text:   name: string * toValue: string * ?fromValue: string * ?duration: float * ?delay: float * ?ease: (float -> float) -> PropertySpec

type ComposedAnimationOptions                // portable fields only; build with { ComposedAnimationOptions.Default with ... }
    Duration: float; Ease: float -> float; Modifier: float -> float; Autoplay: bool

type TimelinePosition = At of float | Expression of string

type ComposedAnimation                       // also an ITickable
    Duration, CurrentTime (get/set), Progress (get/set), Paused, Completed, Cancelled
    Play() Pause() Resume() Reverse() Restart() Seek(time) Complete() Cancel() Revert()   // fluent ones return the animation

type ComposedTimeline                        // also an ITickable
    Add(targets: obj array, specs: PropertySpec array, ?options: ComposedAnimationOptions, ?position: TimelinePosition)
    Label(name, ?position), Duration, CurrentTime, Paused, Completed, Play() Pause() Resume() Reverse() Seek(time) Cancel() Revert()

module Anime =
    val animate:        target: obj -> specs: PropertySpec array -> options: ComposedAnimationOptions -> ComposedAnimation
    val animateTargets: targets: obj array -> specs: PropertySpec array -> options: ComposedAnimationOptions -> ComposedAnimation
    val timeline:       unit -> ComposedTimeline
    val pause:          revertible: IRevertible -> unit     // pauses a tickable; other revertibles are left as they are
```

Native: `Anime.*` call `Composed.*` with `AnimeHost.Current`'s scheduler and, when the options carry no `Computed`, its
computed styles. Fable: `animate` / `createTimeline` from animejs; a `PropertySpec` becomes one entry of the anime
parameter object (`{ from, to, duration, delay, ease }`).

## Host

```fsharp
type AnimeServices = { Scheduler: FrameScheduler; Computed: ComputedStyleService }            // native only
type AnimeInteractionServices = { Draggable: DraggableNativeServices; Viewport: ViewportProvider }   // native only
type AnimeHost =                                                                           // native only
    static member Install: AnimeServices -> IDisposable
    static member Install: AnimeServices * AnimeInteractionServices -> IDisposable
    static member Current: AnimeServices
```

This is today's `MotionHost` (`src/Ranvier.View.Anime/MotionHost.fs`) moved into the library and renamed. It is not
portable: only host start-up code touches it, and that code is native by nature. The Fable package has no host.

## Motion primitives

What `Ranvier.View.Anime` needs so that each part is written once. Each exists today as engine-specific code in one
half of `src/Ranvier.View.Anime/Motion.fs`; the implementation moves into the library unchanged in behaviour.

```fsharp
/// One property of one element that follows a value. The first To is applied at once; later ones animate from
/// wherever the property is. Revert cancels and restores the inline declaration from before the first value.
type Follower<'v> =
    abstract To: 'v -> unit
    abstract Pause: unit -> unit
    abstract Revert: unit -> unit

module Follow =
    val property: element: Element -> property: string -> options: ComposedAnimationOptions -> Follower<float>
    val css:      element: Element -> property: Ranvier.Css.CustomProperty<'s> -> options: ComposedAnimationOptions -> initial: Ranvier.Css.Value<'s> -> Follower<Ranvier.Css.Value<'s>>
    val draw:     element: Element -> options: ComposedAnimationOptions -> initial: float -> Follower<float>      // SVG stroke fraction 0..1
    val morph:    element: Element -> options: ComposedAnimationOptions -> Follower<string>                      // path d / points

/// An identity set of nodes.
type NodeSet =
    new: unit -> NodeSet
    member Count: int
    member Add: Node -> unit
    member Remove: Node -> unit
    member Has: Node -> bool
    member AnyUnder: root: Node -> bool

type LayoutOptions                           // portable fields: Duration, Delay, Ease, Properties; WithDuration, WithDelay
/// A FLIP layout over the live element children of a root, reused across passes.
type FlipLayout =
    /// Measures `children` before the change. `owned`: an element under the root is animated by something else, so the
    /// default visual properties are left alone for this pass.
    abstract Record: children: Element array * owned: bool -> unit
    /// `owned` as it stands once the change is made, for the Animate that follows. Native: the properties of a pass are
    /// fixed at Record, so this changes nothing.
    abstract SetOwned: owned: bool -> unit
    /// Animates the children last recorded to their new boxes.
    abstract Animate: unit -> unit
    abstract Revert: unit -> unit
module Flip =
    val create:   root: Element -> options: LayoutOptions -> FlipLayout
    /// Keeps the element's current visual box; the next Record that measures it starts it from there.
    val keepBox:  element: Element -> unit
    /// The root's element children, without those in either set.
    val children: root: Element -> leaving: NodeSet -> entering: NodeSet -> Element array

type ScrollThreshold, ScrollCommand, ScrollSync, ScrollOptions     // portable fields: Horizontal, Repeat, Enter, Leave, Sync
type ScrollEvent = ... | Update | Enter | Leave | ...               // native DU; Fable has at least these three cases
module Scroll =
    /// Observes `target` in `container` (None: the window), driving `linked`. `onEvent` receives the observer's progress.
    val observe: container: HTMLElement option -> target: Element -> options: ScrollOptions -> linked: ITickable -> onEvent: (ScrollEvent -> float -> unit) -> IRevertible

type DraggableCallbacks                      // OnGrab, OnDrag, OnRelease, OnUpdate, OnSettle, OnSnap, OnResize, OnAfterResize : NativeDraggable -> unit
type NativeDraggableOptions                  // portable fields: Container, Trigger, Modifier, ReleaseMass, ReleaseStiffness, ReleaseDamping, Cursor, CursorHover, CursorGrab, Callbacks
type NativeDraggable                         // X, Y: float get/set (a set raises no OnUpdate); Cancel() stops it where it is; Revert()
module Drag =
    val create: element: Element -> options: NativeDraggableOptions -> NativeDraggable
    // OnSettle runs after the last frame of a release's motion, once the position is final.

type SplitWrap, SplitClone, SplitTemplateOptions, SplitMode
type TextSplitterOptions                     // portable fields: Words, Chars, Lines, Accessible, IncludeSpaces, Debug
type TextSplitter                            // Lines, Words, Chars: HTMLElement array; Freeze() stops resize tracking; Revert()
module Text =
    val split: element: Element -> options: TextSplitterOptions -> TextSplitter

module DomKit =
    /// Pins an HTML element absolute at its current box; other elements are left alone.
    val pinOutOfFlow: Element -> unit
    val scrollHeight: Element -> float
    val setStyle:     Element -> name: string -> value: string -> unit     // kebab-case name, inline style
    val removeStyle:  Element -> name: string -> unit

module Reactive =
    type MotionPolicy = Full | Reduced
    val policy: unit -> Signal<MotionPolicy>
    val own: #IRevertible -> unit           // reverts when the current owner is disposed
```

Rules for both implementations:

- Engine-only record fields stay on the engine's record (`Computed`, `Registry`, `Playback`, ... natively; `Unit`,
  `Extra` under Fable). Portable code never names them.
- Native `Follow.property` keeps today's split: a retargetable transform track for transform components, a composed
  animation per value otherwise. Native `Text.split`, `Follow.draw` and `Follow.morph` own the Ranvier root they need
  and dispose it on `Revert`.
- Native portable functions read `AnimeHost.Current`; `Scroll.observe` and `Drag.create` need the interaction services.
- The existing native API is untouched apart from: `AutoLayout.StartFrom`, `TextSplitter.Freeze` become public; a
  portable `Revert`-returning-unit view is added where a native `Revert` is fluent.
