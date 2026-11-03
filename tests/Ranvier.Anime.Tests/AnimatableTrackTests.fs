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
let private trackReplayWithGroup useGroup (fixture:JsonElement) =
    let parameters=fixture.GetProperty("parameters")
    let originals=fixture.GetProperty("targets")
    let targetValues=Dictionary<struct(string*int),Literal>()
    let writes=ResizeArray<string*int*Literal>()
    let tracks=Dictionary<string,AnimatableTrack>()
    use scheduler=new ManualFrameScheduler()
    let mutable checking=true
    use cleanup={new IDisposable with member _.Dispose()=checking<-false;for track in tracks.Values do track.Cancel()}
    let mutable expectedEvents:JsonElement array=[||]
    let mutable eventIndex=0
    let verifyClock (expected:JsonElement) (clock:Timer) =
        for name,actual in ["time",clock.CurrentTime;"rawTime",clock.Sample.AbsoluteTime;"duration",clock.Duration
                            "iterationDuration",clock.IterationDuration;"delay",clock.Delay;"loopDelay",clock.LoopDelay] do
            Expect.equal actual (expected.GetProperty(name).GetDouble()) name
        for name,actual in ["began",clock.Began;"paused",clock.Paused;"completed",clock.Completed;"reversed",clock.Reversed] do
            Expect.equal actual (expected.GetProperty(name).GetBoolean()) name
    let verifyTargets (expected:JsonElement) =
        for target in 0..expected.GetArrayLength()-1 do
            for property in expected[target].EnumerateObject() do
                trackEqualLiteral (trackLiteral property.Value) targetValues[struct(property.Name,target)]
    let callback phase (animation:NumericAnimation) =
        if checking then
            Expect.isLessThan eventIndex expectedEvents.Length "unexpected group callback"
            let expected=expectedEvents[eventIndex]
            Expect.equal phase (expected.GetProperty("event").GetString()) "group callback phase"
            verifyClock (expected.GetProperty("state")) animation.PlaybackClock
            verifyTargets (expected.GetProperty("targets"))
            eventIndex<-eventIndex+1
    let group=
        if useGroup then Some(new AnimatableGroup(scheduler,onBegin=callback "Begin",onBeforeUpdate=callback "BeforeUpdate",
                                                  onRender=callback "Render",onUpdate=callback "Update",onLoop=callback "Loop",
                                                  onPause=callback "Pause",onComplete=callback "Complete"))
        else None
    use groupCleanup={new IDisposable with member _.Dispose()=checking<-false;group |> Option.iter(fun group -> group.Cancel())}
    for target in 0..originals.GetArrayLength()-1 do
        for property in originals[target].EnumerateObject() do targetValues.Add(struct(property.Name,target),trackLiteral property.Value)
    let initial=fixture.GetProperty("initial")
    expectedEvents<-initial.GetProperty("events").EnumerateArray() |> Seq.toArray
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
        let track=
            match group with
            | Some group -> group.Add(kernels,write,duration,options,Ease.named easeName,modifier,4)
            | None -> new AnimatableTrack(scheduler,kernels,write,duration,options,Ease.named easeName,modifier,4)
        tracks.Add(property.Name,track)
        if not useGroup then track.Initialize()
    let verify (state:JsonElement) (expectedWrites:JsonElement) =
        group |> Option.iter(fun group ->
            Expect.equal eventIndex expectedEvents.Length "group callback count"
            verifyClock (state.GetProperty("callbacks")) group.Callbacks.PlaybackClock)
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
        expectedEvents<-sample.GetProperty("events").EnumerateArray() |> Seq.toArray
        eventIndex<-0
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
            testCase ("source "+name) <| fun _ -> trackReplayWithGroup false fixture
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


