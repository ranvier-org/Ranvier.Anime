module Ranvier.Anime.Tests.AnimatableNumberTests

open System
open System.IO
open System.Text.Json
open System.Collections.Generic
open Expecto
open Ranvier
open Ranvier.Dom
open Ranvier.Anime

let private option name (item:JsonElement) =
    match item.TryGetProperty(name:string) with true,value -> Some value | _ -> None
let private number name fallback item = option name item |> Option.map _.GetDouble() |> Option.defaultValue fallback
let private flag name item = option name item |> Option.exists _.GetBoolean()
let private close expected actual =
    Expect.isTrue (expected=actual || abs(expected-actual)<=1e-12*max 1. (abs expected)) "source numeric value"
let private cases () =
    let root=Environment.GetEnvironmentVariable "RANVIER_ANIME_REFERENCE_ROOT"
    let root=if String.IsNullOrWhiteSpace root then Path.Combine(AppContext.BaseDirectory,"reference") else root
    use doc=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"animatable.json")))
    Expect.equal (doc.RootElement.GetProperty("upstreamCommit").GetString()) "2c9cf8ea00329f6768c7d7902252ed977d75ce42" "pinned source"
    doc.RootElement.GetProperty("fixtures").EnumerateArray()
    |> Seq.filter(fun fixture ->
        let targets=fixture.GetProperty("targets")
        targets.GetArrayLength()>0 &&
        (targets.EnumerateArray() |> Seq.forall(fun target -> target.EnumerateObject() |> Seq.forall(fun property -> property.Value.ValueKind=JsonValueKind.Number))))
    |> Seq.map _.Clone() |> Seq.toList
