/// <summary>
/// An Elmish shell with routes: each route is an arm that crosses over with presence, pinned out of flow while it leaves;
/// each page runs its own Elmish loop; the shell's layout and its route indicator animate with every change. The browser
/// page routes on the location hash; a native window has none, so the tabs dispatch the route they name.
/// </summary>
module Routes

open global.Ranvier
open Ranvier.Anime
open Ranvier.Dom
open Ranvier.Elmish
open Ranvier.View
open Ranvier.View.Sugar
open Ranvier.View.Anime

type Route =
    | Home
    | Gallery
    | Settings
    | About

let routes = [| Home; Gallery; Settings; About |]

let nameOf (route: Route) =
    match route with
    | Home -> "home"
    | Gallery -> "gallery"
    | Settings -> "settings"
    | About -> "about"

let titleOf (route: Route) =
    match route with
    | Home -> "Home"
    | Gallery -> "Gallery"
    | Settings -> "Settings"
    | About -> "About"

/// <summary>The route a location hash such as <c>#/gallery</c> names; <c>Home</c> for any other hash.</summary>
let parse (hash: string) =
    let name = hash.TrimStart('#').TrimStart('/')

    routes
    |> Array.tryFind (fun route -> nameOf route = name)
    |> Option.defaultValue Home

let hashOf (route: Route) =
    "#/" + nameOf route

type Theme =
    | Daylight
    | Dusk

type ShellModel =
    {
        Route: Route
        Theme: Theme
        /// <summary>The number of route changes so far.</summary>
        Visits: int
    }

type ShellMsg =
    | Navigated of Route
    | InitialRoute of Route
    | ThemeChosen of Theme

let updateShell (msg: ShellMsg) (model: ShellModel) =
    match msg with
    | InitialRoute route -> { model with Route = route }
    | Navigated route when route <> model.Route ->
        { model with
            Route = route
            Visits = model.Visits + 1
        }
    | ThemeChosen theme -> { model with Theme = theme }
    | Navigated _ -> model

/// <summary>The shell's loop and the memos its view reads.</summary>
[<NoEquality; NoComparison>]
type Shell =
    {
        Loop: Mvu<ShellModel, ShellMsg>
        Route: Memo<Route>
        Theme: Memo<Theme>
        Visits: Memo<int>
    }

/// <summary>A shell at <c>route</c>, in the current graph's scope.</summary>
let createShell (route: Route) : Shell =
    let loop =
        Mvu.create
            {
                Route = route
                Theme = Daylight
                Visits = 0
            }
            updateShell

    {
        Loop = loop
        Route = loop.Select (fun model -> model.Route)
        Theme = loop.Select (fun model -> model.Theme)
        Visits = loop.Select (fun model -> model.Visits)
    }

// Home: a counter in its own loop, reset each time the page is entered.
type CounterMsg =
    | Increment
    | Decrement

[<NoEquality; NoComparison>]
type HomePage =
    {
        Count: Memo<int>
        Loop: Mvu<int, CounterMsg>
        Visits: Memo<int>
    }

let private home (shell: Shell) =
    Component.template
        (fun _ () ->
            let loop =
                Mvu.create 0 (fun msg count ->
                    match msg with
                    | Increment -> count + 1
                    | Decrement -> count - 1)

            {
                Count = loop.Select id
                Loop = loop
                Visits = shell.Visits
            })
        (div (Attrs.className "page-body") {
            h2 () { "A shell of arms" }

            p () {
                "Each page is an arm of the shell's route. Leaving pages are pinned where they were while they fade, so the next one takes their place at once."
            }

            div (Attrs.className "counter-row") {
                button (Attrs.className "decrement", Events.onClick (fun (h: HomePage) _ -> h.Loop.Dispatch Decrement)) { "−" }
                strong (Attrs.className "count") { fun (h: HomePage) -> string h.Count.Value }
                button (Attrs.className "increment", Events.onClick (fun (h: HomePage) _ -> h.Loop.Dispatch Increment)) { "+" }
            }

            p (Attrs.className "note") {
                fun (h: HomePage) ->
                    sprintf "This counter lives in the page's own loop and resets on every visit. Route changes so far: %d." h.Visits.Value
            }
        })

// Gallery: a filtered grid in its own loop; swatches enter and leave with the scale preset.
type Swatch = { Id: int; Name: string; Warm: bool }

let swatches =
    [|
        { Id = 1; Name = "Coral"; Warm = true }
        {
            Id = 2
            Name = "Indigo"
            Warm = false
        }
        { Id = 3; Name = "Amber"; Warm = true }
        { Id = 4; Name = "Mint"; Warm = false }
        { Id = 5; Name = "Rose"; Warm = true }
        { Id = 6; Name = "Sky"; Warm = false }
    |]

type Filter =
    | All
    | Warm
    | Cool

[<NoEquality; NoComparison>]
type GalleryPage =
    {
        Loop: Mvu<Filter, Filter>
        Shown: Memo<Swatch array>
        Filter: Memo<Filter>
    }

let shownBy (filter: Filter) =
    swatches
    |> Array.filter (fun swatch ->
        match filter with
        | All -> true
        | Warm -> swatch.Warm
        | Cool -> not swatch.Warm)

