namespace Ranvier.Anime

open Fable.Core

/// <summary>An anime object that restores the state its targets had before it was created.</summary>
[<AllowNullLiteral>]
type IRevertible =
    /// <summary>Stops the object and restores the inline styles and attributes it changed.</summary>
    abstract revert: unit -> unit

/// <summary>A timed anime object: a timer, an animation or a timeline.</summary>
/// <remarks>Times are in milliseconds unless <c>Engine.timeUnit</c> is <c>"s"</c>.</remarks>
[<AllowNullLiteral>]
type ITickable =
    inherit IRevertible
    /// <summary>The total duration across iterations; infinite for an endless loop.</summary>
    abstract duration: float
    abstract iterationDuration: float
    abstract iterationCount: float
    /// <summary>The time since the start; setting it seeks.</summary>
    abstract currentTime: float with get, set
    abstract iterationCurrentTime: float with get, set
    /// <summary>The overall progress in 0..1; setting it seeks.</summary>
    abstract progress: float with get, set
    abstract iterationProgress: float with get, set
    abstract currentIteration: int with get, set
    abstract reversed: bool with get, set
    abstract cancelled: bool with get, set
    /// <summary>The playback rate; 1 is normal speed.</summary>
    abstract speed: float with get, set
    abstract fps: float with get, set
    abstract paused: bool
    abstract began: bool
    abstract completed: bool
    abstract backwards: bool
    abstract play: unit -> unit
    abstract pause: unit -> unit
    abstract resume: unit -> unit
    abstract restart: unit -> unit
    abstract reverse: unit -> unit
    abstract alternate: unit -> unit
    /// <summary>Resets the time and state to the start, keeping the tickable paused or playing.</summary>
    abstract reset: unit -> unit
    /// <summary>Moves to <c>time</c>; <c>muteCallbacks</c> skips the callbacks the jump crosses.</summary>
    abstract seek: time: float * ?muteCallbacks: bool -> unit
    /// <summary>Jumps to the end and fires the completion callbacks unless <c>muteCallbacks</c>.</summary>
    abstract complete: ?muteCallbacks: bool -> unit
    /// <summary>Stops at the current values and removes the tickable from the engine.</summary>
    abstract cancel: unit -> unit
    /// <summary>Scales the duration to <c>duration</c>, keeping the relative timing of children and tweens.</summary>
    abstract stretch: duration: float -> unit

/// <summary>A timer: callbacks on anime's clock.</summary>
[<AllowNullLiteral>]
type Timer =
    inherit ITickable
    abstract onBegin: (Timer -> unit) with get, set
    abstract onBeforeUpdate: (Timer -> unit) with get, set
    abstract onUpdate: (Timer -> unit) with get, set
    abstract onLoop: (Timer -> unit) with get, set
    abstract onPause: (Timer -> unit) with get, set
    abstract onComplete: (Timer -> unit) with get, set

/// <summary>An animation of target properties.</summary>
[<AllowNullLiteral>]
type JSAnimation =
    inherit ITickable
    /// <summary>Re-reads function-based values and the targets' current values.</summary>
    abstract refresh: unit -> unit
    abstract onBegin: (JSAnimation -> unit) with get, set
    abstract onBeforeUpdate: (JSAnimation -> unit) with get, set
    abstract onUpdate: (JSAnimation -> unit) with get, set
    abstract onLoop: (JSAnimation -> unit) with get, set
    abstract onPause: (JSAnimation -> unit) with get, set
    abstract onComplete: (JSAnimation -> unit) with get, set
    abstract onRender: (JSAnimation -> unit) with get, set

/// <summary>A WAAPI-backed animation, created by <c>Waapi.animate</c>.</summary>
[<AllowNullLiteral>]
type WAAPIAnimation =
    inherit IRevertible
    abstract duration: float
    abstract currentTime: float with get, set
    abstract progress: float with get, set
    abstract speed: float with get, set
    abstract completed: bool
    abstract paused: bool
    abstract reversed: bool
    abstract play: unit -> unit
    abstract pause: unit -> unit
    abstract resume: unit -> unit
    abstract restart: unit -> unit
    abstract reverse: unit -> unit
    abstract alternate: unit -> unit
    abstract seek: time: float * ?muteCallbacks: bool -> unit
    abstract complete: unit -> unit
    abstract cancel: unit -> unit
    /// <summary>Writes the current animated values to the targets' inline styles.</summary>
    abstract commitStyles: unit -> unit
    abstract onComplete: (WAAPIAnimation -> unit) with get, set