let private replay (fixture:JsonElement) =
    use scheduler=new ManualFrameScheduler()
    let parameters=fixture.GetProperty("parameters")
    let values=fixture.GetProperty("targets").EnumerateArray() |> Seq.map(fun target ->
        let properties=Dictionary<string,float>()
        for property in target.EnumerateObject() do properties.Add(property.Name,property.Value.GetDouble())
        properties) |> Seq.toArray
    let writes=ResizeArray<string*int*float>()
    let handles=Dictionary<string,AnimatableNumber>()
    let mutable checking=true
    let mutable expectedEvents:JsonElement array=[||]
    let mutable eventIndex=0
    let verifyClock (expected:JsonElement) (clock:Timer) =
        for name,actual in ["time",clock.CurrentTime;"rawTime",clock.Sample.AbsoluteTime;"duration",clock.Duration
                            "iterationDuration",clock.IterationDuration;"delay",clock.Delay;"loopDelay",clock.LoopDelay] do
            Expect.equal actual (expected.GetProperty(name).GetDouble()) name
        for name,actual in ["began",clock.Began;"paused",clock.Paused;"completed",clock.Completed;"reversed",clock.Reversed] do
            Expect.equal actual (expected.GetProperty(name).GetBoolean()) name
    let verifyTargets (expected:JsonElement) =
        for index in 0..values.Length-1 do
            for property in expected[index].EnumerateObject() do close (property.Value.GetDouble()) (values[index][property.Name])
    let callback phase (animation:NumericAnimation) =
        if checking then
            Expect.isLessThan eventIndex expectedEvents.Length "unexpected callback"
            let expected=expectedEvents[eventIndex]
            Expect.equal phase (expected.GetProperty("event").GetString()) "callback phase"
            verifyClock (expected.GetProperty("state")) animation.PlaybackClock
            verifyTargets (expected.GetProperty("targets"))
            eventIndex<-eventIndex+1
    use group=new Animatable(scheduler,onBegin=callback "Begin",onBeforeUpdate=callback "BeforeUpdate",onRender=callback "Render",
                             onUpdate=callback "Update",onLoop=callback "Loop",onPause=callback "Pause",onComplete=callback "Complete")
    use cleanup={new IDisposable with member _.Dispose()=checking<-false}
    let initial=fixture.GetProperty("initial")
    expectedEvents<-initial.GetProperty("events").EnumerateArray() |> Seq.toArray
    for property in initial.GetProperty("state").GetProperty("properties").EnumerateObject() do
        let name=property.Name
        let parameter=parameters.GetProperty name
        let nested=parameter.ValueKind=JsonValueKind.Object
        let input key fallback = if nested then number key (number key fallback parameters) parameter else number key fallback parameters
        let duration=if nested then input "duration" 1000. else number "duration" (parameter.GetDouble()) parameters
        let globalEase=option "ease" parameters |> Option.map _.GetString() |> Option.defaultValue "outQuad"
        let ease=if nested then option "ease" parameter |> Option.map _.GetString() |> Option.defaultValue globalEase else globalEase
        let modifierName=if name="x" then option "propertyModifier" fixture |> Option.orElseWith(fun () -> option "modifier" fixture) else option "modifier" fixture
        let modifier=match modifierName |> Option.map _.GetString() with Some "double" -> (fun value -> value*2.) | Some "plusOne" -> (fun value -> value+1.) | _ -> id
        let repeat=match option "loop" parameters with Some value when value.ValueKind=JsonValueKind.True -> PlaybackRepeat.Forever | Some value -> PlaybackRepeat.Count(value.GetUInt32()) | _ -> PlaybackRepeat.Count 0u
        let playback={PlaybackOptions.Default with Delay=input "delay" 0.;LoopDelay=input "loopDelay" 0.;Repeat=repeat;
                                                  Alternate=flag "alternate" parameters;Reversed=flag "reversed" parameters}
        let options={AnimatablePropertyOptions.Default with Playback=playback;Ease=Ease.named ease;Modifier=modifier}
        let targets=values |> Array.mapi(fun index target ->
            NumericProperty((fun () -> target[name]),(fun value -> target[name]<-value;writes.Add(name,index,value))))
        handles.Add(name,group.NumericPrecise(targets,duration,options=options))
    let verify (sample:JsonElement) =
        let state=sample.GetProperty("state")
        Expect.equal eventIndex expectedEvents.Length "callback count"
        verifyClock (state.GetProperty("callbacks")) group.Callbacks.PlaybackClock
        let expectedWrites=sample.GetProperty("writes")
        Expect.equal writes.Count (expectedWrites.GetArrayLength()) "write count"
        for index in 0..writes.Count-1 do
            let name,target,value=writes[index]
            let expected=expectedWrites[index]
            Expect.equal name (expected.GetProperty("property").GetString()) "write property"
            Expect.equal target (expected.GetProperty("target").GetInt32()) "write target"
            close (expected.GetProperty("value").GetDouble()) value
        verifyTargets (state.GetProperty("targets"))
        for property in state.GetProperty("properties").EnumerateObject() do
            verifyClock property.Value handles[property.Name].Playback
            close (property.Value.GetProperty("value").GetDouble()) handles[property.Name].Value
        Expect.equal group.Count (state.GetProperty("properties").EnumerateObject() |> Seq.length) "registered handle count"
    verify initial
    for sample in fixture.GetProperty("samples").EnumerateArray() do
        writes.Clear()
        expectedEvents<-sample.GetProperty("events").EnumerateArray() |> Seq.toArray
        eventIndex<-0
        let step=sample.GetProperty("step")
        let property=option "property" step |> Option.map _.GetString() |> Option.defaultValue ""
        match step.GetProperty("op").GetString() with
        | "retarget" ->
            let duration=option "duration" step |> Option.map _.GetDouble()
            let ease=option "ease" step |> Option.map(fun value -> Ease.named(value.GetString()))
            let action ()=handles[property].ToPrecise(step.GetProperty("value").GetDouble(),?duration=duration,?ease=ease)
            if group.Cancelled then Expect.throwsT<ObjectDisposedException> action "native terminal handle adaptation" else action()
        | "frame" -> scheduler.Advance(step.GetProperty("after").GetDouble());scheduler.RunFrame()
        | "external" ->
            let target=step.GetProperty("target").GetInt32()
            let value=step.GetProperty("value").GetDouble()
            values[target][property]<-value
            writes.Add(property,target,value)
        | "seek" -> handles[property].Playback.Seek(step.GetProperty("time").GetDouble())
        | "pause" -> handles[property].Playback.Pause()
        | "resume" -> handles[property].Playback.Resume()
        | "complete" -> handles[property].Playback.Complete()
        | "revert" -> group.Revert()
        | op -> failtest ("Unknown numeric facade operation "+op)
        verify sample

