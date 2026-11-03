module Ranvier.Anime.Tests.KeyframeNumericTests

open System
open System.IO
open System.Text.Json
open Expecto
open Ranvier
open Ranvier.Dom
open Ranvier.Anime

type private InputCall = {Field:string;Index:int;Count:int;Previous:float option;Value:float}
let private optional (name:string) (item:JsonElement)=match item.TryGetProperty name with true,value -> Some value | _ -> None
let private number name fallback item=optional name item |> Option.map _.GetDouble() |> Option.defaultValue fallback
let private close message actual expected=Expect.isTrue (actual=expected || abs(actual-expected)<=1e-12*max 1. (abs expected)) message
let private cases()=
    let root=Environment.GetEnvironmentVariable "RANVIER_ANIME_REFERENCE_ROOT"
    let root=if String.IsNullOrWhiteSpace root then Path.Combine(AppContext.BaseDirectory,"reference") else root
    use document=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"keyframe-inputs.json")))
    Expect.equal (document.RootElement.GetProperty("upstreamCommit").GetString()) "2c9cf8ea00329f6768c7d7902252ed977d75ce42" "pinned source"
    document.RootElement.GetProperty("fixtures").EnumerateArray() |> Seq.map _.Clone() |> Seq.toList
let private replay (fixture:JsonElement)=
    let input=fixture.GetProperty("input")
    let original=optional "targets" input |> Option.map(fun values -> values.EnumerateArray() |> Seq.map(fun value -> value.GetProperty("x").GetDouble()) |> Seq.toArray)
                 |> Option.defaultWith(fun () -> [|input.GetProperty("original").GetProperty("x").GetDouble()|])
    let values=Array.copy original
    let writes=ResizeArray<int*float>()
    let calls=ResizeArray<InputCall>()
    let events=ResizeArray<string*float*float array>()
    let targets=values |> Array.mapi(fun index _ -> NumericProperty((fun () -> values[index]),(fun value -> values[index]<-value;writes.Add(index,value))))
    let compute field (specification:JsonElement) (context:KeyframeContext<NumericProperty,float>) =
        let target=context.Target.Value
        calls.Add {Field=field;Index=int context.Index;Count=int context.Count;Previous=context.PreviousValue;Value=target}
        let value=number "base" 0. specification+number "step" 0. specification*float context.Index
        match specification.GetProperty("fn").GetString() with
        | "target" -> target+value
        | "previous" -> defaultArg context.PreviousValue target+value
        | "mutate" -> context.Target.Value<-value;value
        | _ -> value
    let modifier name value=
        calls.Add {Field="modifier";Index = -1;Count = -1;Previous=None;Value=value}
        match name with "double" -> value*2. | "plusOne" -> value+1. | _ -> value
    let authored=input.GetProperty("properties").GetProperty("x").EnumerateArray() |> Seq.map(fun item ->
        let frame=
            match optional "to" item with
            | Some value when value.ValueKind=JsonValueKind.Object -> Keyframe.toBy(compute "to" value)
            | Some value -> Keyframe.toValue(value.GetDouble())
            | None -> Keyframe.hold()
        let frame=
            match optional "from" item with
            | Some value when value.ValueKind=JsonValueKind.Object -> frame.WithFromBy(compute "from" value)
            | Some value -> frame.WithFrom(value.GetDouble())
            | None -> frame
        let frame=
            match optional "duration" item with
            | Some value when value.ValueKind=JsonValueKind.Object ->
                if fixture.GetProperty("name").GetString()="fractionalTimingInputs" then frame.WithDurationPreciseBy(compute "duration" value)
                else frame.WithDurationBy(fun context -> uint(compute "duration" value context))
            | Some value -> frame.WithDurationPrecise(value.GetDouble())
            | None -> frame
        let frame=
            match optional "delay" item with
            | Some value when value.ValueKind=JsonValueKind.Object -> frame.WithDelayBy(compute "delay" value)
            | Some value -> frame.WithDelay(value.GetDouble())
            | None -> frame
        match optional "modifier" item with
        | Some value -> frame.WithModifier(modifier(value.GetString()))
        | None -> frame) |> Seq.toArray
    let callback name (timer:Timer)=events.Add(name,timer.CurrentTime,Array.copy values)
    let options=
        {KeyframeAnimationOptions.Default with Autoplay=false;Ease=id;
                                              Modifier=modifier(optional "modifier" input |> Option.map _.GetString() |> Option.defaultValue "");
                                              Playback={PlaybackOptions.Default with Repeat=PlaybackRepeat.Count(uint(number "loop" 0. input))};
                                              OnBegin=callback "Begin";OnBeforeUpdate=callback "BeforeUpdate";OnRender=callback "Render";
                                              OnUpdate=callback "Update";OnLoop=callback "Loop";OnPause=callback "Pause";OnComplete=callback "Complete"}
    use scheduler=new ManualFrameScheduler()
    use animation=Keyframes.NumericPrecise(scheduler,targets,authored,input.GetProperty("duration").GetDouble(),options=options)
    let verify (expected:JsonElement)=
        let clock=animation.Playback
        close "time" clock.CurrentTime (expected.GetProperty("time").GetDouble())
        close "iteration time" clock.IterationCurrentTime (expected.GetProperty("iterationTime").GetDouble())
        close "duration" clock.Duration (expected.GetProperty("duration").GetDouble())
        close "iteration duration" clock.IterationDuration (expected.GetProperty("iterationDuration").GetDouble())
        for name,actual in ["paused",clock.Paused;"completed",clock.Completed;"backwards",clock.Backwards] do
            Expect.equal actual (expected.GetProperty(name).GetBoolean()) name
        let targets=expected.GetProperty("targets")
        for index in 0..values.Length-1 do close "target" values[index] (targets[index].GetProperty("x").GetDouble())
        let expectedCalls=expected.GetProperty("calls")
        Expect.equal calls.Count (expectedCalls.GetArrayLength()) "input/modifier effect count"
        for index in 0..calls.Count-1 do
            let actual=calls[index]
            let expected=expectedCalls[index]
            Expect.equal actual.Field (expected.GetProperty("field").GetString()) "effect order"
            if actual.Field="modifier" then close "modifier argument" actual.Value (expected.GetProperty("value").GetDouble())
            else
                Expect.equal (actual.Index,actual.Count) (expected.GetProperty("index").GetInt32(),expected.GetProperty("count").GetInt32()) "target index/count"
                close "callback target" actual.Value (expected.GetProperty("targetValue").GetDouble())
                let previous=expected.GetProperty("previous")
                if previous.ValueKind=JsonValueKind.Null then Expect.isNone actual.Previous "first callback has no predecessor"
                else close "previous modified cache" actual.Previous.Value (previous.GetDouble())
        calls.Clear()
        let expectedWrites=expected.GetProperty("writes")
        Expect.equal writes.Count (expectedWrites.GetArrayLength()) "write count"
        for index in 0..writes.Count-1 do
            let target,value=writes[index]
            let expected=expectedWrites[index]
            Expect.equal target (expected.GetProperty("target").GetInt32()) "target order"
            close "write" value (expected.GetProperty("value").GetDouble())
        writes.Clear()
        let expectedEvents=expected.GetProperty("events")
        Expect.equal events.Count (expectedEvents.GetArrayLength()) "callback count"
        for index in 0..events.Count-1 do
            let name,time,snapshot=events[index]
            let expected=expectedEvents[index]
            Expect.equal name (expected.GetProperty("event").GetString()) "callback phase"
            close "callback time" time (expected.GetProperty("time").GetDouble())
            let targets=expected.GetProperty("targets")
            for target in 0..snapshot.Length-1 do close "callback target snapshot" snapshot[target] (targets[target].GetProperty("x").GetDouble())
        events.Clear()
    verify(fixture.GetProperty("initial"))
    for sample in fixture.GetProperty("samples").EnumerateArray() do
        let time=sample.GetProperty("time").GetDouble()
        animation.Playback.Seek time
        try verify(sample.GetProperty("state"))
        with error -> failtestf "At %g: %s" time error.Message

