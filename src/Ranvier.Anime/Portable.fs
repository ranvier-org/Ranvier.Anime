namespace Ranvier.Anime

open System
open System.Collections.Generic
open System.Globalization
open Ranvier
open Ranvier.Dom

// The portable surface: names with the same signatures in Fable.Ranvier.Anime, over AnimeHost.Current.

module internal Portable =
    // Nodes are compared by identity, falling back to isSameNode for hosts that rewrap nodes.
    let same (a: Node) (b: Node) = obj.ReferenceEquals(a, b) || a.isSameNode b

    let styleOf (element: Element) =
        match box element with
        | :? ElementCSSInlineStyle as styled -> styled.style
        | _ -> invalidArg "element" "The element has no inline style."

    let html (parameter: string) (element: Element) : HTMLElement =
        match box element with
        | :? HTMLElement as html -> html
        | _ -> invalidArg parameter "Expected an HTML element."

    let withComputed (services: AnimeServices) (options: ComposedAnimationOptions) =
        match options.Computed with
        | Some _ -> options
        | None -> { options with Computed = Some services.Computed }

    // Keeps the inline declaration `name` and returns its restore.
    let keepDeclaration (element: Element) (name: string) =
        let style = styleOf element
        let value = style.getPropertyValue name
        let priority = style.getPropertyPriority name

        fun () ->
            if String.IsNullOrEmpty value then style.removeProperty name |> ignore
            else style.setProperty(name, value, priority = priority)

    // Keeps the inline declaration `property` writes (transform for the transform components) and returns its restore.
    let keepInline (element: Element) (property: string) =
        if ElementTransforms.isTransform property then keepDeclaration element "transform"
        else keepDeclaration element (DomTargetHelpers.toKebab property)

    let invariant (value: float) = value.ToString("R", CultureInfo.InvariantCulture)

/// <summary>The asynchronous result of a completion callback.</summary>
type CompletionResult<'T> = System.Threading.Tasks.Task<'T>

