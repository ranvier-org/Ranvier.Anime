namespace Ranvier.Anime

// The portable surface over anime.js: the names Ranvier.Anime has on .NET, with the same signatures. The contracts are
// views of the anime object itself, so every anime tickable, draggable and text splitter already is one.

open Fable.Core

/// <summary>The DOM types of the engine: <c>Fable.Core.TS.Dom</c> under Fable.</summary>
module Dom =
    /// <summary>A DOM node.</summary>
    type Node = Fable.Core.TS.Dom.Node
    /// <summary>A DOM element.</summary>
    type Element = Fable.Core.TS.Dom.Element
    /// <summary>An HTML element.</summary>
    type HTMLElement = Fable.Core.TS.Dom.HTMLElement

/// <summary>An animation object that can restore the values it changed.</summary>
/// <remarks>Under Fable this is any JavaScript object with a <c>revert</c> method, as every anime object is.</remarks>
[<AllowNullLiteral>]
type IRevertible =
    /// <summary>Restores the values the object changed and stops it.</summary>
    [<Emit("$0.revert()")>]
    abstract Revert: unit -> unit

/// <summary>The playback controls shared by timers, timelines and animations.</summary>
/// <remarks>Under Fable this is the anime tickable itself.</remarks>
[<AllowNullLiteral>]
type ITickable =
    inherit IRevertible

    /// <summary>Whether playback reached its end.</summary>
    [<Emit("$0.completed")>]
    abstract Completed: bool

    /// <summary>Whether playback was cancelled.</summary>
    [<Emit("$0.cancelled")>]
    abstract Cancelled: bool

    /// <summary>Stops scheduled frames at the current values.</summary>
    [<Emit("$0.pause()")>]
    abstract Pause: unit -> unit

    /// <summary>Ends playback at the current values; completion callbacks do not run.</summary>
    [<Emit("$0.cancel()")>]
    abstract Cancel: unit -> unit

    /// <summary>Seeks to the end, running completion callbacks.</summary>
    [<Emit("$0.complete()")>]
    abstract Complete: unit -> unit

    /// <summary>Runs the callback once playback completes, at once when it already has; a cancel drops it.</summary>
    /// <remarks>The callback is chained onto the tickable's <c>onComplete</c>, after the one it had.</remarks>
    [<Emit("((t, f) => { if (t.completed) f(); else { const p = t.onComplete, c = s => { if (t.onComplete === c) t.onComplete = p; if (p) p(s); f(); }; t.onComplete = c; } })($0, $1)")>]
    abstract WhenCompleted: (unit -> unit) -> unit

/// <summary>A position on a timeline: absolute milliseconds, or an anime.js label or relative position expression.</summary>
[<Erase; RequireQualifiedAccess>]
type TimelinePosition =
    | At of float
    | Expression of string

module internal Js =
    [<Emit("({})")>]
    let fresh () : obj = jsNative

    [<Emit("$0[$1] = $2")>]
    let put (target: obj) (key: string) (value: obj) : unit = jsNative

    [<Emit("Object.assign($0, $1 || {})")>]
    let merge (parameters: obj) (extra: obj) : obj = jsNative

    [<Emit("[$0, $1]")>]
    let pair (name: string) (entry: obj) : obj = jsNative

    [<Emit("$0[0]")>]
    let first (pair: obj) : string = jsNative

    [<Emit("$0[1]")>]
    let second (pair: obj) : obj = jsNative

    // Runs `after` after the callback the target had under `callback`.
    [<Emit("((t, k, f) => { const previous = t[k]; t[k] = self => { if (previous) previous(self); f(self); }; })($0, $1, $2)")>]
    let chain (target: obj) (callback: string) (after: obj -> unit) : unit = jsNative

    let spec
        (name: string)
        (toValue: obj)
        (fromValue: obj option)
        (duration: float option)
        (delay: float option)
        (ease: (float -> float) option)
        (modifier: (float -> float) option)
        : obj =
        let entry = fresh ()

        match fromValue with
        | Some value -> put entry "from" value
        | None -> ()

        put entry "to" toValue

        match duration with
        | Some value -> put entry "duration" (box value)
        | None -> ()

        match delay with
        | Some value -> put entry "delay" (box value)
        | None -> ()

        match ease with
        | Some value -> put entry "ease" (box value)
        | None -> ()

        match modifier with
        | Some value -> put entry "modifier" (box value)
        | None -> ()

        pair name entry

