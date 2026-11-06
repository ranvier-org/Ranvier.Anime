namespace Ranvier.View.Anime

open System
open System.Collections.Generic
open Ranvier
open Ranvier.Dom
open Ranvier.Anime
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
/// <c>slot</c> for the animations a handler starts. The part names and lifecycles match the browser
/// <c>Ranvier.View.Anime</c>.
/// </summary>
/// <remarks>
/// Every part creates its animations once the element is placed and reverts them when the instance is disposed. On an
/// element whose exit, or whose ancestor's exit, follows the disposal, the part instead pauses its animations, so the exit
/// starts from the current values. The server omits every part. Under <c>Reactive.policy</c> <c>Reduced</c>, enter and
/// flip do not play and an exit finishes at once. Parts read <c>MotionHost.Current</c> when the element is placed.
/// </remarks>
[<RequireQualifiedAccess>]
module Motion =
    // Elements are compared by identity, falling back to isSameNode for hosts that rewrap nodes.
    let private same (a: Node) (b: Node) = obj.ReferenceEquals(a, b) || a.isSameNode b

    type private Marks() =
        let items = List<Node>()
        member _.Count = items.Count
        member _.Has(node: Node) = items.Exists(fun item -> same item node)
        member this.Add(node: Node) = if not (this.Has node) then items.Add node
        member _.Remove(node: Node) = items.RemoveAll(fun item -> same item node) |> ignore
        member _.Any(predicate: Node -> bool) = items.Exists(fun item -> predicate item)

    // Elements whose Motion exit runs.
    let private departing = Marks()
    // Elements whose Motion enter started in a pass that has not yet been laid out.
    let private arriving = Marks()
    // Elements whose Motion part animates them.
    let private animated = Marks()

    /// <summary>The number of elements whose Motion exit runs.</summary>
    let internal departures () = departing.Count

    let inline private nodeOf (node: 'native) : Node = unbox<Node> (box node)
    let inline private elementOf (node: 'native) : Element = unbox<Element> (box node)

    let private styleOf (element: Element) = (unbox<ElementCSSInlineStyle> (box element)).style

    let private nothing () = ()

    let private reduced () =
        match Api.untrack (fun () -> (Reactive.policy ()).Value) with
        | Reduced -> true
        | Full -> false

    let private options (services: MotionServices) (duration: float) (ease: string) =
        Some
            { ComposedAnimationOptions.Default with
                Duration = duration
                Ease = Ease.named ease
                Computed = Some services.Computed }

    let private animate (element: Element) (duration: float) (ease: string) (specs: PropertySpec array) =
        let services = MotionHost.Current
        Composed.animate services.Scheduler (box element) specs (options services duration ease)

    let private pauseIfPlayable (revertible: IRevertible) =
        match revertible with
        | :? ITickable as tickable -> tickable.Pause()
        | _ -> ()

    // The cleanup of a part's animation: paused while the element's exit follows, reverted otherwise.
    let private settle (context: MotionContext) (revertible: IRevertible) =
        fun () -> if context.ExitPending() then pauseIfPlayable revertible else revertible.Revert()

    // Marks the element as animated by a Motion part until `release` runs.
    let private owning (node: Node) (release: unit -> unit) =
        animated.Add node

        fun () ->
            animated.Remove node
            release ()

    let private pinOutOfFlow (element: Element) =
        match box element with
        | :? HTMLElement as html ->
            let top, left, width, height = html.offsetTop, html.offsetLeft, html.offsetWidth, html.offsetHeight
            let style = styleOf element
            let px (value: float) = value.ToString("R", Globalization.CultureInfo.InvariantCulture) + "px"
            style.setProperty("position", "absolute")
            style.setProperty("top", px (float top))
            style.setProperty("left", px (float left))
            style.setProperty("width", px (float width))
            style.setProperty("height", px (float height))
            style.setProperty("margin", "0")
            style.setProperty("box-sizing", "border-box")
        | _ -> ()

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
                    pinOutOfFlow (elementOf native)

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
        let style = styleOf element
        style.setProperty("overflow", "hidden")
        let height = float element.scrollHeight
        let animation = animate element 240. "outQuad" [| PropertySpec.Scalar("height", height, fromValue = 0.) |]

        (animation :> ITickable).WhenCompleted(fun () ->
            style.removeProperty "height" |> ignore
            style.removeProperty "overflow" |> ignore)

        animation

    let private fold (_: 'inst) (element: Element) =
        (styleOf element).setProperty("overflow", "hidden")
        animate element 180. "inQuad" [| PropertySpec.Scalar("height", 0.) |]

    /// <summary>
    /// Presence that unfolds the element's height from 0 and folds it back to 0, in flow, with its overflow hidden while
    /// it plays.
    /// </summary>
    let collapse<'native, 'inst> : Part<'native, 'inst> =
        Hooks.both (Hooks.motion (entering unfold), Hooks.exit (departure false fold))

    // The layout properties Motion parts animate themselves; a pass leaves them to the parts while one runs under the root.
    let private visual =
        HashSet<string>(LayoutOptions.Default.Properties, StringComparer.Ordinal)

    let private childElements (root: Element) : Element array =
        let children = (unbox<ParentNode> (box root)).children
        Array.init (int children.length) (fun index -> children.item (uint index)) |> Array.filter (isNull >> not)

    let private liveChildren (root: Element) =
        childElements root
        |> Array.choose (fun child ->
            match box child with
            | :? HTMLElement as html when not (departing.Has child) && not (arriving.Has child) -> Some html
            | _ -> None)

    /// <summary>
    /// FLIP for the rows of every region under the element: a Ranvier.Anime <c>AutoLayout</c> over the element's live
    /// element children, recorded before each flush's region passes and animated once their nodes and commits are in place.
    /// </summary>
    /// <remarks>
    /// <c>parameters</c> apply to every animation; <c>Targets</c> is replaced. A row whose Motion exit runs, or whose Motion
    /// enter started in the pass, is left out of the pass. The default layout properties (opacity, colours, borders,
    /// filters, clip paths and font size) apply to a pass in which no element under the root carries a Motion part; in a
    /// pass where one does, they are left to the parts. An interrupted pass restarts from the rows' laid-out boxes.
    /// </remarks>
    let flip (parameters: LayoutOptions) : Part<'native, 'inst> =
        Hooks.reflow (fun _ native ->
            let root = elementOf native
            let mutable layout: AutoLayout option = None
            let mutable recorded = false

            let release () =
                layout |> Option.iter (fun current -> current.Revert())
                layout <- None

            Api.onCleanup release

            let owned () =
                let under (node: Node) = (root :> Node).contains node
                animated.Any under || departing.Any under || arriving.Any under

            {
                Before =
                    fun () ->
                        recorded <- not (reduced ())

                        if recorded then
                            release ()
                            let services = MotionHost.Current

                            let properties =
                                if owned () then parameters.Properties |> Array.filter (visual.Contains >> not)
                                else parameters.Properties

                            let options = { parameters with Targets = Some (liveChildren root); Properties = properties }
                            layout <- Some (Layout.create (unbox<HTMLElement> (box root)) services.Computed services.Scheduler (Some options) None)
                After =
                    fun () ->
                        if recorded then
                            recorded <- false
                            layout |> Option.iter (fun current -> current.Animate() |> ignore)

                            for child in childElements root do
                                arriving.Remove child
            })

    /// <summary>
    /// A slot for the animations the current instance starts outside its parts, from a handler or an effect; call it in
    /// the instance's setup. The instance's disposal cancels the running animation and closes the slot.
    /// </summary>
    let slot () : MotionSlot =
        let created = MotionSlot()
        Api.onCleanup created.Close
        created

    let private transformNames =
        HashSet<string>(
            [| "x"; "y"; "z"; "translateX"; "translateY"; "translateZ"; "rotate"; "rotateX"; "rotateY"; "rotateZ"
               "scale"; "scaleX"; "scaleY"; "scaleZ"; "skew"; "skewX"; "skewY"; "perspective" |],
            StringComparer.Ordinal
        )

    // Keeps the inline declaration `property` writes (transform for the transform aliases) and returns its restore.
    let private keepInline (element: Element) (property: string) =
        let style = styleOf element

        let name =
            if transformNames.Contains property then "transform"
            else Text.RegularExpressions.Regex.Replace(property, "([a-z0-9])([A-Z])", "$1-$2").ToLowerInvariant()

        let value = style.getPropertyValue name
        let priority = style.getPropertyPriority name

        fun () ->
            if String.IsNullOrEmpty value then style.removeProperty name |> ignore
            else style.setProperty(name, value, priority = priority)

    /// <summary>
    /// Animates the element's <c>property</c> to <c>read</c>'s value whenever it changes, from wherever the property is;
    /// the first value is set at once.
    /// </summary>
    /// <remarks>
    /// <c>parameters</c> give each retarget's timing; Replace composition takes over the running tween. The cleanup restores
    /// the property's inline declaration from before the first value.
    /// </remarks>
    let follow (property: string) (parameters: ComposedAnimationOptions) (read: 'inst -> float) : Part<'native, 'inst> =
        Hooks.motion (
            Func<_, _, _, _>(fun inst native context ->
                let element = elementOf native
                let services = MotionHost.Current
                let restore = keepInline element property
                let mutable current: ComposedAnimation option = None

                Api.createEffectOn (fun () -> read inst) (fun value ->
                    let next =
                        Composed.animate services.Scheduler (box element) [| PropertySpec.Scalar(property, value) |]
                            (Some { parameters with Computed = parameters.Computed |> Option.orElse (Some services.Computed) })

                    if current.IsNone then next.Complete() |> ignore
                    current <- Some next)

                owning (nodeOf native) (fun () ->
                    if context.ExitPending() then
                        current |> Option.iter (fun animation -> animation.Pause() |> ignore)
                    else
                        current |> Option.iter (fun animation -> animation.Cancel())
                        restore ()))
        )

    /// <summary>
    /// Replays <c>play</c> with <c>read</c>'s value whenever it changes; Replace composition takes over the running
    /// animation's tweens of the same properties. The first value is played to its end at once.
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

    let private linkOf (tickable: ITickable) =
        match box tickable with
        | :? Timer as timer -> ScrollLink.OfTimer timer
        | :? Timeline as timeline -> ScrollLink.OfTimeline timeline
        | :? ComposedAnimation as animation ->
            { Duration = (fun () -> animation.Duration)
              CurrentTime = (fun () -> animation.CurrentTime)
              Progress = (fun () -> animation.Progress)
              Completed = (fun () -> animation.Completed)
              Seek = (fun time muted -> animation.Seek(time, muteCallbacks = muted) |> ignore)
              Command =
                function
                | ScrollCommand.Play -> animation.Play() |> ignore
                | ScrollCommand.Pause -> animation.Pause() |> ignore
                | ScrollCommand.Resume -> animation.Resume() |> ignore
                | ScrollCommand.Reverse -> animation.Reverse() |> ignore
                | ScrollCommand.Alternate -> animation.Alternate() |> ignore
                | ScrollCommand.Restart -> animation.Restart() |> ignore
                | ScrollCommand.Reset -> animation.Reset() |> ignore
                | ScrollCommand.Complete -> animation.Complete() |> ignore }
        | _ -> invalidArg "play" "Motion.scroll links a Timer, Timeline or ComposedAnimation."

    /// <summary>
    /// Links <c>play</c>'s animation to a scroll observer of the element, or of <c>parameters</c>' target, and writes the
    /// observer's progress and in-view state into the signals <c>bind</c> returns.
    /// </summary>
    /// <remarks>Needs <c>MotionHost.Install</c> with interaction services. <c>play</c> returns a <c>Timer</c>, <c>Timeline</c> or <c>ComposedAnimation</c>.</remarks>
    let scroll (parameters: ScrollParams) (bind: 'inst -> ScrollBinding) (play: 'inst -> Element -> #ITickable) : Part<'native, 'inst> =
        Hooks.motion (
            Func<_, _, _, _>(fun inst native context ->
                let binding = bind inst
                let graph = Graph.Current
                let interaction, registry = MotionHost.Interaction

                let container =
                    match parameters.Container with
                    | Some element -> registry.ForElement element
                    | None -> registry.ForViewport(interaction.Viewport, interaction.Draggable.WindowScroll)

                let tickable = play inst (elementOf native) :> ITickable
                let target = defaultArg parameters.Target (unbox<HTMLElement> (box native))

                let write (signal: Signal<'v> voption) (value: 'v) =
                    signal |> ValueOption.iter (fun signal -> graph.Batch(fun () -> signal.Value <- value))

                let onEvent event (observer: ScrollObserver) =
                    match event with
                    | ScrollEvent.Update -> write binding.Progress observer.Progress
                    | ScrollEvent.Enter -> write binding.InView true
                    | ScrollEvent.Leave -> write binding.InView false
                    | _ -> ()

                let observer =
                    container.ObserveLinked(linkOf tickable, [ target ], options = (fun () -> parameters.Options), onEvent = onEvent)

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
    /// Needs <c>MotionHost.Install</c> with interaction services. <c>parameters</c>' callbacks run before the binding's. On an
    /// element whose exit follows, the draggable stops where it is; otherwise the cleanup reverts it.
    /// </remarks>
    let drag (parameters: NativeDraggableOptions) (bind: 'inst -> DragBinding) : Part<'native, 'inst> =
        Hooks.motion (
            Func<_, _, _, _>(fun inst native context ->
                let binding = bind inst
                let graph = Graph.Current
                let interaction, _ = MotionHost.Interaction
                let callbacks = parameters.Callbacks

                let publish (draggable: NativeDraggable) =
                    graph.Batch(fun () -> binding.Position.Value <- struct (draggable.X, draggable.Y))

                let dragging value =
                    binding.Dragging |> ValueOption.iter (fun signal -> graph.Batch(fun () -> signal.Value <- value))

                let chained =
                    { callbacks with
                        OnUpdate = fun draggable -> callbacks.OnUpdate draggable; publish draggable
                        OnSettle = fun draggable -> callbacks.OnSettle draggable; publish draggable
                        OnGrab =
                            fun draggable ->
                                callbacks.OnGrab draggable
                                binding.Slot |> ValueOption.iter (fun slot -> slot.Stop())
                                dragging true
                        OnRelease = fun draggable -> callbacks.OnRelease draggable; dragging false }

                let draggable =
                    NativeDraggable.createWith { parameters with Callbacks = chained } interaction.Draggable (unbox<HTMLElement> (box native))

                Api.createEffectOn (fun () -> binding.Position.Value) (fun (struct (x, y)) ->
                    if x <> draggable.X then draggable.X <- x
                    if y <> draggable.Y then draggable.Y <- y)

                fun () -> if context.ExitPending() then draggable.Cancel() else draggable.Revert())
        )
