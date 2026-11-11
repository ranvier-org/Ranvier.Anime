module Ranvier.Anime.Tests.TimelineCssSequenceSourceTests

open Expecto
open System
open System.Collections.Generic
open System.Text.Json
open System.Text.Json.Nodes
open Ranvier.Dom
open Ranvier.Css
open Ranvier.Anime

let optional (name:string) (value:JsonElement) = match value.TryGetProperty name with true,item -> Some item | _ -> None
let number name fallback value=optional name value |> Option.map _.GetDouble() |> Option.defaultValue fallback
let flag name value=optional name value |> Option.exists _.GetBoolean()
let parameters value =
    {PlaybackOptions.Default with Delay=number "delay" 0. value;LoopDelay=number "loopDelay" 0. value
                                  Speed=number "playbackRate" 1. value;FrameRate=number "frameRate" 240. value
                                  Reversed=flag "reversed" value;Alternate=flag "alternate" value
                                  Repeat=PlaybackRepeat.Count(uint(number "loop" 0. value))}
let dict (fields:(string*obj) list) =
    let d=Dictionary<string,obj>()
    for key,value in fields do d[key]<-value
    d
let clockState (clock:Timer)=box(dict ["time",box clock.CurrentTime;"iterationTime",box clock.IterationCurrentTime;
                                    "iteration",box clock.CurrentIteration;"paused",box clock.Paused;"began",box clock.Began;
                                    "completed",box clock.Completed;"backwards",box clock.Backwards])
let rootState (root:Timeline)=box(dict ["time",box root.CurrentTime;"iterationTime",box root.IterationCurrentTime;
                                     "iteration",box root.CurrentIteration;"paused",box(not root.Playing);"began",box root.Began;
                                     "completed",box root.Completed;"backwards",box root.Backwards])
