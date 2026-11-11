module Ranvier.Anime.Tests.RelativeKeyframeTests

open System
open Expecto
open Ranvier.Anime
open Ranvier.Anime.ValueInterpolation

let private context()=KeyframeContext((),0u,1u,Some 100.)
let tests=testList "relative typed authoring contracts" [
    testCase "constant relative operands retain literal metadata until compilation" <| fun _ ->
        let frame=(Keyframe<unit,float>.ToRelative(RelativeOperation.Add,5.)).WithFromRelative(RelativeOperation.Subtract,2.)
        let template=frame.Template Number
        Expect.equal (template.From,template.To) (Some(Text "-=2"),Some(Text "+=5")) "relative syntax"
        let resolved=frame.Resolve(context(),Number,(fun () -> true)) |> Option.get
        Expect.equal (resolved.From,resolved.To) (template.From,template.To) "same preflight and resolved operands"

    testCase "relative providers retain destination source duration delay order" <| fun _ ->
        let calls=ResizeArray<string>()
        let frame=(Keyframe<unit,float>.ToRelativeBy(RelativeOperation.Multiply,fun context -> calls.Add "to";context.PreviousValue.Value/50.))
                        .WithFromRelativeBy(RelativeOperation.Add,fun _ -> calls.Add "from";5.)
                        .WithDurationBy(fun _ -> calls.Add "duration";100u)
                        .WithDelayBy(fun _ -> calls.Add "delay";-5.)
        let resolved=frame.Resolve(context(),Number,(fun () -> true)) |> Option.get
        Expect.sequenceEqual calls ["to";"from";"duration";"delay"] "source callback phases"
        Expect.equal (resolved.From,resolved.To,resolved.Duration,resolved.Delay)
            (Some(Text "+=5"),Some(Text "*=2"),Some 100.,Some -5.) "relative metadata and signed timing"

    testCase "cancellation in a relative destination suppresses conversion and later providers" <| fun _ ->
        let mutable current=true
        let frame=(Keyframe<unit,float>.ToRelativeBy(RelativeOperation.Add,fun _ -> current<-false;Double.NaN))
                        .WithFromRelativeBy(RelativeOperation.Subtract,fun _ -> failtest "source after cancellation")
                        .WithDurationBy(fun _ -> failtest "duration after cancellation")
        let resolved=frame.Resolve(context(),(fun _ -> failtest "conversion after cancellation"),(fun () -> current))
        Expect.isNone resolved "cancelled construction"

    testCase "endpoint pairs override relative source providers after preserving their effects" <| fun _ ->
        let mutable calls=0
        let frame=(Keyframe<unit,float>.BetweenBy(fun _ -> 10.,20.))
                        .WithFromRelativeBy(RelativeOperation.Add,fun _ -> calls<-calls+1;5.)
        let resolved=frame.Resolve(context(),Number,(fun () -> true)) |> Option.get
        Expect.equal (calls,resolved.From,resolved.To) (1,Some(Number 10.),Some(Number 20.)) "pair source stays absolute"

    testCase "computed complete frames admit constant relative destinations" <| fun _ ->
        let frame=(Keyframe<unit,float>.Compute(fun _ -> Keyframe.ToRelative(RelativeOperation.Add,10.)))
                        .WithFromBy(fun _ -> failtest "replaced outer source")
        let resolved=frame.Resolve(context(),Number,(fun () -> true)) |> Option.get
        Expect.equal resolved.To (Some(Text "+=10")) "computed relative destination"

    testCase "offset source transfer retains relative endpoint metadata" <| fun _ ->
        let source=Keyframe<unit,float>.ToRelative(RelativeOperation.Add,10.)
        let frame=(Keyframe<unit,float>.To 100.).WithFromDestination source
        Expect.equal (frame.Template Number).From (Some(Text "+=10")) "source endpoint metadata survives transfer"

    testCase "typed CSS relative operands preserve existing declaration units" <| fun _ ->
        let frame=Keyframe<unit,Ranvier.Css.Declaration>.ToRelative(RelativeOperation.Add,Ranvier.Css.Css.width(Ranvier.Css.Css.em 2.))
        let template=frame.Template(fun declaration -> Text(Ranvier.Css.Declaration.value declaration))
        Expect.equal template.To (Some(Text "+=2em")) "typed CSS units"

    testCase "undefined operations and missing providers reject at authoring" <| fun _ ->
        Expect.throwsT<ArgumentException> (fun () -> Keyframe<unit,float>.ToRelative(enum<RelativeOperation> 99,5.) |> ignore) "undefined operation"
        Expect.throwsT<ArgumentNullException> (fun () -> Keyframe<unit,float>.ToRelativeBy(RelativeOperation.Add,Unchecked.defaultof<_>) |> ignore) "missing destination provider"
]
