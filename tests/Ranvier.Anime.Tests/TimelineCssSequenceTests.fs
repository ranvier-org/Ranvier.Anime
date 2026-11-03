module Ranvier.Anime.Tests.TimelineCssSequenceTests

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

let tests=testList "owned typed CSS sparse timeline sequences" [
    testCase "one timeline render spans all sequence properties and target-aware providers" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        let mutable renders=0
        use root=new Timeline(scheduler,onRender=(fun _ -> renders<-renders+1))
        let styles=[|Style();Style()|]
        for style in styles do style.Seed("width","10px","");style.Seed("height","20px","important")
        let calls=ResizeArray<uint*uint>()
        let row=(KeyframeRow<CSSStyleDeclaration,Declaration>.Create [||])
                    .WithValueBy("width",fun context -> calls.Add(context.Index,context.Count);width(100.+float context.Index*100.))
                    .WithValue("height",Css.height(Css.px 200.))
        let sequence=KeyframeSequence.Rows [|row|]
        use child=root.CaptureInlineKeyframes(styles |> Array.map(fun style -> style:>CSSStyleDeclaration),sequence,duration=100u,options=options)
        Expect.isTrue (styles |> Array.forall(fun style -> style.Writes.Count=0)) "constructors do not dispatch"
        Expect.sequenceEqual calls [0u,2u;1u,2u] "target identity is independent of property columns"
        root.Seek 50.
        Expect.equal (styles[0].Value "width",styles[1].Value "width",styles[0].Value "height",renders) ("55px","105px","110px",1) "one root render"
        Expect.equal (styles[1].Priority "height") "important" "per-cell original importance"
        root.Cancel()
        Expect.isTrue child.Cancelled "shared parent ownership"

    testCase "sequence and per-property CSS factories arbitrate the same style property" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        use root=new Timeline(scheduler)
        let style=Style()
        style.Seed("width","10px","")
        style.Seed("height","20px","")
        use first=root.AddKeyframes((style:>CSSStyleDeclaration),width 0.,Keyframe.values [|width 0.;width 100.;width 200.|],
                                    duration=200u,position=TimelinePosition.At 0.,options=options)
        let sequence=KeyframeSequence<CSSStyleDeclaration,Declaration>.Rows [|KeyframeRow.declarations [|width 300.;Css.height(Css.px 200.)|]|]
        use second=root.CaptureInlineKeyframes((style:>CSSStyleDeclaration),sequence,duration=100u,position=TimelinePosition.At 75.,options=options)
        root.Seek 125.
        Expect.equal (style.Value "width",style.Value "height") ("187.5px","110px") "sampled predecessor across factories"
        root.Seek(50.,muteCallbacks=true)
        Expect.equal (style.Value "width") "50px" "backward replacement recovery"
        second.Revert()
        Expect.equal (style.Value "width",style.Value "height") ("10px","20px") "preconstruction snapshots"

    testCase "sparse offset easing transfers independently for each property" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        use root=new Timeline(scheduler)
        let style=Style()
        let sequence=KeyframeSequence<CSSStyleDeclaration,Declaration>.Offsets [|
            (KeyframeOffset.declarations 0. [|width 0.;Css.height(Css.px 0.)|]).WithEase(Ease.named "inQuad")
            (KeyframeOffset.declarations 0.5 [|width 100.|]).WithEase(Ease.named "outQuad")
            KeyframeOffset.declarations 1. [|width 200.;Css.height(Css.px 200.)|] |]
        use child=root.CaptureInlineKeyframes((style:>CSSStyleDeclaration),sequence,duration=200u,options=options)
        root.Seek 50.
        Expect.equal (style.Value "width",style.Value "height") ("25px","12.5px") "sparse first segment"
        root.Seek 150.
        Expect.equal (style.Value "width",style.Value "height") ("175px","112.5px") "independent preceding ease"
        child.Revert()
        Expect.isFalse (style.Contains "width" || style.Contains "height") "scoped absence"

    testCase "missing explicit initial declarations reject before pausing or reading styles" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        let mutable pauses=0
        use root=new Timeline(scheduler,onPause=(fun _ -> pauses<-pauses+1))
        root.AddTimer(100u) |> ignore
        root.Resume()
        let style=Style()
        style.BeforeRead<-fun () -> failtest "preflight style access"
        let sequence=KeyframeSequence<CSSStyleDeclaration,Declaration>.Rows [|KeyframeRow.declarations [|width 100.;Css.height(Css.px 200.)|]|]
        Expect.throwsT<ArgumentException> (fun () -> root.AddKeyframes((style:>CSSStyleDeclaration),[|width 0.|],sequence,options=options) |> ignore) "missing height"
        Expect.equal pauses 0 "parent untouched"
        Expect.isTrue root.Playing "parent remains active"

    testCase "target arrays are captured before a snapshot getter mutates caller storage" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        use root=new Timeline(scheduler)
        let first,second,wrong=Style(),Style(),Style()
        first.Seed("width","10px","")
        second.Seed("width","20px","")
        wrong.Seed("width","999px","")
        let targets=[|first:>CSSStyleDeclaration;second:>CSSStyleDeclaration|]
        first.BeforeRead<-fun () -> targets[1]<-wrong
        let sequence=KeyframeSequence<CSSStyleDeclaration,Declaration>.Rows [|KeyframeRow.declarations [|width 100.|]|]
        use child=root.CaptureInlineKeyframes(targets,sequence,duration=100u,options=options)
        root.Seek 50.
        Expect.equal (first.Value "width",second.Value "width",wrong.Value "width") ("55px","60px","999px") "private target snapshot"

    testCase "snapshot seek aborts later cells and preserves the live nested parent state" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        use root=new Timeline(scheduler)
        root.AddTimer(100u) |> ignore
        let first,second=Style(),Style()
        let mutable armed=true
        first.BeforeRead<-fun () -> if armed then armed<-false;root.Seek 25.
        second.BeforeRead<-fun () -> failtest "later property capture"
        let sequence=KeyframeSequence<CSSStyleDeclaration,Declaration>.Rows [|KeyframeRow.declarations [|width 100.;Css.height(Css.px 200.)|]|]
        Expect.throwsT<InvalidOperationException> (fun () ->
            root.CaptureInlineKeyframes([|first:>CSSStyleDeclaration;second:>CSSStyleDeclaration|],sequence,duration=100u,options=options) |> ignore) "superseded construction"
        Expect.equal (root.CurrentTime,root.Cancelled,root.IterationDuration) (25.,false,100.) "nested seek survives"
        Expect.isEmpty first.Writes "pending child never dispatches"

    testCase "empty CSS sequence has no style reads and uses one epsilon child" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        use root=new Timeline(scheduler)
        let style=Style()
        style.BeforeRead<-fun () -> failtest "empty style read"
        use child=root.CaptureInlineKeyframes((style:>CSSStyleDeclaration),KeyframeSequence<CSSStyleDeclaration,Declaration>.Rows [||],duration=100u,options=options)
        Expect.equal child.Playback.Duration 1e-11 "empty source duration"
        root.Complete()
        Expect.isTrue root.Completed "owned no-op completes"

    testCase "fractional sequence duration is shared by every declaration" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        use root=new Timeline(scheduler)
        let style=Style()
        let sequence=KeyframeSequence<CSSStyleDeclaration,Declaration>.Rows [|KeyframeRow.declarations [|width 100.;Css.height(Css.px 200.)|]|]
        use child=root.AddKeyframesPrecise((style:>CSSStyleDeclaration),[|width 0.;Css.height(Css.px 0.)|],sequence,0.5,options=options)
        root.Seek 0.125
        Expect.equal (style.Value "width",style.Value "height") ("25px","50px") "shared precise duration"

    testCase "a single zero offset creates no tweens and never reads an inline style" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        use root=new Timeline(scheduler)
        let style=Style()
        style.BeforeRead<-fun () -> failtest "zero-key style access"
        let sequence=KeyframeSequence<CSSStyleDeclaration,Declaration>.Offsets [|KeyframeOffset.declarations 0. [|width 400.|]|]
        use child=root.CaptureInlineKeyframes((style:>CSSStyleDeclaration),sequence,duration=100u,options=options)
        Expect.equal child.Playback.Duration 1e-11 "source no-op with a named empty column"
        root.Complete()
        Expect.isEmpty style.Writes "unused column dispatch"

    testCase "actual owner disposal in the first sampled modifier suppresses all property writes" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        use graph=new Graph()
        use activation=graph.Activate()
        let owner=Api.createRoot id
        use root=new Timeline(scheduler)
        owner.Attach root
        let style=Style()
        let mutable armed=false
        let mutable calls=0
        let modifier value=
            if armed then calls<-calls+1;owner.Dispose()
            value
        let sequence=KeyframeSequence<CSSStyleDeclaration,Declaration>.Rows [|KeyframeRow.declarations [|width 100.;Css.height(Css.px 200.)|]|]
        use child=root.AddKeyframes((style:>CSSStyleDeclaration),[|width 0.;Css.height(Css.px 0.)|],sequence,duration=100u,
            options={options with Modifier=modifier})
        armed<-true
        root.Seek 50.
        Expect.equal calls 1 "later cell modifier suppressed"
        Expect.isEmpty style.Writes "guard spans modifiers and dispatch"
        Expect.isTrue (root.Cancelled && child.Cancelled) "actual ownership"

    testCase "cancelled destination providers suppress mismatched return conversion and later properties" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        use root=new Timeline(scheduler)
        let style=Style()
        let row=(KeyframeRow<CSSStyleDeclaration,Declaration>.Create [||])
                    .WithValueBy("width",fun _ -> root.Cancel();Css.height(Css.px 100.))
                    .WithValueBy("height",fun _ -> failtest "later destination")
                    .WithDurationBy(fun _ -> failtest "timing after cancellation")
        Expect.throwsT<InvalidOperationException> (fun () ->
            root.CaptureInlineKeyframes((style:>CSSStyleDeclaration),KeyframeSequence.Rows [|row|],duration=100u,options=options) |> ignore) "pending child closed"
        Expect.isTrue root.Cancelled "parent cancelled"
        Expect.isEmpty style.Writes "no invalid return dispatch"

    testCase "revert restores later CSS properties after retaining the first setter failure" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        use root=new Timeline(scheduler)
        let style=Style()
        style.Seed("width","10px","important")
        style.Seed("height","20px","")
        let sequence=KeyframeSequence<CSSStyleDeclaration,Declaration>.Offsets [|
            KeyframeOffset.declarations 0. [|width 0.;Css.height(Css.px 0.)|]
            KeyframeOffset.declarations 1. [|width 100.;Css.height(Css.px 200.)|] |]
        use child=root.CaptureInlineKeyframes((style:>CSSStyleDeclaration),sequence,duration=100u,options=options)
        root.Seek 50.
        let fault=InvalidOperationException("restore owned CSS width")
        let mutable armed=true
        style.AfterWrite<-fun () -> if armed && style.Value "width"="10px" then armed<-false;raise fault
        let mutable actual:exn option=None
        try child.Revert() with error -> actual<-Some error
        Expect.isTrue (actual |> Option.exists(fun error -> obj.ReferenceEquals(error,fault))) "first fault identity"
        Expect.equal (style.Value "height",style.Priority "width") ("20px","important") "remaining snapshot restored"
        Expect.isTrue child.Cancelled "cleanup completed"

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
                root.CaptureCssKeyframes(computed,[|element;element|],
                                         KeyframeSequence<HTMLElement,Declaration>.Rows [|KeyframeRow.declarations [|width 100.;Css.height(Css.px 200.)|]|],
                                         duration=100u,options=options) |> ignore) "superseded factory"
            Expect.equal computedReads 1 "no later computed targets"
            Expect.equal styleReads 2 "captured inline styles before first computed read"
            Expect.isEmpty style.Writes "no interrupted dispatch"
            Expect.equal root.Cancelled disposeOwner "actual parent lifetime"
            if not disposeOwner then Expect.equal root.CurrentTime 25. "nested seek survives"
            owner.Dispose()
]
