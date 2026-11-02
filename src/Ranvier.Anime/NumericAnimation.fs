namespace Ranvier.Anime

open System
open Ranvier
open Ranvier.Dom

/// <summary>A paused numeric animation using the shared source clock and explicit typed property accessors.</summary>
/// <remarks>
/// Call on the scheduler's thread. Cancellation is terminal. Resume includes the source's 12 ms clock lead.
/// Construction captures the start value; Reset renders initialization. Durations use source 12-place precision.
/// Positive durations rounded to zero are rejected.
/// </remarks>
type NumericAnimation(scheduler: FrameScheduler, read: unit -> float, write: float -> unit, target: float, duration: float,
                      ?ease: (float -> float), ?options: PlaybackOptions,
                      ?onBegin: NumericAnimation -> unit, ?onBeforeUpdate: NumericAnimation -> unit,
                      ?onRender: NumericAnimation -> unit, ?onUpdate: NumericAnimation -> unit,
                      ?onLoop: NumericAnimation -> unit, ?onPause: NumericAnimation -> unit,
                      ?onComplete: NumericAnimation -> unit) as this =
    let ease = defaultArg ease id
    let options = defaultArg options PlaybackOptions.Default
    let finite name value =
        if not(Double.IsFinite value) then invalidArg name "Expected a finite number."
    do
        finite "duration" duration
        if duration < 0. then invalidArg "duration" "Duration must be nonnegative."
        finite "target" target
        for name, value in ["Delay", options.Delay; "LoopDelay", options.LoopDelay; "Speed", options.Speed; "FrameRate", options.FrameRate] do
            finite name value
    let iterationDuration = PropertyNumbers.round 1e12 (if duration = 0. then 1e-11 else duration)
    do
        if iterationDuration = 0. then invalidArg "duration" "Positive duration rounds to zero at source timing precision."
    let mutable first = read()
    do finite "read" first
    let mutable last = target
    let mutable tweenTime = 0.
    let mutable ancestorLive = fun () -> true
    let clock = new Timer(scheduler, iterationDuration, options=options,
                          onBegin=(fun _ -> onBegin |> Option.iter(fun callback -> callback this)),
                          onBeforeUpdate=(fun _ -> onBeforeUpdate |> Option.iter(fun callback -> callback this)),
                          onUpdate=(fun _ -> onUpdate |> Option.iter(fun callback -> callback this)),
                          onLoop=(fun _ -> onLoop |> Option.iter(fun callback -> callback this)),
                          onPause=(fun _ -> onPause |> Option.iter(fun callback -> callback this)),
                          onComplete=(fun _ -> onComplete |> Option.iter(fun callback -> callback this)))
    let render (sample: PlaybackSample) mode muted =
        let expected = clock.Generation
        let canWrite () = expected = clock.Generation && not clock.Cancelled && ancestorLive()
        let delta = sample.AbsoluteTime - clock.PreviousAbsoluteTime
        let force = mode = PlaybackTickMode.Force || (if sample.Backwards then -delta else delta) >= 200.
        let absoluteTime = PropertyNumbers.round 1e12 (options.Delay + sample.IterationTime)
        let start = PropertyNumbers.round 1e12 options.Delay
        let eligible = force || ((tweenTime <> iterationDuration || absoluteTime <= start + iterationDuration) &&
                                 (tweenTime <> 0. || absoluteTime >= start))
        if eligible && canWrite() then
            tweenTime <- Math.Clamp(sample.IterationTime, 0., iterationDuration)
            let progress = ease (tweenTime / iterationDuration)
            if canWrite() then
                finite "ease" progress
                write (PropertyNumbers.interpolate first last progress)
                if canWrite() && sample.AboveZero && not muted then
                    onRender |> Option.iter(fun callback -> callback this)
    do clock.ConfigureRenderer(render, ?totalDuration=(if iterationDuration = 1e-11 then Some 1e-11 else None))

    /// <summary>Accepts an unsigned whole-millisecond duration through the precise float clock.</summary>
    new(scheduler: FrameScheduler, read: unit -> float, write: float -> unit, target: float, duration: uint,
        ?ease: (float -> float), ?options: PlaybackOptions,
        ?onBegin: NumericAnimation -> unit, ?onBeforeUpdate: NumericAnimation -> unit,
        ?onRender: NumericAnimation -> unit, ?onUpdate: NumericAnimation -> unit,
        ?onLoop: NumericAnimation -> unit, ?onPause: NumericAnimation -> unit,
        ?onComplete: NumericAnimation -> unit) =
        new NumericAnimation(scheduler, read, write, target, float duration, ?ease=ease, ?options=options,
                             ?onBegin=onBegin, ?onBeforeUpdate=onBeforeUpdate, ?onRender=onRender, ?onUpdate=onUpdate,
                             ?onLoop=onLoop, ?onPause=onPause, ?onComplete=onComplete)
    /// <summary>Resumes the current direction from the last sample.</summary>
    member _.Resume() = clock.Resume()
    /// <summary>Resumes forward playback, mirroring time when reversed.</summary>
    member _.Play() = clock.Play()
    /// <summary>Resumes reverse playback, mirroring time when forward.</summary>
    member _.Reverse() = clock.Reverse()
    /// <summary>Mirrors time and direction according to repeat parity.</summary>
    member _.Alternate() = clock.Alternate()
    /// <summary>Stops scheduled frames while retaining the last sampled time and value.</summary>
    member _.Pause() = clock.Pause()
    /// <summary>Samples milliseconds after the initial delay; muting suppresses callbacks and preserves property rendering.</summary>
    member _.Seek(milliseconds: float, ?muteCallbacks: bool) = clock.Seek(milliseconds, ?muteCallbacks=muteCallbacks)
    /// <summary>Samples a borrowed child only while every ancestor permits the final write after easing.</summary>
    member internal _.SeekGuarded(milliseconds: float, shouldWrite: unit -> bool) =
        let previous = ancestorLive
        ancestorLive <- fun () -> previous() && shouldWrite()
        try clock.Seek milliseconds finally ancestorLive <- previous
    /// <summary>Rewinds time, renders the initial value and clears completion with muted callbacks.</summary>
    member _.Reset() = clock.Reset()
    /// <summary>Resets and resumes playback.</summary>
    member _.Restart() = clock.Restart()
    /// <summary>Samples the end and cancels unless a callback supersedes the operation.</summary>
    member _.Complete(?muteCallbacks: bool) = clock.Complete(?muteCallbacks=muteCallbacks)
    /// <summary>Starts from the currently rendered value using the original iteration duration and repeat options.</summary>
    member _.Retarget(target: float) =
        if clock.Cancelled then raise(ObjectDisposedException(nameof NumericAnimation))
        finite "target" target
        let value = read()
        finite "read" value
        clock.Rebase(fun () -> first <- value; last <- target; tweenTime <- 0.)
    /// <summary>Stops future writes; repeated cancellation is harmless.</summary>
    member _.Cancel() = clock.Cancel()
    /// <summary>Last sampled time with source bounds and four-decimal precision.</summary>
    member _.CurrentTime = clock.CurrentTime
    /// <summary>Bounded directional time inside the current iteration.</summary>
    member _.IterationCurrentTime = clock.IterationCurrentTime
    /// <summary>Current iteration index using source signed 32-bit conversion.</summary>
    member _.CurrentIteration = clock.CurrentIteration
    /// <summary>Total duration of the repeat sequence, excluding initial delay.</summary>
    member _.Duration = clock.Duration
    /// <summary>Normalized duration of one iteration in milliseconds.</summary>
    member _.IterationDuration = iterationDuration
    /// <summary>Bounded overall progress at the source's ten-decimal precision.</summary>
    member _.Progress = clock.Progress
    /// <summary>Whether directional sampling is running backwards.</summary>
    member _.Backwards = clock.Backwards
    /// <summary>Whether the base playback direction is reversed.</summary>
    member _.Reversed = clock.Reversed
    /// <summary>Whether the most recent sample completed the finite repeat sequence.</summary>
    member _.Completed = clock.Completed
    /// <summary>Whether a frame loop is active.</summary>
    member _.Playing = not clock.Paused
    /// <summary>Whether cancellation has ended the disposable lifetime.</summary>
    member _.Cancelled = clock.Cancelled
    /// <summary>Finite playback multiplier, clamped to at least 1e-11.</summary>
    member _.Speed with get() = clock.Speed and set value = clock.Speed <- value
    /// <summary>Finite callback frame-rate setting, clamped to at least 1e-11.</summary>
    member _.Fps with get() = clock.Fps and set value = clock.Fps <- value
    interface IDisposable with member _.Dispose() = clock.Cancel()

