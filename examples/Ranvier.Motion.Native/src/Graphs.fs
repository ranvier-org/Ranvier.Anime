/// <summary>Interactive SVG charts with animated inspection, viewport changes and ranked bars.</summary>
module Graphs

open System
open global.Ranvier
open Ranvier.Anime
open Ranvier.Dom
open Ranvier.View
open Ranvier.View.Sugar
open Ranvier.View.Anime

/// <summary>The visible horizontal interval in a 960-unit, 24-hour data set.</summary>
type Viewport = { Start: float; Span: float }

/// <summary>The graph controls and selected data point, owned by one page instance.</summary>
[<NoEquality; NoComparison>]
type Store =
    {
        Window: Signal<Viewport>
        Cursor: Signal<int>
        /// <summary>Tooltip position as percentages of the plot's width and height.</summary>
        Pointer: Signal<struct (float * float)>
        Inspecting: Signal<bool>
        Dragging: Signal<bool>
        Comparison: Signal<bool>
        Snapshot: Signal<int>
        Playing: Signal<bool>
    }

let private viewport start span =
    let span = max 120. (min 960. span)

    {
        Start = max 0. (min (960. - span) start)
        Span = span
    }

let private zoom (window: Viewport) factor anchor =
    let span = max 120. (min 960. (window.Span * factor))

    viewport
        (anchor
         - (anchor - window.Start) * span / window.Span)
        span

let private value channel index =
    let t = float index

    91.
    + 27. * sin (t / 10. + channel)
    + 18. * cos (t / 4.7 + channel)
    + 30. * exp (-(((t - 61.) / 9.) ** 2.))

let private y value =
    240. - value * 1.05

let private curve channel =
    let values = Array.init 97 (fun index -> y (value channel index))

    let slope index =
        (values[min 96 (index + 1)]
         - values[max 0 (index - 1)])
        / (if index = 0 || index = 96 then 1. else 2.)

    [|
        sprintf "M0 %.2f" values[0]
        for index in 1..96 do
            let x = float index * 10.

            sprintf
                "C%.2f %.2f %.2f %.2f %.2f %.2f"
                (x - 10. + 10. / 3.)
                (values[index - 1] + slope (index - 1) / 3.)
                (x - 10. / 3.)
                (values[index] - slope index / 3.)
                x
                values[index]
    |]
    |> String.concat " "

let private time x =
    let minutes = int (Math.Round (x * 1.5))
    sprintf "%02d:%02d" (minutes / 60) (minutes % 60)

let private regions = [| "Amsterdam"; "London"; "Singapore"; "Sydney"; "Tokyo" |]

let private snapshots =
    [|
        [| 68; 42; 55; 36; 61 |]
        [| 48; 81; 66; 58; 42 |]
        [| 76; 59; 44; 91; 68 |]
        [| 52; 73; 95; 64; 83 |]
    |]

let private times = [| "09:00"; "12:00"; "15:00"; "18:00" |]

let private rank snapshot index =
    [| 0..4 |]
    |> Array.sortBy (fun i -> -snapshots[snapshot][i], i)
    |> Array.findIndex ((=) index)

/// <summary>Creates the full-day viewport and a paused ranking chart in the current graph.</summary>
let createStore () =
    {
        Window = Api.createSignal (viewport 0. 960.)
        Cursor = Api.createSignal 48
        Pointer = Api.createSignal (struct (50., 35.))
        Inspecting = Api.createSignal false
        Dragging = Api.createSignal false
        Comparison = Api.createSignal true
        Snapshot = Api.createSignal 0
        Playing = Api.createSignal false
    }

let private changeZoom (store: Store) factor =
    let window = store.Window.Value
    store.Inspecting.Value <- false
    store.Window.Value <- zoom window factor (window.Start + window.Span / 2.)

let private pan (store: Store) direction =
    let window = store.Window.Value
    store.Inspecting.Value <- false
    store.Window.Value <- viewport (window.Start + direction * window.Span * 0.25) window.Span

