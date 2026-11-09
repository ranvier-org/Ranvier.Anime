namespace Ranvier.Anime

open Fable.Core

/// <summary>When a tickable starts: at once, paused, or driven by a scroll observer.</summary>
[<Erase>]
type Autoplay =
    static member inline op_Implicit(play: bool) : Autoplay =
        Erased.cast play

    static member inline op_Implicit(observer: ScrollObserver) : Autoplay =
        Erased.cast observer

/// <summary>
/// Animation parameters. The emitted object literal holds exactly the arguments given.
/// </summary>
/// <remarks>
/// Transforms compose in anime's fixed order: perspective, translations, rotations, scales, skews.
/// <c>With</c> adds a property the constructor lacks, such as a CSS variable or an attribute.
/// </remarks>
type Anim
    /// <summary>Animation parameters with the given properties, timings and callbacks.</summary>
    [<ParamObject; Emit("$0")>]
    (
        ?x: Value,
        ?y: Value,
        ?z: Value,
        ?translateX: Value,
        ?translateY: Value,
        ?translateZ: Value,
        ?rotate: Value,
        ?rotateX: Value,
        ?rotateY: Value,
        ?rotateZ: Value,
        ?scale: Value,
        ?scaleX: Value,
        ?scaleY: Value,
        ?scaleZ: Value,
        ?skew: Value,
        ?skewX: Value,
        ?skewY: Value,
        ?perspective: Value,
        ?opacity: Value,
        ?width: Value,
        ?height: Value,
        ?top: Value,
        ?left: Value,
        ?color: Value,
        ?backgroundColor: Value,
        ?borderColor: Value,
        ?borderRadius: Value,
        ?borderWidth: Value,
        ?filter: Value,
        ?fontSize: Value,
        ?letterSpacing: Value,
        ?fill: Value,
        ?stroke: Value,
        ?strokeWidth: Value,
        ?strokeDashoffset: Value,
        ?draw: Value,
        ?d: Value,
        ?points: Value,
        ?id: string,
        ?duration: Value,
        ?delay: Value,
        ?ease: Ease,
        ?playbackEase: Ease,
        ?loop: Loop,
        ?loopDelay: float,
        ?alternate: bool,
        ?reversed: bool,
        ?autoplay: Autoplay,
        ?composition: Composition,
        ?playbackRate: float,
        ?frameRate: float,
        ?priority: int,
        ?persist: bool,
        ?keyframes: Keyframes,
        ?modifier: float -> float,
        ?onBegin: JSAnimation -> unit,
        ?onBeforeUpdate: JSAnimation -> unit,
        ?onUpdate: JSAnimation -> unit,
        ?onLoop: JSAnimation -> unit,
        ?onPause: JSAnimation -> unit,
        ?onComplete: JSAnimation -> unit,
        ?onRender: JSAnimation -> unit
    ) =

    /// <summary>Sets <c>property</c> to <c>value</c> on these parameters and returns them.</summary>
    [<Emit("Object.assign($0, {[$1]: $2})")>]
    member _.With(property: string, value: Value) : Anim = jsNative

/// <summary>Keyframes for an animation's <c>keyframes</c> parameter.</summary>
and [<Erase>] Keyframes =
    /// <summary>Keyframes played in sequence, each with its own properties and timing.</summary>
    static member inline steps(frames: Anim array) : Keyframes =
        Erased.cast frames

    /// <summary>Keyframes at percentages of the duration, keyed <c>"0%"</c> to <c>"100%"</c>.</summary>
    [<Emit("Object.fromEntries($0)")>]
    static member percent(frames: (string * Anim) array) : Keyframes = jsNative

/// <summary>Timer parameters.</summary>
type TimerParams
    /// <summary>Timer parameters with the given timing and callbacks.</summary>
    [<ParamObject; Emit("$0")>]
    (
        ?id: string,
        ?duration: Value,
        ?delay: Value,
        ?loop: Loop,
        ?loopDelay: float,
        ?alternate: bool,
        ?reversed: bool,
        ?autoplay: Autoplay,
        ?frameRate: float,
        ?playbackRate: float,
        ?priority: int,
        ?onBegin: Timer -> unit,
        ?onBeforeUpdate: Timer -> unit,
        ?onUpdate: Timer -> unit,
        ?onLoop: Timer -> unit,
        ?onPause: Timer -> unit,
        ?onComplete: Timer -> unit
    ) = class end