let numericFacadeTests = testList "public numeric animatable" [
    for fixture in cases() do
        testCase ("source "+fixture.GetProperty("name").GetString()) <| fun _ -> replay fixture
    testCase "unsigned and precise inputs retain changed easing and duration" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        use group=new Animatable(scheduler)
        let mutable value=0.
        let handle=group.Numeric(NumericProperty((fun () -> value),(fun next -> value<-next)),duration=100u)
        handle.To(100.,duration=200u,ease=id)
        handle.Playback.Seek 100.
        Expect.equal value 50. "unsigned midpoint"
        handle.ToPrecise(150.,duration=0.125)
        handle.Playback.Seek 0.0625
        Expect.equal value 100. "fractional midpoint"
        handle.To 200.
        Expect.equal handle.Playback.Duration 0.125 "omitted duration retained"
    testCase "revert restores each original target while retaining unrelated state" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        use group=new Animatable(scheduler)
        let values=[|10.;20.|]
        let mutable unrelated=30.
        let targets=values |> Array.mapi(fun index _ -> NumericProperty((fun () -> values[index]),(fun value -> values[index]<-value)))
        let handle=group.Numeric(targets,duration=100u,options={AnimatablePropertyOptions.Default with Ease=id})
        handle.To 100.
        handle.Playback.Seek 50.
        unrelated<-900.
        group.Revert()
        Expect.sequenceEqual values [10.;20.] "captured originals"
        Expect.equal unrelated 900. "unrelated edit survives"
        Expect.equal group.Count 0 "released registrations"
        Expect.isTrue handle.Playback.Cancelled "terminal property"
        Expect.throwsT<ObjectDisposedException> (fun () -> handle.To 200.) "terminal handle"
        group.Revert()
    testCase "owner disposal during group begin stops every public handle" <| fun _ ->
        use graph=new Graph()
        use activation=graph.Activate()
        use scheduler=new ManualFrameScheduler()
        let mutable writes=0
        let owner,group,handle,other=Api.createRoot(fun owner ->
            let group=new Animatable(scheduler,onBegin=(fun _ -> owner.Dispose())) |> Animatable.own
            let handle=group.Numeric(NumericProperty((fun () -> 0.),(fun _ -> writes<-writes+1)),duration=100u)
            let other=group.Numeric(NumericProperty((fun () -> 0.),(fun _ -> writes<-writes+1)),duration=100u)
            owner,group,handle,other)
        use group=group
        handle.To 100.
        other.To 100.
        scheduler.Advance 25.
        scheduler.RunFrame()
        let stopped=writes
        scheduler.Advance 100.
        scheduler.RunFrame()
        Expect.equal writes stopped "owner prevents later writes"
        Expect.isTrue (owner.IsDisposed && group.Cancelled && handle.Playback.Cancelled && other.Playback.Cancelled) "owned cancellation"
        owner.Dispose()
    testCase "cancellation during restore prevents later borrowed writes" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        use group=new Animatable(scheduler)
        let mutable armed=false
        let values=[|10.;20.|]
        let targets=values |> Array.mapi(fun index _ -> NumericProperty((fun () -> values[index]),(fun value ->
            values[index]<-value
            if armed && index=0 then group.Cancel())))
        let handle=group.Numeric(targets,duration=100u)
        handle.To 100.
        handle.Playback.Seek 50.
        let second=values[1]
        armed<-true
        group.Revert()
        Expect.equal values[1] second "later target restoration suppressed"
        Expect.isTrue handle.Playback.Cancelled "all drivers closed"
    testCase "restore failure preserves its exception while finishing other cleanup" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        use group=new Animatable(scheduler)
        let primary=InvalidOperationException "restore failed"
        let mutable armed=false
        let values=[|10.;20.|]
        let targets=values |> Array.mapi(fun index _ -> NumericProperty((fun () -> values[index]),(fun value ->
            if armed && index=0 then raise primary
            values[index]<-value)))
        let handle=group.Numeric(targets,duration=100u)
        handle.To 100.
        handle.Playback.Seek 50.
        armed<-true
        let mutable actual:exn option=None
        try group.Revert() with error -> actual<-Some error
        Expect.isTrue (actual |> Option.exists(fun error -> obj.ReferenceEquals(error,primary))) "primary exception identity"
        Expect.equal values[1] 20. "other original restored"
        Expect.isTrue (group.Cancelled && handle.Playback.Cancelled) "all drivers closed"
    testCase "invalid authoring rejects before reading accessors" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        use group=new Animatable(scheduler)
        let mutable reads=0
        let target=NumericProperty((fun () -> reads<-reads+1;10.),ignore)
        for duration in [nan;infinity;-1.;1e-15] do
            Expect.throwsT<ArgumentException> (fun () -> group.NumericPrecise(target,duration) |> ignore) "invalid precise input"
        Expect.throwsT<ArgumentException> (fun () -> group.Numeric(target,options={AnimatablePropertyOptions.Default with Precision=Some 309u}) |> ignore) "precision"
        Expect.equal reads 0 "validation before reads"
        Expect.equal group.Count 0 "no phantom registration"
    testCase "cancellation during capture prevents later accessor reads" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        use group=new Animatable(scheduler)
        let first=NumericProperty((fun () -> group.Cancel();10.),ignore)
        let second=NumericProperty((fun () -> failtest "read after cancellation"),ignore)
        Expect.throwsT<ObjectDisposedException> (fun () -> group.Numeric([|first;second|]) |> ignore) "capture interrupted"
    testCase "revert during initial modifier restores pending captures and prevents dispatch" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        use group=new Animatable(scheduler)
        let mutable value=10.
        let mutable writes=0
        let target=NumericProperty((fun () -> value),(fun next -> writes<-writes+1;value<-next))
        let options={AnimatablePropertyOptions.Default with Modifier=(fun next -> group.Revert();next)}
        Expect.throwsT<ObjectDisposedException> (fun () -> group.Numeric(target,options=options) |> ignore) "initialization interrupted"
        Expect.equal value 10. "pending capture restored"
        Expect.equal writes 1 "only restoration write"
        Expect.equal group.Count 0 "no live track"
    testCase "revert after a callback fault cannot revive terminal properties" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        let primary=InvalidOperationException "callback failure"
        use group=new Animatable(scheduler,onBegin=(fun _ -> raise primary))
        let mutable writes=0
        let target=NumericProperty((fun () -> 10.),(fun _ -> writes<-writes+1))
        let first=group.Numeric(target,duration=100u)
        let second=group.Numeric(NumericProperty((fun () -> 20.),ignore),duration=200u)
        first.To 100.
        second.To 200.
        scheduler.Advance 25.
        let mutable actual:exn option=None
        try scheduler.RunFrame() with error -> actual<-Some error
        Expect.isTrue (actual |> Option.exists(fun error -> obj.ReferenceEquals(error,primary))) "callback exception identity"
        Expect.isTrue (group.Cancelled && first.Playback.Cancelled && second.Playback.Cancelled) "fault is terminal"
        let stopped=writes
        group.Revert()
        scheduler.Advance 100.
        scheduler.RunFrame()
        Expect.equal writes stopped "no rewind, restoration or future dispatch after fault"
        Expect.equal group.Count 0 "fault cleanup releases handles"
    testCase "empty groups revert and unsigned target groups reject empty inputs" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        use group=new Animatable(scheduler)
        Expect.throwsT<ArgumentException> (fun () -> group.Numeric([||]:NumericProperty array) |> ignore) "nonempty target contract"
        group.Revert()
        Expect.isTrue group.Cancelled "empty group ends"
]
