/// <summary>Value checks of the portable Ranvier.Anime surface under jsdom, on a clock advanced by the test.</summary>
module PortableChecks

open Fable.Core
open global.Ranvier
open Ranvier.Anime
open Ranvier.Anime.Dom

[<Global>]
let private document: Fable.Core.TS.Dom.Document = jsNative

[<Emit("$0.style.getPropertyValue($1)")>]
let private styleOf (element: Element) (property: string) : string = jsNative

[<Emit("$0.style.setProperty($1, $2)")>]
let private setStyle (element: Element) (property: string) (value: string) : unit = jsNative

[<Emit("JSON.stringify($0)")>]
let private json (value: obj) : string = jsNative

[<Emit("Promise.resolve()")>]
let private resolved () : JS.Promise<unit> = jsNative

[<Emit("$0[$1]")>]
let private field (target: obj) (name: string) : obj = jsNative

/// <summary>Advances the mocked <c>Date</c> by the given milliseconds.</summary>
type Clock = float -> unit

let private equal expected actual =
    if expected <> actual then
        failwithf "Expected %A; got %A" expected actual

let private element () : Element =
    let element = document.createElement "div"
    document.body.appendChild element |> ignore
    element

let private advance (clock: Clock) (ms: float) =
    clock ms
    Fable.Ranvier.Anime.Testing.step ()

let private rejects action =
    let mutable rejected = false

    try
        action ()
    with :? System.ArgumentException ->
        rejected <- true

    equal true rejected

let private linear =
    { ComposedAnimationOptions.Default with
        Duration = 1000.
        Ease = Ease.named "linear" }

let private animateScalar (clock: Clock) =
    let el = element ()

    let animation: ComposedAnimation =
        Anime.animate (box el) [| PropertySpec.Scalar ("opacity", 1., fromValue = 0.) |] linear

    advance clock 0.
    advance clock 500.
    equal "0.512" (styleOf el "opacity")
    equal false animation.Completed
    advance clock 600.
    equal "1" (styleOf el "opacity")
    equal true animation.Completed
    equal 1000. animation.Duration

let private animateOptions (clock: Clock) =
    let el = element ()

    let animation =
        Anime.animateTargets [| box el |] [| PropertySpec.Text ("width", "100px", fromValue = "0px") |] { linear with Autoplay = false }

    equal true animation.Paused
    animation.Seek 500. |> ignore
    equal "50px" (styleOf el "width")
    animation.Progress <- 1.
    equal "100px" (styleOf el "width")
    animation.Revert () |> ignore
    equal "" (styleOf el "width")

let private specEntries (_: Clock) =
    let double (value: float) = value * 2.

    equal """["opacity",{"to":1}]""" (json (PropertySpec.Scalar ("opacity", 1.)))
    equal """["x",{"from":0,"to":100,"duration":200,"delay":20}]""" (json (PropertySpec.Scalar ("x", 100., fromValue = 0., duration = 200., delay = 20.)))
    equal """["color",{"from":"#000","to":"#fff"}]""" (json (PropertySpec.Text ("color", "#fff", fromValue = "#000")))

    let eased = PropertySpec.Scalar ("y", 5., ease = double, modifier = double)
    let entry = field (box eased) "1"
    equal true (obj.ReferenceEquals (field entry "ease", double))
    equal true (obj.ReferenceEquals (field entry "modifier", double))
    equal (box 120.) (box (TimelinePosition.At 120.))
    equal (box "+=40") (box (TimelinePosition.Expression "+=40"))

let private whenCompleted (clock: Clock) =
    let el = element ()
    let log = ResizeArray<string> ()

    let animation =
        Anime.animate (box el) [| PropertySpec.Scalar ("opacity", 1.) |] { linear with Duration = 100. }

    let tickable: ITickable = animation
    tickable.WhenCompleted (fun () -> log.Add "first")
    tickable.WhenCompleted (fun () -> log.Add "second")
    advance clock 0.
    advance clock 50.
    equal [] (List.ofSeq log)
    advance clock 100.
    equal [ "first"; "second" ] (List.ofSeq log)
    tickable.WhenCompleted (fun () -> log.Add "late")
    equal [ "first"; "second"; "late" ] (List.ofSeq log)

    let dropped =
        Anime.animate (box el) [| PropertySpec.Scalar ("opacity", 0.) |] { linear with Duration = 100. }

    (dropped :> ITickable).WhenCompleted (fun () -> log.Add "cancelled")
    dropped.Cancel ()
    equal true dropped.Cancelled
    advance clock 200.
    equal 3 log.Count

let private timelineAdds (_: Clock) =
    let el = element ()
    let timeline: ComposedTimeline = Anime.timeline ()
    timeline.Pause () |> ignore

    timeline
        .Add([| box el |], [| PropertySpec.Text ("width", "100px", fromValue = "0px") |], options = linear)
        .Label("half", position = TimelinePosition.At 500.)
        .Add([| box el |], [| PropertySpec.Text ("height", "10px", fromValue = "0px") |], options = linear, position = TimelinePosition.Expression "half")
    |> ignore

    equal 1500. timeline.Duration
    timeline.Seek 1000. |> ignore
    equal "100px" (styleOf el "width")
    equal "5px" (styleOf el "height")
    timeline.Revert () |> ignore
    equal "" (styleOf el "width")

