namespace Ranvier.Anime

open System
open Ranvier
open Ranvier.Dom

/// <summary>Additional iterations after the initial playback, or an indefinite repeat sequence.</summary>
[<RequireQualifiedAccess>]
type PlaybackRepeat =
    /// <summary>Runs this many additional iterations; zero runs once.</summary>
    | Count of uint
    | Forever

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

/// <summary>A paused standalone timer on the scheduler's thread, with nonnegative duration in milliseconds.</summary>
/// <remarks>
/// Positive infinity represents unbounded iteration duration. Cancellation is terminal.
/// Resuming scheduling includes the source's 12 ms clock lead before speed scaling.
/// Frame rates below 240 throttle render callbacks; higher rates render every admitted host frame.
/// </remarks>
type Timer(scheduler: FrameScheduler, duration: float, ?options: PlaybackOptions,
           ?onBegin: Timer -> unit, ?onBeforeUpdate: Timer -> unit, ?onUpdate: Timer -> unit,
           ?onLoop: Timer -> unit, ?onPause: Timer -> unit, ?onComplete: Timer -> unit) as this =
    let options = defaultArg options PlaybackOptions.Default
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
    let mutable renderingEnabled = true

    let live () =
        if state.Cancelled then raise(ObjectDisposedException(nameof Timer))

    let stopFrame () =
        generation <- generation + 1
        let previous = pending
        pending <- None
        previous |> Option.iter(fun frame -> frame.Dispose())
        generation

    let reanchor lead =
        anchor <- scheduler.Now() - (state.Sample.AbsoluteTime + options.Delay) / state.Speed - lead

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

    let rec queue () =
        if not state.Paused && not state.Cancelled && pending.IsNone then
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

    let setTime time =
        finite "time" time
        let wasPaused = state.Paused
        operate (fun current ->
            state.Pause()
            if current() then state.Seek(time, false)
            if current() && not wasPaused then state.Resume())
            (fun () -> if not state.Paused then reanchor 12.)

    let cancel () =
        stopFrame() |> ignore
        state.Cancel()
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
    /// <summary>Ends the disposable lifetime; repeated cancellation is harmless.</summary>
    member _.Cancel() = cancel()
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
    /// <summary>Raw time immediately before the current sample.</summary>
    member internal _.PreviousAbsoluteTime = state.PreviousAbsoluteTime
    /// <summary>Installs an animation renderer and an optional source setter-duration override before sampling.</summary>
    member internal _.ConfigureRenderer(renderer, ?totalDuration: float) =
        state.Renderer <- fun sample mode muted -> if renderingEnabled then renderer sample mode muted
        totalDuration |> Option.iter state.OverrideDuration
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
        member _.Dispose() = cancel()

/// <summary>Lifetime attachment for standalone playback timers.</summary>
[<RequireQualifiedAccess>]
module Timer =
    /// <summary>Attaches the timer to the current Ranvier owner, which cancels it on disposal.</summary>
    let own (timer: Timer) =
        Api.getOwner().Attach timer
        timer
