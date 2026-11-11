module Ranvier.Anime.Tests.KeyframeSequenceTests

open System
open System.Collections.Generic
open System.IO
open System.Text.Json
open Expecto
open Ranvier
open Ranvier.Dom
open Ranvier.Anime

let private optional (name:string) (item:JsonElement)=match item.TryGetProperty name with true,value -> Some value | _ -> None
let private number name fallback item=optional name item |> Option.map _.GetDouble() |> Option.defaultValue fallback
let private flag name item=optional name item |> Option.exists _.GetBoolean()
let private reserved=set ["at";"key";"duration";"delay";"ease";"modifier"]
let private modifier=function "double" -> (fun value -> value*2.) | "plusOne" -> (fun value -> value+1.) | _ -> id
let private close actual expected=Expect.isTrue (actual=expected || abs(actual-expected)<=1e-12*Math.Max(1.,abs expected)) "source number"
let private fixtures file =
    let root=Environment.GetEnvironmentVariable "RANVIER_ANIME_REFERENCE_ROOT"
    let root=if String.IsNullOrWhiteSpace root then Path.Combine(AppContext.BaseDirectory,"reference") else root
    use document=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,file)))
    Expect.equal (document.RootElement.GetProperty("upstreamCommit").GetString()) "2c9cf8ea00329f6768c7d7902252ed977d75ce42" "pinned source"
    document.RootElement.GetProperty("fixtures").EnumerateArray()
    |> Seq.filter(fun fixture ->
        let input=fixture.GetProperty("input")
        (optional "rows" input).IsSome || (optional "offsets" input).IsSome)
    |> Seq.map _.Clone() |> Seq.toList

let private sequence (input:JsonElement) : KeyframeSequence<NumericProperty,float> =
    let named (value:JsonElement)=
        value.EnumerateObject() |> Seq.filter(fun property -> not(reserved.Contains property.Name))
        |> Seq.map(fun property -> property.Name,property.Value.GetDouble()) |> Seq.toArray
    match optional "rows" input with
    | Some rows ->
        rows.EnumerateArray()
        |> Seq.map(fun value ->
            let mutable row=KeyframeRow<NumericProperty,float>.Create(named value)
            optional "duration" value |> Option.iter(fun value -> row<-row.WithDurationPrecise(value.GetDouble()))
            optional "delay" value |> Option.iter(fun value -> row<-row.WithDelay(value.GetDouble()))
            optional "ease" value |> Option.iter(fun value -> row<-row.WithEase(Ease.named(value.GetString())))
            optional "modifier" value |> Option.iter(fun value -> row<-row.WithModifier(modifier(value.GetString())))
            row)
        |> Seq.toArray |> KeyframeSequence.Rows
    | None ->
        input.GetProperty("offsets").EnumerateArray()
        |> Seq.map(fun value ->
            let row=KeyframeOffset<NumericProperty,float>.At(value.GetProperty("at").GetDouble(),named value)
            match optional "ease" value with
            | Some value -> row.WithEase(Ease.named(value.GetString()))
            | _ -> row)
        |> Seq.toArray |> KeyframeSequence.Offsets

let private normalize (fixture:JsonElement) =
    let defaults,expected=KeyframeCompilationTests.capture fixture
    let expected=expected |> Array.filter(fun property -> property.Target=0)
    let actual=(sequence(fixture.GetProperty("input"))).Capture defaults.Duration
    Expect.equal (actual |> Array.map fst) (expected |> Array.map _.Name) "property order"
    for property in 0..actual.Length-1 do
        let _,frames=actual[property]
        Expect.equal frames.Length expected[property].Frames.Length "normalized frame count"
        for index in 0..frames.Length-1 do
            let frame=frames[index].Template ValueInterpolation.Number
            let source=expected[property].Frames[index]
            Expect.equal (frame.From |> Option.map _.ToString(),frame.To |> Option.map _.ToString(),frame.Duration,frame.Delay)
                (source.From |> Option.map _.ToString(),source.To |> Option.map _.ToString(),source.Duration,source.Delay) "normalized inputs"
            for progress in [0.;0.25;0.5;1.] do
                close ((defaultArg frame.Ease defaults.Ease) progress) ((defaultArg source.Ease defaults.Ease) progress)
                close ((defaultArg frame.Modifier defaults.Modifier) progress) ((defaultArg source.Modifier defaults.Modifier) progress)