/// <summary>A timeline: children placed at positions on one clock.</summary>
[<AllowNullLiteral>]
type Timeline =
    inherit ITickable
    /// <summary>Adds a callback at <c>position</c>, the end of the timeline by default.</summary>
    abstract call: callback: (Timer -> unit) * ?position: At -> Timeline
    /// <summary>Names <c>position</c>, the end of the timeline by default, as the label <c>name</c>.</summary>
    abstract label: name: string * ?position: At -> Timeline
    /// <summary>Synchronises <c>tickable</c>'s playback with the timeline from <c>position</c>.</summary>
    abstract sync: tickable: ITickable * ?position: At -> Timeline
    /// <summary>Removes the children animating <c>targets</c>, or only their <c>property</c> tweens.</summary>
    abstract remove: targets: Targets * ?property: string -> Timeline
    abstract refresh: unit -> unit
    abstract onBegin: (Timeline -> unit) with get, set
    abstract onBeforeUpdate: (Timeline -> unit) with get, set
    abstract onUpdate: (Timeline -> unit) with get, set
    abstract onLoop: (Timeline -> unit) with get, set
    abstract onPause: (Timeline -> unit) with get, set
    abstract onComplete: (Timeline -> unit) with get, set
    abstract onRender: (Timeline -> unit) with get, set

/// <summary>Properties that animate to each value set on them, from wherever they currently are.</summary>
[<AllowNullLiteral>]
type Animatable =
    inherit IRevertible

    /// <summary>
    /// Animates <c>property</c> to <c>value</c> over <c>duration</c> with <c>ease</c>, both defaulting to the
    /// property's parameters.
    /// </summary>
    [<Emit("$0[$1]($2, $3, $4)")>]
    abstract set: property: string * value: float * ?duration: float * ?ease: Ease -> unit

    /// <summary>Animates the multi-value <c>property</c> to <c>values</c>, such as an RGB colour's channels.</summary>
    [<Emit("$0[$1](Array.from($2), $3, $4)")>]
    abstract setMany: property: string * values: float array * ?duration: float * ?ease: Ease -> unit

    /// <summary>The current value of <c>property</c>.</summary>
    [<Emit("$0[$1]()")>]
    abstract get: property: string -> float

/// <summary>A draggable element.</summary>
[<AllowNullLiteral>]
type Draggable =
    inherit IRevertible
    /// <summary>The horizontal position; setting it moves the element.</summary>
    abstract x: float with get, set
    abstract y: float with get, set
    /// <summary>The horizontal position as a fraction of the container's draggable range.</summary>
    abstract progressX: float with get, set
    abstract progressY: float with get, set
    abstract velocity: float
    abstract angle: float
    abstract grabbed: bool
    abstract dragged: bool
    abstract released: bool
    abstract enabled: bool
    /// <summary>Moves to <c>x</c>; <c>muteUpdateCallback</c> skips <c>onUpdate</c>.</summary>
    abstract setX: x: float * ?muteUpdateCallback: bool -> Draggable
    abstract setY: y: float * ?muteUpdateCallback: bool -> Draggable
    abstract enable: unit -> Draggable
    abstract disable: unit -> Draggable
    /// <summary>Stops the release animation where it is.</summary>
    abstract stop: unit -> Draggable
    /// <summary>Moves back to the initial position.</summary>
    abstract reset: unit -> Draggable
    /// <summary>Re-measures the container and the element.</summary>
    abstract refresh: unit -> unit
    /// <summary>Scrolls the container until the element is in view, leaving <c>gap</c> pixels around it.</summary>
    abstract scrollInView: ?duration: float * ?gap: float * ?ease: Ease -> Draggable
    /// <summary>Moves the element into the visible part of the container.</summary>
    abstract animateInView: ?duration: float * ?gap: float * ?ease: Ease -> Draggable
    abstract onGrab: (Draggable -> unit) with get, set
    abstract onDrag: (Draggable -> unit) with get, set
    abstract onRelease: (Draggable -> unit) with get, set
    abstract onUpdate: (Draggable -> unit) with get, set
    abstract onSettle: (Draggable -> unit) with get, set
    abstract onSnap: (Draggable -> unit) with get, set
    abstract onResize: (Draggable -> unit) with get, set
    abstract onAfterResize: (Draggable -> unit) with get, set

