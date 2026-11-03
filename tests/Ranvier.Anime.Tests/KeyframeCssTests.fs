module Ranvier.Anime.Tests.KeyframeCssTests

open System
open System.Collections.Generic
open System.IO
open System.Text.Json
open Expecto
open Ranvier
open Ranvier.Dom
open Ranvier.Css
open Ranvier.Anime

type private CssStyle(?log:string -> string -> unit) =
    let values=Dictionary<string,string*string>()
    let writes=ResizeArray<string*string*string>()
    member val BeforeRead:unit -> unit=ignore with get,set
    member val AfterWrite:string -> unit=ignore with get,set
    member val BeforeRemove:string -> unit=ignore with get,set
    member _.Writes=writes
    member _.Seed(name,value,priority)=values[name]<-value,priority
    member _.Value name=match values.TryGetValue name with true,(value,_) -> value | _ -> ""
    member _.Priority name=match values.TryGetValue name with true,(_,priority) -> priority | _ -> ""
    member _.Contains name=values.ContainsKey name
    interface CSSStyleDeclaration with
        member this.getPropertyValue name=this.BeforeRead();this.Value name
        member this.getPropertyPriority name=this.BeforeRead();this.Priority name
        member this.setProperty(name,value,priority)=
            let priority=defaultArg priority ""
            values[name]<-value,priority
            writes.Add(name,value,priority)
            log |> Option.iter(fun callback -> callback name value)
            this.AfterWrite name
        member this.removeProperty name=
            this.BeforeRemove name
            let value=this.Value name
            values.Remove name |> ignore
            value
        member this.item index=this.BeforeRead();values.Keys |> Seq.tryItem(int index) |> Option.defaultValue ""
        member this.length=this.BeforeRead();uint values.Count
        member _.cssText with get()=failwith "Whole style access" and set _=failwith "Whole style replacement"
        member _.parentRule=Unchecked.defaultof<CSSRule>
        member _.cssRules=Unchecked.defaultof<CSSRuleList>

type private CssCall={Field:string;Index:int;Count:int;Previous:string option;Target:string;Number:float}
let private optional (name:string) (item:JsonElement)=match item.TryGetProperty name with true,value -> Some value | _ -> None
let private number name fallback item=optional name item |> Option.map _.GetDouble() |> Option.defaultValue fallback
let private flag name item=optional name item |> Option.exists _.GetBoolean()
let private text (item:JsonElement)=if item.ValueKind=JsonValueKind.Number then ValueInterpolation.number(item.GetDouble()) else item.GetString()
let private close actual expected=Expect.isTrue (actual=expected || abs(actual-expected)<=1e-12*max 1. (abs expected)) "numeric effect"
let private linear={KeyframeAnimationOptions.Default with Autoplay=false;Ease=id}
let private fixtures()=
    let root=Environment.GetEnvironmentVariable "RANVIER_ANIME_REFERENCE_ROOT"
    let root=if String.IsNullOrWhiteSpace root then Path.Combine(AppContext.BaseDirectory,"reference") else root
    use document=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"keyframe-css.json")))
    Expect.equal (document.RootElement.GetProperty("upstreamCommit").GetString()) "2c9cf8ea00329f6768c7d7902252ed977d75ce42" "pinned source"
    document.RootElement.GetProperty("fixtures").EnumerateArray() |> Seq.map _.Clone() |> Seq.toList
