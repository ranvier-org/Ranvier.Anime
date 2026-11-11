namespace Ranvier.Anime

/// <summary>An animation object that can restore the values it changed.</summary>
[<AllowNullLiteral>]
type IRevertible =
    /// <summary>Restores the values the object changed and stops it.</summary>
    abstract Revert: unit -> unit

/// <summary>The playback controls shared by timers, timelines and composed animations.</summary>
/// <remarks>View integrations drive enter, exit and slot animations through this contract.</remarks>
[<AllowNullLiteral>]
type ITickable =
    inherit IRevertible
    /// <summary>Whether playback reached its end.</summary>
    abstract Completed: bool
    /// <summary>Whether playback was cancelled.</summary>
    abstract Cancelled: bool
    /// <summary>Stops scheduled frames at the current values.</summary>
    abstract Pause: unit -> unit
    /// <summary>Ends playback at the current values; completion callbacks do not run.</summary>
    abstract Cancel: unit -> unit
    /// <summary>Seeks to the end, running completion callbacks.</summary>
    abstract Complete: unit -> unit
    /// <summary>Runs the callback once playback completes, at once when it already has; a cancel drops it.</summary>
    abstract WhenCompleted: (unit -> unit) -> unit
