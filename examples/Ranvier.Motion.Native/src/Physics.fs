/// <summary>
/// A spring playground: a puck thrown with a spring release, a chain of followers trailing it, a timeline scrubbed by a
/// range input, and the motion policy switched by hand.
/// </summary>
module Physics

open global.Ranvier
open Ranvier.Anime
open Ranvier.Dom
open Ranvier.View
open Ranvier.View.Sugar
open Ranvier.View.Anime

[<NoEquality; NoComparison>]
type Playground =
    {
        /// <summary>The puck's offset from its home, in pixels.</summary>
        Puck: Signal<struct (float * float)>
        Dragging: Signal<bool>
        /// <summary>The scrubbed timeline's progress, in 0..1.</summary>
        Scrub: Signal<float>
        Tips: Signal<bool>
        Policy: Signal<Reactive.MotionPolicy>
        /// <summary>The scrubbed timeline once placed.</summary>
        Timeline: ComposedTimeline ref
        /// <summary>The spring returning the puck home; a new spring, a grab or the instance's disposal cancels it.</summary>
        Spring: MotionSlot
    }

/// <summary>The followers' durations: each trails the puck further behind the one before it.</summary>
let trail = [| 140.; 260.; 380.; 500.; 620. |]

let private create () =
    {
        Puck = Api.createSignal (struct (0., 0.))
        Dragging = Api.createSignal false
        Scrub = Api.createSignal 0.
        Tips = Api.createSignal false
        Policy = Reactive.policy ()
        Timeline = ref Unchecked.defaultof<ComposedTimeline>
        Spring = Motion.slot ()
    }

let private reduced (playground: Playground) =
    Api.untrack (fun () -> playground.Policy.Value) = Reactive.Reduced

/// <summary>
/// Returns the puck home along a spring, through the position signal its draggable follows, in place of any running
/// spring; under the reduced policy it jumps home.
/// </summary>
let springHome (playground: Playground) =
    let struct (x, y) = Api.untrack (fun () -> playground.Puck.Value)

    if reduced playground then
        playground.Spring.Stop ()
        playground.Puck.Value <- struct (0., 0.)
    else
        let graph = Graph.Current

        playground.Spring.Play (fun () ->
            Native.springPoint 170. 11. (struct (x, y)) (fun point -> graph.Batch (fun () -> playground.Puck.Value <- point)))

let private xOf (playground: Playground) =
    let struct (x, _) = playground.Puck.Value
    x

let private yOf (playground: Playground) =
    let struct (_, y) = playground.Puck.Value
    y

// The track's dots fan out, turn and change colour along one timeline that the range input seeks.
// anime staggers values and delays across one child's targets; here each dot is its own child, at its own offset.
let private scrubbed (playground: Playground) (track: Element) =
    let dots = Native.queryAll track ".dot"
    let defaults = Native.timing 600. Native.inOutQuad
    let heights = Native.spread -70. 70. dots.Length
    let first = Native.stagger 60. StaggerOrigin.First StaggerGrid.Flat dots.Length
    let second = Native.stagger 60. StaggerOrigin.Center StaggerGrid.Flat dots.Length
    let timeline = Native.timeline false

    dots
    |> Array.iteri (fun index dot ->
        timeline.Add ([| box dot |], [| Native.toward "y" heights[index]; Native.toward "rotate" 180. |], defaults, TimelinePosition.At first[index])
        |> ignore)

    let settled = 600. + Array.max first

    dots
    |> Array.iteri (fun index dot ->
        timeline.Add (
            [| box dot |],
            [| Native.fromTo "scale" 1. 1.6; PropertySpec.Text ("borderRadius", "20%") |],
            defaults,
            TimelinePosition.At (settled + second[index])
        )
        |> ignore)

    playground.Timeline.Value <- timeline
    Native.scrub (fun () -> playground.Scrub.Value) timeline
    timeline

let private follower (index: int) =
    let parameters = { ComposedAnimationOptions.Default with Duration = trail[index]; Ease = Native.outQuart }

    div (Attrs.className "follower", Attrs.data ("follower", string index), Motion.follow "x" parameters xOf, Motion.follow "y" parameters yOf) { () }

let private tips =
    show
        (fun (p: Playground) -> p.Tips.Value)
        (aside (Attrs.className "tips", Motion.rise) {
            h3 () { "Try this" }

            ul () {
                li () { "Throw the puck: it keeps its speed, then a spring settles it inside the arena." }
                li () { "Drag the slider: the timeline seeks to its progress, forward or back." }
                li () { "Switch to reduced motion, then toggle this panel: it appears without its rise." }
            }
        })

/// <summary>The playground's component.</summary>
let view: Component<Playground -> unit> =
    Component.template
        (fun _ (publish: Playground -> unit) ->
            let playground = create ()
            publish playground
            playground)
        (section (Attrs.className "physics") {
            div (Attrs.className "arena") {
                for index in trail.Length - 1 .. -1 .. 0 do
                    follower index

                div (
                    Attrs.className (fun (p: Playground) -> if p.Dragging.Value then "puck grabbed" else "puck"),
                    Motion.drag
                        { NativeDraggableOptions.Default with
                            Container = fun () -> Native.select ".arena"
                            ReleaseStiffness = 120.
                            ReleaseDamping = 9.
                            ReleaseMass = 1.
                            Physics = fun () -> { DraggableSettings.Default with VelocityMultiplier = 1.4 } }
                        (fun (p: Playground) ->
                            {
                                Position = p.Puck
                                Dragging = ValueSome p.Dragging
                                Slot = ValueSome p.Spring
                            })
                ) {
                    "drag"
                }
            }

            div (Attrs.className "controls") {
                button (Attrs.className "primary home", Events.onClick (fun (p: Playground) _ -> springHome p)) { "Spring home" }

                span (Attrs.className "readout") {
                    fun (p: Playground) ->
                        let struct (x, y) = p.Puck.Value
                        sprintf "x %.0f · y %.0f" x y
                }

                label (Attrs.className "switch") {
                    input (
                        Attrs.Input.``type`` "checkbox",
                        Attrs.className "reduced",
                        Props.Input.``checked`` (fun (p: Playground) -> p.Policy.Value = Reactive.Reduced),
                        Events.onChange (fun (p: Playground) event ->
                            p.Policy.Value <-
                                if Native.targetChecked event then
                                    Reactive.Reduced
                                else
                                    Reactive.Full)
                    )

                    "Reduced motion"
                }

                button (Attrs.className "tips-toggle", Events.onClick (fun (p: Playground) _ -> p.Tips.Value <- not p.Tips.Value)) {
                    fun (p: Playground) -> if p.Tips.Value then "Hide tips" else "Show tips"
                }
            }

            div (Attrs.className "scrubber") {
                div (Attrs.className "track", Motion.on scrubbed) {
                    for index in 1..9 do
                        div (Attrs.className "dot", Attrs.data ("dot", string index)) { () }
                }

                input (
                    Attrs.Input.``type`` "range",
                    Attrs.attribute ("min", "0"),
                    Attrs.attribute ("max", "1000"),
                    Attrs.className "scrub",
                    Attrs.aria ("label", "Timeline progress"),
                    Props.Input.value (fun (p: Playground) -> string (System.Math.Round (p.Scrub.Value * 1000.))),
                    Events.onInput (fun (p: Playground) event -> p.Scrub.Value <- float (Native.targetValue event) / 1000.)
                )

                span (Attrs.className "readout") { fun (p: Playground) -> sprintf "%.0f%%" (p.Scrub.Value * 100.) }
            }

            div (Attrs.className "tips-frame") { tips }
        })

