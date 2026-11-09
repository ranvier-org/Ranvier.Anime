/// <summary>A call of each binding not run by the value checks, compiled so that the emit checks read its JavaScript.</summary>
module Surface

open Fable.Core.TS.Dom
open Ranvier.Anime

let waapi (el: Element) =
    Waapi.animate (el, WaapiParams (opacity = Value.fromTo (0, 1), duration = 200, ease = Ease.named "ease-out"))

let engine () =
    Engine.settings.speed <- 2.
    Engine.update ()

let number (el: Element) =
    Utils.getNumber (el, "x")

let svg (path: Element) (el: Element) =
    let motion = Svg.motionPath (Targets.selector "#track")

    Anime.animate (el, Anim (translateX = motion.translateX, translateY = motion.translateY, rotate = motion.rotate, d = Svg.morphTo (path, 0)))
    |> ignore

    Anime.animate (Svg.drawable (path, 0., 1.), Anim (draw = "0 1", duration = 600))

let draggable (el: Element) =
    Anime.draggable (
        el,
        DraggableParams (
            container = "section",
            x = DragAxis.options (snap = 10),
            y = false,
            releaseEase = Ease.spring (bounce = 0.4),
            onDrag = (fun drag -> drag.setX (drag.x, true) |> ignore)
        )
    )

let scroll (el: Element) =
    let observer =
        Anime.onScroll (ScrollParams (target = el, enter = "bottom top", leave = Threshold.edges ("top", "bottom"), sync = 0.25))

    Anime.animate (el, Anim (x = 100, autoplay = observer))

let layout (el: Element) =
    let recorder =
        Anime.layout (el, LayoutParams (children = Targets.selector "li", duration = 300))

    recorder.record () |> ignore
    recorder.animate (LayoutParams (enterFrom = Anim (opacity = 0), leaveTo = Anim (opacity = 0), ease = Ease.outQuart))

let text (el: Element) =
    let splitter =
        Text.split (el, SplitParams (chars = SplitValue.template (``class`` = "char"), words = true))

    splitter.addEffect (fun split ->
        Anime.animate (split.chars, Anim (y = Value.keys [| 0; -10; 0 |], delay = Value.stagger (20, from = StaggerFrom.center))))
    |> ignore

    Anime.animate (el, Anim().With("innerHTML", Anime.scramble (ScrambleParams (chars = "A-Z", onChange = (fun text progress -> ())))))

let keyframes (el: Element) =
    Anime.animate (
        el,
        Anim (keyframes = Keyframes.percent [| "0%", Anim (x = 0); "50%", Anim (x = 100, ease = Ease.outBackBy 2.) |], loop = Loop.forever)
    )

let scope (el: Element) =
    let scope =
        Anime.scope (
            ScopeParams (root = el, defaults = Anim (duration = 250), mediaQueries = Pairs.ofArray [| "reduced", "(prefers-reduced-motion)" |])
        )

    scope.add (fun scope ->
        Anime.animate (Targets.selector ".dot", Anim (scale = Value.tween (``to`` = 2, duration = 100)))
        |> ignore)

let timer () =
    Anime.timer (TimerParams (duration = 100, loop = 3, onLoop = (fun timer -> timer.pause ())))
