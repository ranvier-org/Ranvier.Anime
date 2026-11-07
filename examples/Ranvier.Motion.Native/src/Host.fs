/// <summary>Mounting the app on a WebCore host and turning its window loop.</summary>
module Host

open System
open System.IO
open Ranvier
open Ranvier.View
open Ranvier.View.Dom
open Ranvier.Anime
open Ranvier.View.Anime
open Ranvier.View.WebCore
open Ranvier.View.WebCore.Sdl

/// <summary>A mounted app and everything it owns on its host.</summary>
[<NoEquality; NoComparison>]
type Running =
    {
        Graph: Graph
        App: Shell.App
        Errors: ResizeArray<exn>
        Owned: IDisposable
    }

    interface IDisposable with
        member this.Dispose() = this.Owned.Dispose()

let private stylesDirectory = Path.Combine (AppContext.BaseDirectory, "styles")

/// <summary>Installs the Motion services on <c>host</c> and mounts the app on its page at the page named <c>start</c>.</summary>
let mount (host: WebCoreHost) (start: string) : Running =
    let document = host.Document
    Native.document.Value <- document
    let scheduler = Frames.forHost host
    let computed = ComputedStyles.forHost host
    let windowScroll = ViewportScroll.forHost host

    let motion =
        MotionHost.Install (
            { Scheduler = scheduler; Computed = computed },
            {
                Draggable =
                    {
                        Scheduler = scheduler
                        Document = document
                        Computed = computed
                        Geometry = Geometry.forHost host
                        Resize = ResizeObservation.forHost host
                        Media = MediaQueries.forHost host
                        Viewport = Viewport.forHost host
                        WindowScroll = windowScroll
                    }
                Viewport = Viewport.provider host
            }
        )

    let style = document.createElement "style"
    document.body.appendChild style |> ignore
    let container = document.createElement "div"
    document.body.appendChild container |> ignore
    let graph = new Graph ()
    let errors = ResizeArray<exn> ()

    let app: Shell.App =
        graph.Run (fun () ->
            {
                Current = Api.createSignal start
                Prepare =
                    fun name ->
                        (style :> Ranvier.Dom.Node).textContent <- Shell.stylesOf stylesDirectory name
                        windowScroll.ScrollTo { Left = Some 0.; Top = Some 0.; Behavior = Ranvier.Dom.ScrollBehavior.Instant }
                Live = Shell.Live ()
            })

    app.Prepare start

    let report (failures: exn list) =
        for failure in failures do
            errors.Add failure
            eprintfn "Ranvier Motion: %s" (failure.ToString ())

    let mounted =
        Mounting.mount graph None report container None (fun context -> Program.run (Component.render (Shell.view app) ()) context.Backend)

    {
        Graph = graph
        App = app
        Errors = errors
        Owned =
            { new IDisposable with
                member _.Dispose() =
                    graph.Run (fun () -> mounted.Dispose ())
                    graph.Dispose ()
                    motion.Dispose ()
                    scheduler.Dispose () }
    }

/// <summary>SDL's renderer for a window: direct3d11 where the native runtime presents shared D3D11 frames, SDL's choice elsewhere.</summary>
let renderer = if WebCoreHost.Has "presentation-d3d11" then Some "direct3d11" else None

/// <summary>One turn of the window loop: SDL events, the page's frame, then the timers now due.</summary>
let step (window: Window) =
    let wait =
        match Native.timers.NextDelay with
        | Some delay -> min 50 (int (Math.Ceiling delay))
        | None -> 50

    let open' = window.Step (timeout = wait)
    Native.timers.Run ()
    open'
