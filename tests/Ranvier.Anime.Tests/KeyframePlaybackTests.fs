module Ranvier.Anime.Tests.KeyframePlaybackTests

open System
open System.IO
open System.Text.Json
open System.Collections.Generic
open Expecto
open Ranvier.Dom
open Ranvier.Anime
open Ranvier.Anime.ValueInterpolation

let private optional (name:string) (item:JsonElement)=match item.TryGetProperty name with true,value -> Some value | _ -> None
let private number name fallback item=optional name item |> Option.map _.GetDouble() |> Option.defaultValue fallback
let private flag name item=optional name item |> Option.exists _.GetBoolean()
let private literal (item:JsonElement)=if item.ValueKind=JsonValueKind.Number then Number(item.GetDouble()) else Text(item.GetString())
let private equalValue message actual expected=
    match actual,expected with
    | Number actual,Number expected -> Expect.isTrue (actual=expected || abs(actual-expected)<=1e-12*max 1. (abs expected)) message
    | _ -> Expect.equal actual expected message
let private fixtures()=
    let root=Environment.GetEnvironmentVariable "RANVIER_ANIME_REFERENCE_ROOT"
    let root=if String.IsNullOrWhiteSpace root then Path.Combine(AppContext.BaseDirectory,"reference") else root
    use document=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"keyframe-playback.json")))
    Expect.equal (document.RootElement.GetProperty("upstreamCommit").GetString()) "2c9cf8ea00329f6768c7d7902252ed977d75ce42" "pinned source"
    document.RootElement.GetProperty("fixtures").EnumerateArray() |> Seq.map _.Clone() |> Seq.toList