let private followProperty (clock: Clock) =
    let el = element ()
    let follower: Follower<float> = Follow.property el "x" linear
    follower.To 100.
    equal "translateX(100px)" (styleOf el "transform")
    follower.To 300.
    advance clock 0.
    advance clock 500.
    equal "translateX(202.4px)" (styleOf el "transform")
    follower.Pause ()
    advance clock 300.
    equal "translateX(202.4px)" (styleOf el "transform")
    follower.Revert ()
    equal "" (styleOf el "transform")
    follower.To 50.
    equal "" (styleOf el "transform")

type private Length = class end

let private px n : Ranvier.Css.Value<Length> = Ranvier.Css.Generated.number n "px"

let private followCss (clock: Clock) =
    let el = element ()
    let gap: Ranvier.Css.CustomProperty<Length> = Ranvier.Css.CustomProperty.create "--gap"
    setStyle el "--gap" "7px"
    let follower = Follow.css el gap linear (px 0.)
    equal "7px" (styleOf el "--gap")
    follower.To (px 10.)
    equal "10px" (styleOf el "--gap")
    follower.To (px 110.)
    advance clock 0.
    advance clock 500.
    equal "61.2px" (styleOf el "--gap")
    rejects (fun () -> follower.To (Ranvier.Css.Generated.number 1. "em"))
    follower.Revert ()
    equal "7px" (styleOf el "--gap")

let private nodeSet (_: Clock) =
    let root = element ()
    let child = document.createElement "span"
    root.appendChild child |> ignore
    let outside = element ()
    let nodes = NodeSet ()
    equal 0 nodes.Count
    nodes.Add child
    nodes.Add child
    equal 1 nodes.Count
    equal true (nodes.Has child)
    equal false (nodes.Has outside)
    equal true (nodes.AnyUnder root)
    equal false (nodes.AnyUnder outside)
    nodes.Remove child
    equal 0 nodes.Count
    equal false (nodes.AnyUnder root)

let private flipChildren (_: Clock) =
    let root = element ()

    let children =
        [| for _ in 1..3 ->
               let child: Element = document.createElement "span"
               root.appendChild child |> ignore
               child |]

    let leaving = NodeSet ()
    let entering = NodeSet ()
    leaving.Add children[0]
    entering.Add children[2]
    let live = Flip.children root leaving entering
    equal 1 live.Length
    equal true (obj.ReferenceEquals (live[0], children[1]))
    DomKit.setStyle root "overflow" "hidden"
    equal "hidden" (styleOf root "overflow")
    DomKit.removeStyle root "overflow"
    equal "" (styleOf root "overflow")

let private easing (_: Clock) =
    equal 0.75 ((Ease.named "outQuad") 0.5)
    equal 0.5 ((Ease.named "linear") 0.5)
    equal 0.25 ((Ease.named "inOutBack") 0. + 0.25)
    rejects (fun () -> Ease.named "wobble" |> ignore)
    rejects (fun () -> Ease.named "constructor" |> ignore)
    rejects (fun () -> Ease.steps 0u false |> ignore)
    equal 0.5 ((Ease.steps 4u false) 0.6)
    equal 0.75 ((Ease.steps 4u true) 0.6)
    equal 0. ((Ease.cubicBezier 0.2 0. 0. 1.) 0.)
    equal 1. ((Ease.cubicBezier 0.2 0. 0. 1.) 1.)

// A part's play function, typed on the portable contract.
let private settle (tickable: #ITickable) : unit =
    (tickable :> ITickable).Complete ()

let private bindingsArePortable (_: Clock) =
    let el = element ()
    let binding = Fable.Ranvier.Anime.Anime.animate (el, Fable.Ranvier.Anime.Anim (opacity = 0.5, duration = 100))
    settle binding
    equal true binding.completed
    equal "0.5" (styleOf el "opacity")
    let animation: ComposedAnimation = binding
    equal 100. animation.Duration
    let timeline: ComposedTimeline = Fable.Ranvier.Anime.Anime.timeline ()
    settle timeline
    settle (Fable.Ranvier.Anime.Anime.timer (Fable.Ranvier.Anime.TimerParams (duration = 10)))

    use graph = new Graph ()

    let owner =
        graph.Run (fun () ->
            Api.createRoot (fun owner ->
                Reactive.own binding
                owner))

    graph.Run (fun () -> owner.Dispose ())

    equal "" (styleOf el "opacity")

let private synchronous (check: Clock -> unit) : Clock -> JS.Promise<unit> =
    fun clock ->
        check clock
        resolved ()

/// <summary>Each check by name; a check receives the test clock.</summary>
let cases: (string * (Clock -> JS.Promise<unit>)) array =
    [|
        "portable: Anime.animate with a Scalar from and to reaches the end value", synchronous animateScalar
        "portable: Autoplay false, Seek, Progress and Revert drive an animation", synchronous animateOptions
        "portable: a PropertySpec is one entry of the anime parameter object", synchronous specEntries
        "portable: WhenCompleted runs once on completion, at once when completed, and not after a cancel", synchronous whenCompleted
        "portable: a timeline adds children at positions and labels", synchronous timelineAdds
        "portable: Follow.property applies the first value at once, animates the next and reverts", synchronous followProperty
        "portable: Follow.css leaves the initial unwritten, keeps its unit and restores the declaration", synchronous followCss
        "portable: NodeSet holds nodes by identity", synchronous nodeSet
        "portable: Flip.children leaves out leaving and entering children; DomKit writes inline styles", synchronous flipChildren
        "portable: Ease.named returns anime's curves and raises for an unknown name", synchronous easing
        "portable: binding objects are accepted where a portable contract is expected", synchronous bindingsArePortable
    |]
