module Ranvier.Anime.Tests.KeyframeSequenceInputTests

open System
open System.Collections.Generic
open System.Globalization
open System.IO
open System.Text.Json
open Expecto
open Ranvier
open Ranvier.Dom
open Ranvier.Css
open Ranvier.Anime

type private InputCall={Field:string;Index:int;Count:int;Previous:string option;Target:string;Number:float}
type private Style(values:Dictionary<string,string>,write:string -> string -> unit) =
    let priorities=Dictionary<string,string>(StringComparer.Ordinal)
    interface CSSStyleDeclaration with
        member _.getPropertyValue name=match values.TryGetValue name with true,value -> value | _ -> ""
        member _.getPropertyPriority name=match priorities.TryGetValue name with true,value -> value | _ -> ""
        member _.setProperty(name,value,priority)=priorities[name]<-defaultArg priority "";write name value
        member _.removeProperty name=
            let value=match values.TryGetValue name with true,value -> value | _ -> ""
            values.Remove name |> ignore
            priorities.Remove name |> ignore
            value
        member _.item index=values.Keys |> Seq.tryItem(int index) |> Option.defaultValue ""
        member _.length=uint values.Count
        member _.cssText with get()=failtest "unexpected whole style read" and set _=failtest "whole style replacement"
        member _.parentRule=Unchecked.defaultof<CSSRule>
        member _.cssRules=Unchecked.defaultof<CSSRuleList>

let private optional (name:string) (value:JsonElement)=match value.TryGetProperty name with true,value -> Some value | _ -> None
let private number name fallback value=optional name value |> Option.map _.GetDouble() |> Option.defaultValue fallback
let private flag name value=optional name value |> Option.exists _.GetBoolean()
let private reserved=set ["at";"key";"duration";"delay";"ease";"modifier"]
let private fixtureText (value:JsonElement)=if value.ValueKind=JsonValueKind.Number then ValueInterpolation.number(value.GetDouble()) else value.GetString()
let private close actual expected=Expect.isTrue (actual=expected || abs(actual-expected)<=1e-12*Math.Max(1.,abs expected)) "source numeric value"
let private fixtures() =
    let root=Environment.GetEnvironmentVariable "RANVIER_ANIME_REFERENCE_ROOT"
    let root=if String.IsNullOrWhiteSpace root then Path.Combine(AppContext.BaseDirectory,"reference") else root
    use document=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"keyframe-sequences.json")))
    Expect.equal (document.RootElement.GetProperty("upstreamCommit").GetString()) "2c9cf8ea00329f6768c7d7902252ed977d75ce42" "pinned source"
    document.RootElement.GetProperty("fixtures").EnumerateArray() |> Seq.map _.Clone() |> Seq.toList

