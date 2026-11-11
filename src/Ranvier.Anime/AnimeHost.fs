namespace Ranvier.Anime

open System
open Ranvier.Dom

/// <summary>The DOM types of the engine, under names that read the same against either Ranvier.Anime package.</summary>
/// <remarks>Portable code takes and passes these and calls no members on them; DOM work goes through <c>DomKit</c>.</remarks>
module Dom =
    /// <summary>A DOM node.</summary>
    type Node = Ranvier.Dom.Node
    /// <summary>A DOM element.</summary>
    type Element = Ranvier.Dom.Element
    /// <summary>An HTML element.</summary>
    type HTMLElement = Ranvier.Dom.HTMLElement

/// <summary>The host services the portable functions animate with: the page's frame scheduler and computed styles.</summary>
/// <remarks>On WebCore: <c>{ Scheduler = Frames.forHost host; Computed = ComputedStyles.forHost host }</c>.</remarks>
[<NoEquality; NoComparison>]
type AnimeServices =
    {
        /// <summary>The frame scheduler of the page the animated elements belong to.</summary>
        Scheduler: FrameScheduler
        /// <summary>Reads the starting values of the CSS properties the animations change.</summary>
        Computed: ComputedStyleService
    }

/// <summary>The further host services <c>Drag.create</c> and <c>Scroll.observe</c> need: input geometry, resize observation and the viewport.</summary>
/// <remarks>
/// On WebCore: <c>{ Draggable = { Scheduler = ...; Document = host.Document; Computed = ...; Geometry = Geometry.forHost host; Resize =
/// ResizeObservation.forHost host; Media = MediaQueries.forHost host; Viewport = Viewport.forHost host; WindowScroll =
/// ViewportScroll.forHost host }; Viewport = Viewport.provider host }</c>.
/// </remarks>
[<NoEquality; NoComparison>]
type AnimeInteractionServices =
    {
        /// <summary>The services a native draggable borrows; its scheduler and computed styles should match <c>AnimeServices</c>.</summary>
        Draggable: DraggableNativeServices
        /// <summary>The window a scroll observer without a container follows.</summary>
        Viewport: ViewportProvider
    }

[<NoEquality; NoComparison>]
type internal InstalledAnime =
    {
        Services: AnimeServices
        Interaction: (AnimeInteractionServices * ScrollContainerRegistry) voption
    }

/// <summary>
/// The services the portable functions use on the current thread. Install them before animating; the Fable package
/// needs no host.
/// </summary>
[<AbstractClass; Sealed>]
type AnimeHost =
    [<ThreadStatic; DefaultValue>]
    static val mutable private current: InstalledAnime voption

    static member private Push(installed: InstalledAnime) : IDisposable =
        let previous = AnimeHost.current
        AnimeHost.current <- ValueSome installed
        let mutable restored = false

        { new IDisposable with
            member _.Dispose() =
                if not restored then
                    restored <- true
                    AnimeHost.current <- previous
                    installed.Interaction |> ValueOption.iter (fun (_, registry) -> registry.Dispose()) }

    static member private Check(services: AnimeServices) =
        if isNull (box services.Scheduler) then nullArg "services.Scheduler"
        if isNull (box services.Computed) then nullArg "services.Computed"

    /// <summary>Makes <c>services</c> current on this thread until the returned handle is disposed, which restores the previous services.</summary>
    static member Install(services: AnimeServices) : IDisposable =
        AnimeHost.Check services
        AnimeHost.Push { Services = services; Interaction = ValueNone }

    /// <summary>Installs <c>services</c> with the interaction services that <c>Drag.create</c> and <c>Scroll.observe</c> need.</summary>
    /// <remarks>The handle owns the scroll containers the observers share; disposing it releases them.</remarks>
    static member Install(services: AnimeServices, interaction: AnimeInteractionServices) : IDisposable =
        AnimeHost.Check services
        let draggable = interaction.Draggable
        let registry = new ScrollContainerRegistry(services.Scheduler, services.Computed, draggable.Resize)
        AnimeHost.Push { Services = services; Interaction = ValueSome (interaction, registry) }

    static member private Installed =
        match AnimeHost.current with
        | ValueSome installed -> installed
        | ValueNone ->
            raise (InvalidOperationException "Ranvier.Anime needs AnimeHost.Install on the host thread before its portable functions are called.")

    /// <summary>The services installed on this thread.</summary>
    /// <exception cref="T:System.InvalidOperationException">No services are installed on this thread.</exception>
    static member Current: AnimeServices = AnimeHost.Installed.Services

    static member internal Interaction: AnimeInteractionServices * ScrollContainerRegistry =
        match AnimeHost.Installed.Interaction with
        | ValueSome interaction -> interaction
        | ValueNone ->
            raise (InvalidOperationException "Drag.create and Scroll.observe need AnimeHost.Install with interaction services.")