let private replay (fixture:JsonElement)=
    let defaults,properties=KeyframeCompilationTests.capture fixture
    let compile=if fixture.GetProperty("input").GetProperty("composition").GetString()="replace" then KeyframeCompilation.compileReplacing else KeyframeCompilation.compile
    let program=compile(defaults,properties,(fun () -> true)) |> Option.get
    let input=fixture.GetProperty("input")
    let options={PlaybackOptions.Default with Repeat=PlaybackRepeat.Count(uint(number "loop" 0. input));LoopDelay=number "loopDelay" 0. input;
                                             Reversed=flag "reversed" input;Alternate=flag "alternate" input;FrameRate=number "frameRate" 240. input}
    let replace=input.GetProperty("composition").GetString()="replace"
    let values=Dictionary<struct(int*string),Literal>()
    let writes=ResizeArray<int*string*Literal>()
    for property in properties do values[struct(property.Target,property.Name)]<-property.Original
    use scheduler=new ManualFrameScheduler()
    let write target name (output:Output)=
        let value=match output.Scalar with Some value -> Number value | None -> Text output.Text
        values[struct(target,name)]<-value
        writes.Add(target,name,value)
    let events=ResizeArray<string*float*Dictionary<struct(int*string),Literal>>()
    let callback name (clock:Timer)=events.Add(name,clock.CurrentTime,Dictionary(values))
    use driver=new KeyframePlayback(scheduler,program,write,replace,false,4,options,
        onBegin=callback "Begin",onBeforeUpdate=callback "BeforeUpdate",onRender=callback "Render",onUpdate=callback "Update",
        onLoop=callback "Loop",onPause=callback "Pause",onComplete=callback "Complete")
    let verify (expected:JsonElement)=
        let clock=driver.Clock
        Expect.equal clock.CurrentTime (expected.GetProperty("time").GetDouble()) "source current time"
        Expect.equal clock.IterationCurrentTime (expected.GetProperty("iterationTime").GetDouble()) "source iteration time"
        for name,actual in ["paused",clock.Paused;"completed",clock.Completed;"backwards",clock.Backwards] do
            Expect.equal actual (expected.GetProperty(name).GetBoolean()) name
        Expect.equal clock.Duration (expected.GetProperty("duration").GetDouble()) "source total duration"
        Expect.equal clock.IterationDuration (expected.GetProperty("iterationDuration").GetDouble()) "source iteration duration"
        let expectedEvents=expected.GetProperty("events")
        Expect.equal events.Count (expectedEvents.GetArrayLength()) "callback count"
        for index in 0..events.Count-1 do
            let phase,time,snapshot=events[index]
            let expected=expectedEvents[index]
            Expect.equal (phase,time) (expected.GetProperty("event").GetString(),expected.GetProperty("time").GetDouble()) "callback phase/time"
            let targets=expected.GetProperty("targets")
            for target in 0..targets.GetArrayLength()-1 do
                for property in targets[target].EnumerateObject() do
                    equalValue ("callback target "+property.Name) snapshot[struct(target,property.Name)] (literal property.Value)
        events.Clear()
        let targets=expected.GetProperty("targets")
        for index in 0..targets.GetArrayLength()-1 do
            for property in targets[index].EnumerateObject() do
                equalValue ("target "+property.Name) values[struct(index,property.Name)] (literal property.Value)
        let expectedWrites=expected.GetProperty("writes")
        Expect.equal writes.Count (expectedWrites.GetArrayLength()) "source write count"
        for index in 0..writes.Count-1 do
            let target,name,value=writes[index]
            let expected=expectedWrites[index]
            Expect.equal (target,name) (expected.GetProperty("target").GetInt32(),expected.GetProperty("property").GetString()) "source write order"
            equalValue ("write "+name) value (literal(expected.GetProperty("value")))
        writes.Clear()
        Expect.sequenceEqual driver.Times (expected.GetProperty("tweenTimes").EnumerateArray() |> Seq.map _.GetDouble()) "per-frame history"
    driver.Initialize()
    verify(fixture.GetProperty("initial"))
    for sample in fixture.GetProperty("samples").EnumerateArray() do
        let time=sample.GetProperty("time").GetDouble()
        match optional "action" sample with
        | Some action ->
            match action.GetString() with
            | "stretch" -> driver.Clock.Stretch time
            | "frame" -> scheduler.Advance time;scheduler.RunFrame()
            | "speed" -> driver.Clock.Speed<-time
            | "resume" -> driver.Clock.Resume()
            | "play" -> driver.Clock.Play()
            | "pause" -> driver.Clock.Pause()
            | "reverse" -> driver.Clock.Reverse()
            | "alternate" -> driver.Clock.Alternate()
            | "restart" -> driver.Clock.Restart()
            | "reset" -> driver.Clock.Reset()
            | "complete" -> driver.Clock.Complete()
            | "seekMuted" -> driver.Clock.Seek(time,muteCallbacks=true)
            | "seek" -> driver.Clock.Seek time
            | other -> failtestf "Unsupported source operation %s" other
        | _ -> driver.Clock.Seek time
        try verify(sample.GetProperty("state"))
        with error -> failtestf "At action %A, time %g: %s" (optional "action" sample |> Option.map _.GetString()) time error.Message

let playbackTests=testList "keyframe playback" [
    for fixture in fixtures() do testCase ("source "+fixture.GetProperty("name").GetString()) <| fun _ -> replay fixture
]


let private guardedProgram ease modifier explicitFrom =
    let properties=[|
        {FrameProperty.Target=0;Name="x";Original=Number 0.;ConvertUnit=None;
         Frames=[|{FrameInput.Default with From=(if explicitFrom then Some(Number 0.) else None);To=Some(Number 100.)}|]}
        {FrameProperty.Target=0;Name="y";Original=Number 100.;ConvertUnit=None;
         Frames=[|{FrameInput.Default with To=Some(Number 200.)}|]} |]
    KeyframeCompilation.compile({FrameDefaults.Default with Duration=100.;Ease=ease;Modifier=modifier},properties,(fun () -> true)) |> Option.get