let private clientX (event: Event) = (unbox<MouseEvent> event).clientX

let private fraction (element: Element) (x: float) =
    let rect = element.getBoundingClientRect ()
    (x - rect.left) / rect.width

let private numbers (text: string) =
    text.Split (' ', StringSplitOptions.RemoveEmptyEntries)
    |> Array.map (fun part -> Double.Parse (part, Globalization.CultureInfo.InvariantCulture))

// The viewBox on screen, which the browser reads from viewBox.baseVal: the animated attribute's own numbers.
let private displayed (element: Element) =
    match numbers (element.getAttribute "viewBox") with
    | [| start; _; span; _ |] -> { Start = start; Span = span }
    | _ -> { Start = 0.; Span = 960. }

let private prevent (event: Event) = if event.cancelable then event.preventDefault ()

let private inspect (store: Store) world position =
    let index = max 0 (min 96 (int (Math.Round (world / 10.))))
    store.Cursor.Value <- index
    store.Pointer.Value <- struct (max 0. (min 100. (position * 100.)), y (value 0. index) / 2.8)
    store.Inspecting.Value <- true

// The browser listens for pointer events and captures the pointer while panning; the native host sends mouse events, so
// a pan follows the document's mousemove and mouseup until it ends.
let private pointerSetup (graph: Graph) (store: Store) (element: Element) =
    let mutable drag = None
    let page = (element :> Node).ownerDocument :> EventTarget

    let move (event: Event) =
        graph.Run (fun () ->
            let position = fraction element (clientX event)

            match drag with
            | Some (origin, window: Viewport) -> store.Window.Value <- viewport (window.Start - (position - origin) * window.Span) window.Span
            | None ->
                let window = displayed element
                inspect store (window.Start + position * window.Span) position)

    let rec down (event: Event) =
        if (unbox<MouseEvent> event).button = 0s then
            prevent event
            page.addEventListener ("mousemove", panMove)
            page.addEventListener ("mouseup", up)

            graph.Run (fun () ->
                drag <- Some (fraction element (clientX event), displayed element)
                store.Dragging.Value <- true
                store.Inspecting.Value <- false)

    and panMove: Event -> unit = fun event -> if drag.IsSome then move event

    and up: Event -> unit =
        fun _ ->
            page.removeEventListener ("mousemove", panMove)
            page.removeEventListener ("mouseup", up)

            if drag.IsSome then
                drag <- None
                graph.Run (fun () -> store.Dragging.Value <- false)

    let leave (_: Event) =
        if drag.IsNone then
            graph.Run (fun () -> store.Inspecting.Value <- false)

    let wheel (event: Event) =
        prevent event

        graph.Run (fun () ->
            let position = fraction element (clientX event)
            let window = displayed element
            let target = store.Window.Value

            let nextSpan =
                max
                    120.
                    (min
                        960.
                        (target.Span
                         * exp (max -400. (min 400. (unbox<WheelEvent> event).deltaY) * 0.002)))

            let anchor = window.Start + position * window.Span
            store.Inspecting.Value <- false
            store.Window.Value <- viewport (anchor - position * nextSpan) nextSpan)

    let keyboard (event: Event) =
        match (unbox<KeyboardEvent> event).key with
        | "+"
        | "=" ->
            prevent event
            graph.Run (fun () -> changeZoom store 0.65)
        | "-" ->
            prevent event
            graph.Run (fun () -> changeZoom store (1. / 0.65))
        | "0" ->
            prevent event

            graph.Run (fun () ->
                store.Window.Value <- viewport 0. 960.
                store.Inspecting.Value <- false)
        | "ArrowLeft"
        | "ArrowRight" as direction ->
            prevent event

            graph.Run (fun () ->
                let window = store.Window.Value

                let index =
                    max
                        0
                        (min
                            96
                            (store.Cursor.Value
                             + (if direction = "ArrowLeft" then -1 else 1)))

                let world = float index * 10.
                let start = min world (max (world - window.Span) window.Start)
                let window = viewport start window.Span
                store.Window.Value <- window
                inspect store world ((world - window.Start) / window.Span))
        | "Escape" -> graph.Run (fun () -> store.Inspecting.Value <- false)
        | _ -> ()

    let listeners: (string * (Event -> unit)) array =
        [|
            "mousemove", (fun event -> if drag.IsNone then move event)
            "mousedown", down
            "mouseleave", leave
            "wheel", wheel
            "keydown", keyboard
        |]

    let target = element :> EventTarget

    for name, callback in listeners do
        target.addEventListener (name, callback, AddEventListenerOptions (passive = Some false))

    fun () ->
        page.removeEventListener ("mousemove", panMove)
        page.removeEventListener ("mouseup", up)

        for name, callback in listeners do
            target.removeEventListener (name, callback)