let private replay (fixture:JsonElement) =
    let input=fixture.GetProperty("input")
    let sequence=sequence input
    let definitions=sequence.Capture(input.GetProperty("duration").GetDouble())
    let names=definitions |> Array.map fst
    let originals=
        optional "targets" input
        |> Option.map(fun values -> values.EnumerateArray() |> Seq.map _.Clone() |> Seq.toArray)
        |> Option.defaultWith(fun () -> [|input.GetProperty("original")|])
    let values=originals |> Array.map(fun original ->
        let result=Dictionary<string,float>(StringComparer.Ordinal)
        for property in original.EnumerateObject() do result[property.Name]<-property.Value.GetDouble()
        for name in names do if not(result.ContainsKey name) then result[name]<-0.
        result)
    let writes=ResizeArray<int*string*float>()
    let handles=names |> Array.map(fun name ->
        name,values |> Array.mapi(fun target values ->
            NumericProperty((fun () -> values[name]),(fun value -> values[name]<-value;writes.Add(target,name,value)))))
    let events=ResizeArray<string*float*Dictionary<string,float> array>()
    let callback phase (clock:Timer)=events.Add(phase,clock.CurrentTime,values |> Array.map(fun values -> Dictionary<string,float>(values,StringComparer.Ordinal)))
    let playback=
        {PlaybackOptions.Default with Delay=number "delay" 0. input;Repeat=PlaybackRepeat.Count(uint(number "loop" 0. input))
                                      LoopDelay=number "loopDelay" 0. input;Reversed=flag "reversed" input;Alternate=flag "alternate" input
                                      FrameRate=number "frameRate" 240. input}
    let options=
        {Ranvier.Anime.KeyframeAnimationOptions.Default with Autoplay=false;Playback=playback
                                                             Ease=(optional "ease" input |> Option.map(fun value -> Ease.named(value.GetString())) |> Option.defaultValue id)
                                                             Modifier=(optional "modifier" input |> Option.map _.GetString() |> Option.defaultValue "" |> modifier)
                                                             Composition=(if optional "composition" input |> Option.exists(fun value -> value.GetString()="none") then KeyframeComposition.None else KeyframeComposition.Replace)
                                                             OnBegin=callback "Begin";OnBeforeUpdate=callback "BeforeUpdate";OnRender=callback "Render";OnUpdate=callback "Update"
                                                             OnLoop=callback "Loop";OnPause=callback "Pause";OnComplete=callback "Complete"}
    use scheduler=new ManualFrameScheduler()
    use animation=Keyframes.NumericPrecise(scheduler,handles,sequence,input.GetProperty("duration").GetDouble(),options=options)
    let verifyTargets (actual:Dictionary<string,float> array) (expected:JsonElement) =
        Expect.equal actual.Length (expected.GetArrayLength()) "target count"
        for target in 0..actual.Length-1 do
            for property in expected[target].EnumerateObject() do
                close actual.[target].[property.Name] (property.Value.GetDouble())
    let verify (expected:JsonElement) =
        let clock=animation.Playback
        for name,actual in ["time",clock.CurrentTime;"iterationTime",clock.IterationCurrentTime;"duration",clock.Duration;"iterationDuration",clock.IterationDuration] do
            close actual (expected.GetProperty(name).GetDouble())
        for name,actual in ["paused",clock.Paused;"completed",clock.Completed;"backwards",clock.Backwards] do
            Expect.equal actual (expected.GetProperty(name).GetBoolean()) name
        verifyTargets values (expected.GetProperty("targets"))
        let expectedWrites=expected.GetProperty("writes")
        Expect.equal writes.Count (expectedWrites.GetArrayLength()) "source write count"
        for index in 0..writes.Count-1 do
            let target,name,value=writes[index]
            let expected=expectedWrites[index]
            Expect.equal (target,name) (expected.GetProperty("target").GetInt32(),expected.GetProperty("property").GetString()) "write order"
            close value (expected.GetProperty("value").GetDouble())
        writes.Clear()
        let expectedEvents=expected.GetProperty("events")
        Expect.equal events.Count (expectedEvents.GetArrayLength()) "callback count"
        for index in 0..events.Count-1 do
            let phase,time,snapshot=events[index]
            let expected=expectedEvents[index]
            Expect.equal phase (expected.GetProperty("event").GetString()) "callback phase"
            close time (expected.GetProperty("time").GetDouble())
            verifyTargets snapshot (expected.GetProperty("targets"))
        events.Clear()
    verify(fixture.GetProperty("initial"))
    for sample in fixture.GetProperty("samples").EnumerateArray() do
        animation.Playback.Seek(sample.GetProperty("time").GetDouble())
        verify(sample.GetProperty("state"))

let normalizationTests=testList "public sequence normalization" [
    for fixture in fixtures "keyframes.json" do testCase (fixture.GetProperty("name").GetString()) <| fun _ -> normalize fixture
]
let playbackTests=testList "public sequence playback" [
    for fixture in fixtures "keyframe-playback.json" do testCase (fixture.GetProperty("name").GetString()) <| fun _ -> replay fixture
]