/// <summary>One animated property: its name and its tween, one entry of the anime.js parameter object.</summary>
[<Sealed; AllowNullLiteral>]
type PropertySpec private () =
    /// <summary>A numeric property animated to <c>toValue</c>, from <c>fromValue</c> or the current value.</summary>
    static member Scalar
        (name: string, toValue: float, ?fromValue: float, ?duration: float, ?delay: float, ?ease: float -> float, ?modifier: float -> float)
        : PropertySpec =
        unbox (Js.spec name (box toValue) (fromValue |> Option.map box) duration delay ease modifier)

    /// <summary>A text property (a CSS value, a unit value or a colour) animated to <c>toValue</c>.</summary>
    static member Text
        (name: string, toValue: string, ?fromValue: string, ?duration: float, ?delay: float, ?ease: float -> float, ?modifier: float -> float)
        : PropertySpec =
        unbox (Js.spec name (box toValue) (fromValue |> Option.map box) duration delay ease modifier)

/// <summary>The default curves and captured layout properties, matching the native ones.</summary>
module internal BrowserEase =
    let identity: float -> float = fun value -> value
    let outQuad: float -> float = fun t -> 1. - (1. - t) * (1. - t)

    let inOutPower (power: float) : float -> float =
        fun t ->
            if t < 0.5 then
                (2. * t) ** power / 2.
            else
                1. - (2. - 2. * t) ** power / 2.

    let layoutProperties =
        [|
            "opacity"
            "font-size"
            "color"
            "background-color"
            "border-radius"
            "border"
            "filter"
            "clip-path"
        |]

/// <summary>Timing for an animation: duration in milliseconds, easing and value modifier.</summary>
[<NoEquality; NoComparison>]
type ComposedAnimationOptions =
    {
        /// <summary>Duration in milliseconds.</summary>
        Duration: float
        /// <summary>Easing curve over progress 0..1.</summary>
        Ease: float -> float
        /// <summary>Modifier applied to each animated number.</summary>
        Modifier: float -> float
        /// <summary>Whether playback starts on creation.</summary>
        Autoplay: bool
        /// <summary>Browser only: the unit suffix <c>Follow.property</c> and <c>Follow.draw</c> give their property unless its own entry sets one.</summary>
        Unit: string option
        /// <summary>Browser only: further anime.js parameters, merged last; null for none.</summary>
        Extra: obj
    }

    /// <summary>1000 ms, outQuad, no modifier, autoplay.</summary>
    static member Default =
        {
            Duration = 1000.
            Ease = BrowserEase.outQuad
            Modifier = BrowserEase.identity
            Autoplay = true
            Unit = None
            Extra = null
        }

/// <summary>The element a layout timing function is evaluated for, its index and the number of targets.</summary>
type LayoutContext =
    {
        Element: Dom.HTMLElement
        Index: uint
        Count: uint
    }

/// <summary>Layout timing and captured properties for a FLIP layout.</summary>
[<NoEquality; NoComparison>]
type LayoutOptions =
    {
        /// <summary>Duration in milliseconds for each element.</summary>
        Duration: LayoutContext -> float
        /// <summary>Delay in milliseconds for each element.</summary>
        Delay: LayoutContext -> float
        /// <summary>Easing curve.</summary>
        Ease: float -> float
        /// <summary>Captured CSS properties, kebab-case; the default visual set is always captured.</summary>
        Properties: string array
        /// <summary>Browser only: further anime.js layout parameters, merged last; null for none.</summary>
        Extra: obj
    }

    /// <summary>350 ms, no delay, inOut(3.5) and the default visual properties.</summary>
    static member Default =
        {
            Duration = fun _ -> 350.
            Delay = fun _ -> 0.
            Ease = BrowserEase.inOutPower 3.5
            Properties = BrowserEase.layoutProperties
            Extra = null
        }

    /// <summary>Replaces duration with unsigned whole milliseconds.</summary>
    member this.WithDuration(duration: uint) =
        { this with
            Duration = fun _ -> float duration
        }

    /// <summary>Replaces delay with unsigned whole milliseconds.</summary>
    member this.WithDelay(delay: uint) =
        { this with
            Delay = fun _ -> float delay
        }

