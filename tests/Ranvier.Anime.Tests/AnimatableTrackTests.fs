module Ranvier.Anime.Tests.AnimatableTrackTests

open System
open System.IO
open System.Text.Json
open System.Collections.Generic
open Expecto
open Ranvier
open Ranvier.Dom
open Ranvier.Anime
open Ranvier.Anime.ValueInterpolation

let private trackOptional name (item:JsonElement) =
    match item.TryGetProperty(name:string) with true,value -> Some value | _ -> None
let private trackNumber name fallback item = trackOptional name item |> Option.map _.GetDouble() |> Option.defaultValue fallback
let private trackFlag name item = trackOptional name item |> Option.exists _.GetBoolean()
let private trackLiteral (item:JsonElement) = if item.ValueKind=JsonValueKind.Number then Number(item.GetDouble()) else Text(item.GetString())
let private trackEqualLiteral expected actual =
    match expected,actual with
    | Number expected,Number actual -> Expect.isTrue (expected=actual || abs(expected-actual)<=1e-12*max 1. (abs expected)) "source scalar"
    | _ -> Expect.equal actual expected "source text"
let private trackCases () =
    let root=Environment.GetEnvironmentVariable "RANVIER_ANIME_REFERENCE_ROOT"
    let root=if String.IsNullOrWhiteSpace root then Path.Combine(AppContext.BaseDirectory,"reference") else root
    use doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"animatable.json")))
    Expect.equal (doc.RootElement.GetProperty("upstreamCommit").GetString()) "2c9cf8ea00329f6768c7d7902252ed977d75ce42" "pinned source"
    doc.RootElement.GetProperty("fixtures").EnumerateArray() |> Seq.map _.Clone() |> Seq.toList
