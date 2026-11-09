/// <summary>Call sites whose emitted JavaScript the emit checks and the bundle-size check read.</summary>
module Fixtures

open Fable.Core.TS.Dom
open Ranvier.Anime

/// <summary>The bundle-size entry: one animation, matched by <c>size.mjs</c>'s hand-written twin.</summary>
let sized (el: Element) =
    Anime.animate (el, Anim (x = 100, opacity = Value.fromTo (0, 1), duration = 400, ease = Ease.outQuart))

let single (el: Element) =
    Anime.animate (el, Anim (x = 1))

let mixed (el: Element) =
    Anime.animate (el, Anim (y = "50%", scale = 0.9, rotate = Value.from 45, delay = Value.stagger (20, start = 100)))

let callbacks (el: Element) (log: string -> unit) =
    Anime.animate (el, Anim (x = 1, onComplete = (fun _ -> log "done"), modifier = (fun v -> v * 2.)))

let custom (el: Element) =
    Anime.animate (el, Anim(duration = 300).With("--hue", 120))

let timeline (el: Element) =
    Anime.timeline(TimelineParams (autoplay = false)).add(el, Anim (x = 1), At.after 50).label("end")

let spring (el: Element) =
    Anime.animate (el, Anim (x = 1, ease = Ease.spring (stiffness = 200.)))

let bounds () =
    DraggableParams (container = DragContainer.bounds (0., 100., 50., 0.), snap = Snap.points (0., 10.))

let gridFrom (el: Element) =
    Anime.animate (el, Anim (delay = Value.stagger (10, grid = Grid.size (3, 3), from = StaggerFrom.at (0.5, 0.5))))
