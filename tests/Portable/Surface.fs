/// <summary>
/// The portable surface of Ranvier.Anime as code: every portable name, used with its expected type. This file compiles
/// unchanged against Ranvier.Anime on .NET and against Fable.Ranvier.Anime under Fable; nothing in it has to run.
/// </summary>
module PortableSurface

open Ranvier
open Ranvier.Anime
open Ranvier.Anime.Dom

let dom (node: Node) (element: Element) (html: HTMLElement) : unit = ignore (node, element, html)

let contracts (revertible: IRevertible) (tickable: ITickable) : unit =
    let inherited: IRevertible = tickable
    let completed: bool = tickable.Completed
    let cancelled: bool = tickable.Cancelled
    let pause: unit = tickable.Pause()
    let cancel: unit = tickable.Cancel()
    let complete: unit = tickable.Complete()
    let whenCompleted: unit = tickable.WhenCompleted(fun () -> ())
    let revert: unit = revertible.Revert()
    ignore (inherited, completed, cancelled, pause, cancel, complete, whenCompleted, revert)

let easing () : unit =
    let named: float -> float = Ease.named "outQuad"
    let bezier: float -> float = Ease.cubicBezier 0.2 0. 0. 1.
    let steps: float -> float = Ease.steps 4u false
    ignore (named, bezier, steps)

let specs () : PropertySpec array =
    let scalar: PropertySpec = PropertySpec.Scalar("opacity", 1.)
    let scalarFull: PropertySpec = PropertySpec.Scalar("x", 100., fromValue = 0., duration = 200., delay = 20., ease = Ease.named "outQuad")
    let text: PropertySpec = PropertySpec.Text("width", "50%")
    let textFull: PropertySpec = PropertySpec.Text("color", "#fff", fromValue = "#000", duration = 200., delay = 20., ease = Ease.named "linear")
    [| scalar; scalarFull; text; textFull |]

let animationOptions () : ComposedAnimationOptions =
    { ComposedAnimationOptions.Default with
        Duration = 240.
        Ease = Ease.named "outQuad"
        Modifier = (fun (value: float) -> value)
        Autoplay = false }

let positions () : TimelinePosition array = [| TimelinePosition.At 120.; TimelinePosition.Expression "+=40" |]

let animation (target: obj) (targets: obj array) : ComposedAnimation =
    let animation: ComposedAnimation = Anime.animate target (specs ()) (animationOptions ())
    let several: ComposedAnimation = Anime.animateTargets targets (specs ()) (animationOptions ())
    let tickable: ITickable = animation
    let duration: float = animation.Duration
    let time: float = animation.CurrentTime
    animation.CurrentTime <- time
    let progress: float = animation.Progress
    animation.Progress <- progress
    let paused: bool = animation.Paused
    let completed: bool = animation.Completed
    let cancelled: bool = animation.Cancelled
    let play: ComposedAnimation = animation.Play()
    let pause: ComposedAnimation = animation.Pause()
    let resume: ComposedAnimation = animation.Resume()
    let reverse: ComposedAnimation = animation.Reverse()
    let restart: ComposedAnimation = animation.Restart()
    let seek: ComposedAnimation = animation.Seek 120.
    let complete: ComposedAnimation = animation.Complete()
    let cancel: unit = animation.Cancel()
    let revert: ComposedAnimation = animation.Revert()
    ignore (several, tickable, duration, paused, completed, cancelled, play, pause, resume, reverse, restart, seek, complete, cancel, revert)
    animation

