/// <summary>The samples of src/Ranvier.View.Anime/README.md, in page order.</summary>
module Fixtures

open global.Ranvier
open Fable.Ranvier.Anime
open Ranvier.Anime
open Ranvier.View
open Ranvier.View.Sugar
open Ranvier.View.Anime

type Card = { Id: int; Title: string }

type Board =
    {
        Cards: Signal<Card array>
        Selected: Signal<bool>
    }

let board =
    Component.template
        (fun _ (cards: Card array) ->
            {
                Cards = Api.createSignal cards
                Selected = Api.createSignal false
            })
        (section () {
            ul (Attrs.className "cards", Motion.flip { LayoutOptions.Default.WithDuration 350u with Ease = fun t -> 1. - (1. - t) ** 4. }) {
                forEach
                    (fun card -> card.Id)
                    (fun b -> b.Cards.Value)
                    (li (
                        Motion.presence
                            (fun _ el -> Anime.animate el [| PropertySpec.Scalar ("opacity", 1., fromValue = 0.); PropertySpec.Scalar ("y", 0., fromValue = 12.) |] { ComposedAnimationOptions.Default with Duration = 240. })
                            (fun _ el -> Anime.animate el [| PropertySpec.Scalar ("opacity", 0.); PropertySpec.Scalar ("scale", 0.9) |] { ComposedAnimationOptions.Default with Duration = 180. })
                    ) {
                        fun current -> (current ()).Title
                    })
            }

            show (fun b -> b.Selected.Value) (p (Motion.fade) { "Selected" })
        })

type Banner = { Text: string }

let banner =
    Component.template
        (fun _ (text: string) -> { Text = text })
        (header () {
            h1 (Motion.appear (fun _ el -> Anime.animate el [| PropertySpec.Scalar ("opacity", 1., fromValue = 0.) |] { ComposedAnimationOptions.Default with Duration = 400. })) { fun b -> b.Text }

            span (Motion.on (fun _ el -> Anime.animate el [| PropertySpec.Scalar ("rotate", 360.) |] { ComposedAnimationOptions.Default with Duration = 1200.; Ease = Ease.named "linear" })) { "*" }
        })

type Toast = { Messages: Signal<string array> }

let toasts =
    Component.template
        (fun _ () -> { Messages = Api.createSignal [||] })
        (ol () {
            forEach
                id
                (fun (t: Toast) -> t.Messages.Value)
                (li (
                    Motion.enter (fun _ el -> Anime.animate el [| PropertySpec.Scalar ("x", 0., fromValue = 40.) |] { ComposedAnimationOptions.Default with Duration = 200. }),
                    Motion.exit (fun _ el -> Anime.animate el [| PropertySpec.Scalar ("x", 40.); PropertySpec.Scalar ("opacity", 0.) |] { ComposedAnimationOptions.Default with Duration = 200. })
                ) {
                    fun message -> message ()
                })
        })

type Spinner = { Glyph: string }

let spinner =
    Component.template
        (fun _ (glyph: string) -> { Glyph = glyph })
        (span (
            Motion.on (fun _ el ->
                Fable.Ranvier.Anime.Anime.animate (el, Anim (rotate = 360, duration = 1200, loop = Loop.forever, ease = Ease.linear)))
        ) {
            fun s -> s.Glyph
        })

type Gauge =
    {
        Load: Signal<float>
        Label: Signal<string>
    }

let gauge =
    Component.template
        (fun _ (load: float) ->
            {
                Load = Api.createSignal load
                Label = Api.createSignal "idle"
            })
        (div () {
            div (Attrs.className "bar", Motion.follow "scaleX" { ComposedAnimationOptions.Default with Duration = 400.; Ease = fun t -> 1. - (1. - t) ** 4. } (fun g -> g.Load.Value)) {
                ()
            }

            span (Motion.animateOn (fun g -> g.Label.Value) (fun _ el _ -> Anime.animate el [| PropertySpec.Scalar ("opacity", 1., fromValue = 0.) |] { ComposedAnimationOptions.Default with Duration = 200. })) {
                fun g -> g.Label.Value
            }
        })

type Story =
    {
        Progress: Signal<float>
        Seen: Signal<bool>
        Puck: Signal<struct (float * float)>
    }

let story =
    Component.template
        (fun _ () ->
            {
                Progress = Api.createSignal 0.
                Seen = Api.createSignal false
                Puck = Api.createSignal (struct (0., 0.))
            })
        (article () {
            h2 (
                Motion.split { TextSplitterOptions.Default with Chars = SplitMode.Enabled } (fun _ split ->
                    Anime.animateTargets (Array.map box split.Chars) [| PropertySpec.Scalar ("opacity", 1., fromValue = 0.) |] { ComposedAnimationOptions.Default with Duration = 400. })
            ) {
                "Ranvier"
            }

            section (
                Motion.scroll
                    { ScrollParams.Default with Options = { ScrollOptions.Default with Enter = { Container = "bottom"; Target = "top" }; Leave = { Container = "top"; Target = "bottom" }; Sync = ScrollSync.Smooth 1. } }
                    (fun s ->
                        {
                            Progress = ValueSome s.Progress
                            InView = ValueSome s.Seen
                        })
                    (fun _ el -> Anime.animate el [| PropertySpec.Scalar ("opacity", 1., fromValue = 0.2) |] { ComposedAnimationOptions.Default with Autoplay = false })
            ) {
                fun s -> string (round (s.Progress.Value * 100.)) + " %"
            }

            div (
                Attrs.className "puck",
                Motion.drag NativeDraggableOptions.Default (fun s ->
                    {
                        Position = s.Puck
                        Dragging = ValueNone
                        Slot = ValueNone
                    })
            ) {
                "drag"
            }
        })

type Chart =
    {
        Fraction: Signal<float>
        Line: Signal<string>
    }

let chart =
    Component.template
        (fun _ () ->
            {
                Fraction = Api.createSignal 0.
                Line = Api.createSignal "M0 0 L100 0"
            })
        (svg () {
            path (Motion.draw { ComposedAnimationOptions.Default with Duration = 300. } (fun c -> c.Fraction.Value)) { () }
            path (Motion.morph { ComposedAnimationOptions.Default with Duration = 300. } (fun c -> c.Line.Value)) { () }
        })

/// <summary>A point anime tweens in place.</summary>
type Point = { mutable x: float; mutable y: float }

type Tile =
    {
        Offset: Signal<struct (float * float)>
        Spring: MotionSlot
    }

let springHome (tile: Tile) =
    let graph = Graph.Current
    let struct (x, y) = Api.untrack (fun () -> tile.Offset.Value)
    let point = { x = x; y = y }

    tile.Spring.Play (fun () ->
        Fable.Ranvier.Anime.Anime.animate (
            Targets.object point,
            Anim (
                x = 0,
                y = 0,
                ease = Ease.spring (stiffness = 170., damping = 11.),
                onUpdate = fun _ -> graph.Batch (fun () -> tile.Offset.Value <- struct (point.x, point.y))
            )
        ))

let tile =
    Component.template
        (fun _ () ->
            {
                Offset = Api.createSignal (struct (0., 0.))
                Spring = Motion.slot ()
            })
        (div () {
            div (
                Attrs.className "tile",
                Motion.drag NativeDraggableOptions.Default (fun t ->
                    {
                        Position = t.Offset
                        Dragging = ValueNone
                        Slot = ValueSome t.Spring
                    })
            ) {
                "drag"
            }

            button (Events.onClick (fun (t: Tile) _ -> springHome t)) { "Home" }
        })
