namespace Ranvier.View.Anime

// One source over the portable surface of Ranvier.Anime: the native engine on .NET hosts, anime.js through
// Fable.Ranvier.Anime under Fable.

open System
open Ranvier
open Ranvier.Anime
open Ranvier.Anime.Dom
open Ranvier.View.Representation
open Ranvier.View.Template
open Ranvier.View.Sugar

/// <summary>
/// The one animation an instance runs outside its parts, such as a spring a handler starts: playing another cancels the
/// running one, and the instance's disposal cancels it. A cancelled animation stops at its current values.
/// </summary>
/// <remarks>Created by <c>Motion.slot</c>.</remarks>
[<AllowNullLiteral>]
type MotionSlot internal () =
    let mutable running: ITickable = null
    let mutable closed = false

    /// <summary>The latest animation played, or null before the first play and after a stop.</summary>
    member _.Running = running

    /// <summary>Whether the latest animation is live: played, and not yet completed or cancelled.</summary>
    member _.Playing = not (isNull running) && not running.Completed && not running.Cancelled

    /// <summary>
    /// Cancels the running animation, then plays the one <c>start</c> creates while the instance is live; after its
    /// disposal the slot is closed and <c>Play</c> only cancels.
    /// </summary>
    member this.Play(start: unit -> #ITickable) =
        this.Stop()

        if not closed then
            let next = start () :> ITickable

            if not (isNull running) && not (obj.ReferenceEquals(running, next)) then
                running.Cancel()

            running <- next

    /// <summary>Cancels the running animation at its current values.</summary>
    member _.Stop() =
        if not (isNull running) then
            running.Cancel()
            running <- null

    member internal this.Close() =
        closed <- true
        this.Stop()

/// <summary>The instance signals a scroll-linked element writes; each is written while given.</summary>
[<NoEquality; NoComparison>]
type ScrollBinding =
    {
        /// <summary>The observer's progress between its enter and leave thresholds, in 0..1.</summary>
        Progress: Signal<float> voption
        /// <summary>Whether the element is between its thresholds.</summary>
        InView: Signal<bool> voption
    }

/// <summary>Where <c>Motion.scroll</c> observes: the scroll container, the observed element and the observer's thresholds and sync.</summary>
[<NoEquality; NoComparison>]
type ScrollParams =
    {
        /// <summary>The scrolling element; <c>None</c> follows the window.</summary>
        Container: HTMLElement option
        /// <summary>The element whose position drives progress; <c>None</c> observes the part's element.</summary>
        Target: HTMLElement option
        /// <summary>Thresholds, repeat and playback synchronisation.</summary>
        Options: ScrollOptions
    }

    /// <summary>The window, the part's element and the default thresholds.</summary>
    static member Default = { Container = None; Target = None; Options = ScrollOptions.Default }

/// <summary>The instance signals a draggable element shares with the rest of the instance.</summary>
[<NoEquality; NoComparison>]
type DragBinding =
    {
        /// <summary>The element's drag position in pixels, written by dragging and read to move the element.</summary>
        Position: Signal<struct (float * float)>
        /// <summary>True from grab to release.</summary>
        Dragging: Signal<bool> voption
        /// <summary>The slot whose running animation a grab cancels, such as the spring a release starts.</summary>
        Slot: MotionSlot voption
    }

/// <summary>
/// Template parts that animate elements with Ranvier.Anime: lifecycle parts (<c>on</c>, <c>appear</c>, <c>enter</c>,
/// <c>exit</c>, <c>presence</c> and its presets, <c>flip</c>) and reactive parts (<c>follow</c>, <c>animateOn</c>), with
/// <c>slot</c> for the animations a handler starts. The parts are the same source on .NET and under Fable.
/// </summary>
/// <remarks>
/// Every part creates its animations once the element is placed and reverts them when the instance is disposed. On an
/// element whose exit, or whose ancestor's exit, follows the disposal, the part instead pauses its animations, so the exit
/// starts from the current values. The server omits every part. Under <c>Reactive.policy</c> <c>Reduced</c>, enter and
/// flip do not play and an exit finishes at once. On .NET the parts animate over the host installed with
/// <c>AnimeHost.Install</c>, read when the element is placed.
/// </remarks>
[<RequireQualifiedAccess>]
module Motion =
    let inline private nodeOf (native: 'native) : Node = unbox<Node> (box native)
    let inline private elementOf (native: 'native) : Element = unbox<Element> (box native)

    // Elements whose Motion exit runs.
    let private departing = NodeSet()
    // Elements whose Motion enter started in a pass that has not yet been laid out.
    let private arriving = NodeSet()
    // Elements whose Motion part animates them.
    let private animated = NodeSet()
    let private none = NodeSet()

    /// <summary>The number of elements whose Motion exit runs.</summary>
    let internal departures () = departing.Count

    let private nothing () = ()

    let private reduced () =
        match Api.untrack (fun () -> (Reactive.policy ()).Value) with
        | Reactive.Reduced -> true
        | Reactive.Full -> false

    let private animate (element: Element) (duration: float) (ease: string) (specs: PropertySpec array) =
        Anime.animate (box element) specs { ComposedAnimationOptions.Default with Duration = duration; Ease = Ease.named ease }

    // The cleanup of a part's animation: paused while the element's exit follows, reverted otherwise.
    let private settle (context: MotionContext) (revertible: IRevertible) =
        fun () -> if context.ExitPending() then Anime.pause revertible else revertible.Revert()

    let private settleFollower (context: MotionContext) (follower: Follower<'v>) =
        fun () -> if context.ExitPending() then follower.Pause() else follower.Revert()

    // Marks the element as animated by a Motion part until `release` runs.
    let private owning (node: Node) (release: unit -> unit) =
        animated.Add node

        fun () ->
            animated.Remove node
            release ()

    let private entering (play: 'inst -> Element -> #ITickable) =
        Func<'inst, 'native, MotionContext, unit -> unit>(fun inst native context ->
            if context.Inserted && not (reduced ()) then
                let node = nodeOf native
                let tickable = play inst (elementOf native) :> ITickable
                arriving.Add node
                let stop = settle context tickable

                owning node (fun () ->
                    arriving.Remove node
                    stop ())
            else
                nothing)

    // Starts the exit, pinned out of flow when `pin`; the element leaves once the tickable completes, and at once under
    // Reduced. The returned cancel stops the tickable.
    let private departure (pin: bool) (play: 'inst -> Element -> #ITickable) =
        Func<'inst, 'native, unit -> unit, unit -> unit>(fun inst native finish ->
            let node = nodeOf native

            let finished () =
                departing.Remove node
                finish ()

            if reduced () then
                finished ()
                nothing
            else
                departing.Add node

                if pin then
                    DomKit.pinOutOfFlow (elementOf native)

                let tickable =
                    try
                        play inst (elementOf native) :> ITickable
                    with _ ->
                        departing.Remove node
                        reraise ()

                tickable.WhenCompleted finished

                fun () ->
                    departing.Remove node
                    tickable.Cancel())

    /// <summary>Plays on every placement, hydration included; the cleanup reverts the animation.</summary>
    let on (play: 'inst -> Element -> #IRevertible) : Part<'native, 'inst> =
        Hooks.motion (
            Func<_, _, _, _>(fun inst native context ->
                owning (nodeOf native) (settle context (play inst (elementOf native) :> IRevertible)))
        )

    /// <summary>Plays on every placement of an element created on the client; an element adopted from server HTML stays as served.</summary>
    let appear (play: 'inst -> Element -> #IRevertible) : Part<'native, 'inst> =
        Hooks.motion (
            Func<_, _, _, _>(fun inst native context ->
                if context.Hydrated then
                    nothing
                else
                    owning (nodeOf native) (settle context (play inst (elementOf native) :> IRevertible)))
        )

    /// <summary>
    /// Plays when the element's row or arm is created by a region pass after the region's first; rows of the first pass,
    /// hydrated rows and rows nested inside an entering row stay still.
    /// </summary>
    let enter (play: 'inst -> Element -> #ITickable) : Part<'native, 'inst> = Hooks.motion (entering play)

    /// <summary>On a row or arm root element: plays when the row is retired, and the element leaves when the tickable completes.</summary>
    /// <remarks>A forced removal, as when the mount is disposed, cancels the tickable and removes the element at once.</remarks>
    let exit (play: 'inst -> Element -> #ITickable) : Part<'native, 'inst> = Hooks.exit (departure false play)

    /// <summary>
    /// On a row or arm root element: <c>enter</c> and <c>exit</c> together. The leaving element is pinned out of flow,
    /// absolute at its current box, so later rows take its place while it plays.
    /// </summary>
    /// <remarks>The element's offset parent is the containing block of the pinned box; position the list's container.</remarks>
    let presence (enter: 'inst -> Element -> #ITickable) (exit: 'inst -> Element -> #ITickable) : Part<'native, 'inst> =
        Hooks.both (Hooks.motion (entering enter), Hooks.exit (departure true exit))

    /// <summary>Presence that fades the element in over 240 ms and out over 180 ms.</summary>
    let fade<'native, 'inst> : Part<'native, 'inst> =
        presence
            (fun (_: 'inst) element -> animate element 240. "outQuad" [| PropertySpec.Scalar("opacity", 1., fromValue = 0.) |])
            (fun _ element -> animate element 180. "inQuad" [| PropertySpec.Scalar("opacity", 0.) |])

    /// <summary>Presence that fades the element in while rising 12 px, and out while rising a further 12 px.</summary>
    let rise<'native, 'inst> : Part<'native, 'inst> =
        presence
            (fun (_: 'inst) element ->
                animate element 240. "outQuad" [| PropertySpec.Scalar("y", 0., fromValue = 12.); PropertySpec.Scalar("opacity", 1., fromValue = 0.) |])
            (fun _ element -> animate element 180. "inQuad" [| PropertySpec.Scalar("y", -12.); PropertySpec.Scalar("opacity", 0.) |])

    /// <summary>Presence that fades the element in from 92 % scale and out to it.</summary>
    let scale<'native, 'inst> : Part<'native, 'inst> =
        presence
            (fun (_: 'inst) element ->
                animate element 240. "outQuad" [| PropertySpec.Scalar("scale", 1., fromValue = 0.92); PropertySpec.Scalar("opacity", 1., fromValue = 0.) |])
            (fun _ element -> animate element 180. "inQuad" [| PropertySpec.Scalar("scale", 0.92); PropertySpec.Scalar("opacity", 0.) |])

    let private unfold (_: 'inst) (element: Element) =
        DomKit.setStyle element "overflow" "hidden"
        let height = DomKit.scrollHeight element
        let animation = animate element 240. "outQuad" [| PropertySpec.Scalar("height", height, fromValue = 0.) |]

        (animation :> ITickable).WhenCompleted(fun () ->
            DomKit.removeStyle element "height"
            DomKit.removeStyle element "overflow")

        animation

    let private fold (_: 'inst) (element: Element) =
        DomKit.setStyle element "overflow" "hidden"
        animate element 180. "inQuad" [| PropertySpec.Scalar("height", 0.) |]

    /// <summary>
    /// Presence that unfolds the element's height from 0 and folds it back to 0, in flow, with its overflow hidden while
    /// it plays.
    /// </summary>
    let collapse<'native, 'inst> : Part<'native, 'inst> =
        Hooks.both (Hooks.motion (entering unfold), Hooks.exit (departure false fold))

    /// <summary>
    /// FLIP for the rows of every region under the element: a layout over the element's live element children, recorded
    /// before each flush's region passes and animated once their nodes and commits are in place.
    /// </summary>
    /// <remarks>
    /// <c>parameters</c> apply to every animation. A row whose Motion exit runs, or whose Motion enter started in the pass,
    /// is left out of the pass. The default layout properties (opacity, colours, borders, filters, clip paths and font
    /// size) apply to a pass in which no element under the root carries a Motion part; in a pass where one does, they are
    /// left to the parts. An interrupted pass restarts from the rows' laid-out boxes.
    /// </remarks>
    let flip (parameters: LayoutOptions) : Part<'native, 'inst> =
        Hooks.reflow (fun _ native ->
            let root = elementOf native
            let rootNode = nodeOf native
            let layout = Flip.create root parameters
            let mutable recorded = false

            Api.onCleanup layout.Revert

            let owned () =
                animated.AnyUnder rootNode || departing.AnyUnder rootNode || arriving.AnyUnder rootNode

            {
                Before =
                    fun () ->
                        recorded <- not (reduced ())

                        if recorded then
                            layout.Record(Flip.children root departing arriving, owned ())
                After =
                    fun () ->
                        if recorded then
                            recorded <- false
                            layout.SetOwned(owned ())
                            layout.Animate()

                            for child in Flip.children root none none do
                                arriving.Remove child
            })

    /// <summary>
    /// Starts the element's next FLIP from its current visual box, drag transform included, instead of its recorded
    /// layout box: the next <c>flip</c> pass that measures the element as a row moves it from there to its new place.
    /// </summary>
    /// <remarks>
    /// Call before the change that moves the row, and before the drag position is reset. The FLIP keeps the recorded size;
    /// only the position moves. Under the ambient graph's <c>Reactive.policy</c> <c>Reduced</c> the call returns at once.
    /// </remarks>
    let flipFrom (element: Element) : unit =
        match Graph.TryCurrent with
        | ValueSome _ when reduced () -> ()
        | _ -> Flip.keepBox element

    /// <summary>
    /// A slot for the animations the current instance starts outside its parts, from a handler or an effect; call it in
    /// the instance's setup. The instance's disposal cancels the running animation and closes the slot.
    /// </summary>
    let slot () : MotionSlot =
        let created = MotionSlot()
        Api.onCleanup created.Close
        created

    /// <summary>
    /// Animates the element's <c>property</c> to <c>read</c>'s value whenever it changes, from wherever the property is;
    /// the first value is set at once.
    /// </summary>
    /// <remarks>
    /// <c>parameters</c> give each retarget's duration, ease and modifier; the follower is <c>Follow.property</c>'s. The
    /// cleanup restores the property's inline declaration from before the first value.
    /// </remarks>
    let follow (property: string) (parameters: ComposedAnimationOptions) (read: 'inst -> float) : Part<'native, 'inst> =
        Hooks.motion (
            Func<_, _, _, _>(fun inst native context ->
                let follower = Follow.property (elementOf native) property parameters
                Api.createEffectOn (fun () -> read inst) (fun value -> follower.To value)
                owning (nodeOf native) (settleFollower context follower))
        )

    /// <summary>Sets a typed numeric CSS custom property immediately, then animates each reader change from its current value.</summary>
    /// <remarks>
    /// Each reader value must carry numeric token metadata with the first value's unit suffix, or the effect raises
    /// <c>ArgumentException</c>. The part pauses during an exit and restores the original inline value and priority on
    /// ordinary disposal.
    /// </remarks>
    let followCss
        (property: Ranvier.Css.CustomProperty<'syntax>)
        (parameters: ComposedAnimationOptions)
        (read: 'inst -> Ranvier.Css.Value<'syntax>)
        : Part<'native, 'inst> =
        Hooks.motion (
            Func<_, _, _, _>(fun inst native context ->
                let follower = Follow.css (elementOf native) property parameters (Api.untrack (fun () -> read inst))
                Api.createEffectOn (fun () -> read inst) (fun value -> follower.To value)
                owning (nodeOf native) (settleFollower context follower))
        )

    /// <summary>
    /// Replays <c>play</c> with <c>read</c>'s value whenever it changes; the new animation takes over the running one's
    /// tweens of the same properties. The first value is played to its end at once.
    /// </summary>
    /// <remarks>The cleanup reverts the latest animation.</remarks>
    let animateOn (read: 'inst -> 'v) (play: 'inst -> Element -> 'v -> #ITickable) : Part<'native, 'inst> =
        Hooks.motion (
            Func<_, _, _, _>(fun inst native context ->
                let mutable current: ITickable = null

                Api.createEffectOn (fun () -> read inst) (fun value ->
                    let tickable = play inst (elementOf native) value :> ITickable
                    if isNull current then tickable.Complete()
                    current <- tickable)

                owning (nodeOf native) (fun () -> if not (isNull current) then settle context current ()))
        )

    /// <summary>
    /// On an SVG geometry element: draws the stroke from its start to the fraction <c>read</c> returns, in 0..1, animating
    /// to each new fraction with <c>parameters</c>' timing; the first fraction is drawn at once. The cleanup restores the
    /// element's stroke attributes.
    /// </summary>
    let draw (parameters: ComposedAnimationOptions) (read: 'inst -> float) : Part<'native, 'inst> =
        Hooks.motion (
            Func<_, _, _, _>(fun inst native context ->
                let follower = Follow.draw (elementOf native) parameters (Api.untrack (fun () -> read inst))
                Api.createEffectOn (fun () -> read inst) (fun fraction -> follower.To fraction)
                settleFollower context follower)
        )

    /// <summary>
    /// On a <c>path</c>, <c>polygon</c> or <c>polyline</c>: morphs the shape to the <c>d</c> or <c>points</c> that
    /// <c>read</c> returns whenever it changes, with <c>parameters</c>' timing. The first shape is set at once, and each
    /// completed morph leaves exactly the shape read. The cleanup restores the shape the element had before the first.
    /// </summary>
    let morph (parameters: ComposedAnimationOptions) (read: 'inst -> string) : Part<'native, 'inst> =
        Hooks.motion (
            Func<_, _, _, _>(fun inst native context ->
                let follower = Follow.morph (elementOf native) parameters
                Api.createEffectOn (fun () -> read inst) (fun shape -> follower.To shape)
                settleFollower context follower)
        )

    /// <summary>
    /// Links <c>play</c>'s animation to a scroll observer of the element, or of <c>parameters</c>' target, and writes the
    /// observer's progress and in-view state into the signals <c>bind</c> returns.
    /// </summary>
    /// <remarks>
    /// On .NET, needs <c>AnimeHost.Install</c> with interaction services, and <c>play</c> returns one of the tickables
    /// <c>Scroll.observe</c> links.
    /// </remarks>
    let scroll (parameters: ScrollParams) (bind: 'inst -> ScrollBinding) (play: 'inst -> Element -> #ITickable) : Part<'native, 'inst> =
        Hooks.motion (
            Func<_, _, _, _>(fun inst native context ->
                let binding = bind inst
                let graph = Graph.Current
                let element = elementOf native
                let tickable = play inst element :> ITickable

                let target =
                    match parameters.Target with
                    | Some target -> target :> Element
                    | None -> element

                let write (signal: Signal<'v> voption) (value: 'v) =
                    signal |> ValueOption.iter (fun signal -> graph.Batch(fun () -> signal.Value <- value))

                let onEvent (event: ScrollEvent) (progress: float) =
                    match event with
                    | ScrollEvent.Update -> write binding.Progress progress
                    | ScrollEvent.Enter -> write binding.InView true
                    | ScrollEvent.Leave -> write binding.InView false
                    | _ -> ()

                let observer = Scroll.observe parameters.Container target parameters.Options tickable onEvent
                let stop = settle context tickable

                fun () ->
                    stop ()
                    observer.Revert())
        )

    /// <summary>
    /// Makes the element draggable, two-way: dragging, and the end of a release's motion, write <c>Position</c>, and a write to
    /// <c>Position</c> from elsewhere moves the element. A grab cancels the running animation of <c>Slot</c>.
    /// </summary>
    /// <remarks>
    /// On .NET, needs <c>AnimeHost.Install</c> with interaction services. <c>parameters</c>' callbacks run before the
    /// binding's. On an element whose exit follows, the draggable stops where it is; otherwise the cleanup reverts it.
    /// </remarks>
    let drag (parameters: NativeDraggableOptions) (bind: 'inst -> DragBinding) : Part<'native, 'inst> =
        Hooks.motion (
            Func<_, _, _, _>(fun inst native context ->
                let binding = bind inst
                let graph = Graph.Current
                let callbacks = parameters.Callbacks

                let publish (draggable: NativeDraggable) =
                    graph.Batch(fun () -> binding.Position.Value <- struct (draggable.X, draggable.Y))

                let dragging (value: bool) =
                    binding.Dragging |> ValueOption.iter (fun signal -> graph.Batch(fun () -> signal.Value <- value))

                let chained =
                    { callbacks with
                        OnUpdate =
                            fun draggable ->
                                callbacks.OnUpdate draggable
                                publish draggable
                        OnSettle =
                            fun draggable ->
                                callbacks.OnSettle draggable
                                publish draggable
                        OnGrab =
                            fun draggable ->
                                callbacks.OnGrab draggable
                                binding.Slot |> ValueOption.iter (fun slot -> slot.Stop())
                                dragging true
                        OnRelease =
                            fun draggable ->
                                callbacks.OnRelease draggable
                                dragging false }

                let draggable = Drag.create (elementOf native) { parameters with Callbacks = chained }

                Api.createEffectOn (fun () -> binding.Position.Value) (fun (struct (x, y)) ->
                    if x <> draggable.X then draggable.X <- x
                    if y <> draggable.Y then draggable.Y <- y)

                fun () -> if context.ExitPending() then draggable.Cancel() else draggable.Revert())
        )

    /// <summary>
    /// Splits the element's static text with <c>Text.split</c> and plays <c>play</c> over the parts; the cleanup reverts
    /// the tickable, then the split. On an element whose exit follows, the split stays and stops resizing.
    /// </summary>
    /// <remarks>The element's content must be static text: a text slot or region inside it stops updating once split.</remarks>
    let split (parameters: TextSplitterOptions) (play: 'inst -> TextSplitter -> #ITickable) : Part<'native, 'inst> =
        Hooks.motion (
            Func<_, _, _, _>(fun inst native context ->
                let splitter = Text.split (elementOf native) parameters
                let tickable = play inst splitter :> ITickable

                fun () ->
                    if context.ExitPending() then
                        tickable.Pause()
                        splitter.Freeze()
                    else
                        tickable.Revert()
                        splitter.Revert() |> ignore)
        )