/// <summary>Timeline parameters. <c>defaults</c> supplies each parameter a child leaves unset.</summary>
type TimelineParams
    /// <summary>Timeline parameters with the given timing, child defaults and callbacks.</summary>
    [<ParamObject; Emit("$0")>]
    (
        ?id: string,
        ?defaults: Anim,
        ?duration: Value,
        ?delay: Value,
        ?loop: Loop,
        ?loopDelay: float,
        ?alternate: bool,
        ?reversed: bool,
        ?autoplay: Autoplay,
        ?frameRate: float,
        ?playbackRate: float,
        ?priority: int,
        ?playbackEase: Ease,
        ?composition: bool,
        ?onBegin: Timeline -> unit,
        ?onBeforeUpdate: Timeline -> unit,
        ?onUpdate: Timeline -> unit,
        ?onLoop: Timeline -> unit,
        ?onPause: Timeline -> unit,
        ?onComplete: Timeline -> unit,
        ?onRender: Timeline -> unit
    ) = class end

/// <summary>The parameters of one animatable property. A number alone is the property's duration.</summary>
type AnimatableProp
    /// <summary>Animatable property parameters; <c>unit</c> is appended to each value set, such as <c>"px"</c>.</summary>
    [<ParamObject; Emit("$0")>]
    (?unit: string, ?duration: float, ?ease: Ease, ?modifier: float -> float, ?composition: Composition) =
    static member inline op_Implicit(duration: float) : AnimatableProp =
        Erased.cast duration

    static member inline op_Implicit(duration: int) : AnimatableProp =
        Erased.cast duration

/// <summary>
/// Animatable parameters: the properties to make animatable, each with its own parameters, and the defaults the
/// others share.
/// </summary>
type AnimatableParams
    /// <summary>Animatable parameters with the given properties, defaults and callbacks.</summary>
    [<ParamObject; Emit("$0")>]
    (
        ?x: AnimatableProp,
        ?y: AnimatableProp,
        ?z: AnimatableProp,
        ?translateX: AnimatableProp,
        ?translateY: AnimatableProp,
        ?rotate: AnimatableProp,
        ?scale: AnimatableProp,
        ?scaleX: AnimatableProp,
        ?scaleY: AnimatableProp,
        ?opacity: AnimatableProp,
        ?width: AnimatableProp,
        ?height: AnimatableProp,
        ?strokeDashoffset: AnimatableProp,
        ?unit: string,
        ?duration: float,
        ?ease: Ease,
        ?modifier: float -> float,
        ?composition: Composition,
        ?onBegin: JSAnimation -> unit,
        ?onUpdate: JSAnimation -> unit,
        ?onComplete: JSAnimation -> unit,
        ?onRender: JSAnimation -> unit
    ) =

    /// <summary>Makes <c>property</c> animatable with <c>parameters</c> and returns these parameters.</summary>
    [<Emit("Object.assign($0, {[$1]: $2})")>]
    member _.With(property: string, parameters: AnimatableProp) : AnimatableParams = jsNative

/// <summary>Snap points: a grid increment or explicit positions.</summary>
[<Erase>]
type Snap =
    static member inline op_Implicit(increment: float) : Snap =
        Erased.cast increment

    static member inline op_Implicit(increment: int) : Snap =
        Erased.cast increment

    /// <summary>Snaps to the nearest of <c>positions</c>.</summary>
    [<Emit("[$0...]")>]
    static member points([<System.ParamArray>] positions: float array) : Snap = jsNative

/// <summary>A drag axis: enabled, disabled, or with its own mapping and snap.</summary>
[<Erase>]
type DragAxis =
    static member inline op_Implicit(enabled: bool) : DragAxis =
        Erased.cast enabled

    /// <summary>An enabled axis writing to the property <c>mapTo</c> instead of <c>x</c> or <c>y</c>.</summary>
    [<ParamObject; Emit("$0")>]
    static member options(?mapTo: string, ?modifier: float -> float, ?composition: Composition, ?snap: Snap) : DragAxis = jsNative

/// <summary>The area a draggable stays in: an element, a selector, or bounds relative to the element's origin.</summary>
[<Erase>]
type DragContainer =
    static member inline op_Implicit(selector: string) : DragContainer =
        Erased.cast selector

    static member inline op_Implicit(element: TS.Dom.Element) : DragContainer =
        Erased.cast element

    /// <summary>Bounds in pixels from the element's initial position.</summary>
    [<Emit("[$0, $1, $2, $3]")>]
    static member bounds(top: float, right: float, bottom: float, left: float) : DragContainer = jsNative

