/// <summary>
/// The server render of every native README template and of the presets equals the render of its twin without Motion
/// parts: the hooks behind the parts add no markup.
/// </summary>
module Ranvier.Anime.NativeSmoke.MotionServerProbe

open System
open Ranvier
open Ranvier.View
open Ranvier.View.Sugar
open Ranvier.Anime.NativeSmoke.MotionFixtures

let private boardTwinOf (selected: bool) =
    Component.template
        (fun _ (cards: Card array) ->
            {
                Cards = Api.createSignal cards
                Selected = Api.createSignal selected
            })
        (section () {
            ul (Attrs.className "cards") { forEach (fun card -> card.Id) (fun b -> b.Cards.Value) (li () { fun current -> (current ()).Title }) }
            show (fun b -> b.Selected.Value) (p () { "Selected" })
        })

let private selectedBoard =
    Component.template
        (fun _ (cards: Card array) ->
            {
                Cards = Api.createSignal cards
                Selected = Api.createSignal true
            })
        (section () {
            ul (Attrs.className "cards", Ranvier.View.Anime.Motion.flip Ranvier.Anime.LayoutOptions.Default) {
                forEach (fun card -> card.Id) (fun b -> b.Cards.Value) (li (Ranvier.View.Anime.Motion.fade) { fun current -> (current ()).Title })
            }

            show (fun b -> b.Selected.Value) (p (Ranvier.View.Anime.Motion.fade) { "Selected" })
        })

let private bannerTwin =
    Component.template (fun _ (text: string) -> { Text = text }) (header () {
        h1 () { fun b -> b.Text }
        span () { "*" }
    })

let private toastsTwin =
    Component.template
        (fun _ () -> { Messages = Api.createSignal [||] })
        (ol () { forEach id (fun (t: Toast) -> t.Messages.Value) (li () { fun message -> message () }) })

let private notesTwin =
    Component.template
        (fun _ () -> { Notes = [| "a"; "b" |] })
        (ol () { forEachFixed id (fun (n: Notes) -> n.Notes) (li () { fun (note: string) -> note }) })

let private gaugeTwin =
    Component.template
        (fun _ (load: float) ->
            {
                Load = Api.createSignal load
                Label = Api.createSignal "idle"
            })
        (div () {
            div (Attrs.className "bar") { () }
            span () { fun g -> g.Label.Value }
        })

let private storyTwin =
    Component.template
        (fun _ () ->
            {
                Progress = Api.createSignal 0.
                Seen = Api.createSignal false
                Puck = Api.createSignal (struct (0., 0.))
            })
        (article () {
            h2 () { "Ranvier" }
            section () { fun s -> string (round (s.Progress.Value * 100.)) + " %" }
            div (Attrs.className "puck") { "drag" }
        })

let private chartTwin =
    Component.template
        (fun _ () ->
            {
                Fraction = Api.createSignal 0.
                Line = Api.createSignal "M0 0 L100 0"
            })
        (svg () {
            path () { () }
            path () { () }
        })

let private presetsTwin =
    Component.template
        (fun _ () -> { Books = [| "a"; "b" |] })
        (div () {
            ul () { forEachFixed id (fun (s: Shelf) -> s.Books) (li () { fun (book: string) -> book }) }
            ul () { forEachFixed id (fun (s: Shelf) -> s.Books) (li () { fun (book: string) -> book }) }
            ul () { forEachFixed id (fun (s: Shelf) -> s.Books) (li () { fun (book: string) -> book }) }
        })

let private cards = [| { Id = 1; Title = "One" }; { Id = 2; Title = "Two" } |]

let private render (definition: Component<'props>) (props: 'props) =
    Server.render Server.Options.defaults definition props

/// <summary>Renders each Motion template and its twin on the server and requires equal HTML.</summary>
let run () =
    let comparisons =
        [
            "board", (fun () -> render board cards, render (boardTwinOf false) cards)
            "selected board", (fun () -> render selectedBoard cards, render (boardTwinOf true) cards)
            "banner", (fun () -> render banner "Hello", render bannerTwin "Hello")
            "toasts", (fun () -> render toasts (), render toastsTwin ())
            "notes", (fun () -> render notes (), render notesTwin ())
            "gauge", (fun () -> render gauge 0.5, render gaugeTwin 0.5)
            "story", (fun () -> render story (), render storyTwin ())
            "chart", (fun () -> render chart (), render chartTwin ())
            "presets", (fun () -> render presets (), render presetsTwin ())
        ]

    for name, compare in comparisons do
        let withMotion, twin = compare ()

        if withMotion <> twin then
            failwith ("Motion server render: " + name + "\n  motion: " + withMotion + "\n  twin:   " + twin)

        Console.WriteLine("PASS motion server render " + name + " equals its twin")
