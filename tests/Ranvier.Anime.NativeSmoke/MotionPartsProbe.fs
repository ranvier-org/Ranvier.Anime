module Ranvier.Anime.NativeSmoke.MotionPartsProbe

open System
open System.Diagnostics
open System.Globalization
open Ranvier
open Ranvier.Css
open Ranvier.Dom
open Ranvier.Anime
open Ranvier.View
open Ranvier.View.Dom
open Ranvier.View.Sugar
open Ranvier.View.Anime
open Ranvier.View.WebCore

let private styleOf (element: Element) = (unbox<ElementCSSInlineStyle> (box element)).style
let private value (element: Element) name = (styleOf element).getPropertyValue name

let private number (text: string) =
    match Double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture) with
    | true, parsed -> ValueSome parsed
    | _ -> ValueNone

let private fadeTo (target: float) (_: 'inst) (element: Element) =
    Anime.animate (box element) [| PropertySpec.Scalar("opacity", target) |] { ComposedAnimationOptions.Default with Duration = 120. }

let private fadeIn (_: 'inst) (element: Element) =
    Anime.animate (box element) [| PropertySpec.Scalar("opacity", 1., fromValue = 0.) |] { ComposedAnimationOptions.Default with Duration = 120. }

let private rowsWith (part: Part<HTMLLIElement, unit -> int>) =
    let row =
        Component.define (fun _ (current: unit -> int) -> current) (fun t -> t.li [| part |] [| t.text (fun current -> string (current ())) |])

    Component.define
        (fun _ (items: Signal<int array>) -> items)
        (fun t -> t.ul [| t.Attrs.attribute ("style", "position:relative;margin:0;padding:0") |] [| t.each id (fun (items: Signal<int array>) -> items.Value) row |])

/// <summary>Covers each Motion part's placement, change and cleanup on the native host.</summary>
let run (host: WebCoreHost) (scheduler: FrameScheduler) =
    let require name condition =
        if not condition then failwith ("Motion part: " + name)
        Console.WriteLine("PASS motion part " + name)

    let pump condition =
        let watch = Stopwatch.StartNew()
        while not (condition ()) && watch.ElapsedMilliseconds < 3000L do
            host.Cycle(milliseconds = 1)
            host.Frame()
        condition ()

    use motion = AnimeHost.Install { Scheduler = scheduler; Computed = ComputedStyles.forHost host }

    // Mounts `view` with `props` in its own graph and container; the result disposes the mount.
    let mountOf (view: Component<'props>) (props: Graph -> 'props) =
        let graph = new Graph()
        let container = host.Document.createElement "div"
        host.Document.body.appendChild container |> ignore
        let state = graph.Run(fun () -> props graph)

        let mounted =
            Mounting.mount graph None (fun errors -> raise (AggregateException(errors: exn seq))) container None (fun context ->
                Program.run (Component.render view state) context.Backend)

        let dispose () =
            graph.Run(fun () -> mounted.Dispose())
            graph.Dispose()
            host.Document.body.removeChild container |> ignore

        graph, container, state, dispose

    let first (container: Element) selector = (container.querySelectorAll selector).item 0u |> unbox<Element>

    let rowTexts (container: Element) =
        let found = container.querySelectorAll "li"
        [ for index in 0 .. int found.length - 1 -> (found.item (uint index)).textContent ]

    let row (container: Element) text =
        let found = container.querySelectorAll "li"
        [ for index in 0 .. int found.length - 1 -> unbox<Element> (found.item (uint index)) ]
        |> List.find (fun item -> (item :> Node).textContent = text)

    // on and appear: play on placement, revert on disposal.
    for name, part in [ "on", Motion.on (fadeTo 0.5); "appear", Motion.appear (fadeTo 0.5) ] do
        let view = Component.define (fun _ () -> ()) (fun t -> t.div [| part |] [||])
        let _, container, _, dispose = mountOf view (fun _ -> ())
        let element = first container "div"
        require (name + " plays on placement") (pump (fun () -> value element "opacity" = "0.5"))
        dispose ()
        require (name + " reverts on disposal") (value element "opacity" = "")

    // enter and exit as separate parts: the exit is not pinned.
    do
        let view = rowsWith (Hooks.both (Motion.enter fadeIn, Motion.exit (fadeTo 0.)))
        let graph, container, items, dispose = mountOf view (fun _ -> Api.createSignal [| 1 |])
        graph.Run(fun () -> items.Value <- [| 1; 2 |])
        require "enter plays on an inserted row" (number (value (row container "2") "opacity") |> ValueOption.exists (fun v -> v < 0.5))
        require "enter completes" (pump (fun () -> value (row container "2") "opacity" = "1"))
        graph.Run(fun () -> items.Value <- [| 1 |])
        let leaving = row container "2"
        require "exit keeps the row in flow" (rowTexts container = [ "1"; "2" ] && value leaving "position" = "")
        require "exit removes the row on completion" (pump (fun () -> rowTexts container = [ "1" ]))
        dispose ()

    // A part on a leaving row pauses instead of reverting, so the exit starts from the current values.
    do
        let view =
            rowsWith (
                Hooks.both (
                    Motion.follow "x" { ComposedAnimationOptions.Default with Duration = 100. } (fun (_: unit -> int) -> 10.),
                    Motion.exit (fadeTo 0.)
                )
            )

        let graph, container, items, dispose = mountOf view (fun _ -> Api.createSignal [| 1 |])
        let leaving = row container "1"
        graph.Run(fun () -> items.Value <- [||])
        require "a part keeps its values while the exit plays" ((value leaving "transform").Contains "translateX(10px)" && rowTexts container = [ "1" ])
        require "the paused part's row still leaves" (pump (fun () -> rowTexts container = []))
        dispose ()

    // Presence presets.
    for name, part, property, fragment in
        [ "rise", Motion.rise, "transform", "translateY"
          "scale", Motion.scale, "transform", "scale"
          "fade", Motion.fade, "opacity", "" ] do
        let graph, container, items, dispose = mountOf (rowsWith part) (fun _ -> Api.createSignal [| 1 |])
        graph.Run(fun () -> items.Value <- [| 1; 2 |])
        require (name + " starts its enter") ((value (row container "2") property).Contains fragment && value (row container "2") "opacity" <> "1")
        require (name + " finishes its enter") (pump (fun () -> value (row container "2") "opacity" = "1"))
        graph.Run(fun () -> items.Value <- [| 1 |])
        require (name + " pins its exit") (value (row container "2") "position" = "absolute")
        require (name + " leaves") (pump (fun () -> rowTexts container = [ "1" ]))
        dispose ()

    do
        let graph, container, items, dispose = mountOf (rowsWith Motion.collapse) (fun _ -> Api.createSignal [| 1 |])
        graph.Run(fun () -> items.Value <- [| 1; 2 |])
        let entering = row container "2"
        require "collapse unfolds from zero height" (value entering "overflow" = "hidden" && (number ((value entering "height").Replace("px", "")) |> ValueOption.exists (fun v -> v < 5.)))
        require "collapse clears its styles after unfolding" (pump (fun () -> value entering "height" = "" && value entering "overflow" = ""))
        graph.Run(fun () -> items.Value <- [| 1 |])
        require "collapse folds in flow" (value entering "overflow" = "hidden" && value entering "position" = "")
        require "collapse leaves" (pump (fun () -> rowTexts container = [ "1" ]))
        dispose ()

    // slot: playing again cancels the running animation; disposal cancels and closes.
    do
        let holder = ref (null: MotionSlot)
        let view = Component.define (fun _ () -> holder.Value <- Motion.slot ()) (fun t -> t.div [||] [||])
        let graph, container, _, dispose = mountOf view (fun _ -> ())
        let element = first container "div"
        let firstPlay = ref (null: ITickable)
        graph.Run(fun () -> holder.Value.Play(fun () -> let a = fadeTo 0.2 () element in firstPlay.Value <- a; a))
        graph.Run(fun () -> holder.Value.Play(fun () -> fadeTo 0.8 () element))
        require "slot cancels the previous animation" (firstPlay.Value.Cancelled && holder.Value.Playing)
        let running = holder.Value.Running
        dispose ()
        require "slot disposal cancels the running animation" (running.Cancelled && isNull holder.Value.Running)
        holder.Value.Play(fun () -> failwith "a closed slot must not start")
        require "closed slot only cancels" (isNull holder.Value.Running)

    // follow: the first value is set at once, later values animate, disposal restores.
    do
        let view =
            Component.define
                (fun _ (x: Signal<float>) -> x)
                (fun t -> t.div [| Motion.follow "x" { ComposedAnimationOptions.Default with Duration = 100. } (fun (x: Signal<float>) -> x.Value) |] [||])

        let graph, container, x, dispose = mountOf view (fun _ -> Api.createSignal 10.)
        let element = first container "div"
        require "follow sets the first value at once" ((value element "transform").Contains "translateX(10px)")
        graph.Run(fun () -> x.Value <- 50.)
        require "follow animates to a new value" (pump (fun () -> (value element "transform").Contains "translateX(50px)"))
        dispose ()
        require "follow restores the inline transform" (value element "transform" = "")

    // followCss: a typed custom property, same contract.
    do
        let size = RuntimeApi.createCustomProperty "--motion-size"

        let view =
            Component.define
                (fun _ (amount: Signal<float>) -> amount)
                (fun t ->
                    t.div
                        [| Motion.followCss size { ComposedAnimationOptions.Default with Duration = 100. } (fun (amount: Signal<float>) -> Generated.number amount.Value "px") |]
                        [||])

        let graph, container, amount, dispose = mountOf view (fun _ -> Api.createSignal 10.)
        let element = first container "div"
        require "followCss sets the first value at once" (value element "--motion-size" = "10px")
        graph.Run(fun () -> amount.Value <- 40.)
        require "followCss animates to a new value" (pump (fun () -> value element "--motion-size" = "40px"))
        dispose ()
        require "followCss restores the inline declaration" (value element "--motion-size" = "")

    // animateOn: the first value plays to its end at once; disposal reverts the latest animation.
    do
        let view =
            Component.define
                (fun _ (level: Signal<float>) -> level)
                (fun t -> t.div [| Motion.animateOn (fun (level: Signal<float>) -> level.Value) (fun _ element target -> fadeTo target () element) |] [||])

        let graph, container, level, dispose = mountOf view (fun _ -> Api.createSignal 0.2)
        let element = first container "div"
        require "animateOn completes the first value at once" (value element "opacity" = "0.2")
        graph.Run(fun () -> level.Value <- 0.8)
        require "animateOn replays on change" (pump (fun () -> value element "opacity" = "0.8"))
        dispose ()
        require "animateOn reverts on disposal" (value element "opacity" <> "0.8")

    // animateOn disposed while its first, completed animation is still the latest.
    do
        let view =
            Component.define
                (fun _ (level: Signal<float>) -> level)
                (fun t -> t.div [| Motion.animateOn (fun (level: Signal<float>) -> level.Value) (fun _ element target -> fadeTo target () element) |] [||])

        let graph, container, _, dispose = mountOf view (fun _ -> Api.createSignal 0.3)
        let element = first container "div"
        require "animateOn holds its completed first value" (value element "opacity" = "0.3")
        dispose ()
        require "animateOn reverts a completed first value on disposal" (value element "opacity" <> "0.3")

    // draw and morph on SVG geometry.
    do
        let view =
            Component.define
                (fun _ (fraction: Signal<float>) -> fraction)
                (fun _ ->
                    svg (Attrs.attribute ("viewBox", "0 0 200 200"), Attrs.attribute ("width", "200"), Attrs.attribute ("height", "200")) {
                        path (
                            Attrs.attribute ("d", "M0 0 L100 0"),
                            Attrs.attribute ("stroke", "black"),
                            Motion.draw { ComposedAnimationOptions.Default with Duration = 100. } (fun (fraction: Signal<float>) -> fraction.Value)
                        ) {
                            ()
                        }
                    })

        let graph, container, fraction, dispose = mountOf view (fun _ -> Api.createSignal 0.5)
        let shape = first container "path"
        let dash () = shape.getAttribute "stroke-dasharray"
        let initial = dash ()
        require "draw normalises the path and draws the first fraction" (shape.getAttribute "pathLength" = "1000" && not (isNull initial))
        graph.Run(fun () -> fraction.Value <- 1.)
        require "draw animates to a new fraction" (pump (fun () -> dash () <> initial && not (isNull (dash ())) && (dash ()).StartsWith "1000"))
        dispose ()
        require "draw restores the stroke attributes" (isNull (shape.getAttribute "pathLength") && isNull (dash ()))

    do
        let view =
            Component.define
                (fun _ (shape: Signal<string>) -> shape)
                (fun _ ->
                    svg (Attrs.attribute ("viewBox", "0 0 200 200"), Attrs.attribute ("width", "200"), Attrs.attribute ("height", "200")) {
                        path (Attrs.attribute ("d", "M0 0 L100 0"), Motion.morph { ComposedAnimationOptions.Default with Duration = 100. } (fun (shape: Signal<string>) -> shape.Value)) {
                            ()
                        }
                    })

        let graph, container, target, dispose = mountOf view (fun _ -> Api.createSignal "M0 0 L100 0")
        let shape = first container "path"
        graph.Run(fun () -> target.Value <- "M0 0 L0 100")
        let sampled = ref false
        require "morph completes on the exact shape read" (pump (fun () ->
            let d = shape.getAttribute "d"
            if d <> "M0 0 L100 0" && d <> "M0 0 L0 100" then sampled.Value <- true
            d = "M0 0 L0 100"))
        require "morph interpolates between shapes" sampled.Value
        dispose ()
        require "morph restores the original shape" (shape.getAttribute "d" = "M0 0 L100 0")

    // split: splits static text, plays over the parts, and restores the text on disposal.
    do
        let view =
            Component.define
                (fun _ () -> ())
                (fun t ->
                    t.p
                        [| Motion.split TextSplitterOptions.Default (fun _ splitter ->
                               Anime.animateTargets (splitter.Words |> Array.map box) [| PropertySpec.Scalar("opacity", 1., fromValue = 0.) |]
                                   { ComposedAnimationOptions.Default with Duration = 100. }) |]
                        [| t.text (fun () -> "Hello motion world") |])

        let _, container, _, dispose = mountOf view (fun _ -> ())
        let paragraph = first container "p"
        let words = paragraph.querySelectorAll "[data-word]"
        require "split splits the text into words" (words.length = 3u || paragraph.children.length = 3u)
        dispose ()
        require "split restores the text" ((paragraph :> Node).textContent = "Hello motion world" && paragraph.children.length = 0u)

    // flipFrom: the next FLIP pass starts the row from its kept visual box.
    do
        let row' =
            Component.define (fun _ (current: unit -> int) -> current) (fun t ->
                t.li [| t.Attrs.attribute ("style", "height:20px;list-style:none") |] [| t.text (fun current -> string (current ())) |])

        let view =
            Component.define
                (fun _ (items: Signal<int array>) -> items)
                (fun t ->
                    t.ul
                        [| t.Attrs.attribute ("style", "position:relative;margin:0;padding:0")
                           Motion.flip { LayoutOptions.Default with Duration = (fun _ -> 400.) } |]
                        [| t.each id (fun (items: Signal<int array>) -> items.Value) row' |])

        let graph, container, items, dispose = mountOf view (fun _ -> Api.createSignal [| 1; 2 |])
        let moved = row container "2"
        let laidOut = moved.getBoundingClientRect().top
        (styleOf moved).setProperty("transform", "translateY(60px)")
        Motion.flipFrom moved
        (styleOf moved).removeProperty "transform" |> ignore
        graph.Run(fun () -> items.Value <- [| 2; 1 |])
        host.Frame()
        let started = (row container "2").getBoundingClientRect().top
        require "flipFrom starts the FLIP from the kept box" (started > laidOut + 30.)
        dispose ()
