namespace Ranvier.View.Anime

#if !FABLE_COMPILER

open System
open Ranvier.Dom
open Ranvier.Anime

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

/// <summary>The further host services <c>Motion.drag</c> and <c>Motion.scroll</c> need: input geometry, resize observation and the viewport.</summary>
/// <remarks>
/// On WebCore: <c>{ Draggable = { Scheduler = ...; Document = host.Document; Computed = ...; Geometry = Geometry.forHost host; Resize =
/// ResizeObservation.forHost host; Media = MediaQueries.forHost host; Viewport = Viewport.forHost host; WindowScroll =
/// ViewportScroll.forHost host }; Viewport = Viewport.provider host }</c>.
/// </remarks>
[<NoEquality; NoComparison>]
type MotionInteractionServices =
    {
        /// <summary>The services a native draggable borrows; its scheduler and computed styles should match <c>MotionServices</c>.</summary>
        Draggable: DraggableNativeServices
        /// <summary>The window a scroll observer without a container follows.</summary>
        Viewport: ViewportProvider
    }

[<NoEquality; NoComparison>]
type internal InstalledMotion =
    {
        Services: MotionServices
        Interaction: (MotionInteractionServices * ScrollContainerRegistry) voption
    }

/// <summary>
/// The services Motion parts use on the current thread. Install them before mounting; a browser build needs no host.
/// </summary>
[<AbstractClass; Sealed>]
type MotionHost =
    [<ThreadStatic; DefaultValue>]
    static val mutable private current: InstalledMotion voption

    static member private Push(installed: InstalledMotion) : IDisposable =
        let previous = MotionHost.current
        MotionHost.current <- ValueSome installed
        let mutable restored = false

        { new IDisposable with
            member _.Dispose() =
                if not restored then
                    restored <- true
                    MotionHost.current <- previous
                    installed.Interaction |> ValueOption.iter (fun (_, registry) -> registry.Dispose()) }

    static member private Check(services: MotionServices) =
        if isNull (box services.Scheduler) then nullArg "services.Scheduler"
        if isNull (box services.Computed) then nullArg "services.Computed"

    /// <summary>Makes <c>services</c> current on this thread until the returned handle is disposed, which restores the previous services.</summary>
    static member Install(services: MotionServices) : IDisposable =
        MotionHost.Check services
        MotionHost.Push { Services = services; Interaction = ValueNone }

    /// <summary>Installs <c>services</c> with the interaction services that <c>Motion.drag</c> and <c>Motion.scroll</c> need.</summary>
    /// <remarks>The handle owns the scroll containers the parts share; disposing it releases them.</remarks>
    static member Install(services: MotionServices, interaction: MotionInteractionServices) : IDisposable =
        MotionHost.Check services
        let draggable = interaction.Draggable
        let registry = new ScrollContainerRegistry(services.Scheduler, services.Computed, draggable.Resize)
        MotionHost.Push { Services = services; Interaction = ValueSome (interaction, registry) }

    static member private Installed =
        match MotionHost.current with
        | ValueSome installed -> installed
        | ValueNone ->
            raise (InvalidOperationException "Motion parts need MotionHost.Install on the host thread before they are placed.")

    /// <summary>The services installed on this thread.</summary>
    /// <exception cref="T:System.InvalidOperationException">No services are installed on this thread.</exception>
    static member Current: MotionServices = MotionHost.Installed.Services

    static member internal Interaction: MotionInteractionServices * ScrollContainerRegistry =
        match MotionHost.Installed.Interaction with
        | ValueSome interaction -> interaction
        | ValueNone ->
            raise (InvalidOperationException "Motion.drag and Motion.scroll need MotionHost.Install with interaction services.")
#endif