let timeline (targets: obj array) : ComposedTimeline =
    let timeline: ComposedTimeline = Anime.timeline ()
    let tickable: ITickable = timeline
    let added: ComposedTimeline = timeline.Add(targets, specs ())
    let addedAt: ComposedTimeline = timeline.Add(targets, specs (), options = animationOptions (), position = TimelinePosition.At 0.)
    let label: ComposedTimeline = timeline.Label "start"
    let labelAt: ComposedTimeline = timeline.Label("middle", position = TimelinePosition.Expression "<")
    let duration: float = timeline.Duration
    let time: float = timeline.CurrentTime
    timeline.CurrentTime <- time
    let paused: bool = timeline.Paused
    let completed: bool = timeline.Completed
    let play: ComposedTimeline = timeline.Play()
    let pause: ComposedTimeline = timeline.Pause()
    let resume: ComposedTimeline = timeline.Resume()
    let reverse: ComposedTimeline = timeline.Reverse()
    let seek: ComposedTimeline = timeline.Seek 120.
    let cancel: unit = timeline.Cancel()
    let revert: ComposedTimeline = timeline.Revert()
    ignore (tickable, added, addedAt, label, labelAt, duration, paused, completed, play, pause, resume, reverse, seek, cancel, revert)
    timeline

let follower (follower: Follower<'v>) (value: 'v) : unit =
    let moved: unit = follower.To value
    let paused: unit = follower.Pause()
    let reverted: unit = follower.Revert()
    ignore (moved, paused, reverted)