/// <summary>Where a scroll observer's threshold sits on the container and on the target.</summary>
type ScrollThreshold =
    {
        Container: string
        Target: string
    }

    /// <summary>The container's end meets the target's start.</summary>
    static member Enter = { Container = "end"; Target = "start" }
    /// <summary>The container's start meets the target's end.</summary>
    static member Leave = { Container = "start"; Target = "end" }

/// <summary>The notifications of a scroll observer.</summary>
[<StringEnum; RequireQualifiedAccess>]
type ScrollEvent =
    | Enter
    | Leave
    | EnterForward
    | LeaveForward
    | EnterBackward
    | LeaveBackward
    | Update
    | Resize
    | SyncComplete

/// <summary>Playback commands a scroll observer issues on entering and leaving.</summary>
[<RequireQualifiedAccess>]
type ScrollCommand =
    | Play
    | Pause
    | Resume
    | Reverse
    | Alternate
    | Restart
    | Reset
    | Complete

/// <summary>How a scroll observer drives its linked animation.</summary>
[<RequireQualifiedAccess>]
type ScrollSync =
    | Disabled
    | Smooth of amount: float
    | Ease of (float -> float)
    | Methods of enter: ScrollCommand option * leave: ScrollCommand option
    | DirectionalMethods of
        enterForward: ScrollCommand option *
        leaveForward: ScrollCommand option *
        enterBackward: ScrollCommand option *
        leaveBackward: ScrollCommand option

/// <summary>Scroll observer axis, repetition, thresholds and synchronisation.</summary>
[<NoEquality; NoComparison>]
type ScrollOptions =
    {
        Horizontal: bool
        Repeat: bool
        Enter: ScrollThreshold
        Leave: ScrollThreshold
        Sync: ScrollSync
        /// <summary>Browser only: further anime.js onScroll parameters, merged last; null for none.</summary>
        Extra: obj
    }

    /// <summary>Vertical repeated observation with play on entry and pause on exit.</summary>
    static member Default =
        {
            Horizontal = false
            Repeat = true
            Enter = ScrollThreshold.Enter
            Leave = ScrollThreshold.Leave
            Sync = ScrollSync.Methods (Some ScrollCommand.Play, Some ScrollCommand.Pause)
            Extra = null
        }

/// <summary>An animation of target properties.</summary>
/// <remarks>Under Fable this is the anime.js animation itself.</remarks>
[<AllowNullLiteral>]
type ComposedAnimation =
    inherit ITickable

    /// <summary>The total duration across iterations.</summary>
    [<Emit("$0.duration")>]
    abstract Duration: float

    /// <summary>The time since the start; setting it seeks.</summary>
    [<Emit("$0.currentTime{{ = $1}}")>]
    abstract CurrentTime: float with get, set

    /// <summary>The overall progress in 0..1; setting it seeks.</summary>
    [<Emit("$0.progress{{ = $1}}")>]
    abstract Progress: float with get, set

    /// <summary>Whether scheduling is paused.</summary>
    [<Emit("$0.paused")>]
    abstract Paused: bool

    /// <summary>Plays forward.</summary>
    [<Emit("$0.play()")>]
    abstract Play: unit -> ComposedAnimation

    /// <summary>Pauses scheduling and returns this animation.</summary>
    [<Emit("$0.pause()")>]
    abstract Pause: unit -> ComposedAnimation

    /// <summary>Resumes scheduling in the current direction.</summary>
    [<Emit("$0.resume()")>]
    abstract Resume: unit -> ComposedAnimation

    /// <summary>Plays in reverse.</summary>
    [<Emit("$0.reverse()")>]
    abstract Reverse: unit -> ComposedAnimation

    /// <summary>Resets and resumes playback.</summary>
    [<Emit("$0.restart()")>]
    abstract Restart: unit -> ComposedAnimation

    /// <summary>Seeks to the time; <c>muteCallbacks</c> skips the callbacks the jump crosses.</summary>
    [<Emit("$0.seek($1, $2)")>]
    abstract Seek: time: float * ?muteCallbacks: bool -> ComposedAnimation

    /// <summary>Seeks to the end, running completion callbacks, and returns this animation.</summary>
    [<Emit("$0.complete()")>]
    abstract Complete: unit -> ComposedAnimation

    /// <summary>Seeks to the end, running completion callbacks unless <c>muteCallbacks</c>.</summary>
    [<Emit("$0.complete($1)")>]
    abstract Complete: muteCallbacks: bool -> ComposedAnimation

    /// <summary>Rewinds, restores the values from before the animation and cancels playback.</summary>
    [<Emit("$0.revert()")>]
    abstract Revert: unit -> ComposedAnimation

