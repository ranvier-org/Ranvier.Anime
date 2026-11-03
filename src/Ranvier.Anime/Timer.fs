namespace Ranvier.Anime

open System
open Ranvier
open Ranvier.Dom

/// <summary>Additional iterations after the initial playback, or an indefinite repeat sequence.</summary>
[<RequireQualifiedAccess; Diagnostics.DebuggerDisplay("{ToString(),nq}")>]
type PlaybackRepeat =
    /// <summary>Runs this many additional iterations; zero runs once.</summary>
    | Count of uint
    | Forever
    /// <summary>Invariant diagnostic text for the selected repeat policy.</summary>
    override this.ToString() =
        match this with
        | PlaybackRepeat.Count count -> "Count " + count.ToString(Globalization.CultureInfo.InvariantCulture) + "u"
        | PlaybackRepeat.Forever -> "Forever"

/// <summary>Shared playback geometry and clock settings. Delays and loop gaps use signed milliseconds.</summary>
type PlaybackOptions =
    { Repeat: PlaybackRepeat
      Delay: float
      LoopDelay: float
      Reversed: bool
      Alternate: bool
      Speed: float
      FrameRate: float }
    /// <summary>One forward iteration, zero delays, unit speed and a 240 FPS ceiling.</summary>
    static member Default =
        { Repeat = PlaybackRepeat.Count 0u; Delay = 0.; LoopDelay = 0.; Reversed = false
          Alternate = false; Speed = 1.; FrameRate = 240. }
    /// <summary>An invariant one-line diagnostic of the clock options.</summary>
    override this.ToString() =
        let culture = Globalization.CultureInfo.InvariantCulture
        String.Concat [| "PlaybackOptions(Repeat="; this.Repeat.ToString(); "; Delay="; this.Delay.ToString("R", culture)
                         "; LoopDelay="; this.LoopDelay.ToString("R", culture); "; Reversed="; this.Reversed.ToString()
                         "; Alternate="; this.Alternate.ToString(); "; Speed="; this.Speed.ToString("R", culture)
                         "; FrameRate="; this.FrameRate.ToString("R", culture); ")" |]

/// <summary>Optional scope clock overrides; None inherits, while false and zero remain explicit values.</summary>
type PlaybackDefaults =
    { Repeat: PlaybackRepeat option
      Delay: float option
      LoopDelay: float option
      Reversed: bool option
      Alternate: bool option
      Speed: float option
      FrameRate: float option }
    /// <summary>Inherits every clock setting.</summary>
    static member Empty =
        { Repeat=None; Delay=None; LoopDelay=None; Reversed=None; Alternate=None; Speed=None; FrameRate=None }
    /// <summary>Applies present fields without normalization; the playback controller validates and normalizes its settings.</summary>
    member this.Apply(fallback: PlaybackOptions) : PlaybackOptions =
        { Repeat=defaultArg this.Repeat fallback.Repeat
          Delay=defaultArg this.Delay fallback.Delay
          LoopDelay=defaultArg this.LoopDelay fallback.LoopDelay
          Reversed=defaultArg this.Reversed fallback.Reversed
          Alternate=defaultArg this.Alternate fallback.Alternate
          Speed=defaultArg this.Speed fallback.Speed
          FrameRate=defaultArg this.FrameRate fallback.FrameRate }
    /// <summary>An invariant diagnostic preserving explicit values and identifying inherited fields.</summary>
    override this.ToString() =
        let optional render value = value |> Option.map render |> Option.defaultValue "inherit"
        let number (value:float) = value.ToString("R",Globalization.CultureInfo.InvariantCulture)
        String.Concat [| "PlaybackDefaults(Repeat="; optional (fun (value:PlaybackRepeat) -> value.ToString()) this.Repeat
                         "; Delay="; optional number this.Delay; "; LoopDelay="; optional number this.LoopDelay
                         "; Reversed="; optional (fun (value:bool) -> value.ToString()) this.Reversed
                         "; Alternate="; optional (fun (value:bool) -> value.ToString()) this.Alternate
                         "; Speed="; optional number this.Speed; "; FrameRate="; optional number this.FrameRate; ")" |]

