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

    use motion = MotionHost.Install { Scheduler = scheduler; Computed = ComputedStyles.forHost host }
    let graph = new Graph()
    let container = host.Document.createElement "div"
    host.Document.body.appendChild container |> ignore

    let rows () =
        let found = container.querySelectorAll "li"
        [ for index in 0u .. found.length - 1u -> unbox<Element> (found.item index) ]

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

        graph.Run(fun () -> Reactive.policy().Value <- Reduced)
        graph.Run(fun () -> items.Value <- [| 3 |])
        require "reduced policy removes a row at once" (texts () = [ "3" ])
    finally
        graph.Run(fun () -> mounted.Dispose())
        graph.Dispose()
        host.Document.body.removeChild container |> ignore
