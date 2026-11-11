/// <summary>
/// The native app's frame, in place of the browser example's separate documents: the site header with its page
/// navigation, each page's introduction, and the selected page, mounted in an arm that owns its state and timers.
/// </summary>
module Shell

open System
open System.IO
open Ranvier
open Ranvier.View
open Ranvier.View.Sugar
open Ranvier.Anime
open Ranvier.View.Anime

type Info =
    {
        Name: string
        Title: string
        Tag: string
        Description: string
    }

/// <summary>The pages, in navigation order, with the browser example's words.</summary>
let infos =
    [|
        {
            Name = "kanban"
            Title = "Kanban"
            Tag = "Elmish · presence · flip · drag"
            Description =
                "Cards enter and leave with presence, glide between lanes with FLIP, and drop into the lane you drag them to. Every change goes through one Elmish loop, so undo is a list of earlier boards."
        }
        {
            Name = "dashboard"
            Title = "Dashboard"
            Tag = "stream · follow · draw · track · boundary"
            Description =
                "Metrics arrive from a core stream. Gauges follow each reading, the ring draws the CPU load, and the chart scrolls steadily while its tip eases to the next value. Reconnect keeps the sample history."
        }
        {
            Name = "story"
            Title = "Story"
            Tag = "scroll · split · stagger · appear"
            Description =
                "A scroll-driven landing page. Scroll timelines write their progress into signals, the headline is split into letters, and the gallery staggers out from its centre."
        }
        {
            Name = "physics"
            Title = "Physics"
            Tag = "drag · spring · follow · scrub · policy"
            Description =
                "Throw the puck and a spring settles it; five followers trail it, each a step slower. The slider seeks a timeline, and the switch writes the graph's motion policy."
        }
        {
            Name = "graphs"
            Title = "Graphs"
            Tag = "follow · viewBox · pointer · timeline · stagger"
            Description =
                "Inspect a signal with a following crosshair and tooltip, zoom and pan through the day, then play a ranked bar chart whose rows and values animate together."
        }
        {
            Name = "routes"
            Title = "Routes"
            Tag = "Elmish · matchWith · presence · flip"
            Description =
                "An Elmish shell routes between pages. Every page is an arm that crosses over with presence, pinned in place while it leaves, and runs its own loop; the shell's layout and its tab indicator follow each change."
        }
    |]

/// <summary>The state of the page on screen, for the smoke checks; each page writes its own once created.</summary>
[<AllowNullLiteral>]
type Live() =
    member val Board: Kanban.Board option = None with get, set
    member val Store: Dashboard.Store option = None with get, set
    member val Pump: Dashboard.Pump option = None with get, set
    member val Story: Story.Story option = None with get, set
    member val Playground: Physics.Playground option = None with get, set
    member val Graphs: Graphs.Store option = None with get, set
    member val Routes: Routes.Shell option = None with get, set

// Creates a page's state in its arm's scope and builds its component over it; `activate` runs once it is placed, and
// its result runs when the arm leaves: PageLayout.pageView of the browser example.
let private pageView (create: unit -> 'state) (view: 'state -> Component<unit>) (activate: Graph -> 'state -> IDisposable) =
    Component.template
        (fun _ () ->
            let state = create ()
            let nested = view state
            let selected = Component.template (fun _ (_: unit -> unit) -> ()) (renderOf nested ())
            state, selected)
        (div (
            Hooks.mounted (fun (state, _) _ ->
                let owned = activate Graph.Current state
                fun () -> owned.Dispose ())
        ) {
            matchWith snd (fun selected -> arm selected ())
        })

let private nothing = { new IDisposable with member _.Dispose() = () }

let private published (create: ('state -> unit) -> Component<unit>) (keep: 'state -> unit) =
    let captured = ref Unchecked.defaultof<'state>

    pageView
        (fun () -> captured)
        (fun state -> create (fun created -> state.Value <- created))
        (fun _ state ->
            keep state.Value
            nothing)

let private pages (live: Live) : (string * Component<unit>) array =
    [|
        "kanban",
        pageView
            (fun () -> Kanban.createBoard Kanban.init)
            Kanban.view
            (fun _ board ->
                live.Board <- Some board
                nothing)
        "dashboard",
        pageView Dashboard.createStore Dashboard.view (fun graph store ->
            let pump, timers = Dashboard.activate graph store
            live.Store <- Some store
            live.Pump <- Some pump
            timers)
        "story",
        published
            (fun publish -> Component.template (fun _ () -> ()) (render Story.view (fun () -> publish)))
            (fun story -> live.Story <- Some story)
        "physics",
        pageView
            (fun () -> ref Unchecked.defaultof<Physics.Playground>)
            (fun state -> Component.template (fun _ () -> ()) (render Physics.view (fun () -> fun created -> state.Value <- created)))
            (fun graph state ->
                live.Playground <- Some state.Value

                // The browser page starts from the reduced-motion media query on every load; the native graph outlives
                // the page, so leaving it restores the full policy.
                { new IDisposable with
                    member _.Dispose() = graph.Run (fun () -> (Reactive.policy ()).Value <- Reactive.Full) })
        "graphs",
        pageView Graphs.createStore Graphs.view (fun graph store ->
            live.Graphs <- Some store
            Graphs.activate graph store)
        "routes",
        pageView
            (fun () -> Routes.createShell Routes.Home)
            Routes.view
            (fun _ shell ->
                live.Routes <- Some shell
                nothing)
    |]

/// <summary>The stylesheets of a page: the shared look, then the page's own.</summary>
let stylesOf (directory: string) (name: string) =
    File.ReadAllText (Path.Combine (directory, "motion.css"))
    + "\n"
    + File.ReadAllText (Path.Combine (directory, name + ".css"))

/// <summary>The app's state: the page on screen and the hook that prepares the window for a page before it renders.</summary>
[<NoEquality; NoComparison>]
type App =
    {
        Current: Signal<string>
        /// <summary>Loads the page's stylesheets and scrolls the window to its top, as a browser page load does.</summary>
        Prepare: string -> unit
        Live: Live
    }

    /// <summary>Shows the page named <c>name</c> in a window prepared for it.</summary>
    member app.Open(name: string) =
        if Api.untrack (fun () -> app.Current.Value) <> name then
            app.Prepare name
            app.Current.Value <- name

let private info (name: string) = infos |> Array.find (fun entry -> entry.Name = name)

/// <summary>The app's component over <c>app</c>.</summary>
let view (app: App) : Component<unit> =
    let arms = pages app.Live |> Array.map (fun (name, page) -> name, armOf (renderOf page ()))

    Component.template
        (fun _ () -> app)
        (div (Attrs.className "frame") {
            header (Attrs.className "site-header") {
                a (Attrs.className "brand") {
                    span (Attrs.className "brand-mark") { () }
                    "Ranvier Motion"
                }

                nav () {
                    for entry in infos do
                        a (
                            Attrs.data ("page", entry.Name),
                            Attrs.attribute ("aria-current", fun (a: App) -> if a.Current.Value = entry.Name then Some "page" else None),
                            Events.onClick (fun (a: App) _ -> a.Open entry.Name)
                        ) {
                            entry.Title
                        }
                }
            }

            section (Attrs.className "intro") {
                span (Attrs.className "tag") { fun (a: App) -> (info a.Current.Value).Tag }
                h1 () { fun (a: App) -> (info a.Current.Value).Title }
                p () { fun (a: App) -> (info a.Current.Value).Description }
            }

            matchWith (fun (a: App) -> a.Current.Value) (fun name -> arm (arms |> Array.find (fun (key, _) -> key = name) |> snd) ())
        })