/// <summary>Sequence-local endpoint arbitration; independent-animation composition remains separate.</summary>
[<RequireQualifiedAccess; Diagnostics.DebuggerDisplay("{ToString(),nq}")>]
type KeyframeComposition =
    | None
    | Replace
    /// <summary>The invariant composition mode name.</summary>
    override this.ToString()=match this with KeyframeComposition.None -> "None" | KeyframeComposition.Replace -> "Replace"

/// <summary>Resolved constant animation defaults; target-dependent authoring remains on the typed keyframe APIs.</summary>
[<NoEquality;NoComparison;System.Diagnostics.DebuggerDisplay("{ToString(),nq}")>]
type AnimationDefaultOptions =
    { /// <summary>Nominal nonnegative precise milliseconds, validated and normalized by the selected factory.</summary>
      Duration: float
      Ease: float -> float
      Modifier: float -> float
      Composition: KeyframeComposition
      Autoplay: bool }
    /// <summary>1000 milliseconds, out-quad easing, identity modifier, replace composition and autoplay.</summary>
    static member Default =
        { Duration=1000.;Ease=Ease.named "outQuad";Modifier=id;Composition=KeyframeComposition.Replace;Autoplay=true }
    /// <summary>An invariant duration, composition and startup diagnostic with callback functions omitted.</summary>
    override this.ToString()=
        "AnimationDefaultOptions(Duration="+this.Duration.ToString("R",System.Globalization.CultureInfo.InvariantCulture)+
        "; Composition="+this.Composition.ToString()+"; Autoplay="+this.Autoplay.ToString()+")"

/// <summary>Optional constant animation overrides; None inherits, while duration0 and autoplayfalse remain explicit.</summary>
[<NoEquality;NoComparison>]
type AnimationDefaults =
    { /// <summary>Optional nonnegative precise milliseconds; Some0 overrides an inherited duration.</summary>
      Duration: float option
      Ease: (float -> float) option
      Modifier: (float -> float) option
      Composition: KeyframeComposition option
      Autoplay: bool option }
    /// <summary>Inherits every constant animation setting.</summary>
    static member Empty = {Duration=None;Ease=None;Modifier=None;Composition=None;Autoplay=None}
    /// <summary>Applies present constant fields without invoking user callbacks or normalizing duration.</summary>
    member this.Apply(fallback: AnimationDefaultOptions) : AnimationDefaultOptions =
        { Duration=defaultArg this.Duration fallback.Duration
          Ease=defaultArg this.Ease fallback.Ease
          Modifier=defaultArg this.Modifier fallback.Modifier
          Composition=defaultArg this.Composition fallback.Composition
          Autoplay=defaultArg this.Autoplay fallback.Autoplay }
    /// <summary>An invariant diagnostic of constant overrides; easing and modifier callbacks are omitted.</summary>
    override this.ToString() =
        let optional render value = value |> Option.map render |> Option.defaultValue "inherit"
        String.Concat [| "AnimationDefaults(Duration="
                         optional (fun (value:float) -> value.ToString("R",Globalization.CultureInfo.InvariantCulture)) this.Duration
                         "; Composition="; optional (fun (value:KeyframeComposition) -> value.ToString()) this.Composition
                         "; Autoplay="; optional (fun (value:bool) -> value.ToString()) this.Autoplay; ")" |]

/// <summary>Mutable clock defaults shared by scopes without overrides; overridden scopes retain a separate cell.</summary>
[<Sealed>]
type ScopeClockDefaults(options: PlaybackOptions, ?animation: AnimationDefaultOptions) =
    let mutable options = options
    let mutable animation = defaultArg animation AnimationDefaultOptions.Default
    do if isNull(box options) then nullArg "options"
    /// <summary>The authored clock values, read when a factory constructs a controller.</summary>
    member _.Options
        with get() = options
        and set value =
            if isNull(box value) then nullArg "value"
            options <- value
    /// <summary>Constant animation values retained in the same shared-or-copied defaults cell.</summary>
    member _.Animation
        with get() = animation
        and set value =
            if isNull(box value) then nullArg "value"
            animation <- value

