namespace Ranvier.View.Anime

open System
open Ranvier.Dom

/// <summary>The host services Motion parts animate with: the page's frame scheduler and computed styles.</summary>
/// <remarks>On WebCore: <c>{ Scheduler = Frames.forHost host; Computed = ComputedStyles.forHost host }</c>.</remarks>
[<NoEquality; NoComparison>]
type MotionServices =
    {
        /// <summary>The frame scheduler of the page the parts' elements belong to.</summary>
        Scheduler: FrameScheduler
        /// <summary>Reads the starting values of the CSS properties the parts animate.</summary>
        Computed: ComputedStyleService
    }

/// <summary>
/// The services Motion parts use on the current thread. Install them before mounting; a browser build needs no host.
/// </summary>
[<AbstractClass; Sealed>]
type MotionHost =
    [<ThreadStatic; DefaultValue>]
    static val mutable private current: MotionServices voption

    /// <summary>Makes <c>services</c> current on this thread until the returned handle is disposed, which restores the previous services.</summary>
    static member Install(services: MotionServices) : IDisposable =
        if isNull (box services.Scheduler) then nullArg "services.Scheduler"
        if isNull (box services.Computed) then nullArg "services.Computed"
        let previous = MotionHost.current
        MotionHost.current <- ValueSome services
        let mutable restored = false

        { new IDisposable with
            member _.Dispose() =
                if not restored then
                    restored <- true
                    MotionHost.current <- previous }

    /// <summary>The services installed on this thread.</summary>
    /// <exception cref="T:System.InvalidOperationException">No services are installed on this thread.</exception>
    static member Current: MotionServices =
        match MotionHost.current with
        | ValueSome services -> services
        | ValueNone ->
            raise (InvalidOperationException "Motion parts need MotionHost.Install on the host thread before they are placed.")
