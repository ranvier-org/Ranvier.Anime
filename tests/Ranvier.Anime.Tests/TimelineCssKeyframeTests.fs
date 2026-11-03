module Ranvier.Anime.Tests.TimelineCssKeyframeTests

open System
open System.Collections.Generic
open Expecto
open Ranvier
open Ranvier.Dom
open Ranvier.Css
open Ranvier.Anime

type private Style() =
    let values=Dictionary<string,string*string>(StringComparer.Ordinal)
    let writes=ResizeArray<string*string*string>()
    member val BeforeRead:unit -> unit=ignore with get,set
    member val AfterWrite:unit -> unit=ignore with get,set
    member _.Seed(name,value,priority)=values[name]<-value,priority
    member _.Value name=match values.TryGetValue name with true,(value,_) -> value | _ -> ""
    member _.Priority name=match values.TryGetValue name with true,(_,value) -> value | _ -> ""
    member _.Contains name=values.ContainsKey name
    member _.Writes=writes
    interface CSSStyleDeclaration with
        member this.getPropertyValue name=this.BeforeRead();this.Value name
        member this.getPropertyPriority name=this.BeforeRead();this.Priority name
        member this.item index=this.BeforeRead();values.Keys |> Seq.tryItem(int index) |> Option.defaultValue ""
        member this.length=this.BeforeRead();uint values.Count
        member this.setProperty(name,value,priority)=
            let priority=defaultArg priority ""
            values[name]<-value,priority
            writes.Add(name,value,priority)
            this.AfterWrite()
        member this.removeProperty name=
            this.BeforeRead()
            let value=this.Value name
            values.Remove name |> ignore
            value
        member _.cssText with get()=failtest "whole-style access" and set _=failtest "whole-style replacement"
        member _.parentRule=Unchecked.defaultof<CSSRule>
        member _.cssRules=Unchecked.defaultof<CSSRuleList>

let private options={KeyframeAnimationOptions.Default with Ease=id;Precision=None}
let private width value=Css.width(Css.px value)