/// <summary>Composes typed completion results for animation, timer and timeline handles.</summary>
[<RequireQualifiedAccess>]
module Completion =
    /// <summary>Resolves with the completed handle.</summary>
    let inline instance (handle: ^Handle) : CompletionResult<^Handle> =
        (^Handle : (member AsTask: unit -> CompletionResult<^Handle>) handle)

    /// <summary>Resolves with the callback result or faults with its exception.</summary>
    let inline thenResult (handle: ^Handle) (callback: ^Handle -> 'T) : CompletionResult<'T> =
        (^Handle : (member ThenResult: (^Handle -> 'T) -> CompletionResult<'T>) (handle, callback))

    /// <summary>Waits for the callback's task and propagates its result, fault or cancellation.</summary>
    let inline thenAsync (handle: ^Handle) (callback: ^Handle -> CompletionResult<'T>) : CompletionResult<'T> =
        (^Handle : (member ThenAsync: (^Handle -> CompletionResult<'T>) -> CompletionResult<'T>) (handle, callback))

[<AutoOpen>]
module PortableMuting =
    type ComposedAnimationOptions with
        /// <summary>Sets persistent muting before the animation starts.</summary>
        member this.WithMuted(value: bool) =
            { this with Playback = { this.Playback with Muted = value } }

/// <summary>Animations over the host installed with <c>AnimeHost.Install</c>.</summary>
[<RequireQualifiedAccess>]
module Anime =
    /// <summary>Animates the properties of several targets as one animation.</summary>
    /// <remarks>Options without computed styles read starting values through the host's.</remarks>
    /// <exception cref="T:System.InvalidOperationException">No host is installed on this thread.</exception>
    let animateTargets (targets: obj array) (specs: PropertySpec array) (options: ComposedAnimationOptions) : ComposedAnimation =
        let services = AnimeHost.Current
        Composed.animateTargets services.Scheduler targets specs (Some (Portable.withComputed services options))

    /// <summary>Animates the properties of one target.</summary>
    /// <remarks>Options without computed styles read starting values through the host's.</remarks>
    /// <exception cref="T:System.InvalidOperationException">No host is installed on this thread.</exception>
    let animate (target: obj) (specs: PropertySpec array) (options: ComposedAnimationOptions) : ComposedAnimation =
        if isNull target then nullArg "target"
        animateTargets [| target |] specs options

    let private createTimeline options =
        let services = AnimeHost.Current
        let created = Composed.createTimeline services.Scheduler options None
        created.DefaultComputed <- Some services.Computed
        created

    /// <summary>Creates an empty timeline on the host's scheduler.</summary>
    /// <remarks>Children added without computed styles read starting values through the host's.</remarks>
    /// <exception cref="T:System.InvalidOperationException">No host is installed on this thread.</exception>
    let timeline () : ComposedTimeline = createTimeline None

    /// <summary>Creates a timeline with persistent muting set before its children play.</summary>
    let timelineWithMute (muted: bool) : ComposedTimeline =
        createTimeline (Some { PlaybackDefaults.current() with Muted = muted })

    /// <summary>Starts a timer with persistent muting set before its first tick.</summary>
    let timerWithMute (duration: float) (muted: bool) : Timer =
        let options = { PlaybackDefaults.current() with Muted = muted }
        let timer = new Timer(AnimeHost.Current.Scheduler, duration, options = options)
        timer.Play() |> ignore
        timer

    /// <summary>Pauses the object at its current values when it is a tickable; any other revertible is left as it is.</summary>
    let pause (revertible: IRevertible) : unit =
        match revertible with
        | :? ITickable as tickable -> tickable.Pause()
        | _ -> ()

/// <summary>
/// One property of one element that follows a value. Later values animate from wherever the property is, and
/// <c>Revert</c> restores what the property was before the first.
/// </summary>
type Follower<'v> =
    /// <summary>Moves the property to the value. After <c>Revert</c> the call does nothing.</summary>
    abstract To: 'v -> unit
    /// <summary>Stops the running animation at its current value.</summary>
    abstract Pause: unit -> unit
    /// <summary>Cancels the running animation and restores the property.</summary>
    abstract Revert: unit -> unit

/// <summary>Followers of element properties, over the host installed with <c>AnimeHost.Install</c>.</summary>
[<RequireQualifiedAccess>]
module Follow =
    // A new composed animation per value, Replace composition taking over the running tween; the first is completed at once.
    [<Sealed>]
    type private ComposedFollower<'v>
        (services: AnimeServices, target: obj, options: ComposedAnimationOptions, spec: 'v -> PropertySpec, restore: unit -> unit) =
        let followerOptions = { options with Autoplay = true }
        let mutable current: ComposedAnimation voption = ValueNone
        let mutable reverted = false

        interface Follower<'v> with
            member _.To(value: 'v) =
                if not reverted then
                    let next =
                        ScopeRegistration.run ignore (fun () ->
                            Composed.animate services.Scheduler target [| spec value |] (Some followerOptions))
                    if current.IsNone then next.Complete() |> ignore
                    current <- ValueSome next

            member _.Pause() = current |> ValueOption.iter (fun animation -> animation.Pause() |> ignore)

            member _.Revert() =
                if not reverted then
                    reverted <- true
                    current |> ValueOption.iter (fun animation -> animation.Cancel())
                    restore ()

    // One retargetable track for the follower's life, written through the element's transform cache.
    [<Sealed>]
    type private TransformFollower
        (services: AnimeServices, element: Element, canonical: string, options: ComposedAnimationOptions,
         inlineOrig: string option, priority: string) =
        let mutable held: struct (Animatable * AnimatableNumber) voption = ValueNone
        let mutable reverted = false

        interface Follower<float> with
            member _.To(value: float) =
                if not reverted then
                    match held with
                    | ValueSome (struct (_, track)) -> track.RetargetPrecise(value, options.Duration, options.Ease)
                    | ValueNone ->
                        ElementTransforms.writeScalar element canonical priority value
                        let created = ScopeRegistration.run ignore (fun () -> new Animatable(services.Scheduler))

                        let trackOptions =
                            { AnimatablePropertyOptions.Default with
                                Playback = options.Playback
                                Ease = options.Ease
                                Modifier = options.Modifier
                                Precision = options.Precision |> Option.orElse AnimatablePropertyOptions.Default.Precision }

                        let handle =
                            NumericProperty((fun () -> value), ElementTransforms.writeScalar element canonical priority)

                        let track = ScopeRegistration.run ignore (fun () -> created.NumericPrecise(handle, options.Duration, trackOptions))
                        held <- ValueSome (struct (created, track))

            member _.Pause() =
                match held with
                | ValueSome (struct (_, track)) -> track.Playback.Pause() |> ignore
                | ValueNone -> ()

            member _.Revert() =
                if not reverted then
                    reverted <- true

                    match held with
                    | ValueSome (struct (group, _)) -> group.Cancel()
                    | ValueNone -> ()

                    ElementTransforms.revertComponents element [| canonical, inlineOrig |] priority

    /// <summary>
    /// Follows a value with the element's <c>property</c>: the first value is set at once, later ones animate from
    /// wherever the property is.
    /// </summary>
    /// <remarks>
    /// <c>options</c> give each retarget's duration, delay, ease, modifier and precision. A transform component
    /// (<c>x</c>, <c>scaleX</c>, <c>rotate</c> and the rest), in its default unit, keeps one retargetable track for the
    /// follower's life, so a value that changes every frame costs a retarget, not a new animation; it owns its component,
    /// so other animations of that component on the element do not take it over. Other properties start a composed
    /// animation per value, whose Replace composition takes over the running tween. <c>Revert</c> restores the property's
    /// inline declaration from before the follower was created.
    /// </remarks>
    /// <exception cref="T:System.InvalidOperationException">No host is installed on this thread.</exception>
    let property (element: Element) (property: string) (options: ComposedAnimationOptions) : Follower<float> =
        if isNull (box element) then nullArg "element"
        let services = AnimeHost.Current

        match ElementTransforms.canonicalName property with
        | Some canonical when canonical <> "translate3d" && canonical <> "rotate3d" && canonical <> "matrix" && canonical <> "matrix3d" ->
            let priority = (Portable.styleOf element).getPropertyPriority "transform"
            let _, inlineOrig = ElementTransforms.readWithInlineOrigin element canonical
            TransformFollower(services, element, canonical, options, inlineOrig, priority)
        | _ ->
            let restore = Portable.keepInline element property
            ComposedFollower<float>(
                services, box element, Portable.withComputed services options, (fun value -> PropertySpec.Scalar(property, value)), restore)

    /// <summary>
    /// Follows a typed numeric value with a CSS custom property of the element: the first value is set at once, later
    /// ones animate from the property's current value.
    /// </summary>
    /// <remarks>
    /// <c>initial</c> fixes the unit and is not written. Every value must carry numeric token metadata with that unit
    /// suffix, or <c>To</c> raises <c>ArgumentException</c>. <c>Revert</c> restores the inline value and priority from
    /// before the follower was created.
    /// </remarks>
    /// <exception cref="T:System.ArgumentException"><c>initial</c> carries no numeric token metadata.</exception>
    /// <exception cref="T:System.InvalidOperationException">No host is installed on this thread.</exception>
    let css
        (element: Element)
        (property: Ranvier.Css.CustomProperty<'s>)
        (options: ComposedAnimationOptions)
        (initial: Ranvier.Css.Value<'s>)
        : Follower<Ranvier.Css.Value<'s>> =
        if isNull (box element) then nullArg "element"
        let services = AnimeHost.Current
        let name = Ranvier.Css.RuntimeApi.customPropertyName property

        let numeric (parameter: string) (value: Ranvier.Css.Value<'s>) =
            match Ranvier.Css.RuntimeApi.tryNumericValue value with
            | Some (number, suffix) -> struct (number, suffix)
            | None -> invalidArg parameter (name + " values need numeric token metadata.")

        let struct (_, suffix) = numeric "initial" initial

        let spec (value: Ranvier.Css.Value<'s>) =
            let struct (number, unit) = numeric "value" value

            if unit <> suffix then
                invalidArg "value" (name + " changed its unit from '" + suffix + "' to '" + unit + "'.")

            PropertySpec.Text(name, Portable.invariant number + suffix)

        ComposedFollower<Ranvier.Css.Value<'s>>(
            services, box element, Portable.withComputed services options, spec, Portable.keepDeclaration element name)

    [<Sealed>]
    type private DrawFollower(services: AnimeServices, root: IDisposable, last: NumericProperty, options: ComposedAnimationOptions) =
        let followerOptions = { options with Autoplay = true }
        let mutable targetGoal = last.Value
        let mutable current: ComposedAnimation voption = ValueNone
        let mutable reverted = false

        interface Follower<float> with
            member _.To(fraction: float) =
                if not reverted && fraction <> targetGoal then
                    targetGoal <- fraction
                    current <-
                        ValueSome (
                            ScopeRegistration.run ignore (fun () ->
                                Composed.animate services.Scheduler (box last) [| PropertySpec.Scalar("value", fraction) |] (Some followerOptions)))

            member _.Pause() = current |> ValueOption.iter (fun animation -> animation.Pause() |> ignore)

            member _.Revert() =
                if not reverted then
                    reverted <- true
                    current |> ValueOption.iter (fun animation -> animation.Cancel())
                    root.Dispose()

    /// <summary>
    /// On an SVG geometry element: draws the stroke from its start to the fraction <c>initial</c>, in 0..1, at once; each
    /// <c>To</c> then animates the stroke's end to its fraction with <c>options</c>' timing.
    /// </summary>
    /// <remarks>
    /// Needs a current graph: the follower owns the Ranvier root its drawable lives in. <c>Revert</c> disposes the root,
    /// which restores the element's stroke attributes.
    /// </remarks>
    /// <exception cref="T:System.ArgumentException">The element is not an SVG geometry element.</exception>
    /// <exception cref="T:System.InvalidOperationException">No host is installed on this thread.</exception>
    let draw (element: Element) (options: ComposedAnimationOptions) (initial: float) : Follower<float> =
        let services = AnimeHost.Current

        let geometry =
            match box element with
            | :? SVGGeometryElement as geometry -> geometry
            | _ -> invalidArg "element" "Follow.draw needs an SVG geometry element."

        let root = Api.createRoot id

        try
            let last =
                Api.runWithOwner root (fun () ->
                    let drawable = Svg.createDrawable geometry (services.Computed.Get(element, None)) 0. initial
                    let _, last = SvgProperties.drawRange drawable 0. initial
                    last)

            DrawFollower(services, root, last, Portable.withComputed services options)
        with _ ->
            root.Dispose()
            reraise ()

    let private svgNamespace = "http://www.w3.org/2000/svg"

    [<Sealed>]
    type private MorphFollower(services: AnimeServices, element: SVGGeometryElement, options: ComposedAnimationOptions) =
        let followerOptions = { options with Autoplay = true }
        let tag = element.tagName.ToLowerInvariant()
        let attribute = if tag = "path" then "d" else "points"
        let original = element.getAttribute attribute
        let mutable morphRoot: IDisposable voption = ValueNone
        let mutable current: ComposedAnimation voption = ValueNone
        let mutable first = true
        let mutable reverted = false

        let disposeStep () =
            current |> ValueOption.iter (fun animation -> animation.Cancel())
            current <- ValueNone
            morphRoot |> ValueOption.iter (fun r -> r.Dispose())
            morphRoot <- ValueNone

        interface Follower<string> with
            member _.To(shape: string) =
                if reverted then
                    ()
                elif first then
                    first <- false
                    element.setAttribute(attribute, shape)
                else
                    disposeStep ()
                    let stepRoot = Api.createRoot id
                    morphRoot <- ValueSome stepRoot
                    try
                        let destination = (element :> Node).ownerDocument.createElementNS(svgNamespace, tag) :?> SVGGeometryElement
                        destination.setAttribute(attribute, shape)
                        let morphing = Api.runWithOwner stepRoot (fun () -> SvgProperties.morph element destination 0.33 None)

                        let animation =
                            ScopeRegistration.run ignore (fun () ->
                                Composed.animate services.Scheduler (box morphing.Progress) [| PropertySpec.Scalar("value", 1., fromValue = 0.) |]
                                    (Some followerOptions))

                        (animation :> ITickable).WhenCompleted(fun () ->
                            element.setAttribute(attribute, shape)
                            if morphRoot |> ValueOption.exists (fun r -> obj.ReferenceEquals(r, stepRoot)) then
                                stepRoot.Dispose()
                                morphRoot <- ValueNone)
                        current <- ValueSome animation
                    with _ ->
                        disposeStep ()
                        reraise ()

            member _.Pause() = current |> ValueOption.iter (fun animation -> animation.Pause() |> ignore)

            member _.Revert() =
                if not reverted then
                    reverted <- true
                    disposeStep ()

                    if isNull original then element.removeAttribute attribute
                    else element.setAttribute(attribute, original)

    /// <summary>
    /// On a <c>path</c>, <c>polygon</c> or <c>polyline</c>: the first <c>To</c> sets the shape, a <c>d</c> or
    /// <c>points</c> value, at once; later ones morph to it with <c>options</c>' timing, and each completed morph leaves
    /// exactly the shape given.
    /// </summary>
    /// <remarks>
    /// Needs a current graph: the follower owns the Ranvier root its morphs live in. <c>Revert</c> disposes the root and
    /// restores the shape the element had when the follower was created.
    /// </remarks>
    /// <exception cref="T:System.ArgumentException">The element is not an SVG geometry element.</exception>
    /// <exception cref="T:System.InvalidOperationException">No host is installed on this thread.</exception>
    let morph (element: Element) (options: ComposedAnimationOptions) : Follower<string> =
        let services = AnimeHost.Current

        match box element with
        | :? SVGGeometryElement as geometry -> MorphFollower(services, geometry, Portable.withComputed services options)
        | _ -> invalidArg "element" "Follow.morph needs a path, polygon or polyline element."

/// <summary>An identity set of nodes.</summary>
[<Sealed>]
type NodeSet() =
    let perThread = new System.Threading.ThreadLocal<List<Node>>(fun () -> List<Node>())
    let items () = perThread.Value

    /// <summary>The number of nodes in the set.</summary>
    member _.Count = (items ()).Count

    /// <summary>Whether the node is in the set.</summary>
    member _.Has(node: Node) : bool =
        let list = items ()
        let mutable found = false
        let mutable i = 0
        let count = list.Count
        while i < count && not found do
            if Portable.same list[i] node then found <- true
            i <- i + 1
        found

    /// <summary>Adds the node, unless the set has it.</summary>
    member this.Add(node: Node) : unit = if not (this.Has node) then (items ()).Add node

    /// <summary>Removes the node, if the set has it.</summary>
    member _.Remove(node: Node) : unit =
        let list = items ()
        let mutable i = list.Count - 1
        while i >= 0 do
            if Portable.same list[i] node then list.RemoveAt i
            i <- i - 1

    /// <summary>Whether <c>root</c> is, or contains, a node of the set.</summary>
    member _.AnyUnder(root: Node) : bool =
        let list = items ()
        let mutable found = false
        let mutable i = 0
        let count = list.Count
        while i < count && not found do
            if root.contains list[i] then found <- true
            i <- i + 1
        found

/// <summary>A FLIP layout over the live element children of a root, reused across passes.</summary>
/// <remarks>Created by <c>Flip.create</c>.</remarks>
type FlipLayout =
    /// <summary>
    /// Measures <c>children</c> before the change, ending the previous pass. <c>owned</c>: an element under the root is
    /// animated by something else, so the default visual properties are left alone for this pass.
    /// </summary>
    abstract Record: children: Element array * owned: bool -> unit
    /// <summary>Gives <c>owned</c> as it stands once the change is made, for the <c>Animate</c> that follows.</summary>
    /// <remarks>The native layout fixes the properties of a pass when it records, so the value recorded stays in force.</remarks>
    abstract SetOwned: owned: bool -> unit
    /// <summary>Animates the children last recorded to their new boxes.</summary>
    abstract Animate: unit -> unit
    /// <summary>Cancels the running pass and restores the declarations it owns.</summary>
    abstract Revert: unit -> unit

[<AbstractClass; Sealed>]
type private FlipBoxTls =
    [<ThreadStatic; DefaultValue>]
    static val mutable private boxes: List<struct (WeakReference<Node> * float * float)>

    static member Boxes =
        let current = FlipBoxTls.boxes
        if obj.ReferenceEquals(current, null) then
            let created = List<struct (WeakReference<Node> * float * float)>()
            FlipBoxTls.boxes <- created
            created
        else
            current

/// <summary>FLIP layout transitions over the host installed with <c>AnimeHost.Install</c>.</summary>
[<RequireQualifiedAccess>]
module Flip =
    let private takeBox (node: Node) =
        let boxes = FlipBoxTls.Boxes
        let mutable found = ValueNone
        let mutable i = boxes.Count - 1
        while i >= 0 do
            let struct (weak, left, top) = boxes[i]
            let mutable kept = Unchecked.defaultof<Node>
            if not (weak.TryGetTarget(&kept)) then
                boxes.RemoveAt i
            elif found.IsNone && Portable.same kept node then
                boxes.RemoveAt i
                found <- ValueSome(struct (left, top))
            i <- i - 1
        found

    // The layout properties other animations change themselves; an owned pass leaves them alone.
    let private visual =
        System.Collections.Frozen.FrozenSet.ToFrozenSet(LayoutOptions.Default.Properties, StringComparer.Ordinal)

    [<Sealed>]
    type private Layouts(root: HTMLElement, options: LayoutOptions) =
        let mutable layout: AutoLayout voption = ValueNone
        let mutable visualsOwned = false

        let release () =
            match layout with
            | ValueSome current ->
                layout <- ValueNone
                current.Revert()
            | ValueNone -> ()

        interface FlipLayout with
            member _.Record(children: Element array, owned: bool) =
                release ()
                visualsOwned <- owned
                let services = AnimeHost.Current

                let targets =
                    children
                    |> Array.choose (fun child ->
                        match box child with
                        | :? HTMLElement as html -> Some html
                        | _ -> None)

                let created =
                    Layout.create root services.Computed services.Scheduler (Some { options with Targets = Some targets }) None

                for child in targets do
                    match takeBox child with
                    | ValueSome (struct (left, top)) -> created.StartFrom(child, left, top)
                    | ValueNone -> ()

                layout <- ValueSome created

            member _.SetOwned(owned: bool) =
                visualsOwned <- owned

            member _.Animate() =
                layout
                |> ValueOption.iter (fun current ->
                    let overrideOpts =
                        if visualsOwned then
                            Some { options with Properties = options.Properties |> Array.filter (visual.Contains >> not) }
                        else
                            None
                    current.Animate(?animationOptions = overrideOpts) |> ignore)

            member _.Revert() = release ()

    /// <summary>Creates a FLIP layout over <c>root</c>; nothing is measured until the first <c>Record</c>.</summary>
    /// <remarks>
    /// <c>options</c> apply to every pass; <c>Targets</c> is replaced by the children recorded. Each <c>Record</c> reads
    /// the host installed on the thread.
    /// </remarks>
    /// <exception cref="T:System.ArgumentException">The root is not an HTML element.</exception>
    let create (root: Element) (options: LayoutOptions) : FlipLayout = Layouts(Portable.html "root" root, options)

    /// <summary>
    /// Keeps the element's current visual box, transforms included; the next <c>Record</c> that measures it starts it
    /// from there, keeping its recorded size.
    /// </summary>
    let keepBox (element: Element) : unit =
        let rect = element.getBoundingClientRect()
        let node = element :> Node
        let boxes = FlipBoxTls.Boxes
        let mutable i = boxes.Count - 1
        while i >= 0 do
            let struct (weak, _, _) = boxes[i]
            let mutable kept = Unchecked.defaultof<Node>
            if not (weak.TryGetTarget(&kept)) || Portable.same kept node then
                boxes.RemoveAt i
            i <- i - 1
        boxes.Add(struct (WeakReference<Node>(node), rect.left, rect.top))

    /// <summary>The root's HTML element children, without those in either set.</summary>
    let children (root: Element) (leaving: NodeSet) (entering: NodeSet) : Element array =
        let collection = (unbox<ParentNode> (box root)).children
        let len = int collection.length
        if len = 0 then
            [||]
        else
            let checkLeaving = leaving.Count > 0
            let checkEntering = entering.Count > 0
            let acc = List<Element>(len)
            for index = 0 to len - 1 do
                let child = collection.item (uint index)
                match box child with
                | :? HTMLElement ->
                    if (not checkLeaving || not (leaving.Has child)) && (not checkEntering || not (entering.Has child)) then
                        acc.Add child
                | _ -> ()
            acc.ToArray()

/// <summary>Scroll observation over the host installed with <c>AnimeHost.Install</c>.</summary>
[<RequireQualifiedAccess>]
module Scroll =
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
        | :? ComposedTimeline as timeline ->
            { Duration = (fun () -> timeline.Duration)
              CurrentTime = (fun () -> timeline.CurrentTime)
              Progress = (fun () -> timeline.Progress)
              Completed = (fun () -> timeline.Completed)
              Seek = (fun time muted -> timeline.Seek(time, muteCallbacks = muted) |> ignore)
              Command =
                function
                | ScrollCommand.Play -> timeline.Play() |> ignore
                | ScrollCommand.Pause -> timeline.Pause() |> ignore
                | ScrollCommand.Resume -> timeline.Resume() |> ignore
                | ScrollCommand.Reverse -> timeline.Reverse() |> ignore
                | ScrollCommand.Alternate -> timeline.Alternate() |> ignore
                | ScrollCommand.Restart -> timeline.Restart() |> ignore
                | ScrollCommand.Reset -> timeline.Reset() |> ignore
                | ScrollCommand.Complete -> timeline.Complete() |> ignore }
        | _ -> invalidArg "linked" "Scroll.observe links a Timer, Timeline, ComposedAnimation or ComposedTimeline."

    /// <summary>
    /// Observes <c>target</c> in <c>container</c>, or in the window when there is none, driving <c>linked</c> as
    /// <c>options</c>' sync says. <c>onEvent</c> receives each event with the observer's progress, in 0..1.
    /// </summary>
    /// <remarks>
    /// Needs <c>AnimeHost.Install</c> with interaction services. <c>linked</c> is a <c>Timer</c>, <c>Timeline</c>,
    /// <c>ComposedAnimation</c> or <c>ComposedTimeline</c>; it is paused, and the observer does not own it. Reverting the
    /// result ends the observation.
    /// </remarks>
    /// <exception cref="T:System.ArgumentException">The target is not an HTML element, or <c>linked</c> is of another type.</exception>
    /// <exception cref="T:System.InvalidOperationException">No host with interaction services is installed on this thread.</exception>
    let observe
        (container: HTMLElement option)
        (target: Element)
        (options: ScrollOptions)
        (linked: ITickable)
        (onEvent: ScrollEvent -> float -> unit)
        : IRevertible =
        let interaction, registry = AnimeHost.Interaction
        let target = Portable.html "target" target
        let link = linkOf linked

        let scrolled =
            match container with
            | Some element -> registry.ForElement element
            | None -> registry.ForViewport(interaction.Viewport, interaction.Draggable.WindowScroll)

        let observer =
            scrolled.ObserveLinked(
                link,
                [ target ],
                options = (fun () -> options),
                onEvent = (fun event (observer: ScrollObserver) -> onEvent event observer.Progress)
            )

        { new IRevertible with
            member _.Revert() = observer.Revert() }

/// <summary>Draggables over the host installed with <c>AnimeHost.Install</c>.</summary>
[<RequireQualifiedAccess>]
module Drag =
    /// <summary>Makes the element draggable.</summary>
    /// <remarks>
    /// Needs <c>AnimeHost.Install</c> with interaction services. <c>OnSettle</c> runs after the last frame of a release's
    /// motion, once the position is final. The caller owns the draggable: <c>Cancel</c> stops it where it is and
    /// <c>Revert</c> restores the element.
    /// </remarks>
    /// <exception cref="T:System.ArgumentException">The element is not an HTML element.</exception>
    /// <exception cref="T:System.InvalidOperationException">No host with interaction services is installed on this thread.</exception>
    let create (element: Element) (options: NativeDraggableOptions) : NativeDraggable =
        let interaction, _ = AnimeHost.Interaction
        NativeDraggable.createWith options interaction.Draggable (Portable.html "element" element)

/// <summary>The DOM work portable code needs, on the engine's own DOM types.</summary>
[<RequireQualifiedAccess>]
module DomKit =
    let private px (value: float) = Portable.invariant value + "px"

    /// <summary>Pins an HTML element absolute at its current box; other elements are left alone.</summary>
    /// <remarks>The element's offset parent is the containing block of the pinned box.</remarks>
    let pinOutOfFlow (element: Element) : unit =
        match box element with
        | :? HTMLElement as html ->
            let top, left, width, height = html.offsetTop, html.offsetLeft, html.offsetWidth, html.offsetHeight
            let style = Portable.styleOf element
            style.setProperty("position", "absolute")
            style.setProperty("top", px (float top))
            style.setProperty("left", px (float left))
            style.setProperty("width", px (float width))
            style.setProperty("height", px (float height))
            style.setProperty("margin", "0")
            style.setProperty("box-sizing", "border-box")
        | _ -> ()

    /// <summary>The height of the element's content, overflow included, in pixels.</summary>
    let scrollHeight (element: Element) : float = float element.scrollHeight

    /// <summary>Sets the inline declaration <c>name</c>, a kebab-case property name, to <c>value</c>.</summary>
    let setStyle (element: Element) (name: string) (value: string) : unit = (Portable.styleOf element).setProperty(name, value)

    /// <summary>Removes the inline declaration <c>name</c>, a kebab-case property name.</summary>
    let removeStyle (element: Element) (name: string) : unit = (Portable.styleOf element).removeProperty name |> ignore
