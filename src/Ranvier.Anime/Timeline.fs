namespace Ranvier.Anime

open System
open Ranvier.Dom

/// <summary>A paused timeline borrowing numeric children and sampling them through one frame scheduler.</summary>
/// <remarks>Use the scheduler's owning thread. Disposal is terminal and preserves child values.</remarks>
type Timeline(scheduler: FrameScheduler) =
    let children = System.Collections.Generic.List<float * NumericAnimation>()
    let labels = System.Collections.Generic.Dictionary<string, float>(System.StringComparer.Ordinal)
    let mutable duration = 0.
    let mutable elapsed = 0.
    let mutable anchor = 0.
    let mutable playing = false
    let mutable completed = false
    let mutable cancelled = false
    let mutable generation = 0
    let mutable pending: System.IDisposable option = None

    let finite name value = if not(System.Double.IsFinite value) then invalidArg name "Expected a finite time."
    let ensureLive () = if cancelled then raise(System.ObjectDisposedException("Timeline"))
    let stopFrame () =
        generation <- generation + 1
        let previous = pending
        pending <- None
        previous |> Option.iter _.Dispose()
    let cancel () =
        playing <- false
        cancelled <- true
        stopFrame()
        children.Clear()
        labels.Clear()
    let render time =
        let expected = generation
        let previous = elapsed
        let time = System.Math.Clamp(time, 0., duration)
        elapsed <- time
        completed <- time >= duration
        if completed then playing <- false
        let snapshots = children.ToArray()
        let snapshots = if time < previous then Array.rev snapshots else snapshots
        for offset, child in snapshots do
            let childTime = time - offset
            if expected = generation && not cancelled && not(time < previous && childTime > child.Duration) &&
               (time >= offset || previous >= offset) then
                child.Pause()
                child.SeekGuarded(System.Math.Clamp(childTime, 0., child.Duration), (fun () -> expected = generation && not cancelled))
    let rec queue () =
        if playing && not cancelled && pending.IsNone then
            let expected = generation
            pending <- Some(scheduler.Frame(fun now ->
                if expected = generation && playing && not cancelled then
                    pending <- None
                    try
                        render (now - anchor)
                        if expected = generation then queue()
                    with _ ->
                        cancel()
                        reraise()))


    /// <summary>Resolves a position against the current duration, labels and previous child.</summary>
    member _.ResolvePosition(?position: TimelinePosition) =
        ensureLive()
        let previous =
            if children.Count = 0 then None
            else let offset, child = children[children.Count - 1] in Some(offset, offset + child.Duration)
        TimelinePositions.resolve duration previous
            (fun label -> match labels.TryGetValue label with true, value -> Some value | _ -> None) position

    /// <summary>Records a label at an explicit position or the current timeline end.</summary>
    member this.Label(name: string, ?position: TimelinePosition) =
        ensureLive()
        if isNull name then nullArg "name"
        labels[name] <- this.ResolvePosition(?position = position)
        this

    /// <summary>Pauses and borrows a child at a position, defaulting to the current timeline end.</summary>
    member this.Sync(child: NumericAnimation, ?position: TimelinePosition) =
        ensureLive()
        if isNull (box child) then nullArg "child"
        let offset = this.ResolvePosition(?position = position)
        let finish = offset + child.Duration
        finite "position" finish
        stopFrame()
        child.Pause()
        children.Add(offset, child)
        duration <- System.Math.Max(duration, finish)
        try
            render elapsed
            queue()
            this
        with _ ->
            cancel()
            reraise()

    /// <summary>Borrows a child group using stagger offsets based on the timeline end before insertion.</summary>
    member this.SyncStaggered(children: NumericAnimation array, value: StaggerValue, options: StaggerOptions) =
        ensureLive()
        if isNull children then nullArg "children"
        let originalStart =
            match options.Start, value with
            | Some start, _ when start <> 0. -> start
            | _, StaggerValue.Range(first, _) -> first
            | _ -> 0.
        let start = if options.Start.IsNone then duration else originalStart
        let offsets =
            Stagger.distributeAt (Some start) value options (uint children.Length)
            |> Array.map(fun offset ->
                finite "options" offset
                offset)
        Array.iter2(fun child offset -> this.Sync(child, TimelinePosition.At offset) |> ignore) children offsets
        this

    /// <summary>The scheduled duration in milliseconds.</summary>
    member _.Duration = duration

    /// <summary>The last sampled or paused time in milliseconds.</summary>
    member _.CurrentTime = elapsed

    /// <summary>Whether the scheduler is advancing this timeline.</summary>
    member _.Playing = playing

    /// <summary>Whether the latest sample reached the end.</summary>
    member _.Completed = completed

    /// <summary>Samples synchronized children at a clamped millisecond position.</summary>
    member _.Seek(milliseconds: float) =
        ensureLive()
        finite "milliseconds" milliseconds
        stopFrame()
        anchor <- scheduler.Now() - System.Math.Clamp(milliseconds, 0., duration)
        try
            render milliseconds
            queue()
        with _ ->
            cancel()
            reraise()

    /// <summary>Continues from the current time; completed timelines stay completed.</summary>
    member _.Play() =
        ensureLive()
        if not playing && not completed then
            playing <- true
            anchor <- scheduler.Now() - elapsed
            if duration = 0. then render 0. else queue()

    /// <summary>Stops scheduled frames while retaining elapsed time.</summary>
    member _.Pause() =
        if playing then
            elapsed <- System.Math.Clamp(scheduler.Now() - anchor, 0., duration)
            playing <- false
            stopFrame()

    /// <summary>Pauses and rewinds synchronized children.</summary>
    member this.Reset() =
        this.Pause()
        this.Seek 0.

    /// <summary>Rewinds and starts the timeline.</summary>
    member this.Restart() =
        this.Reset()
        this.Play()

    /// <summary>Samples the end and stops scheduling frames.</summary>
    member this.Complete() = this.Seek duration

    /// <summary>Stops future timeline writes and releases borrowed child references.</summary>
    member _.Cancel() = cancel()

    interface IDisposable with member _.Dispose() = cancel()

/// <summary>Optional Ranvier ownership for borrowed-child timelines.</summary>
[<RequireQualifiedAccess>]
module Timeline =
    /// <summary>Registers terminal cancellation with the current Ranvier owner.</summary>
    let own (timeline: Timeline) =
        let owner = Ranvier.Api.getOwner()
        owner.Attach timeline
        timeline
