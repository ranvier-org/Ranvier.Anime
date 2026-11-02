namespace Ranvier.Anime

open System
open Ranvier
open Ranvier.Dom

/// <summary>A single numeric tween driven by an explicit frame scheduler. Instances start paused.</summary>
/// <remarks>Call from the scheduler's thread. Cancellation is terminal and preserves the last written value.</remarks>
type NumericAnimation(scheduler: FrameScheduler, read: unit -> float, write: float -> unit, target: float, duration: float,
                      ?ease: (float -> float)) =
    let ease = defaultArg ease id
    let finite name value =
        if not (Double.IsFinite value) then invalidArg name "Expected a finite number."
    do
        finite "duration" duration
        if duration < 0. then invalidArg "duration" "Duration must be nonnegative."
        finite "target" target
    let mutable first = read()
    do finite "read" first
    let mutable last = target
    let mutable elapsed = 0.
    let mutable anchor = 0.
    let mutable playing = false
    let mutable completed = false
    let mutable cancelled = false
    let mutable generation = 0
    let mutable pending : IDisposable option = None

    let stopFrame () =
        generation <- generation + 1
        let previous = pending
        pending <- None
        previous |> Option.iter (fun subscription -> subscription.Dispose())

    let ensureLive () =
        if cancelled then raise (ObjectDisposedException(nameof NumericAnimation))

    let cancel () =
        playing <- false
        cancelled <- true
        stopFrame()

    let render time =
        let expected = generation
        elapsed <- max 0. (min duration time)
        completed <- elapsed >= duration
        if completed then playing <- false
        let progress = ease (if duration = 0. then 1. else elapsed / duration)
        if expected = generation && not cancelled then
            finite "ease" progress
            let value =
                if progress = 0. then first
                elif progress = 1. then last
                else
                    let delta = last - first
                    if Double.IsFinite delta then first + delta * progress
                    else first * (1. - progress) + last * progress
            write value

    let rec queue () =
        if playing && not cancelled && pending.IsNone then
            let expected = generation
            pending <- Some (scheduler.Frame(fun now ->
                if expected = generation && playing && not cancelled then
                    pending <- None
                    try
                        render (now - anchor)
                        if expected = generation then queue()
                    with _ ->
                        cancel()
                        reraise()))

    /// <summary>Resumes from the current time; a completed tween stays completed.</summary>
    member _.Play() =
        ensureLive()
        if not playing && not completed then
            playing <- true
            anchor <- scheduler.Now() - elapsed
            if duration = 0. then render 0. else queue()

    /// <summary>Stops scheduled frames and retains elapsed time without writing a new value.</summary>
    member _.Pause() =
        if playing then
            elapsed <- max 0. (min duration (scheduler.Now() - anchor))
            playing <- false
            stopFrame()

    /// <summary>Writes the value at a time in milliseconds, clamped to this tween's duration.</summary>
    member _.Seek(milliseconds: float) =
        ensureLive()
        finite "milliseconds" milliseconds
        stopFrame()
        anchor <- scheduler.Now() - max 0. (min duration milliseconds)
        render milliseconds
        queue()

    /// <summary>Starts a new tween from the current target value using the original duration.</summary>
    member this.Retarget(target: float) =
        ensureLive()
        finite "target" target
        let current = read()
        finite "read" current
        this.Pause()
        stopFrame()
        first <- current
        last <- target
        elapsed <- 0.
        completed <- false
        this.Play()

    /// <summary>Stops future writes; repeated cancellation is harmless.</summary>
    member _.Cancel() = cancel()

    /// <summary>The last sampled or paused time, in milliseconds.</summary>
    member _.CurrentTime = elapsed

    /// <summary>Whether the most recent sample reached the end of the duration.</summary>
    member _.Completed = completed

    /// <summary>Whether a frame loop is active.</summary>
    member _.Playing = playing

    interface IDisposable with
        member _.Dispose() = cancel()

/// <summary>Numeric tweens with explicit scheduling and optional Ranvier ownership.</summary>
[<RequireQualifiedAccess>]
module Animation =
    /// <summary>Creates a tween with a custom easing function.</summary>
    let animateWith ease scheduler read write target duration =
        let animation = new NumericAnimation(scheduler, read, write, target, duration, ease = ease)
        try
            animation.Seek 0.
            animation.Play()
            animation
        with _ ->
            (animation :> IDisposable).Dispose()
            reraise()

    /// <summary>Creates and starts a linear tween. The caller owns its disposable lifetime.</summary>
    let animate scheduler read write target duration = animateWith id scheduler read write target duration

    /// <summary>Attaches a tween to the current Ranvier owner, which cancels it on disposal.</summary>
    let own (animation: NumericAnimation) =
        let owner = Api.getOwner()
        owner.Attach animation
        animation