let private gallery =
    Component.template
        (fun _ () ->
            let loop = Mvu.create All (fun filter _ -> filter)

            {
                Loop = loop
                Shown = loop.Select shownBy
                Filter = loop.Select id
            })
        (div (Attrs.className "page-body") {
            h2 () { "Swatches" }

            div (Attrs.className "filters") {
                for filter, label in [ All, "All"; Warm, "Warm"; Cool, "Cool" ] do
                    button (
                        Attrs.className (fun (g: GalleryPage) -> if g.Filter.Value = filter then "chip active" else "chip"),
                        Attrs.data ("filter", label.ToLowerInvariant ()),
                        Events.onClick (fun (g: GalleryPage) _ -> g.Loop.Dispatch filter)
                    ) {
                        label
                    }
            }

            ul (Attrs.className "swatches") {
                forEach
                    (fun (swatch: Swatch) -> swatch.Id)
                    (fun (g: GalleryPage) -> g.Shown.Value)
                    (li (Attrs.className (fun (swatch: unit -> Swatch) -> "swatch " + (swatch ()).Name.ToLowerInvariant()), Motion.scale) {
                        fun (swatch: unit -> Swatch) -> (swatch ()).Name
                    })
            }
        })

// Settings: a theme picker in its own loop that reports each choice to the shell.
[<NoEquality; NoComparison>]
type SettingsPage =
    {
        Loop: Mvu<Theme, Theme>
        Theme: Memo<Theme>
    }

let private settings (shell: Shell) =
    Component.template
        (fun _ () ->
            let start = Api.untrack (fun () -> shell.Theme.Value)

            let loop =
                Mvu.withCmd (start, []) (fun theme _ -> theme, [ fun _ -> shell.Loop.Dispatch (ThemeChosen theme) ])

            { Loop = loop; Theme = loop.Select id })
        (div (Attrs.className "page-body") {
            h2 () { "Settings" }
            p () { "The picker keeps its own loop and reports each choice to the shell, which themes the whole frame." }

            div (Attrs.className "themes") {
                for theme, label in [ Daylight, "Daylight"; Dusk, "Dusk" ] do
                    button (
                        Attrs.className (fun (s: SettingsPage) -> if s.Theme.Value = theme then "chip active" else "chip"),
                        Attrs.data ("theme", label.ToLowerInvariant ()),
                        Events.onClick (fun (s: SettingsPage) _ -> s.Loop.Dispatch theme)
                    ) {
                        label
                    }
            }
        })

let private about =
    Component.template
        (fun _ () -> ())
        (div (Attrs.className "page-body") {
            h2 () { "About" }

            p () {
                "Each tab dispatches its route to the shell's loop. Switch quickly: every leaving page finishes its exit, and only the last page stays."
            }

            p (Attrs.className "note") { "Built with Ranvier's view sugar, Ranvier.Elmish and Ranvier.Anime." }
        })

let private arrive (_: unit -> unit) (element: Element) =
    Native.animate element (Native.timing 420. Native.outQuart) [| Native.fromTo "opacity" 0. 1.; Native.fromTo "y" 24. 0. |]

let private depart (_: unit -> unit) (element: Element) =
    Native.animate element (Native.timing 260. Native.inQuad) [| Native.toward "opacity" 0.; Native.toward "y" -16. |]

let private page (route: Route) (content: TemplateView<unit -> unit>) =
    armOf (section (Attrs.className "page", Attrs.data ("route", nameOf route), Motion.presence arrive depart) { content })

/// <summary>The shell's component over <c>shell</c>; build it once per shell.</summary>
let view (shell: Shell) : Component<unit> =
    let homeArm = page Home (renderOf (home shell) ())
    let galleryArm = page Gallery (renderOf gallery ())
    let settingsArm = page Settings (renderOf (settings shell) ())
    let aboutArm = page About (renderOf about ())

    Component.template
        (fun _ () -> shell)
        (div (Attrs.className "shell", Attrs.data ("theme", fun (s: Shell) -> Some (if s.Theme.Value = Dusk then "dusk" else "daylight"))) {
            nav (Attrs.className "tabs") {
                for route in routes do
                    a (
                        Attrs.attribute ("href", hashOf route),
                        Events.onClick (fun (s: Shell) _ -> s.Loop.Dispatch (Navigated route)),
                        Attrs.className (fun (s: Shell) -> if s.Route.Value = route then "tab active" else "tab"),
                        Attrs.data ("route", nameOf route)
                    ) {
                        titleOf route
                    }

                // The indicator is as wide as a tab at every breakpoint, so its offset is the route's index in widths of its own:
                // anime's x in % of the element, here in the pixels of its measured width.
                span (
                    Attrs.className "indicator",
                    Motion.follow "x" { ComposedAnimationOptions.Default with Duration = 420.; Ease = Native.outBack } (fun (s: Shell) ->
                        float (Array.findIndex ((=) s.Route.Value) routes)
                        * (Native.select ".indicator" |> Option.map (box >> Native.widthOf) |> Option.defaultValue 0.))
                ) {
                    ()
                }
            }

            main (Attrs.className "stage", Motion.flip { LayoutOptions.Default.WithDuration 380u with Ease = Native.outQuart }) {
                matchWith (fun (s: Shell) -> s.Route.Value) (function
                    | Home -> arm homeArm ()
                    | Gallery -> arm galleryArm ()
                    | Settings -> arm settingsArm ()
                    | About -> arm aboutArm ())

                footer (Attrs.className "shell-foot") { fun (s: Shell) -> sprintf "%s · %d route changes" (titleOf s.Route.Value) s.Visits.Value }
            }
        })