/// <summary>A scroll observer, linking a tickable's playback to a target's scroll position.</summary>
[<AllowNullLiteral>]
type ScrollObserver =
    inherit IRevertible
    /// <summary>The progress between the enter and leave thresholds, in 0..1.</summary>
    abstract progress: float
    abstract isInView: bool
    abstract velocity: float
    abstract backward: bool
    /// <summary>The container's scroll offset on the observed axis.</summary>
    abstract scroll: float
    abstract began: bool
    abstract completed: bool
    /// <summary>Links <c>tickable</c> to the observer, replacing any linked before.</summary>
    abstract link: tickable: ITickable -> ScrollObserver
    /// <summary>Re-measures the thresholds.</summary>
    abstract refresh: unit -> ScrollObserver
    abstract onEnter: (ScrollObserver -> unit) with get, set
    abstract onLeave: (ScrollObserver -> unit) with get, set
    abstract onEnterForward: (ScrollObserver -> unit) with get, set
    abstract onLeaveForward: (ScrollObserver -> unit) with get, set
    abstract onEnterBackward: (ScrollObserver -> unit) with get, set
    abstract onLeaveBackward: (ScrollObserver -> unit) with get, set
    abstract onUpdate: (ScrollObserver -> unit) with get, set
    abstract onResize: (ScrollObserver -> unit) with get, set
    abstract onSyncComplete: (ScrollObserver -> unit) with get, set

/// <summary>An element's text split into line, word and character elements.</summary>
[<AllowNullLiteral>]
type TextSplitter =
    inherit IRevertible
    abstract lines: TS.Dom.Element array
    abstract words: TS.Dom.Element array
    abstract chars: TS.Dom.Element array

    /// <summary>
    /// Runs <c>effect</c> now and after each re-split (a resize that re-flows lines), reverting the tickable it
    /// returned before each re-run.
    /// </summary>
    abstract addEffect: effect: (TextSplitter -> ITickable) -> TextSplitter

    /// <summary>Splits the text again, reading the current markup when <c>clearCache</c>.</summary>
    abstract split: ?clearCache: bool -> TextSplitter
    abstract refresh: unit -> unit

/// <summary>Records the layout of a root's children and animates them from the recorded layout to the current one.</summary>
[<AllowNullLiteral>]
type AutoLayout =
    inherit IRevertible
    abstract root: TS.Dom.Element
    /// <summary>The timeline of the last <c>animate</c>.</summary>
    abstract timeline: Timeline
    /// <summary>Measures the current layout as the start of the next <c>animate</c>.</summary>
    abstract record: unit -> AutoLayout

/// <summary>A scope: a root element, defaults and media queries shared by the anime objects created in it.</summary>
[<AllowNullLiteral>]
type Scope =
    inherit IRevertible

    /// <summary>
    /// Runs <c>constructor</c> in the scope now and on each media-query change. The anime objects it creates are reverted
    /// before each re-run and with the scope.
    /// </summary>
    abstract add: constructor: (Scope -> unit) -> Scope

    /// <summary>Runs <c>body</c> with the scope as anime's current scope and returns its result.</summary>
    abstract execute: body: (Scope -> 'T) -> 'T

    /// <summary>Whether the media query registered under <c>name</c> matches.</summary>
    [<Emit("$0.matches[$1]")>]
    abstract matches: name: string -> bool

    /// <summary>Reverts and re-runs every constructor.</summary>
    abstract refresh: unit -> Scope

/// <summary>The motion path functions of an SVG path: translations and rotation along it.</summary>
[<AllowNullLiteral>]
type MotionPath =
    abstract translateX: Value
    abstract translateY: Value
    abstract rotate: Value