let private trackReplay (fixture:JsonElement) =
    let parameters=fixture.GetProperty("parameters")
    let originals=fixture.GetProperty("targets")
    let targetValues=Dictionary<struct(string*int),Literal>()
    let writes=ResizeArray<string*int*Literal>()
    let tracks=Dictionary<string,AnimatableTrack>()
    use scheduler=new ManualFrameScheduler()
    use cleanup={new IDisposable with member _.Dispose()=for track in tracks.Values do track.Cancel()}
    for target in 0..originals.GetArrayLength()-1 do
        for property in originals[target].EnumerateObject() do targetValues.Add(struct(property.Name,target),trackLiteral property.Value)
    let initial=fixture.GetProperty("initial")
    for property in initial.GetProperty("state").GetProperty("properties").EnumerateObject() do
        let parameter=parameters.GetProperty property.Name
        let nested=parameter.ValueKind=JsonValueKind.Object
        let number name fallback =
            if nested then trackNumber name (trackNumber name fallback parameters) parameter
            else trackNumber name fallback parameters
        let duration=if nested then number "duration" 1000. else trackNumber "duration" (parameter.GetDouble()) parameters
        let unit=if nested then trackOptional "unit" parameter |> Option.map _.GetString() else None
        let easeName=
            let globalEase=trackOptional "ease" parameters |> Option.map _.GetString() |> Option.defaultValue "outQuad"
            if nested then trackOptional "ease" parameter |> Option.map _.GetString() |> Option.defaultValue globalEase else globalEase
        let modifierName=if property.Name="x" then trackOptional "propertyModifier" fixture |> Option.orElseWith(fun () -> trackOptional "modifier" fixture) else trackOptional "modifier" fixture
        let modifier=match modifierName |> Option.map _.GetString() with Some "double" -> (fun value -> value*2.) | Some "plusOne" -> (fun value -> value+1.) | _ -> id
        let repeat=
            match trackOptional "loop" parameters with
            | Some value when value.ValueKind=JsonValueKind.True -> PlaybackRepeat.Forever
            | Some value -> PlaybackRepeat.Count(value.GetUInt32())
            | _ -> PlaybackRepeat.Count 0u
        let options={PlaybackOptions.Default with Delay=number "delay" 0.;LoopDelay=number "loopDelay" 0.;Repeat=repeat;
                                                 Alternate=trackFlag "alternate" parameters;Reversed=trackFlag "reversed" parameters}
        let kernels=Array.init (originals.GetArrayLength()) (fun index -> AnimatableValue(targetValues[struct(property.Name,index)],unit,false,None))
        let write index (value:Output) =
            let literal=match value.Scalar with Some number -> Number number | None -> Text value.Text
            targetValues[struct(property.Name,index)]<-literal
            writes.Add(property.Name,index,literal)
        let track=new AnimatableTrack(scheduler,kernels,write,duration,options,Ease.named easeName,modifier,4)
        tracks.Add(property.Name,track)
        track.Initialize()
    let verify (state:JsonElement) (expectedWrites:JsonElement) =
        Expect.equal writes.Count (expectedWrites.GetArrayLength()) "source write count"
        for index in 0..writes.Count-1 do
            let name,target,value=writes[index]
            let expected=expectedWrites[index]
            Expect.equal name (expected.GetProperty("property").GetString()) "write property"
            Expect.equal target (expected.GetProperty("target").GetInt32()) "write target"
            trackEqualLiteral (trackLiteral(expected.GetProperty("value"))) value
        let targets=state.GetProperty("targets")
        for property in state.GetProperty("properties").EnumerateObject() do
            let track=tracks[property.Name]
            let clock=track.Clock
            let expected=property.Value
            for name,actual in ["time",clock.CurrentTime;"rawTime",clock.Sample.AbsoluteTime;"duration",clock.Duration
                                "iterationDuration",clock.IterationDuration;"delay",clock.Delay;"loopDelay",clock.LoopDelay] do
                Expect.equal actual (expected.GetProperty(name).GetDouble()) name
            for name,actual in ["began",clock.Began;"paused",clock.Paused;"completed",clock.Completed;"reversed",clock.Reversed] do
                Expect.equal actual (expected.GetProperty(name).GetBoolean()) name
            for target in 0..targets.GetArrayLength()-1 do
                trackEqualLiteral (trackLiteral(targets[target].GetProperty(property.Name))) targetValues[struct(property.Name,target)]
            let getter=expected.GetProperty("value")
            if getter.ValueKind=JsonValueKind.Number then
                let actual=track.ScalarValue 0 |> Option.get
                trackEqualLiteral (Number(getter.GetDouble())) (Number actual)
            else
                let expected=getter.EnumerateArray() |> Seq.map _.GetDouble() |> Seq.toArray
                let actual=track.Components 0
                Expect.equal actual.Length expected.Length "getter component count"
                Array.iter2 (fun expected actual -> trackEqualLiteral (Number expected) (Number actual)) expected actual
    verify (initial.GetProperty("state")) (initial.GetProperty("writes"))
    for sample in fixture.GetProperty("samples").EnumerateArray() |> Seq.takeWhile(fun sample -> sample.GetProperty("step").GetProperty("op").GetString()<>"revert") do
        writes.Clear()
        let step=sample.GetProperty("step")
        let property=trackOptional "property" step |> Option.map _.GetString() |> Option.defaultValue ""
        match step.GetProperty("op").GetString() with
        | "retarget" ->
            let duration=trackOptional "duration" step |> Option.map _.GetDouble()
            let ease=trackOptional "ease" step |> Option.map(fun name -> Ease.named(name.GetString()))
            let value=step.GetProperty("value")
            if value.ValueKind=JsonValueKind.Array then
                tracks[property].RetargetComponents(value.EnumerateArray() |> Seq.map _.GetDouble() |> Seq.toArray,?newDuration=duration,?newEase=ease)
            else tracks[property].RetargetScalar(value.GetDouble(),?newDuration=duration,?newEase=ease)
        | "frame" -> scheduler.Advance(step.GetProperty("after").GetDouble());scheduler.RunFrame()
        | "external" ->
            let target=step.GetProperty("target").GetInt32()
            let value=trackLiteral(step.GetProperty("value"))
            targetValues[struct(property,target)]<-value
            writes.Add(property,target,value)
        | "seek" -> tracks[property].Clock.Seek(step.GetProperty("time").GetDouble())
        | "pause" -> tracks[property].Clock.Pause()
        | "resume" -> tracks[property].Clock.Resume()
        | "complete" -> tracks[property].Clock.Complete()
        | op -> failtest ("Unknown track operation "+op)
        verify (sample.GetProperty("state")) (sample.GetProperty("writes"))