let private px (value:float)=value.ToString("G",Globalization.CultureInfo.InvariantCulture)+"px"
let verify (fixture:JsonElement) =
    use scheduler=new Ranvier.Dom.ManualFrameScheduler()
    let input=fixture.GetProperty "input"
    let initial=optional "targets" input |> Option.map(fun targets -> targets.EnumerateArray() |> Seq.map(fun target -> [|px(target.GetProperty("width").GetDouble());px(target.GetProperty("height").GetDouble())|]) |> Seq.toArray)
                |> Option.defaultValue [|[|"10px";"20px"|]|]
    let values=initial |> Array.map Array.copy
    let children=Dictionary<string,KeyframeAnimation>()
    let writes=ResizeArray<obj>()
    let effects=ResizeArray<obj>()
    let events=ResizeArray<obj>()
    let trace=ResizeArray<obj>()
    let record kind (value:Dictionary<string,obj>) =
        (match kind with "write" -> writes | "effect" -> effects | _ -> events).Add value
        let entry=Dictionary<string,obj>(value)
        entry["kind"]<-kind
        trace.Add entry
    let mutable activeRoot:Timeline option=None
    let snapshot() =
        let states=Dictionary<string,obj>()
        for KeyValue(id,child) in children do states[id]<-clockState child.Playback
        box(dict ["targets",box(values |> Array.map(fun value -> dict ["width",box value[0];"height",box value[1]]));
                  "root",rootState activeRoot.Value;"children",box states])
    let event id phase self=record "event" (dict ["id",box id;"event",box phase;"self",self;"snapshot",(if activeRoot.IsSome then snapshot() else null)])
    let rootCallback phase root=event "root" phase (rootState root)
    let rootParameters=optional "parameters" input |> Option.defaultValue (JsonSerializer.SerializeToElement(dict []))
    use root=new Timeline(scheduler,options=parameters rootParameters,
        onBegin=rootCallback "Begin",onBeforeUpdate=rootCallback "BeforeUpdate",onRender=rootCallback "Render",
        onUpdate=rootCallback "Update",onLoop=rootCallback "Loop",onPause=rootCallback "Pause",onComplete=rootCallback "Complete")
    activeRoot<-Some root
    let propertyIndex name=if name="width" then 0 elif name="height" then 1 else failwith("Unexpected CSS property "+name)
    let styles=Array.init values.Length (fun target ->
        { new CSSStyleDeclaration with
            member _.getPropertyValue name=values[target].[propertyIndex name]
            member _.getPropertyPriority _=""
            member _.length=2u
            member _.item index=if index=0u then "width" elif index=1u then "height" else ""
            member _.setProperty(name,value,_) =
                values[target].[propertyIndex name]<-value
                record "write" (dict ["target",box target;"property",box name;"value",box value])
            member _.removeProperty name=
                let prior=values[target].[propertyIndex name]
                values[target].[propertyIndex name]<-""
                record "write" (dict ["target",box target;"property",box name;"value",box ""])
                prior
            member _.cssText with get()=failtest "whole-style source read" and set _=failtest "whole-style source write"
            member _.parentRule=Unchecked.defaultof<CSSRule>
            member _.cssRules=Unchecked.defaultof<CSSRuleList> })
    let capture() =
        let value=dict ["snapshot",snapshot();"writes",box(writes.ToArray());"effects",box(effects.ToArray());"events",box(events.ToArray());"trace",box(trace.ToArray())]
        writes.Clear();effects.Clear();events.Clear();trace.Clear()
        JsonSerializer.SerializeToNode value
    let compare label (actual:JsonNode) (expected:JsonElement) =
        let expected=JsonNode.Parse(expected.GetRawText())
        if not(JsonNode.DeepEquals(actual,expected)) then failwith(label+"\nACTUAL "+actual.ToJsonString()+"\nEXPECTED "+expected.ToJsonString())
    let construction=fixture.GetProperty "construction"
    for childIndex,child in input.GetProperty("children").EnumerateArray() |> Seq.indexed do
        let childId=child.GetProperty("id").GetString()
        let p=child.GetProperty "parameters"
        let sequenceInput=optional "keyframes" p
        let propertyName=if optional "width" p |> Option.isSome then "width" else "height"
        let scalar (text:string)=Double.Parse(text.Substring(0,text.Length-2),Globalization.CultureInfo.InvariantCulture)
        let relative (value:JsonElement) =
            let text=value.GetString()
            let operation=match text[0] with '+' -> RelativeOperation.Add | '-' -> RelativeOperation.Subtract | '*' -> RelativeOperation.Multiply | _ -> failwith("Unexpected relative operation "+text)
            operation,Declaration.create propertyName (text.Substring(2))
        let read field (value:JsonElement) (context:KeyframeContext<CSSStyleDeclaration,Declaration>) =
            let previous=context.PreviousValue |> Option.map Declaration.value
            let targetValue=values.[int context.Index].[0]
            record "effect" (dict ["id",box childId;"field",box field;"index",box(int context.Index);"count",box(int context.Count);
                                  "previous",(previous |> Option.map box |> Option.defaultValue null);"targetValue",box targetValue])
            let basis=number "base" 0. value+number "step" 0. value*float context.Index
            match value.GetProperty("fn").GetString() with
            | "target" -> scalar targetValue+basis
            | "previous" -> scalar(defaultArg previous targetValue)+basis
            | _ -> basis
        let raw=if sequenceInput.IsSome then [||] else p.GetProperty(propertyName).EnumerateArray() |> Seq.toArray
        let frames=
            if raw |> Array.forall(fun value -> value.ValueKind=JsonValueKind.Number) then
                Keyframe.values(raw |> Array.map(fun value -> Declaration.create propertyName (px(value.GetDouble()))))
            else raw |> Array.map(fun frame ->
                let mutable result=
                    match optional "to" frame with
                    | Some value when value.ValueKind=JsonValueKind.Object -> Keyframe<CSSStyleDeclaration,Declaration>.ToBy(fun context -> Declaration.create propertyName (px(read "to" value context)))
                    | Some value when value.ValueKind=JsonValueKind.String -> let operation,value=relative value in Keyframe.ToRelative(operation,value)
                    | Some value -> Keyframe.To(Declaration.create propertyName (px(value.GetDouble())))
                    | None -> Keyframe.Hold()
                optional "from" frame |> Option.iter(fun value ->
                    result<-
                        if value.ValueKind=JsonValueKind.Object then result.WithFromBy(fun context -> Declaration.create propertyName (px(read "from" value context)))
                        elif value.ValueKind=JsonValueKind.String then let operation,value=relative value in result.WithFromRelative(operation,value)
                        else result.WithFrom(Declaration.create propertyName (px(value.GetDouble()))))
                optional "duration" frame |> Option.iter(fun value -> result<-if value.ValueKind=JsonValueKind.Object then result.WithDurationPreciseBy(read "duration" value) else result.WithDurationPrecise(value.GetDouble()))
                optional "delay" frame |> Option.iter(fun value -> result<-result.WithDelay(value.GetDouble()))
                result)
        let cb phase clock=event childId phase (clockState clock)
        let modifier=
            match optional "modifier" p with
            | Some modifier -> fun value -> record "effect" (dict ["id",box childId;"value",box value]);if modifier.GetString()="plusOne" then value+1. else value*2.
            | None -> id
        let options=
            {Ranvier.Anime.KeyframeAnimationOptions.Default with
                Playback=parameters p;Ease=id;Precision=Some 4u;Modifier=modifier
                Composition=(if fixture.GetProperty("name").GetString().StartsWith("none-") then KeyframeComposition.None else KeyframeComposition.Replace)
                OnBegin=cb "Begin";OnBeforeUpdate=cb "BeforeUpdate";OnRender=cb "Render";OnUpdate=cb "Update"
                OnLoop=cb "Loop";OnPause=cb "Pause";OnComplete=cb "Complete"}
        let duration=number "duration" 1000. p
        let position=TimelinePosition.At(child.GetProperty("at").GetDouble())
        let controller=
            match sequenceInput with
            | None -> root.CaptureInlineKeyframesPrecise(styles,propertyName,frames,duration,position=position,options=options)
            | Some input ->
                let entries (value:JsonElement)=value.EnumerateObject() |> Seq.filter(fun item -> item.Name="width" || item.Name="height")
                                               |> Seq.map(fun item -> item.Name,Declaration.create item.Name (px(item.Value.GetDouble()))) |> Seq.toArray
                let sequence:KeyframeSequence<CSSStyleDeclaration,Declaration>=
                    if input.ValueKind=JsonValueKind.Array then
                        input.EnumerateArray() |> Seq.map(fun row ->
                            let mutable value=KeyframeRow.values(entries row)
                            optional "duration" row |> Option.iter(fun duration -> value<-value.WithDurationPrecise(duration.GetDouble()))
                            optional "delay" row |> Option.iter(fun delay -> value<-value.WithDelay(delay.GetDouble()))
                            optional "ease" row |> Option.iter(fun ease -> value<-value.WithEase(Ease.named(ease.GetString())))
                            value) |> Seq.toArray |> KeyframeSequence.Rows
                    else
                        input.EnumerateObject() |> Seq.map(fun key ->
                            let position=Double.Parse(key.Name.TrimEnd('%'),Globalization.CultureInfo.InvariantCulture)/100.
                            let mutable value=KeyframeOffset.at position (entries key.Value)
                            optional "ease" key.Value |> Option.iter(fun ease -> value<-value.WithEase(Ease.named(ease.GetString())))
                            value) |> Seq.toArray |> KeyframeSequence.Offsets
                root.CaptureInlineKeyframesPrecise(styles,sequence,duration,position=position,options=options)
        children.Add(childId,controller)
        let actual=capture()
        let expected=construction[childIndex]
        for name in ["snapshot";"writes";"effects";"events";"trace"] do compare (fixture.GetProperty("name").GetString()+" construction "+string childIndex+" "+name) actual[name] (expected.GetProperty name)
    root.Reset()
    compare (fixture.GetProperty("name").GetString()+" initial") (capture()) (fixture.GetProperty "initial")
    for sample in fixture.GetProperty("samples").EnumerateArray() do
        let arg=optional "value" sample |> Option.filter(fun value -> value.ValueKind<>JsonValueKind.Null) |> Option.map _.GetDouble() |> Option.defaultValue 0.
        match sample.GetProperty("operation").GetString() with
        | "seek" -> root.Seek arg
        | "seekMuted" -> root.Seek(arg,muteCallbacks=true)
        | "reset" -> root.Reset()
        | "completeMuted" -> root.Complete(muteCallbacks=true)
        | "resume" -> root.Resume()
        | "pause" -> root.Pause()
        | "reverse" -> root.Reverse()
        | "restart" -> root.Restart()
        | "speed" -> root.Speed<-arg
        | "frame" -> scheduler.Advance arg;scheduler.RunFrame()
        | other -> failwith other
        compare (fixture.GetProperty("name").GetString()+" "+sample.GetProperty("operation").GetString()+" "+string arg) (capture()) (sample.GetProperty "state")

let private fixtures() =
    let root=Environment.GetEnvironmentVariable "RANVIER_ANIME_REFERENCE_ROOT"
    let root=if String.IsNullOrWhiteSpace root then System.IO.Path.Combine(AppContext.BaseDirectory,"reference") else root
    use source=JsonDocument.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(root,"timeline-css-keyframes.json")))
    Expect.equal (source.RootElement.GetProperty("upstreamCommit").GetString()) "2c9cf8ea00329f6768c7d7902252ed977d75ce42" "pinned owned source"
    let supported=source.RootElement.GetProperty("fixtures").EnumerateArray()
                  |> Seq.map _.Clone() |> Seq.toArray
    Expect.equal supported.Length 42 "complete captured CSS owned schedules"
    supported

let sourceTests=testList "public owned CSS source traces" [
    for fixture in fixtures() do
        testCase (fixture.GetProperty("name").GetString()) <| fun _ -> verify fixture
]