let groupTests = testList "animatable group callbacks" [
    testCase "root disposal releases only pending timeout and microtask registrations" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        let host=scheduler:>FrameScheduler
        let mutable releases=0
        let counted (lease:IDisposable)={new IDisposable with member _.Dispose()=releases<-releases+1;lease.Dispose()}
        let monitor=
            {new FrameScheduler with
                member _.Now()=host.Now()
                member _.Frame callback=host.Frame callback
                member _.Timeout(delay,callback)=counted(host.Timeout(delay,callback))
                member _.Microtask callback=counted(host.Microtask callback)
                member _.Dispose()=()}
        let queue=AnimatableFrameQueue(monitor)
        let root=queue.CreateScheduler()
        let mutable calls=0
        root.Microtask(fun () -> calls<-calls+1) |> ignore
        scheduler.RunMicrotasks()
        root.Timeout(5.,fun () -> calls<-calls+1) |> ignore
        scheduler.Advance 5.
        let cancelled=root.Timeout(50.,fun () -> calls<-calls+1)
        cancelled.Dispose()
        root.Timeout(100.,fun () -> calls<-calls+1) |> ignore
        root.Dispose()
        Expect.equal releases 2 "completed and previously disposed work is no longer registered"
        scheduler.Advance 200.
        scheduler.RunMicrotasks()
        Expect.equal calls 2 "only completed work ran"
    testCase "callback controller retains identity when another property restarts a completed clock" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        use group=new AnimatableGroup(scheduler)
        let first=group.Add([|AnimatableValue(Number 0.,None,false,None)|],(fun _ _ -> ()),2000.,PlaybackOptions.Default,id,id,4)
        let second=group.Add([|AnimatableValue(Number 0.,None,false,None)|],(fun _ _ -> ()),100.,PlaybackOptions.Default,id,id,4)
        let callbacks=group.Callbacks
        first.RetargetScalar 100.
        scheduler.Advance 25.
        scheduler.RunFrame()
        scheduler.Advance 1100.
        scheduler.RunFrame()
        Expect.isTrue callbacks.Completed "independent callback duration"
        Expect.isFalse first.Clock.Completed "long property continues"
        second.RetargetScalar 100.
        scheduler.Advance 25.
        scheduler.RunFrame()
        Expect.isTrue (obj.ReferenceEquals(callbacks,group.Callbacks)) "retained callback object"
        Expect.isFalse callbacks.Completed "callback clock reset"
    testCase "owner disposal in a group begin callback stops later phases and frames" <| fun _ ->
        use graph=new Graph()
        use activation=graph.Activate()
        use scheduler=new ManualFrameScheduler()
        let mutable begins=0
        let mutable updates=0
        let mutable writes=0
        let owner,group,first,second=Api.createRoot(fun owner ->
            let group=new AnimatableGroup(scheduler,onBegin=(fun _ -> begins<-begins+1;owner.Dispose()),onUpdate=(fun _ -> updates<-updates+1))
            Api.getOwner().Attach(group:>IDisposable)
            let add ()=group.Add([|AnimatableValue(Number 0.,None,false,None)|],(fun _ _ -> writes<-writes+1),100.,PlaybackOptions.Default,id,id,4)
            owner,group,add(),add())
        use group=group
        first.RetargetScalar 100.
        second.RetargetScalar 100.
        scheduler.Advance 25.
        scheduler.RunFrame()
        let stopped=writes
        scheduler.Advance 100.
        scheduler.RunFrame()
        Expect.equal begins 1 "one begin callback"
        Expect.equal updates 0 "same callback tick interrupted"
        Expect.equal writes stopped "no future property writes"
        Expect.isTrue (group.Cancelled && first.Clock.Cancelled && second.Clock.Cancelled) "all owned lifetimes end"
        owner.Dispose()
    testCase "property callback failure cancels every group member and preserves the primary exception" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        let primary=InvalidOperationException "group begin failure"
        use group=new AnimatableGroup(scheduler,onBegin=(fun _ -> raise primary))
        let add ()=group.Add([|AnimatableValue(Number 0.,None,false,None)|],(fun _ _ -> ()),100.,PlaybackOptions.Default,id,id,4)
        let first,second=add(),add()
        first.RetargetScalar 100.
        second.RetargetScalar 100.
        scheduler.Advance 25.
        let mutable actual:exn option=None
        try scheduler.RunFrame() with error -> actual<-Some error
        Expect.isTrue (actual |> Option.exists(fun error -> obj.ReferenceEquals(error,primary))) "fault identity"
        Expect.isTrue (group.Cancelled && first.Clock.Cancelled && second.Clock.Cancelled) "fault closes all members"
    testCase "an empty group has an unstarted independent callback controller" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        use group=new AnimatableGroup(scheduler)
        Expect.equal group.Count 0 "empty track collection"
        Expect.equal group.Callbacks.Duration 1000. "callback duration"
        Expect.isTrue group.Callbacks.PlaybackClock.Paused "unstarted"
        group.Cancel()
        Expect.throwsT<ObjectDisposedException> (fun () -> group.Add([|AnimatableValue(Number 0.,None,false,None)|],(fun _ _ -> ()),100.,PlaybackOptions.Default,id,id,4) |> ignore) "terminal group lifetime"
    testCase "root adapters preserve order and keep the host scheduler alive after disposal" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        let queue=AnimatableFrameQueue(scheduler)
        let first,second,later=queue.CreateScheduler(),queue.CreateScheduler(),queue.CreateScheduler()
        let events=ResizeArray<string>()
        let rec tickFirst _ =
            events.Add "first"
            first.Frame tickFirst |> ignore
            later.Frame(fun _ -> events.Add "later") |> ignore
        first.Frame tickFirst |> ignore
        second.Frame(fun _ -> events.Add "second") |> ignore
        scheduler.Advance 25.
        scheduler.RunFrame()
        Expect.sequenceEqual events ["first";"second";"later"] "cached-next traversal"
        first.Dispose()
        second.Dispose()
        later.Dispose()
        (scheduler:>FrameScheduler).Frame(fun _ -> events.Add "host") |> ignore
        scheduler.Advance 25.
        scheduler.RunFrame()
        Expect.equal events[events.Count-1] "host" "host remains usable"
    testCase "disposing an old frame lease cannot cancel its replacement" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        let queue=AnimatableFrameQueue(scheduler)
        use root=queue.CreateScheduler()
        let events=ResizeArray<string>()
        let old=root.Frame(fun _ -> events.Add "stale")
        root.Frame(fun _ -> events.Add "current") |> ignore
        old.Dispose()
        scheduler.Advance 25.
        scheduler.RunFrame()
        Expect.sequenceEqual events ["current"] "new request survives old lease"

    for fixture in trackCases() do
        let name=fixture.GetProperty("name").GetString()
        if not(["componentAlias";"noTargets";"emptyProperties"] |> List.contains name) then
            testCase ("source group "+name) <| fun _ -> trackReplayWithGroup true fixture
]