/// <summary>Numeric tweens with explicit scheduling and optional Ranvier ownership.</summary>
[<RequireQualifiedAccess>]
module Animation =
    /// <summary>Creates and starts a tween with custom easing and shared playback options.</summary>
    let animateWithOptions options ease scheduler read write target (duration: float) =
        let animation = new NumericAnimation(scheduler, read, write, target, duration, ease=ease, options=options)
        try
            animation.Reset()
            animation.Resume()
            animation
        with _ ->
            (animation :> IDisposable).Dispose()
            reraise()
    /// <summary>Creates and starts a tween with custom easing and default playback options.</summary>
    let animateWith ease scheduler read write target duration =
        animateWithOptions PlaybackOptions.Default ease scheduler read write target duration
    /// <summary>Creates and starts a linear tween. The caller owns its disposable lifetime.</summary>
    let animate scheduler read write target duration = animateWith id scheduler read write target duration
    /// <summary>Creates a tween using a spring easing and its calculated settling duration.</summary>
    let animateSpring (spring: Spring) scheduler read write target =
        animateWith spring.Ease scheduler read write target spring.SettlingDuration
    /// <summary>Attaches a tween to the current Ranvier owner, which cancels it on disposal.</summary>
    let own (animation: NumericAnimation) =
        Api.getOwner().Attach animation
        animation
