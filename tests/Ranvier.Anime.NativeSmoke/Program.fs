module Ranvier.Anime.NativeSmoke.Program

open System
open System.Diagnostics
open System.Globalization
open Ranvier
open Ranvier.Anime
open Ranvier.Css
open Ranvier.Css.Dom
open Ranvier.Dom
open Ranvier.View.WebCore

let private require name condition =
    if not condition then failwith name
    Console.WriteLine("PASS " + name)

let private pumpUntil (host: WebCoreHost) condition =
    let watch = Stopwatch.StartNew()
    while not(condition()) && watch.ElapsedMilliseconds < 3000L do
        host.Cycle(milliseconds = 1)
        host.Frame()
    require "native frame loop reached its target" (condition())

[<EntryPoint>]
let main args =
    try
        if Array.contains "--require-aot" args then
            require "consumer is NativeAOT" (not System.Runtime.CompilerServices.RuntimeFeature.IsDynamicCodeSupported)
#if EXPECT_TRACED
        let expectedTrace = true
#else
        let expectedTrace = false
#endif
        for assembly in [typeof<Graph>.Assembly; typeof<Document>.Assembly; typeof<WebCoreHost>.Assembly] do
            let traced =
                System.Reflection.CustomAttributeExtensions.GetCustomAttributes<System.Reflection.AssemblyMetadataAttribute>(assembly)
                |> Seq.exists(fun attribute -> attribute.Key = "RanvierTrace" && attribute.Value = "true")
            require (assembly.GetName().Name + " trace cohort") (traced = expectedTrace)
        use host = WebCoreHost.Create(320, 200)
        use scheduler = Frames.forHost host
        EngineCoreNativeProbe.run host scheduler
        CompositionNativeIdentityProbe.run host.Document
        SvgNativeProbe.run host.Document (ComputedStyles.forHost host)
        SvgNativeProbe.scopeOwnersAndReentry host.Document (ComputedStyles.forHost host)
        let element = host.Document.createElement "div" :?> HTMLElement
        host.Document.body.appendChild element |> ignore
        let style = (element :> ElementCSSInlineStyle).style
        do
            use graph=new Graph()
            use activation=graph.Activate()
            ScopeNativeProbe.run host.Document (MediaQueries.provider host)
            ScopeDefaultsNativeProbe.run host.Document
            ScopeAnimationDefaultsNativeProbe.run host.Document
            CssScopeNativeProbe.run host.Document
            CssDirectRevertNativeProbe.run host.Document
            require "native scoped legacy CSS snapshots and priorities" true
            require "native scope selectors media refresh and actual ownership" true
        do
            use graph=new Graph()
            use activation=graph.Activate()
            let owner=Api.createRoot id
            use scope=Api.runWithOwner owner (fun () -> new Scope())
            let target=host.Document.createElement "div" :?> HTMLElement
            host.Document.body.appendChild target |> ignore
            let inlineStyle=(target:>ElementCSSInlineStyle).style
            inlineStyle.setProperty("width","10px","important")
            inlineStyle.setProperty("height","20px","important")
            let originals=ResizeArray<float*float>()
            let timelines=ResizeArray<Timeline>()
            let sequences=ResizeArray<Ranvier.Anime.KeyframeAnimation>()
            scope.Add(fun _ ->
                originals.Add(target.getBoundingClientRect().width,target.getBoundingClientRect().height)
                let timeline=new Timeline(scheduler) |> Timeline.own
                timelines.Add timeline
                let width=NumericProperty((fun () -> target.getBoundingClientRect().width),
                    (fun value -> StyleDeclaration.apply inlineStyle (Declaration.important(Css.width(Css.px value)))))
                timeline.Animate(width,100.,100u,from=0.) |> ignore
                let sequence=Keyframes.CaptureCss(scheduler,ComputedStyles.forHost host,target,"height",
                    [|Keyframe.toValue(Css.height(Css.px 220.))|],duration=100u,
                    options={Ranvier.Anime.KeyframeAnimationOptions.Default with Ease=id;Autoplay=false;Precision=None})
                sequences.Add sequence
                timeline.Seek 50.
                sequence.Playback.Seek 50.
                None) |> ignore
            require "automatic scope members render through actual native CSS and layout"
                (target.getBoundingClientRect().width=50. && target.getBoundingClientRect().height=120.)
            scope.Refresh() |> ignore
            require "native scope refresh restores before capturing replacement members"
                ((originals |> Seq.toList)=[(10.,20.);(10.,20.)] && timelines[0].Cancelled && sequences[0].Cancelled)
            owner.Dispose()
            require "actual scope owner restores typed CSS snapshots and scalar originals"
                (target.getBoundingClientRect().width=10. && target.getBoundingClientRect().height=20.
                 && inlineStyle.getPropertyPriority "width"="important" && inlineStyle.getPropertyPriority "height"="important")
            host.Cycle(milliseconds=100)
            host.Frame()
            require "disposed scope leaves no native member scheduling or cleanup faults"
                (scope.IsDisposed && (timelines |> Seq.forall _.Cancelled) && (sequences |> Seq.forall _.Cancelled)
                 && owner.ErrorCount=0 && scope.Errors.Count=0
                 && target.getBoundingClientRect().width=10. && target.getBoundingClientRect().height=20.)
            host.Document.body.removeChild target |> ignore
        do
            use graph=new Graph()
            use activation=graph.Activate()
            let owner=Api.createRoot id
            let target=host.Document.createElement "div" :?> HTMLElement
            host.Document.body.appendChild target |> ignore
            let inlineStyle=(target:>ElementCSSInlineStyle).style
            inlineStyle.setProperty("width","10px")
            let property=NumericProperty((fun () -> target.getBoundingClientRect().width),
                (fun value -> StyleDeclaration.apply inlineStyle (Css.width(Css.px value))))
            let mutable renders=0
            use timeline=new Timeline(scheduler,onRender=(fun _ -> renders<-renders+1))
            owner.Attach timeline
            timeline.Animate(property,100.,100u,from=0.,position=TimelinePosition.At 0.) |> ignore
            use child=timeline.AddKeyframes(property,[|Keyframe.toValue 200.;Keyframe.toValue 300.|],duration=200u,
                position=TimelinePosition.At 50.,options={Ranvier.Anime.KeyframeAnimationOptions.Default with Ease=id;Precision=None})
            require "mixed scalar-first construction leaves native geometry untouched" (target.getBoundingClientRect().width=10. && renders=0)
            timeline.Seek 100.
            require "owned keyframes inherit the sampled native scalar predecessor and notify one root render"
                (target.getBoundingClientRect().width=125. && renders=1)
            timeline.Seek(25.,muteCallbacks=true)
            require "mixed scalar-first native playback recovers backward with inherited muting"
                (target.getBoundingClientRect().width=25. && renders=1)
            child.Revert()
            require "mixed keyframe revert preserves its native preconstruction snapshot" (target.getBoundingClientRect().width=10.)
            owner.Dispose()
            host.Cycle(milliseconds=100)
            host.Frame()
            require "actual owner closes mixed native scalar and keyframe scheduling"
                (timeline.Cancelled && child.Cancelled && target.getBoundingClientRect().width=10.)
            host.Document.body.removeChild target |> ignore
        do
            let target=host.Document.createElement "div" :?> HTMLElement
            host.Document.body.appendChild target |> ignore
            let inlineStyle=(target:>ElementCSSInlineStyle).style
            inlineStyle.setProperty("width","10px")
            let property=NumericProperty((fun () -> target.getBoundingClientRect().width),
                (fun value -> StyleDeclaration.apply inlineStyle (Css.width(Css.px value))))
            use timeline=new Timeline(scheduler)
            use child=timeline.AddKeyframes(property,Keyframe.values [|0.;100.;200.|],duration=200u,
                position=TimelinePosition.At 0.,options={Ranvier.Anime.KeyframeAnimationOptions.Default with Ease=id;Precision=None})
            timeline.Animate(property,400.,100u,position=TimelinePosition.At 75.) |> ignore
            require "mixed keyframe-first scalar construction remains cache-only" (target.getBoundingClientRect().width=10.)
            timeline.Seek 125.
            require "scalar facade inherits sampled native keyframe predecessor through shared siblings"
                (target.getBoundingClientRect().width=237.5)
            timeline.Set(property,500.,position=TimelinePosition.At 125.) |> ignore
            require "mixed native set construction preserves the current target" (target.getBoundingClientRect().width=237.5)
            timeline.Seek 125.
            require "mixed native set arbitrates scalar and keyframe predecessors" (target.getBoundingClientRect().width=500.)
            timeline.Seek(25.,muteCallbacks=true)
            require "mixed native set and scalar takeover retain earlier keyframe recovery" (target.getBoundingClientRect().width=25.)
            timeline.Cancel()
            host.Document.body.removeChild target |> ignore
        do
            use graph=new Graph()
            use activation=graph.Activate()
            let owner=Api.createRoot id
            let target=host.Document.createElement "div" :?> HTMLElement
            host.Document.body.appendChild target |> ignore
            let inlineStyle=(target:>ElementCSSInlineStyle).style
            inlineStyle.setProperty("width","10px")
            inlineStyle.setProperty("height","20px")
            let mutable writes=0
            let width=NumericProperty((fun () -> target.getBoundingClientRect().width),
                (fun value -> writes<-writes+1;StyleDeclaration.apply inlineStyle (Css.width(Css.px value))))
            let height=NumericProperty((fun () -> target.getBoundingClientRect().height),
                (fun value -> writes<-writes+1;StyleDeclaration.apply inlineStyle (Css.height(Css.px value))))
            let mutable armed=false
            use timeline=new Timeline(scheduler)
            owner.Attach timeline
            timeline.Animate(width,100.,100u,from=0.,position=TimelinePosition.At 0.,
                modifier=(fun value -> if armed then owner.Dispose();Double.NaN else value)) |> ignore
            use child=timeline.AddKeyframes(height,Keyframe.values [|0.;200.|],duration=100u,
                position=TimelinePosition.At 0.,options={Ranvier.Anime.KeyframeAnimationOptions.Default with Ease=id;Precision=None})
            armed<-true
            timeline.Resume()
            pumpUntil host (fun () -> timeline.Cancelled)
            require "native scalar modifier owner disposal suppresses invalid output and later keyframe dispatch"
                (writes=0 && child.Cancelled && target.getBoundingClientRect().width=10. && target.getBoundingClientRect().height=20.)
            host.Document.body.removeChild target |> ignore
        do
            let mutable value= -1e308
            let property=NumericProperty((fun () -> value),(fun next -> value<-next))
            use timeline=new Timeline(scheduler)
            timeline.Animate(property,1e308,100u) |> ignore
            timeline.Seek 50.
            require "native scalar facade retains finite interpolation when endpoint subtraction overflows" (value=0.)
        do
            let target=host.Document.createElement "div" :?> HTMLElement
            host.Document.body.appendChild target |> ignore
            let inlineStyle=(target:>ElementCSSInlineStyle).style
            inlineStyle.setProperty("width","100px")
            let property=NumericProperty((fun () -> target.getBoundingClientRect().width),
                (fun value -> StyleDeclaration.apply inlineStyle (Css.width(Css.px value))))
            use timeline=new Timeline(scheduler)
            use child=timeline.AddKeyframes(property,[|Keyframe.toRelative RelativeOperation.Add 50.;Keyframe.toRelative RelativeOperation.Multiply 2.|],
                duration=200u,options={Ranvier.Anime.KeyframeAnimationOptions.Default with Ease=id;Precision=None})
            require "typed relative numeric construction retains original native geometry" (target.getBoundingClientRect().width=100.)
            timeline.Seek 150.
            require "typed relative numeric keyframes chain raw destinations in native layout" (target.getBoundingClientRect().width=225.)
            child.Revert()
            require "typed relative numeric revert restores the preconstruction native value" (target.getBoundingClientRect().width=100.)
            host.Document.body.removeChild target |> ignore
        do
            let target=host.Document.createElement "div" :?> HTMLElement
            host.Document.body.appendChild target |> ignore
            let inlineStyle=(target:>ElementCSSInlineStyle).style
            inlineStyle.setProperty("width","10px")
            let property=NumericProperty((fun () -> target.getBoundingClientRect().width),
                (fun value -> StyleDeclaration.apply inlineStyle (Css.width(Css.px value))))
            use timeline=new Timeline(scheduler)
            let options={Ranvier.Anime.KeyframeAnimationOptions.Default with Ease=id;Precision=None}
            use first=timeline.AddKeyframes(property,Keyframe.values [|0.;100.;200.|],duration=200u,position=TimelinePosition.At 0.,options=options)
            let frame=(Keyframe<NumericProperty,float>.To 300.).WithFromRelative(RelativeOperation.Add,5.)
            use second=timeline.AddKeyframes(property,[|frame|],duration=100u,position=TimelinePosition.At 75.,options=options)
            timeline.Seek 125.
            require "typed relative owned source uses raw predecessor100 plus5 rather than sampled75"
                (target.getBoundingClientRect().width=202.5)
            timeline.Cancel()
            require "relative sibling controllers share native root cancellation" (first.Cancelled && second.Cancelled)
            host.Document.body.removeChild target |> ignore
        do
            let target=host.Document.createElement "div" :?> HTMLElement
            host.Document.body.appendChild target |> ignore
            let inlineStyle=(target:>ElementCSSInlineStyle).style
            inlineStyle.setProperty("font-size","16px")
            inlineStyle.setProperty("width","16px",priority="important")
            use timeline=new Timeline(scheduler)
            let frame=Keyframe<HTMLElement,Declaration>.ToRelative(RelativeOperation.Add,Css.width(Css.em 2.))
            use child=timeline.CaptureCssKeyframes(ComputedStyles.forHost host,target,"width",[|frame|],duration=100u,
                options={Ranvier.Anime.KeyframeAnimationOptions.Default with Ease=id})
            require "typed relative CSS construction retains its original native declaration" (inlineStyle.getPropertyValue "width"="16px")
            timeline.Seek 50.
            require "typed relative CSS operands retain source arithmetic before native unit conversion"
                (target.getBoundingClientRect().width=152. && inlineStyle.getPropertyValue "width"="9.5em" &&
                 inlineStyle.getPropertyPriority "width"="important")
            child.Revert()
            require "typed relative CSS revert restores original units and priority"
                (inlineStyle.getPropertyValue "width"="16px" && inlineStyle.getPropertyPriority "width"="important")
            host.Document.body.removeChild target |> ignore
        do
            use graph=new Graph()
            use activation=graph.Activate()
            let owner=Api.createRoot id
            let target=host.Document.createElement "div" :?> HTMLElement
            host.Document.body.appendChild target |> ignore
            let inlineStyle=(target:>ElementCSSInlineStyle).style
            inlineStyle.setProperty("width","10px")
            use timeline=new Timeline(scheduler)
            owner.Attach timeline
            let frame=Keyframe<HTMLElement,Declaration>.ToRelativeBy(RelativeOperation.Add,fun _ ->
                owner.Dispose()
                Css.height(Css.px 100.))
            let mutable interrupted=false
            try timeline.CaptureCssKeyframes(ComputedStyles.forHost host,target,"width",[|frame|],duration=100u) |> ignore
            with :? InvalidOperationException -> interrupted<-true
            require "actual native owner interruption suppresses relative operand conversion and invalid returned property"
                (interrupted && timeline.Cancelled && inlineStyle.getPropertyValue "width"="10px" && inlineStyle.getPropertyValue "height"="")
            host.Document.body.removeChild target |> ignore
        do
            use graph=new Graph()
            use activation=graph.Activate()
            let owner=Api.createRoot id
            let targets=[|host.Document.createElement "div" :?> HTMLElement;host.Document.createElement "div" :?> HTMLElement|]
            let styles=targets |> Array.map(fun target -> (target:>ElementCSSInlineStyle).style)
            for index in 0..1 do
                host.Document.body.appendChild targets[index] |> ignore
                styles[index].setProperty("font-size",if index=0 then "16px" else "32px")
                styles[index].setProperty("width",if index=0 then "10px" else "20px")
                styles[index].setProperty("height",(if index=0 then "20px" else "40px"),priority=(if index=0 then "" else "important"))
            let originalChildren=host.Document.body.childNodes.length
            let service=ComputedStyles.forHost host
            let mutable reads=0
            let computed={new ComputedStyleService with member _.Get(target,pseudo)=reads<-reads+1;service.Get(target,pseudo)}
            let inputs=ResizeArray<uint*uint>()
            let row=(KeyframeRow<HTMLElement,Declaration>.Create [||])
                        .WithValueBy("width",fun context -> inputs.Add(context.Index,context.Count);Css.width(Css.em(2.+float context.Index)))
                        .WithValue("height",Css.height(Css.em 3.))
            let sequence=KeyframeSequence.Rows [|row|]
            let mutable renders=0
            use timeline=new Timeline(scheduler,onRender=(fun _ -> renders<-renders+1))
            owner.Attach timeline
            use child=timeline.CaptureCssKeyframes(computed,targets,sequence,duration=100u,
                options={Ranvier.Anime.KeyframeAnimationOptions.Default with Ease=id})
            require "owned CSS rows capture all native contexts without dispatch or leaked unit probes"
                (reads=4 && inputs.ToArray()=[|0u,2u;1u,2u|] && styles[0].getPropertyValue "width"="10px" &&
                 styles[1].getPropertyValue "height"="40px" && host.Document.body.childNodes.length=originalChildren && renders=0)
            timeline.Seek 50.
            require "owned CSS rows share one root render across native properties and independent font contexts"
                (targets[0].getBoundingClientRect().width=21. && targets[1].getBoundingClientRect().width=58. &&
                 targets[0].getBoundingClientRect().height=34. && targets[1].getBoundingClientRect().height=68. &&
                 styles[1].getPropertyPriority "height"="important" && renders=1)
            timeline.Resume()
            pumpUntil host (fun () -> timeline.Completed)
            require "actual host frames complete owned CSS sparse rows without computed recapture"
                (reads=4 && targets[0].getBoundingClientRect().width=32. && targets[1].getBoundingClientRect().width=96. &&
                 targets[0].getBoundingClientRect().height=48. && targets[1].getBoundingClientRect().height=96.)
            styles[0].setProperty("color","red")
            child.Revert()
            require "owned CSS sequence revert restores every native snapshot and preserves unrelated edits"
                (styles[0].getPropertyValue "width"="10px" && styles[1].getPropertyValue "width"="20px" &&
                 styles[0].getPropertyValue "height"="20px" && styles[1].getPropertyValue "height"="40px" &&
                 styles[1].getPropertyPriority "height"="important" && styles[0].getPropertyValue "color"<>"")
            owner.Dispose()
            require "actual owner closes native sparse CSS root and child" (timeline.Cancelled && child.Cancelled)
            for target in targets do host.Document.body.removeChild target |> ignore
        do
            let target=host.Document.createElement "div" :?> HTMLElement
            host.Document.body.appendChild target |> ignore
            let inlineStyle=(target:>ElementCSSInlineStyle).style
            inlineStyle.setProperty("width","10px")
            inlineStyle.setProperty("height","20px")
            use timeline=new Timeline(scheduler)
            let width value=Css.width(Css.px value)
            let options={Ranvier.Anime.KeyframeAnimationOptions.Default with Ease=id}
            use first=timeline.AddKeyframes((inlineStyle:>CSSStyleDeclaration),width 0.,Keyframe.values [|width 0.;width 100.;width 200.|],
                duration=200u,position=TimelinePosition.At 0.,options=options)
            let sequence=KeyframeSequence<HTMLElement,Declaration>.Rows [|KeyframeRow.declarations [|width 300.;Css.height(Css.px 200.)|]|]
            use second=timeline.CaptureCssKeyframes(ComputedStyles.forHost host,target,sequence,
                duration=100u,position=TimelinePosition.At 75.,options=options)
            timeline.Seek 125.
            require "owned multi-property CSS sequence shares native sibling identity with per-property style factory"
                (target.getBoundingClientRect().width=187.5 && target.getBoundingClientRect().height=110.)
            second.Revert()
            require "owned CSS sibling sequence restores original native multi-property snapshot"
                (inlineStyle.getPropertyValue "width"="10px" && inlineStyle.getPropertyValue "height"="20px")
            timeline.Cancel()
            host.Document.body.removeChild target |> ignore
        do
            use graph=new Graph()
            use activation=graph.Activate()
            let owner=Api.createRoot id
            let target=host.Document.createElement "div" :?> HTMLElement
            host.Document.body.appendChild target |> ignore
            let inlineStyle=(target:>ElementCSSInlineStyle).style
            inlineStyle.setProperty("width","10px")
            inlineStyle.setProperty("height","20px")
            use timeline=new Timeline(scheduler)
            owner.Attach timeline
            let service=ComputedStyles.forHost host
            let mutable reads=0
            let computed=
                {new ComputedStyleService with
                    member _.Get(element,pseudo) =
                        reads<-reads+1
                        let result=service.Get(element,pseudo)
                        owner.Dispose()
                        result}
            let sequence=KeyframeSequence<HTMLElement,Declaration>.Rows [|KeyframeRow.declarations [|Css.width(Css.px 100.);Css.height(Css.px 200.)|]|]
            let mutable interrupted=false
            try timeline.CaptureCssKeyframes(computed,[|target;target|],sequence,duration=100u) |> ignore
            with :? InvalidOperationException -> interrupted<-true
            require "actual owner disposal after native computed Get stops later sparse CSS cells and publication"
                (interrupted && timeline.Cancelled && reads=1 && inlineStyle.getPropertyValue "width"="10px" &&
                 inlineStyle.getPropertyValue "height"="20px")
            require "owned sparse CSS factory keeps its native computed service borrowed"
                ((service.Get((target:>Element),None)).getPropertyValue "width"="10px")
            host.Document.body.removeChild target |> ignore
        do
            let target=host.Document.createElement "div" :?> HTMLElement
            host.Document.body.appendChild target |> ignore
            let inlineStyle=(target:>ElementCSSInlineStyle).style
            inlineStyle.setProperty("width","10px")
            inlineStyle.setProperty("height","20px")
            let width=NumericProperty((fun () -> target.getBoundingClientRect().width),
                (fun value -> StyleDeclaration.apply inlineStyle (Css.width(Css.px value))))
            let height=NumericProperty((fun () -> target.getBoundingClientRect().height),
                (fun value -> StyleDeclaration.apply inlineStyle (Css.height(Css.px value))))
            use timeline=new Timeline(scheduler)
            let options={Ranvier.Anime.KeyframeAnimationOptions.Default with Ease=id;Precision=None}
            use first=timeline.AddKeyframes(width,Keyframe.values [|0.;100.;200.|],duration=200u,position=TimelinePosition.At 0.,options=options)
            let sequence=KeyframeSequence<NumericProperty,float>.Rows [|KeyframeRow.values [|"alias",300.;"y",200.|]|]
            use second=timeline.AddKeyframes([|"alias",width;"y",height|],sequence,duration=100u,position=TimelinePosition.At 75.,options=options)
            require "owned sparse numeric timeline prepares actual native targets without dispatch"
                (target.getBoundingClientRect().width=10. && target.getBoundingClientRect().height=20.)
            timeline.Seek 125.
            require "owned sparse numeric alias shares replacement identity with single-property native factory"
                (target.getBoundingClientRect().width=187.5 && target.getBoundingClientRect().height=110.)
            second.Revert()
            require "owned sparse native revert restores both captured property values"
                (target.getBoundingClientRect().width=10. && target.getBoundingClientRect().height=20.)
            timeline.Cancel()
            require "native sparse numeric children share root cancellation" (first.Cancelled && second.Cancelled)
            host.Document.body.removeChild target |> ignore
        do
            use graph=new Graph()
            use activation=graph.Activate()
            let owner=Api.createRoot id
            let target=host.Document.createElement "div" :?> HTMLElement
            host.Document.body.appendChild target |> ignore
            let inlineStyle=(target:>ElementCSSInlineStyle).style
            let repeated=(target:>ElementCSSInlineStyle).style
            require "native inline style wrappers retain canonical identity for shared timeline replacement"
                (obj.ReferenceEquals(inlineStyle,repeated))
            inlineStyle.setProperty("width","10px",priority="important")
            let mutable renders=0
            use timeline=new Timeline(scheduler,onRender=(fun _ -> renders<-renders+1))
            owner.Attach timeline
            let options={Ranvier.Anime.KeyframeAnimationOptions.Default with Ease=id;Precision=None}
            let width value=Css.width(Css.px value)
            use first=timeline.AddKeyframes((inlineStyle:>CSSStyleDeclaration),width 0.,
                Keyframe.values [|width 0.;width 100.;width 200.|],duration=200u,position=TimelinePosition.At 0.,options=options)
            use second=timeline.CaptureCssKeyframes(ComputedStyles.forHost host,target,"width",[|Keyframe.toValue(width 300.)|],
                duration=100u,position=TimelinePosition.At 75.,options=options)
            require "owned typed CSS factories prepare cross-facade siblings without native dispatch"
                (inlineStyle.getPropertyValue "width"="10px" && renders=0)
            timeline.Seek 125.
            require "owned computed and explicit style factories share sampled native replacement"
                (target.getBoundingClientRect().width=187.5 && inlineStyle.getPropertyPriority "width"="important" && renders=1)
            timeline.Seek(50.,muteCallbacks=true)
            require "owned CSS root muting preserves backward native replacement"
                (target.getBoundingClientRect().width=50. && renders=1)
            inlineStyle.setProperty("color","red")
            second.Revert()
            require "owned CSS revert restores scoped priority and retains unrelated native edits"
                (inlineStyle.getPropertyValue "width"="10px" && inlineStyle.getPropertyPriority "width"="important" &&
                 inlineStyle.getPropertyValue "color"<>"")
            owner.Dispose()
            require "native owner closes typed CSS root and both cross-facade children"
                (timeline.Cancelled && first.Cancelled && second.Cancelled)
            host.Document.body.removeChild target |> ignore
        do
            let targets=[|host.Document.createElement "div" :?> HTMLElement;host.Document.createElement "div" :?> HTMLElement|]
            let styles=targets |> Array.map(fun target -> (target:>ElementCSSInlineStyle).style)
            for index in 0..1 do
                host.Document.body.appendChild targets[index] |> ignore
                styles[index].setProperty("font-size",if index=0 then "16px" else "32px")
                styles[index].setProperty("width",if index=0 then "10px" else "20px")
            use timeline=new Timeline(scheduler)
            let indices=ResizeArray<uint*uint>()
            let frame=Keyframe.toBy(fun (context:KeyframeContext<HTMLElement,Declaration>) ->
                indices.Add(context.Index,context.Count)
                Css.width(Css.px(100.+float context.Index*100.)))
            use child=timeline.AddKeyframes(targets,Css.width(Css.em 1.),[|frame|],duration=100u,
                options={Ranvier.Anime.KeyframeAnimationOptions.Default with Ease=id;Precision=None})
            require "owned typed CSS contextual construction retains original native styles"
                (styles[0].getPropertyValue "width"="10px" && styles[1].getPropertyValue "width"="20px" &&
                 indices.ToArray()=[|0u,2u;1u,2u|])
            timeline.Seek 50.
            require "owned element keyframes convert em in each actual native font context"
                (targets[0].getBoundingClientRect().width=58. && targets[1].getBoundingClientRect().width=116.)
            timeline.Resume()
            pumpUntil host (fun () -> timeline.Completed)
            require "actual native frame scheduling completes owned contextual CSS children"
                (targets[0].getBoundingClientRect().width=100. && targets[1].getBoundingClientRect().width=200. && child.Playback.Completed)
            child.Revert()
            require "owned contextual CSS revert restores each original native width"
                (styles[0].getPropertyValue "width"="10px" && styles[1].getPropertyValue "width"="20px")
            for target in targets do host.Document.body.removeChild target |> ignore
        do
            use graph=new Graph()
            use activation=graph.Activate()
            let owner=Api.createRoot id
            let target=host.Document.createElement "div" :?> HTMLElement
            host.Document.body.appendChild target |> ignore
            let inlineStyle=(target:>ElementCSSInlineStyle).style
            inlineStyle.setProperty("width","10px")
            use timeline=new Timeline(scheduler)
            owner.Attach timeline
            let service=ComputedStyles.forHost host
            let mutable reads=0
            let computed=
                {new ComputedStyleService with
                    member _.Get(element,pseudo) =
                        reads<-reads+1
                        let result=service.Get(element,pseudo)
                        owner.Dispose()
                        result}
            let mutable interrupted=false
            try
                timeline.CaptureCssKeyframes(computed,[|target;target|],"width",
                    [|Keyframe.toValue(Css.width(Css.px 100.))|],duration=100u) |> ignore
            with :? InvalidOperationException -> interrupted<-true
            require "native owner disposal after computed service Get closes pending child before later reads"
                (interrupted && timeline.Cancelled && reads=1 && inlineStyle.getPropertyValue "width"="10px")
            require "owned computed CSS factory borrows the actual host style service"
                ((service.Get((target:>Element),None)).getPropertyValue "width"="10px")
            host.Document.body.removeChild target |> ignore
        do
            use graph=new Graph()
            use activation=graph.Activate()
            let owner=Api.createRoot id
            let target=host.Document.createElement "div" :?> HTMLElement
            host.Document.body.appendChild target |> ignore
            let inlineStyle=(target:>ElementCSSInlineStyle).style
            inlineStyle.setProperty("width","10px")
            let mutable rootRenders=0
            use timeline=new Timeline(scheduler,onRender=(fun _ -> rootRenders<-rootRenders+1))
            owner.Attach timeline
            let property=NumericProperty((fun () -> target.getBoundingClientRect().width),
                (fun value -> StyleDeclaration.apply inlineStyle (Css.width(Css.px value))))
            let options={KeyframeAnimationOptions.Default with Ease=id;Precision=None}
            use first=timeline.AddKeyframes(property,Keyframe.values [|0.;100.;200.|],duration=200u,position=TimelinePosition.At 0.,options=options)
            let inputs=ResizeArray<float>()
            let frames=[|Keyframe<NumericProperty,float>.ToBy(fun context -> inputs.Add context.PreviousValue.Value;context.PreviousValue.Value+10.)
                         Keyframe<NumericProperty,float>.ToBy(fun context -> inputs.Add context.PreviousValue.Value;context.PreviousValue.Value+20.)|]
            use second=timeline.AddKeyframes(property,frames,duration=200u,position=TimelinePosition.At 75.,options=options)
            require "public owned timeline keyframes prepare sampled caches without native target dispatch"
                (target.getBoundingClientRect().width=10. && inputs.ToArray()=[|200.;200.|] && rootRenders=0)
            timeline.Seek 125.
            require "public owned timeline keyframes arbitrate native takeover and notify one root render"
                (target.getBoundingClientRect().width=142.5 && rootRenders=1)
            timeline.Seek(50.,muteCallbacks=true)
            require "public owned timeline keyframes recover backward geometry and inherit parent muting"
                (target.getBoundingClientRect().width=50. && rootRenders=1)
            owner.Dispose()
            host.Cycle(milliseconds=100)
            host.Frame()
            require "actual native owner cancellation closes the root and both public keyframe children"
                (timeline.Cancelled && first.Cancelled && second.Cancelled && target.getBoundingClientRect().width=50.)
            host.Document.body.removeChild target |> ignore
        do
            use graph=new Graph()
            use activation=graph.Activate()
            let owner=Api.createRoot id
            let target=host.Document.createElement "div" :?> HTMLElement
            host.Document.body.appendChild target |> ignore
            let inlineStyle=(target:>ElementCSSInlineStyle).style
            inlineStyle.setProperty("width","10px")
            let registry=FrameSiblingRegistry()
            let keys=[|struct(box target,"width")|]
            let inputCaches=ResizeArray<float>()
            let writes=ResizeArray<float>()
            let current()=not owner.IsDisposed
            let build offset (frames:FrameInput array) computed =
                let property={FrameProperty.Target=0;Name="width";Original=ValueInterpolation.Number 10.;Frames=frames;ConvertUnit=None}
                let transaction=registry.Begin(keys,current)
                let resolve _ index (previous:FrameTween option) =
                    if computed then
                        let value=previous.Value.CachedEnd.Scalar.Value
                        inputCaches.Add value
                        Some {FrameInput.Default with To=Some(ValueInterpolation.Number(value+(if index=0 then 10. else 20.)))}
                    else Some frames[index]
                let program=KeyframeCompilation.compileWithSiblings({FrameDefaults.Default with Duration=200.;Offset=offset;Ease=id},[|property|],
                    resolve,(fun _ -> ValueInterpolation.Number 10.),transaction.Compilation,current) |> Option.get
                let nodes=transaction.Bind(program,1.,program.IterationDuration)
                let driver=new KeyframePlayback(scheduler,program,(fun _ _ output ->
                    writes.Add output.Scalar.Value
                    StyleDeclaration.apply inlineStyle (Css.width(Css.px output.Scalar.Value))),
                    true,false,-1,PlaybackOptions.Default,siblings=nodes)
                owner.Attach driver
                driver.Initialize(timelineChild=true)
                if not(transaction.Commit()) then failwith "native sibling generation was superseded"
                driver,nodes
            let first,firstNodes=build 0. (KeyframeCompilation.values [|ValueInterpolation.Number 0.;ValueInterpolation.Number 100.;ValueInterpolation.Number 200.|]) false
            use first=first
            first.Clock.SampleChild(75.,PlaybackTickMode.Auto,true,240.,false,false,current,internalRender=true)
            let second,_=build 75. [|FrameInput.Default;FrameInput.Default|] true
            use second=second
            require "native shared sibling construction separates tail200 sampled75 and raw predecessor100"
                (target.getBoundingClientRect().width=10. && inputCaches.ToArray()=[|200.;200.|] &&
                 firstNodes[0].Change=75. && firstNodes[1].Overridden)
            second.Clock.SampleChild(-75.,PlaybackTickMode.Force,true,240.,true,false,current,internalRender=true)
            first.Clock.SampleChild(0.,PlaybackTickMode.Force,true,240.,true,false,current,internalRender=true)
            writes.Clear()
            first.Clock.SampleChild(125.,PlaybackTickMode.Auto,true,240.,false,true,current)
            second.Clock.SampleChild(50.,PlaybackTickMode.Auto,true,240.,false,true,current)
            require "native replacement arbitration spans driver arrays and renders only the eligible takeover"
                (target.getBoundingClientRect().width=142.5 && writes.ToArray()=[|142.5|])
            second.Clock.SampleChild(-25.,PlaybackTickMode.Auto,true,240.,true,true,current)
            first.Clock.SampleChild(50.,PlaybackTickMode.Auto,true,240.,true,true,current)
            require "native shared sibling playback restores earlier geometry on a backward jump"
                (target.getBoundingClientRect().width=50.)
            owner.Dispose()
            first.Clock.SampleChild(125.,PlaybackTickMode.Force,true,240.,false,true,current)
            second.Clock.SampleChild(50.,PlaybackTickMode.Force,true,240.,false,true,current)
            registry.Clear()
            require "native shared sibling owner cancellation suppresses both drivers and releases links"
                (first.Clock.Cancelled && second.Clock.Cancelled && target.getBoundingClientRect().width=50. &&
                 isNull firstNodes[0].Next && isNull firstNodes[1].Previous)
            host.Document.body.removeChild target |> ignore
        do
            let target=host.Document.createElement "div" :?> HTMLElement
            host.Document.body.appendChild target |> ignore
            let inlineStyle=(target:>ElementCSSInlineStyle).style
            inlineStyle.setProperty("width","7px")
            let mutable active:Timer option=None
            let mutable armed=false
            let modifier value=
                if armed then armed<-false;active.Value.Seek(100.,muteCallbacks=true)
                value
            let frame={FrameInput.Default with From=Some(ValueInterpolation.Number 0.);To=Some(ValueInterpolation.Number 100.)}
            let property={FrameProperty.Target=0;Name="width";Original=ValueInterpolation.Number 7.;Frames=[|frame|];ConvertUnit=None}
            let program=KeyframeCompilation.compile({FrameDefaults.Default with Duration=100.;Ease=id;Modifier=modifier},[|property|],(fun () -> true)) |> Option.get
            use child=new KeyframePlayback(scheduler,program,(fun _ _ output -> StyleDeclaration.apply inlineStyle (Css.width(Css.px output.Scalar.Value))),
                false,false,-1,PlaybackOptions.Default)
            active<-Some child.Clock
            armed<-true
            child.Initialize(timelineChild=true)
            require "native child initialization preserves superseding seek geometry cache and completion flags"
                (target.getBoundingClientRect().width=100. && child.Clock.CurrentTime=100. && child.Clock.Began &&
                 child.Clock.Completed && child.CurrentValues[0].Scalar=Some 100. && child.RenderVersion=1L)
            host.Document.body.removeChild target |> ignore
        do
            use graph=new Graph()
            use activation=graph.Activate()
            let owner=Api.createRoot id
            let target=host.Document.createElement "div" :?> HTMLElement
            host.Document.body.appendChild target |> ignore
            let inlineStyle=(target:>ElementCSSInlineStyle).style
            inlineStyle.setProperty("width","7px")
            let mutable armed=false
            let modify value=
                if armed then owner.Dispose()
                value
            let frame={FrameInput.Default with From=Some(ValueInterpolation.Number 0.);To=Some(ValueInterpolation.Number 100.)}
            let property={FrameProperty.Target=0;Name="width";Original=ValueInterpolation.Number 7.;Frames=[|frame|];ConvertUnit=None}
            let program=KeyframeCompilation.compile({FrameDefaults.Default with Duration=100.;Ease=id;Modifier=modify},[|property|],(fun () -> true)) |> Option.get
            let mutable notifications=0
            use child=new KeyframePlayback(scheduler,program,(fun _ _ output -> StyleDeclaration.apply inlineStyle (Css.width(Css.px output.Scalar.Value))),
                false,false,-1,PlaybackOptions.Default,onRender=(fun _ -> notifications<-notifications+1),onUpdate=(fun _ -> notifications<-notifications+1))
            owner.Attach child
            child.Initialize(timelineChild=true)
            require "owned keyframe child initialization refreshes native caches without target writes"
                (target.getBoundingClientRect().width=7. && child.RenderVersion=0L && notifications=0)
            child.Clock.SampleChild(50.,PlaybackTickMode.Force,true,240.,false,true,(fun () -> true))
            require "muted owned keyframe child dispatches native geometry without lifecycle callbacks"
                (target.getBoundingClientRect().width=50. && child.RenderVersion=1L && notifications=0)
            child.Clock.SampleChild(75.,PlaybackTickMode.Force,true,240.,false,true,(fun () -> true),internalRender=true)
            require "internal child sampling retains a new cache independently of native geometry"
                (target.getBoundingClientRect().width=50. && child.CurrentValues[0].Scalar=Some 75. && child.RenderVersion=1L)
            child.Clock.SampleChild(80.,PlaybackTickMode.Skip,false,240.,false,true,(fun () -> true))
            child.Clock.Resume()
            host.Cycle(milliseconds=1)
            host.Frame()
            require "skipped and parented native children cannot report stale dispatch or schedule their own writes"
                (target.getBoundingClientRect().width=50. && child.RenderVersion=1L && notifications=0)
            armed<-true
            child.Clock.SampleChild(90.,PlaybackTickMode.Force,true,240.,false,true,(fun () -> true),internalRender=true)
            require "actual native owner cancellation inside internal child sampling suppresses cache publication and dispatch"
                (owner.IsDisposed && child.Clock.Cancelled && target.getBoundingClientRect().width=50. &&
                 child.CurrentValues[0].Scalar=Some 75. && child.RenderVersion=1L && notifications=0)
            host.Document.body.removeChild target |> ignore
        do
            let elements=[|host.Document.createElement "div" :?> HTMLElement;host.Document.createElement "div" :?> HTMLElement|]
            let styles=elements |> Array.map(fun element -> (element:>ElementCSSInlineStyle).style)
            for index in 0..1 do
                host.Document.body.appendChild elements[index] |> ignore
                styles[index].setProperty("font-size",(if index=0 then "16px" else "32px"))
                styles[index].setProperty("width",(if index=0 then "7px" else "11px"))
            let calls=ResizeArray<string>()
            let frame=Keyframe.compute(fun (context:KeyframeContext<HTMLElement,Declaration>) ->
                if context.Count<>2u || not(obj.ReferenceEquals(context.Target,elements[int context.Index])) then failwith "computed frame target context"
                calls.Add "frame"
                (Keyframe.toValue(Declaration.important(Css.width(Css.em 3.))))
                    .WithFromBy(fun _ -> calls.Add "from";Css.width(Css.em 1.))
                    .WithDurationBy(fun _ -> calls.Add "duration";100u+100u*context.Index))
            let outer=frame.WithFromBy(fun _ -> failwith "replaced outer source")
                           .WithDurationBy(fun _ -> failwith "replaced outer duration")
            let mutable renders=0
            use animation=Keyframes.CaptureCss(scheduler,ComputedStyles.forHost host,elements,"width",[|outer|],
                options={Ranvier.Anime.KeyframeAnimationOptions.Default with Autoplay=false;Ease=id;OnRender=(fun _ -> renders<-renders+1)})
            renders<-0
            animation.Playback.Seek 50.
            require "computed typed frames use actual target contexts returned timing and native element units"
                (abs(elements[0].getBoundingClientRect().width-32.)<0.02 &&
                 abs(elements[1].getBoundingClientRect().width-48.)<0.02 && renders=1 &&
                 calls.ToArray()=[|"frame";"from";"duration";"frame";"from";"duration"|] &&
                 styles[0].getPropertyPriority "width"="important" && styles[1].getPropertyPriority "width"="important")
            animation.Playback.Stretch 400.
            animation.Playback.Seek 100.
            require "computed frame stretching keeps captured endpoints and does not rerun providers"
                (abs(elements[0].getBoundingClientRect().width-32.)<0.02 &&
                 abs(elements[1].getBoundingClientRect().width-48.)<0.02 && calls.Count=6)
            animation.Revert()
            require "computed typed frames restore original values and priorities"
                (styles[0].getPropertyValue "width"="7px" && styles[1].getPropertyValue "width"="11px" &&
                 styles[0].getPropertyPriority "width"="" && styles[1].getPropertyPriority "width"="" && calls.Count=6)
            for element in elements do host.Document.body.removeChild element |> ignore
        do
            let target=host.Document.createElement "div" :?> HTMLElement
            host.Document.body.appendChild target |> ignore
            let inlineStyle=(target:>ElementCSSInlineStyle).style
            inlineStyle.setProperty("width","7px")
            let calls=ResizeArray<string>()
            let pair=Keyframe.betweenBy(fun (_:KeyframeContext<HTMLElement,Declaration>) ->
                calls.Add "pair";Css.width(Css.px 10.),Css.width(Css.px 110.))
            let frame=pair.WithFromBy(fun _ -> calls.Add "from";Css.width(Css.px 999.)).WithDurationBy(fun _ -> calls.Add "duration";100u)
            use animation=Keyframes.CaptureCss(scheduler,ComputedStyles.forHost host,target,"width",[|frame|],
                options={Ranvier.Anime.KeyframeAnimationOptions.Default with Autoplay=false;Ease=id})
            animation.Playback.Seek 50.
            require "typed endpoint pair callbacks retain source timing effects and override the sampled source"
                (abs(target.getBoundingClientRect().width-60.)<0.02 && calls.ToArray()=[|"pair";"from";"duration"|])
            animation.Revert()
            require "typed endpoint pair revert restores the native inline snapshot" (inlineStyle.getPropertyValue "width"="7px")
            host.Document.body.removeChild target |> ignore
        do
            use graph=new Graph()
            use activation=graph.Activate()
            let owner=Api.createRoot id
            let frame=Keyframe.compute(fun (_:KeyframeContext<HTMLElement,Declaration>) ->
                owner.Dispose();Unchecked.defaultof<Keyframe<HTMLElement,Declaration>>)
            let mutable interrupted=false
            try Keyframes.CaptureCss(scheduler,ComputedStyles.forHost host,element,"width",[|frame|],owner=owner) |> ignore
            with :? ObjectDisposedException -> interrupted<-true
            require "actual owner disposal inside computed typed frame suppresses invalid return and dispatch"
                (interrupted && owner.IsDisposed && style.getPropertyValue "width"="")
        do
            let elements=[|host.Document.createElement "div" :?> HTMLElement;host.Document.createElement "div" :?> HTMLElement|]
            let styles=elements |> Array.map(fun element -> (element:>ElementCSSInlineStyle).style)
            for element in elements do host.Document.body.appendChild element |> ignore
            for index in 0..1 do
                styles[index].setProperty("width",(if index=0 then "7px" else "17px"))
                styles[index].setProperty("height",(if index=0 then "11px" else "21px"))
            let handles name=
                styles |> Array.map(fun style ->
                    NumericProperty((fun () -> Double.Parse(style.getPropertyValue(name).Replace("px",""),CultureInfo.InvariantCulture)),
                                    (fun value -> StyleDeclaration.apply style (Declaration.create name (ValueInterpolation.number value+"px")))))
            let sequence=KeyframeSequence<NumericProperty,float>.Rows [|
                (KeyframeRow<NumericProperty,float>.Create [||]).WithValueBy("width",(fun context -> context.Target.Value+100.)).WithDuration 100u
                (KeyframeRow<NumericProperty,float>.Create [||]).WithValueBy("height",(fun context -> context.Target.Value+200.)).WithDuration 100u |]
            let mutable renders=0
            use animation=Keyframes.Numeric(scheduler,[|"width",handles "width";"height",handles "height"|],sequence,duration=200u,
                options={Ranvier.Anime.KeyframeAnimationOptions.Default with Autoplay=false;Ease=id;OnRender=(fun _ -> renders<-renders+1)})
            renders<-0
            animation.Playback.Seek 50.
            require "public sparse numeric rows share one root callback across properties and native targets"
                (renders=1 && animation.TargetCount=2 && abs(elements[0].getBoundingClientRect().width-57.)<0.02 &&
                 abs(elements[1].getBoundingClientRect().width-67.)<0.02 && abs(elements[0].getBoundingClientRect().height-11.)<0.02)
            animation.Playback.Stretch 400.
            animation.Playback.Seek 300.
            require "public numeric row stretching scales every native property on the same clock"
                (abs(elements[0].getBoundingClientRect().width-107.)<0.02 && abs(elements[1].getBoundingClientRect().width-117.)<0.02 &&
                 abs(elements[0].getBoundingClientRect().height-111.)<0.02 && abs(elements[1].getBoundingClientRect().height-121.)<0.02)
            animation.Revert()
            require "public numeric rows restore every captured native property"
                (styles[0].getPropertyValue "width"="7px" && styles[0].getPropertyValue "height"="11px" &&
                 styles[1].getPropertyValue "width"="17px" && styles[1].getPropertyValue "height"="21px")
            for element in elements do host.Document.body.removeChild element |> ignore
        do
            let first=host.Document.createElement "div" :?> HTMLElement
            let second=host.Document.createElement "div" :?> HTMLElement
            first.setAttribute("class","anime-sequence-first")
            second.setAttribute("class","anime-sequence-second")
            let sheet=host.Document.createElement "style"
            (sheet:>Node).textContent<-".anime-sequence-first { width:16px; height:12px; } .anime-sequence-second { width:32px; height:24px; }"
            host.Document.head.appendChild sheet |> ignore
            host.Document.body.appendChild first |> ignore
            host.Document.body.appendChild second |> ignore
            let firstStyle=(first:>ElementCSSInlineStyle).style
            let secondStyle=(second:>ElementCSSInlineStyle).style
            firstStyle.setProperty("font-size","16px")
            secondStyle.setProperty("font-size","32px")
            secondStyle.setProperty("height","8px",priority="important")
            let originalChildren=host.Document.body.childNodes.length
            let service=ComputedStyles.forHost host
            let mutable reads=0
            let computed={new ComputedStyleService with member _.Get(target,pseudo)=reads<-reads+1;service.Get(target,pseudo)}
            let previous=ResizeArray<string>()
            let sequence=KeyframeSequence<HTMLElement,Declaration>.Rows [|
                (KeyframeRow<HTMLElement,Declaration>.Create [||])
                    .WithValueBy("width",(fun context -> Css.width(Css.em(float context.Index+2.)))).WithDuration 100u
                (KeyframeRow<HTMLElement,Declaration>.Create [||])
                    .WithValueBy("height",(fun context ->
                        previous.Add(Declaration.value context.PreviousValue.Value)
                        Css.height(Css.px(200.+20.*float context.Index)))).WithDuration 100u |]
            let mutable renders=0
            use animation=Keyframes.CaptureCss(scheduler,computed,[|first;second|],sequence,duration=200u,
                options={Ranvier.Anime.KeyframeAnimationOptions.Default with Autoplay=false;Ease=id;OnRender=(fun _ -> renders<-renders+1)})
            renders<-0
            animation.Playback.Seek 50.
            require "public typed CSS rows capture each computed property and independent element unit context"
                (reads=4 && previous.ToArray()=[|"12px";"8px"|] && renders=1 && animation.TargetCount=2 &&
                 abs(first.getBoundingClientRect().width-24.)<0.02 && abs(second.getBoundingClientRect().width-64.)<0.02 &&
                 abs(first.getBoundingClientRect().height-12.)<0.02 && abs(second.getBoundingClientRect().height-8.)<0.02 &&
                 host.Document.body.childNodes.length=originalChildren)
            animation.Playback.Stretch 400.
            animation.Playback.Seek 300.
            require "typed CSS row stretching preserves sparse holds without computed rereads"
                (reads=4 && abs(first.getBoundingClientRect().width-32.)<0.02 && abs(second.getBoundingClientRect().width-96.)<0.02 &&
                 abs(first.getBoundingClientRect().height-106.)<0.02 && abs(second.getBoundingClientRect().height-114.)<0.02)
            firstStyle.setProperty("opacity","0.5")
            animation.Revert()
            require "typed CSS rows restore per-property absence priority stylesheet fallback and unrelated edits"
                (firstStyle.getPropertyValue "width"="" && firstStyle.getPropertyValue "height"="" &&
                 secondStyle.getPropertyValue "width"="" && secondStyle.getPropertyValue "height"="8px" &&
                 secondStyle.getPropertyPriority "height"="important" && firstStyle.getPropertyValue "opacity"="0.5" &&
                 abs(first.getBoundingClientRect().width-16.)<0.02 && abs(first.getBoundingClientRect().height-12.)<0.02)
            host.Document.body.removeChild first |> ignore
            host.Document.body.removeChild second |> ignore
            host.Document.head.removeChild sheet |> ignore
        do
            let target=host.Document.createElement "div" :?> HTMLElement
            host.Document.body.appendChild target |> ignore
            let inlineStyle=(target:>ElementCSSInlineStyle).style
            inlineStyle.setProperty("width","10px")
            inlineStyle.setProperty("height","20px",priority="important")
            inlineStyle.setProperty("color","red")
            inlineStyle.setProperty("transform","translate(3px,4px)")
            let originals=[|"width";"height";"color";"transform"|] |> Array.map(fun name -> name,inlineStyle.getPropertyValue name,inlineStyle.getPropertyPriority name)
            let sequence=KeyframeSequence<HTMLElement,Declaration>.Offsets [|
                (KeyframeOffset.declarations 0. [|Css.width(Css.px 0.);Css.height(Css.px 0.);Declaration.create "color" "#ff0000";Declaration.create "transform" "translate(0px,0px)"|]).WithEase(Ease.named "inQuad")
                (KeyframeOffset.declarations 0.5 [|Css.width(Css.px 100.);Declaration.create "color" "#00ff00";Declaration.create "transform" "translate(100px,200px)"|]).WithEase(Ease.named "outQuad")
                KeyframeOffset.declarations 1. [|Css.width(Css.px 200.);Css.height(Css.px 200.);Declaration.create "color" "#0000ff";Declaration.create "transform" "translate(200px,400px)"|] |]
            use animation=Keyframes.CaptureCss(scheduler,ComputedStyles.forHost host,target,sequence,duration=200u,
                options={Ranvier.Anime.KeyframeAnimationOptions.Default with Autoplay=false;Ease=id})
            let start=target.getBoundingClientRect()
            animation.Playback.Seek 50.
            let first=target.getBoundingClientRect()
            require "public typed offset keys transfer easing independently across sparse color geometry and compounds"
                (abs(first.width-25.)<0.02 && abs(first.height-12.5)<0.02 &&
                 abs(first.left-start.left-25.)<0.02 && abs(first.top-start.top-50.)<0.02)
            animation.Playback.Seek 150.
            let second=target.getBoundingClientRect()
            let color=inlineStyle.getPropertyValue "color"
            require "native offset playback retains preceding-property easing and legacy color channels"
                (abs(second.width-175.)<0.02 && abs(second.height-112.5)<0.02 &&
                 abs(second.left-start.left-175.)<0.02 && abs(second.top-start.top-350.)<0.02 &&
                 (color.Contains("0, 128, 221") || color.Contains("0,128,221")))
            inlineStyle.setProperty("opacity","0.5")
            animation.Revert()
            require "public CSS offsets restore every original property without replacing unrelated inline style"
                ((originals |> Array.forall(fun (name,value,priority) -> inlineStyle.getPropertyValue name=value && inlineStyle.getPropertyPriority name=priority)) &&
                 inlineStyle.getPropertyValue "opacity"="0.5")
            host.Document.body.removeChild target |> ignore
        do
            let target=host.Document.createElement "div" :?> HTMLElement
            host.Document.body.appendChild target |> ignore
            let inlineStyle=((target:>ElementCSSInlineStyle).style:>CSSStyleDeclaration)
            inlineStyle.setProperty("width","7px")
            inlineStyle.setProperty("height","11px",priority="important")
            let sequence=KeyframeSequence<CSSStyleDeclaration,Declaration>.Rows [|KeyframeRow.declarations [|Css.width(Css.px 107.);Css.height(Css.px 211.)|]|]
            use animation=Keyframes.CaptureInline(scheduler,inlineStyle,sequence,duration=100u,
                options={Ranvier.Anime.KeyframeAnimationOptions.Default with Autoplay=false;Ease=id})
            animation.Playback.Seek 50.
            require "style-only sparse sequence overloads drive native declarations through shared playback"
                (inlineStyle.getPropertyValue "width"="57px" && inlineStyle.getPropertyValue "height"="111px")
            animation.Revert()
            require "style-only multi-property snapshots restore original priority"
                (inlineStyle.getPropertyValue "width"="7px" && inlineStyle.getPropertyValue "height"="11px" &&
                 inlineStyle.getPropertyPriority "height"="important")
            host.Document.body.removeChild target |> ignore
        do
            use graph=new Graph()
            use activation=graph.Activate()
            let owner=Api.createRoot id
            let target=host.Document.createElement "div" :?> HTMLElement
            host.Document.body.appendChild target |> ignore
            let inlineStyle=(target:>ElementCSSInlineStyle).style
            inlineStyle.setProperty("width","7px")
            inlineStyle.setProperty("height","11px")
            let mutable calls=0
            let computed=
                { new ComputedStyleService with
                    member _.Get(element,pseudo)=
                        if pseudo.IsSome || not(obj.ReferenceEquals(element,(target:>Element))) then failwith "unexpected computed target"
                        calls<-calls+1
                        owner.Dispose()
                        Unchecked.defaultof<CSSStyleDeclaration> }
            let sequence=KeyframeSequence<HTMLElement,Declaration>.Rows [|KeyframeRow.declarations [|Css.width(Css.px 100.);Css.height(Css.px 200.)|]|]
            let mutable interrupted=false
            try Keyframes.CaptureCss(scheduler,computed,target,sequence,owner=owner) |> ignore
            with :? ObjectDisposedException -> interrupted<-true
            require "multi-property computed capture checks actual ownership before reading returned styles or later properties"
                (interrupted && calls=1 && owner.IsDisposed && inlineStyle.getPropertyValue "width"="7px" && inlineStyle.getPropertyValue "height"="11px")
            host.Document.body.removeChild target |> ignore
        do
            use graph=new Graph()
            use activation=graph.Activate()
            let owner=Api.createRoot id
            let target=host.Document.createElement "div" :?> HTMLElement
            host.Document.body.appendChild target |> ignore
            let inlineStyle=(target:>ElementCSSInlineStyle).style
            let mutable armed=false
            let mutable calls=0
            let modify value=if armed then calls<-calls+1;owner.Dispose();value else value
            let sequence=KeyframeSequence<HTMLElement,Declaration>.Rows [|KeyframeRow.declarations [|Css.width(Css.px 100.);Css.height(Css.px 200.)|]|]
            use animation=Keyframes.DeclarationOn(scheduler,target,[|Css.width(Css.px 0.);Css.height(Css.px 0.)|],sequence,duration=100u,owner=owner,
                options={Ranvier.Anime.KeyframeAnimationOptions.Default with Ease=id;Modifier=modify})
            let before=inlineStyle.getPropertyValue "width",inlineStyle.getPropertyValue "height"
            armed<-true
            pumpUntil host (fun () -> animation.Cancelled)
            require "scheduled multi-property CSS sequences stop all dispatch on actual owner disposal"
                (calls=1 && owner.IsDisposed && (inlineStyle.getPropertyValue "width",inlineStyle.getPropertyValue "height")=before)
            host.Document.body.removeChild target |> ignore
        for logicalFirst in [false;true] do
            let target=host.Document.createElement "div" :?> HTMLElement
            host.Document.body.appendChild target |> ignore
            let inlineStyle=((target:>ElementCSSInlineStyle).style:>CSSStyleDeclaration)
            if logicalFirst then inlineStyle.setProperty("margin-inline-start","11px")
            inlineStyle.setProperty("margin-left","7px")
            if not logicalFirst then inlineStyle.setProperty("margin-inline-start","11px")
            use group=new Animatable(scheduler)
            let handle=group.DeclarationOn(target,Declaration.create "margin" "0px",duration=100u)
            handle.To(Declaration.create "margin" "100px",ease=id)
            handle.Playback.Seek 50.
            inlineStyle.setProperty("margin-inline-start","17px")
            inlineStyle.setProperty("height","23px")
            group.Revert()
            let computed=(ComputedStyles.forHost host).Get((target:>Element),None)
            require ("native CSS animatable retains logical precedence "+logicalFirst.ToString())
                (computed.getPropertyValue "margin-left"=(if logicalFirst then "7px" else "17px") &&
                 inlineStyle.getPropertyValue "margin-inline-start"="17px" && inlineStyle.getPropertyValue "height"="23px")
            host.Document.body.removeChild target |> ignore
        do
            let target=host.Document.createElement "div" :?> HTMLElement
            host.Document.body.appendChild target |> ignore
            let inlineStyle=((target:>ElementCSSInlineStyle).style:>CSSStyleDeclaration)
            inlineStyle.setProperty("border-top-left-radius","7px",priority="important")
            use group=new Animatable(scheduler)
            let handle=group.DeclarationOn(target,Declaration.create "-webkit-border-radius" "0px",duration=100u)
            handle.To(Declaration.create "-webkit-border-radius" "100px",ease=id)
            handle.Playback.Seek 50.
            require "native shorthand animatable getter retains its sampled cache"
                (Declaration.value handle.Value="50px")
            handle.To(Declaration.create "-webkit-border-radius" "200px",ease=id)
            handle.Playback.Seek 50.
            require "native shorthand alias retarget starts from its existing cache"
                (inlineStyle.getPropertyValue "border-top-left-radius"="125px" && Declaration.value handle.Value="125px")
            group.Revert()
            require "native shorthand alias animatable restores canonical priority and absent corners"
                (inlineStyle.getPropertyValue "border-top-left-radius"="7px" &&
                 inlineStyle.getPropertyPriority "border-top-left-radius"="important" &&
                 inlineStyle.getPropertyValue "border-bottom-right-radius"="")
            host.Document.body.removeChild target |> ignore
        do
            let target=host.Document.createElement "div" :?> HTMLElement
            host.Document.body.appendChild target |> ignore
            let inlineStyle=((target:>ElementCSSInlineStyle).style:>CSSStyleDeclaration)
            inlineStyle.setProperty("margin","10px 20px 30px 40px")
            use group=new Animatable(scheduler)
            let handle=group.CaptureCss(ComputedStyles.forHost host,target,"margin",duration=100u,options={AnimatablePropertyOptions.Default with Ease=id})
            handle.ToComponents [|100.;200.;300.;400.|]
            handle.Playback.Seek 50.
            require "native computed shorthand animatable drives captured component geometry"
                (inlineStyle.getPropertyValue "margin-top"="55px" && inlineStyle.getPropertyValue "margin-right"="110px" &&
                 inlineStyle.getPropertyValue "margin-bottom"="165px" && inlineStyle.getPropertyValue "margin-left"="220px")
            inlineStyle.setProperty("height","23px")
            group.Revert()
            require "native computed shorthand animatable restores authored sides and unrelated edits"
                (inlineStyle.getPropertyValue "margin"="10px 20px 30px 40px" && inlineStyle.getPropertyValue "height"="23px")
            host.Document.body.removeChild target |> ignore
        do
            let target=host.Document.createElement "div" :?> HTMLElement
            host.Document.body.appendChild target |> ignore
            let inlineStyle=((target:>ElementCSSInlineStyle).style:>CSSStyleDeclaration)
            inlineStyle.setProperty("--m","1px 2px")
            inlineStyle.setProperty("margin","var(--m)")
            use group=new Animatable(scheduler)
            let handle=group.DeclarationOn(target,Declaration.create "margin" "0px",duration=100u)
            handle.To(Declaration.create "margin" "100px",ease=id)
            handle.Playback.Seek 50.
            group.Revert()
            inlineStyle.setProperty("--m","3px 4px")
            let computed=(ComputedStyles.forHost host).Get((target:>Element),None)
            require "native shorthand animatable revert retains custom property dependence"
                (inlineStyle.cssText.Contains("var(--m)") && computed.getPropertyValue "margin-top"="3px" &&
                 computed.getPropertyValue "margin-right"="4px")
            host.Document.body.removeChild target |> ignore
        do
            let target=host.Document.createElement "div" :?> HTMLElement
            host.Document.body.appendChild target |> ignore
            let inlineStyle=((target:>ElementCSSInlineStyle).style:>CSSStyleDeclaration)
            inlineStyle.setProperty("--m","1px 2px")
            inlineStyle.setProperty("margin","var(--m)")
            inlineStyle.setProperty("margin-left","7px",priority="important")
            let before=inlineStyle.cssText
            use group=new Animatable(scheduler)
            let mutable rejected=false
            try group.DeclarationOn(target,Declaration.create "margin" "0px") |> ignore
            with :? NotSupportedException -> rejected<-true
            require "opaque animatable snapshot admission preserves the native style and group"
                (rejected && group.Count=0 && not group.Cancelled && inlineStyle.cssText=before)
            let handle=group.DeclarationOn(target,Css.width(Css.px 0.),duration=50u)
            handle.To(Css.width(Css.px 50.),ease=id)
            handle.Playback.Seek 25.
            require "native animatable group accepts another property after rejected opaque capture"
                (inlineStyle.getPropertyValue "width"="25px")
            group.Revert()
            let computed=(ComputedStyles.forHost host).Get((target:>Element),None)
            require "later animatable revert preserves the untouched opaque shorthand"
                (inlineStyle.getPropertyValue "width"="" && computed.getPropertyValue "margin-top"="1px" &&
                 computed.getPropertyValue "margin-left"="7px")
            host.Document.body.removeChild target |> ignore
        do
            let target=host.Document.createElement "div" :?> HTMLElement
            host.Document.body.appendChild target |> ignore
            let inlineStyle=((target:>ElementCSSInlineStyle).style:>CSSStyleDeclaration)
            inlineStyle.setProperty("margin-left","7px",priority="important")
            inlineStyle.setProperty("margin-right","8px")
            inlineStyle.setProperty("height","9px")
            use group=new Animatable(scheduler)
            let handle=group.DeclarationOn(target,Declaration.create "margin" "0px",duration=100u)
            handle.To(Declaration.create "margin" "100px",duration=100u,ease=id)
            handle.Playback.Seek 50.
            require "native CSS animatable shorthand retarget drives expanded sides"
                (inlineStyle.getPropertyValue "margin-left"="50px" && inlineStyle.getPropertyValue "margin-right"="50px")
            inlineStyle.setProperty("height","23px")
            group.Revert()
            require "native CSS animatable shorthand revert retains mixed priorities and unrelated edits"
                (inlineStyle.getPropertyValue "margin-left"="7px" && inlineStyle.getPropertyPriority "margin-left"="important" &&
                 inlineStyle.getPropertyValue "margin-right"="8px" && inlineStyle.getPropertyPriority "margin-right"="" &&
                 inlineStyle.getPropertyValue "margin-top"="" && inlineStyle.getPropertyValue "margin-bottom"="" &&
                 inlineStyle.getPropertyValue "height"="23px")
            host.Document.body.removeChild target |> ignore
        do
            let target=host.Document.createElement "div" :?> HTMLElement
            host.Document.body.appendChild target |> ignore
            let inlineStyle=((target:>ElementCSSInlineStyle).style:>CSSStyleDeclaration)
            inlineStyle.setProperty("margin-left","7px",priority="important")
            inlineStyle.setProperty("margin-right","8px")
            inlineStyle.setProperty("height","9px")
            use animation=Keyframes.DeclarationOn(scheduler,target,Declaration.create "margin" "0px",
                [|Keyframe.toValue(Declaration.create "margin" "100px")|],duration=100u,
                options={KeyframeAnimationOptions.Default with Autoplay=false;Ease=id})
            animation.Playback.Seek 50.
            require "native CSS shorthand keyframes dispatch all expanded margin sides"
                ([|"margin-top";"margin-right";"margin-bottom";"margin-left"|] |> Array.forall(fun name -> inlineStyle.getPropertyValue name="50px"))
            inlineStyle.setProperty("height","23px")
            animation.Revert()
            require "native CSS shorthand revert preserves partial longhands and mixed priorities"
                (inlineStyle.getPropertyValue "margin-left"="7px" && inlineStyle.getPropertyPriority "margin-left"="important" &&
                 inlineStyle.getPropertyValue "margin-right"="8px" && inlineStyle.getPropertyPriority "margin-right"="" &&
                 inlineStyle.getPropertyValue "margin-top"="" && inlineStyle.getPropertyValue "margin-bottom"="" &&
                 inlineStyle.getPropertyValue "height"="23px")
            host.Document.body.removeChild target |> ignore
        do
            let target=host.Document.createElement "div" :?> HTMLElement
            host.Document.body.appendChild target |> ignore
            let inlineStyle=((target:>ElementCSSInlineStyle).style:>CSSStyleDeclaration)
            inlineStyle.setProperty("border-image-slice","13",priority="important")
            inlineStyle.setProperty("border-image-repeat","round")
            use animation=Keyframes.DeclarationOn(scheduler,target,Declaration.create "border" "1px solid rgb(255,0,0)",
                [|Keyframe.toValue(Declaration.create "border" "5px solid rgb(0,0,255)")|],duration=100u,
                options={KeyframeAnimationOptions.Default with Autoplay=false;Ease=id})
            animation.Playback.Seek 100.
            require "native border keyframes exercise reset-only border image longhands"
                (inlineStyle.getPropertyValue "border-image-slice"<>"13")
            animation.Revert()
            require "native border keyframes restore reset-only fields and their individual priority"
                (inlineStyle.getPropertyValue "border-image-slice"="13" && inlineStyle.getPropertyPriority "border-image-slice"="important" &&
                 inlineStyle.getPropertyValue "border-image-repeat"="round" && inlineStyle.getPropertyPriority "border-image-repeat"="" &&
                 inlineStyle.getPropertyValue "border-top-width"="")
            host.Document.body.removeChild target |> ignore
        for property,originalLeaf in ["-webkit-border-radius","border-top-left-radius";"-webkit-margin-start","margin-inline-start"] do
            let target=host.Document.createElement "div" :?> HTMLElement
            host.Document.body.appendChild target |> ignore
            let inlineStyle=((target:>ElementCSSInlineStyle).style:>CSSStyleDeclaration)
            inlineStyle.setProperty(originalLeaf,"7px",priority="important")
            use animation=Keyframes.DeclarationOn(scheduler,target,Declaration.create property "0px",
                [|Keyframe.toValue(Declaration.create property "100px")|],duration=100u,
                options={KeyframeAnimationOptions.Default with Autoplay=false;Ease=id})
            animation.Playback.Seek 50.
            require ("native CSS alias dispatch "+property) (inlineStyle.getPropertyValue originalLeaf="50px")
            animation.Revert()
            require ("native CSS alias restores canonical original "+property)
                (inlineStyle.getPropertyValue originalLeaf="7px" && inlineStyle.getPropertyPriority originalLeaf="important")
            host.Document.body.removeChild target |> ignore
        for logicalFirst in [false;true] do
            let target=host.Document.createElement "div" :?> HTMLElement
            host.Document.body.appendChild target |> ignore
            let inlineStyle=((target:>ElementCSSInlineStyle).style:>CSSStyleDeclaration)
            if logicalFirst then inlineStyle.setProperty("margin-inline-start","11px")
            inlineStyle.setProperty("margin-left","7px")
            if not logicalFirst then inlineStyle.setProperty("margin-inline-start","11px")
            use animation=Keyframes.DeclarationOn(scheduler,target,Declaration.create "margin" "0px",
                [|Keyframe.toValue(Declaration.create "margin" "100px")|],duration=100u,
                options={KeyframeAnimationOptions.Default with Autoplay=false;Ease=id})
            animation.Playback.Seek 50.
            inlineStyle.setProperty("margin-inline-start","17px")
            inlineStyle.setProperty("height","23px")
            animation.Revert()
            let computed=(ComputedStyles.forHost host).Get((target:>Element),None)
            require ("native shorthand revert retains logical precedence "+logicalFirst.ToString())
                (computed.getPropertyValue "margin-left"=(if logicalFirst then "7px" else "17px") &&
                 inlineStyle.getPropertyValue "margin-inline-start"="17px" && inlineStyle.getPropertyValue "height"="23px")
            host.Document.body.removeChild target |> ignore
        do
            let target=host.Document.createElement "div" :?> HTMLElement
            host.Document.body.appendChild target |> ignore
            let inlineStyle=((target:>ElementCSSInlineStyle).style:>CSSStyleDeclaration)
            inlineStyle.setProperty("all","initial")
            inlineStyle.setProperty("width","7px")
            inlineStyle.setProperty("direction","rtl")
            inlineStyle.setProperty("--keep","1")
            let snapshot=CssPropertySnapshot.Capture(inlineStyle,"all",ignore)
            inlineStyle.setProperty("all","unset")
            inlineStyle.setProperty("width","23px")
            inlineStyle.setProperty("direction","ltr")
            inlineStyle.setProperty("--keep","2")
            snapshot.Restore(inlineStyle,(fun () -> true))
            let computed=(ComputedStyles.forHost host).Get((target:>Element),None)
            require "native all snapshots restore affected longhands and retain excluded properties"
                (inlineStyle.getPropertyValue "color"="initial" && inlineStyle.getPropertyValue "width"="7px" &&
                 computed.getPropertyValue "width"="7px" && inlineStyle.getPropertyValue "direction"="ltr" &&
                 inlineStyle.getPropertyValue "--keep"="2")
            host.Document.body.removeChild target |> ignore
        do
            let target=host.Document.createElement "div" :?> HTMLElement
            host.Document.body.appendChild target |> ignore
            let inlineStyle=((target:>ElementCSSInlineStyle).style:>CSSStyleDeclaration)
            inlineStyle.setProperty("--m","1px 2px")
            inlineStyle.setProperty("margin","var(--m)")
            require "native pending shorthand longhands serialize empty"
                (inlineStyle.getPropertyValue "margin-top"="")
            use animation=Keyframes.DeclarationOn(scheduler,target,Declaration.create "margin" "0px",
                [|Keyframe.toValue(Declaration.create "margin" "100px")|],duration=100u,
                options={KeyframeAnimationOptions.Default with Autoplay=false;Ease=id})
            animation.Playback.Seek 50.
            animation.Revert()
            let computed=(ComputedStyles.forHost host).Get((target:>Element),None)
            require "native shorthand revert retains original variable expression"
                (computed.getPropertyValue "margin-top"="1px" && computed.getPropertyValue "margin-right"="2px" &&
                 computed.getPropertyValue "margin-left"="2px" &&
                 inlineStyle.cssText.Contains("var(--m)"))
            inlineStyle.setProperty("--m","3px 4px")
            let updated=(ComputedStyles.forHost host).Get((target:>Element),None)
            require "restored shorthand variables remain reactive to native custom property changes"
                (updated.getPropertyValue "margin-top"="3px" && updated.getPropertyValue "margin-right"="4px" &&
                 updated.getPropertyValue "margin-left"="4px")
            host.Document.body.removeChild target |> ignore
        do
            let target=host.Document.createElement "div" :?> HTMLElement
            host.Document.body.appendChild target |> ignore
            let inlineStyle=((target:>ElementCSSInlineStyle).style:>CSSStyleDeclaration)
            inlineStyle.setProperty("margin-left","7px")
            inlineStyle.setProperty("--m","11px 13px")
            inlineStyle.setProperty("margin-inline","var(--m)")
            use animation=Keyframes.DeclarationOn(scheduler,target,Declaration.create "margin" "0px",
                [|Keyframe.toValue(Declaration.create "margin" "100px")|],duration=100u,
                options={KeyframeAnimationOptions.Default with Autoplay=false;Ease=id})
            animation.Playback.Seek 50.
            inlineStyle.setProperty("--n","17px 19px")
            inlineStyle.setProperty("margin-inline","var(--n)")
            animation.Revert()
            let computed=(ComputedStyles.forHost host).Get((target:>Element),None)
            require "native shorthand revert preserves current pending logical counterpart expression"
                (computed.getPropertyValue "margin-left"="17px" && computed.getPropertyValue "margin-right"="19px" &&
                 inlineStyle.cssText.Contains("var(--n)"))
            inlineStyle.setProperty("--n","23px 29px")
            let updated=(ComputedStyles.forHost host).Get((target:>Element),None)
            require "retained pending counterparts keep native custom property dependence"
                (updated.getPropertyValue "margin-left"="23px" && updated.getPropertyValue "margin-right"="29px")
            host.Document.body.removeChild target |> ignore
        for property in ["margin";"margin-top"] do
            let target=host.Document.createElement "div" :?> HTMLElement
            host.Document.body.appendChild target |> ignore
            let inlineStyle=((target:>ElementCSSInlineStyle).style:>CSSStyleDeclaration)
            inlineStyle.setProperty("--m","1px 2px")
            inlineStyle.setProperty("margin","var(--m)")
            if property="margin" then inlineStyle.setProperty("margin-left","7px",priority="important")
            let before=inlineStyle.cssText
            let mutable rejected=false
            try
                Keyframes.DeclarationOn(scheduler,target,Declaration.create property "0px",
                    [|Keyframe.toValue(Declaration.create property "100px")|]) |> ignore
            with :? NotSupportedException -> rejected<-true
            let computed=(ComputedStyles.forHost host).Get((target:>Element),None)
            require ("opaque pending shorthand snapshot gap rejects before native dispatch "+property)
                (rejected && inlineStyle.cssText=before && computed.getPropertyValue "margin-top"="1px" &&
                 computed.getPropertyValue "margin-left"=(if property="margin" then "7px" else "2px"))
            host.Document.body.removeChild target |> ignore
        do
            let rows=[|
                {FrameRow.Values=[|"width",ValueInterpolation.Text "10px"|]
                 Frame={FrameInput.Default with Duration=Some 100.;Delay=Some 40.}}
                {FrameRow.Values=[|"width",ValueInterpolation.Text "30px";"color",ValueInterpolation.Text "#ff0000"|]
                 Frame={FrameInput.Default with Duration=Some 100.}} |]
            let properties=KeyframeCompilation.rows rows |> Array.map(fun (name,frames) ->
                {FrameProperty.Target=0;Name=name;Original=ValueInterpolation.Text(if name="width" then "7px" else "#0000ff")
                 Frames=frames;ConvertUnit=None})
            let defaults={FrameDefaults.Default with Duration=200.;Offset=173.27;Ease=id}
            let before=style.getPropertyValue "width"
            let program=KeyframeCompilation.compile(defaults,properties,(fun () -> true)) |> Option.get
            require "keyframe compiler preserves sparse holds, raw endpoints and trimmed timing without dispatch"
                (program.Delay=40. && program.IterationDuration=200. && program.Tweens.Length=4 &&
                 program.Tweens[1].First.Number=10. && program.Tweens[1].Last.Number=30. &&
                 program.Tweens[2].CachedEnd.Text="rgba(0,0,255,1)" && style.getPropertyValue "width"=before)
            let offsetRows=[|
                {FrameOffset.Offset=0.;Values=[|"width",ValueInterpolation.Text "10px"|];Ease=Some(Ease.named "inQuad")}
                {FrameOffset.Offset=0.5;Values=[|"width",ValueInterpolation.Text "20px"|];Ease=Some(Ease.named "outQuad")}
                {FrameOffset.Offset=1.;Values=[|"width",ValueInterpolation.Text "30px"|];Ease=None} |]
            let offsetProperties=KeyframeCompilation.offsets(200.,offsetRows) |> Array.map(fun (name,frames) ->
                {FrameProperty.Target=0;Name=name;Original=ValueInterpolation.Text "7px";Frames=frames;ConvertUnit=None})
            let offsets=KeyframeCompilation.compile(defaults,offsetProperties,(fun () -> true)) |> Option.get
            let tween=offsets.Tweens[0]
            let output=ValueInterpolation.sample(true,tween.First,tween.Last,tween.Ease 0.5,4,tween.Modifier,(fun () -> true))
                       |> Option.get |> fun struct(_,output) -> output
            StyleDeclaration.apply style (Declaration.create "width" output.Text)
            require "compiled offset keyframes feed existing native interpolation and layout"
                (offsets.Tweens.Length=2 && offsets.Tweens[1].Ease(0.5)=0.75 && output.Text="12.5px" &&
                 abs(element.getBoundingClientRect().width-12.5)<0.01)
            if String.IsNullOrEmpty before then style.removeProperty "width" |> ignore else style.setProperty("width",before)
        do
            use graph=new Graph()
            use activation=graph.Activate()
            let owner=Api.createRoot id
            let mutable calls=0
            let modify value=calls<-calls+1;owner.Dispose();value
            let defaults={FrameDefaults.Default with Modifier=modify}
            let properties=[|{FrameProperty.Target=0;Name="transform";Original=ValueInterpolation.Text "translate(0px,0px)"
                              Frames=[|{FrameInput.Default with To=Some(ValueInterpolation.Text "translate(100px,200px)")}|];ConvertUnit=None}|]
            let result=KeyframeCompilation.compile(defaults,properties,(fun () -> not owner.IsDisposed))
            require "keyframe compilation owner disposal discards the program and later component effects"
                (result.IsNone && owner.IsDisposed && calls=1)
            owner.Dispose()
        do
            let names=["width";"height";"color";"transform"]
            let originals=names |> List.map(fun name -> name,style.getPropertyValue name,style.getPropertyPriority name)
            style.setProperty("width","7px",priority="important")
            use width=Keyframes.Declaration(scheduler,style,Css.width(Css.px 10.),
                [|(Keyframe.toValue(Css.width(Css.px 110.))).WithDuration 100u
                  (Keyframe.toValue(Declaration.important(Css.width(Css.px 210.)))).WithDuration(50u).WithDelay(100.)|],
                options={KeyframeAnimationOptions.Default with Autoplay=false;Ease=id})
            width.Playback.Seek 150.
            let gap=element.getBoundingClientRect().width
            width.Playback.Seek 225.
            let midpoint=element.getBoundingClientRect().width
            style.setProperty("height","23px")
            width.Revert()
            require "public typed CSS keyframes retain gaps priority and scoped native restoration"
                (abs(gap-110.)<0.02 && abs(midpoint-160.)<0.02 && style.getPropertyValue "width"="7px" &&
                 style.getPropertyPriority "width"="important" && style.getPropertyValue "height"="23px")
            style.setProperty("color","#ff0000")
            let red=style.getPropertyValue "color"
            let mutable colorInputs=0
            use color=Keyframes.CaptureInline(scheduler,style,"color",
                [|Keyframe.toValue(Declaration.create "color" "#00ff00")
                  Keyframe.toBy(fun context -> colorInputs<-colorInputs+1;context.PreviousValue.Value)|],duration=200u,
                options={KeyframeAnimationOptions.Default with Autoplay=false;Ease=id})
            color.Playback.Seek 50.
            let blended=style.getPropertyValue "color"
            color.Revert()
            require "public CSS keyframes retain legacy color interpolation and typed previous caches"
                (blended.Contains("180") && colorInputs=1 && style.getPropertyValue "color"=red)
            use transform=Keyframes.Declaration(scheduler,style,Declaration.create "transform" "translate(0px,0px)",
                [|Keyframe.toValue(Declaration.create "transform" "translate(100px,200px)")|],duration=100u,
                options={KeyframeAnimationOptions.Default with Autoplay=false;Ease=id})
            let initialGeometry=element.getBoundingClientRect()
            transform.Playback.Seek 50.
            let moved=element.getBoundingClientRect()
            require "public typed compound keyframes drive native transformed geometry"
                (abs(moved.left-initialGeometry.left-50.)<0.02 && abs(moved.top-initialGeometry.top-100.)<0.02)
            transform.Revert()
            for name,value,priority in originals do
                if String.IsNullOrEmpty value then style.removeProperty name |> ignore else style.setProperty(name,value,priority=priority)
        do
            let first=host.Document.createElement "div" :?> HTMLElement
            let second=host.Document.createElement "div" :?> HTMLElement
            first.setAttribute("class","anime-keyframe-first")
            second.setAttribute("class","anime-keyframe-second")
            let sheet=host.Document.createElement "style"
            (sheet:>Node).textContent<-".anime-keyframe-first { width:16px; } .anime-keyframe-second { width:32px; }"
            host.Document.head.appendChild sheet |> ignore
            host.Document.body.appendChild first |> ignore
            host.Document.body.appendChild second |> ignore
            let firstStyle=(first:>ElementCSSInlineStyle).style
            let secondStyle=(second:>ElementCSSInlineStyle).style
            firstStyle.setProperty("font-size","16px")
            secondStyle.setProperty("font-size","32px")
            secondStyle.setProperty("width","8px",priority="important")
            let originalChildren=host.Document.body.childNodes.length
            let service=ComputedStyles.forHost host
            let mutable reads=0
            let computed={new ComputedStyleService with member _.Get(target,pseudo)=reads<-reads+1;service.Get(target,pseudo)}
            let previous=ResizeArray<string>()
            let frames=[|
                Keyframe.toBy(fun (context:KeyframeContext<HTMLElement,Declaration>) -> Css.width(Css.em(float context.Index+2.)))
                Keyframe.toBy(fun context -> previous.Add(Declaration.value context.PreviousValue.Value);Css.width(Css.em(float context.Index+3.))) |]
            use animation=Keyframes.CaptureCss(scheduler,computed,[|first;second|],"width",frames,duration=200u,
                                               options={KeyframeAnimationOptions.Default with Autoplay=false;Ease=id})
            animation.Playback.Seek 50.
            let firstMid,secondMid=first.getBoundingClientRect().width,second.getBoundingClientRect().width
            animation.Playback.Seek 150.
            require "public CSS keyframes use actual computed services and independent element unit contexts"
                (reads=2 && previous.ToArray()=[|"2em";"3em"|] && abs(firstMid-24.)<0.02 && abs(secondMid-52.)<0.02 &&
                 abs(first.getBoundingClientRect().width-40.)<0.02 && abs(second.getBoundingClientRect().width-112.)<0.02 &&
                 host.Document.body.childNodes.length=originalChildren)
            firstStyle.setProperty("height","33px")
            animation.Revert()
            require "computed keyframe revert restores stylesheet fallback and each original inline priority"
                (reads=2 && firstStyle.getPropertyValue "width"="" && abs(first.getBoundingClientRect().width-16.)<0.02 &&
                 secondStyle.getPropertyValue "width"="8px" && secondStyle.getPropertyPriority "width"="important" && firstStyle.getPropertyValue "height"="33px")
            host.Document.body.removeChild first |> ignore
            host.Document.body.removeChild second |> ignore
            host.Document.head.removeChild sheet |> ignore
        do
            use graph=new Graph()
            use activation=graph.Activate()
            let owner=Api.createRoot id
            let mutable calls=0
            let computed=
                { new ComputedStyleService with
                    member _.Get(_,pseudo)=
                        if pseudo.IsSome then failwith "unexpected computed pseudo-element"
                        calls<-calls+1
                        owner.Dispose()
                        Unchecked.defaultof<CSSStyleDeclaration> }
            let before=style.getPropertyValue "width"
            let mutable interrupted=false
            try Keyframes.CaptureCss(scheduler,computed,element,"width",[|Keyframe.toValue(Css.width(Css.px 100.))|],owner=owner) |> ignore
            with :? ObjectDisposedException -> interrupted<-true
            require "computed keyframe ownership is checked after Get before reading its returned style"
                (interrupted && calls=1 && owner.IsDisposed && style.getPropertyValue "width"=before)
        do
            use graph=new Graph()
            use activation=graph.Activate()
            let owner=Api.createRoot id
            let mutable armed=false
            let mutable calls=0
            let modify value=if armed then calls<-calls+1;owner.Dispose();value else value
            let original=style.getPropertyValue "transform"
            use animation=Keyframes.DeclarationOn(scheduler,element,Declaration.create "transform" "translate(0px,0px)",
                [|(Keyframe.toValue(Declaration.create "transform" "translate(100px,200px)")).WithModifier modify|],duration=100u,owner=owner,
                options={KeyframeAnimationOptions.Default with Ease=id})
            let before=style.getPropertyValue "transform"
            armed<-true
            pumpUntil host (fun () -> animation.Cancelled)
            require "scheduled public CSS keyframes stop partial component effects on actual owner disposal"
                (calls=1 && owner.IsDisposed && style.getPropertyValue "transform"=before)
            if String.IsNullOrEmpty original then style.removeProperty "transform" |> ignore else style.setProperty("transform",original)
        do
            let before=style.getPropertyValue "width"
            let priority=style.getPropertyPriority "width"
            style.setProperty("width","7px",priority="important")
            let property=NumericProperty(
                (fun () -> Double.Parse(style.getPropertyValue("width").Replace("px",""),CultureInfo.InvariantCulture)),
                (fun value -> StyleDeclaration.apply style (Declaration.important(Css.width(Css.px value)))))
            let mutable evaluations=0
            let frames=[|
                (Keyframe.toBy(fun (context:KeyframeContext<NumericProperty,float>) -> evaluations<-evaluations+1;context.Target.Value+100.)).WithDuration 200u
                (Keyframe.toBy(fun context -> evaluations<-evaluations+1;context.PreviousValue.Value+100.)).WithDuration 100u |]
            use animation=Keyframes.Numeric(scheduler,property,frames,duration=300u,
                                            options={KeyframeAnimationOptions.Default with Autoplay=false;Ease=id})
            animation.Playback.Seek 100.
            let first=element.getBoundingClientRect().width
            animation.Playback.Seek 250.
            let second=element.getBoundingClientRect().width
            animation.Playback.Stretch 600u
            animation.Playback.Seek 500.
            style.setProperty("--keyframe-unrelated","42")
            require "public numeric keyframes evaluate typed inputs once and drive native layout through shared stretch"
                (evaluations=2 && abs(first-57.)<0.01 && abs(second-157.)<0.01 && abs(element.getBoundingClientRect().width-157.)<0.01)
            animation.Revert()
            require "public keyframe revert restores captured numeric properties and preserves unrelated native style"
                (style.getPropertyValue "width"="7px" && style.getPropertyPriority "width"="important" &&
                 style.getPropertyValue "--keyframe-unrelated"="42" && evaluations=2 && animation.Cancelled)
            style.removeProperty "--keyframe-unrelated" |> ignore
            if String.IsNullOrEmpty before then style.removeProperty "width" |> ignore else style.setProperty("width",before,priority=priority)
        do
            use graph=new Graph()
            use activation=graph.Activate()
            let owner=Api.createRoot id
            let mutable calls=0
            let property=NumericProperty((fun () -> 10.),(fun _ -> failwith "interrupted public keyframe dispatch"))
            let frame=(Keyframe.toBy(fun (_:KeyframeContext<NumericProperty,float>) -> calls<-calls+1;owner.Dispose();100.))
                         .WithFromBy(fun _ -> failwith "source callback after disposal")
                         .WithDurationBy(fun _ -> failwith "duration callback after disposal")
            let mutable interrupted=false
            try Keyframes.Numeric(scheduler,property,[|frame|],owner=owner) |> ignore with :? ObjectDisposedException -> interrupted<-true
            require "public native keyframe ownership precedes input callbacks and suppresses later construction effects"
                (interrupted && calls=1 && owner.IsDisposed)
        do
            let beforeWidth=style.getPropertyValue "width"
            let beforeHeight=style.getPropertyValue "height"
            let properties=[|
                {FrameProperty.Target=0;Name="width";Original=ValueInterpolation.Text "10px";ConvertUnit=None
                 Frames=[|{FrameInput.Default with To=Some(ValueInterpolation.Text "110px");Duration=Some 100.}
                          {FrameInput.Default with To=Some(ValueInterpolation.Text "210px");Duration=Some 50.;Delay=Some 100.}|]}
                {FrameProperty.Target=0;Name="height";Original=ValueInterpolation.Text "0px";ConvertUnit=None
                 Frames=[|{FrameInput.Default with To=Some(ValueInterpolation.Text "400px");Duration=Some 400.}|]} |]
            let program=KeyframeCompilation.compileReplacing({FrameDefaults.Default with Duration=400.;Ease=id},properties,(fun () -> true)) |> Option.get
            use driver=new KeyframePlayback(scheduler,program,(fun _ name output -> StyleDeclaration.apply style (Declaration.create name output.Text)),
                                           true,true,4,PlaybackOptions.Default)
            driver.Initialize()
            driver.Clock.Seek 300.
            let tail=element.getBoundingClientRect().width
            driver.Clock.Seek 150.
            let gap=element.getBoundingClientRect().width
            driver.Clock.Seek 225.
            let middle=element.getBoundingClientRect().width
            driver.Clock.Seek 0.
            require "shared native keyframe playback restores short tails and backward gaps"
                (abs(tail-210.)<0.01 && abs(gap-110.)<0.01 && abs(middle-160.)<0.01 && abs(element.getBoundingClientRect().width-10.)<0.01)
            driver.Clock.Stretch 800u
            driver.Clock.Seek 450.
            require "native keyframe stretching scales per-frame geometry through the shared timer"
                (abs(element.getBoundingClientRect().width-160.)<0.01 && driver.Clock.Duration=800.)
            driver.Clock.Reset()
            driver.Clock.Resume()
            pumpUntil host (fun () -> driver.Clock.CurrentTime>=50.)
            require "compiled keyframes consume native scheduled frames"
                (element.getBoundingClientRect().width>10. && element.getBoundingClientRect().width<110.)
            driver.Clock.Cancel()
            for name,value in ["width",beforeWidth;"height",beforeHeight] do
                if String.IsNullOrEmpty value then style.removeProperty name |> ignore else style.setProperty(name,value)
        do
            use graph=new Graph()
            use activation=graph.Activate()
            let owner=Api.createRoot id
            let mutable armed=false
            let mutable calls=0
            let modifier value=if armed then calls<-calls+1;owner.Dispose();value else value
            let properties=[|{FrameProperty.Target=0;Name="transform";Original=ValueInterpolation.Text "translate(0px,0px)";ConvertUnit=None
                              Frames=[|{FrameInput.Default with To=Some(ValueInterpolation.Text "translate(100px,200px)");Modifier=Some modifier}|]}|]
            let program=KeyframeCompilation.compileReplacing({FrameDefaults.Default with Duration=100.;Ease=id},properties,(fun () -> true)) |> Option.get
            use driver=new KeyframePlayback(scheduler,program,(fun _ name output -> StyleDeclaration.apply style (Declaration.create name output.Text)),
                                           true,true,4,PlaybackOptions.Default)
            owner.Attach driver.Clock
            driver.Initialize()
            let before=style.getPropertyValue "transform"
            armed<-true
            driver.Clock.Resume()
            pumpUntil host (fun () -> driver.Clock.Cancelled)
            require "native keyframe owner disposal suppresses partial components and later dispatch"
                (calls=1 && style.getPropertyValue "transform"=before)
            owner.Dispose()
            style.removeProperty "transform" |> ignore
        do
            style.setProperty("width","7px",priority="important")
            use group=new Animatable(scheduler)
            let settings={AnimatablePropertyOptions.Default with Ease=id}
            let width=group.Declaration(style,Css.width(Css.px 10.),duration=100u,options=settings)
            width.To(Css.width(Css.px 110.))
            width.Playback.Seek 50.
            require "public CSS animatable uses typed declarations, cached units and native layout"
                (Declaration.value width.Value="60px" && width.Magnitude=60. && style.getPropertyPriority "width"="important" &&
                 abs(element.getBoundingClientRect().width-60.)<0.01)
            let opacity=group.Declaration(style,Css.opacity(Css.number 0.),duration=100u,options=settings)
            opacity.To(Css.opacity(Css.number 1.))
            opacity.Playback.Seek 50.
            let originalGeometry=element.getBoundingClientRect()
            let transform=group.Declaration(style,Declaration.create "transform" "translate(10px,20px)",duration=100u,options=settings)
            transform.ToComponents [|110.;220.|]
            transform.Playback.Seek 50.
            let geometry=element.getBoundingClientRect()
            require "public CSS animatable component handles drive native transforms"
                (Declaration.value transform.Value="translate(60px,120px)" &&
                 abs(geometry.left-originalGeometry.left-60.)<0.02 && abs(geometry.top-originalGeometry.top-120.)<0.02)
            let color=group.Declaration(style,Declaration.create "color" "#ff0000",duration=100u,options=settings)
            color.To(Declaration.create "color" "#00ff00")
            color.Playback.Seek 50.
            require "public CSS animatable typed colors retain source cached channel interpolation"
                (Declaration.value color.Value="rgba(180,180,0,1)" && color.Components.Length=4)
            style.setProperty("height","23px")
            group.Revert()
            require "public CSS animatable restores inline value, importance and absence without replacing unrelated style"
                (style.getPropertyValue "width"="7px" && style.getPropertyPriority "width"="important" &&
                 style.getPropertyValue "opacity"="" && style.getPropertyValue "transform"="" && style.getPropertyValue "color"="" &&
                 style.getPropertyValue "height"="23px" && width.Playback.Cancelled && transform.Playback.Cancelled)
            style.removeProperty "width" |> ignore
            style.removeProperty "height" |> ignore
        do
            let first=host.Document.createElement "div" :?> HTMLElement
            let second=host.Document.createElement "div" :?> HTMLElement
            first.setAttribute("class","anime-computed-first")
            second.setAttribute("class","anime-computed-second")
            let sheet=host.Document.createElement "style"
            (sheet:>Node).textContent<-".anime-computed-first { width:16px; } .anime-computed-second { width:32px; }"
            host.Document.head.appendChild sheet |> ignore
            host.Document.body.appendChild first |> ignore
            host.Document.body.appendChild second |> ignore
            let firstStyle=(first:>ElementCSSInlineStyle).style
            let secondStyle=(second:>ElementCSSInlineStyle).style
            firstStyle.setProperty("font-size","16px")
            secondStyle.setProperty("font-size","32px")
            secondStyle.setProperty("width","8px",priority="important")
            let computed=ComputedStyles.forHost host
            use group=new Animatable(scheduler)
            let handle=group.CaptureCss(computed,[|first;second|],"width",duration=100u,
                                        options={AnimatablePropertyOptions.Default with Ease=id})
            handle.To(Css.width(Css.em 2.))
            handle.Playback.Seek 50.
            require "public CSS animatable captures actual computed styles and converts retarget units per element"
                (abs(first.getBoundingClientRect().width-24.)<0.02 && abs(second.getBoundingClientRect().width-36.)<0.02 &&
                 Declaration.value handle.Value="24px" && secondStyle.getPropertyPriority "width"="important")
            firstStyle.setProperty("height","33px")
            group.Revert()
            require "computed CSS animatable revert restores stylesheet fallback and each original inline priority"
                (firstStyle.getPropertyValue "width"="" && abs(first.getBoundingClientRect().width-16.)<0.02 &&
                 secondStyle.getPropertyValue "width"="8px" && secondStyle.getPropertyPriority "width"="important" &&
                 firstStyle.getPropertyValue "height"="33px")
            host.Document.body.removeChild first |> ignore
            host.Document.body.removeChild second |> ignore
            host.Document.head.removeChild sheet |> ignore
        do
            use graph=new Graph()
            use activation=graph.Activate()
            let mutable armed=false
            let owner,group,handle=Api.createRoot(fun owner ->
                let group=new Animatable(scheduler) |> Animatable.own
                let modify value =
                    if armed then owner.Dispose()
                    value
                let settings={AnimatablePropertyOptions.Default with Ease=id;Modifier=modify}
                let handle=group.Declaration(style,Declaration.create "transform" "translate(0px,0px)",duration=100u,options=settings)
                owner,group,handle)
            use group=group
            handle.ToComponents [|100.;200.|]
            let initial=style.getPropertyValue "transform"
            armed<-true
            handle.Playback.Seek 50.
            require "public CSS animatable owner disposal inside a component modifier prevents dispatch"
                (owner.IsDisposed && group.Cancelled && handle.Playback.Cancelled && style.getPropertyValue "transform"=initial)
            owner.Dispose()
            style.removeProperty "transform" |> ignore
        do
            let mutable current=42.
            let property=NumericProperty((fun () -> current),(fun next ->
                current<-next
                StyleDeclaration.apply style (Declaration.create "width" (next.ToString("R",CultureInfo.InvariantCulture)+"px") |> Declaration.important)))
            use animatable=new Animatable(scheduler)
            let handle=animatable.Numeric(property,duration=100u,options={AnimatablePropertyOptions.Default with Ease=id})
            handle.To(100.,duration=200u)
            handle.Playback.Seek 100.
            require "public numeric animatable drives typed native width and cached getter"
                (handle.Value=71. && style.getPropertyValue "width"="71px" && abs(element.getBoundingClientRect().width-71.)<0.01)
            style.setProperty("height","17px")
            animatable.Revert()
            require "public numeric animatable revert restores its captured property and preserves unrelated style"
                (style.getPropertyValue "width"="42px" && style.getPropertyPriority "width"="important" &&
                 style.getPropertyValue "height"="17px" && animatable.Cancelled && handle.Playback.Cancelled && animatable.Count=0 &&
                 animatable.Callbacks.CurrentTime=0. && not animatable.Callbacks.Completed)
            style.removeProperty "width" |> ignore
            style.removeProperty "height" |> ignore
        do
            use graph=new Graph()
            use activation=graph.Activate()
            let mutable writes=0
            let owner,animatable,first,second=Api.createRoot(fun owner ->
                let animatable=new Animatable(scheduler,onBegin=(fun _ -> owner.Dispose())) |> Animatable.own
                let add ()=animatable.Numeric(NumericProperty((fun () -> 0.),(fun _ -> writes<-writes+1)),duration=100u)
                owner,animatable,add(),add())
            use animatable=animatable
            first.To 100.
            second.To 100.
            pumpUntil host (fun () -> animatable.Cancelled)
            let stopped=writes
            host.Frame()
            host.Frame()
            require "public numeric animatable owner disposal stops every shared driver"
                (owner.IsDisposed && first.Playback.Cancelled && second.Playback.Cancelled && writes=stopped)
            owner.Dispose()
        let cache = AnimatableValue(ValueInterpolation.Number 10., None, false, None)
        let live () = true
        let double value = value*2.
        cache.Sample(0.,4,double,live) |> ignore
        require "animatable kernel getter applies its modifier to the cache" (cache.ScalarValue(double,live)=Some 40.)
        require "animatable kernel retarget remains current" (cache.RetargetScalar(100.,double,live))
        cache.Sample(0.,4,double,live) |> ignore
        let scalarOutput=cache.Sample(0.37,4,double,live) |> Option.get
        require "animatable kernel preserves repeated scalar modifier behavior"
            (scalarOutput.Scalar |> Option.exists(fun value -> abs(value-124.4)<1e-10))
        let scalarSnapshot=cache.Snapshot(double,live) |> Option.get
        require "animatable cached scalar snapshot applies getter modifier without changing its cache"
            (scalarSnapshot.Scalar |> Option.exists(fun value -> abs(value-248.8)<1e-10) && abs(cache.CachedScalar-124.4)<1e-10)
        let signedColor=AnimatableValue(ValueInterpolation.Text "rgba(10,20,30,0.5)",None,true,None)
        signedColor.Sample(0.,4,(fun value -> -value),live) |> ignore
        let signedSnapshot=signedColor.Snapshot((fun _ -> failwith "component getter repeated modifier"),live) |> Option.get
        signedSnapshot.Components[0]<-999.
        require "animatable cached color snapshot preserves signed modifier results and independent components"
            (signedSnapshot.Text="rgba(-10,-20,-30,-0.5)" && signedColor.ComponentValues[0]= -10.)
        let compound=AnimatableValue(ValueInterpolation.Text "translate(10px,20px)",None,false,None)
        compound.Sample(0.,4,id,live) |> ignore
        compound.RetargetComponents([|110.;220.|],id,live) |> ignore
        compound.Sample(0.37,4,id,live) |> ignore
        compound.RetargetComponents([|210.|],id,live) |> ignore
        compound.Sample(0.37,4,id,live) |> ignore
        let compoundOutput=compound.Snapshot((fun _ -> failwith "compound getter repeated modifier"),live) |> Option.get
        let untransformed=element.getBoundingClientRect()
        StyleDeclaration.apply style (Declaration.create "transform" compoundOutput.Text)
        let transformed=element.getBoundingClientRect()
        require "animatable kernel partial components drive native geometry"
            (compoundOutput.Text="translate(107.31px,94px)" &&
             abs(transformed.left-untransformed.left-107.31)<0.02 && abs(transformed.top-untransformed.top-94.)<0.02)
        let components=compound.ComponentValues
        components[0]<-999.
        require "animatable kernel component snapshots preserve its cache" (compound.ComponentValues[0]=107.31)
        style.removeProperty "transform" |> ignore
        let retargetWrites=ref 0
        use retargetTrack=new AnimatableTrack(scheduler,[|AnimatableValue(ValueInterpolation.Text "0px",None,true,None)|],
            (fun _ output ->
                retargetWrites.Value<-retargetWrites.Value+1
                StyleDeclaration.apply style (Declaration.create "width" output.Text |> Declaration.important)),
            100.,PlaybackOptions.Default,id,id,4)
        retargetTrack.Initialize()
        let beforeRetarget=retargetWrites.Value
        retargetTrack.RetargetScalar 100.
        require "native animatable retarget updates cache without a target write"
            (retargetWrites.Value=beforeRetarget && style.getPropertyValue "width"="0px")
        pumpUntil host (fun () -> retargetTrack.Clock.Completed)
        retargetTrack.RetargetScalar(200.,newDuration=200.,newEase=Ease.named "inQuad")
        retargetTrack.Clock.Seek 100.
        require "native animatable retarget retains typed units and uses changed easing and timing"
            (style.getPropertyValue "width"="125px" && style.getPropertyPriority "width"="important" &&
             abs(element.getBoundingClientRect().width-125.)<0.01)
        require "native animatable track exposes its composed cached unit snapshot"
            ((retargetTrack.Snapshot(0) |> Option.get).Text="125px")
        retargetTrack.Cancel()
        do
            use retargetGraph=new Graph()
            use retargetActivation=retargetGraph.Activate()
            let mutable armed=false
            let mutable modifierCalls=0
            let mutable ownedWrites=0
            let owner,owned=Api.createRoot(fun owner ->
                let modifier value =
                    if armed then
                        modifierCalls<-modifierCalls+1
                        if modifierCalls=2 then owner.Dispose()
                    value
                let track=new AnimatableTrack(scheduler,[|AnimatableValue(ValueInterpolation.Number 10.,None,false,None)|],
                    (fun _ _ -> ownedWrites<-ownedWrites+1),100.,PlaybackOptions.Default,id,modifier,4)
                Timer.own track.Clock |> ignore
                track.Initialize()
                owner,track)
            use owned=owned
            armed<-true
            owned.RetargetScalar 100.
            require "native animatable owner disposal inside soft reset prevents dispatch"
                (owned.Clock.Cancelled && modifierCalls=2 && ownedWrites=1 && not owned.Clock.InternalRender)
            owner.Dispose()
        do
            let mutable groupBegins=0
            let mutable groupCompletions=0
            use group=new AnimatableGroup(scheduler,onBegin=(fun _ -> groupBegins<-groupBegins+1),
                                          onComplete=(fun _ -> groupCompletions<-groupCompletions+1))
            let width=group.Add([|AnimatableValue(ValueInterpolation.Number 0.,None,false,None)|],
                (fun _ output -> StyleDeclaration.apply style (Ranvier.Css.Css.width(Ranvier.Css.Css.px(Option.get output.Scalar)) |> Declaration.important)),
                100.,PlaybackOptions.Default,id,id,4)
            let height=group.Add([|AnimatableValue(ValueInterpolation.Number 0.,None,false,None)|],
                (fun _ output -> StyleDeclaration.apply style (Ranvier.Css.Css.height(Ranvier.Css.Css.px(Option.get output.Scalar)))),
                200.,PlaybackOptions.Default,id,id,4)
            width.RetargetScalar 100.
            height.RetargetScalar 200.
            pumpUntil host (fun () -> group.Callbacks.Completed)
            require "native animatable group callbacks finish after independent properties pause"
                (groupBegins=1 && groupCompletions=1 && width.Clock.Completed && height.Clock.Completed &&
                 group.Callbacks.CurrentTime=1000. && abs(element.getBoundingClientRect().width-100.)<0.01 &&
                 abs(element.getBoundingClientRect().height-200.)<0.01 && style.getPropertyPriority "width"="important")
        do
            use groupGraph=new Graph()
            use groupActivation=groupGraph.Activate()
            let mutable groupBegins=0
            let mutable groupUpdates=0
            let mutable groupWrites=0
            let owner,group,first,second=Api.createRoot(fun owner ->
                let group=new AnimatableGroup(scheduler,onBegin=(fun _ -> groupBegins<-groupBegins+1;owner.Dispose()),
                                              onUpdate=(fun _ -> groupUpdates<-groupUpdates+1))
                Api.getOwner().Attach(group:>IDisposable)
                let add ()=group.Add([|AnimatableValue(ValueInterpolation.Number 0.,None,false,None)|],
                                    (fun _ _ -> groupWrites<-groupWrites+1),1000.,PlaybackOptions.Default,id,id,4)
                owner,group,add(),add())
            use group=group
            first.RetargetScalar 100.
            second.RetargetScalar 100.
            pumpUntil host (fun () -> group.Cancelled)
            let stopped=groupWrites
            host.Cycle(milliseconds=10)
            host.Frame()
            require "native animatable group owner disposal interrupts callbacks and all property roots"
                (groupBegins=1 && groupUpdates=0 && groupWrites=stopped && first.Clock.Cancelled && second.Clock.Cancelled)
            owner.Dispose()
        let superseded=AnimatableValue(ValueInterpolation.Number 10.,None,false,None)
        superseded.RetargetScalar(100.,id,live) |> ignore
        let retargetAgain value = superseded.RetargetScalar(300.,id,live) |> ignore; value
        require "animatable kernel nested retarget interrupts stale sampling"
            ((superseded.Sample(0.5,4,retargetAgain,live)).IsNone &&
             (superseded.Sample(1.,4,id,live) |> Option.get).Scalar=Some 300.)
        style.setProperty("--anime-gap", "0px", priority = "important")
        style.setProperty("width", "var(--anime-gap)")
        style.setProperty("height", "20px")
        style.setProperty("background-color", "red")
        use tween = CssAnimation.customProperty scheduler style (RuntimeApi.createCustomProperty "--anime-gap") (Ranvier.Css.Css.px 0.) (Ranvier.Css.Css.px 100.) 100.
        tween.Pause()
        tween.Seek 25.
        require "typed CSS seek writes native style" (style.getPropertyValue "--anime-gap" = "25px")
        require "typed CSS retains priority" (style.getPropertyPriority "--anime-gap" = "important")
        tween.Play()
        pumpUntil host (fun () -> style.getPropertyValue "--anime-gap" = "100px")
        require "native layout consumes animated custom property" (abs(element.getBoundingClientRect().width - 100.) < 0.01)
        tween.Stretch 200u
        require "native numeric stretch retains its last property value" (style.getPropertyValue "--anime-gap"="100px")
        tween.Seek 100.
        require "native numeric stretch samples the new iteration duration" (style.getPropertyValue "--anime-gap"="50px")
        tween.Seek 200.
        style.setProperty("--anime-repeat-gap", "7px", priority = "important")
        use repeatedCss =
            CssAnimation.customPropertyWithOptions
                { PlaybackOptions.Default with Repeat = PlaybackRepeat.Count 1u; Alternate = true }
                scheduler style (RuntimeApi.createCustomProperty "--anime-repeat-gap")
                (Ranvier.Css.Css.px 0.) (Ranvier.Css.Css.px 100.) 25.
        pumpUntil host (fun () -> repeatedCss.Completed)
        require "native typed CSS repeats and alternates through the shared clock"
            (repeatedCss.CurrentIteration = 1 && style.getPropertyValue "--anime-repeat-gap" = "0px" &&
             style.getPropertyPriority "--anime-repeat-gap" = "important")
        style.setProperty("--anime-timeline-gap", "0px", priority = "important")
        style.setProperty("height", "var(--anime-timeline-gap)")
        let mutable timelineValue = 0.
        let mutable timelineLoops = 0
        let timelineProperty = NumericProperty((fun () -> timelineValue), (fun value ->
            timelineValue <- value
            style.setProperty("--anime-timeline-gap", value.ToString("G17", CultureInfo.InvariantCulture) + "px", priority = "important")))
        use repeatedTimeline = new Timeline(scheduler,
            options = { PlaybackOptions.Default with Repeat = PlaybackRepeat.Count 1u; Alternate = true },
            onLoop = (fun _ -> timelineLoops <- timelineLoops + 1))
        repeatedTimeline.Animate(timelineProperty, 100., 25u) |> ignore
        let timelineTimer = repeatedTimeline.AddTimer(25u, TimelinePosition.At 0.)
        repeatedTimeline.Reset()
        repeatedTimeline.Resume()
        pumpUntil host (fun () -> repeatedTimeline.Completed)
        require "native timeline repeats and alternates through the shared clock"
            (timelineValue = 0. && timelineLoops = 1 && repeatedTimeline.CurrentIteration = 1 && not repeatedTimeline.Playing &&
             style.getPropertyValue "--anime-timeline-gap" = "0px" && style.getPropertyPriority "--anime-timeline-gap" = "important")
        require "native layout consumes timeline output" (abs(element.getBoundingClientRect().height) < 0.01)
        require "native timeline samples an owned timer track"
            (timelineTimer.CurrentTime = 0. && timelineTimer.Duration = 25. && not timelineTimer.Cancelled)
        use borrowedTimer = new Timer(scheduler, 25u,
            options = { PlaybackOptions.Default with Repeat = PlaybackRepeat.Count 1u })
        use borrowedTimeline = new Timeline(scheduler,
            options = { PlaybackOptions.Default with Repeat = PlaybackRepeat.Count 1u; Alternate = true })
        borrowedTimeline.Sync(borrowedTimer, TimelinePosition.At 0.) |> ignore
        borrowedTimeline.Reset()
        borrowedTimeline.Resume()
        pumpUntil host (fun () -> borrowedTimeline.Completed)
        borrowedTimeline.Cancel()
        require "native timeline borrows repeated timer playback without disposing it"
            (borrowedTimer.CurrentTime = 0. && not borrowedTimer.Cancelled)
        use graph = new Graph()
        let mutable curveValue = 0.
        let writeCurve value =
            curveValue <- value
            style.setProperty("--anime-gap", value.ToString("G17", CultureInfo.InvariantCulture) + "px", priority = "important")
        let piecewise = Ease.linear [|LinearStop.Value 0.; LinearStop.At(1.5, 0.5); LinearStop.Value 1.|]
        use curveTween = new NumericAnimation(scheduler, (fun () -> curveValue), writeCurve, 100., 100., ease = piecewise)
        curveTween.Seek 50.
        require "native piecewise easing retains overshoot and layout"
            (style.getPropertyValue "--anime-gap" = "150px" && abs(element.getBoundingClientRect().width - 150.) < 0.01)
        curveTween.Resume()
        pumpUntil host (fun () -> curveTween.Completed)
        require "native piecewise easing finishes at its final stop" (curveValue = 100.)
        let mutable randomCalls = 0
        let irregular = Ease.irregularWith (fun () -> randomCalls <- randomCalls + 1; 0.5) 3u 1.
        use irregularTween = new NumericAnimation(scheduler, (fun () -> 0.), writeCurve, 100., 100., ease = irregular)
        irregularTween.Seek 50.
        require "native irregular easing retains construction draws"
            (curveValue = 75. && style.getPropertyValue "--anime-gap" = "75px" && randomCalls = 1)
        use activation = graph.Activate()
        use declarationWidth = CssAnimation.declaration scheduler style
                                    (Ranvier.Css.Css.width(Ranvier.Css.Css.px 0.))
                                    (Ranvier.Css.Css.width(Ranvier.Css.Css.px 120.)) 100u
        declarationWidth.Seek 50.
        require "native typed declaration drives layout"
            (abs(element.getBoundingClientRect().width - 60.) < 0.01)
        declarationWidth.Stretch 200u
        require "native declaration stretch retains style without immediate sampling"
            (style.getPropertyValue "width"="60px" && declarationWidth.CurrentTime=50. && declarationWidth.Progress=0.25)
        declarationWidth.Seek 100.
        require "native declaration stretch uses mutable geometry"
            (abs(element.getBoundingClientRect().width-60.)<0.01)
        declarationWidth.Stretch 0.125
        declarationWidth.Seek 0.0625
        require "native declaration stretch preserves precise fractional milliseconds"
            (abs(element.getBoundingClientRect().width-60.)<0.01)
        declarationWidth.Cancel()
        use declarationColor = CssAnimation.declaration scheduler style
                                    (Declaration.create "background-color" "#000")
                                    (Declaration.create "background-color" "#fff") 100u
        declarationColor.Seek 50.
        require "native color declaration uses source gamma-like interpolation"
            (style.getPropertyValue "background-color" = "rgb(180, 180, 180)")
        declarationColor.Cancel()
        let beforeTransform = element.getBoundingClientRect().left
        use declarationTransform = CssAnimation.declaration scheduler style
                                        (Declaration.create "transform" "translate(0px,0px)")
                                        (Declaration.create "transform" "translate(100px,50px)") 100u
        declarationTransform.Seek 50.
        require "native compound declaration changes transformed geometry"
            (abs(element.getBoundingClientRect().left - beforeTransform - 50.) < 0.01)
        declarationTransform.Cancel()
        style.removeProperty "transform" |> ignore
        let mutable modifierArmed = false
        let mutable modifierCalls = 0
        let mutable declarationUpdates = 0
        let declarationOwner, ownedDeclaration = Api.createRoot(fun owner ->
            let modify value =
                if modifierArmed then
                    modifierCalls <- modifierCalls + 1
                    owner.Dispose()
                value
            let settings =
                { DeclarationAnimationOptions.Default with
                    Modifier = modify
                    OnUpdate = (fun _ -> declarationUpdates <- declarationUpdates + 1) }
            let clock = CssAnimation.declarationWith settings scheduler style
                            (Declaration.create "transform" "translate(0px,0px)")
                            (Declaration.create "transform" "translate(100px,50px)") 1000u |> Timer.own
            owner, clock)
        use ownedDeclaration = ownedDeclaration
        let retainedTransform = style.getPropertyValue "transform"
        modifierArmed <- true
        pumpUntil host (fun () -> ownedDeclaration.Cancelled)
        require "native declaration owner cancellation interrupts component modifiers"
            (modifierCalls = 1 && declarationUpdates = 0 && style.getPropertyValue "transform" = retainedTransform)
        declarationOwner.Dispose()
        let unitParent=host.Document.createElement "div":?>HTMLElement
        host.Document.body.appendChild unitParent |> ignore
        let unitParentStyle=(unitParent:>ElementCSSInlineStyle).style
        unitParentStyle.setProperty("width","400px")
        let unitFirst=host.Document.createElement "div":?>HTMLElement
        let unitSecond=host.Document.createElement "div":?>HTMLElement
        unitParent.appendChild unitFirst |> ignore
        unitParent.appendChild unitSecond |> ignore
        let unitFirstStyle=(unitFirst:>ElementCSSInlineStyle).style
        let unitSecondStyle=(unitSecond:>ElementCSSInlineStyle).style
        unitFirstStyle.setProperty("font-size","16px")
        unitSecondStyle.setProperty("font-size","32px")
        unitFirstStyle.setProperty("width","7px",priority="important")
        use emFirst=CssAnimation.declarationOn scheduler unitFirst
                        (Ranvier.Css.Css.width(Ranvier.Css.Css.em 1.)) (Ranvier.Css.Css.width(Ranvier.Css.Css.px 100.)) 100u
        require "native unit context resolves em and retains priority"
            (unitFirstStyle.getPropertyValue "width"="16px" && unitFirstStyle.getPropertyPriority "width"="important")
        emFirst.Seek 50.
        require "native contextual em interpolation drives layout" (abs(unitFirst.getBoundingClientRect().width-58.)<0.02)
        emFirst.Cancel()
        use emSecond=CssAnimation.declarationOn scheduler unitSecond
                        (Ranvier.Css.Css.width(Ranvier.Css.Css.em 1.)) (Ranvier.Css.Css.width(Ranvier.Css.Css.px 100.)) 100u
        require "native unit contexts do not share stale font ratios" (unitSecondStyle.getPropertyValue "width"="32px")
        emSecond.Cancel()
        use percentWidth=CssAnimation.declarationOn scheduler unitFirst
                            (Ranvier.Css.Css.width(Ranvier.Css.Css.px 40.)) (Ranvier.Css.Css.width(Ranvier.Css.Css.percent 50.)) 100u
        percentWidth.Seek 50.
        require "native unit context resolves containing-block percentages"
            (unitFirstStyle.getPropertyValue "width"="30%" && abs(unitFirst.getBoundingClientRect().width-120.)<0.02)
        percentWidth.Cancel()
        use physicalWidth=CssAnimation.declarationOn scheduler unitFirst
                            (Ranvier.Css.Css.width(Ranvier.Css.Css.cm 1.)) (Ranvier.Css.Css.width(Ranvier.Css.Css.px 100.)) 100u
        require "native unit context retains source integer baseline measurements"
            (unitFirstStyle.getPropertyValue "width"="37.8px")
        physicalWidth.Cancel()
        let rootElement=host.Document.documentElement:?>HTMLElement
        let rootStyle=(rootElement:>ElementCSSInlineStyle).style
        let priorRootFont=rootStyle.getPropertyValue "font-size"
        let priorRootPriority=rootStyle.getPropertyPriority "font-size"
        try
            rootStyle.setProperty("font-size","20px")
            use remWidth=CssAnimation.declarationOn scheduler unitFirst
                            (Ranvier.Css.Css.width(Ranvier.Css.Css.rem 1.)) (Ranvier.Css.Css.width(Ranvier.Css.Css.px 100.)) 100u
            require "native unit context resolves root-relative lengths" (unitFirstStyle.getPropertyValue "width"="20px")
            remWidth.Cancel()
        finally
            if priorRootFont="" then rootStyle.removeProperty "font-size" |> ignore
            else rootStyle.setProperty("font-size",priorRootFont,priority=priorRootPriority)
        use angle=CssAnimation.declarationOn scheduler unitSecond
                    (Ranvier.Css.Css.rotate(Ranvier.Css.Css.turn 0.5)) (Ranvier.Css.Css.rotate(Ranvier.Css.Css.deg 270.)) 100u
        angle.Seek 50.
        require "native unit context converts angles without length probes" (unitSecondStyle.getPropertyValue "rotate"="225deg")
        angle.Cancel()
        require "native unit conversion removes all temporary clones" (unitParent.childNodes.length=2u)
        let mutable writes = 0
        let owner, signal = Api.createRoot(fun owner ->
            let signal = Api.createSignal 0.
            Reactive.drive scheduler 50. (fun () -> signal.Value) (fun value ->
                writes <- writes + 1
                style.setProperty("opacity", value.ToString("G17", CultureInfo.InvariantCulture))) |> ignore
            owner, signal)
        graph.Flush()
        signal.Value <- 1.
        graph.Flush()
        pumpUntil host (fun () -> style.getPropertyValue "opacity" = "1")
        owner.Dispose()
        let priorWrites = writes
        signal.Value <- 0.
        graph.Flush()
        host.Cycle()
        host.Frame()
        require "Ranvier owner disposal stops native writes" (writes = priorWrites)
        let mutable timerLoops = 0
        use stretchedTimer=new Timer(scheduler,100u,options={PlaybackOptions.Default with Delay=40.})
        stretchedTimer.Seek 75.
        stretchedTimer.Stretch 200u
        require "native timer stretch retains playhead and scales delay"
            (stretchedTimer.CurrentTime=75. && stretchedTimer.Progress=0.375 && stretchedTimer.Delay=80.)
        stretchedTimer.Resume()
        pumpUntil host (fun () -> stretchedTimer.Completed)
        require "native stretched timer completes through shared scheduling" (stretchedTimer.CurrentTime=200.)
        let mutable timerCompletions = 0
        use repeatedTimer = new Timer(scheduler, 25u,
            options = { PlaybackOptions.Default with Repeat = PlaybackRepeat.Count 1u; Alternate = true },
            onLoop = (fun _ -> timerLoops <- timerLoops + 1),
            onComplete = (fun _ -> timerCompletions <- timerCompletions + 1))
        repeatedTimer.Resume()
        pumpUntil host (fun () -> repeatedTimer.Completed)
        require "native timer repeats and completes once" (timerLoops = 1 && timerCompletions = 1 && repeatedTimer.Paused)
        let mutable timerBeforeUpdates = 0
        let mutable timerUpdates = 0
        let timerOwner, ownedTimer = Api.createRoot(fun owner ->
            let timer = new Timer(scheduler, 1000u,
                onBeforeUpdate = (fun _ -> timerBeforeUpdates <- timerBeforeUpdates + 1; owner.Dispose()),
                onUpdate = (fun _ -> timerUpdates <- timerUpdates + 1))
            Timer.own timer |> ignore
            owner, timer)
        use timer = ownedTimer
        timer.Resume()
        pumpUntil host (fun () -> timerBeforeUpdates > 0)
        host.Cycle(milliseconds = 10)
        host.Frame()
        host.Cycle(milliseconds = 10)
        host.Frame()
        require "native timer owner disposal interrupts active callbacks"
            (timer.Cancelled && not timer.Completed && timerBeforeUpdates = 1 && timerUpdates = 0)
        timerOwner.Dispose()
        let mutable timelineBeforeUpdates = 0
        let mutable timelineUpdates = 0
        let mutable timelineWrites = 0
        let timelineOwner, ownedTimeline, ownedTimelineTimer = Api.createRoot(fun owner ->
            let timeline = new Timeline(scheduler,
                onBeforeUpdate = (fun _ -> timelineBeforeUpdates <- timelineBeforeUpdates + 1; owner.Dispose()),
                onUpdate = (fun _ -> timelineUpdates <- timelineUpdates + 1))
            timeline.Animate(NumericProperty((fun () -> 0.), (fun _ -> timelineWrites <- timelineWrites + 1)), 100., 1000u) |> ignore
            let child = timeline.AddTimer(1000u, TimelinePosition.At 0.)
            Timeline.own timeline |> ignore
            owner, timeline, child)
        use timeline = ownedTimeline
        timeline.Reset()
        timeline.Resume()
        pumpUntil host (fun () -> timelineBeforeUpdates > 0)
        host.Cycle(milliseconds = 10)
        host.Frame()
        host.Cycle(milliseconds = 10)
        host.Frame()
        require "native timeline owner disposal interrupts active property writes"
            (timeline.Cancelled && ownedTimelineTimer.Cancelled && not timeline.Completed &&
             timelineBeforeUpdates = 1 && timelineUpdates = 0 && timelineWrites = 1)
        timelineOwner.Dispose()
        require "native host has no listener errors" (host.Errors.Count = 0)
        Console.WriteLine "NATIVE_ANIME_SMOKE_PASS"
        0
    with error ->
        Console.Error.WriteLine("NATIVE_ANIME_SMOKE_FAIL " + error.GetType().FullName + ": " + error.Message)
        1