let follow
    (element: Element)
    (property: Ranvier.Css.CustomProperty<'s>)
    (initial: Ranvier.Css.Value<'s>)
    : unit =
    let options = animationOptions ()
    let scalar: Follower<float> = Follow.property element "x" options
    let css: Follower<Ranvier.Css.Value<'s>> = Follow.css element property options initial
    let draw: Follower<float> = Follow.draw element options 0.5
    let morph: Follower<string> = Follow.morph element options
    follower scalar 10.
    follower css initial
    follower draw 1.
    follower morph "M0 0L10 10"

let nodes (node: Node) (root: Node) : NodeSet =
    let nodes: NodeSet = NodeSet()
    let count: int = nodes.Count
    let added: unit = nodes.Add node
    let removed: unit = nodes.Remove node
    let has: bool = nodes.Has node
    let under: bool = nodes.AnyUnder root
    ignore (count, added, removed, has, under)
    nodes

let layoutOptions () : LayoutOptions =
    let timed: LayoutOptions = LayoutOptions.Default.WithDuration(300u).WithDelay(20u)

    { timed with
        Duration = (fun _ -> 300.)
        Delay = (fun _ -> 20.)
        Ease = Ease.named "outQuad"
        Properties = [| "opacity"; "background-color" |] }

let flip (root: Element) (element: Element) (leaving: NodeSet) (entering: NodeSet) : unit =
    let layout: FlipLayout = Flip.create root (layoutOptions ())
    let children: Element array = Flip.children root leaving entering
    let kept: unit = Flip.keepBox element
    let recorded: unit = layout.Record(children, false)
    let animated: unit = layout.Animate()
    let reverted: unit = layout.Revert()
    ignore (kept, recorded, animated, reverted)

let scrollOptions () : ScrollOptions =
    let enter: ScrollThreshold = { Container = "end"; Target = "start" }
    let leave: ScrollThreshold = { Container = "start"; Target = "end" }

    let commands: ScrollCommand array =
        [| ScrollCommand.Play
           ScrollCommand.Pause
           ScrollCommand.Resume
           ScrollCommand.Reverse
           ScrollCommand.Alternate
           ScrollCommand.Restart
           ScrollCommand.Reset
           ScrollCommand.Complete |]

    let syncs: ScrollSync array =
        [| ScrollSync.Disabled
           ScrollSync.Smooth 0.25
           ScrollSync.Ease(Ease.named "outQuad")
           ScrollSync.Methods(Some ScrollCommand.Play, Some ScrollCommand.Pause)
           ScrollSync.DirectionalMethods(Some ScrollCommand.Play, None, Some ScrollCommand.Reverse, None) |]

    ignore commands

    { ScrollOptions.Default with
        Horizontal = false
        Repeat = true
        Enter = enter
        Leave = leave
        Sync = syncs[1] }

let scroll (container: HTMLElement option) (target: Element) (linked: ITickable) : IRevertible =
    let onEvent (event: ScrollEvent) (progress: float) : unit =
        match event with
        | ScrollEvent.Update
        | ScrollEvent.Enter
        | ScrollEvent.Leave -> ignore progress
        | _ -> ()

    Scroll.observe container target (scrollOptions ()) linked onEvent

let dragOptions (container: HTMLElement) (trigger: HTMLElement) : NativeDraggableOptions =
    let each (draggable: NativeDraggable) : unit = ignore draggable

    let callbacks: DraggableCallbacks =
        { DraggableCallbacks.Default with
            OnGrab = each
            OnDrag = each
            OnRelease = each
            OnUpdate = each
            OnSettle = each
            OnSnap = each
            OnResize = each
            OnAfterResize = each }

    { NativeDraggableOptions.Default with
        Container = (fun () -> Some container)
        Trigger = Some trigger
        Modifier = (fun (value: float) -> value)
        ReleaseMass = 1.
        ReleaseStiffness = 80.
        ReleaseDamping = 20.
        Cursor = true
        CursorHover = "grab"
        CursorGrab = "grabbing"
        Callbacks = callbacks }

let drag (element: Element) (container: HTMLElement) (trigger: HTMLElement) : NativeDraggable =
    let draggable: NativeDraggable = Drag.create element (dragOptions container trigger)
    let x: float = draggable.X
    let y: float = draggable.Y
    draggable.X <- x
    draggable.Y <- y
    let cancelled: unit = draggable.Cancel()
    let reverted: unit = draggable.Revert()
    ignore (cancelled, reverted)
    draggable

let splitOptions () : TextSplitterOptions =
    let wraps: SplitWrap array = [| SplitWrap.None; SplitWrap.Clip; SplitWrap.Overflow "hidden" |]

    let clones: SplitClone array =
        [| SplitClone.None; SplitClone.Center; SplitClone.Left; SplitClone.Right; SplitClone.Top; SplitClone.Bottom |]

    let template: SplitTemplateOptions =
        { SplitTemplateOptions.Default with
            Class = Some "word"
            Wrap = wraps[1]
            Clone = clones[0] }

    let modes: SplitMode array =
        [| SplitMode.Disabled
           SplitMode.Enabled
           SplitMode.Options template
           SplitMode.Template "<span>{value}</span>"
           SplitMode.TemplateBy(fun (_: Node) -> "<span>{value}</span>") |]

    { TextSplitterOptions.Default with
        Words = modes[2]
        Chars = modes[1]
        Lines = modes[0]
        Accessible = true
        IncludeSpaces = false
        Debug = false }

let split (element: Element) : TextSplitter =
    let splitter: TextSplitter = Text.split element (splitOptions ())
    let lines: HTMLElement array = splitter.Lines
    let words: HTMLElement array = splitter.Words
    let chars: HTMLElement array = splitter.Chars
    let frozen: unit = splitter.Freeze()
    let reverted: TextSplitter = splitter.Revert()
    ignore (lines, words, chars, frozen, reverted)
    splitter

let domKit (element: Element) : float =
    let pinned: unit = DomKit.pinOutOfFlow element
    let set: unit = DomKit.setStyle element "overflow" "hidden"
    let removed: unit = DomKit.removeStyle element "overflow"
    ignore (pinned, set, removed)
    DomKit.scrollHeight element

let reactive (animation: ComposedAnimation) (splitter: TextSplitter) (draggable: NativeDraggable) (observer: IRevertible) : bool =
    let policy: Signal<Reactive.MotionPolicy> = Reactive.policy ()
    let ownedAnimation: unit = Reactive.own animation
    let ownedSplitter: unit = Reactive.own splitter
    let ownedDraggable: unit = Reactive.own draggable
    let ownedObserver: unit = Reactive.own observer
    ignore (ownedAnimation, ownedSplitter, ownedDraggable, ownedObserver)

    match Api.untrack (fun () -> policy.Value) with
    | Reactive.Full -> false
    | Reactive.Reduced -> true
