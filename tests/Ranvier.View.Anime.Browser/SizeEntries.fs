/// <summary>
/// The bundle entries of motion-size.mjs: each mounts one list whose rows use one Motion part, and each twin mounts the
/// same list with the seam hooks calling anime by hand.
/// </summary>
module SizeEntries

open Fable.Core
open Fable.Core.TS.Dom
open global.Ranvier
open Fable.Ranvier.Anime
open Ranvier.Anime
open Ranvier.View
open Ranvier.View.Dom
open Ranvier.View.Sugar
open Ranvier.View.Anime

type Board =
    {
        Items: Signal<int array>
        Level: Signal<float>
        Position: Signal<struct (float * float)>
    }

let private mountRows (target: HTMLElement) (view: Component<Board>) =
    use graph = new Graph ()

    let board =
        graph.Run (fun () ->
            {
                Items = Api.createSignal [| 1; 2; 3 |]
                Level = Api.createSignal 0.
                Position = Api.createSignal (struct (0., 0.))
            })

    let mounted =
        Mounting.mount graph None ignore (unbox target) None (fun context -> Program.run (Component.render view board) context.Backend)

    graph.Run (fun () -> board.Items.Value <- [| 3; 1 |])
    mounted.Flush ()
    mounted.Dispose ()

let private listOf (row: Part<Ranvier.Dom.HTMLLIElement, unit -> int>) (parent: Part<Ranvier.Dom.HTMLUListElement, Board>) =
    Component.template
        (fun _ (board: Board) -> board)
        (ul parent { forEach id (fun (b: Board) -> b.Items.Value) (li row { fun (item: unit -> int) -> string (item ()) }) })

let private rowEnter (_: unit -> int) (node: Element) =
    Fable.Ranvier.Anime.Anime.animate (node, Anim (opacity = Value.fromTo (0, 1), duration = 240))

let private rowExit (_: unit -> int) (node: Element) =
    Fable.Ranvier.Anime.Anime.animate (node, Anim (opacity = 0, duration = 180))

let private rowOn (_: unit -> int) (node: Element) =
    Fable.Ranvier.Anime.Anime.animate (node, Anim (opacity = 0.5, duration = 240))

let private still: Part<Ranvier.Dom.HTMLUListElement, Board> =
    Attrs.className "rows"

/// <summary>Rows with <c>Motion.presence</c>.</summary>
let presence (target: HTMLElement) =
    mountRows target (listOf (Motion.presence rowEnter rowExit) still)

[<Emit("(previous => { $0.onComplete = self => { if (previous) previous(self); $1(); }; })($0.onComplete)")>]
let private whenComplete (animation: JSAnimation) (action: unit -> unit) : unit = jsNative

/// <summary>Rows with a motion hook and an exit hook calling <c>animate</c> by hand.</summary>
let presenceTwin (target: HTMLElement) =
    let enter =
        Hooks.motion (
            System.Func<_, _, _, _>(fun (item: unit -> int) (node: Ranvier.Dom.HTMLLIElement) context ->
                if context.Inserted then
                    let animation = rowEnter item (unbox node)
                    fun () -> animation.revert ()
                else
                    ignore)
        )

    let exit =
        Hooks.exit (
            System.Func<_, _, _, _>(fun (item: unit -> int) (node: Ranvier.Dom.HTMLLIElement) finish ->
                let animation = rowExit item (unbox node)
                whenComplete animation finish
                fun () -> animation.cancel ())
        )

    let view =
        Component.template
            (fun _ (board: Board) -> board)
            (ul still { forEach id (fun (b: Board) -> b.Items.Value) (li (enter, exit) { fun (item: unit -> int) -> string (item ()) }) })

    mountRows target view

let private flipParameters () =
    LayoutParams (duration = 350, ease = Ease.outQuart)

/// <summary>A list with <c>Motion.flip</c>.</summary>
let flip (target: HTMLElement) =
    mountRows target (listOf (Attrs.className "row") (Motion.flip { LayoutOptions.Default.WithDuration 350u with Ease = fun t -> 1. - (1. - t) ** 4. }))

/// <summary>A list with a reflow hook calling <c>createLayout</c> by hand.</summary>
let flipTwin (target: HTMLElement) =
    let reflow =
        Hooks.reflow (fun (_: Board) (node: Ranvier.Dom.HTMLUListElement) ->
            let layout = Fable.Ranvier.Anime.Anime.layout (unbox<Element> node, flipParameters ())
            Api.onCleanup (fun () -> layout.revert ())

            {
                Before = fun () -> layout.record () |> ignore
                After = fun () -> layout.animate () |> ignore
            })

    mountRows target (listOf (Attrs.className "row") reflow)

/// <summary>Rows with no Motion part, the baseline of the isolation entries.</summary>
let plain (target: HTMLElement) =
    mountRows target (listOf (Attrs.className "row") still)

let on (target: HTMLElement) =
    mountRows target (listOf (Motion.on rowOn) still)

let appear (target: HTMLElement) =
    mountRows target (listOf (Motion.appear rowOn) still)

let enter (target: HTMLElement) =
    mountRows target (listOf (Motion.enter rowEnter) still)

let exit (target: HTMLElement) =
    mountRows target (listOf (Motion.exit rowExit) still)

let fade (target: HTMLElement) =
    mountRows target (listOf Motion.fade still)

let collapse (target: HTMLElement) =
    mountRows target (listOf Motion.collapse still)

let follow (target: HTMLElement) =
    mountRows target (listOf (Attrs.className "row") (Motion.follow "opacity" { ComposedAnimationOptions.Default with Duration = 400. } (fun b -> b.Level.Value)))

let animateOn (target: HTMLElement) =
    mountRows
        target
        (listOf (Attrs.className "row") (Motion.animateOn (fun b -> b.Level.Value) (fun _ node level -> Fable.Ranvier.Anime.Anime.animate (node, Anim (opacity = level)))))

let draw (target: HTMLElement) =
    mountRows target (listOf (Attrs.className "row") (Motion.draw { ComposedAnimationOptions.Default with Duration = 400. } (fun b -> b.Level.Value)))

let morph (target: HTMLElement) =
    mountRows target (listOf (Attrs.className "row") (Motion.morph { ComposedAnimationOptions.Default with Duration = 400. } (fun b -> string b.Level.Value)))

let scroll (target: HTMLElement) =
    mountRows
        target
        (listOf
            (Attrs.className "row")
            (Motion.scroll
                ScrollParams.Default
                (fun _ ->
                    {
                        Progress = ValueNone
                        InView = ValueNone
                    })
                (fun _ node -> Fable.Ranvier.Anime.Anime.animate (node, Anim (opacity = 1)))))

let drag (target: HTMLElement) =
    mountRows
        target
        (listOf
            (Attrs.className "row")
            (Motion.drag NativeDraggableOptions.Default (fun b ->
                {
                    Position = b.Position
                    Dragging = ValueNone
                    Slot = ValueNone
                })))

let split (target: HTMLElement) =
    mountRows
        target
        (listOf
            (Attrs.className "row")
            (Motion.split { TextSplitterOptions.Default with Chars = SplitMode.Enabled } (fun _ splitter -> Anime.animateTargets (Array.map box splitter.Chars) [| PropertySpec.Scalar ("opacity", 1.) |] ComposedAnimationOptions.Default)))
