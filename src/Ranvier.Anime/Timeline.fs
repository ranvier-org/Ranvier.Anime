namespace Ranvier.Anime

open System
open Ranvier.Dom

/// <summary>A paused timeline borrowing animation/timeline children and sampling callback tracks through one scheduler.</summary>
/// <remarks>Use the scheduler's owning thread. Disposal is terminal and preserves child values.</remarks>
type Timeline(scheduler: FrameScheduler) =
    let children = System.Collections.Generic.List<TimelineEntry>()
    let labels = System.Collections.Generic.Dictionary<string, float>(System.StringComparer.Ordinal)
    let mutable duration = 0.
    let mutable elapsed = 0.
    let mutable sampledTime = 0.
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
    let roundChildTime value =
        let scaled = value * 1e12
        let rounded = Math.Round(scaled, MidpointRounding.ToEven)
        (if abs(scaled - rounded) = 0.5 then Math.Ceiling scaled else rounded) / 1e12
    let renderWith ancestorLive muteCallbacks requestedTime =
        let expected = generation
        let previous = sampledTime
        let time = Math.Min(requestedTime, duration)
        sampledTime <- requestedTime
        elapsed <- Math.Clamp(requestedTime, 0., duration)
        completed <- elapsed >= duration
        if completed then playing <- false
        let backwards = requestedTime < previous
        let canWrite () = expected = generation && not cancelled && ancestorLive()
        let snapshots = children.ToArray()
        let snapshots = if backwards then Array.rev snapshots else snapshots
        for entry in snapshots do
            let childTime = roundChildTime (time - entry.Offset)
            if canWrite() && not(backwards && childTime > entry.Duration) then
                match entry with
                | TimelineEntry.Numeric(_, child) when time >= entry.Offset || previous >= entry.Offset ->
                    child.Pause()
                    child.SeekGuarded(Math.Clamp(childTime, 0., entry.Duration), canWrite)
                | TimelineEntry.Nested(_, _, child) when time >= entry.Offset || previous >= entry.Offset ->
                    child.Pause()
                    child.SeekGuarded(Math.Clamp(childTime, 0., entry.Duration), canWrite)
                | TimelineEntry.Callback(_, callback, state) ->
                    if not muteCallbacks &&
                       ((elapsed > 0. && not backwards && childTime > 0. && not state.Value) ||
                        (backwards && childTime <= 1e-11 && state.Value)) then
                        state.Value <- not backwards
                        callback()
                | _ -> ()
    let render muteCallbacks time = renderWith (fun () -> true) muteCallbacks time
    let rec queue () =
        if playing && not cancelled && pending.IsNone then
            let expected = generation
            pending <- Some(scheduler.Frame(fun now ->
                if expected = generation && playing && not cancelled then
                    pending <- None
                    try
                        render false (now - anchor)
                        if expected = generation then queue()
                    with _ ->
                        cancel()
                        reraise()))


    let seek canWrite muteCallbacks milliseconds =
        ensureLive()
        finite "milliseconds" milliseconds
        stopFrame()
        anchor <- scheduler.Now() - milliseconds
        try
            renderWith canWrite muteCallbacks milliseconds
            queue()
        with _ ->
            cancel()
            reraise()

    let addChild (entry: TimelineEntry) =
        let finish = entry.Offset + entry.Duration
        finite "position" finish
        stopFrame()
        match entry with
        | TimelineEntry.Numeric(_, child) -> child.Pause()
        | TimelineEntry.Nested(_, _, child) -> child.Pause()
        | _ -> ()
        children.Add entry
        duration <- Math.Max(duration, finish)
        match entry with
        | TimelineEntry.Callback _ when duration = 0. -> duration <- 1e-11
        | _ -> ()
        try
            render true sampledTime
            queue()
        with _ ->
            cancel()
            reraise()

    member private this.Contains(candidate: Timeline) =
        Object.ReferenceEquals(this, candidate) ||
        children.Exists(function TimelineEntry.Nested(_, _, child) -> child.Contains candidate | _ -> false)

    /// <summary>Resolves a position against the current duration, labels and previous child.</summary>
    member _.ResolvePosition(?position: TimelinePosition) =
        ensureLive()
        let previous =
            if children.Count = 0 then None
            else let entry = children[children.Count - 1] in Some(entry.Offset, entry.Offset + entry.Duration)
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
        addChild (TimelineEntry.Numeric(offset, child))
        this

    /// <summary>Pauses and borrows a nested controller at a position; synchronization cycles are rejected.</summary>
    /// <remarks>Captures the child's duration. Parent callback muting does not suppress the borrowed controller's callbacks.</remarks>
    member this.Sync(child: Timeline, ?position: TimelinePosition) =
        ensureLive()
        if isNull (box child) then nullArg "child"
        if child.Contains this then invalidArg "child" "Timeline synchronization cannot contain a cycle."
        let offset = this.ResolvePosition(?position = position)
        addChild (TimelineEntry.Nested(offset, child.Duration, child))
        this

    /// <summary>Adds a callback track at an explicit position or the current timeline end.</summary>
    /// <remarks>Callbacks run on forward/backward crossings. State changes precede user code to make reentrant seeks finite.</remarks>
    member this.Call(callback: unit -> unit, ?position: TimelinePosition) =
        ensureLive()
        if isNull (box callback) then nullArg "callback"
        let offset = this.ResolvePosition(?position = position) - 1e-11
        addChild (TimelineEntry.Callback(offset, callback, ref false))
        this

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
        Array.iter2(fun (child: NumericAnimation) offset -> this.Sync(child, TimelinePosition.At offset) |> ignore) children offsets
        this

    /// <summary>The scheduled duration in milliseconds.</summary>
    member _.Duration = duration

    /// <summary>The last sampled or paused time in milliseconds.</summary>
    member _.CurrentTime = elapsed

    /// <summary>Whether the scheduler is advancing this timeline.</summary>
    member _.Playing = playing

    /// <summary>Whether the latest sample reached the end.</summary>
    member _.Completed = completed

    /// <summary>Samples a millisecond position, optionally suppressing this controller's direct callbacks.</summary>
    /// <remarks>CurrentTime is clamped; negative samples cross signed offsets. Borrowed nested callbacks retain their own policy.</remarks>
    member _.Seek(milliseconds: float, ?muteCallbacks: bool) =
        seek (fun () -> true) (defaultArg muteCallbacks false) milliseconds

    /// <summary>Samples a borrowed controller while retaining every ancestor's final-write guard.</summary>
    member internal _.SeekGuarded(milliseconds: float, canWrite: unit -> bool) = seek canWrite false milliseconds

    /// <summary>Continues from the current time; completed timelines stay completed.</summary>
    member _.Play() =
        ensureLive()
        if not playing && not completed then
            playing <- true
            anchor <- scheduler.Now() - elapsed
            if duration = 0. then render false 0. else queue()

    /// <summary>Stops scheduled frames while retaining elapsed time.</summary>
    member _.Pause() =
        if playing then
            sampledTime <- Math.Min(scheduler.Now() - anchor, duration)
            elapsed <- Math.Clamp(sampledTime, 0., duration)
            playing <- false
            stopFrame()

    /// <summary>Pauses and rewinds children, suppressing and resetting direct callback tracks.</summary>
    member this.Reset() =
        this.Pause()
        let expected = generation + 1
        this.Seek(0., muteCallbacks = true)
        if generation = expected then
            for entry in children do
                match entry with TimelineEntry.Callback(_, _, state) -> state.Value <- false | _ -> ()

    /// <summary>Rewinds and starts the timeline.</summary>
    member this.Restart() =
        this.Reset()
        this.Play()

    /// <summary>Samples the end and stops scheduling frames.</summary>
    member this.Complete() = this.Seek duration

    /// <summary>Stops future timeline writes and releases borrowed child references.</summary>
    member _.Cancel() = cancel()

    interface IDisposable with member _.Dispose() = cancel()

and private TimelineEntry =
    | Numeric of offset: float * child: NumericAnimation
    | Nested of offset: float * duration: float * child: Timeline
    | Callback of offset: float * callback: (unit -> unit) * completed: bool ref

    member this.Offset =
        match this with Numeric(offset, _) | Nested(offset, _, _) | Callback(offset, _, _) -> offset

    member this.Duration =
        match this with Numeric(_, child) -> child.Duration | Nested(_, duration, _) -> duration | Callback _ -> 1e-11

/// <summary>Optional Ranvier ownership for borrowed-child timelines.</summary>
[<RequireQualifiedAccess>]
module Timeline =
    /// <summary>Registers terminal cancellation with the current Ranvier owner.</summary>
    let own (timeline: Timeline) =
        let owner = Ranvier.Api.getOwner()
        owner.Attach timeline
        timeline
