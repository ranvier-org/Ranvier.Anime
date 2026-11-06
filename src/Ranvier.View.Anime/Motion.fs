namespace Ranvier.View.Anime

// One Motion API over two engines: anime.js through Fable.Ranvier.Anime in the browser, Ranvier.Anime on .NET hosts.
// The shared types and part signatures read the same on both; `Element` and `HTMLElement` are the DOM types of the target.

open System
open System.Collections.Generic
open Ranvier
#if FABLE_COMPILER
open Fable.Core
open Fable.Core.TS.Dom
#else
open Ranvier.Dom
#endif
open Ranvier.Anime
open Ranvier.View.Representation
open Ranvier.View.Template
open Ranvier.View.Sugar

// The ITickable members the shared types use, named per engine.
module internal Tick =
#if FABLE_COMPILER
    let completed (tickable: ITickable) = tickable.completed
    let cancelled (tickable: ITickable) = tickable.cancelled
    let cancel (tickable: ITickable) = tickable.cancel ()
#else
    let completed (tickable: ITickable) = tickable.Completed
    let cancelled (tickable: ITickable) = tickable.Cancelled
    let cancel (tickable: ITickable) = tickable.Cancel()
#endif

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
    member _.Playing = not (isNull running) && not (Tick.completed running) && not (Tick.cancelled running)

    /// <summary>
    /// Cancels the running animation, then plays the one <c>start</c> creates while the instance is live; after its
    /// disposal the slot is closed and <c>Play</c> only cancels.
    /// </summary>
    member this.Play(start: unit -> #ITickable) =
        this.Stop()

        if not closed then
            let next = start () :> ITickable

            if not (isNull running) && not (obj.ReferenceEquals(running, next)) then
                Tick.cancel running

            running <- next

    /// <summary>Cancels the running animation at its current values.</summary>
    member _.Stop() =
        if not (isNull running) then
            Tick.cancel running
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
#if FABLE_COMPILER
[<RequireQualifiedAccess>]
module Motion =
    // Sets, not WeakSets: flip scans their members, and every member leaves with its part's cleanup or its exit's end.
    [<Emit("new Set()")>]
    let private markSet () : obj = jsNative

    // Elements whose Motion exit runs.
    let private departing = markSet ()
    // Elements whose Motion enter started in a pass that has not yet been laid out.
    let private arriving = markSet ()
    // Elements whose Motion part animates them.
    let private animated = markSet ()

    [<Emit("$0.size")>]
    let private sizeOf (set: obj) : int = jsNative

    /// <summary>The number of elements whose Motion exit runs.</summary>
    let internal departures () = sizeOf departing

    [<Emit("$0.add($1)")>]
    let private mark (set: obj) (element: obj) : unit = jsNative

    [<Emit("$0.delete($1)")>]
    let private forget (set: obj) (element: obj) : unit = jsNative

    [<Emit("(r => { if (typeof r.pause === 'function') r.pause(); else if (r.animations) for (const k in r.animations) r.animations[k].pause(); })($0)")>]
    let private pauseIfPlayable (revertible: obj) : unit = jsNative

    [<Emit("((t, k, f) => { const previous = t[k]; t[k] = self => { if (previous) previous(self); f(self); }; })($0, $1, $2)")>]
    let private chain (target: obj) (callback: string) (after: obj -> unit) : unit = jsNative

    [<Emit("Object.assign({}, $0, {[$1]: $2})")>]
    let private withKey (parameters: 'p) (key: string) (value: obj) : 'p = jsNative

    [<Emit("Object.assign({[$1]: $2}, $0)")>]
    let private withDefault (parameters: 'p) (key: string) (value: obj) : 'p = jsNative

    [<Emit("(e => { const top = e.offsetTop, left = e.offsetLeft, width = e.offsetWidth, height = e.offsetHeight, s = e.style; s.position = 'absolute'; s.top = top + 'px'; s.left = left + 'px'; s.width = width + 'px'; s.height = height + 'px'; s.margin = '0'; s.boxSizing = 'border-box'; })($0)")>]
    let private pinOutOfFlow (element: obj) : unit = jsNative

    [<Emit("Array.prototype.filter.call($0.children, c => !$1.has(c) && !$2.has(c))")>]
    let private liveChildren (root: obj) (leaving: obj) (entering: obj) : Element array = jsNative

    // Takes the layout's visual properties (opacity, colours, borders, filters, clip paths, font size) that `parameters`
    // do not name out of its animated and recorded properties while an element under the root carries a Motion part, and
    // puts them back otherwise. anime 4.5.0 has no public way to remove them (AutoLayout's `properties` and
    // `recordedProperties` Sets).
    [<Emit("((l, p, r, a, b, c) => { if (!l.motionVisual) { const named = new Set([...(p.properties || []), ...Object.keys(p.enterFrom || {}), ...Object.keys(p.leaveTo || {}), ...Object.keys(p.swapAt || {})]); l.motionVisual = ['opacity', 'fontSize', 'color', 'backgroundColor', 'borderRadius', 'border', 'filter', 'clipPath'].filter(n => !named.has(n)); } const owned = [a, b, c].some(s => { for (const n of s) if (r.contains(n)) return true; return false; }); for (const n of l.motionVisual) { if (owned) { l.properties.delete(n); l.recordedProperties.delete(n); } else { l.properties.add(n); l.recordedProperties.add(n); } } })($0, $1, $2, $3, $4, $5)")>]
    let private leaveVisuals (layout: AutoLayout) (parameters: obj) (root: obj) (animated: obj) (departing: obj) (arriving: obj) : unit =
        jsNative

    [<Emit("(r => { for (const c of r.children) $1.delete(c); })($0)")>]
    let private laidOut (root: obj) (arriving: obj) : unit = jsNative

    [<Emit("$0.children = $1")>]
    let private measureChildren (layout: AutoLayout) (children: Element array) : unit = jsNative

    [<Emit("$0.motionFrom = $0.getBoundingClientRect()")>]
    let private keepBox (element: Element) : unit = jsNative

    // Moves the recorded box of each child carrying a `flipFrom` box to that box, and removes the box.
    [<Emit("((l, c) => { for (const e of c) { const r = e.motionFrom; if (r) { delete e.motionFrom; const p = l.oldState.getNode(e).properties; p.x += r.left - p.left; p.y += r.top - p.top; p.left = r.left; p.top = r.top; } } })($0, $1)")>]
    let private startFrom (layout: AutoLayout) (children: Element array) : unit = jsNative

    // The parameters without the shared unit, which anime would read as a property, and with the property's own entry,
    // which takes the shared unit when it sets none.
    [<Emit("(({unit, ...p}, k) => { const e = p[k]; p[k] = Object.assign({unit}, typeof e === 'number' ? {duration: e} : e); return p; })($0, $1)")>]
    let private withUnit (parameters: obj) (property: string) : AnimatableParams = jsNative

    [<Emit("$0.animations[$1].complete()")>]
    let private completeProperty (animatable: Animatable) (property: string) : unit = jsNative

    [<Emit("((el, f) => ({ get draw() { return f; }, set draw(v) { f = v; el.setAttribute('draw', '0 ' + v); } }))($0, $1)")>]
    let private strokeTarget (drawable: Element) (initial: float) : obj = jsNative

    [<Emit("($0.tagName.toLowerCase() === 'path' ? 'd' : 'points')")>]
    let private shapeAttribute (element: obj) : string = jsNative

    [<Emit("((e, a, v) => { const shape = e.ownerDocument.createElementNS('http://www.w3.org/2000/svg', e.tagName.toLowerCase()); shape.setAttribute(a, v); return shape; })($0, $1, $2)")>]
    let private shapeLike (element: obj) (attribute: string) (shape: string) : Element = jsNative

    [<Emit("$0.setAttribute($1, $2)")>]
    let private setShape (element: obj) (attribute: string) (shape: string) : unit = jsNative

    [<Emit("(d => { d.stop(); d.disable(); d.$target.classList.remove('is-disabled'); d.updateTicker.pause(); d.overshootTicker.pause(); d.resizeTicker.pause(); for (const k in d.animate.animations) d.animate.animations[k].pause(); d.resizeObserver.disconnect(); })($0)")>]
    let private freezeDraggable (draggable: Draggable) : unit = jsNative

    // Runs `after` once each of the draggable's position animations completes, after its last frame.
    [<Emit("((d, f) => { for (const k of [d.xProp, d.yProp]) { const a = d.animate.animations[k]; if (a) { const p = a.onComplete; a.onComplete = s => { if (p) p(s); f(s); }; } } })($0, $1)")>]
    let private whenPositioned (draggable: Draggable) (after: obj -> unit) : unit = jsNative

    [<Emit("(s => { clearTimeout(s.resizeTimeout); s.resizeObserver.disconnect(); })($0)")>]
    let private freezeSplitter (splitter: TextSplitter) : unit = jsNative

    [<Emit("((e, names) => { const kept = names.map(n => e.getAttribute(n)); return () => names.forEach((n, i) => kept[i] == null ? e.removeAttribute(n) : e.setAttribute(n, kept[i])); })($0, $1)")>]
    let private keepAttributes (element: obj) (names: string array) : unit -> unit = jsNative

    [<Emit("$0.scrollHeight")>]
    let private contentHeight (element: obj) : float = jsNative

    [<Emit("$0.style.overflow = $1")>]
    let private overflow (element: obj) (value: string) : unit = jsNative

    [<Emit("Object.assign({}, $0, $1 ? {container: $1} : {}, {target: $2})")>]
    let private observing (parameters: obj) (container: obj) (target: obj) : Ranvier.Anime.ScrollParams = jsNative

    // The numeric-token checks of Fable.Ranvier.Anime's internal CssAnimationRuntime, which Fable cannot reach from a
    // package under another assembly name.
    let private unitOf (value: Ranvier.Css.Value<'syntax>) =
        match Ranvier.Css.RuntimeApi.tryNumericValue value with
        | Some (number, suffix) when not (Double.IsNaN number || Double.IsInfinity number) -> suffix
        | _ -> invalidArg "value" "Animation endpoints require numeric CSS tokens. Animate a variable consumed by the CSS expression."

    let private requireUnit expected value =
        if unitOf value <> expected then
            invalidArg "value" "Animation endpoints must use the same unit suffix. Convert endpoints to a common unit first."

    [<Emit("(() => { const style = $0.style; const value = style.getPropertyValue($1); const priority = style.getPropertyPriority($1); return () => { if (value) style.setProperty($1, value, priority); else style.removeProperty($1); }; })()")>]
    let private captureProperty (element: Element) (name: string) : unit -> unit = jsNative

    [<Emit("Object.assign({}, $0, {unit: $1})")>]
    let private propertyWithUnit (parameters: AnimatableProp) (suffix: string) : AnimatableProp = jsNative

    let inline private targetsOf (node: 'native) : Targets = Erased.cast node

    let inline private elementOf (node: 'native) : Element = Erased.cast node

    let private nothing () = ()

    let private reduced () =
        match Api.untrack (fun () -> (Reactive.policy ()).Value) with
        | Reactive.Reduced -> true
        | Reactive.Full -> false

    // The cleanup of a part's anime object: paused while the element's exit follows, reverted otherwise.
    let private settle (context: MotionContext) (revertible: IRevertible) =
        let release () =
            if context.ExitPending () then
                pauseIfPlayable revertible
            else
                revertible.revert ()

        release

    // Marks the element as animated by a Motion part until `release` runs.
    let private owning (element: obj) (release: unit -> unit) =
        mark animated element

        let owned () =
            forget animated element
            release ()

        owned

    let private entering (play: 'inst -> Element -> #ITickable) =
        Func<'inst, 'native, MotionContext, unit -> unit>(fun inst node context ->
            if context.Inserted && not (reduced ()) then
                let element = box node
                let tickable = play inst (elementOf node) :> ITickable
                mark arriving element
                let stop = settle context tickable

                let release () =
                    forget arriving element
                    stop ()

                owning element release
            else
                nothing)

    // Starts the exit, pinned out of flow when `pin`; the element leaves once the tickable completes, and at once under
    // Reduced. The returned cancel stops the tickable.
    let private departure (pin: bool) (play: 'inst -> Element -> #ITickable) =
        Func<'inst, 'native, unit -> unit, unit -> unit>(fun inst node finish ->
            let element = box node

            let finished () =
                forget departing element
                finish ()

            if reduced () then
                finished ()
                nothing
            else
                mark departing element

                if pin then
                    pinOutOfFlow element

                let tickable =
                    try
                        play inst (elementOf node) :> ITickable
                    with _ ->
                        forget departing element
                        reraise ()

                chain tickable "onComplete" (fun _ -> finished ())

                let cancel () =
                    forget departing element
                    tickable.cancel ()

                cancel)

    let on (play: 'inst -> Element -> #IRevertible) : Part<'native, 'inst> =
        Hooks.motion (Func<_, _, _, _>(fun inst node context -> owning (box node) (settle context (play inst (elementOf node) :> IRevertible))))

    let appear (play: 'inst -> Element -> #IRevertible) : Part<'native, 'inst> =
        Hooks.motion (
            Func<_, _, _, _>(fun inst node context ->
                if context.Hydrated then
                    nothing
                else
                    owning (box node) (settle context (play inst (elementOf node) :> IRevertible)))
        )

    let enter (play: 'inst -> Element -> #ITickable) : Part<'native, 'inst> = Hooks.motion (entering play)

    let exit (play: 'inst -> Element -> #ITickable) : Part<'native, 'inst> = Hooks.exit (departure false play)

    let presence (enter: 'inst -> Element -> #ITickable) (exit: 'inst -> Element -> #ITickable) : Part<'native, 'inst> =
        Hooks.both (Hooks.motion (entering enter), Hooks.exit (departure true exit))

    let fade<'native, 'inst> : Part<'native, 'inst> =
        presence
            (fun (_: 'inst) node -> Anime.animate (node, Anim (opacity = Value.fromTo (0, 1), duration = 240, ease = Ease.outQuad)))
            (fun _ node -> Anime.animate (node, Anim (opacity = 0, duration = 180, ease = Ease.inQuad)))

    let rise<'native, 'inst> : Part<'native, 'inst> =
        presence
            (fun (_: 'inst) node ->
                Anime.animate (node, Anim (y = Value.fromTo (12, 0), opacity = Value.fromTo (0, 1), duration = 240, ease = Ease.outQuad)))
            (fun _ node -> Anime.animate (node, Anim (y = -12, opacity = 0, duration = 180, ease = Ease.inQuad)))

    let scale<'native, 'inst> : Part<'native, 'inst> =
        presence
            (fun (_: 'inst) node ->
                Anime.animate (node, Anim (scale = Value.fromTo (0.92, 1), opacity = Value.fromTo (0, 1), duration = 240, ease = Ease.outQuad)))
            (fun _ node -> Anime.animate (node, Anim (scale = 0.92, opacity = 0, duration = 180, ease = Ease.inQuad)))

    let private unfold (_: 'inst) (node: Element) =
        let element = box node
        overflow element "hidden"

        Anime.animate (
            node,
            Anim (
                height = Value.fromTo (0, contentHeight element),
                duration = 240,
                ease = Ease.outQuad,
                onComplete =
                    fun animation ->
                        Utils.cleanInlineStyles animation
                        overflow element ""
            )
        )

    let private fold (_: 'inst) (node: Element) =
        overflow (box node) "hidden"
        Anime.animate (node, Anim (height = 0, duration = 180, ease = Ease.inQuad))

    let collapse<'native, 'inst> : Part<'native, 'inst> =
        Hooks.both (Hooks.motion (entering unfold), Hooks.exit (departure false fold))

    let flip (parameters: LayoutOptions) : Part<'native, 'inst> =
        let converted = BrowserParams.layout parameters

        Hooks.reflow (fun _ node ->
            let root = box node
            let layout: AutoLayout ref = ref null
            let recorded = ref false

            Api.onCleanup (fun () ->
                if not (isNull layout.Value) then
                    layout.Value.revert ())

            {
                Before =
                    fun () ->
                        recorded.Value <- not (reduced ())

                        if recorded.Value then
                            let children = liveChildren root departing arriving

                            if isNull layout.Value then
                                layout.Value <- Anime.layout (elementOf node, withKey (Erased.cast converted: LayoutParams) "children" children)
                            else
                                measureChildren layout.Value children
                                leaveVisuals layout.Value converted root animated departing arriving
                                layout.Value.record () |> ignore

                            startFrom layout.Value children
                After =
                    fun () ->
                        if recorded.Value then
                            recorded.Value <- false
                            measureChildren layout.Value (liveChildren root departing arriving)
                            leaveVisuals layout.Value converted root animated departing arriving
                            layout.Value.animate () |> ignore
                            laidOut root arriving
            })

    let flipFrom (element: Element) : unit =
        match Graph.TryCurrent with
        | ValueSome _ when reduced () -> ()
        | _ -> keepBox element

    let slot () : MotionSlot =
        let created = MotionSlot ()
        Api.onCleanup created.Close
        created

    let follow (property: string) (parameters: ComposedAnimationOptions) (read: 'inst -> float) : Part<'native, 'inst> =
        let converted = BrowserParams.composed parameters

        Hooks.motion (
            Func<_, _, _, _>(fun inst node context ->
                let animatable = Anime.animatable (targetsOf node, withUnit converted property)
                let first = ref true

                Api.createEffectOn (fun () -> read inst) (fun value ->
                    animatable.set (property, value)

                    if first.Value then
                        first.Value <- false
                        completeProperty animatable property)

                owning (box node) (settle context animatable))
        )

    let followCss
        (property: Ranvier.Css.CustomProperty<'syntax>)
        (parameters: ComposedAnimationOptions)
        (read: 'inst -> Ranvier.Css.Value<'syntax>)
        : Part<'native, 'inst> =
        let converted: AnimatableProp = Erased.cast (BrowserParams.composed parameters)

        Hooks.motion (
            Func<_, _, _, _>(fun inst node context ->
                let initial = read inst
                let suffix = unitOf initial
                let name = Ranvier.Css.RuntimeApi.customPropertyName property
                let restore = captureProperty (elementOf node) name
                let settings = propertyWithUnit converted suffix
                let seed = Utils.set (targetsOf node, Anim().With(property, initial))

                let animatable =
                    Anime.animatable (targetsOf node, AnimatableParams().With(name, settings))

                let first = ref true

                Api.createEffectOn (fun () -> read inst) (fun value ->
                    requireUnit suffix value

                    let number, _ =
                        Ranvier.Css.RuntimeApi.tryNumericValue value
                        |> Option.get

                    animatable.set (name, number)

                    if first.Value then
                        first.Value <- false
                        completeProperty animatable name)

                let release () =
                    if context.ExitPending () then
                        pauseIfPlayable animatable
                    else
                        animatable.revert ()
                        seed.revert ()
                        restore ()

                owning (box node) release)
        )

    let animateOn (read: 'inst -> 'v) (play: 'inst -> Element -> 'v -> #ITickable) : Part<'native, 'inst> =
        Hooks.motion (
            Func<_, _, _, _>(fun inst node context ->
                let current: ITickable ref = ref null

                Api.createEffectOn (fun () -> read inst) (fun value ->
                    let tickable = play inst (elementOf node) value :> ITickable

                    if isNull current.Value then
                        tickable.complete ()

                    current.Value <- tickable)

                let release () =
                    if not (isNull current.Value) then
                        settle context current.Value ()

                owning (box node) release)
        )

    let draw (parameters: ComposedAnimationOptions) (read: 'inst -> float) : Part<'native, 'inst> =
        let converted: AnimatableParams = Erased.cast (BrowserParams.composed parameters)

        Hooks.motion (
            Func<_, _, _, _>(fun inst node context ->
                let initial = read inst

                let restore =
                    keepAttributes (box node) [| "pathLength"; "stroke-dasharray"; "stroke-dashoffset" |]

                let drawable = (Svg.drawable (targetsOf node, 0., initial))[0]

                let animatable =
                    Anime.animatable (Targets.object (strokeTarget drawable initial), withDefault converted "draw" (AnimatableProp ()))

                Api.createEffectOn (fun () -> read inst) (fun fraction -> animatable.set ("draw", fraction))

                let release () =
                    if context.ExitPending () then
                        pauseIfPlayable animatable
                    else
                        animatable.revert ()
                        restore ()

                release)
        )

    let morph (parameters: ComposedAnimationOptions) (read: 'inst -> string) : Part<'native, 'inst> =
        let converted: Anim = Erased.cast (BrowserParams.composed parameters)

        Hooks.motion (
            Func<_, _, _, _>(fun inst node context ->
                let element = box node
                let attribute = shapeAttribute element
                let restore = keepAttributes element [| attribute |]
                let current: JSAnimation ref = ref null
                let first = ref true

                Api.createEffectOn (fun () -> read inst) (fun shape ->
                    if first.Value then
                        first.Value <- false
                        setShape element attribute shape
                    else
                        let morphing =
                            Anime.animate (targetsOf node, withKey converted attribute (Svg.morphTo (Erased.cast (shapeLike element attribute shape))))

                        chain morphing "onComplete" (fun _ -> setShape element attribute shape)
                        current.Value <- morphing)

                let release () =
                    if context.ExitPending () then
                        if not (isNull current.Value) then
                            current.Value.pause ()
                    else
                        if not (isNull current.Value) then
                            current.Value.cancel ()

                        restore ()

                release)
        )

    let scroll (parameters: ScrollParams) (bind: 'inst -> ScrollBinding) (play: 'inst -> Element -> #ITickable) : Part<'native, 'inst> =
        let converted = BrowserParams.scroll parameters.Options

        Hooks.motion (
            Func<_, _, _, _>(fun inst node context ->
                let binding = bind inst
                let graph = Graph.Current
                let target = parameters.Target |> Option.map box |> Option.defaultValue (box node)
                let container = parameters.Container |> Option.map box |> Option.toObj
                let observer = Anime.onScroll (observing converted container target)
                let tickable = play inst (elementOf node) :> ITickable
                observer.link tickable |> ignore

                match binding.Progress with
                | ValueSome progress -> chain observer "onUpdate" (fun _ -> graph.Batch (fun () -> progress.Value <- observer.progress))
                | ValueNone -> ()

                match binding.InView with
                | ValueSome inView ->
                    chain observer "onEnter" (fun _ -> graph.Batch (fun () -> inView.Value <- true))
                    chain observer "onLeave" (fun _ -> graph.Batch (fun () -> inView.Value <- false))
                | ValueNone -> ()

                let stop = settle context tickable

                let release () =
                    stop ()
                    observer.revert ()

                release)
        )

    let drag (parameters: NativeDraggableOptions) (bind: 'inst -> DragBinding) : Part<'native, 'inst> =
        Hooks.motion (
            Func<_, _, _, _>(fun inst node context ->
                let binding = bind inst
                let graph = Graph.Current
                let draggable = Anime.draggable (targetsOf node, Erased.cast (BrowserParams.draggable parameters))

                let publish _ =
                    graph.Batch (fun () -> binding.Position.Value <- struct (draggable.x, draggable.y))

                chain draggable "onUpdate" publish
                whenPositioned draggable publish

                match binding.Slot with
                | ValueSome owned -> chain draggable "onGrab" (fun _ -> owned.Stop ())
                | ValueNone -> ()

                match binding.Dragging with
                | ValueSome dragging ->
                    chain draggable "onGrab" (fun _ -> graph.Batch (fun () -> dragging.Value <- true))
                    chain draggable "onRelease" (fun _ -> graph.Batch (fun () -> dragging.Value <- false))
                | ValueNone -> ()

                Api.createEffectOn (fun () -> binding.Position.Value) (fun (struct (x, y)) ->
                    if x <> draggable.x then
                        draggable.setX (x, true) |> ignore

                    if y <> draggable.y then
                        draggable.setY (y, true) |> ignore)

                let release () =
                    if context.ExitPending () then
                        freezeDraggable draggable
                    else
                        draggable.revert ()

                release)
        )

    let split (parameters: TextSplitterOptions) (play: 'inst -> TextSplitter -> #ITickable) : Part<'native, 'inst> =
        let converted: SplitParams = Erased.cast (BrowserParams.split parameters)

        Hooks.motion (
            Func<_, _, _, _>(fun inst node context ->
                let splitter = Anime.splitText (elementOf node, converted)
                let tickable = play inst splitter :> ITickable

                let release () =
                    if context.ExitPending () then
                        tickable.pause ()
                        freezeSplitter splitter
                    else
                        tickable.revert ()
                        splitter.revert ()

                release)
        )
#else
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
        | Reactive.Reduced -> true
        | Reactive.Full -> false

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

    // Visual boxes kept by `flipFrom` for the next FLIP pass that measures the element as a row.
    let private boxes = List<struct (Node * float * float)>()

    let private takeBox (node: Node) =
        let index = boxes.FindIndex(fun (struct (kept, _, _)) -> same kept node)

        if index < 0 then
            ValueNone
        else
            let struct (_, left, top) = boxes[index]
            boxes.RemoveAt index
            ValueSome (struct (left, top))

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
                            let created = Layout.create (unbox<HTMLElement> (box root)) services.Computed services.Scheduler (Some options) None

                            for child in options.Targets.Value do
                                match takeBox child with
                                | ValueSome (struct (left, top)) -> created.StartFrom(child, left, top)
                                | ValueNone -> ()

                            layout <- Some created
                After =
                    fun () ->
                        if recorded then
                            recorded <- false
                            layout |> Option.iter (fun current -> current.Animate() |> ignore)

                            for child in childElements root do
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
        | _ ->
            let rect = element.getBoundingClientRect()
            let node = element :> Node
            boxes.RemoveAll(fun (struct (kept, _, _)) -> same kept node) |> ignore
            boxes.Add(struct (node, rect.left, rect.top))

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

    // Keeps the inline declaration `name` and returns its restore.
    let private keepDeclaration (element: Element) (name: string) =
        let style = styleOf element
        let value = style.getPropertyValue name
        let priority = style.getPropertyPriority name

        fun () ->
            if String.IsNullOrEmpty value then style.removeProperty name |> ignore
            else style.setProperty(name, value, priority = priority)

    // Keeps the inline declaration `property` writes (transform for the transform aliases) and returns its restore.
    let private keepInline (element: Element) (property: string) =
        if transformNames.Contains property then keepDeclaration element "transform"
        else keepDeclaration element (Text.RegularExpressions.Regex.Replace(property, "([a-z0-9])([A-Z])", "$1-$2").ToLowerInvariant())

    let private withComputed (services: MotionServices) (parameters: ComposedAnimationOptions) =
        Some { parameters with Computed = parameters.Computed |> Option.orElse (Some services.Computed) }

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

    let private invariant (value: float) = value.ToString("R", Globalization.CultureInfo.InvariantCulture)

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
                let element = elementOf native
                let services = MotionHost.Current
                let name = Ranvier.Css.RuntimeApi.customPropertyName property

                let numeric (value: Ranvier.Css.Value<'syntax>) =
                    match Ranvier.Css.RuntimeApi.tryNumericValue value with
                    | Some (number, suffix) -> number, suffix
                    | None -> invalidArg "read" (name + " values need numeric token metadata.")

                let restore = keepDeclaration element name
                let _, suffix = numeric (Api.untrack (fun () -> read inst))
                let mutable current: ComposedAnimation option = None

                Api.createEffectOn (fun () -> read inst) (fun value ->
                    let number, unit = numeric value

                    if unit <> suffix then
                        invalidArg "read" (name + " changed its unit from '" + suffix + "' to '" + unit + "'.")

                    let next =
                        Composed.animate services.Scheduler (box element) [| PropertySpec.Text(name, invariant number + suffix) |]
                            (withComputed services parameters)

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
    /// On an SVG geometry element: draws the stroke from its start to the fraction <c>read</c> returns, in 0..1, animating
    /// to each new fraction with <c>parameters</c>' timing; the first fraction is drawn at once. The cleanup restores the
    /// element's stroke attributes.
    /// </summary>
    let draw (parameters: ComposedAnimationOptions) (read: 'inst -> float) : Part<'native, 'inst> =
        Hooks.motion (
            Func<_, _, _, _>(fun inst native context ->
                let element = elementOf native
                let services = MotionHost.Current
                let initial = Api.untrack (fun () -> read inst)

                // The drawable lives in its own root, so an exit keeps the stroke where it is.
                let root, last =
                    Api.createRoot (fun root ->
                        let drawable =
                            Svg.createDrawable (unbox<SVGGeometryElement> (box element)) (services.Computed.Get(element, None)) 0. initial

                        let _, last = SvgProperties.drawRange drawable 0. initial
                        root, last)

                let mutable current: ComposedAnimation option = None

                Api.createEffectOn (fun () -> read inst) (fun fraction ->
                    if fraction <> last.Value then
                        current <-
                            Some (
                                Composed.animate services.Scheduler (box last) [| PropertySpec.Scalar("value", fraction) |]
                                    (withComputed services parameters)
                            ))

                fun () ->
                    if context.ExitPending() then
                        current |> Option.iter (fun animation -> animation.Pause() |> ignore)
                    else
                        current |> Option.iter (fun animation -> animation.Cancel())
                        root.Dispose())
        )

    let private svgNamespace = "http://www.w3.org/2000/svg"

    /// <summary>
    /// On a <c>path</c>, <c>polygon</c> or <c>polyline</c>: morphs the shape to the <c>d</c> or <c>points</c> that
    /// <c>read</c> returns whenever it changes, with <c>parameters</c>' timing. The first shape is set at once, and each
    /// completed morph leaves exactly the shape read. The cleanup restores the shape the element had before the first.
    /// </summary>
    let morph (parameters: ComposedAnimationOptions) (read: 'inst -> string) : Part<'native, 'inst> =
        Hooks.motion (
            Func<_, _, _, _>(fun inst native context ->
                let element = elementOf native
                let services = MotionHost.Current
                let tag = element.tagName.ToLowerInvariant()
                let attribute = if tag = "path" then "d" else "points"
                let original = element.getAttribute attribute
                let root = Api.createRoot id
                let mutable current: ComposedAnimation option = None
                let mutable first = true

                Api.createEffectOn (fun () -> read inst) (fun shape ->
                    if first then
                        first <- false
                        element.setAttribute(attribute, shape)
                    else
                        current |> Option.iter (fun animation -> animation.Cancel())
                        let destination = (element :> Node).ownerDocument.createElementNS(svgNamespace, tag) :?> SVGGeometryElement
                        destination.setAttribute(attribute, shape)

                        let morphing =
                            Api.runWithOwner root (fun () -> SvgProperties.morph (unbox<SVGGeometryElement> (box element)) destination 0.33 None)

                        let animation =
                            Composed.animate services.Scheduler (box morphing.Progress) [| PropertySpec.Scalar("value", 1., fromValue = 0.) |]
                                (withComputed services parameters)

                        (animation :> ITickable).WhenCompleted(fun () -> element.setAttribute(attribute, shape))
                        current <- Some animation)

                fun () ->
                    if context.ExitPending() then
                        current |> Option.iter (fun animation -> animation.Pause() |> ignore)
                    else
                        current |> Option.iter (fun animation -> animation.Cancel())
                        root.Dispose()

                        if isNull original then element.removeAttribute attribute
                        else element.setAttribute(attribute, original))
        )

    /// <summary>
    /// Splits the element's static text with a Ranvier.Anime <c>TextSplitter</c> and plays <c>play</c> over the parts; the
    /// cleanup reverts the tickable, then the split. On an element whose exit follows, the split stays and stops resizing.
    /// </summary>
    /// <remarks>The element's content must be static text: a text slot or region inside it stops updating once split.</remarks>
    let split (parameters: TextSplitterOptions) (play: 'inst -> TextSplitter -> #ITickable) : Part<'native, 'inst> =
        Hooks.motion (
            Func<_, _, _, _>(fun inst native context ->
                let root = Api.createRoot id
                let splitter = Api.runWithOwner root (fun () -> Text.splitText (unbox<HTMLElement> (box native)) (Some parameters))
                let tickable = play inst splitter :> ITickable

                fun () ->
                    if context.ExitPending() then
                        tickable.Pause()
                        splitter.Freeze()
                    else
                        tickable.Revert()
                        splitter.Revert() |> ignore
                        root.Dispose())
        )
#endif