/// <summary>Draggable parameters.</summary>
type DraggableParams
    /// <summary>Draggable parameters; friction values are in 0..1, velocities in pixels per millisecond.</summary>
    [<ParamObject; Emit("$0")>]
    (
        ?trigger: Targets,
        ?container: DragContainer,
        ?x: DragAxis,
        ?y: DragAxis,
        ?modifier: float -> float,
        ?snap: Snap,
        ?containerPadding: float,
        ?containerFriction: float,
        ?releaseContainerFriction: float,
        ?dragSpeed: float,
        ?dragThreshold: float,
        ?scrollSpeed: float,
        ?scrollThreshold: float,
        ?minVelocity: float,
        ?maxVelocity: float,
        ?velocityMultiplier: float,
        ?releaseMass: float,
        ?releaseStiffness: float,
        ?releaseDamping: float,
        ?releaseEase: Ease,
        ?cursor: bool,
        ?onGrab: Draggable -> unit,
        ?onDrag: Draggable -> unit,
        ?onRelease: Draggable -> unit,
        ?onUpdate: Draggable -> unit,
        ?onSettle: Draggable -> unit,
        ?onSnap: Draggable -> unit,
        ?onResize: Draggable -> unit,
        ?onAfterResize: Draggable -> unit
    ) = class end

/// <summary>A scroll threshold: a position keyword or offset such as <c>"top"</c>, <c>"bottom-=50"</c> or a pixel count.</summary>
[<Erase>]
type Threshold =
    static member inline op_Implicit(position: string) : Threshold =
        Erased.cast position

    static member inline op_Implicit(pixels: float) : Threshold =
        Erased.cast pixels

    /// <summary>The threshold reached when the target's <c>target</c> edge meets the container's <c>container</c> edge.</summary>
    [<Emit("{target:$0,container:$1}")>]
    static member edges(target: string, container: string) : Threshold = jsNative

/// <summary>How a linked tickable follows the scroll: on enter and leave, scrubbed, smoothed or eased.</summary>
[<Erase>]
type ScrollSync =
    /// <summary>Scrubbed with the scroll position when <c>true</c>; played on enter and leave when <c>false</c>.</summary>
    static member inline op_Implicit(scrub: bool) : ScrollSync =
        Erased.cast scrub

    /// <summary>Scrubbed with smoothing in 0..1; higher values lag further behind the scroll.</summary>
    static member inline op_Implicit(smoothing: float) : ScrollSync =
        Erased.cast smoothing

    /// <summary>Scrubbed through <c>ease</c>.</summary>
    static member inline eased(ease: Ease) : ScrollSync =
        Erased.cast ease

    /// <summary>Playback methods called on enter forward, leave forward, enter backward and leave backward.</summary>
    static member inline methods(methods: string) : ScrollSync =
        Erased.cast methods

/// <summary>The scroll axis an observer follows.</summary>
[<Erase>]
type ScrollAxis =
    static member inline x: ScrollAxis = Erased.cast "x"
    static member inline y: ScrollAxis = Erased.cast "y"

/// <summary>Scroll observer parameters.</summary>
type ScrollParams
    /// <summary>Scroll observer parameters; <c>container</c> defaults to the window.</summary>
    [<ParamObject; Emit("$0")>]
    (
        ?id: string,
        ?container: Targets,
        ?target: Targets,
        ?axis: ScrollAxis,
        ?enter: Threshold,
        ?leave: Threshold,
        ?sync: ScrollSync,
        ?repeat: bool,
        ?debug: bool,
        ?onEnter: ScrollObserver -> unit,
        ?onLeave: ScrollObserver -> unit,
        ?onEnterForward: ScrollObserver -> unit,
        ?onLeaveForward: ScrollObserver -> unit,
        ?onEnterBackward: ScrollObserver -> unit,
        ?onLeaveBackward: ScrollObserver -> unit,
        ?onUpdate: ScrollObserver -> unit,
        ?onResize: ScrollObserver -> unit,
        ?onSyncComplete: ScrollObserver -> unit
    ) = class end

/// <summary>
/// Layout parameters. <c>enterFrom</c>, <c>leaveTo</c> and <c>swapAt</c> hold the properties and timing of entering,
/// leaving and swapped elements.
/// </summary>
type LayoutParams
    /// <summary>Layout parameters; <c>children</c> selects the elements measured, every descendant by default.</summary>
    [<ParamObject; Emit("$0")>]
    (
        ?id: string,
        ?children: Targets,
        ?properties: string array,
        ?duration: Value,
        ?delay: Value,
        ?ease: Ease,
        ?playbackEase: Ease,
        ?enterFrom: Anim,
        ?leaveTo: Anim,
        ?swapAt: Anim,
        ?onBegin: Timeline -> unit,
        ?onUpdate: Timeline -> unit,
        ?onComplete: Timeline -> unit
    ) = class end

/// <summary>Whether and how a text splitter splits lines, words or characters.</summary>
[<Erase>]
type SplitValue =
    static member inline op_Implicit(split: bool) : SplitValue =
        Erased.cast split

    /// <summary>Splits with <c>html</c> as each part's markup, holding <c>{value}</c> and optionally <c>{i}</c>.</summary>
    static member inline op_Implicit(html: string) : SplitValue =
        Erased.cast html

    /// <summary>
    /// Splits into elements with the class <c>class</c> (<c>false</c> for none), each wrapped in an element with the
    /// overflow <c>wrap</c> and cloned towards the side <c>clone</c> (<c>true</c> for anime's defaults).
    /// </summary>
    [<ParamObject; Emit("$0")>]
    static member template(?``class``: Switch, ?wrap: Switch, ?clone: Switch) : SplitValue = jsNative

