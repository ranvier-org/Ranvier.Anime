module Ranvier.Anime.NativeSmoke.MotionViewProbe

open System
open System.Diagnostics
open Ranvier
open Ranvier.Dom
open Ranvier.Anime
open Ranvier.View
open Ranvier.View.Dom
open Ranvier.View.Anime
open Ranvier.View.WebCore

let private row =
    Component.define
        (fun _ (current: unit -> int) -> current)
        (fun t -> t.li [| Motion.fade |] [| t.text (fun current -> string (current ())) |])

let private list =
    Component.define
        (fun _ (items: Signal<int array>) -> items)
        (fun t ->
            t.ul
                [| t.Attrs.attribute ("style", "position:relative;list-style:none;margin:0;padding:0")
                   Motion.flip LayoutOptions.Default |]
                [| t.each id (fun (items: Signal<int array>) -> items.Value) row |])

/// <summary>Mounts a View list with Motion.fade rows under Motion.flip on the native host and drives an enter and an exit.</summary>
let run (host: WebCoreHost) (scheduler: FrameScheduler) =
    let require name condition =
        if not condition then failwith ("Motion view: " + name)
        Console.WriteLine("PASS motion view " + name)

    let pump condition =
        let watch = Stopwatch.StartNew()
        while not (condition ()) && watch.ElapsedMilliseconds < 3000L do
            host.Cycle(milliseconds = 1)
            host.Frame()
        condition ()

    use motion = AnimeHost.Install { Scheduler = scheduler; Computed = ComputedStyles.forHost host }
    let graph = new Graph()
    let container = host.Document.createElement "div"
    host.Document.body.appendChild container |> ignore

    let rows () =
        let found = container.querySelectorAll "li"
        [ for index in 0 .. int found.length - 1 -> unbox<Element> (found.item (uint index)) ]

    let texts () = rows () |> List.map (fun item -> (item :> Node).textContent)
    let opacity (item: Element) = (unbox<ElementCSSInlineStyle> (box item)).style.getPropertyValue "opacity"
    let rowText text = rows () |> List.find (fun item -> (item :> Node).textContent = text)

    let items = graph.Run(fun () -> Api.createSignal [| 1; 2 |])

    let mounted =
        Mounting.mount graph None (fun errors -> raise (AggregateException(errors: exn seq))) container None (fun context ->
            Program.run (Component.render list items) context.Backend)

    try
        require "first pass renders still rows" (texts () = [ "1"; "2" ] && rows () |> List.forall (fun item -> opacity item = ""))

        graph.Run(fun () -> items.Value <- [| 1; 2; 3 |])
        require "inserted row enters" (texts () = [ "1"; "2"; "3" ])
        let entering = rowText "3"
        require "entering row starts transparent" (opacity entering <> "" && Double.Parse(opacity entering, Globalization.CultureInfo.InvariantCulture) < 0.5)
        require "entering row fades in" (pump (fun () -> opacity (rowText "3") = "1"))

        graph.Run(fun () -> items.Value <- [| 1; 3 |])
        require "retired row stays while its exit plays" (texts () = [ "1"; "2"; "3" ])
        let leaving = rowText "2"
        require "leaving row is pinned out of flow" ((unbox<ElementCSSInlineStyle> (box leaving)).style.getPropertyValue "position" = "absolute")
        require "retired row leaves once its exit completes" (pump (fun () -> texts () = [ "1"; "3" ]))

        let listRoot = (container.querySelectorAll "ul").item 0u |> unbox<Element>
        graph.Run(fun () -> items.Value <- [| 3; 1 |])
        require "reordered rows keep their nodes" (texts () = [ "3"; "1" ])
        require "flip animates the reorder" (listRoot.classList.contains "is-animated")
        require "flip settles" (pump (fun () -> not (listRoot.classList.contains "is-animated")))

        graph.Run(fun () -> Reactive.policy().Value <- Reactive.Reduced)
        graph.Run(fun () -> items.Value <- [| 3 |])
        require "reduced policy removes a row at once" (texts () = [ "3" ])
    finally
        graph.Run(fun () -> mounted.Dispose())
        graph.Dispose()
        host.Document.body.removeChild container |> ignore

[<NoEquality; NoComparison>]
type private Interactive =
    { Progress: Signal<float>
      InView: Signal<bool>
      Position: Signal<struct (float * float)>
      Dragging: Signal<bool> }

let private interactive =
    Component.define
        (fun _ (signals: Interactive) -> signals)
        (fun t ->
            t.div
                [||]
                [| t.div
                       [| t.Attrs.attribute ("style", "position:absolute;top:600px;left:0;width:50px;height:50px")
                          Motion.scroll
                              ScrollParams.Default
                              (fun state -> { Progress = ValueSome state.Progress; InView = ValueSome state.InView })
                              (fun _ element ->
                                  Anime.animate (box element) [| PropertySpec.Scalar("opacity", 1., fromValue = 0.) |]
                                      { ComposedAnimationOptions.Default with Duration = 1000. }) |]
                       [||]
                   t.div
                       [| t.Attrs.attribute ("style", "position:absolute;top:20px;left:20px;width:40px;height:40px;background:red")
                          Motion.drag
                              NativeDraggableOptions.Default
                              (fun state -> { Position = state.Position; Dragging = ValueSome state.Dragging; Slot = ValueNone }) |]
                       [||]
                   // anime's scroll-linked timeline(): a ComposedTimeline child scrubbed like the animation above.
                   t.div
                       [| t.Attrs.attribute ("style", "position:absolute;top:600px;left:60px;width:50px;height:50px")
                          Motion.scroll
                              ScrollParams.Default
                              (fun _ -> { Progress = ValueNone; InView = ValueNone })
                              (fun _ element ->
                                  let services = AnimeHost.Current
                                  let timeline = Composed.createTimeline services.Scheduler None (Some false)
                                  timeline.Add(
                                      [| box element |],
                                      [| PropertySpec.Scalar("opacity", 1., fromValue = 0.) |],
                                      { ComposedAnimationOptions.Default with Duration = 1000.; Computed = Some services.Computed }
                                  )) |]
                       [||] |])