/// <summary>A timeline: child animations placed at positions on one clock.</summary>
/// <remarks>Under Fable this is the anime.js timeline itself.</remarks>
[<AllowNullLiteral>]
type ComposedTimeline =
    inherit ITickable

    /// <summary>Names a position, the end of the timeline by default.</summary>
    [<Emit("$0.label($1, $2)")>]
    abstract Label: name: string * ?position: TimelinePosition -> ComposedTimeline

    /// <summary>The total duration across iterations.</summary>
    [<Emit("$0.duration")>]
    abstract Duration: float

    /// <summary>The time since the start; setting it seeks.</summary>
    [<Emit("$0.currentTime{{ = $1}}")>]
    abstract CurrentTime: float with get, set

    /// <summary>Whether scheduling is paused.</summary>
    [<Emit("$0.paused")>]
    abstract Paused: bool

    /// <summary>Plays forward.</summary>
    [<Emit("$0.play()")>]
    abstract Play: unit -> ComposedTimeline

    /// <summary>Pauses scheduling and returns this timeline.</summary>
    [<Emit("$0.pause()")>]
    abstract Pause: unit -> ComposedTimeline

    /// <summary>Resumes scheduling.</summary>
    [<Emit("$0.resume()")>]
    abstract Resume: unit -> ComposedTimeline

    /// <summary>Reverses playback.</summary>
    [<Emit("$0.reverse()")>]
    abstract Reverse: unit -> ComposedTimeline

    /// <summary>Seeks to the time; <c>muteCallbacks</c> skips the callbacks the jump crosses.</summary>
    [<Emit("$0.seek($1, $2)")>]
    abstract Seek: time: float * ?muteCallbacks: bool -> ComposedTimeline

    /// <summary>Reverts the children in reverse order and cancels this timeline.</summary>
    [<Emit("$0.revert()")>]
    abstract Revert: unit -> ComposedTimeline

/// <summary>A draggable element.</summary>
/// <remarks>Under Fable this is the anime.js draggable itself.</remarks>
[<AllowNullLiteral>]
type NativeDraggable =
    inherit IRevertible

    /// <summary>The horizontal position; setting it moves the element without raising <c>OnUpdate</c>.</summary>
    [<Emit("$0{{.setX($1, true)}}.x")>]
    abstract X: float with get, set

    /// <summary>The vertical position; setting it moves the element without raising <c>OnUpdate</c>.</summary>
    [<Emit("$0{{.setY($1, true)}}.y")>]
    abstract Y: float with get, set

    /// <summary>Stops input and motion where they are, keeping the rendered values; terminal.</summary>
    [<Emit("(d => { d.stop(); d.disable(); d.$target.classList.remove('is-disabled'); d.updateTicker.pause(); d.overshootTicker.pause(); d.resizeTicker.pause(); for (const k in d.animate.animations) d.animate.animations[k].pause(); d.resizeObserver.disconnect(); })($0)")>]
    abstract Cancel: unit -> unit