let private sequence (input:JsonElement) (wrap:string -> float -> 'Value)
                     (compute:string -> JsonElement -> KeyframeContext<'Target,'Value> -> float)
                     (modifier:string -> float -> float) : KeyframeSequence<'Target,'Value> =
    match optional "rows" input with
    | Some rows ->
        rows.EnumerateArray()
        |> Seq.map(fun value ->
            let mutable row=KeyframeRow<'Target,'Value>.Create [||]
            for property in value.EnumerateObject() do
                if not(reserved.Contains property.Name) then
                    let name=property.Name
                    if property.Value.ValueKind=JsonValueKind.Object then
                        let specification=property.Value
                        row<-row.WithValueBy(name,(fun context -> wrap name (compute name specification context)))
                    else row<-row.WithValue(name,wrap name (property.Value.GetDouble()))
            match optional "duration" value with
            | Some specification when specification.ValueKind=JsonValueKind.Object -> row<-row.WithDurationPreciseBy(compute "duration" specification)
            | Some value -> row<-row.WithDurationPrecise(value.GetDouble())
            | None -> ()
            match optional "delay" value with
            | Some specification when specification.ValueKind=JsonValueKind.Object -> row<-row.WithDelayBy(compute "delay" specification)
            | Some value -> row<-row.WithDelay(value.GetDouble())
            | None -> ()
            optional "ease" value |> Option.iter(fun value -> row<-row.WithEase(Ease.named(value.GetString())))
            optional "modifier" value |> Option.iter(fun value -> row<-row.WithModifier(modifier(value.GetString())))
            row)
        |> Seq.toArray |> KeyframeSequence.Rows
    | None ->
        input.GetProperty("offsets").EnumerateArray()
        |> Seq.map(fun value ->
            let mutable key=KeyframeOffset<'Target,'Value>.At(value.GetProperty("at").GetDouble(),[||])
            for property in value.EnumerateObject() do
                if not(reserved.Contains property.Name) then
                    let name=property.Name
                    if property.Value.ValueKind=JsonValueKind.Object then
                        let specification=property.Value
                        key<-key.WithValueBy(name,(fun context -> wrap name (compute name specification context)))
                    else key<-key.WithValue(name,wrap name (property.Value.GetDouble()))
            optional "ease" value |> Option.iter(fun value -> key<-key.WithEase(Ease.named(value.GetString())))
            key)
        |> Seq.toArray |> KeyframeSequence.Offsets

let private replay (fixture:JsonElement) =
    let input=fixture.GetProperty("input")
    let css=input.GetProperty("mode").GetString()="css"
    let defaultName=if css then "width" else "x"
    let inputs=optional "targets" input |> Option.map(fun values -> values.EnumerateArray() |> Seq.toArray)
               |> Option.defaultWith(fun () -> [|input.GetProperty("original")|])
    let values=inputs |> Array.map(fun input ->
        let values=Dictionary<string,string>(StringComparer.Ordinal)
        for property in input.EnumerateObject() do
            values[property.Name]<-ValueInterpolation.number(property.Value.GetDouble())+(if css then "px" else "")
        values)
    let writes=ResizeArray<int*string*string>()
    let calls=ResizeArray<InputCall>()
    let events=ResizeArray<string*float*Dictionary<string,string> array>()
    let write target name value=values[target][name]<-value;writes.Add(target,name,value)
    let parse (value:string)=Double.Parse((if css then value.Replace("px","") else value),NumberStyles.Float,CultureInfo.InvariantCulture)
    let compute field (specification:JsonElement) target count previous =
        let original=values[target][defaultName]
        calls.Add {Field=field;Index=target;Count=count;Previous=previous;Target=original;Number=0.}
        let baseValue=number "base" 0. specification+number "step" 0. specification*float target
        match specification.GetProperty("fn").GetString() with
        | "target" -> parse original+baseValue
        | "previous" -> parse(defaultArg previous original)+baseValue
        | "mutate" -> write target defaultName (ValueInterpolation.number baseValue+(if css then "px" else ""));baseValue
        | _ -> baseValue
    let modifier name value=
        calls.Add {Field="modifier";Index = -1;Count = -1;Previous=None;Target="";Number=value}
        match name with "plusOne" -> value+1. | "double" -> value*2. | _ -> value
    let callback phase (clock:Timer)=events.Add(phase,clock.CurrentTime,values |> Array.map(fun value -> Dictionary<string,string>(value,StringComparer.Ordinal)))
    let options=
        {Ranvier.Anime.KeyframeAnimationOptions.Default with Autoplay=false;Ease=id;Modifier=modifier ""
                                                             Composition=(if input.GetProperty("composition").GetString()="none" then KeyframeComposition.None else KeyframeComposition.Replace)
                                                             Playback={PlaybackOptions.Default with Delay=number "delay" 0. input;Repeat=PlaybackRepeat.Count(uint(number "loop" 0. input));
                                                                                                   LoopDelay=number "loopDelay" 0. input;Alternate=flag "alternate" input;Reversed=flag "reversed" input}
                                                             OnBegin=callback "Begin";OnBeforeUpdate=callback "BeforeUpdate";OnRender=callback "Render";OnUpdate=callback "Update"
                                                             OnLoop=callback "Loop";OnPause=callback "Pause";OnComplete=callback "Complete"}
    use scheduler=new ManualFrameScheduler()
    use animation=
        if css then
            let styles=values |> Array.mapi(fun target value -> Style(value,write target):>CSSStyleDeclaration)
            let compute field specification (context:KeyframeContext<CSSStyleDeclaration,Declaration>) =
                Expect.isTrue (obj.ReferenceEquals(context.Target,styles[int context.Index])) "typed CSS target"
                compute field specification (int context.Index) (int context.Count) (context.PreviousValue |> Option.map Declaration.value)
            let wrap name value=Declaration.create name (ValueInterpolation.number value+"px")
            Keyframes.CaptureInlinePrecise(scheduler,styles,sequence input wrap compute modifier,input.GetProperty("duration").GetDouble(),options=options)
        else
            let rows=optional "rows" input |> Option.defaultWith(fun () -> input.GetProperty("offsets"))
            let names=rows.EnumerateArray() |> Seq.collect(fun row -> row.EnumerateObject() |> Seq.filter(fun property -> not(reserved.Contains property.Name)) |> Seq.map _.Name) |> Seq.distinct |> Seq.toArray
            let handles=names |> Array.map(fun name ->
                name,values |> Array.mapi(fun target values ->
                    NumericProperty((fun () -> Double.Parse(values[name],CultureInfo.InvariantCulture)),(fun value -> write target name (ValueInterpolation.number value)))))
            let compute field specification (context:KeyframeContext<NumericProperty,float>) =
                let target=int context.Index
                let found=
                    handles |> Array.exists(fun (name,handles) ->
                        (reserved.Contains field || name=field) && obj.ReferenceEquals(context.Target,handles[target]))
                Expect.isTrue found "typed numeric target and index"
                compute field specification target (int context.Count) (context.PreviousValue |> Option.map ValueInterpolation.number)
            Keyframes.NumericPrecise(scheduler,handles,sequence input (fun _ value -> value) compute modifier,input.GetProperty("duration").GetDouble(),options=options)
    let equalValue actual (expected:JsonElement)=if css then Expect.equal actual (expected.GetString()) "source CSS text" else close (Double.Parse(actual,CultureInfo.InvariantCulture)) (expected.GetDouble())
    let equalTargets (actual:Dictionary<string,string> array) (expected:JsonElement)=
        Expect.equal actual.Length (expected.GetArrayLength()) "source target count"
        for target in 0..actual.Length-1 do
            for property in expected[target].EnumerateObject() do equalValue actual.[target].[property.Name] property.Value
    let verify (expected:JsonElement) =
        let clock=animation.Playback
        for name,actual in ["time",clock.CurrentTime;"iterationTime",clock.IterationCurrentTime;"duration",clock.Duration;"iterationDuration",clock.IterationDuration] do close actual (expected.GetProperty(name).GetDouble())
        for name,actual in ["paused",clock.Paused;"completed",clock.Completed;"backwards",clock.Backwards] do Expect.equal actual (expected.GetProperty(name).GetBoolean()) name
        equalTargets values (expected.GetProperty("targets"))
        let expectedCalls=expected.GetProperty("calls")
        Expect.equal calls.Count (expectedCalls.GetArrayLength()) "source input/modifier effect count"
        for index in 0..calls.Count-1 do
            let actual=calls[index]
            let expected=expectedCalls[index]
            Expect.equal actual.Field (expected.GetProperty("field").GetString()) "effect order"
            if actual.Field="modifier" then close actual.Number (expected.GetProperty("value").GetDouble())
            else
                Expect.equal (actual.Index,actual.Count) (expected.GetProperty("index").GetInt32(),expected.GetProperty("count").GetInt32()) "context index/count"
                equalValue actual.Target (expected.GetProperty("targetValue"))
                let previous=expected.GetProperty("previous")
                if previous.ValueKind=JsonValueKind.Null then Expect.isNone actual.Previous "first predecessor"
                else equalValue actual.Previous.Value previous
        calls.Clear()
        let expectedWrites=expected.GetProperty("writes")
        Expect.equal writes.Count (expectedWrites.GetArrayLength()) "source write count"
        for index in 0..writes.Count-1 do
            let target,name,value=writes[index]
            let expected=expectedWrites[index]
            Expect.equal (target,name) (expected.GetProperty("target").GetInt32(),expected.GetProperty("property").GetString()) "source write order"
            equalValue value (expected.GetProperty("value"))
        writes.Clear()
        let expectedEvents=expected.GetProperty("events")
        Expect.equal events.Count (expectedEvents.GetArrayLength()) "source callback count"
        for index in 0..events.Count-1 do
            let phase,time,snapshot=events[index]
            let expected=expectedEvents[index]
            Expect.equal phase (expected.GetProperty("event").GetString()) "callback phase"
            close time (expected.GetProperty("time").GetDouble())
            equalTargets snapshot (expected.GetProperty("targets"))
        events.Clear()
    verify(fixture.GetProperty("initial"))
    for sample in fixture.GetProperty("samples").EnumerateArray() do
        animation.Playback.Seek(sample.GetProperty("time").GetDouble())
        verify(sample.GetProperty("state"))

let tests=testList "public sequence input source" [
    for fixture in fixtures() do testCase (fixture.GetProperty("name").GetString()) <| fun _ -> replay fixture
]