/// <summary>The active clock-default cell, restored when scoped execution ends.</summary>
module internal ScopePlaybackContext =
    let globalDefaults = ScopeClockDefaults(PlaybackOptions.Default)
    let private active = new System.Threading.ThreadLocal<ScopeClockDefaults option>(fun () -> None)
    let current () = defaultArg active.Value globalDefaults
    let isScoped () = active.Value.IsSome
    let resolve options = defaultArg options (current().Options)
    let run defaults callback =
        let previous = active.Value
        active.Value <- Some defaults
        try callback() finally active.Value <- previous

/// <summary>Clock defaults for explicitly authored factory options.</summary>
[<RequireQualifiedAccess>]
module PlaybackDefaults =
    /// <summary>Reads the active scope's clock values, or the shared global clock defaults outside scoped execution.</summary>
    let current () = ScopePlaybackContext.current().Options
    /// <summary>Applies present factory fields to the active clock values, preserving explicit false and zero.</summary>
    let resolve (overrides: PlaybackDefaults) = overrides.Apply(current())

/// <summary>Typed constant animation defaults applied by animation and keyframe factories.</summary>
[<RequireQualifiedAccess>]
module AnimationDefaults =
    /// <summary>Reads the retained constant settings without invoking easing or modifier callbacks.</summary>
    let current () = ScopePlaybackContext.current().Animation
    /// <summary>Applies present constant fields to the active retained settings.</summary>
    let resolve (overrides: AnimationDefaults) = overrides.Apply(current())
    /// <summary>Resolves an optional precise duration, preserving an explicit zero.</summary>
    let duration value = defaultArg value (current().Duration)
    let internal ease fallback = if ScopePlaybackContext.isScoped() then (current()).Ease else fallback
    let internal modifier (_fallback:float -> float) = (current()).Modifier
    let internal composition (_fallback:KeyframeComposition) = (current()).Composition
    let internal autoplay (_fallback:bool) = (current()).Autoplay

/// <summary>Iteration and scheduling state transferred when a scope reconstructs a playback controller.</summary>
[<Struct>]
type internal ScopeClockSnapshot =
    { Iteration: int
      IterationProgress: float
      Reversed: bool
      Alternate: bool
      Anchor: float }