/// <summary>Draggable lifecycle callbacks.</summary>
[<NoEquality; NoComparison>]
type DraggableCallbacks =
    {
        OnGrab: NativeDraggable -> unit
        OnDrag: NativeDraggable -> unit
        OnRelease: NativeDraggable -> unit
        OnUpdate: NativeDraggable -> unit
        /// <summary>Runs after the last frame of a release's motion, once the position is final.</summary>
        OnSettle: NativeDraggable -> unit
        OnSnap: NativeDraggable -> unit
        OnResize: NativeDraggable -> unit
        OnAfterResize: NativeDraggable -> unit
    }

    /// <summary>No callbacks.</summary>
    static member Default =
        {
            OnGrab = ignore
            OnDrag = ignore
            OnRelease = ignore
            OnUpdate = ignore
            OnSettle = ignore
            OnSnap = ignore
            OnResize = ignore
            OnAfterResize = ignore
        }

/// <summary>Draggable container, trigger, release physics, cursor and callbacks.</summary>
[<NoEquality; NoComparison>]
type NativeDraggableOptions =
    {
        Container: unit -> Dom.HTMLElement option
        Trigger: Dom.HTMLElement option
        Modifier: float -> float
        ReleaseMass: float
        ReleaseStiffness: float
        ReleaseDamping: float
        Cursor: bool
        CursorHover: string
        CursorGrab: string
        Callbacks: DraggableCallbacks
        /// <summary>Browser only: further anime.js draggable parameters, merged last; null for none.</summary>
        Extra: obj
    }

    /// <summary>The page as container, the element as trigger and the anime.js release defaults.</summary>
    static member Default =
        {
            Container = fun () -> None
            Trigger = None
            Modifier = BrowserEase.identity
            ReleaseMass = 1.
            ReleaseStiffness = 80.
            ReleaseDamping = 20.
            Cursor = true
            CursorHover = "grab"
            CursorGrab = "grabbing"
            Callbacks = DraggableCallbacks.Default
            Extra = null
        }

/// <summary>Overflow wrapper for split elements.</summary>
[<RequireQualifiedAccess>]
type SplitWrap =
    | None
    | Clip
    | Overflow of string

/// <summary>Clone offset direction for split elements.</summary>
[<RequireQualifiedAccess>]
type SplitClone =
    | None
    | Center
    | Left
    | Right
    | Top
    | Bottom

/// <summary>Generated span template parameters for one split granularity.</summary>
type SplitTemplateOptions =
    {
        Class: string option
        Wrap: SplitWrap
        Clone: SplitClone
    }

    /// <summary>No extra class, wrapper or clone.</summary>
    static member Default =
        {
            Class = None
            Wrap = SplitWrap.None
            Clone = SplitClone.None
        }

/// <summary>Whether and how one granularity is split.</summary>
[<RequireQualifiedAccess>]
type SplitMode =
    | Disabled
    | Enabled
    | Options of SplitTemplateOptions
    | Template of string
    | TemplateBy of (Dom.Node -> string)

/// <summary>Granularities and accessibility for a text splitter.</summary>
[<NoEquality; NoComparison>]
type TextSplitterOptions =
    {
        Words: SplitMode
        Chars: SplitMode
        Lines: SplitMode
        Accessible: bool
        IncludeSpaces: bool
        Debug: bool
        /// <summary>Browser only: further anime.js splitText parameters, merged last; null for none.</summary>
        Extra: obj
    }

    /// <summary>Words, accessible, no spaces, no debug.</summary>
    static member Default =
        {
            Words = SplitMode.Enabled
            Chars = SplitMode.Disabled
            Lines = SplitMode.Disabled
            Accessible = true
            IncludeSpaces = false
            Debug = false
            Extra = null
        }