let private inspector =
    article (Attrs.className "graph-card explorer") {
        header (Attrs.className "graph-heading") {
            div () {
                span (Attrs.className "eyebrow") { "01 / INSPECT & NAVIGATE" }
                h2 () { "Explore a signal" }
                p () { "API latency over one day. Move over the line, scroll to zoom, or drag to pan." }
            }

            span (Attrs.className "graph-unit") { "milliseconds" }
        }

        div (Attrs.className "graph-controls") {
            button (Events.onClick (fun store _ -> changeZoom store 0.65)) { "Zoom in" }
            button (Events.onClick (fun store _ -> changeZoom store (1. / 0.65))) { "Zoom out" }
            button (Events.onClick (fun store _ -> pan store -1.)) { "Pan left" }
            button (Events.onClick (fun store _ -> pan store 1.)) { "Pan right" }

            button (
                Events.onClick (fun (store: Store) _ ->
                    store.Window.Value <- viewport 0. 960.
                    store.Inspecting.Value <- false)
            ) {
                "Reset view"
            }

            button (
                Events.onClick (fun (store: Store) _ -> store.Comparison.Value <- not store.Comparison.Value),
                Attrs.attribute (
                    "aria-pressed",
                    fun (store: Store) ->
                        Some (
                            string store.Comparison.Value
                            |> fun s -> s.ToLowerInvariant ()
                        )
                )
            ) {
                "Worker comparison"
            }
        }

        div (Attrs.className "plot-frame") {
            div (Attrs.className "y-axis", Attrs.attribute ("aria-hidden", "true")) {
                for value in [ 200; 150; 100; 50; 0 ] do
                    span () { string value }
            }

            div (Attrs.className "plot-area") {
                svg (
                    Attrs.className "explorer-svg",
                    Attrs.attribute ("viewBox", "0 0 960 280"),
                    Attrs.attribute ("preserveAspectRatio", "none"),
                    Attrs.attribute ("tabindex", "0"),
                    Attrs.role "group",
                    Attrs.attribute ("aria-label", "Latency chart. Arrow keys inspect points; plus and minus zoom; zero resets."),
                    Hooks.mounted (fun store element -> pointerSetup Graph.Current store (unbox<Element> (box element))),
                    Motion.animateOn (fun (store: Store) -> store.Window.Value, store.Dragging.Value) (fun _ element (window, dragging) ->
                        Native.animate
                            element
                            (Native.timing (if dragging then 100. else 650.) Native.outQuart)
                            [| PropertySpec.Text ("viewBox", sprintf "%.3f 0 %.3f 280" window.Start window.Span) |])
                ) {
                    defs () {
                        linearGradient (
                            Attrs.attribute ("id", "graph-fill"),
                            Attrs.attribute ("x1", "0"),
                            Attrs.attribute ("y1", "0"),
                            Attrs.attribute ("x2", "0"),
                            Attrs.attribute ("y2", "1")
                        ) {
                            stop (
                                Attrs.attribute ("offset", "0%"),
                                Attrs.attribute ("stop-color", "#6953e8"),
                                Attrs.attribute ("stop-opacity", ".24")
                            ) {
                                ()
                            }

                            stop (
                                Attrs.attribute ("offset", "100%"),
                                Attrs.attribute ("stop-color", "#6953e8"),
                                Attrs.attribute ("stop-opacity", ".01")
                            ) {
                                ()
                            }
                        }
                    }

                    for value in [ 0; 50; 100; 150; 200 ] do
                        line (
                            Attrs.className "graph-grid",
                            Attrs.attribute ("x1", "0"),
                            Attrs.attribute ("x2", "960"),
                            Attrs.attribute ("y1", string (y (float value))),
                            Attrs.attribute ("y2", string (y (float value)))
                        ) {
                            ()
                        }

                    path (Attrs.className "signal-area", Attrs.attribute ("d", curve 0. + " L960 280 L0 280 Z")) { () }

                    show
                        (fun (store: Store) -> store.Comparison.Value)
                        (path (Attrs.className "comparison-line", Attrs.attribute ("d", curve 1.7), Motion.fade) { () })

                    path (Attrs.className "signal-line", Attrs.attribute ("d", curve 0.)) { () }

                    line (
                        Attrs.className "crosshair",
                        Attrs.attribute ("y1", "12"),
                        Attrs.attribute ("y2", "255"),
                        Motion.follow "x1" { ComposedAnimationOptions.Default with Duration = 240.; Ease = Native.outQuart } (fun (store: Store) ->
                            float store.Cursor.Value * 10.),
                        Motion.follow "x2" { ComposedAnimationOptions.Default with Duration = 240.; Ease = Native.outQuart } (fun (store: Store) ->
                            float store.Cursor.Value * 10.)
                    ) {
                        ()
                    }

                    ellipse (
                        Attrs.className "focus-point",
                        Attrs.attribute ("rx", "5"),
                        Attrs.attribute ("ry", "5"),
                        Motion.animateOn (fun (store: Store) -> store.Window.Value.Span, store.Dragging.Value) (fun _ element (span, dragging) ->
                            Native.animate element (Native.timing (if dragging then 100. else 650.) Native.outQuart) [| Native.toward "rx" (span * 5. / 960.) |]),
                        Motion.follow "cx" { ComposedAnimationOptions.Default with Duration = 240.; Ease = Native.outQuart } (fun (store: Store) ->
                            float store.Cursor.Value * 10.),
                        Motion.follow "cy" { ComposedAnimationOptions.Default with Duration = 240.; Ease = Native.outQuart } (fun (store: Store) ->
                            y (value 0. store.Cursor.Value))
                    ) {
                        ()
                    }
                }

                show
                    (fun (store: Store) -> store.Inspecting.Value)
                    (div (
                        Attrs.className "graph-tooltip",
                        Attrs.role "status",
                        // anime follows both in %; Ranvier.Anime's follow keeps a property's default unit, so the tooltip
                        // eases to each pointer position as percentage text instead.
                        Motion.animateOn (fun (store: Store) -> store.Pointer.Value) (fun _ element (struct (x, y)) ->
                            Native.animate
                                element
                                (Native.timing 240. Native.outQuart)
                                [| PropertySpec.Text ("--tip-x", sprintf "%.3f%%" x); PropertySpec.Text ("top", sprintf "%.3f%%" y) |])
                    ) {
                        span () { fun (store: Store) -> time (float store.Cursor.Value * 10.) }
                        strong () { fun (store: Store) -> sprintf "%.1f ms" (value 0. store.Cursor.Value) }
                    })

                div (Attrs.className "x-axis", Attrs.attribute ("aria-hidden", "true")) {
                    for index in 0..4 do
                        span () { fun (store: Store) -> let window = store.Window.Value in time (window.Start + window.Span * float index / 4.) }
                }
            }
        }

        footer (Attrs.className "graph-footnote") {
            span (Attrs.className "legend-api") { "API" }
            span (Attrs.className "legend-worker") { "Worker" }
            span () { "← / → inspect · + / − zoom · 0 reset" }
        }

        p (Attrs.className "technique") {
            "Motion.follow eases the crosshair and tooltip. Motion.animateOn interpolates the SVG viewBox; pointer coordinates use the viewBox currently on screen."
        }
    }