let interruptionTests=testList "keyframe interruption" [
    testCase "owner disposal inside easing suppresses remaining properties and callbacks" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        use graph=new Ranvier.Graph()
        use activation=graph.Activate()
        let owner=Ranvier.Api.createRoot id
        let mutable armed=false
        let writes=ResizeArray<string>()
        let callbacks=ResizeArray<string>()
        let ease value=if armed then owner.Dispose();value else value
        let program=guardedProgram ease id false
        use driver=new KeyframePlayback(scheduler,program,(fun _ name _ -> writes.Add name),true,false,4,PlaybackOptions.Default,
            onRender=(fun _ -> callbacks.Add "render"),onUpdate=(fun _ -> callbacks.Add "update"))
        owner.Attach driver.Clock
        driver.Initialize()
        writes.Clear()
        armed<-true
        driver.Clock.Seek 50.
        Expect.isTrue driver.Clock.Cancelled "actual owner attachment"
        Expect.isEmpty writes "cancelled easing dispatch"
        Expect.isEmpty callbacks "later callback phases"

    testCase "owner disposal in a component modifier suppresses later components and targets" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        use graph=new Ranvier.Graph()
        use activation=graph.Activate()
        let owner=Ranvier.Api.createRoot id
        let mutable armed=false
        let mutable calls=0
        let modifier value=if armed then calls<-calls+1;owner.Dispose();value else value
        let properties=[|{FrameProperty.Target=0;Name="transform";Original=Text "translate(0px,0px)";ConvertUnit=None;
                         Frames=[|{FrameInput.Default with To=Some(Text "translate(100px,200px)");Modifier=Some modifier}|]}|]
        let program=KeyframeCompilation.compile({FrameDefaults.Default with Duration=100.;Ease=id},properties,(fun () -> true)) |> Option.get
        let writes=ResizeArray<string>()
        use driver=new KeyframePlayback(scheduler,program,(fun _ name _ -> writes.Add name),true,true,4,PlaybackOptions.Default)
        owner.Attach driver.Clock
        driver.Initialize()
        writes.Clear()
        armed<-true
        driver.Clock.Seek 50.
        Expect.equal calls 1 "remaining component effects"
        Expect.isEmpty writes "no partially composed output"
        Expect.isTrue driver.Clock.Cancelled "owner cancellation"

    testCase "a setter seek supersedes remaining outer properties and render callback" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        let program=guardedProgram id id false
        let values=Dictionary<string,float>()
        let writes=ResizeArray<string*float>()
        let mutable active:Timer option=None
        let mutable armed=false
        let mutable renders=0
        let write _ name (output:Output)=
            let value=output.Scalar.Value
            values[name]<-value
            writes.Add(name,value)
            if armed && name="x" then
                armed<-false
                active.Value.Seek(75.,muteCallbacks=true)
        use driver=new KeyframePlayback(scheduler,program,write,true,false,4,PlaybackOptions.Default,onRender=(fun _ -> renders<-renders+1))
        active<-Some driver.Clock
        driver.Initialize()
        writes.Clear()
        armed<-true
        driver.Clock.Seek 50.
        Expect.equal (values["x"],values["y"]) (75.,175.) "inner playhead"
        Expect.sequenceEqual writes [("x",50.);("x",75.);("y",175.)] "no stale second property"
        Expect.equal renders 0 "muted inner and superseded outer"

    testCase "an easing seek prevents the outer value and following property" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        let mutable active:Timer option=None
        let mutable armed=false
        let ease value=
            if armed then
                armed<-false
                active.Value.Seek(25.,muteCallbacks=true)
            value
        let program=guardedProgram ease id false
        let writes=ResizeArray<string*float>()
        use driver=new KeyframePlayback(scheduler,program,(fun _ name output -> writes.Add(name,output.Scalar.Value)),true,false,4,PlaybackOptions.Default)
        active<-Some driver.Clock
        driver.Initialize()
        writes.Clear()
        armed<-true
        driver.Clock.Seek 75.
        Expect.sequenceEqual writes [("x",25.);("y",125.)] "inner sample owns both properties"
        Expect.equal driver.Clock.CurrentTime 25. "inner time retained"

    testCase "a render seek supersedes outer update and completion" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        let program=guardedProgram id id false
        let mutable armed=false
        let phases=ResizeArray<string>()
        use driver=new KeyframePlayback(scheduler,program,(fun _ _ _ -> ()),true,false,4,PlaybackOptions.Default,
            onRender=(fun clock -> phases.Add "render";if armed then armed<-false;clock.Seek(25.,muteCallbacks=true)),
            onUpdate=(fun _ -> phases.Add "update"),onComplete=(fun _ -> phases.Add "complete"))
        driver.Initialize()
        armed<-true
        driver.Clock.Seek 100.
        Expect.sequenceEqual phases ["render"] "stale update/completion suppressed"
        Expect.equal driver.Clock.CurrentTime 25. "inner seek"
        Expect.isFalse driver.Clock.Completed "inner lifecycle retained"

    testCase "construction cancellation prevents initialization and later explicit-from notifications" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        let program=guardedProgram id id true
        let writes=ResizeArray<string>()
        let mutable calls=0
        use driver=new KeyframePlayback(scheduler,program,(fun _ name _ -> writes.Add name),true,false,4,PlaybackOptions.Default,
            onRender=(fun clock -> calls<-calls+1;clock.Cancel()))
        driver.Initialize()
        driver.Initialize()
        Expect.equal calls 1 "construction notification once"
        Expect.isEmpty writes "cancelled before reset"

    testCase "a callback fault cancels scheduled playback and preserves the original fault" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        let program=guardedProgram id id false
        let fault=InvalidOperationException("keyframe callback fault")
        use driver=new KeyframePlayback(scheduler,program,(fun _ _ _ -> ()),true,false,4,PlaybackOptions.Default,
            onBeforeUpdate=(fun _ -> raise fault))
        driver.Initialize()
        driver.Clock.Resume()
        scheduler.Advance 25.
        let mutable actual:exn option=None
        try scheduler.RunFrame() with error -> actual<-Some error
        Expect.isTrue (actual |> Option.exists(fun error -> Object.ReferenceEquals(error,fault))) "primary fault"
        Expect.isTrue driver.Clock.Cancelled "fault lifetime"
        scheduler.Advance 100.
        scheduler.RunFrame()

    testCase "borrowed ancestor guards suppress later keyframe writes" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        let program=guardedProgram id id false
        let mutable alive=true
        let mutable armed=false
        let writes=ResizeArray<string>()
        use driver=new KeyframePlayback(scheduler,program,(fun _ name _ -> writes.Add name;if armed then alive<-false),true,false,4,PlaybackOptions.Default)
        driver.Initialize()
        writes.Clear()
        armed<-true
        driver.Clock.SetTimeGuarded(50.,fun () -> alive)
        Expect.sequenceEqual writes ["x"] "remaining property blocked by ancestor"
    for disposeOwner in [false;true] do
        testCase ("cancellation before first initialization suppresses callbacks "+string disposeOwner) <| fun _ ->
            use scheduler=new ManualFrameScheduler()
            use graph=new Ranvier.Graph()
            use activation=graph.Activate()
            let owner=Ranvier.Api.createRoot id
            let program=guardedProgram id id true
            let mutable calls=0
            use driver=new KeyframePlayback(scheduler,program,(fun _ _ _ -> failtest "cancelled initial write"),true,false,4,PlaybackOptions.Default,
                onRender=(fun _ -> calls<-calls+1))
            owner.Attach driver.Clock
            if disposeOwner then owner.Dispose() else driver.Clock.Cancel()
            driver.Initialize()
            Expect.equal calls 0 "cancelled construction callback"
            Expect.isTrue driver.Clock.Cancelled "terminal lifetime"
            owner.Dispose()
]