/// <summary>An element's text split into line, word and character elements.</summary>
/// <remarks>Under Fable this is the anime.js text splitter itself.</remarks>
[<AllowNullLiteral>]
type TextSplitter =
    inherit IRevertible

    /// <summary>The line elements.</summary>
    [<Emit("$0.lines")>]
    abstract Lines: Dom.HTMLElement array

    /// <summary>The word elements.</summary>
    [<Emit("$0.words")>]
    abstract Words: Dom.HTMLElement array

    /// <summary>The character elements.</summary>
    [<Emit("$0.chars")>]
    abstract Chars: Dom.HTMLElement array

    /// <summary>Stops resize tracking, keeping the split elements.</summary>
    [<Emit("(s => { clearTimeout(s.resizeTimeout); s.resizeObserver.disconnect(); })($0)")>]
    abstract Freeze: unit -> unit

    /// <summary>Stops resize tracking and restores the original markup.</summary>
    [<Emit("$0.revert()")>]
    abstract Revert: unit -> TextSplitter

/// <summary>Conversions of the option records to anime.js parameter objects.</summary>
module internal BrowserParams =
    open Js

    let private command =
        function
        | Some ScrollCommand.Play -> "play"
        | Some ScrollCommand.Pause -> "pause"
        | Some ScrollCommand.Resume -> "resume"
        | Some ScrollCommand.Reverse -> "reverse"
        | Some ScrollCommand.Alternate -> "alternate"
        | Some ScrollCommand.Restart -> "restart"
        | Some ScrollCommand.Reset -> "reset"
        | Some ScrollCommand.Complete -> "complete"
        | None -> "none"

    let private timing (options: ComposedAnimationOptions) (autoplay: bool) : obj =
        let parameters = fresh ()
        put parameters "duration" (box options.Duration)
        put parameters "ease" (box options.Ease)

        if not (obj.ReferenceEquals (options.Modifier, BrowserEase.identity)) then
            put parameters "modifier" (box options.Modifier)

        if autoplay && not options.Autoplay then
            put parameters "autoplay" (box false)

        parameters

    /// <summary>The anime.js timing parameters of an animatable or an animatable property, with the unit.</summary>
    let composed (options: ComposedAnimationOptions) : obj =
        let parameters = timing options true

        match options.Unit with
        | Some unit -> put parameters "unit" (box unit)
        | None -> ()

        merge parameters options.Extra

    /// <summary>The anime.js animate parameters: timing, one entry per spec, then Extra. A timeline child has no autoplay.</summary>
    let animation (specs: PropertySpec array) (options: ComposedAnimationOptions) (child: bool) : obj =
        let parameters = timing options (not child)

        for spec in specs do
            put parameters (first (box spec)) (second (box spec))

        merge parameters options.Extra

    /// <summary>The anime.js createLayout parameters; the default visual properties are anime.js's own and stay unnamed.</summary>
    /// <remarks>A timing function receives a plain object with the LayoutContext fields.</remarks>
    [<Emit("Object.assign({duration: (e, i, t) => $0.Duration({Element: e, Index: i, Count: t}), delay: (e, i, t) => $0.Delay({Element: e, Index: i, Count: t}), ease: $0.Ease}, (p => p.length ? {properties: p} : 0)($0.Properties.filter(n => !$1.includes(n)).map(n => n.replace(/-./g, c => c[1].toUpperCase()))), $0.Extra)")>]
    let private layoutOf (options: LayoutOptions) (defaults: string array) : obj = jsNative

    let layout (options: LayoutOptions) : obj =
        layoutOf options BrowserEase.layoutProperties

    let private threshold (value: ScrollThreshold) =
        let result = fresh ()
        put result "container" (box value.Container)
        put result "target" (box value.Target)
        result

    /// <summary>The anime.js onScroll parameters, without container and target.</summary>
    let scroll (options: ScrollOptions) : obj =
        let sync: obj =
            match options.Sync with
            | ScrollSync.Disabled -> box false
            | ScrollSync.Smooth amount when amount >= 1. -> box true
            | ScrollSync.Smooth amount -> box amount
            | ScrollSync.Ease ease -> box ease
            | ScrollSync.Methods (enter, leave) -> box (command enter + " " + command leave)
            | ScrollSync.DirectionalMethods (enterForward, leaveForward, enterBackward, leaveBackward) ->
                box (
                    command enterForward
                    + " "
                    + command leaveForward
                    + " "
                    + command enterBackward
                    + " "
                    + command leaveBackward
                )

        let parameters = fresh ()

        if options.Horizontal then
            put parameters "axis" (box "x")

        put parameters "repeat" (box options.Repeat)
        put parameters "enter" (threshold options.Enter)
        put parameters "leave" (threshold options.Leave)
        put parameters "sync" sync
        merge parameters options.Extra

    /// <summary>The anime.js createDraggable parameters, without onSettle, which <c>Drag.create</c> raises itself.</summary>
    let draggable (options: NativeDraggableOptions) : obj =
        let callbacks = options.Callbacks
        let parameters = fresh ()

        match options.Container () with
        | Some container -> put parameters "container" (box container)
        | None -> ()

        match options.Trigger with
        | Some trigger -> put parameters "trigger" (box trigger)
        | None -> ()

        if not (obj.ReferenceEquals (options.Modifier, BrowserEase.identity)) then
            put parameters "modifier" (box options.Modifier)

        put parameters "releaseMass" (box options.ReleaseMass)
        put parameters "releaseStiffness" (box options.ReleaseStiffness)
        put parameters "releaseDamping" (box options.ReleaseDamping)

        if options.Cursor then
            let cursor = fresh ()
            put cursor "onHover" (box options.CursorHover)
            put cursor "onGrab" (box options.CursorGrab)
            put parameters "cursor" cursor
        else
            put parameters "cursor" (box false)

        put parameters "onGrab" (box callbacks.OnGrab)
        put parameters "onDrag" (box callbacks.OnDrag)
        put parameters "onRelease" (box callbacks.OnRelease)
        put parameters "onUpdate" (box callbacks.OnUpdate)
        put parameters "onSnap" (box callbacks.OnSnap)
        put parameters "onResize" (box callbacks.OnResize)
        put parameters "onAfterResize" (box callbacks.OnAfterResize)
        merge parameters options.Extra

    let private template (value: SplitTemplateOptions) =
        let result = fresh ()

        match value.Class with
        | Some name -> put result "class" (box name)
        | None -> ()

        match value.Wrap with
        | SplitWrap.None -> ()
        | SplitWrap.Clip -> put result "wrap" (box "clip")
        | SplitWrap.Overflow overflow -> put result "wrap" (box overflow)

        let clone =
            match value.Clone with
            | SplitClone.None -> null
            | SplitClone.Center -> "center"
            | SplitClone.Left -> "left"
            | SplitClone.Right -> "right"
            | SplitClone.Top -> "top"
            | SplitClone.Bottom -> "bottom"

        if not (isNull clone) then
            put result "clone" (box clone)

        result

    let private mode (value: SplitMode) : obj =
        match value with
        | SplitMode.Disabled -> box false
        | SplitMode.Enabled -> box true
        | SplitMode.Options options -> template options
        | SplitMode.Template text -> box text
        | SplitMode.TemplateBy template -> box template

    /// <summary>The anime.js splitText parameters.</summary>
    let split (options: TextSplitterOptions) : obj =
        let parameters = fresh ()
        put parameters "words" (mode options.Words)
        put parameters "chars" (mode options.Chars)
        put parameters "lines" (mode options.Lines)
        put parameters "accessible" (box options.Accessible)
        put parameters "includeSpaces" (box options.IncludeSpaces)
        put parameters "debug" (box options.Debug)
        merge parameters options.Extra

/// <summary>The timeline members that take option records.</summary>
[<AutoOpen>]
module ComposedTimelineExtensions =
    [<Emit("$0.add($1, $2, $3)")>]
    let private addChild (timeline: ComposedTimeline) (targets: obj array) (parameters: obj) (position: TimelinePosition option) : ComposedTimeline =
        jsNative

    type ComposedTimeline with

        /// <summary>Adds an animation of the targets at the position, the end of the timeline by default.</summary>
        member this.Add
            (targets: obj array, specs: PropertySpec array, ?options: ComposedAnimationOptions, ?position: TimelinePosition)
            : ComposedTimeline =
            addChild this targets (BrowserParams.animation specs (defaultArg options ComposedAnimationOptions.Default) true) position