let inputTests=testList "public numeric keyframe input source" [
    for fixture in cases() do testCase (fixture.GetProperty("name").GetString()) <| fun _ -> replay fixture
]


let contractTests=testList "public numeric keyframe lifetime" [
    testCase "raw arrays, immutable capture and property-scoped revert share one controller" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        let mutable value=5.
        let mutable unrelated=9.
        let property=NumericProperty((fun () -> value),(fun next -> value<-next))
        let frames=Keyframe.values [|0.;100.;200.|]
        let options={KeyframeAnimationOptions.Default with Autoplay=false;Ease=id}
        use animation=Keyframes.Numeric(scheduler,property,frames,duration=100u,options=options)
        frames[1]<-Keyframe.toValue 999.
        animation.Playback.Seek 75.
        Expect.equal value 150. "captured raw array"
        unrelated<-42.
        animation.Revert()
        Expect.equal (value,unrelated) (5.,42.) "captured property restored"
        Expect.isTrue animation.Cancelled "revert ends lifetime"
        animation.Revert()
        Expect.equal value 5. "repeated restoration is harmless"

    testCase "uint maximum duration preserves the precise internal midpoint" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        let mutable value=0.
        let property=NumericProperty((fun () -> value),(fun next -> value<-next))
        use animation=Keyframes.Numeric(scheduler,property,[|Keyframe.toValue 100.|],duration=UInt32.MaxValue,
                                        options={KeyframeAnimationOptions.Default with Autoplay=false;Ease=id})
        animation.Playback.Seek(float UInt32.MaxValue/2.)
        Expect.equal value 50. "uint converted without signed overflow"

    testCase "constant invalid endpoints reject before property reads" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        let property=NumericProperty((fun () -> failtest "read before endpoint validation"),ignore)
        let frames=[|Keyframe.toValue 100.;Keyframe.toValue nan|]
        Expect.throwsT<ArgumentException> (fun () -> Keyframes.Numeric(scheduler,property,frames) |> ignore) "finite endpoints"

    testCase "empty repeated sequences complete without reading an unused property" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        let property=NumericProperty((fun () -> failtest "unused empty property"),(fun _ -> failtest "empty dispatch"))
        let options={KeyframeAnimationOptions.Default with Playback={PlaybackOptions.Default with Repeat=PlaybackRepeat.Forever}}
        use animation=Keyframes.Numeric(scheduler,property,[||],options=options)
        Expect.isTrue animation.Playback.Completed "empty no-op completion"
        animation.Revert()

    testCase "owner disposal during destination evaluation suppresses remaining input callbacks" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        use graph=new Graph()
        use activation=graph.Activate()
        let owner=Api.createRoot id
        let mutable calls=0
        let property=NumericProperty((fun () -> 10.),(fun _ -> failtest "interrupted construction dispatch"))
        let frame=(Keyframe.toBy(fun (_:KeyframeContext<NumericProperty,float>) -> calls<-calls+1;owner.Dispose();100.))
                     .WithFromBy(fun _ -> failtest "source after disposal")
                     .WithDurationBy(fun _ -> failtest "duration after disposal")
                     .WithDelayBy(fun _ -> failtest "delay after disposal")
        Expect.throwsT<ObjectDisposedException> (fun () -> Keyframes.Numeric(scheduler,property,[|frame|],owner=owner) |> ignore) "interrupted construction"
        Expect.equal calls 1 "destination called once"
        scheduler.Advance 100.
        scheduler.RunFrame()

    testCase "invalid dynamic timing leaves all targets undispatched" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        let mutable value=5.
        let mutable calls=0
        let property=NumericProperty((fun () -> value),(fun next -> value<-next;calls<-calls+1))
        let frame=(Keyframe.toValue 100.).WithDurationPreciseBy(fun _ -> -1.)
        Expect.throwsT<ArgumentException> (fun () -> Keyframes.Numeric(scheduler,property,[|frame|]) |> ignore) "dynamic finite timing"
        Expect.equal (value,calls) (5.,0) "atomic publication"

    testCase "owner disposal inside precise timing suppresses later validation and delay effects" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        use graph=new Graph()
        use activation=graph.Activate()
        let owner=Api.createRoot id
        let property=NumericProperty((fun () -> 5.),(fun _ -> failtest "cancelled timing dispatch"))
        let frame=(Keyframe.toValue 100.).WithDurationPreciseBy(fun _ -> owner.Dispose();nan)
                                         .WithDelayBy(fun _ -> failtest "delay after timing disposal")
        Expect.throwsT<ObjectDisposedException> (fun () -> Keyframes.Numeric(scheduler,property,[|frame|],owner=owner) |> ignore) "lifetime takes precedence"

    testCase "revert restores pre-construction snapshots after an input callback mutates its target" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        let mutable value=5.
        let property=NumericProperty((fun () -> value),(fun next -> value<-next))
        let frame=Keyframe.toBy(fun (context:KeyframeContext<NumericProperty,float>) -> context.Target.Value<-40.;100.)
        use animation=Keyframes.Numeric(scheduler,property,[|frame|],duration=100u,
                                        options={KeyframeAnimationOptions.Default with Autoplay=false;Ease=id})
        Expect.equal value 40. "original interpolation captured after callback"
        animation.Playback.Seek 50.
        Expect.equal value 70. "source callback/original order"
        animation.Revert()
        Expect.equal value 5. "explicit native pre-construction restoration contract"

    testCase "completed sequences rewind and restore while cancellation remains terminal" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        let mutable value=5.
        let writes=ResizeArray<float>()
        let property=NumericProperty((fun () -> value),(fun next -> value<-next;writes.Add next))
        use animation=Keyframes.Numeric(scheduler,property,[|Keyframe.between 10. 110.|],duration=100u,
                                        options={KeyframeAnimationOptions.Default with Autoplay=false;Ease=id})
        animation.Playback.Seek 100.
        writes.Clear()
        animation.Revert()
        Expect.sequenceEqual writes [10.;5.] "raw rewind then original snapshot"
        Expect.isTrue animation.Cancelled "reverted controller"
        Expect.throwsT<ObjectDisposedException> (fun () -> animation.Playback.Resume()) "terminal timer"

    testCase "actual owner disposal stops every target on the shared scheduler" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        use graph=new Graph()
        use activation=graph.Activate()
        let owner=Api.createRoot id
        let values=[|5.;6.|]
        let targets=values |> Array.mapi(fun index _ -> NumericProperty((fun () -> values[index]),(fun value -> values[index]<-value)))
        use animation=Keyframes.Numeric(scheduler,targets,[|Keyframe.toValue 100.|],duration=100u,owner=owner,
                                        options={KeyframeAnimationOptions.Default with Ease=id})
        scheduler.Advance 25.
        scheduler.RunFrame()
        let before=Array.copy values
        owner.Dispose()
        scheduler.Advance 100.
        scheduler.RunFrame()
        Expect.sequenceEqual values before "no later shared writes"
        Expect.isTrue animation.Cancelled "owner owns sequence"

    testCase "owner disposal during restoration stops later properties" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        use graph=new Graph()
        use activation=graph.Activate()
        let owner=Api.createRoot id
        let values=[|5.;6.|]
        let mutable armed=false
        let targets=values |> Array.mapi(fun index _ -> NumericProperty((fun () -> values[index]),(fun value ->
            values[index]<-value
            if armed && index=0 && value=5. then owner.Dispose())))
        use animation=Keyframes.Numeric(scheduler,targets,[|Keyframe.between 10. 110.|],duration=100u,owner=owner,
                                        options={KeyframeAnimationOptions.Default with Autoplay=false;Ease=id})
        animation.Playback.Seek 50.
        armed<-true
        animation.Revert()
        Expect.sequenceEqual values [|5.;10.|] "later snapshot restoration suppressed"

    testCase "restoration continues after a setter fault and preserves the first exception" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        let values=[|5.;6.|]
        let fault=InvalidOperationException("keyframe restore fault")
        let mutable armed=false
        let targets=values |> Array.mapi(fun index _ -> NumericProperty((fun () -> values[index]),(fun value ->
            if armed && index=0 && value=5. then raise fault
            values[index]<-value)))
        use animation=Keyframes.Numeric(scheduler,targets,[|Keyframe.between 10. 110.|],duration=100u,
                                        options={KeyframeAnimationOptions.Default with Autoplay=false;Ease=id})
        animation.Playback.Seek 50.
        armed<-true
        let mutable actual:exn option=None
        try animation.Revert() with error -> actual<-Some error
        Expect.isTrue (actual |> Option.exists(fun error -> Object.ReferenceEquals(error,fault))) "primary restoration fault"
        Expect.equal values[1] 6. "later restoration completed"
        Expect.isTrue animation.Cancelled "fault closes lifetime"

    testCase "revert inside an interpolation modifier supersedes outer dispatch" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        let mutable value=5.
        let mutable active:KeyframeAnimation option=None
        let mutable armed=false
        let modify value=
            if armed then
                armed<-false
                active.Value.Revert()
            value
        let property=NumericProperty((fun () -> value),(fun next -> value<-next))
        use animation=Keyframes.Numeric(scheduler,property,[|(Keyframe.between 10. 110.).WithModifier modify|],duration=100u,
                                        options={KeyframeAnimationOptions.Default with Autoplay=false;Ease=id})
        active<-Some animation
        armed<-true
        animation.Playback.Seek 50.
        Expect.equal value 5. "restoration owns final property"
        Expect.isTrue animation.Cancelled "reentrant revert completes"
    for explicitDuration in [false;true] do
        testCase ("constant timing preflight precedes getters "+string explicitDuration) <| fun _ ->
            use scheduler=new ManualFrameScheduler()
            let mutable reads=0
            let property=NumericProperty((fun () -> reads<-reads+1;5.),ignore)
            let frames=
                if explicitDuration then [|(Keyframe.toValue 100.).WithDurationPrecise 1e-14|]
                else Keyframe.values [|0.;100.;200.;300.|]
            Expect.throwsT<ArgumentException> (fun () -> Keyframes.NumericPrecise(scheduler,property,frames,1e-12) |> ignore) "construction precision"
            Expect.equal reads 0 "pure timing validation"

    for completed in [false;true] do
        testCase ("shared playback cancellation closes restoration "+string completed) <| fun _ ->
            use scheduler=new ManualFrameScheduler()
            let mutable value=5.
            let writes=ResizeArray<float>()
            let property=NumericProperty((fun () -> value),(fun next -> value<-next;writes.Add next))
            use animation=Keyframes.Numeric(scheduler,property,[|Keyframe.between 10. 110.|],duration=100u,
                                            options={KeyframeAnimationOptions.Default with Autoplay=false;Ease=id})
            animation.Playback.Seek(if completed then 100. else 50.)
            let before=value
            animation.Playback.Cancel()
            writes.Clear()
            animation.Revert()
            Expect.isEmpty writes "terminal restoration"
            Expect.equal value before "cancel retains last output"

    testCase "playback Complete retains restoration while explicit Cancel closes it" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        let mutable value=5.
        let property=NumericProperty((fun () -> value),(fun next -> value<-next))
        use animation=Keyframes.Numeric(scheduler,property,[|Keyframe.between 10. 110.|],duration=100u,
                                        options={KeyframeAnimationOptions.Default with Autoplay=false;Ease=id})
        animation.Playback.Complete()
        Expect.isTrue animation.Playback.Cancelled "completed clock"
        animation.Revert()
        Expect.equal value 5. "completion retains snapshot cleanup"

    testCase "cancellation callback faults still close restoration and preserve the fault" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        let mutable value=5.
        let fault=InvalidOperationException("keyframe pause fault")
        let property=NumericProperty((fun () -> value),(fun next -> value<-next))
        use animation=Keyframes.Numeric(scheduler,property,[|Keyframe.toValue 100.|],duration=100u,
                                        options={KeyframeAnimationOptions.Default with Ease=id;OnPause=(fun _ -> raise fault)})
        let before=value
        let mutable actual:exn option=None
        try animation.Playback.Cancel() with error -> actual<-Some error
        Expect.isTrue (actual |> Option.exists(fun error -> Object.ReferenceEquals(error,fault))) "original cancellation fault"
        animation.Revert()
        Expect.equal value before "cancelled restoration"

]
