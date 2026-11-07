namespace Ranvier.Anime

open System
open System.Runtime.CompilerServices
open Ranvier.Dom

/// <summary>Time unit used by an Anime.js engine instance.</summary>
[<RequireQualifiedAccess; Diagnostics.DebuggerDisplay("{ToString(),nq}")>]
type EngineTimeUnit =
    /// <summary>Default 1.0 millisecond time scale.</summary>
    | Milliseconds
    /// <summary>0.001 second time scale.</summary>
    | Seconds
    /// <summary>The upstream string representation ("ms" or "s").</summary>
    override this.ToString() =
        match this with
        | Milliseconds -> "ms"
        | Seconds -> "s"
    /// <summary>Parses "ms" or "s"; unknown values raise ArgumentException.</summary>
    static member Parse(unitName: string) =
        if isNull unitName then nullArg "unitName"
        match unitName with
        | "ms" -> Milliseconds
        | "s" -> Seconds
        | _ -> invalidArg "unitName" ("Expected 'ms' or 's', got: " + unitName)

/// <summary>Public shared Anime.js engine controller bound to a host frame scheduler.</summary>
/// <remarks>
/// Repeated lookups for the same underlying host scheduler return the canonical <see cref="T:Ranvier.Anime.Engine"/>
/// instance without taking ownership of the caller's scheduler lifetime.
/// </remarks>
[<Sealed; Diagnostics.DebuggerDisplay("{ToString(),nq}")>]
type Engine internal (queue: EngineFrameQueue) =
    /// <summary>Whether the automatic main loop is enabled.</summary>
    member _.UseDefaultMainLoop
        with get() = queue.UseDefaultMainLoop
        and set value = queue.UseDefaultMainLoop <- value

    /// <summary>Whether attached document visibility listeners pause and resume the engine on hidden state changes.</summary>
    member _.PauseOnDocumentHidden
        with get() = queue.PauseOnDocumentHidden
        and set value = queue.PauseOnDocumentHidden <- value

    /// <summary>Whether the engine is currently paused.</summary>
    member _.Paused = queue.Paused

    /// <summary>Authored engine playback rate; changing speed reanchors active roots.</summary>
    member _.Speed
        with get() = queue.Speed
        and set value = queue.Speed <- value

    /// <summary>Finite frame-rate ceiling; higher values also raise subsequent playback defaults.</summary>
    member _.Fps
        with get() = queue.Clock.Fps
        and set value = queue.Clock.Fps <- value

    /// <summary>Whether time values use seconds (0.001 scale) rather than milliseconds.</summary>
    member _.Seconds
        with get() = queue.Seconds
        and set value =
            if queue.Seconds <> value then
                queue.Seconds <- value
                EngineDefaults.applyTimeScale value

    /// <summary>Time unit ("ms" or "s"); toggling scales default animation duration and active elapsed samples.</summary>
    member this.TimeUnit
        with get() = if queue.Seconds then EngineTimeUnit.Seconds else EngineTimeUnit.Milliseconds
        and set (value: EngineTimeUnit) =
            this.Seconds <- (value = EngineTimeUnit.Seconds)

    /// <summary>Default decimal rounding precision for interpolated CSS and compound values (None disables rounding).</summary>
    member _.Precision
        with get() = EngineDefaults.precision()
        and set value = EngineDefaults.setPrecision value

    /// <summary>Elapsed host milliseconds between the two most recent admitted engine frames.</summary>
    member _.DeltaTime = queue.Clock.Delta

    /// <summary>Retained host timestamp of the most recent admitted engine tick.</summary>
    member _.LastTickTime = queue.Clock.LastTick

    /// <summary>Forced-render jump threshold in the current time unit (200 ms or 0.2 s).</summary>
    member _.TickThreshold = queue.TickThreshold

    /// <summary>Global clock and constant animation defaults cell.</summary>
    member _.Defaults = ScopePlaybackContext.globalDefaults

    /// <summary>Manually runs one gated engine update using the host scheduler's current timestamp.</summary>
    member _.Update() = queue.Update()

    /// <summary>Pauses scheduled host frame requests while preserving active root playheads.</summary>
    member _.Pause() = queue.Pause()

    /// <summary>Reanchors active roots and resumes scheduled host frame requests.</summary>
    member _.Resume() = queue.Resume()

    /// <summary>Subscribes to a document's visibilitychange events to pause/resume while PauseOnDocumentHidden is enabled.</summary>
    member this.AttachVisibility(document: Document) : IDisposable =
        if isNull (box document) then nullArg "document"
        let mutable active = true
        let listener =
            { new EventListenerObject with
                member _.handleEvent _ =
                    if active && queue.PauseOnDocumentHidden then
                        if document.hidden then this.Pause() else this.Resume() }
        document.addEventListener("visibilitychange", listener)
        { new IDisposable with
            member _.Dispose() =
                if active then
                    active <- false
                    document.removeEventListener("visibilitychange", listener) }

    /// <summary>An invariant diagnostic of the engine state.</summary>
    override this.ToString() =
        let culture = Globalization.CultureInfo.InvariantCulture
        "Engine(TimeUnit=" + this.TimeUnit.ToString() + "; Speed=" + this.Speed.ToString("R", culture) +
        "; Fps=" + this.Fps.ToString("R", culture) + "; Paused=" + this.Paused.ToString() + ")"

/// <summary>Canonical lookup for shared host Anime.js engines.</summary>
[<RequireQualifiedAccess>]
module Engine =
    let private instances = ConditionalWeakTable<EngineFrameQueue, Engine>()

    /// <summary>Returns the canonical <see cref="T:Ranvier.Anime.Engine"/> associated with the supplied scheduler.</summary>
    let forScheduler (scheduler: FrameScheduler) : Engine =
        let queue = EngineFrames.forScheduler scheduler
        match instances.TryGetValue queue with
        | true, engine -> engine
        | _ -> instances.GetValue(queue, fun q -> Engine q)