let private replay (fixture:JsonElement)=
    let input=fixture.GetProperty("input")
    let originals=input.GetProperty("originals").EnumerateArray() |> Seq.map _.GetString() |> Seq.toArray
    let writes=ResizeArray<int*string>()
    let styles=originals |> Array.mapi(fun index value ->
        let style=CssStyle(log=(fun _ value -> writes.Add(index,value)))
        style.Seed("opacity",value,"")
        style)
    let calls=ResizeArray<CssCall>()
    let events=ResizeArray<string*float*string array>()
    let compute field (spec:JsonElement) (context:KeyframeContext<CSSStyleDeclaration,Declaration>) =
        let target=context.Target.getPropertyValue "opacity"
        let previous=context.PreviousValue |> Option.map Declaration.value
        calls.Add {Field=field;Index=int context.Index;Count=int context.Count;Previous=previous;Target=target;Number=0.}
        let value=number "base" 0. spec+number "step" 0. spec*float context.Index
        let raw value=(ValueInterpolation.decompose(ValueInterpolation.Text value)).Number
        let kind=spec.GetProperty("fn").GetString()
        let result=if kind="previous" then defaultArg previous target
                   else
                       let number=if kind="shift" then raw target+value elif kind="shiftPrevious" then raw(defaultArg previous target)+value else value
                       ValueInterpolation.number number+(optional "unit" spec |> Option.map _.GetString() |> Option.defaultValue "")
        Declaration.create "opacity" result
    let modifier name value=
        calls.Add {Field="modifier";Index = -1;Count = -1;Previous=None;Target="";Number=value}
        match name with "double" -> value*2. | "plusOne" -> value+1. | _ -> value
    let authored=input.GetProperty("frames").EnumerateArray() |> Seq.map(fun item ->
        let frame=
            match optional "to" item with
            | Some value when value.ValueKind=JsonValueKind.Object -> Keyframe.toBy(compute "to" value)
            | Some value -> Keyframe.toValue(Declaration.create "opacity" (text value))
            | None -> Keyframe.hold()
        let frame=
            match optional "from" item with
            | Some value when value.ValueKind=JsonValueKind.Object -> frame.WithFromBy(compute "from" value)
            | Some value -> frame.WithFrom(Declaration.create "opacity" (text value))
            | None -> frame
        let frame=
            match optional "duration" item with
            | Some value when value.ValueKind=JsonValueKind.Object -> frame.WithDurationPreciseBy(fun context -> Double.Parse(Declaration.value(compute "duration" value context),Globalization.CultureInfo.InvariantCulture))
            | Some value -> frame.WithDurationPrecise(value.GetDouble())
            | None -> frame
        let frame=
            match optional "delay" item with
            | Some value when value.ValueKind=JsonValueKind.Object -> frame.WithDelayBy(fun context -> Double.Parse(Declaration.value(compute "delay" value context),Globalization.CultureInfo.InvariantCulture))
            | Some value -> frame.WithDelay(value.GetDouble())
            | None -> frame
        match optional "modifier" item with Some value -> frame.WithModifier(modifier(value.GetString())) | _ -> frame) |> Seq.toArray
    let callback name (clock:Timer)=events.Add(name,clock.CurrentTime,styles |> Array.map(fun style -> style.Value "opacity"))
    let options=
        {linear with Modifier=modifier(optional "modifier" input |> Option.map _.GetString() |> Option.defaultValue "");
                     Composition=(if optional "composition" input |> Option.exists(fun value -> value.GetString()="none") then KeyframeComposition.None else KeyframeComposition.Replace);
                     Playback={PlaybackOptions.Default with Delay=number "delay" 0. input;Repeat=PlaybackRepeat.Count(uint(number "loop" 0. input))
                                                            LoopDelay=number "loopDelay" 0. input;Reversed=flag "reversed" input;Alternate=flag "alternate" input};
                     OnBegin=callback "Begin";OnBeforeUpdate=callback "BeforeUpdate";OnRender=callback "Render"
                     OnUpdate=callback "Update";OnLoop=callback "Loop";OnPause=callback "Pause";OnComplete=callback "Complete"}
    use scheduler=new ManualFrameScheduler()
    use animation=Keyframes.CaptureInlinePrecise(scheduler,styles |> Array.map(fun style -> style:>CSSStyleDeclaration),"opacity",authored,input.GetProperty("duration").GetDouble(),options=options)
    let verify compareWrites (expected:JsonElement)=
        let clock=animation.Playback
        for name,actual in ["time",clock.CurrentTime;"iterationTime",clock.IterationCurrentTime;"duration",clock.Duration;"iterationDuration",clock.IterationDuration] do
            close actual (expected.GetProperty(name).GetDouble())
        for name,actual in ["paused",clock.Paused;"completed",clock.Completed;"backwards",clock.Backwards] do Expect.equal actual (expected.GetProperty(name).GetBoolean()) name
        let sourceValues=expected.GetProperty("values").EnumerateArray() |> Seq.map _.GetString() |> Seq.toArray
        if compareWrites then
            Expect.sequenceEqual (styles |> Array.map(fun style -> style.Value "opacity")) sourceValues "source CSS final values"
        else
            if input.GetProperty("frames").GetArrayLength()>1 then
                for value in sourceValues do Expect.equal value "" "pinned source frame-order cleanup"
            else Expect.sequenceEqual sourceValues originals "source single-frame or empty cleanup"
            Expect.sequenceEqual (styles |> Array.map(fun style -> style.Value "opacity")) originals "native pre-construction snapshot restoration"
        let expectedCalls=expected.GetProperty("calls")
        Expect.equal calls.Count (expectedCalls.GetArrayLength()) "effect count"
        for index in 0..calls.Count-1 do
            let actual=calls[index]
            let expected=expectedCalls[index]
            Expect.equal actual.Field (expected.GetProperty("field").GetString()) "effect order"
            if actual.Field="modifier" then close actual.Number (expected.GetProperty("number").GetDouble())
            else
                Expect.equal (actual.Index,actual.Count,actual.Target) (expected.GetProperty("index").GetInt32(),expected.GetProperty("count").GetInt32(),expected.GetProperty("target").GetString()) "typed context"
                let previous=expected.GetProperty("previous")
                Expect.equal actual.Previous (if previous.ValueKind=JsonValueKind.Null then None else Some(previous.GetString())) "previous composed cache"
        calls.Clear()
        if compareWrites then
            let expectedWrites=expected.GetProperty("writes")
            Expect.equal writes.Count (expectedWrites.GetArrayLength()) "CSS write count"
            for index in 0..writes.Count-1 do
                let target,value=writes[index]
                let expected=expectedWrites[index]
                Expect.equal (target,value) (expected.GetProperty("target").GetInt32(),expected.GetProperty("value").GetString()) "ordered CSS dispatch"
        writes.Clear()
        let expectedEvents=expected.GetProperty("events")
        Expect.equal events.Count (expectedEvents.GetArrayLength()) "callback count"
        for index in 0..events.Count-1 do
            let name,time,values=events[index]
            let expected=expectedEvents[index]
            Expect.equal name (expected.GetProperty("event").GetString()) "callback phase"
            close time (expected.GetProperty("time").GetDouble())
            Expect.sequenceEqual values (expected.GetProperty("values").EnumerateArray() |> Seq.map _.GetString()) "callback targets"
        events.Clear()
    verify true (fixture.GetProperty("initial"))
    for sample in fixture.GetProperty("samples").EnumerateArray() do
        let time=sample.GetProperty("time").GetDouble()
        animation.Playback.Seek time
        try verify true (sample.GetProperty("state")) with error -> failtestf "At %g: %s" time error.Message
    animation.Revert()
    verify false (fixture.GetProperty("reverted"))