/// <summary>Text splitter parameters.</summary>
type SplitParams
    /// <summary>Text splitter parameters; <c>accessible</c> keeps the original text for screen readers.</summary>
    [<ParamObject; Emit("$0")>]
    (?lines: SplitValue, ?words: SplitValue, ?chars: SplitValue, ?accessible: bool, ?includeSpaces: bool, ?debug: bool) = class end

/// <summary>Scramble parameters; <c>chars</c> takes named sets such as <c>"uppercase"</c> or ranges such as <c>"A-Z"</c>.</summary>
type ScrambleParams
    /// <summary>Scramble parameters; times are in milliseconds and rates per second.</summary>
    /// <remarks>
    /// <c>from</c> is a character index or <c>"left"</c>, <c>"center"</c>, <c>"right"</c>, <c>"random"</c> or
    /// <c>"auto"</c>; <c>cursor</c> is <c>true</c>, a character code or a string; <c>override</c> sets the starting text.
    /// </remarks>
    [<ParamObject; Emit("$0")>]
    (
        ?text: string,
        ?chars: string,
        ?ease: Ease,
        ?from: Value,
        ?reversed: bool,
        ?cursor: Switch,
        ?``override``: Switch,
        ?perturbation: float,
        ?seed: int,
        ?revealRate: float,
        ?settleDuration: float,
        ?settleRate: float,
        ?duration: float,
        ?revealDelay: float,
        ?delay: float,
        ?onChange: System.Action<string, float>
    ) = class end

/// <summary>WAAPI animation parameters; <c>composition</c> is a WAAPI composite operation.</summary>
type WaapiParams
    /// <summary>WAAPI animation parameters with the given properties, timing and completion callback.</summary>
    [<ParamObject; Emit("$0")>]
    (
        ?x: Value,
        ?y: Value,
        ?translateX: Value,
        ?translateY: Value,
        ?rotate: Value,
        ?scale: Value,
        ?scaleX: Value,
        ?scaleY: Value,
        ?opacity: Value,
        ?width: Value,
        ?height: Value,
        ?color: Value,
        ?backgroundColor: Value,
        ?borderRadius: Value,
        ?filter: Value,
        ?duration: Value,
        ?delay: Value,
        ?ease: Ease,
        ?loop: Loop,
        ?alternate: bool,
        ?reversed: bool,
        ?autoplay: Autoplay,
        ?playbackRate: float,
        ?composition: string,
        ?persist: bool,
        ?onComplete: WAAPIAnimation -> unit
    ) =

    /// <summary>Sets <c>property</c> to <c>value</c> on these parameters and returns them.</summary>
    [<Emit("Object.assign($0, {[$1]: $2})")>]
    member _.With(property: string, value: Value) : WaapiParams = jsNative

/// <summary>Scope parameters; <c>mediaQueries</c> names the queries read by <c>Scope.matches</c>.</summary>
type ScopeParams
    /// <summary>Scope parameters; <c>root</c> limits selector targets to its descendants.</summary>
    [<ParamObject; Emit("$0")>]
    (?root: Targets, ?defaults: Anim, ?mediaQueries: Pairs<string>) = class end

/// <summary>The members of timelines and layouts that take parameters.</summary>
[<AutoOpen>]
module TickableParams =
    type Timeline with

        /// <summary>Adds an animation of <c>targets</c> at <c>position</c>, the end of the timeline by default.</summary>
        [<Emit("$0.add($1, $2, $3)")>]
        member _.add(targets: Targets, parameters: Anim, ?position: At) : Timeline = jsNative

        /// <summary>Adds a timer at <c>position</c>, the end of the timeline by default.</summary>
        [<Emit("$0.add($1, $2)")>]
        member _.addTimer(parameters: TimerParams, ?position: At) : Timeline = jsNative

        /// <summary>Sets the properties of <c>targets</c> at <c>position</c> without tweening.</summary>
        [<Emit("$0.set($1, $2, $3)")>]
        member _.set(targets: Targets, parameters: Anim, ?position: At) : Timeline = jsNative

    type AutoLayout with

        /// <summary>Animates the root's children from the recorded layout to the current one.</summary>
        [<Emit("$0.animate($1)")>]
        member _.animate(?parameters: LayoutParams) : Timeline = jsNative

        /// <summary>Records the layout, runs <c>change</c>, then animates to the layout <c>change</c> produced.</summary>
        [<Emit("$0.update($1, $2)")>]
        member _.update(change: AutoLayout -> unit, ?parameters: LayoutParams) : Timeline = jsNative
