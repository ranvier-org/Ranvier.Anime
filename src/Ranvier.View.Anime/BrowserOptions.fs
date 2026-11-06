namespace Ranvier.Anime

#if FABLE_COMPILER
// The browser counterparts of the Ranvier.Anime option records the Motion parts take. Each carries the fields the native
// record shares with anime.js, under the native names, plus Extra: anime.js parameters merged last, for the options only
// anime.js has. The parts convert them to anime.js parameter objects.

open Fable.Core
open Fable.Core.TS.Dom

/// <summary>The default curves and captured layout properties, matching Ranvier.Anime's.</summary>
module internal BrowserEase =
    let identity: float -> float = fun value -> value
    let outQuad: float -> float = fun t -> 1. - (1. - t) * (1. - t)

    let inOutPower (power: float) : float -> float =
        fun t -> if t < 0.5 then (2. * t) ** power / 2. else 1. - (2. - 2. * t) ** power / 2.

    let layoutProperties =
        [| "opacity"; "font-size"; "color"; "background-color"; "border-radius"; "border"; "filter"; "clip-path" |]

/// <summary>Timing for a composed animation: duration in milliseconds, easing and value modifier.</summary>
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
        /// <summary>Browser only: the unit suffix <c>follow</c> and <c>draw</c> give their property unless its own entry sets one.</summary>
        Unit: string option
        /// <summary>Browser only: further anime.js parameters, merged last; null for none.</summary>
        Extra: obj
    }

    /// <summary>1000 ms, outQuad, no modifier, autoplay: the Ranvier.Anime and anime.js defaults.</summary>
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
type LayoutContext = { Element: HTMLElement; Index: uint; Count: uint }

/// <summary>Layout timing and captured properties for <c>Motion.flip</c>.</summary>
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
        /// <summary>Further anime.js layout parameters, merged last; null for none.</summary>
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
    member this.WithDuration(duration: uint) = { this with Duration = fun _ -> float duration }
    /// <summary>Replaces delay with unsigned whole milliseconds.</summary>
    member this.WithDelay(delay: uint) = { this with Delay = fun _ -> float delay }

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

/// <summary>Draggable lifecycle callbacks.</summary>
[<NoEquality; NoComparison>]
type DraggableCallbacks =
    {
        OnGrab: Draggable -> unit
        OnDrag: Draggable -> unit
        OnRelease: Draggable -> unit
        OnUpdate: Draggable -> unit
        OnSettle: Draggable -> unit
        OnSnap: Draggable -> unit
        OnResize: Draggable -> unit
        OnAfterResize: Draggable -> unit
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

/// <summary>Draggable container, trigger, release physics, cursor and callbacks for <c>Motion.drag</c>.</summary>
[<NoEquality; NoComparison>]
type NativeDraggableOptions =
    {
        Container: unit -> HTMLElement option
        Trigger: HTMLElement option
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
    static member Default = { Class = None; Wrap = SplitWrap.None; Clone = SplitClone.None }

/// <summary>Whether and how one granularity is split.</summary>
[<RequireQualifiedAccess>]
type SplitMode =
    | Disabled
    | Enabled
    | Options of SplitTemplateOptions
    | Template of string

/// <summary>Granularities and accessibility for <c>Motion.split</c>.</summary>
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

/// <summary>Conversions of the option records to anime.js parameter objects.</summary>
module internal BrowserParams =
    [<Emit("({})")>]
    let private fresh () : obj = jsNative

    [<Emit("$0[$1] = $2")>]
    let private put (target: obj) (key: string) (value: obj) : unit = jsNative

    [<Emit("Object.assign($0, $1 || {})")>]
    let private merge (parameters: obj) (extra: obj) : obj = jsNative

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

    /// <summary>The anime.js timing parameters of a composed animation, an animatable or an animatable property.</summary>
    let composed (options: ComposedAnimationOptions) : obj =
        let parameters = fresh ()
        put parameters "duration" (box options.Duration)
        put parameters "ease" (box options.Ease)

        if not (obj.ReferenceEquals (options.Modifier, BrowserEase.identity)) then
            put parameters "modifier" (box options.Modifier)

        if not options.Autoplay then
            put parameters "autoplay" (box false)

        match options.Unit with
        | Some unit -> put parameters "unit" (box unit)
        | None -> ()

        merge parameters options.Extra

    /// <summary>The anime.js createLayout parameters; the default visual properties are anime.js's own and stay unnamed.</summary>
    /// <remarks>One expression, so flip's glue stays within its bundle budget. A timing function receives a plain object with the
    /// LayoutContext fields.</remarks>
    [<Emit("Object.assign({duration: (e, i, t) => $0.Duration({Element: e, Index: i, Count: t}), delay: (e, i, t) => $0.Delay({Element: e, Index: i, Count: t}), ease: $0.Ease}, (p => p.length ? {properties: p} : 0)($0.Properties.filter(n => !$1.includes(n)).map(n => n.replace(/-./g, c => c[1].toUpperCase()))), $0.Extra)")>]
    let private layoutOf (options: LayoutOptions) (defaults: string array) : obj = jsNative

    let layout (options: LayoutOptions) : obj = layoutOf options BrowserEase.layoutProperties

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
                box (command enterForward + " " + command leaveForward + " " + command enterBackward + " " + command leaveBackward)

        let parameters = fresh ()

        if options.Horizontal then
            put parameters "axis" (box "x")

        put parameters "repeat" (box options.Repeat)
        put parameters "enter" (threshold options.Enter)
        put parameters "leave" (threshold options.Leave)
        put parameters "sync" sync
        merge parameters options.Extra

    /// <summary>The anime.js createDraggable parameters.</summary>
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
        put parameters "onSettle" (box callbacks.OnSettle)
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
#endif
