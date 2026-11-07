/// <summary>
/// The app's behaviour checks: every page opened in a hidden window and driven through its host's real mouse, wheel and
/// frame loop, against the behaviour the browser example's checks require of it.
/// </summary>
module Smoke

open System
open System.Diagnostics
open System.Globalization
open System.Runtime.InteropServices
open Ranvier
open Ranvier.Dom
open Ranvier.Anime
open Ranvier.View.WebCore
open Ranvier.View.WebCore.Sdl

let mutable private passed = 0

let private require (name: string) (condition: bool) =
    if not condition then failwith ("FAIL " + name)
    passed <- passed + 1
    Console.WriteLine ("PASS " + name)

[<NoEquality; NoComparison>]
type private Driver =
    {
        Window: Window
        Host: WebCoreHost
        Running: Host.Running
    }

    member d.Document = d.Host.Document

    /// <summary>Turns the loop for <c>milliseconds</c> of wall time.</summary>
    member d.Pump(milliseconds: float) =
        let watch = Stopwatch.StartNew ()

        while watch.Elapsed.TotalMilliseconds < milliseconds do
            Host.step d.Window |> ignore

    /// <summary>Turns the loop until <c>condition</c> holds, for at most three seconds; whether it held.</summary>
    member d.Until(condition: unit -> bool) =
        let watch = Stopwatch.StartNew ()

        while not (condition ()) && watch.ElapsedMilliseconds < 3000L do
            Host.step d.Window |> ignore

        condition ()

    member d.All(selector: string) = Native.queryAll d.Document.documentElement selector

    member d.One(selector: string) =
        match d.All selector with
        | [||] -> failwith ("No element matches " + selector)
        | found -> found[0]

    member d.Count(selector: string) = (d.All selector).Length

    member d.Mouse(phase: int, x: float, y: float) =
        d.Host.Mouse (phase, int (Math.Round x), int (Math.Round y), (if phase = 0 then -1 else 0), (if phase = 0 then 0 else 1), 0)
        |> ignore

    member d.Center(element: Element) =
        let rect = element.getBoundingClientRect ()
        rect.left + rect.width / 2., rect.top + rect.height / 2.

    /// <summary>Scrolls the element into the window, then presses and releases the primary button over its centre.</summary>
    member d.Click(element: Element) =
        element.scrollIntoView ()
        d.Host.Frame ()
        d.Pump 30.
        let x, y = d.Center element
        d.Mouse (0, x, y)
        d.Pump 20.
        d.Mouse (1, x, y)
        d.Mouse (2, x, y)
        d.Pump 30.

    member d.ClickText(selector: string, text: string) =
        match d.All selector |> Array.tryFind (fun element -> (element :> Node).textContent.Trim () = text) with
        | Some element -> d.Click element
        | None -> failwith ("No " + selector + " reads " + text)

    /// <summary>Presses over <c>element</c>'s centre, moves by <c>(dx, dy)</c> in steps, and releases.</summary>
    member d.Drag(element: Element, dx: float, dy: float) =
        let x, y = d.Center element
        d.Mouse (0, x, y)
        d.Mouse (1, x, y)
        d.Pump 20.

        for step in 1..12 do
            let t = float step / 12.
            d.Mouse (0, x + dx * t, y + dy * t)
            d.Pump 16.

        d.Mouse (2, x + dx, y + dy)
        d.Pump 20.

    member d.Open(name: string) =
        d.Running.Graph.Run (fun () -> d.Running.App.Open name)
        d.Pump 60.

    member d.Write(action: unit -> unit) =
        d.Running.Graph.Run action
        d.Pump 20.

    member d.Read(read: unit -> 'v) =
        d.Running.Graph.Run (fun () -> Api.untrack read)

let private style (element: Element) (property: string) =
    (unbox<ElementCSSInlineStyle> (box element)).style.getPropertyValue property

let private text (element: Element) = (element :> Node).textContent.Trim ()

let private query (root: Element) (selector: string) = Native.query root selector

let private number (value: string) =
    let digits = String (value |> Seq.takeWhile (fun c -> Char.IsDigit c || c = '.' || c = '-') |> Array.ofSeq)

    match Double.TryParse (digits, NumberStyles.Float, CultureInfo.InvariantCulture) with
    | true, parsed -> parsed
    | _ -> nan

let private opened (d: Driver) =
    for entry in Shell.infos do
        d.Open entry.Name
        d.Pump 400.

        require
            (entry.Name + " opens with its introduction")
            (text (d.One ".intro h1") = entry.Title
             && (d.One (".site-header nav a[data-page='" + entry.Name + "']")).getAttribute "aria-current" = "page")

let private near (expected: float) (actual: float) = abs (expected - actual) < 1.

let private kanban (d: Driver) =
    d.Open "kanban"
    let board = d.Running.App.Live.Board.Value
    let card (id: int) = d.All "li.card-slot" |> Array.find (fun element -> element.getAttribute "data-id" = string id)
    let has (id: int) = d.All "li.card-slot" |> Array.exists (fun element -> element.getAttribute "data-id" = string id)
    let laneOf (id: int) = (card id).getAttribute "data-lane"
    let counts () = d.All ".lane-head .count" |> Array.map text
    let home (id: int) = let t = style (card id) "transform" in t = "" || t.StartsWith "translate(0px, 0px)"

    require "kanban: five cards over three lanes" (d.Count "li.card-slot" = 5 && counts () = [| "2"; "2"; "1" |])
    d.ClickText ("button", "Add card")
    require "kanban: Add card places a sixth card" (has 6 && text (d.One ".toolbar .meta") = "6 cards")
    require "kanban: the new card enters with presence" (number (style (card 6) "opacity") < 1.)
    require "kanban: the entering card settles opaque" (d.Until (fun () -> style (card 6) "opacity" = "1"))
    d.Click (query (card 6) ".delete")
    require "kanban: a deleted card stays while it leaves" (has 6 && d.Read (fun () -> board.Total.Value) = 5)
    require "kanban: the deleted card is removed once its exit ends" (d.Until (fun () -> not (has 6)))
    d.Click (query (card 1) ".right")
    require "kanban: Move right moves the card a lane" (laneOf 1 = "1" && counts () = [| "1"; "3"; "1" |])
    require "kanban: the board flips its cards to their new places" ((d.One "ul.board").classList.contains "is-animated")
    require "kanban: the flip settles" (d.Until (fun () -> not ((d.One "ul.board").classList.contains "is-animated")))
    d.ClickText ("button", "Undo")
    require "kanban: Undo restores the earlier board" (laneOf 1 = "0" && counts () = [| "2"; "2"; "1" |])
    d.Pump 500.
    let lane = Native.widthOf (d.One "ul.board") / 3.
    d.Drag (card 2, lane, 10.)
    require "kanban: a card dragged a lane over drops into that lane" (d.Until (fun () -> laneOf 2 = "1"))
    d.Pump 500.
    d.Drag (card 3, 24., 12.)
    require "kanban: a short drag keeps the lane" (laneOf 3 = "1")
    require "kanban: the card springs home" (d.Until (fun () -> home 3))
    d.ClickText ("button", "Clear")
    require "kanban: Clear empties the board after the exits" (d.Until (fun () -> d.Count "li.card-slot" = 0) && d.Count "p.empty" = 1)

let private dashboard (d: Driver) =
    d.Open "dashboard"
    require "dashboard: a skeleton shows while the stream is pending" (d.Count ".skeleton" = 1 && d.Count ".metrics" = 0)
    let store = d.Running.App.Live.Store.Value
    let pump = d.Running.App.Live.Pump.Value
    d.Write (fun () -> store.Running.Value <- false)
    pump.Push Dashboard.sampleAt
    d.Pump 60.
    require "dashboard: the first sample reveals the metrics" (d.Count ".metrics" = 1 && d.Count ".skeleton" = 0)
    require "dashboard: gauges follow the reading" (d.Until (fun () -> (style (d.One ".gauge-fill") "transform").Contains "scaleX("))
    require "dashboard: the ring draws the CPU load" (not (isNull ((d.One ".ring-value").getAttribute "stroke-dasharray")))
    pump.Push Dashboard.sampleAt
    let second = Dashboard.sampleAt 2
    require "dashboard: the counter counts up to the latest requests" (d.Until (fun () -> text (d.One ".counter") = sprintf "%.0f" second.Requests))
    require "dashboard: the chart holds both samples" (text (d.One ".chart-head .note") = "2 of 24")
    pump.Push Dashboard.spikeAt
    require "dashboard: a CPU spike raises the alert" (d.Until (fun () -> d.Count ".alert" = 1))
    d.ClickText ("button", "Show details")
    require "dashboard: Show details fades the panel in" (d.Count "aside.details" = 1)
    d.ClickText ("button", "Resume")
    let before = text (d.One ".tile.requests .note")
    require "dashboard: the window loop's timer feeds a sample a second" (d.Until (fun () -> text (d.One ".tile.requests .note") <> before))
    d.ClickText ("button", "Pause")
    let paused = text (d.One ".tile.requests .note")
    d.Pump 1300.
    require "dashboard: Pause stops the feed" (text (d.One ".tile.requests .note") = paused && text (d.One ".toolbar .meta") = "paused")
    let held = text (d.One ".chart-head .note")
    d.ClickText ("button", "Reconnect")
    d.Pump 100.
    require "dashboard: Reconnect keeps the sample history" (text (d.One ".chart-head .note") = held)

let private story (d: Driver) =
    d.Open "story"
    let story = d.Running.App.Live.Story.Value
    require "story: the headline is split into letters" ((d.All ".headline [data-char]").Length >= 20)
    require "story: the letters stagger in" (d.Until (fun () -> d.All ".headline [data-char]" |> Array.forall (fun c -> style c "opacity" = "1")))
    let progress () = d.Read (fun () -> story.Chapters |> Array.map (fun chapter -> chapter.Progress.Value))
    require "story: the last chapter starts below the window" ((progress ()).[2] = 0.)
    (d.All "section.chapter").[2].scrollIntoView ()
    require "story: scrolling writes the chapter's progress" (d.Until (fun () -> (progress ()).[2] > 0.))
    require "story: the rail reads the progress" (d.All ".rail-value" |> Array.exists (fun value -> text value <> "0%"))
    require "story: a chapter between its thresholds is seen" (d.Until (fun () -> d.Count ".rail-item.seen" > 0))
    d.ClickText ("button", "Show the extras")
    require "story: the extras appear" (d.Count ".tile.extra" = 4)
    require "story: the extras pop in" (d.Until (fun () -> d.All ".tile.extra" |> Array.forall (fun tile -> style tile "opacity" = "1")))

let private physics (d: Driver) =
    d.Open "physics"
    let playground = d.Running.App.Live.Playground.Value
    let puck () = d.Read (fun () -> playground.Puck.Value)
    let follower () = style (d.One "[data-follower=\"0\"]") "transform"
    d.Drag (d.One ".puck", 120., 40.)
    require "physics: dragging moves the puck" (let struct (x, y) = puck () in x > 40. && y > 10.)
    require "physics: release clears dragging" (d.Until (fun () -> not (d.Read (fun () -> playground.Dragging.Value))))
    require "physics: followers trail the puck" (d.Until (fun () -> (follower ()).StartsWith "translate" && not ((follower ()).StartsWith "translateX(0px)")))
    d.Pump 1500.
    d.ClickText ("button", "Spring home")
    require "physics: Spring home springs the puck back" (d.Until (fun () -> let struct (x, y) = puck () in near 0. x && near 0. y))
    require "physics: the readout reads the puck" (d.Until (fun () -> text (d.One ".controls .readout") = "x 0 · y 0"))
    let dot () = style (d.One "[data-dot=\"1\"]") "transform"
    let resting = dot ()
    d.Write (fun () -> playground.Scrub.Value <- 0.5)
    require "physics: the slider seeks the timeline" (dot () <> resting && text (d.One ".scrubber .readout") = "50%")
    d.Write (fun () -> playground.Scrub.Value <- 0.)
    require "physics: seeking back restores the dots" (dot () = resting || (dot ()).Contains "translateY(0px)")
    d.Click (d.One "input.reduced")
    require "physics: the switch writes the reduced policy" (d.Read (fun () -> playground.Policy.Value) = Reactive.Reduced)
    d.Write (fun () -> playground.Puck.Value <- struct (60., 30.))
    d.ClickText ("button", "Spring home")
    require "physics: under reduced motion the puck jumps home" (puck () = struct (0., 0.))
    d.ClickText ("button", "Show tips")
    require "physics: under reduced motion the tips appear without their rise" (d.Count "aside.tips" = 1 && style (d.One "aside.tips") "opacity" = "")

let private graphs (d: Driver) =
    d.Open "graphs"
    let store = d.Running.App.Live.Graphs.Value
    let plot () = d.One ".explorer-svg"

    let span () =
        match ((plot ()).getAttribute "viewBox").Split ' ' with
        | [| _; _; width; _ |] -> number width
        | _ -> nan

    require "graphs: the plot shows the whole day" (near 960. (span ()))
    d.ClickText ("button", "Zoom in")
    require "graphs: Zoom in animates the viewBox" (d.Until (fun () -> near 624. (span ())))
    d.ClickText ("button", "Reset view")
    require "graphs: Reset view returns to the whole day" (d.Until (fun () -> near 960. (span ())))
    (plot ()).scrollIntoView ()
    d.Pump 30.
    let rect = (plot ()).getBoundingClientRect ()
    d.Mouse (0, rect.left + rect.width * 0.25, rect.top + rect.height * 0.5)
    d.Pump 40.
    let cursor = d.Read (fun () -> store.Cursor.Value)
    require "graphs: moving over the plot inspects the point under the pointer" (d.Read (fun () -> store.Inspecting.Value) && abs (cursor - 24) <= 1)
    require "graphs: the tooltip appears" (d.Count ".graph-tooltip" = 1)
    require "graphs: the crosshair follows the cursor" (d.Until (fun () -> near (float cursor * 10.) (number ((d.One ".crosshair").getAttribute "x1"))))
    d.Host.Wheel (int (rect.left + rect.width / 2.), int (rect.top + rect.height / 2.), 0.0f, -200.0f, 0) |> ignore
    require "graphs: the wheel zooms about the pointer" (d.Until (fun () -> d.Read (fun () -> store.Window.Value.Span) <> 960.))
    d.ClickText ("button", "Reset view")
    d.ClickText ("button", "Worker comparison")
    require "graphs: the comparison line fades out" (d.Until (fun () -> d.Count ".comparison-line" = 0))
    d.ClickText ("button", "Worker comparison")
    require "graphs: the comparison line returns as SVG" (d.Count "svg path.comparison-line" = 1 && (d.One ".comparison-line").namespaceURI = "http://www.w3.org/2000/svg")
    d.ClickText ("button", "Next snapshot")
    require "graphs: Next snapshot moves to 12:00" (text (d.One ".snapshot-time") = "12:00")
    let london = d.One "[data-region=\"London\"]"
    require "graphs: rows move to their new rank" (d.Until (fun () -> (style london "transform").Contains "translateY(0px)"))

let private routes (d: Driver) =
    d.Open "routes"
    let shell = d.Running.App.Live.Routes.Value
    let route () = d.All "section.page" |> Array.map (fun page -> page.getAttribute "data-route")
    require "routes: the shell starts home" (route () = [| "home" |])
    d.Click (d.One "button.increment")
    require "routes: the home page counts in its own loop" (text (d.One ".count") = "1")
    d.Click (d.One "a.tab[data-route=\"gallery\"]")
    require "routes: a tab dispatches its route" (d.Read (fun () -> shell.Route.Value) = Routes.Gallery)
    require "routes: the leaving page stays while it crosses over" (route () |> Array.contains "home")
    require "routes: only the new page stays" (d.Until (fun () -> route () = [| "gallery" |]))
    require "routes: the indicator follows the tab" (d.Until (fun () -> (style (d.One ".indicator") "transform").StartsWith "translateX(116px)"))
    d.Click (d.One "[data-filter=\"warm\"]")
    require "routes: the filter's leaving swatches scale out" (d.Until (fun () -> d.Count "li.swatch" = 3))
    d.Click (d.One "a.tab[data-route=\"settings\"]")
    d.Pump 100.
    d.Click (d.One "[data-theme=\"dusk\"]")
    require "routes: Settings reports its theme to the shell" (d.Until (fun () -> (d.One ".shell").getAttribute "data-theme" = "dusk"))
    d.Click (d.One "a.tab[data-route=\"home\"]")
    require "routes: home starts a new loop on every visit" (d.Until (fun () -> route () = [| "home" |]) && text (d.One ".count") = "0")
    require "routes: the footer counts route changes" (text (d.One ".shell-foot") = "Home · 3 route changes")

/// <summary>Runs every check in a hidden window; 0 when all pass.</summary>
let run () =
    try
        if OperatingSystem.IsWindows () && RuntimeInformation.OSArchitecture = Architecture.X64 then
            require "the Windows x64 runtime presents through D3D11" (WebCoreHost.Has "presentation-d3d11")

        use window = Window.Create ("Ranvier Motion smoke", 1180, 860, hidden = true, ?renderer = Host.renderer)
        use running = Host.mount window.Host "kanban"

        let d =
            {
                Window = window
                Host = window.Host
                Running = running
            }

        d.Pump 200.
        opened d
        kanban d
        dashboard d
        story d
        physics d
        graphs d
        routes d
        require "no mount or commit failures" (running.Errors.Count = 0)
        Console.WriteLine ("Ranvier Motion smoke passed: " + string passed + " checks")
        0
    with error ->
        Console.Error.WriteLine (error.ToString ())
        1
