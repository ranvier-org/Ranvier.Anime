/// <summary>
/// A scroll-driven landing page: scroll timelines write each section's progress into signals, the headline is split and
/// staggered, the gallery staggers across its grid, and tiles created later appear. The browser page is rendered by the
/// .NET server and hydrated; this native page mounts it directly, in the window's scrolling document.
/// </summary>
module Story

open global.Ranvier
open Ranvier.Anime
open Ranvier.Dom
open Ranvier.View
open Ranvier.View.Sugar
open Ranvier.View.Anime

/// <summary>A section's scroll progress, in 0..1, and whether it is between its thresholds.</summary>
[<NoEquality; NoComparison>]
type Chapter =
    {
        Progress: Signal<float>
        Seen: Signal<bool>
    }

/// <summary>The words of a chapter.</summary>
type Copy =
    {
        Title: string
        Text: string
        Mark: string
    }

[<NoEquality; NoComparison>]
type Story =
    {
        Chapters: Chapter array
        Gallery: Chapter
        /// <summary>Whether the tiles created on the client are shown.</summary>
        More: Signal<bool>
    }

let chapters =
    [|
        {
            Title = "Owned"
            Text =
                "Every animation is created when its element is placed and reverted when its owner is disposed. Nothing outlives the view that started it."
            Mark = "01"
        }
        {
            Title = "Reactive"
            Text =
                "Signals drive animations, and animations publish their progress back as signals, so the rail at the top is text bound to those signals."
            Mark = "02"
        }
        {
            Title = "Portable"
            Text =
                "The same template runs in the browser over anime.js and here over Ranvier.Anime. The motion parts add no markup, so the server render matches either."
            Mark = "03"
        }
    |]

/// <summary>The labels of the gallery's tiles, and of the tiles the extras button shows.</summary>
let tiles =
    [|
        "Enter"
        "Exit"
        "Flip"
        "Follow"
        "Draw"
        "Morph"
        "Scroll"
        "Drag"
        "Split"
        "Stagger"
        "Policy"
        "Hydrate"
    |]

let extras = [| "Spring"; "Timeline"; "Scrub"; "Track" |]

let private chapter () =
    {
        Progress = Api.createSignal 0.
        Seen = Api.createSignal false
    }

/// <summary>A story with every progress at 0, in the current graph's scope.</summary>
let create () : Story =
    {
        Chapters = Array.init chapters.Length (fun _ -> chapter ())
        Gallery = chapter ()
        More = Api.createSignal false
    }

let private percent (value: float) =
    sprintf "%.0f%%" (value * 100.)

let private scrubbed =
    { ScrollParams.Default with
        Options =
            { ScrollOptions.Default with
                Enter = { Container = "bottom"; Target = "top" }
                Leave = { Container = "top"; Target = "bottom" }
                Sync = ScrollSync.Smooth 0.25 } }

let private bound (read: Story -> Chapter) (story: Story) =
    let chapter = read story

    {
        Progress = ValueSome chapter.Progress
        InView = ValueSome chapter.Seen
    }

// A chapter's copy slides in from the side its art leaves, and the art turns and settles, scrubbed by the scroll.
let private chapterTimeline (index: int) (_: Story) (section: Element) =
    let side = if index % 2 = 0 then -1. else 1.
    let timeline = Native.timeline false

    timeline
        .Add(
            Native.queryAll section ".chapter-copy" |> Array.map box,
            [| Native.fromTo "x" (side * 80.) 0.; Native.fromTo "opacity" 0. 1. |],
            Native.timing 500. Native.outCubic
        )
        .Add(
            Native.queryAll section ".chapter-art" |> Array.map box,
            [| Native.fromTo "rotate" (side * -24.) 0.; Native.fromTo "scale" 0.7 1.; Native.fromTo "opacity" 0.2 1. |],
            Native.timing 500. Native.outBack,
            Native.withPrevious
        )

let private galleryTimeline (_: Story) (section: Element) =
    let tiles = Native.queryAll section ".tile"
    let delays = Native.stagger 60. StaggerOrigin.Center (StaggerGrid.Grid2D (4u, 3u)) tiles.Length

    Native.animateAll
        (Array.map box tiles)
        ({ Native.timing 500. Native.outQuart with Autoplay = false } |> Native.delayedBy delays)
        [| Native.fromTo "y" 60. 0.; Native.fromTo "scale" 0.6 1.; Native.fromTo "opacity" 0. 1. |]

let private rise (_: Story) (element: Element) =
    Native.animate element (Native.timing 600. Native.outQuart) [| Native.fromTo "y" 14. 0.; Native.fromTo "opacity" 0. 1. |]