/// <summary>Mounts Motion.scroll and Motion.drag on the native host and drives real scroll and mouse input.</summary>
let runInteraction () =
    use host = WebCoreHost.Create(320, 200)
    use scheduler = Frames.forHost host
    let require name condition =
        if not condition then failwith ("Motion interaction: " + name)
        Console.WriteLine("PASS motion interaction " + name)

    let pump condition =
        let watch = Stopwatch.StartNew()
        while not (condition ()) && watch.ElapsedMilliseconds < 3000L do
            host.Cycle(milliseconds = 1)
            host.Frame()
        condition ()

    let mouse phase (x: float) (y: float) =
        host.Mouse(phase, int (Math.Round x), int (Math.Round y), (if phase = 0 then -1 else 0), (if phase = 0 then 0 else 1), 0) |> ignore

    let computed = ComputedStyles.forHost host
    let windowScroll = ViewportScroll.forHost host

    let interaction =
        { Draggable =
            { Scheduler = scheduler; Document = host.Document; Computed = computed; Geometry = Geometry.forHost host
              Resize = ResizeObservation.forHost host; Media = MediaQueries.forHost host; Viewport = Viewport.forHost host
              WindowScroll = windowScroll }
          Viewport = Viewport.provider host }

    use motion = AnimeHost.Install({ Scheduler = scheduler; Computed = computed }, interaction)
    (unbox<ElementCSSInlineStyle> (box host.Document.body)).style.setProperty("height", "2000px")
    (unbox<ElementCSSInlineStyle> (box host.Document.body)).style.setProperty("margin", "0")
    let graph = new Graph()
    let container = host.Document.createElement "div"
    host.Document.body.appendChild container |> ignore
    let state =
        graph.Run(fun () ->
            { Progress = Api.createSignal 0.
              InView = Api.createSignal false
              Position = Api.createSignal (struct (0., 0.))
              Dragging = Api.createSignal false })

    let mounted =
        Mounting.mount graph None (fun errors -> raise (AggregateException(errors: exn seq))) container None (fun context ->
            Program.run (Component.render interactive state) context.Backend)

    let read (signal: Signal<'v>) = graph.Run(fun () -> Api.untrack (fun () -> signal.Value))

    try
        let probe = (container.querySelectorAll "div").item 1u |> unbox<Element>
        let handle = (container.querySelectorAll "div").item 2u |> unbox<Element>
        windowScroll.ScrollTo { Left = None; Top = Some 525.; Behavior = ScrollBehavior.Instant }
        let scrollEvent = (EventConstruction.forHost host).Create("scroll", Ranvier.Dom.EventConstruction.Event EventOptions.Default)
        (Viewport.provider host).Window.dispatchEvent scrollEvent |> ignore
        let opacity () = (unbox<ElementCSSInlineStyle> (box probe)).style.getPropertyValue "opacity"
        let scrubbed () =
            match Double.TryParse(opacity (), Globalization.NumberStyles.Float, Globalization.CultureInfo.InvariantCulture) with
            | true, value -> value > 0.2 && value < 0.8
            | _ -> false
        require "scroll scrubs the linked animation" (pump scrubbed)
        require "scroll writes progress" (let progress = read state.Progress in progress > 0.2 && progress < 0.8)
        require "scroll writes in-view" (read state.InView)
        let linked = (container.querySelectorAll "div").item 3u |> unbox<Element>
        let timelineOpacity () = (unbox<ElementCSSInlineStyle> (box linked)).style.getPropertyValue "opacity"
        require "scroll scrubs a linked composed timeline" (pump (fun () ->
            match Double.TryParse(timelineOpacity (), Globalization.NumberStyles.Float, Globalization.CultureInfo.InvariantCulture) with
            | true, value -> value > 0.2 && value < 0.8
            | _ -> false))

        windowScroll.ScrollTo { Left = None; Top = Some 0.; Behavior = ScrollBehavior.Instant }
        host.Frame()
        // Another animation writes the handle's transform after its draggable was created, as presence does to a card.
        (unbox<ElementCSSInlineStyle> (box handle)).style.setProperty("transform", "rotate(5deg)")
        let rect = handle.getBoundingClientRect()
        let x, y = rect.left + 5., rect.top + 5.
        mouse 1 x y
        mouse 0 (x + 10.) y
        mouse 0 (x + 30.) (y + 10.)
        let transform () = (unbox<ElementCSSInlineStyle> (box handle)).style.getPropertyValue "transform"
        require "drag writes position" (pump (fun () -> read state.Position = struct (30., 10.)))
        require "drag moves the element" (transform().StartsWith("translate(30px, 10px)", StringComparison.Ordinal))
        require "drag keeps transform components other animations wrote" (transform().Contains "rotate(5deg)")
        require "drag writes dragging" (read state.Dragging)
        mouse 2 (x + 30.) (y + 10.)
        require "release clears dragging" (not (read state.Dragging))
        graph.Run(fun () -> state.Position.Value <- struct (5., 7.))
        require "position writes move the element" (pump (fun () -> transform().StartsWith("translate(5px, 7px)", StringComparison.Ordinal)))
    finally
        graph.Run(fun () -> mounted.Dispose())
        graph.Dispose()
        host.Document.body.removeChild container |> ignore