let cssSourceTests=testList "public CSS keyframe source" [
    for fixture in fixtures() do testCase (fixture.GetProperty("name").GetString()) <| fun _ -> replay fixture
]


let cssContractTests=testList "public CSS keyframe contracts" [
    testCase "captured auto uses source zero while restoration retains the authored keyword" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        let recorder=CssStyle()
        recorder.Seed("width","auto","")
        use animation=Keyframes.CaptureInline(scheduler,(recorder:>CSSStyleDeclaration),"width",
                                             [|Keyframe.toValue(Css.width(Css.px 100.))|],duration=100u,options=linear)
        animation.Playback.Seek 50.
        Expect.equal (recorder.Value "width") "50px" "auto-to-zero initialization"
        animation.Revert()
        Expect.equal (recorder.Value "width") "auto" "original keyword restored"
    testCase "scoped snapshots restore value priority empty presence and absence independently" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        let styles=[|CssStyle();CssStyle();CssStyle()|]
        styles[0].Seed("--value","7px","important")
        styles[1].Seed("--value","","important")
        let targets=styles |> Array.map(fun style -> style:>CSSStyleDeclaration)
        use animation=Keyframes.Declaration(scheduler,targets,Declaration.create "--value" "10px",
                                            [|Keyframe.toValue(Declaration.create "--value" "100px")|],duration=100u,options=linear)
        animation.Playback.Seek 50.
        for style in styles do Expect.equal (style.Value "--value") "55px" "shared typed interpolation"
        targets[0].setProperty("height","42px")
        animation.Revert()
        Expect.equal (styles[0].Value "--value",styles[0].Priority "--value") ("7px","important") "value and priority"
        Expect.isTrue (styles[1].Contains "--value") "empty authored presence"
        Expect.equal (styles[1].Value "--value",styles[1].Priority "--value") ("","important") "empty value priority"
        Expect.isFalse (styles[2].Contains "--value") "absent property removed"
        Expect.equal (styles[0].Value "height") "42px" "unrelated edit"

    testCase "a constant important endpoint requests importance throughout and restores original priority" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        let recorder=CssStyle()
        recorder.Seed("width","7px","")
        use animation=Keyframes.Declaration(scheduler,(recorder:>CSSStyleDeclaration),Css.width(Css.px 0.),
                                            [|Keyframe.toValue(Css.width(Css.px 100.));Keyframe.toValue(Declaration.important(Css.width(Css.px 200.)))|],
                                            duration=200u,options=linear)
        Expect.equal (recorder.Priority "width") "important" "constant importance captured before initialization"
        animation.Revert()
        Expect.equal (recorder.Priority "width") "" "original priority"

    testCase "a later constant property mismatch rejects before any style access" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        let recorder=CssStyle()
        recorder.BeforeRead<-fun () -> failtest "style read before complete endpoint validation"
        Expect.throwsT<ArgumentException> (fun () ->
            Keyframes.Declaration(scheduler,(recorder:>CSSStyleDeclaration),Css.width(Css.px 0.),
                                  [|Keyframe.toValue(Css.width(Css.px 100.));Keyframe.toValue(Css.height(Css.px 200.))|]) |> ignore) "one captured property"

    testCase "a dynamic property mismatch cannot publish a partial sequence" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        let recorder=CssStyle()
        recorder.Seed("width","7px","")
        let frame=Keyframe.toBy(fun (_:KeyframeContext<CSSStyleDeclaration,Declaration>) -> Css.height(Css.px 100.))
        Expect.throwsT<ArgumentException> (fun () ->
            Keyframes.CaptureInline(scheduler,(recorder:>CSSStyleDeclaration),"width",[|frame|]) |> ignore) "dynamic property identity"
        Expect.isEmpty recorder.Writes "construction dispatch"

    testCase "constant divided durations reject before style capture" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        let recorder=CssStyle()
        recorder.BeforeRead<-fun () -> failtest "style read before timing preflight"
        let frames=Keyframe.values [|Css.width(Css.px 0.);Css.width(Css.px 100.);Css.width(Css.px 200.);Css.width(Css.px 300.)|]
        Expect.throwsT<ArgumentException> (fun () ->
            Keyframes.DeclarationPrecise(scheduler,(recorder:>CSSStyleDeclaration),Css.width(Css.px 0.),frames,1e-12) |> ignore) "positive subprecision"

    testCase "owner disposal inside destination callbacks suppresses later source and timing effects" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        use graph=new Graph()
        use activation=graph.Activate()
        let owner=Api.createRoot id
        let recorder=CssStyle()
        recorder.Seed("width","7px","")
        let frame=(Keyframe.toBy(fun (_:KeyframeContext<CSSStyleDeclaration,Declaration>) -> owner.Dispose();Css.width(Css.px 100.)))
                     .WithFromBy(fun _ -> failtest "source after disposal")
                     .WithDurationBy(fun _ -> failtest "duration after disposal")
                     .WithDelayBy(fun _ -> failtest "delay after disposal")
        Expect.throwsT<ObjectDisposedException> (fun () ->
            Keyframes.CaptureInline(scheduler,(recorder:>CSSStyleDeclaration),"width",[|frame|],owner=owner) |> ignore) "interrupted construction"
        Expect.isEmpty recorder.Writes "cancelled dispatch"

    testCase "snapshot getter disposal prevents later getters and target capture" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        use graph=new Graph()
        use activation=graph.Activate()
        let owner=Api.createRoot id
        let first=CssStyle()
        let second=CssStyle()
        first.BeforeRead<-fun () -> owner.Dispose()
        second.BeforeRead<-fun () -> failtest "later target after disposal"
        Expect.throwsT<ObjectDisposedException> (fun () ->
            Keyframes.CaptureInline(scheduler,[|first:>CSSStyleDeclaration;second:>CSSStyleDeclaration|],"width",
                                    [|Keyframe.toValue(Css.width(Css.px 100.))|],owner=owner) |> ignore) "capture interruption"

    testCase "component modifier owner disposal prevents partial composed output" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        use graph=new Graph()
        use activation=graph.Activate()
        let owner=Api.createRoot id
        let recorder=CssStyle()
        let mutable armed=false
        let mutable calls=0
        let modify value=if armed then calls<-calls+1;owner.Dispose();value else value
        use animation=Keyframes.Declaration(scheduler,(recorder:>CSSStyleDeclaration),Declaration.create "transform" "translate(0px,0px)",
                                            [|(Keyframe.toValue(Declaration.create "transform" "translate(100px,200px)")).WithModifier modify|],
                                            duration=100u,options=linear,owner=owner)
        recorder.Writes.Clear()
        armed<-true
        animation.Playback.Seek 50.
        Expect.equal calls 1 "later components suppressed"
        Expect.isEmpty recorder.Writes "partial output suppressed"
        Expect.isTrue animation.Cancelled "owner lifetime"

    testCase "a setter seek supersedes outer callbacks and final output" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        let recorder=CssStyle()
        let mutable armed=false
        let mutable active:Timer option=None
        let mutable renders=0
        recorder.AfterWrite<-fun _ -> if armed then armed<-false;active.Value.Seek(75.,muteCallbacks=true)
        use animation=Keyframes.Declaration(scheduler,(recorder:>CSSStyleDeclaration),Css.width(Css.px 0.),
                                            [|Keyframe.toValue(Css.width(Css.px 100.))|],duration=100u,
                                            options={linear with OnRender=(fun _ -> renders<-renders+1)})
        active<-Some animation.Playback
        armed<-true
        animation.Playback.Seek 50.
        Expect.equal (recorder.Value "width") "75px" "inner write"
        Expect.equal renders 0 "muted inner and superseded outer"

    testCase "restore faults preserve the first exception and finish later targets" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        let first=CssStyle()
        let second=CssStyle()
        first.Seed("width","7px","")
        second.Seed("width","8px","")
        let fault=InvalidOperationException("CSS keyframe restore fault")
        let mutable armed=false
        first.AfterWrite<-fun _ -> if armed && first.Value "width"="7px" then raise fault
        use animation=Keyframes.Declaration(scheduler,[|first:>CSSStyleDeclaration;second:>CSSStyleDeclaration|],Css.width(Css.px 0.),
                                            [|Keyframe.toValue(Css.width(Css.px 100.))|],duration=100u,options=linear)
        animation.Playback.Seek 50.
        armed<-true
        let mutable actual:exn option=None
        try animation.Revert() with error -> actual<-Some error
        Expect.isTrue (actual |> Option.exists(fun error -> Object.ReferenceEquals(error,fault))) "primary fault"
        Expect.equal (second.Value "width") "8px" "later restoration"
        Expect.isTrue animation.Cancelled "cleanup lifetime"

    testCase "owner disposal while restoring prevents later target writes" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        use graph=new Graph()
        use activation=graph.Activate()
        let owner=Api.createRoot id
        let first=CssStyle()
        let second=CssStyle()
        first.Seed("width","7px","")
        second.Seed("width","8px","")
        let mutable armed=false
        first.AfterWrite<-fun _ -> if armed && first.Value "width"="7px" then owner.Dispose()
        use animation=Keyframes.Declaration(scheduler,[|first:>CSSStyleDeclaration;second:>CSSStyleDeclaration|],Css.width(Css.px 0.),
                                            [|Keyframe.toValue(Css.width(Css.px 100.))|],duration=100u,options=linear,owner=owner)
        animation.Playback.Seek 50.
        armed<-true
        animation.Revert()
        Expect.equal (first.Value "width",second.Value "width") ("7px","0px") "interrupted snapshot restoration"

    testCase "empty CSS sequences skip unused style access" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        let recorder=CssStyle()
        recorder.BeforeRead<-fun () -> failtest "unused empty style"
        use animation=Keyframes.CaptureInline(scheduler,(recorder:>CSSStyleDeclaration),"width",[||])
        Expect.isTrue animation.Playback.Completed "no-op completion"
        animation.Revert()

    testCase "shared playback cancellation closes CSS restoration" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        let recorder=CssStyle()
        recorder.Seed("width","7px","important")
        use animation=Keyframes.Declaration(scheduler,(recorder:>CSSStyleDeclaration),Css.width(Css.px 0.),
                                            [|Keyframe.toValue(Css.width(Css.px 100.))|],duration=100u,options=linear)
        animation.Playback.Seek 50.
        animation.Playback.Cancel()
        recorder.Writes.Clear()
        animation.Revert()
        Expect.isEmpty recorder.Writes "terminal restoration"
        Expect.equal (recorder.Value "width") "50px" "last output retained"

    testCase "style-only differing scalar units reject before dispatch" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        let recorder=CssStyle()
        recorder.Seed("width","7px","")
        Expect.throwsT<ArgumentException> (fun () ->
            Keyframes.Declaration(scheduler,(recorder:>CSSStyleDeclaration),Css.width(Css.px 16.),
                                  [|Keyframe.toValue(Css.width(Css.em 2.))|]) |> ignore) "element context required"
        Expect.isEmpty recorder.Writes "unit preparation is atomic"

    testCase "construction captures later authored frames before callback mutation" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        let recorder=CssStyle()
        recorder.Seed("width","0px","")
        let frames=[|Keyframe.toValue(Css.width(Css.px 100.));Keyframe.toValue(Css.width(Css.px 200.))|]
        frames[0]<-Keyframe.toBy(fun (_:KeyframeContext<CSSStyleDeclaration,Declaration>) ->
            frames[1]<-Keyframe.toValue(Css.height(Css.px 999.))
            Css.width(Css.px 100.))
        use animation=Keyframes.CaptureInline(scheduler,(recorder:>CSSStyleDeclaration),"width",frames,duration=200u,options=linear)
        animation.Playback.Seek 150.
        Expect.equal (recorder.Value "width") "150px" "captured later endpoint"

    testCase "unsupported CSS shorthands reject before style access or callbacks" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        let recorder=CssStyle()
        recorder.BeforeRead<-fun () -> failtest "shorthand style access"
        for name in ["margin";"MARGIN";"border";"background";"font";"all";"grid-area";"padding-inline";"animation-range"] do
            Expect.throwsT<ArgumentException> (fun () ->
                Keyframes.CaptureInline(scheduler,(recorder:>CSSStyleDeclaration),name,
                    [|Keyframe.toBy(fun _ -> failtest "shorthand callback")|]) |> ignore) name
        Expect.isEmpty recorder.Writes "no shorthand dispatch"
]
