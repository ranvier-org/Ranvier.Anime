module Ranvier.Anime.Tests.KeyframeComputedTests

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
    use document=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"keyframe-computed.json")))
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
    let rec author (item:JsonElement) : Keyframe<NumericProperty,float> =
        let frame=
            match optional "to" item with
            | Some value when value.ValueKind=JsonValueKind.Object && value.GetProperty("fn").GetString()="frame" ->
                Keyframe.compute(fun context ->
                    compute "to" value context |> ignore
                    optional "mutate" value |> Option.iter(fun next -> context.Target.Value<-next.GetDouble())
                    author(value.GetProperty("result")))
            | Some value when value.ValueKind=JsonValueKind.Object && value.GetProperty("fn").GetString()="pair" ->
                Keyframe.betweenBy(fun context ->
                    let first=compute "to" value context
                    first,number "toBase" 0. value+number "toStep" 0. value*float context.Index)
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
        let frame=match optional "ease" item with Some value -> frame.WithEase(Ease.named(value.GetString())) | None -> frame
        match optional "modifier" item with
        | Some value -> frame.WithModifier(modifier(value.GetString()))
        | None -> frame
    let authored=input.GetProperty("properties").GetProperty("x").EnumerateArray() |> Seq.map author |> Seq.toArray
    let callback name (timer:Timer)=events.Add(name,timer.CurrentTime,Array.copy values)
    let options=
        {KeyframeAnimationOptions.Default with Autoplay=false;Ease=Ease.named(optional "ease" input |> Option.map _.GetString() |> Option.defaultValue "linear");
                                              Composition=(if input.GetProperty("composition").GetString()="none" then KeyframeComposition.None else KeyframeComposition.Replace);
                                              Modifier=modifier(optional "modifier" input |> Option.map _.GetString() |> Option.defaultValue "");
                                              Playback={PlaybackOptions.Default with Delay=number "delay" 0. input;Repeat=PlaybackRepeat.Count(uint(number "loop" 0. input));
                                                                                    LoopDelay=number "loopDelay" 0. input;Alternate=optional "alternate" input |> Option.exists _.GetBoolean()};
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

let sourceTests=testList "public computed keyframe source" [
    for fixture in cases() do testCase (fixture.GetProperty("name").GetString()) <| fun _ -> replay fixture
]

let contractTests=testList "computed keyframe contracts" [
    testCase "null provider factories reject before capture" <| fun _ ->
        Expect.throwsT<ArgumentNullException> (fun () -> Keyframe<NumericProperty,float>.Compute(Unchecked.defaultof<_>) |> ignore) "computed callback"
        Expect.throwsT<ArgumentNullException> (fun () -> Keyframe<NumericProperty,float>.BetweenBy(Unchecked.defaultof<_>) |> ignore) "pair callback"

    for shape in ["null";"hold";"nested";"destination"] do
        testCase ("invalid returned frame rejects atomically "+shape) <| fun _ ->
            use scheduler=new ManualFrameScheduler()
            let values=[|5.;6.|]
            let mutable effects=0
            let targets=values |> Array.mapi(fun index _ -> NumericProperty((fun () -> values[index]),(fun value -> failtest "invalid frame dispatched")))
            let frame:Keyframe<NumericProperty,float>=Keyframe.compute(fun (context:KeyframeContext<NumericProperty,float>) ->
                if context.Index=0u then Keyframe.toValue 100.
                else
                    match shape with
                    | "hold" -> Keyframe.hold()
                    | "nested" -> Keyframe.compute(fun _ -> effects<-effects+1;Keyframe.toValue 200.)
                    | "destination" -> Keyframe.toBy(fun _ -> effects<-effects+1;200.)
                    | _ -> Unchecked.defaultof<_>)
            Expect.throwsT<ArgumentException> (fun () -> Keyframes.Numeric(scheduler,targets,[|frame|]) |> ignore) "invalid return"
            Expect.equal effects 0 "invalid nested providers never invoked"
            Expect.sequenceEqual values [5.;6.] "all targets retain originals"

    for pair in [false;true] do
        testCase ("provider disposal suppresses later effects "+string pair) <| fun _ ->
            use scheduler=new ManualFrameScheduler()
            use graph=new Graph()
            use activation=graph.Activate()
            let owner=Api.createRoot id
            let property=NumericProperty((fun () -> 5.),(fun _ -> failtest "cancelled dispatch"))
            let frame:Keyframe<NumericProperty,float>=
                if pair then Keyframe.betweenBy(fun _ -> owner.Dispose();10.,100.)
                else Keyframe.compute(fun _ -> owner.Dispose();Unchecked.defaultof<_>)
            let frame=frame.WithFromBy(fun _ -> failtest "source after disposal")
                           .WithDurationBy(fun _ -> failtest "duration after disposal")
                           .WithDelayBy(fun _ -> failtest "delay after disposal")
            Expect.throwsT<ObjectDisposedException> (fun () -> Keyframes.Numeric(scheduler,property,[|frame|],owner=owner) |> ignore) "lifetime wins over invalid return"

    testCase "returned source disposal suppresses timing and conversion" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        use graph=new Graph()
        use activation=graph.Activate()
        let owner=Api.createRoot id
        let property=NumericProperty((fun () -> 5.),(fun _ -> failtest "cancelled dispatch"))
        let frame:Keyframe<NumericProperty,float>=Keyframe.compute(fun _ ->
            (Keyframe.toValue nan).WithFromBy(fun _ -> owner.Dispose();nan)
                .WithDurationBy(fun _ -> failtest "timing after source disposal"))
        Expect.throwsT<ObjectDisposedException> (fun () -> Keyframes.Numeric(scheduler,property,[|frame|],owner=owner) |> ignore) "guarded result"

    for pair in [false;true] do
        testCase ("provider faults preserve identity and suppress later targets "+string pair) <| fun _ ->
            use scheduler=new ManualFrameScheduler()
            let mutable calls=0
            let fault=InvalidOperationException("computed provider fault")
            let targets=Array.init 2 (fun _ -> NumericProperty((fun () -> 5.),(fun _ -> failtest "faulted dispatch")))
            let frame:Keyframe<NumericProperty,float>=
                if pair then Keyframe.betweenBy(fun _ -> calls<-calls+1;raise fault)
                else Keyframe.compute(fun _ -> calls<-calls+1;raise fault)
            let mutable actual:exn option=None
            try Keyframes.Numeric(scheduler,targets,[|frame|]) |> ignore with error -> actual<-Some error
            Expect.isTrue (actual |> Option.exists(fun error -> obj.ReferenceEquals(error,fault))) "first provider exception"
            Expect.equal calls 1 "remaining target suppressed"

    for pair in [false;true] do
        testCase ("unused outer source constant is ignored "+string pair) <| fun _ ->
            use scheduler=new ManualFrameScheduler()
            let mutable value=5.
            let property=NumericProperty((fun () -> value),(fun next -> value<-next))
            let frame:Keyframe<NumericProperty,float>=
                if pair then Keyframe.betweenBy(fun _ -> 10.,110.)
                else Keyframe.compute(fun _ -> Keyframe.between 10. 110.)
            use animation=Keyframes.Numeric(scheduler,property,[|frame.WithFrom nan|],duration=100u,
                                            options={KeyframeAnimationOptions.Default with Autoplay=false;Ease=id})
            animation.Playback.Seek 50.
            Expect.equal value 60. "pair/result replaces unused outer endpoint"

    testCase "invalid computed timing leaves every target undispatched" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        let values=[|5.;6.|]
        let targets=values |> Array.mapi(fun index _ -> NumericProperty((fun () -> values[index]),(fun _ -> failtest "invalid timing dispatch")))
        let frame:Keyframe<NumericProperty,float>=Keyframe.compute(fun (context:KeyframeContext<NumericProperty,float>) ->
            (Keyframe.toValue 100.).WithDurationPreciseBy(fun _ -> if context.Index=0u then 100. else nan))
        Expect.throwsT<ArgumentException> (fun () -> Keyframes.Numeric(scheduler,targets,[|frame|]) |> ignore) "dynamic timing"
        Expect.sequenceEqual values [5.;6.] "atomic construction"

    testCase "returned providers finish before dynamic endpoint validation" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        let calls=ResizeArray<string>()
        let property=NumericProperty((fun () -> 5.),(fun _ -> failtest "nonfinite dispatch"))
        let frame:Keyframe<NumericProperty,float>=Keyframe.compute(fun _ ->
            calls.Add "frame"
            (Keyframe.toValue nan).WithFromBy(fun _ -> calls.Add "from";10.)
                .WithDurationBy(fun _ -> calls.Add "duration";100u)
                .WithDelayBy(fun _ -> calls.Add "delay";0.))
        Expect.throwsT<ArgumentException> (fun () -> Keyframes.Numeric(scheduler,property,[|frame|]) |> ignore) "late endpoint validation"
        Expect.sequenceEqual calls ["frame";"from";"duration";"delay"] "source construction effect order"

    for pair in [false;true] do
        testCase ("providers run only during construction "+string pair) <| fun _ ->
            use scheduler=new ManualFrameScheduler()
            let mutable calls=0
            let mutable value=5.
            let property=NumericProperty((fun () -> value),(fun next -> value<-next))
            let frame:Keyframe<NumericProperty,float>=
                if pair then Keyframe.betweenBy(fun context -> calls<-calls+1;context.Target.Value<-40.;40.,100.)
                else Keyframe.compute(fun context -> calls<-calls+1;context.Target.Value<-40.;Keyframe.toValue 100.)
            use animation=Keyframes.Numeric(scheduler,property,[|frame|],duration=100u,
                                            options={KeyframeAnimationOptions.Default with Autoplay=false;Ease=id})
            for time in [25.;100.;50.;0.] do animation.Playback.Seek time
            animation.Playback.Stretch 200.
            animation.Playback.Seek 100.
            animation.Revert()
            Expect.equal calls 1 "no sampling/stretch/revert recapture"
            Expect.equal value 5. "pre-construction snapshot restored"
]