/// <summary>A paused standalone timer on the scheduler's thread, with nonnegative duration in milliseconds.</summary>
/// <remarks>
/// Positive infinity represents unbounded iteration duration. Cancellation is terminal.
/// Resuming scheduling includes the source's 12 ms clock lead before speed scaling.
/// Frame rates below 240 throttle render callbacks; higher rates render every admitted host frame.
/// </remarks>
type Timer(scheduler: FrameScheduler, duration: float, ?options: PlaybackOptions,
           ?onBegin: Timer -> unit, ?onBeforeUpdate: Timer -> unit, ?onUpdate: Timer -> unit,
           ?onLoop: Timer -> unit, ?onPause: Timer -> unit, ?onComplete: Timer -> unit) as this =
    let mutable duration = duration
    let options = ScopePlaybackContext.resolve options
    let iterations = match options.Repeat with PlaybackRepeat.Count count -> float count + 1. | PlaybackRepeat.Forever -> infinity
    let callback event =
        let action =
            match event with
            | PlaybackEvent.Begin -> onBegin
            | PlaybackEvent.BeforeUpdate -> onBeforeUpdate
            | PlaybackEvent.Update -> onUpdate
            | PlaybackEvent.Loop -> onLoop
            | PlaybackEvent.Pause -> onPause
            | PlaybackEvent.Complete -> onComplete
        action |> Option.iter(fun action -> action this)
    let state = PlaybackState(duration, iterations, options.Delay, options.LoopDelay, options.Reversed, options.Alternate, callback)
    let finite name value =
        if not(Double.IsFinite value) then invalidArg name "Expected a finite number."
    do finite "FrameRate" options.FrameRate
    let mutable fps = Math.Max(1e-11, options.FrameRate)
    let mutable lastTick = scheduler.Now()
    let mutable anchor = lastTick
    let mutable generation = 0
    let mutable pending : IDisposable option = None
    do state.Speed <- options.Speed
    let mutable stretchRenderer : float -> unit = ignore
    let mutable renderingEnabled = true
    let mutable parented = false
    let mutable onFault = fun () -> ()
    let mutable onCancel = fun () -> ()

    let live () =
        if state.Cancelled then raise(ObjectDisposedException(nameof Timer))

    let stopFrame () =
        generation <- generation + 1
        let previous = pending
        pending <- None
        previous |> Option.iter(fun frame -> frame.Dispose())
        generation

    let reanchor lead =
        anchor <- scheduler.Now() - (state.Sample.AbsoluteTime + state.Delay) / state.Speed - lead

    let requestTick now =
        if fps >= 240. then PlaybackTickMode.Auto
        else
            let frameDuration = 1000. / fps
            let elapsed = now - lastTick
            let tolerance = Math.Min(frameDuration * 0.25, 4.)
            if elapsed + tolerance < frameDuration then PlaybackTickMode.Skip
            else
                lastTick <- if elapsed >= frameDuration then now - elapsed % frameDuration else now
                PlaybackTickMode.Auto

    let abandon () =
        stopFrame() |> ignore
        try state.Cancel() with _ -> ()
        try onFault() with _ -> ()

    let rec queue () =
        if not parented && not state.Paused && not state.Cancelled && pending.IsNone then
            let expected = generation
            pending <- Some(scheduler.Frame(fun now ->
                if expected = generation && not state.Paused && not state.Cancelled then
                    pending <- None
                    try
                        state.Tick((now - anchor) * state.Speed, requestTick now, false)
                        if expected = generation && not state.Cancelled then
                            if Double.IsPositiveInfinity iterations && state.Sample.AboveZero && state.Sample.AtEnd then
                                anchor <- anchor + state.Duration
                            queue()
                    with _ ->
                        abandon()
                        reraise()))

    let operate action finish =
        live()
        let expected = stopFrame()
        try
            action (fun () -> expected = generation && not state.Cancelled)
            if expected = generation && not state.Cancelled then finish()
            queue()
        with _ ->
            abandon()
            reraise()

    let resume action changedDirection =
        let wasPaused = state.Paused
        operate (fun _ -> action()) (fun () ->
            if wasPaused && not state.Paused then reanchor 12.
            elif changedDirection then reanchor 0.)

    let setTimeGuarded canWrite time =
        finite "time" time
        state.WithWriteGuard(canWrite, fun () ->
            let wasPaused = state.Paused
            operate (fun current ->
                state.Pause()
                if current() && canWrite() then state.Seek(time, false)
                if current() && canWrite() && not wasPaused then state.Resume())
                (fun () -> if canWrite() && not state.Paused then reanchor 12.))

    let setTime time = setTimeGuarded (fun () -> true) time

    let cancel notify =
        stopFrame() |> ignore
        try state.Cancel() finally if notify then onCancel()
    do ScopeRegistration.register this.RewindForRevert
    /// <summary>Accepts an unsigned whole-millisecond iteration duration.</summary>
    new(scheduler: FrameScheduler, duration: uint, ?options: PlaybackOptions,
        ?onBegin: Timer -> unit, ?onBeforeUpdate: Timer -> unit, ?onUpdate: Timer -> unit,
        ?onLoop: Timer -> unit, ?onPause: Timer -> unit, ?onComplete: Timer -> unit) =
        new Timer(scheduler, float duration, ?options=options, ?onBegin=onBegin, ?onBeforeUpdate=onBeforeUpdate,
                  ?onUpdate=onUpdate, ?onLoop=onLoop, ?onPause=onPause, ?onComplete=onComplete)
    /// <summary>Resumes the current direction.</summary>
    member _.Resume() = resume state.Resume false
    /// <summary>Resumes forward playback.</summary>
    member _.Play() = resume state.Play state.Reversed
    /// <summary>Resumes reversed playback.</summary>
    member _.Reverse() = resume state.Reverse (not state.Reversed)
    /// <summary>Mirrors time and direction according to iteration parity.</summary>
    member _.Alternate() = operate (fun _ -> state.Alternate()) (fun () -> reanchor 0.)
    /// <summary>Stops scheduled frames and emits pause once when active.</summary>
    member _.Pause() =
        if not state.Cancelled then operate (fun _ -> state.Pause()) ignore
    /// <summary>Seeks in milliseconds after the initial delay; callbacks can be muted.</summary>
    member _.Seek(time: float, ?muteCallbacks: bool) =
        finite "time" time
        operate (fun _ -> state.Seek(time, defaultArg muteCallbacks false))
                (fun () -> if not state.Paused then reanchor 12.)
    /// <summary>Resets time and flags with muted render callbacks.</summary>
    member _.Reset() = operate (fun _ -> state.Reset()) ignore
    /// <summary>Resets and resumes playback.</summary>
    member _.Restart() = operate (fun _ -> state.Restart()) (fun () -> reanchor 12.)
    /// <summary>Completes then cancels unless a callback supersedes the operation.</summary>
    member _.Complete(?muteCallbacks: bool) =
        operate (fun _ -> state.Complete(defaultArg muteCallbacks false)) ignore
    /// <summary>Stretches total duration and delay geometry without sampling or moving the raw playhead.</summary>
    /// <remarks>Finite nonnegative milliseconds are required. Scaled delay overflow rejects before changing active playback.</remarks>
    member _.Stretch(milliseconds:float) =
        live()
        match state.PrepareStretch milliseconds with
        | None -> ()
        | Some next ->
            let scale=milliseconds/state.Duration
            operate (fun current ->
                stretchRenderer scale
                if current() then
                    state.ApplyStretch next
                    duration<-next.IterationDuration) ignore
    /// <summary>Stretches total duration using unsigned whole milliseconds.</summary>
    member _.Stretch(milliseconds:uint) = this.Stretch(float milliseconds)
    /// <summary>Ends the disposable lifetime; repeated cancellation is harmless.</summary>
    member _.Cancel() = cancel true
    /// <summary>Last sampled time in milliseconds; setting it emits pause when active and then restores playback.</summary>
    member _.CurrentTime with get() = state.Sample.CurrentTime and set value = setTime value
    /// <summary>Gets bounded overall progress; setting scales by total duration and seeks with playback preserved.</summary>
    member _.Progress
        with get() = state.Sample.Progress
        and set value = setTime (state.Duration * value)
    /// <summary>Gets bounded directional iteration time; setting seeks within the current iteration with playback preserved.</summary>
    member _.IterationCurrentTime
        with get() = state.Sample.IterationCurrentTime
        and set value = setTime (duration * float state.Sample.Iteration + value)
    /// <summary>Gets bounded iteration progress; setting scales by iteration duration and seeks with playback preserved.</summary>
    member _.IterationProgress
        with get() = state.Sample.IterationProgress
        and set value = setTime (duration * float state.Sample.Iteration + duration * value)
    /// <summary>Current iteration index using the source's signed 32-bit conversion.</summary>
    member _.CurrentIteration = state.Sample.Iteration
    /// <summary>Seeks an unsigned iteration index, bounded to the repeat count; gaps follow source setter semantics.</summary>
    member _.SeekIteration(index: uint) =
        setTime (duration * Math.Clamp(float index, 0., iterations - 1.))
    /// <summary>Duration of the complete repeat sequence, excluding initial delay.</summary>
    member _.Duration = state.Duration
    /// <summary>Duration of one iteration in milliseconds.</summary>
    member _.IterationDuration = duration
    /// <summary>Iteration count, including the initial iteration; infinity denotes forever.</summary>
    member _.IterationCount = iterations
    /// <summary>Whether raw sampling direction and iteration reversal combine into backward playback.</summary>
    member _.Backwards = state.Sample.Backwards
    /// <summary>Gets base direction; setting resumes playback in the requested direction.</summary>
    member _.Reversed
        with get() = state.Reversed
        and set value = if value then this.Reverse() else this.Play()
    /// <summary>Whether positive time has been sampled since reset or a nonpositive sample.</summary>
    member _.Began = state.Began
    /// <summary>Whether the last sample completed the finite sequence.</summary>
    member _.Completed = state.Completed
    /// <summary>Whether scheduling is paused.</summary>
    member _.Paused = state.Paused
    /// <summary>Whether cancellation has ended the disposable lifetime.</summary>
    member _.Cancelled = state.Cancelled
    /// <summary>The source callback delta in milliseconds.</summary>
    member _.DeltaTime = state.DeltaTime
    /// <summary>Finite playback multiplier; values below 1e-11 clamp to that minimum.</summary>
    member _.Speed
        with get() = state.Speed
        and set value =
            finite "value" value
            operate (fun _ -> state.Speed <- value) (fun () -> reanchor 0.)
    /// <summary>Finite frame-rate ceiling; values below 1e-11 clamp to that minimum.</summary>
    member _.Fps
        with get() = fps
        and set value =
            finite "value" value
            live()
            fps <- Math.Max(1e-11, value)
    /// <summary>Raw geometry for shared playback integration and behavioral verification.</summary>
    member internal _.Sample = state.Sample
    /// <summary>Both scheduler and state generations for guarding property writes across user code.</summary>
    member internal _.Generation = generation, state.Generation
    /// <summary>Captures clock/state generations and current inherited guards before an easing or modifier callback.</summary>
    member internal _.CaptureWriteGuard() =
        let expected = generation
        let inherited = state.CaptureWriteGuard()
        fun () -> expected = generation && inherited()
    /// <summary>Raw time immediately before the current sample.</summary>
    member internal _.PreviousAbsoluteTime = state.PreviousAbsoluteTime
    /// <summary>Installs an animation renderer and an optional source setter-duration override before sampling.</summary>
    member internal _.ConfigureRenderer(renderer, ?totalDuration: float, ?onStretch: (float -> unit)) =
        state.Renderer <- fun sample mode muted -> if renderingEnabled then renderer sample mode muted
        onStretch |> Option.iter(fun hook -> stretchRenderer <- hook)
        totalDuration |> Option.iter state.OverrideDuration
    /// <summary>Assigns current time with pause/seek/resume semantics while every ancestor remains live.</summary>
    member internal _.SetTimeGuarded(time, canWrite) = setTimeGuarded canWrite time
    /// <summary>Captures rounded iteration progress, direction policy and the scheduling anchor before scope reversion.</summary>
    member internal _.CaptureScopeTime() : ScopeClockSnapshot =
        { Iteration = this.CurrentIteration
          IterationProgress = this.IterationProgress
          Reversed = this.Reversed
          Alternate = options.Alternate
          Anchor = anchor }
    /// <summary>Restores source setter semantics and then the original clock anchor, while cancellation remains terminal.</summary>
    member internal _.RestoreScopeTime(snapshot: ScopeClockSnapshot) =
        live()
        this.SeekIteration(uint(Math.Max(0,snapshot.Iteration)))
        if not state.Cancelled then
            let backwards = snapshot.Reversed <> (snapshot.Alternate && snapshot.Iteration % 2 <> 0)
            this.IterationProgress <- if backwards then 1. - snapshot.IterationProgress else snapshot.IterationProgress
            if not state.Cancelled then anchor <- snapshot.Anchor
    /// <summary>Installs cleanup invoked after callback/render faults cancel this driver.</summary>
    member internal _.OnFault with set value = onFault <- value
    /// <summary>Registers cleanup after explicit Cancel/Dispose; completion and internal revert retain restoration ownership.</summary>
    member internal _.OnCancel with set value = onCancel <- value
    /// <summary>Initial delay of an owned timeline child.</summary>
    member internal _.Delay = state.Delay
    /// <summary>Current gap between repeated iterations.</summary>
    member internal _.LoopDelay = state.LoopDelay
    /// <summary>Prepares the source constructor's zero playhead before an owned animation notification.</summary>
    member internal _.PrepareConstruction()=state.PrepareConstruction()
    /// <summary>Assigns scheduling to the timeline before construction callbacks can resume this child.</summary>
    member internal _.AssignToTimeline() =
        parented <- true
        stopFrame() |> ignore
    /// <summary>Assigns scheduling to the timeline and initializes silently, optionally refreshing caches without target dispatch.</summary>
    member internal _.AttachToTimeline(?internalRender:bool,?canWrite:unit -> bool) =
        this.AssignToTimeline()
        state.TickChild(0., PlaybackTickMode.Force, true, false, false, defaultArg canWrite (fun () -> true), ?internalRender=internalRender, initialize=true)
    /// <summary>Samples a timeline child without an independent frame, optionally refreshing caches without target dispatch.</summary>
    member internal _.SampleChild(time, mode, muted, parentFps, backwards, parentBegan, canWrite, ?internalRender:bool) =
        if not state.Cancelled && canWrite() then
            let mode = if fps < parentFps then requestTick(scheduler.Now()) else mode
            state.TickChild(time, mode, muted, backwards, parentBegan, canWrite, ?internalRender=internalRender)
    /// <summary>Emits the source reverse-loop boundary completion without changing child flags.</summary>
    member internal _.NotifyBoundaryCompletion() =
        if not state.Cancelled then callback PlaybackEvent.Complete
    /// <summary>Resets owned child flags after a timeline reset or forward loop.</summary>
    member internal _.ResetChildFlags() =
        if not state.Cancelled then state.ResetChildFlags()
    /// <summary>Configures the timeline child phases before rendering begins.</summary>
    member internal _.ConfigureChildren(beforeLoop, renderChildren) =
        state.ConfigureChildren(beforeLoop, renderChildren)
    /// <summary>Updates timeline iteration geometry and resets with muted callbacks.</summary>
    member internal _.Resize(value: float) =
        operate (fun current ->
            state.Resize value
            duration <- value
            if current() then state.Reset()) ignore
    /// <summary>Revives the internal animatable callback controller and resets its retained clock history.</summary>
    member internal _.ReviveReset() =
        state.ReviveForReset()
        this.Reset()
    /// <summary>Samples raw zero with muted render callbacks, then cancels if the rewind remains current.</summary>
    member internal _.RewindForRevert() =
        state.ReviveForReset()
        operate (fun current ->
            state.Tick(0.,PlaybackTickMode.Auto,true)
            if current() then cancel false) ignore
    /// <summary>Whether the current renderer invocation refreshes caches without target dispatch.</summary>
    member internal _.InternalRender = state.InternalRender
    /// <summary>Prepares endpoints, optional stretch, cache-only reset and resume under one interruption guard.</summary>
    member internal _.Retarget(prepare: (unit -> bool) -> bool, ?newDuration:float) =
        live()
        let next=newDuration |> Option.bind state.PrepareStretch
        let scale=newDuration |> Option.map(fun value -> value/state.Duration)
        operate (fun current ->
            if prepare current && current() then
                match next,scale with
                | Some geometry,Some scale ->
                    stretchRenderer scale
                    if current() then
                        state.ApplyStretch geometry
                        duration <- geometry.IterationDuration
                | _ -> ()
                if current() then state.SoftReset()
                if current() then state.Resume()) (fun () -> reanchor 12.)
    /// <summary>Resets retargeted endpoints without dispatching a reset write, then resumes under one interruption guard.</summary>
    member internal _.Rebase(prepare: unit -> unit) =
        operate (fun current ->
            state.Pause()
            if current() then prepare()
            if current() then
                renderingEnabled <- false
                try state.Reset() finally renderingEnabled <- true
            if current() then state.Resume()) (fun () -> reanchor 12.)
    interface IDisposable with
        member _.Dispose() = cancel true

/// <summary>Lifetime attachment for standalone playback timers.</summary>
[<RequireQualifiedAccess>]
module Timer =
    /// <summary>Attaches the timer to the current Ranvier owner, which cancels it on disposal.</summary>
    let own (timer: Timer) =
        ScopeRegistration.attach timer
        timer