let private bar (index: int) =
    div (
        Attrs.className ("rank-row region-" + string index),
        Attrs.data ("region", regions[index]),
        Attrs.role "listitem",
        Attrs.attribute ("style", sprintf "transform:translateY(%dpx)" (rank 0 index * 52)),
        Motion.animateOn
            (fun (store: Store) -> let snapshot = store.Snapshot.Value in rank snapshot index, snapshots[snapshot][index])
            (fun _ element (rank, score) ->
                Native
                    .timeline(true)
                    .Add([| box element |], [| Native.toward "y" (float rank * 52.) |], Native.timing 650. Native.outQuart, TimelinePosition.At (float rank * 30.))
                    .Add(
                        [| box (Native.query element ".bar-fill") |],
                        [| Native.toward "scaleX" (float score / 100.) |],
                        Native.timing 650. Native.outQuart,
                        TimelinePosition.At (80. + float rank * 30.)
                    ))
    ) {
        span (Attrs.className "region-name") { regions[index] }
        div (Attrs.className "bar-track") { div (Attrs.className "bar-fill") { () } }
        strong (Attrs.className "region-score") { fun (store: Store) -> sprintf "%d k" (snapshots[store.Snapshot.Value][index]) }
    }

/// <summary>The line explorer and ranked traffic chart over <c>store</c>.</summary>
let view (store: Store) =
    Component.template
        (fun _ () -> store)
        (section (Attrs.className "graphs") {
            inspector

            article (Attrs.className "graph-card rankings") {
                header (Attrs.className "graph-heading") {
                    div () {
                        span (Attrs.className "eyebrow") { "02 / REORDER & RETARGET" }
                        h2 () { "A changing leaderboard" }
                        p () { "Regional requests per minute. Change the snapshot, or play the four-part cycle." }
                    }

                    strong (Attrs.className "snapshot-time") { fun (store: Store) -> times[store.Snapshot.Value] }
                }

                div (Attrs.className "graph-controls") {
                    button (
                        Attrs.className "primary",
                        Events.onClick (fun (store: Store) _ -> store.Snapshot.Value <- (store.Snapshot.Value + 1) % snapshots.Length)
                    ) {
                        "Next snapshot"
                    }

                    button (Events.onClick (fun (store: Store) _ -> store.Playing.Value <- not store.Playing.Value)) {
                        fun (store: Store) ->
                            if store.Playing.Value then
                                "Pause snapshots"
                            else
                                "Play snapshots"
                    }
                }

                div (Attrs.className "rank-bars", Attrs.role "list", Attrs.attribute ("aria-label", "Regional request ranking")) {
                    for index in 0..4 do
                        bar index
                }

                p (Attrs.className "technique") {
                    "Each row owns a timeline. Position and bar length overlap with a short rank-based stagger, and new snapshots retarget the same elements."
                }
            }

            p (Attrs.className "graph-credit") {
                "Interaction ideas inspired by the "
                a (Attrs.attribute ("href", "https://livecharts.dev/docs/Avalonia/2.0.4/gallery")) { "LiveCharts gallery" }
                ". These charts use Ranvier views and Ranvier.Anime. Data is synthetic."
            }
        })

/// <summary>Owns the snapshot timer on the window loop: while playing, the next snapshot every 2.6 s.</summary>
let activate (graph: Graph) (store: Store) : IDisposable =
    Native.timers.Every (
        2600.,
        fun () ->
            graph.Run (fun () ->
                if store.Playing.Value then
                    store.Snapshot.Value <- (store.Snapshot.Value + 1) % snapshots.Length)
    )
