/// <summary>Fable runtime integration of the generated CSS sugars and Anime's typed variable endpoints.</summary>
module SugarChecks

open Fable.Core
open Fable.Core.TS.Dom
open Ranvier.Anime
open Ranvier.Css

[<Global>]
let private document: Document = jsNative

[<Emit("$0.style.getPropertyValue($1)")>]
let private styleOf (element: Element) (property: string) : string = jsNative

let private equal expected actual =
    if expected <> actual then
        failwithf "Expected %A; got %A" expected actual

/// <summary>Checks generated numeric-wrapper metadata, typed endpoint/keyframe emission and expression serialization under Fable.</summary>
let run () =
    let el: Element = document.createElement "div"
    document.body.appendChild el |> ignore
    let gap: CustomProperty<CssValue.Length.Value> = CustomProperty.create "--gap"
    let finish = Css.px 100.
    equal (Some (100., "px")) (RuntimeApi.tryNumericValue finish)

    let animation =
        Anime.animate (el, Anim(duration = 1000, ease = Ease.linear, autoplay = false).With(gap, Css.px 0., finish))

    animation.seek 500.
    equal "50px" (styleOf el "--gap")
    animation.revert ()

    let frames =
        Anime.animate (el, Anim(duration = 1000, autoplay = false).With(gap, [| Css.px 0.; Css.px 100.; Css.px 20. |]))

    frames.seek 1000.
    equal "20px" (styleOf el "--gap")
    frames.revert ()

    let expression =
        Css.width (
            CssValue.Length.calc (
                CssValue.Length.variableWithFallback "--gap" (Css.px 0.)
                * Css.number 2.
                + Css.px 10.
            )
        )

    equal "width: calc(var(--gap, 0px) * 2 + 10px);" (RuntimeApi.renderDeclaration expression)
    el.remove ()