let private bob (_: Story) (element: Element) =
    Native.animate element (Native.timing 900. Native.inOutSine |> Native.alternating) [| Native.toward "y" 8. |]

let private letters (_: Story) (splitter: TextSplitter) =
    let chars = splitter.Chars |> Array.map box
    let delays = Native.stagger 22. StaggerOrigin.First StaggerGrid.Flat chars.Length

    Native.animateAll
        chars
        (Native.timing 640. Native.outQuart |> Native.delayedBy delays)
        [| Native.fromTo "y" 28. 0.; Native.fromTo "opacity" 0. 1.; Native.fromTo "rotate" 8. 0. |]

let private popIn (_: string) (element: Element) =
    Native.animate element (Native.timing 420. Native.outBack) [| Native.fromTo "scale" 0.4 1.; Native.fromTo "opacity" 0. 1. |]

/// <summary>The story's component; its props receive each instance once set up.</summary>
let view: Component<Story -> unit> =
    Component.template
        (fun _ (publish: Story -> unit) ->
            let story = create ()
            publish story
            story)
        (article (Attrs.className "story") {
                    header (Attrs.className "hero") {
                        p (Attrs.className "eyebrow", Motion.appear rise) { "Mounted natively · animated by Ranvier.Anime" }

                        h1 (Attrs.className "headline", Motion.split { TextSplitterOptions.Default with Chars = SplitMode.Enabled; Words = SplitMode.Enabled } letters) {
                            "Motion, owned by the view."
                        }

                        p (Attrs.className "lede") {
                            "Scroll down. Each section below runs a Ranvier.Anime timeline scrubbed by the scroll, and writes how far it has come into a signal."
                        }

                        div (Attrs.className "cue", Attrs.aria ("hidden", "true"), Motion.on bob) { "↓" }
                    }

                    nav (Attrs.className "rail", Attrs.aria ("label", "Progress")) {
                        for index in 0 .. chapters.Length - 1 do
                            div (
                                Attrs.className (fun (s: Story) ->
                                    if s.Chapters[index].Seen.Value then
                                        "rail-item seen"
                                    else
                                        "rail-item"),
                                Attrs.attribute ("style", fun (s: Story) -> Some (sprintf "--p:%.3f" s.Chapters[index].Progress.Value))
                            ) {
                                span (Attrs.className "rail-mark") { chapters[index].Mark }
                                span (Attrs.className "rail-title") { chapters[index].Title }
                                span (Attrs.className "rail-value") { fun (s: Story) -> percent s.Chapters[index].Progress.Value }
                            }
                    }

                    for index in 0 .. chapters.Length - 1 do
                        section (
                            Attrs.className (if index % 2 = 0 then "chapter" else "chapter flipped"),
                            Attrs.data ("chapter", string index),
                            Motion.scroll scrubbed (bound (fun s -> s.Chapters[index])) (chapterTimeline index)
                        ) {
                            div (Attrs.className "chapter-copy") {
                                span (Attrs.className "chapter-mark") { chapters[index].Mark }
                                h2 () { chapters[index].Title }
                                p () { chapters[index].Text }
                            }

                            div (Attrs.className "chapter-art", Attrs.aria ("hidden", "true")) {
                                div (Attrs.className "orb") { () }
                                div (Attrs.className "ring") { () }
                            }
                        }

                    section (Attrs.className "gallery", Motion.scroll scrubbed (bound (fun s -> s.Gallery)) galleryTimeline) {
                        h2 () { "Twelve parts, one stagger" }
                        p (Attrs.className "gallery-note") { fun (s: Story) -> "Gallery " + percent s.Gallery.Progress.Value }

                        div (Attrs.className "tiles") {
                            for label in tiles do
                                div (Attrs.className "tile") { label }
                        }
                    }

                    section (Attrs.className "more") {
                        h2 () { "Four more, created on the client" }

                        button (Attrs.className "primary more-toggle", Events.onClick (fun (s: Story) _ -> s.More.Value <- not s.More.Value)) {
                            fun (s: Story) ->
                                if s.More.Value then
                                    "Hide the extras"
                                else
                                    "Show the extras"
                        }

                        show
                            (fun (s: Story) -> s.More.Value)
                            (div (Attrs.className "tiles extras") {
                                forEachFixed
                                    id
                                    (fun (_: Story) -> extras)
                                    (div (Attrs.className "tile extra", Motion.appear popIn) { fun (label: string) -> label })
                            })
                    }

                    footer (Attrs.className "story-end") { "Built with Ranvier and Ranvier.Anime." }
                })