let trackTests = testList "animatable shared track" [
    testCase "inherited setter cancellation prevents publishing retarget endpoints" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        let writes=ResizeArray<float>()
        let mutable active:AnimatableTrack option=None
        let mutable calls=0
        let mutable armed=false
        let mutable parentLive=true
        let modifier value =
            if armed then
                calls<-calls+1
                if calls=1 then active |> Option.iter(fun track -> track.RetargetScalar 100.)
                elif calls=2 then parentLive<-false;armed<-false
            value
        use track=new AnimatableTrack(scheduler,[|AnimatableValue(Number 10.,None,false,None)|],
                                      (fun _ output -> writes.Add(Option.get output.Scalar)),100.,PlaybackOptions.Default,id,modifier,4)
        active<-Some track
        track.Initialize()
        writes.Clear()
        armed<-true
        track.Clock.SetTimeGuarded(50.,fun () -> parentLive)
        Expect.equal calls 2 "outer render then nested preparation"
        Expect.isEmpty writes "cancelled ancestor prevents dispatch"
        track.Clock.Seek 100.
        Expect.equal writes[writes.Count-1] 10. "unpublished destination stays discarded"
    for fixture in trackCases() do
        let name=fixture.GetProperty("name").GetString()
        if not(["componentAlias";"noTargets";"emptyProperties"] |> List.contains name) then
            testCase ("source "+name) <| fun _ -> trackReplay fixture
    testCase "multi-target preparation discards earlier staged endpoints after a later pause" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        let writes=ResizeArray<int*float>()
        let mutable active:AnimatableTrack option=None
        let mutable calls=0
        let mutable armed=false
        let modifier value =
            if armed then
                calls<-calls+1
                if calls=2 then
                    armed<-false
                    active |> Option.iter(fun track -> track.Clock.Pause())
            value
        let kernels=[|AnimatableValue(Number 10.,None,false,None);AnimatableValue(Number 20.,None,false,None)|]
        use track=new AnimatableTrack(scheduler,kernels,(fun index output -> writes.Add(index,Option.get output.Scalar)),100.,
                                      PlaybackOptions.Default,id,modifier,4)
        active<-Some track
        track.Initialize()
        writes.Clear()
        armed<-true
        track.RetargetScalar 100.
        Expect.equal calls 2 "pause in later target"
        Expect.isEmpty writes "no cache-reset writes"
        track.Clock.Seek 100.
        Expect.sequenceEqual writes [0,10.;1,20.] "staged endpoints were not published"
    testCase "normal seek inside soft reset gets its own dispatch context" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        let writes=ResizeArray<float>()
        let mutable active:AnimatableTrack option=None
        let mutable calls=0
        let mutable armed=false
        let modifier value =
            if armed then
                calls<-calls+1
                if calls=2 then
                    armed<-false
                    active |> Option.iter(fun track -> track.Clock.Seek 75.)
            value
        use track=new AnimatableTrack(scheduler,[|AnimatableValue(Number 0.,None,false,None)|],
                                      (fun _ output -> writes.Add(Option.get output.Scalar)),100.,PlaybackOptions.Default,id,modifier,4)
        active<-Some track
        track.Initialize()
        writes.Clear()
        armed<-true
        track.RetargetScalar 100.
        Expect.sequenceEqual writes [75.] "inner seek writes despite outer cache-only context"
        Expect.isTrue track.Clock.Paused "inner seek superseded outer resume"
        Expect.isFalse track.Clock.InternalRender "render context restored"
        track.Clock.Seek 50.
        Expect.sequenceEqual writes [75.;50.] "later normal seek still dispatches"
    testCase "owner disposal inside soft reset prevents dispatch and future frames" <| fun _ ->
        use graph=new Graph()
        use activation=graph.Activate()
        use scheduler=new ManualFrameScheduler()
        let writes=ResizeArray<float>()
        let mutable armed=false
        let mutable calls=0
        let owner,track=Api.createRoot(fun owner ->
            let modifier value =
                if armed then
                    calls<-calls+1
                    if calls=2 then owner.Dispose()
                value
            let track=new AnimatableTrack(scheduler,[|AnimatableValue(Number 10.,None,false,None)|],
                                          (fun _ output -> writes.Add(Option.get output.Scalar)),100.,PlaybackOptions.Default,id,modifier,4)
            Timer.own track.Clock |> ignore
            track.Initialize()
            owner,track)
        use track=track
        writes.Clear()
        armed<-true
        track.RetargetScalar 100.
        Expect.equal calls 2 "preparation then soft reset"
        Expect.isTrue track.Clock.Cancelled "real owner ended playback"
        Expect.isEmpty writes "cancelled reset cannot dispatch"
        scheduler.Advance 100.
        scheduler.RunFrame()
        Expect.isEmpty writes "no future writes"
        Expect.isFalse track.Clock.InternalRender "context restored after disposal"
        owner.Dispose()
    testCase "invalid retarget duration preserves active playback and avoids modifiers" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        let writes=ResizeArray<float>()
        let mutable calls=0
        let modifier value = calls<-calls+1;value
        use track=new AnimatableTrack(scheduler,[|AnimatableValue(Number 0.,None,false,None)|],
                                      (fun _ output -> writes.Add(Option.get output.Scalar)),100.,PlaybackOptions.Default,id,modifier,4)
        track.Initialize()
        track.RetargetScalar 100.
        let before=calls
        for duration in [-1.;nan;infinity] do
            Expect.throwsT<ArgumentException> (fun () -> track.RetargetScalar(200.,newDuration=duration)) "duration validation"
        Expect.equal calls before "validation precedes modifier"
        scheduler.Advance 25.
        scheduler.RunFrame()
        Expect.equal writes[writes.Count-1] 37. "original target and frame retained"
    testCase "mixed target shapes reject before interrupting playback" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        let mutable calls=0
        use track=new AnimatableTrack(scheduler,[|AnimatableValue(Number 0.,None,false,None);AnimatableValue(Text "v(0,10)",None,false,None)|],
                                      (fun _ _ -> calls<-calls+1),100.,PlaybackOptions.Default,id,id,4)
        track.Initialize()
        let before=calls
        Expect.throwsT<ArgumentException> (fun () -> track.RetargetScalar 100.) "all target shapes checked"
        Expect.isFalse track.Clock.Cancelled "invalid shape preserves lifetime"
        Expect.equal calls before "no dispatch"
    testCase "staged target forks retain normalized units without new measurements" <| fun _ ->
        let mutable conversions=0
        let convert value _ _ = conversions<-conversions+1;value*32.
        let kernel=AnimatableValue(Text "1em",Some "px",true,Some convert)
        let fork=kernel.Fork()
        fork.RetargetScalar(100.,id,(fun () -> true)) |> ignore
        Expect.equal ((fork.Sample(1.,4,id,(fun () -> true)) |> Option.get).Text) "100px" "fork destination"
        Expect.equal ((kernel.Sample(0.,4,id,(fun () -> true)) |> Option.get).Text) "32px" "original independent cache"
        Expect.equal conversions 1 "normalization retained"
]
