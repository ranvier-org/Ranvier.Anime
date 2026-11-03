namespace Ranvier.Anime

/// <summary>Callback phases for a playback controller.</summary>
[<System.Diagnostics.DebuggerDisplay("{ToString(),nq}")>]
type internal PlaybackEvent =
    | Begin
    | BeforeUpdate
    | Update
    | Loop
    | Pause
    | Complete
    /// <summary>Callback phase name for diagnostics.</summary>
    override this.ToString() =
        match this with
        | Begin -> "Begin"
        | BeforeUpdate -> "BeforeUpdate"
        | Update -> "Update"
        | Loop -> "Loop"
        | Pause -> "Pause"
        | Complete -> "Complete"

/// <summary>Playback callback and control state; clock input includes the initial delay.</summary>
type internal PlaybackState(duration: float, iterations: float, delay: float, loopDelay: float,
                            initialReversed: bool, alternate: bool, callback: PlaybackEvent -> unit) =
    let mutable delay = delay
    let mutable loopDelay = loopDelay
    let mutable duration = duration
    let mutable timing = PlaybackTiming(duration, iterations, delay, loopDelay)
    let mutable sample = timing.Sample(0., 0., initialReversed, alternate, false, PlaybackTickMode.Force)
    let mutable paused = true
    let mutable began = false
    let mutable completed = false
    let mutable cancelled = false
    let mutable reversed = initialReversed
    let mutable speed = 1.
    let mutable deltaTime = 0.
    let mutable lastTime = 0.
    let mutable generation = 0
    let mutable previousAbsoluteTime = 0.
    let mutable parentContext : (bool * bool * (unit -> bool)) option = None
    let mutable writeGuard = fun () -> true
    let mutable completingSetter = false
    let mutable internalRender = false
    let mutable renderer : PlaybackSample -> PlaybackTickMode -> bool -> unit = fun _ _ _ -> ()
    let mutable childRenderer : (PlaybackSample -> PlaybackTickMode -> bool -> bool) option = None
    let mutable prepareLoop : PlaybackSample -> PlaybackTickMode -> bool -> unit = fun _ _ _ -> ()

    let live () =
        if cancelled then raise (System.ObjectDisposedException("Timer"))

    let change () =
        generation <- generation + 1
        generation

    let render time mode mute soft =
        let previousInternal = internalRender
        internalRender <- soft
        try
            let expected = generation
            let previousIteration = sample.Iteration
            let wasCompleted = completed
            previousAbsoluteTime <- sample.AbsoluteTime
            let next = timing.Sample(previousAbsoluteTime, time, reversed, alternate, wasCompleted, mode,
                                     ?parentBackwards=(parentContext |> Option.map(fun (backwards,_,_) -> backwards)), child=parentContext.IsSome)
            sample <- next
            let current () = expected = generation && not cancelled && writeGuard() &&
                             (parentContext |> Option.forall(fun (_,_,live) -> live()))
            let parentAllowsLifecycle () = parentContext |> Option.forall(fun (_,parentBegan,_) -> not next.Backwards && parentBegan)
            let emit event = if not mute && current() then callback event
            if next.AboveZero && not began then
                began <- true
                if parentAllowsLifecycle() then emit PlaybackEvent.Begin
            elif next.AbsoluteTime <= 0. then
                began <- false
            if childRenderer.IsNone && current() && next.AboveZero && next.Iteration <> previousIteration then emit PlaybackEvent.Loop
            if current() && next.ShouldRender then
                if next.AboveZero then
                    deltaTime <- next.PreviousTime - lastTime
                    lastTime <- next.PreviousTime
                    emit PlaybackEvent.BeforeUpdate
                if childRenderer.IsNone && current() then renderer next mode mute
                if next.AboveZero then emit PlaybackEvent.Update
            if current() then
                if parentContext.IsSome && timing.Duration <= 1e-11 then
                    let _,parentBegan,_ = parentContext.Value
                    if not mute && not completingSetter &&
                       ((parentBegan && not next.Backwards && next.AbsoluteTime > 0. && not completed) ||
                        (next.Backwards && next.AbsoluteTime <= 1e-11 && completed)) then
                        completingSetter <- true
                        try emit PlaybackEvent.Complete finally completingSetter <- false
                        if current() then completed <- not next.Backwards
                elif next.AboveZero && next.AtEnd then
                    if System.Double.IsFinite iterations && float next.Iteration >= iterations - 1. then
                        paused <- true
                        if childRenderer.IsNone && not wasCompleted then
                            completed <- true
                            if parentAllowsLifecycle() then emit PlaybackEvent.Complete
                else completed <- false
            match childRenderer with
            | Some renderChildren when current() ->
                if next.Iteration <> previousIteration then
                    prepareLoop next mode mute
                    if current() then emit PlaybackEvent.Loop
                let childrenComplete = if current() then renderChildren next mode mute else false
                if current() && (childrenComplete || next.Backwards) && next.AbsoluteTime >= timing.Duration then
                    paused <- true
                    if not completed then
                        completed <- true
                        emit PlaybackEvent.Complete
            | _ -> ()

        finally internalRender <- previousInternal

    do
        if sample.AboveZero then
            deltaTime <- sample.PreviousTime
            lastTime <- sample.PreviousTime

    let current expected = generation = expected && not cancelled

    let pause () =
        if not paused then
            paused <- true
            callback PlaybackEvent.Pause

    let rec resume expected =
        if current expected && paused then
            paused <- false
            if timing.Duration <= 1e-11 && childRenderer.IsNone then render 1e-11 PlaybackTickMode.Force false false
    and seek expected time mute =
        if not(System.Double.IsFinite time) then invalidArg "time" "Expected finite milliseconds."
        if current expected then
            completed <- false
            let wasPaused = paused
            paused <- true
            render (time + delay) PlaybackTickMode.Auto mute false
            if current expected && not wasPaused then resume expected
    and toggle expected =
        let count = if System.Double.IsPositiveInfinity iterations then System.Math.Floor(1e12 / duration) else iterations
        reversed <- if alternate && count % 2. = 0. then reversed else not reversed
        if System.Double.IsPositiveInfinity iterations then
            let progress = if reversed then 1. - sample.IterationProgress else sample.IterationProgress
            let time = duration * float sample.Iteration + duration * progress
            let wasPaused = paused
            pause()
            if current expected then seek expected time false
            if current expected && not wasPaused then resume expected
        else seek expected (duration * count - sample.AbsoluteTime) false
    and play expected =
        if reversed then toggle expected
        resume expected

    let reset expected soft =
        if reversed && not initialReversed then play expected
        if current expected then
            render 0. PlaybackTickMode.Force true soft
            if current expected then
                paused <- true
                began <- false
                completed <- false

    let run action =
        live()
        action (change())

    let cancel () =
        if not cancelled then
            change() |> ignore
            cancelled <- true
            pause()

    /// <summary>Most recent geometry, including raw time and public rounded getters.</summary>
    member _.Sample = sample
    /// <summary>Whether the current renderer invocation updates caches without target dispatch.</summary>
    member _.InternalRender = internalRender
    /// <summary>Total duration including repeated iterations and gaps.</summary>
    member _.Duration = timing.Duration
    /// <summary>Whether scheduling is paused.</summary>
    member _.Paused = paused
    /// <summary>Whether positive time has been sampled since the most recent reset or nonpositive sample.</summary>
    member _.Began = began
    /// <summary>Whether the most recent sample completed the finite repeat sequence.</summary>
    member _.Completed = completed
    /// <summary>Whether cancellation has ended this controller's lifetime.</summary>
    member _.Cancelled = cancelled
    /// <summary>Whether the base playback direction is reversed.</summary>
    member _.Reversed = reversed
    /// <summary>The source callback delta in milliseconds.</summary>
    member _.DeltaTime = deltaTime
    /// <summary>Changes on control operations and sampling, allowing callers to reject interrupted work.</summary>
    member _.Generation = generation
    /// <summary>Captures this sample's generation and inherited write guards for user-code interruption checks.</summary>
    member _.CaptureWriteGuard() =
        let expected = generation
        let inherited = writeGuard
        let parent = parentContext
        fun () -> expected = generation && not cancelled && inherited() && (parent |> Option.forall(fun (_,_,live) -> live()))
    /// <summary>Scopes a borrowed setter's complete ancestor guard across rendering and lifecycle callbacks.</summary>
    member _.WithWriteGuard(guard: unit -> bool, action: unit -> unit) =
        let previous = writeGuard
        writeGuard <- guard
        try action() finally writeGuard <- previous
    /// <summary>Renders eligible samples between before-update and update callbacks, including muted and nonpositive samples.</summary>
    member _.Renderer with set value = renderer <- value
    /// <summary>Renders timeline children after root update callbacks; loop preparation precedes the root loop callback.</summary>
    member _.ConfigureChildren(beforeLoop, renderChildren) =
        prepareLoop <- beforeLoop
        childRenderer <- Some renderChildren
    /// <summary>Raw time preceding the current sample, used for forced property-render eligibility.</summary>
    member _.PreviousAbsoluteTime = previousAbsoluteTime
    /// <summary>Initial delay after any source stretch operation.</summary>
    member _.Delay = delay
    /// <summary>Loop gap after any source stretch operation.</summary>
    member _.LoopDelay = loopDelay
    /// <summary>Prepares validated geometry before an active scheduler subscription is interrupted.</summary>
    member _.PrepareStretch(value:float) =
        let next=timing.Stretch value
        if obj.ReferenceEquals(next,timing) then None else Some next
    /// <summary>Commits prepared geometry, retaining raw playhead, callback flags and clock history.</summary>
    member _.ApplyStretch(next:PlaybackTiming) =
        live()
        change() |> ignore
        duration<-next.IterationDuration
        delay<-next.Delay
        loopDelay<-next.LoopDelay
        timing<-next
        sample<-timing.Rebound sample
    /// <summary>Updates iteration geometry while retaining the most recent sample until the next render.</summary>
    member _.Resize(value: float) =
        let next = PlaybackTiming(value, iterations, delay, loopDelay)
        live()
        change() |> ignore
        duration <- value
        timing <- next
    /// <summary>Overrides total geometry for source animation setter durations while preserving iteration count.</summary>
    member _.OverrideDuration(value: float) =
        if not(System.Double.IsFinite value) then invalidArg "value" "Expected finite total milliseconds."
        change() |> ignore
        timing <- PlaybackTiming(duration, iterations, delay, loopDelay, totalDuration=value)
    /// <summary>Playback multiplier; finite values below the source minimum become 1e-11.</summary>
    member _.Speed
        with get() = speed
        and set value =
            live()
            if not(System.Double.IsFinite value) then invalidArg "value" "Expected a finite playback rate."
            change() |> ignore
            speed <- System.Math.Max(1e-11, value)
    /// <summary>Samples controller callbacks and any configured child phase using a source tick mode.</summary>
    member _.Tick(time: float, mode: PlaybackTickMode, mute: bool) =
        run (fun _ -> render time mode mute false)
    /// <summary>Samples an owned timeline child with inherited direction, lifecycle filtering and ancestor interruption.</summary>
    member _.TickChild(time, mode, mute, backwards, parentBegan, canWrite) =
        let previous = parentContext
        parentContext <- Some(backwards, parentBegan, canWrite)
        try run (fun _ -> render time mode mute false)
        finally parentContext <- previous
    /// <summary>Resets child lifecycle flags while retaining its clock sample history.</summary>
    member _.ResetChildFlags() =
        paused <- true
        began <- false
        completed <- false
    /// <summary>Seeks in milliseconds after the initial delay and retains the preceding scheduling state.</summary>
    member _.Seek(time: float, mute: bool) = run (fun expected -> seek expected time mute)
    /// <summary>Pauses scheduling and emits the pause callback once for an active controller.</summary>
    member _.Pause() =
        change() |> ignore
        pause()
    /// <summary>Resumes scheduling; zero-duration standalone timers complete immediately.</summary>
    member _.Resume() = run resume
    /// <summary>Reopens an internal callback animation before resetting its retained clock history.</summary>
    member _.ReviveForReset() =
        if cancelled then
            change() |> ignore
            cancelled<-false
            paused<-true
    /// <summary>Resets time and flags with muted render callbacks, restoring the initial direction policy.</summary>
    member _.Reset() = run (fun expected -> reset expected false)
    /// <summary>Refreshes caches during reset while exposing an internal-render context to the renderer.</summary>
    member _.SoftReset() = run (fun expected -> reset expected true)
    /// <summary>Resets and resumes scheduling.</summary>
    member _.Restart() = run (fun expected -> reset expected false; resume expected)
    /// <summary>Mirrors time and changes direction according to repeat parity.</summary>
    member _.Alternate() = run toggle
    /// <summary>Resumes forward playback, mirroring time when currently reversed.</summary>
    member _.Play() = run play
    /// <summary>Resumes reversed playback, mirroring time when currently forward.</summary>
    member _.Reverse() =
        run (fun expected ->
            if not reversed then toggle expected
            resume expected)
    /// <summary>Ends the disposable lifetime and emits pause when previously active.</summary>
    member _.Cancel() = cancel()
    /// <summary>Seeks to completion then cancels, optionally muting render callbacks.</summary>
    member _.Complete(mute: bool) =
        run (fun expected ->
            seek expected timing.Duration mute
            if current expected then cancel())
