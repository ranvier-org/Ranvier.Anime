namespace Ranvier.Anime

// The motion primitives Ranvier.View.Anime's parts are written over, on anime.js.

open Fable.Core
open Ranvier.Anime.Dom
open Fable.Ranvier.Anime.TickableParams

module internal Raw =
    type Anime = Fable.Ranvier.Anime.Anime
    type Svg = Fable.Ranvier.Anime.Svg
    type Targets = Fable.Ranvier.Anime.Targets
    type Anim = Fable.Ranvier.Anime.Anim
    type Animatable = Fable.Ranvier.Anime.Animatable
    type AnimatableParams = Fable.Ranvier.Anime.AnimatableParams
    type AnimatableProp = Fable.Ranvier.Anime.AnimatableProp
    type JSAnimation = Fable.Ranvier.Anime.JSAnimation
    type AutoLayout = Fable.Ranvier.Anime.AutoLayout
    type Draggable = Fable.Ranvier.Anime.Draggable
    type ScrollParams = Fable.Ranvier.Anime.ScrollParams

    [<Emit("(a => { for (const k in a.animations) a.animations[k].pause(); })($0)")>]
    let pauseAnimatable (animatable: Animatable) : unit = jsNative

    [<Emit("Object.assign({}, $0, {[$1]: $2})")>]
    let withKey (parameters: 'p) (key: string) (value: obj) : 'p = jsNative

    [<Emit("Object.assign({[$1]: $2}, $0)")>]
    let withDefault (parameters: 'p) (key: string) (value: obj) : 'p = jsNative

    // The parameters without the shared unit, which anime would read as a property, and with the property's own entry,
    // which takes the shared unit when it sets none.
    [<Emit("(({unit, ...p}, k) => { const e = p[k]; p[k] = Object.assign({unit}, typeof e === 'number' ? {duration: e} : e); return p; })($0, $1)")>]
    let withUnit (parameters: obj) (property: string) : AnimatableParams = jsNative

    [<Emit("$0.animations[$1].complete()")>]
    let completeProperty (animatable: Animatable) (property: string) : unit = jsNative

    [<Emit("((el, f) => ({ get draw() { return f; }, set draw(v) { f = v; el.setAttribute('draw', '0 ' + v); } }))($0, $1)")>]
    let strokeTarget (drawable: Element) (initial: float) : obj = jsNative

    [<Emit("($0.tagName.toLowerCase() === 'path' ? 'd' : 'points')")>]
    let shapeAttribute (element: Element) : string = jsNative

    [<Emit("((e, a, v) => { const shape = e.ownerDocument.createElementNS('http://www.w3.org/2000/svg', e.tagName.toLowerCase()); shape.setAttribute(a, v); return shape; })($0, $1, $2)")>]
    let shapeLike (element: Element) (attribute: string) (shape: string) : Element = jsNative

    [<Emit("$0.setAttribute($1, $2)")>]
    let setShape (element: Element) (attribute: string) (shape: string) : unit = jsNative

    [<Emit("((e, names) => { const kept = names.map(n => e.getAttribute(n)); return () => names.forEach((n, i) => kept[i] == null ? e.removeAttribute(n) : e.setAttribute(n, kept[i])); })($0, $1)")>]
    let keepAttributes (element: Element) (names: string array) : unit -> unit = jsNative

    // The root's live element children, carrying the query that produced them so that a layout can repeat it once the
    // change is made.
    [<Emit("((r, a, b) => { const q = () => Array.prototype.filter.call(r.children, c => !a.has(c) && !b.has(c)); const c = q(); c.again = q; return c; })($0, $1, $2)")>]
    let liveChildren (root: Element) (leaving: obj) (entering: obj) : Element array = jsNative

    // The children to animate: the recorded query run again, or the recorded children still under the root.
    [<Emit("((c, r) => c.again ? c.again() : c.filter(e => e.parentNode === r))($0, $1)")>]
    let childrenNow (recorded: Element array) (root: Element) : Element array = jsNative

    // Takes the layout's visual properties (opacity, colours, borders, filters, clip paths, font size) that the
    // parameters do not name out of its animated and recorded properties while `owned`, and puts them back otherwise.
    // anime 4.5.0 has no public way to remove them (AutoLayout's `properties` and `recordedProperties` Sets).
    [<Emit("((l, p, owned) => { if (!l.motionVisual) { const named = new Set([...(p.properties || []), ...Object.keys(p.enterFrom || {}), ...Object.keys(p.leaveTo || {}), ...Object.keys(p.swapAt || {})]); l.motionVisual = ['opacity', 'fontSize', 'color', 'backgroundColor', 'borderRadius', 'border', 'filter', 'clipPath'].filter(n => !named.has(n)); } for (const n of l.motionVisual) { if (owned) { l.properties.delete(n); l.recordedProperties.delete(n); } else { l.properties.add(n); l.recordedProperties.add(n); } } })($0, $1, $2)")>]
    let leaveVisuals (layout: AutoLayout) (parameters: obj) (owned: bool) : unit = jsNative

    [<Emit("$0.children = $1")>]
    let measureChildren (layout: AutoLayout) (children: Element array) : unit = jsNative

    // Moves the recorded box of each child carrying a kept box to that box, and removes the kept box.
    [<Emit("((l, c) => { for (const e of c) { const r = e.motionFrom; if (r) { delete e.motionFrom; const p = l.oldState.getNode(e).properties; p.x += r.left - p.left; p.y += r.top - p.top; p.left = r.left; p.top = r.top; } } })($0, $1)")>]
    let startFrom (layout: AutoLayout) (children: Element array) : unit = jsNative

    [<Emit("Object.assign({}, $0, $1 ? {container: $1} : {}, {target: $2})")>]
    let observing (parameters: obj) (container: obj) (target: Element) : ScrollParams = jsNative

    // Runs `settled` once per grab, after the last frame of the release's motion: when a position animation completes
    // outside a grab and a programmatic move, and every enabled axis has completed.
    [<Emit("((d, f) => { let armed = false; const g = d.onGrab; d.onGrab = s => { armed = true; g(s); }; const axes = [d.xProp, d.yProp].filter((k, i) => !d.disabled[i] && d.animate.animations[k]); for (const k of axes) { const a = d.animate.animations[k], p = a.onComplete; a.onComplete = s => { if (p) p(s); if (armed && !d.manual && !d.grabbed && axes.every(n => d.animate.animations[n].completed)) { armed = false; f(d); } }; } })($0, $1)")>]
    let whenSettled (draggable: Draggable) (settled: NativeDraggable -> unit) : unit = jsNative

/// <summary>
/// One property of one element that follows a value. The first <c>To</c> is applied at once; later ones animate from
/// wherever the property is.
/// </summary>
type Follower<'v> =
    /// <summary>Moves the property to the value: at once the first time, animated afterwards.</summary>
    abstract To: 'v -> unit
    /// <summary>Stops the running animation at its current value.</summary>
    abstract Pause: unit -> unit
    /// <summary>Cancels the animation and restores the inline declaration from before the first value.</summary>
    abstract Revert: unit -> unit

/// <summary>Followers: properties that animate to each value given to them.</summary>
[<RequireQualifiedAccess>]
module Follow =
    /// <summary>A follower of a numeric property, transform component or CSS property of the element.</summary>
    let property (element: Element) (property: string) (options: ComposedAnimationOptions) : Follower<float> =
        let animatable =
            Raw.Anime.animatable (Raw.Targets.op_Implicit element, Raw.withUnit (BrowserParams.composed options) property)

        let first = ref true
        let reverted = ref false

        { new Follower<float> with
            member _.To value =
                if not reverted.Value then
                    animatable.set (property, value)

                    if first.Value then
                        first.Value <- false
                        Raw.completeProperty animatable property

            member _.Pause() =
                Raw.pauseAnimatable animatable

            member _.Revert() =
                if not reverted.Value then
                    reverted.Value <- true
                    animatable.revert ()
        }

    /// <summary>
    /// A follower of a typed numeric custom property. <c>initial</c> fixes the unit suffix every value keeps; it is not
    /// written.
    /// </summary>
    /// <remarks>The first value replaces the inline declaration, so a declaration that is an expression is not animated from.</remarks>
    /// <exception cref="T:System.ArgumentException">A value lacks numeric token metadata or changes the unit suffix.</exception>
    let css
        (element: Element)
        (property: Ranvier.Css.CustomProperty<'s>)
        (options: ComposedAnimationOptions)
        (initial: Ranvier.Css.Value<'s>)
        : Follower<Ranvier.Css.Value<'s>> =
        let suffix = Fable.Ranvier.Anime.CssAnimationRuntime.unitOf initial
        let name = Ranvier.Css.RuntimeApi.customPropertyName property
        let restore = Fable.Ranvier.Anime.CssAnimationRuntime.capture element name

        let settings =
            Fable.Ranvier.Anime.CssAnimationRuntime.withUnit (unbox (BrowserParams.composed options)) suffix

        let targets = Raw.Targets.op_Implicit element
        let seed: Raw.JSAnimation ref = ref null
        let animatable: Raw.Animatable ref = ref null
        let reverted = ref false

        { new Follower<Ranvier.Css.Value<'s>> with
            member _.To value =
                if not reverted.Value then
                    Fable.Ranvier.Anime.CssAnimationRuntime.requireUnit suffix value

                    let number, _ =
                        Ranvier.Css.RuntimeApi.tryNumericValue value
                        |> Option.get

                    if isNull animatable.Value then
                        let rendered =
                            Fable.Ranvier.Anime.Value.op_Implicit (Ranvier.Css.RuntimeApi.renderValue value)

                        seed.Value <- Fable.Ranvier.Anime.Utils.set (targets, Raw.Anim().With(name, rendered))
                        animatable.Value <- Raw.Anime.animatable (targets, Raw.AnimatableParams().With(name, settings))
                        animatable.Value.set (name, number)
                        Raw.completeProperty animatable.Value name
                    else
                        animatable.Value.set (name, number)

            member _.Pause() =
                if not (isNull animatable.Value) then
                    Raw.pauseAnimatable animatable.Value

            member _.Revert() =
                if not reverted.Value then
                    reverted.Value <- true

                    if not (isNull animatable.Value) then
                        animatable.Value.revert ()
                        seed.Value.revert ()

                    restore ()
        }

    /// <summary>
    /// A follower of the drawn fraction, 0..1, of an SVG geometry element's stroke. <c>initial</c> is drawn at creation and
    /// every <c>To</c> animates.
    /// </summary>
    let draw (element: Element) (options: ComposedAnimationOptions) (initial: float) : Follower<float> =
        let restore =
            Raw.keepAttributes element [| "pathLength"; "stroke-dasharray"; "stroke-dashoffset" |]

        let drawable = (Raw.Svg.drawable (Raw.Targets.op_Implicit element, 0., initial))[0]

        let parameters: Raw.AnimatableParams =
            Raw.withDefault (unbox (BrowserParams.composed options)) "draw" (Raw.AnimatableProp ())

        let animatable =
            Raw.Anime.animatable (Raw.Targets.object (Raw.strokeTarget drawable initial), parameters)

        let reverted = ref false

        { new Follower<float> with
            member _.To fraction =
                if not reverted.Value then
                    animatable.set ("draw", fraction)

            member _.Pause() =
                Raw.pauseAnimatable animatable

            member _.Revert() =
                if not reverted.Value then
                    reverted.Value <- true
                    animatable.revert ()
                    restore ()
        }

    /// <summary>A follower of the shape of an SVG path (<c>d</c>) or polygon (<c>points</c>): each later shape is morphed to.</summary>
    let morph (element: Element) (options: ComposedAnimationOptions) : Follower<string> =
        let converted: Raw.Anim = unbox (BrowserParams.animation [||] options false)
        let attribute = Raw.shapeAttribute element
        let restore = Raw.keepAttributes element [| attribute |]
        let current: Raw.JSAnimation ref = ref null
        let first = ref true
        let reverted = ref false

        { new Follower<string> with
            member _.To shape =
                if reverted.Value then
                    ()
                elif first.Value then
                    first.Value <- false
                    Raw.setShape element attribute shape
                else
                    let target =
                        Raw.Svg.morphTo (Raw.Targets.op_Implicit (Raw.shapeLike element attribute shape))

                    let morphing =
                        Raw.Anime.animate (Raw.Targets.op_Implicit element, Raw.withKey converted attribute target)

                    Js.chain morphing "onComplete" (fun _ -> Raw.setShape element attribute shape)
                    current.Value <- morphing

            member _.Pause() =
                if not (isNull current.Value) then
                    current.Value.pause ()

            member _.Revert() =
                if not reverted.Value then
                    reverted.Value <- true

                    if not (isNull current.Value) then
                        current.Value.cancel ()

                    restore ()
        }

/// <summary>An identity set of nodes.</summary>
/// <remarks>Under Fable this is a JavaScript <c>Set</c>.</remarks>
[<Global("Set")>]
type NodeSet
    /// <summary>An empty set.</summary>
    [<Emit("new Set()")>]
    () =
    /// <summary>The number of nodes in the set.</summary>
    [<Emit("$0.size")>]
    member _.Count: int = jsNative

    /// <summary>Adds the node; a node already in the set stays once.</summary>
    [<Emit("$0.add($1)")>]
    member _.Add(node: Node) : unit = jsNative

    /// <summary>Removes the node.</summary>
    [<Emit("$0.delete($1)")>]
    member _.Remove(node: Node) : unit = jsNative

    /// <summary>Whether the node is in the set.</summary>
    [<Emit("$0.has($1)")>]
    member _.Has(node: Node) : bool = jsNative

    /// <summary>Whether a node of the set is <c>root</c> or one of its descendants.</summary>
    [<Emit("((s, r) => { for (const n of s) if (r.contains(n)) return true; return false; })($0, $1)")>]
    member _.AnyUnder(root: Node) : bool = jsNative

/// <summary>A FLIP layout over the live element children of a root, reused across passes.</summary>
type FlipLayout =
    /// <summary>
    /// Measures <c>children</c> before the change. <c>owned</c>: an element under the root is animated by something else,
    /// so the default visual properties are left alone for this pass.
    /// </summary>
    abstract Record: children: Element array * owned: bool -> unit
    /// <summary>Gives <c>owned</c> as it stands once the change is made, for the <c>Animate</c> that follows.</summary>
    abstract SetOwned: owned: bool -> unit
    /// <summary>Animates the children last recorded to their new boxes.</summary>
    /// <remarks>
    /// Children from <c>Flip.children</c> are queried again, so an element that began leaving during the change stays out
    /// and a new plain child is measured.
    /// </remarks>
    abstract Animate: unit -> unit
    /// <summary>Stops the layout's animations and restores the styles it changed.</summary>
    abstract Revert: unit -> unit

/// <summary>FLIP layouts on anime.js's <c>createLayout</c>.</summary>
[<RequireQualifiedAccess>]
module Flip =
    /// <summary>A layout over <c>root</c>; nothing is measured until the first <c>Record</c>.</summary>
    let create (root: Element) (options: LayoutOptions) : FlipLayout =
        let converted = BrowserParams.layout options
        let layout: Raw.AutoLayout ref = ref null
        let recorded: Element array ref = ref [||]
        let visualsOwned = ref false

        { new FlipLayout with
            member _.Record(children, owned) =
                recorded.Value <- children
                visualsOwned.Value <- owned

                if isNull layout.Value then
                    layout.Value <- Raw.Anime.layout (root, unbox (Raw.withKey converted "children" children))
                else
                    Raw.measureChildren layout.Value children
                    Raw.leaveVisuals layout.Value converted owned
                    layout.Value.record () |> ignore

                Raw.startFrom layout.Value children

            member _.SetOwned owned =
                visualsOwned.Value <- owned

            member _.Animate() =
                if not (isNull layout.Value) then
                    Raw.measureChildren layout.Value (Raw.childrenNow recorded.Value root)
                    Raw.leaveVisuals layout.Value converted visualsOwned.Value
                    layout.Value.animate () |> ignore

            member _.Revert() =
                if not (isNull layout.Value) then
                    layout.Value.revert ()
                    layout.Value <- null
        }

    /// <summary>Keeps the element's current visual box; the next <c>Record</c> that measures it starts it from there.</summary>
    [<Emit("$0.motionFrom = $0.getBoundingClientRect()")>]
    let keepBox (element: Element) : unit = jsNative

    /// <summary>The root's element children, without those in either set.</summary>
    let children (root: Element) (leaving: NodeSet) (entering: NodeSet) : Element array =
        Raw.liveChildren root leaving entering

/// <summary>Scroll observers on anime.js's <c>onScroll</c>.</summary>
[<RequireQualifiedAccess>]
module Scroll =
    /// <summary>
    /// Observes <c>target</c> in <c>container</c> (<c>None</c>: the window), driving <c>linked</c>. <c>onEvent</c> receives
    /// each notification with the observer's progress.
    /// </summary>
    /// <remarks>Reverting the observer leaves <c>linked</c> as it is.</remarks>
    let observe
        (container: HTMLElement option)
        (target: Element)
        (options: ScrollOptions)
        (linked: ITickable)
        (onEvent: ScrollEvent -> float -> unit)
        : IRevertible =
        let scroller =
            match container with
            | Some element -> box element
            | None -> null

        let observer =
            Fable.Ranvier.Anime.Anime.onScroll (Raw.observing (BrowserParams.scroll options) scroller target)

        observer.link (unbox linked) |> ignore

        let raise (callback: string) (event: ScrollEvent) =
            Js.chain observer callback (fun _ -> onEvent event observer.progress)

        raise "onEnter" ScrollEvent.Enter
        raise "onLeave" ScrollEvent.Leave
        raise "onEnterForward" ScrollEvent.EnterForward
        raise "onLeaveForward" ScrollEvent.LeaveForward
        raise "onEnterBackward" ScrollEvent.EnterBackward
        raise "onLeaveBackward" ScrollEvent.LeaveBackward
        raise "onUpdate" ScrollEvent.Update
        raise "onResize" ScrollEvent.Resize
        raise "onSyncComplete" ScrollEvent.SyncComplete
        observer :> IRevertible

/// <summary>Draggables on anime.js's <c>createDraggable</c>.</summary>
[<RequireQualifiedAccess>]
module Drag =
    /// <summary>Makes the element draggable.</summary>
    /// <remarks><c>OnSettle</c> runs after the last frame of a release's motion, once the position is final.</remarks>
    let create (element: Element) (options: NativeDraggableOptions) : NativeDraggable =
        let draggable =
            Fable.Ranvier.Anime.Anime.draggable (Raw.Targets.op_Implicit element, unbox (BrowserParams.draggable options))

        Raw.whenSettled draggable options.Callbacks.OnSettle
        draggable :> NativeDraggable

/// <summary>Text splitting on anime.js's <c>splitText</c>.</summary>
[<RequireQualifiedAccess>]
module Text =
    /// <summary>Splits the element's text into line, word and character elements.</summary>
    let split (element: Element) (options: TextSplitterOptions) : TextSplitter =
        Fable.Ranvier.Anime.Anime.splitText (element, unbox (BrowserParams.split options)) :> TextSplitter

/// <summary>The DOM operations the motion parts need.</summary>
[<RequireQualifiedAccess>]
module DomKit =
    /// <summary>Pins an HTML element absolute at its current box; other elements are left alone.</summary>
    [<Emit("(e => { if (typeof e.offsetTop !== 'number') return; const top = e.offsetTop, left = e.offsetLeft, width = e.offsetWidth, height = e.offsetHeight, s = e.style; s.position = 'absolute'; s.top = top + 'px'; s.left = left + 'px'; s.width = width + 'px'; s.height = height + 'px'; s.margin = '0'; s.boxSizing = 'border-box'; })($0)")>]
    let pinOutOfFlow (element: Element) : unit = jsNative

    /// <summary>The height of the element's content, overflow included.</summary>
    [<Emit("$0.scrollHeight")>]
    let scrollHeight (element: Element) : float = jsNative

    /// <summary>Sets an inline style declaration; the name is kebab-case.</summary>
    [<Emit("$0.style.setProperty($1, $2)")>]
    let setStyle (element: Element) (name: string) (value: string) : unit = jsNative

    /// <summary>Removes an inline style declaration; the name is kebab-case.</summary>
    [<Emit("$0.style.removeProperty($1)")>]
    let removeStyle (element: Element) (name: string) : unit = jsNative