let tests=testList "owned typed CSS timeline keyframes" [
    testCase "declaration and inline factories share captured style replacement identity" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        let mutable renders=0
        use root=new Timeline(scheduler,onRender=(fun _ -> renders<-renders+1))
        let style=Style()
        style.Seed("width","10px","important")
        use first=root.AddKeyframes((style:>CSSStyleDeclaration),width 0.,Keyframe.values [|width 0.;width 100.;width 200.|],
                                    duration=200u,position=TimelinePosition.At 0.,options=options)
        use second=root.CaptureInlineKeyframes((style:>CSSStyleDeclaration),"width",[|Keyframe.toValue(width 300.)|],
                                               duration=100u,position=TimelinePosition.At 75.,options=options)
        Expect.isEmpty style.Writes "cache-only constructors"
        root.Seek 125.
        Expect.equal (style.Value "width",renders) ("187.5px",1) "shared sampled predecessor"
        Expect.equal (style.Priority "width") "important" "priority retained during composition"
        root.Seek(50.,muteCallbacks=true)
        Expect.equal (style.Value "width",renders) ("50px",1) "muted backward recovery"
        style.Seed("color","red","")
        second.Revert()
        Expect.equal (style.Value "width",style.Priority "width",style.Value "color") ("10px","important","red") "scoped snapshot"

    testCase "typed inputs retain actual target index count and prior sampled declaration" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        use root=new Timeline(scheduler)
        let styles=[|Style();Style()|]
        styles[0].Seed("width","10px","")
        styles[1].Seed("width","20px","")
        let targets=styles |> Array.map(fun value -> value:>CSSStyleDeclaration)
        let calls=ResizeArray<uint*uint*bool>()
        let frame=Keyframe.toBy(fun (context:KeyframeContext<CSSStyleDeclaration,Declaration>) ->
            calls.Add(context.Index,context.Count,obj.ReferenceEquals(context.Target,targets[int context.Index]))
            width(100.+float context.Index*100.))
        use child=root.CaptureInlineKeyframes(targets,"width",[|frame|],duration=100u,options=options)
        root.Seek 50.
        Expect.sequenceEqual calls [(0u,2u,true);(1u,2u,true)] "captured authoring contexts"
        Expect.equal (styles[0].Value "width",styles[1].Value "width") ("55px","110px") "target-specific originals"

    testCase "complete constant preflight precedes parent pause and style capture" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        let mutable pauses=0
        use root=new Timeline(scheduler,onPause=(fun _ -> pauses<-pauses+1))
        root.AddTimer(100u) |> ignore
        root.Resume()
        let style=Style()
        style.BeforeRead<-fun () -> failtest "read before validation"
        Expect.throwsT<ArgumentException> (fun () ->
            root.AddKeyframes((style:>CSSStyleDeclaration),width 0.,[|Keyframe.toValue(width 100.);Keyframe.toValue(Css.height(Css.px 100.))|],
                               duration=100u,options=options) |> ignore) "mismatched later property"
        Expect.equal (pauses,root.Playing) (0,true) "parent unaffected"

    testCase "a snapshot getter seek stops later capture while preserving the nested parent" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        use root=new Timeline(scheduler)
        root.AddTimer(100u) |> ignore
        let first=Style()
        let second=Style()
        let mutable once=true
        first.BeforeRead<-fun () -> if once then once<-false;root.Seek(25.,muteCallbacks=true)
        second.BeforeRead<-fun () -> failtest "later capture after parent seek"
        Expect.throwsT<InvalidOperationException> (fun () ->
            root.CaptureInlineKeyframes([|first:>CSSStyleDeclaration;second:>CSSStyleDeclaration|],"width",
                                        [|Keyframe.toValue(width 100.)|],duration=100u,options=options) |> ignore) "superseded capture"
        Expect.equal (root.CurrentTime,root.Cancelled,root.IterationDuration) (25.,false,100.) "nested state preserved"

    testCase "actual ownership interrupts snapshot reads and closes pending construction" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        use graph=new Graph()
        use activation=graph.Activate()
        let owner=Api.createRoot id
        use root=new Timeline(scheduler)
        owner.Attach root
        let first=Style()
        let second=Style()
        first.BeforeRead<-owner.Dispose
        second.BeforeRead<-fun () -> failtest "later owner-disposed capture"
        Expect.throwsT<InvalidOperationException> (fun () ->
            root.CaptureInlineKeyframes([|first:>CSSStyleDeclaration;second:>CSSStyleDeclaration|],"width",
                                        [|Keyframe.toValue(width 100.)|],duration=100u,options=options) |> ignore) "closed factory"
        Expect.isTrue root.Cancelled "actual root owner"
        Expect.isEmpty first.Writes "no partial construction dispatch"

    testCase "ordinary capture faults retain identity and cancel owned lifetimes" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        use root=new Timeline(scheduler)
        let prior=root.AddTimer(100u)
        let style=Style()
        let fault=InvalidOperationException("owned CSS snapshot")
        style.BeforeRead<-fun () -> raise fault
        let mutable actual:exn option=None
        try root.CaptureInlineKeyframes((style:>CSSStyleDeclaration),"width",[|Keyframe.toValue(width 100.)|],duration=100u,options=options) |> ignore
        with error -> actual<-Some error
        Expect.isTrue (actual |> Option.exists(fun error -> obj.ReferenceEquals(error,fault))) "first fault identity"
        Expect.isTrue (root.Cancelled && prior.Cancelled) "root cleanup"

    testCase "empty CSS frames read no style and complete as an epsilon child" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        use root=new Timeline(scheduler)
        let style=Style()
        style.BeforeRead<-fun () -> failtest "empty CSS read"
        use child=root.CaptureInlineKeyframes((style:>CSSStyleDeclaration),"width",[||],duration=100u,options=options)
        Expect.equal child.Playback.Duration 1e-11 "source no-op"
        root.Complete()
        Expect.isTrue root.Completed "owned no-op completes"

    testCase "a typed provider can cancel without evaluating later source timing or modifiers" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        use root=new Timeline(scheduler)
        let style=Style()
        style.Seed("width","10px","")
        let frame=(Keyframe.toBy(fun (_:KeyframeContext<CSSStyleDeclaration,Declaration>) -> root.Cancel();Css.height(Css.px 100.)))
                    .WithFromBy(fun _ -> failtest "from after cancellation")
                    .WithDurationBy(fun _ -> failtest "duration after cancellation")
                    .WithModifier(fun _ -> failtest "modifier after cancellation")
        Expect.throwsT<InvalidOperationException> (fun () ->
            root.CaptureInlineKeyframes((style:>CSSStyleDeclaration),"width",[|frame|],duration=100u,options=options) |> ignore) "cancelled factory"
        Expect.isEmpty style.Writes "no dispatch or invalid return conversion"

    testCase "precise CSS durations preserve fractional timing and scoped absence restoration" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        use root=new Timeline(scheduler)
        let style=Style()
        use child=root.AddKeyframesPrecise((style:>CSSStyleDeclaration),width 0.,[|Keyframe.toValue(width 100.)|],0.5,options=options)
        root.Seek 0.25
        Expect.equal (style.Value "width") "50px" "precise midpoint"
        child.Revert()
        Expect.isFalse (style.Contains "width") "initial absence restored"

    testCase "reentrant setter seek supersedes outer callbacks through the root" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        let mutable renders=0
        use root=new Timeline(scheduler,onRender=(fun _ -> renders<-renders+1))
        let style=Style()
        let mutable armed=false
        style.AfterWrite<-fun () -> if armed then armed<-false;root.Seek(75.,muteCallbacks=true)
        use child=root.AddKeyframes((style:>CSSStyleDeclaration),width 0.,[|Keyframe.toValue(width 100.)|],duration=100u,options=options)
        armed<-true
        root.Seek 50.
        Expect.equal (style.Value "width",root.CurrentTime,renders) ("75px",75.,0) "inner muted sample supersedes outer"

    for disposeOwner in [false;true] do
        testCase (sprintf "computed service returning after %s never reads its result or later targets" (if disposeOwner then "owner disposal" else "parent seek")) <| fun _ ->
            use scheduler=new ManualFrameScheduler()
            use graph=new Graph()
            use activation=graph.Activate()
            let owner=Api.createRoot id
            use root=new Timeline(scheduler)
            owner.Attach root
            root.AddTimer(100u) |> ignore
            let mutable computedReads=0
            let mutable styleReads=0
            let style=Style()
            style.Seed("width","10px","")
            let inlineStyle=Reflection.DispatchProxy.Create<CSSStyleProperties,UnitConversionTests.UnitProxy>()
            (box inlineStyle:?>UnitConversionTests.UnitProxy).Handler<-fun methodInfo args ->
                methodInfo.Invoke((style:>CSSStyleDeclaration),args)
            let element=Reflection.DispatchProxy.Create<HTMLElement,UnitConversionTests.UnitProxy>()
            (box element:?>UnitConversionTests.UnitProxy).Handler<-fun methodInfo _ ->
                if methodInfo.Name="get_style" then styleReads<-styleReads+1;box inlineStyle
                else failtest ("Unexpected element access "+methodInfo.Name)
            let poisoned=Style()
            poisoned.BeforeRead<-fun () -> failtest "superseded computed result was read"
            let computed=
                { new ComputedStyleService with
                    member _.Get(_,_) =
                        computedReads<-computedReads+1
                        if disposeOwner then owner.Dispose() else root.Seek 25.
                        poisoned:>CSSStyleDeclaration }
            Expect.throwsT<InvalidOperationException> (fun () ->
                root.CaptureCssKeyframes(computed,[|element;element|],"width",[|Keyframe.toValue(width 100.)|],
                                         duration=100u,options=options) |> ignore) "superseded factory"
            Expect.equal computedReads 1 "no later computed targets"
            Expect.equal styleReads 2 "captured inline styles before first computed read"
            Expect.isEmpty style.Writes "no interrupted dispatch"
            Expect.equal root.Cancelled disposeOwner "actual parent lifetime"
            if not disposeOwner then Expect.equal root.CurrentTime 25. "nested seek survives"
            owner.Dispose()
]
