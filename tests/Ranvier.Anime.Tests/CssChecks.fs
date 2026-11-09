/// <summary>Typed CSS animation checks against Anime's JavaScript engine and browser CSS evaluation.</summary>
module CssChecks

open Fable.Core
open Fable.Core.TS.Dom
open global.Ranvier
open Ranvier.Anime
open Ranvier.Css

type private Length = class end

let private px n : Ranvier.Css.Value<Length> =
    Generated.number n "px"

let private gap: CustomProperty<Length> = CustomProperty.create "--gap"

[<Global>]
let private document: Document = jsNative

[<Emit("$0.style.getPropertyValue($1)")>]
let private styleOf (element: Element) (property: string) : string = jsNative

[<Emit("$0.style.setProperty($1, $2, $3)")>]
let private setStyle (element: Element) (property: string) (value: string) (priority: string) : unit = jsNative

[<Emit("$0.style.getPropertyPriority($1)")>]
let private priorityOf (element: Element) (property: string) : string = jsNative

let private equal expected actual =
    if expected <> actual then
        failwithf "Expected %A; got %A" expected actual

let private element () : Element =
    let el = document.createElement "div"
    document.body.appendChild el |> ignore
    el

let private rejects action =
    let mutable rejected = false

    try
        action ()
    with :? System.ArgumentException ->
        rejected <- true

    equal true rejected

let private endpoints _ =
    let el = element ()
    el.className <- "user-class"

    let parameters =
        Anim(opacity = 0.5, duration = 1000, ease = Ease.linear, autoplay = false).With(gap, px 0., px 100.)

    let animation = Anime.animate (el, parameters)
    animation.seek 500.
    equal "50px" (styleOf el "--gap")
    equal "0.75" (styleOf el "opacity")
    equal "user-class" el.className
    animation.revert ()
    equal "" (styleOf el "--gap")

let private keyframes _ =
    let el = element ()
    let frames = [| px 0.; px 100.; px 20. |]

    let animation =
        Anime.animate (el, Anim(duration = 1000, ease = Ease.linear, autoplay = false).With(gap, frames))

    animation.seek 1000.
    equal "20px" (styleOf el "--gap")
    animation.revert ()

let private numericFamilies _ =
    let el = element ()
    let angle: CustomProperty<int> = CustomProperty.create "--angle"
    let fraction: CustomProperty<float> = CustomProperty.create "--fraction"
    let percentage: CustomProperty<bool> = CustomProperty.create "--percentage"

    let degrees n : Ranvier.Css.Value<int> =
        Generated.number n "deg"

    let number n : Ranvier.Css.Value<float> =
        Generated.number n ""

    let percent n : Ranvier.Css.Value<bool> =
        Generated.number n "%"

    let parameters =
        Anim(duration = 1000, ease = Ease.linear, autoplay = false)
            .With(angle, degrees 0., degrees 90.)
            .With(fraction, number 0., number 1.)
            .With(percentage, percent 0., percent 100.)

    let animation = Anime.animate (el, parameters)
    animation.seek 500.
    equal "45deg" (styleOf el "--angle")
    equal "0.5" (styleOf el "--fraction")
    equal "50%" (styleOf el "--percentage")
    animation.revert ()

let private validation _ =
    let parameters = Anim().With(gap, px 4.)
    let em: Ranvier.Css.Value<Length> = Generated.number 2. "em"
    rejects (fun () -> parameters.With (gap, px 0., em) |> ignore)
    rejects (fun () -> parameters.With (gap, [| px 0.; em |]) |> ignore)

    rejects (fun () ->
        parameters.With (gap, ([||]: Ranvier.Css.Value<Length> array))
        |> ignore)

    let expression: Ranvier.Css.Value<Length> = Generated.value "calc(2px + 3px)"
    rejects (fun () -> parameters.With (gap, expression) |> ignore)
    let el = element ()
    Utils.set (el, parameters) |> ignore
    equal "4px" (styleOf el "--gap")

let private reactive (clock: float -> unit) =
    use graph = new Graph ()
    let el = element ()
    setStyle el "--gap" "7px" "important"
    let mutable reads = 0

    let target, owner =
        graph.Run (fun () ->
            Api.createRoot (fun owner ->
                let target = Api.createSignal 0.

                Reactive.driveCss
                    (fun () ->
                        reads <- reads + 1
                        px target.Value)
                    el
                    gap
                    (AnimatableProp (duration = 1000., ease = Ease.linear))

                target, owner))

    equal "0px" (styleOf el "--gap")
    graph.Run (fun () -> target.Value <- 100.)
    clock 0.
    Testing.step ()
    clock 500.
    Testing.step ()
    let midway = styleOf el "--gap"
    equal "51.2px" midway
    equal 2 reads
    graph.Run (fun () -> target.Value <- 200.)
    equal midway (styleOf el "--gap")
    clock 0.
    Testing.step ()
    clock 1100.
    Testing.step ()
    equal "200px" (styleOf el "--gap")
    equal 3 reads
    graph.Run (fun () -> owner.Dispose ())
    equal "7px" (styleOf el "--gap")
    equal "important" (priorityOf el "--gap")
    graph.Run (fun () -> target.Value <- 300.)
    clock 1100.
    Testing.step ()
    equal "7px" (styleOf el "--gap")

[<Emit("getComputedStyle($0, $1).width")>]
let private computedWidth (element: Element) (pseudo: string) : string = jsNative

/// <summary>Checks browser evaluation of numeric CSS expressions and pseudo-elements during a typed variable animation.</summary>
let browserExpressions () =
    Testing.manual ()
    let el = element ()
    let reference = CalculationRuntime.reference "length" "--gap" None

    let expression =
        CalculationRuntime.binary
            "+"
            (CalculationRuntime.binary "*" reference (CalculationRuntime.literal "number" "" 2.))
            (CalculationRuntime.literal "length" "px" 10.)

    let width =
        CalculationRuntime.declaration
            "width"
            Ranvier.Css.Spec.NumericSemantics.dimensionOnlyContext
            (Ranvier.Css.Spec.NumericSemantics.dimension Ranvier.Css.Spec.NumericDimension.Length)
            expression

    let definition = Style.create [ Style.declarations [ width ] ]
    let style = document.createElement "style"

    style.textContent <-
        Stylesheet.render definition.Definition.Rules
        + "."
        + definition.Definition.ClassName
        + "::before { content: ''; display: block; width: calc(var(--gap) + 5px); }"

    document.head.appendChild style |> ignore
    el.className <- "user-class " + definition.Definition.ClassName

    let animation =
        Anime.animate (el, Anim(duration = 1000, ease = Ease.linear, autoplay = false).With(gap, px 0., px 100.))

    try
        equal "10px" (computedWidth el "")
        equal "5px" (computedWidth el "::before")
        animation.seek 500.
        equal "110px" (computedWidth el "")
        equal "55px" (computedWidth el "::before")
        animation.seek 1000.
        equal "210px" (computedWidth el "")
        equal "105px" (computedWidth el "::before")
        equal ("user-class " + definition.Definition.ClassName) el.className
    finally
        animation.revert ()
        el.remove ()
        style.remove ()

/// <summary>Runtime checks sharing the test runner's manually advanced animation clock.</summary>
let cases: (string * ((float -> unit) -> unit)) array =
    [|
        "typed CSS endpoints retain ordinary properties and classes", endpoints
        "typed CSS keyframes reach their final value", keyframes
        "typed custom properties retain angle, number and percentage units", numericFamilies
        "incompatible CSS endpoints fail before mutating parameters", validation
        "reactive CSS retargets from the current value and restores the original style", reactive
    |]
