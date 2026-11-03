module Ranvier.Anime.Tests.TimelinePlaybackTests

open System
open System.IO
open System.Text.Json
open Expecto
open Ranvier.Anime
open Ranvier.Dom

let private optionalNumber name fallback (value:JsonElement) =
    match value.TryGetProperty(name:string) with true, field -> field.GetDouble() | _ -> fallback
let private optionalBool name (value:JsonElement) =
    match value.TryGetProperty(name:string) with true, field -> field.GetBoolean() | _ -> false
let private options (parameters:JsonElement) =
    let repeat =
        match parameters.TryGetProperty "loop" with
        | true, field when field.ValueKind=JsonValueKind.True -> PlaybackRepeat.Forever
        | true, field -> PlaybackRepeat.Count(field.GetUInt32())
        | _ -> PlaybackRepeat.Count 0u
    {PlaybackOptions.Default with Repeat=repeat;Delay=optionalNumber "delay" 0. parameters
                                  LoopDelay=optionalNumber "loopDelay" 0. parameters
                                  Reversed=optionalBool "reversed" parameters;Alternate=optionalBool "alternate" parameters
                                  FrameRate=optionalNumber "frameRate" 240. parameters}
let private verifyState value (timeline:Timeline) (expected:JsonElement) =
    Expect.equal value (expected.GetProperty("value").GetDouble()) "property value"
    let root=expected.GetProperty "root"
    for key,actual in ["time",timeline.CurrentTime;"iterationTime",timeline.IterationCurrentTime] do
        Expect.equal actual (root.GetProperty(key).GetDouble()) key
    Expect.equal timeline.CurrentIteration (root.GetProperty("iteration").GetInt32()) "iteration"
    for key,actual in ["backwards",timeline.Backwards;"paused",not timeline.Playing;"began",timeline.Began;"completed",timeline.Completed] do
        Expect.equal actual (root.GetProperty(key).GetBoolean()) key

let private verify (fixture:JsonElement) =
    use scheduler=new ManualFrameScheduler()
    let mutable value=0.
    let mutable expectedEvents:JsonElement array=[||]
    let mutable eventIndex=0
    let mutable checking=false
    let mutable stepDescription="initial"
    let mutable active:Timeline option=None
    let callback id phase timeline =
        if checking then
            Expect.isLessThan eventIndex expectedEvents.Length ("unexpected "+id+" "+phase+" "+stepDescription)
            let expected=expectedEvents[eventIndex]
            Expect.equal (id,phase) (expected.GetProperty("id").GetString(),expected.GetProperty("event").GetString()) ("callback "+stepDescription)
            verifyState value timeline (expected.GetProperty "state")
            eventIndex <- eventIndex+1
    use timeline=new Timeline(scheduler,options=options(fixture.GetProperty "parameters"),
        onBegin=callback "root" "Begin",onBeforeUpdate=callback "root" "BeforeUpdate",onRender=callback "root" "Render",
        onUpdate=callback "root" "Update",onLoop=callback "root" "Loop",onPause=callback "root" "Pause",onComplete=callback "root" "Complete")
    active <- Some timeline
    let property=NumericProperty((fun () -> value),(fun next -> value<-next))
    let call id position = timeline.Call((fun () -> callback id "Call" active.Value),TimelinePosition.At position) |> ignore
    match fixture.GetProperty("kind").GetString() with
    | "property" -> timeline.Animate(property,100.,100u,TimelinePosition.At 0.,from=0.) |> ignore
    | "overlap" ->
        timeline.Animate(property,100.,100u,TimelinePosition.At 0.,from=0.) |> ignore
        timeline.Animate(property,200.,100u,TimelinePosition.At 50.) |> ignore
        call "mid" 75.
    | "sets" ->
        call "start" 0.
        timeline.Animate(property,100.,100u,TimelinePosition.At 0.,from=0.) |> ignore
        timeline.Set(property,40.,TimelinePosition.At 50.) |> ignore
        call "middle" 50.
        call "end" 100.
    | "calls" -> call "start" 0.;call "middle" 50.;call "end" 100.
    | "zeroCalls" -> call "start" 0.
    | kind -> failtestf "Unimplemented fixture kind %s" kind
    timeline.Reset()
    checking <- true
    use cleanup={new IDisposable with member _.Dispose()=checking<-false}
    Expect.equal timeline.Duration (fixture.GetProperty("duration").GetDouble()) "total duration"
    Expect.equal timeline.IterationDuration (fixture.GetProperty("iterationDuration").GetDouble()) "period"
    verifyState value timeline (fixture.GetProperty "initial")
    for sample in fixture.GetProperty("samples").EnumerateArray() do
        let step=sample.GetProperty "step"
        stepDescription<-step.GetRawText()
        expectedEvents <- sample.GetProperty("events").EnumerateArray()
            |> Seq.filter(fun event -> event.GetProperty("id").GetString()="root" || event.GetProperty("event").GetString()="Call") |> Seq.toArray
        eventIndex<-0
        match step.GetProperty("op").GetString() with
        | "seek" -> timeline.Seek(step.GetProperty("time").GetDouble(),muteCallbacks=optionalBool "mute" step)
        | "frame" -> scheduler.Advance(step.GetProperty("after").GetDouble());scheduler.RunFrame()
        | "speed" -> timeline.Speed<-step.GetProperty("value").GetDouble()
        | "resume" -> timeline.Resume()
        | "play" -> timeline.Play()
        | "reverse" -> timeline.Reverse()
        | "pause" -> timeline.Pause()
        | "restart" -> timeline.Restart()
        | "reset" -> timeline.Reset()
        | "complete" -> timeline.Complete(muteCallbacks=optionalBool "mute" step)
        | op -> failtestf "Unknown timeline operation %s" op
        Expect.equal eventIndex expectedEvents.Length ("callback count "+step.GetRawText())
        verifyState value timeline (sample.GetProperty "state")

let private referenceTests =
    let root =
        match Environment.GetEnvironmentVariable "RANVIER_ANIME_REFERENCE_ROOT" with
        | null | "" -> Path.Combine(AppContext.BaseDirectory,"reference")
        | value -> value
    use document=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"timeline-playback.json")))
    document.RootElement.GetProperty("fixtures").EnumerateArray()
    |> Seq.filter(fun fixture -> ["property";"overlap";"sets";"calls";"zeroCalls"] |> List.contains(fixture.GetProperty("kind").GetString()))
    |> Seq.map(fun fixture -> let fixture=fixture.Clone() in testCase (fixture.GetProperty("name").GetString()) (fun _ -> verify fixture))
    |> Seq.toList |> testList "timeline playback property integration"

let private lifetimeTests = testList "timeline shared playback lifetime" [
    testCase "cancellation retains descriptive geometry after releasing entries" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        use timeline=new Timeline(scheduler,options={PlaybackOptions.Default with Repeat=PlaybackRepeat.Count 2u;LoopDelay=25.})
        timeline.Animate(NumericProperty((fun () -> 0.),ignore),100.,100u) |> ignore
        timeline.Cancel()
        Expect.equal (timeline.Duration,timeline.IterationDuration) (350.,100.) "geometry retained"

    testCase "owner disposal during before-update prevents track dispatch" <| fun _ ->
        use graph=new Ranvier.Graph()
        use activation=graph.Activate()
        use scheduler=new ManualFrameScheduler()
        let writes=ResizeArray<float>()
        let owner,timeline=Ranvier.Api.createRoot(fun owner ->
            let timeline=new Timeline(scheduler,onBeforeUpdate=(fun _ -> owner.Dispose())) |> Timeline.own
            timeline.Animate(NumericProperty((fun () -> 0.),writes.Add),100.,100u) |> ignore
            owner,timeline)
        use timeline=timeline
        timeline.Reset()
        timeline.Play()
        scheduler.Advance 13.;scheduler.RunFrame()
        scheduler.Advance 13.;scheduler.RunFrame()
        Expect.sequenceEqual writes [0.] "only explicit initialization wrote"
        Expect.isTrue timeline.Cancelled "actual owner ended the lifetime"
        owner.Dispose()

    testCase "a loop seek suppresses outer child rendering" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        let mutable value=0.
        use timeline=new Timeline(scheduler,options={PlaybackOptions.Default with Repeat=PlaybackRepeat.Count 2u},
            onLoop=(fun timeline -> timeline.Seek(20.,muteCallbacks=true)))
        timeline.Animate(NumericProperty((fun () -> value),(fun next -> value<-next)),100.,100u) |> ignore
        timeline.Reset()
        timeline.Seek 125.
        Expect.equal (value,timeline.CurrentTime) (20.,20.) "inner sample owns state and value"
        Expect.isFalse timeline.Completed "outer sample cannot complete"

    testCase "a skipped callback seek supersedes loop preparation" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        let mutable value=0.
        use timeline=new Timeline(scheduler,options={PlaybackOptions.Default with Repeat=PlaybackRepeat.Count 2u})
        timeline.Animate(NumericProperty((fun () -> value),(fun next -> value<-next)),100.,100u) |> ignore
        let mutable armed=true
        timeline.Call((fun () ->
            if armed then
                armed<-false
                timeline.Seek(20.,muteCallbacks=true)),TimelinePosition.At 50.) |> ignore
        timeline.Reset()
        timeline.Seek 225.
        Expect.equal (value,timeline.CurrentTime) (20.,20.) "preparation cannot overwrite the inner sample"

    testCase "an on-render seek supersedes endpoint completion" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        let mutable value=0.
        let mutable armed=true
        use timeline=new Timeline(scheduler,onRender=(fun timeline ->
            if armed then
                armed<-false
                timeline.Seek(20.,muteCallbacks=true)))
        timeline.Animate(NumericProperty((fun () -> value),(fun next -> value<-next)),100.,100u) |> ignore
        timeline.Reset()
        timeline.Seek 100.
        Expect.equal (value,timeline.CurrentTime) (20.,20.) "inner render retained"
        Expect.isFalse timeline.Completed "outer completion rejected"

    testCase "adding a track resizes repeated geometry without a property dispatch" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        let mutable value=0.
        let writes=ResizeArray<float>()
        let property=NumericProperty((fun () -> value),(fun next -> value<-next;writes.Add next))
        use timeline=new Timeline(scheduler,options={PlaybackOptions.Default with Repeat=PlaybackRepeat.Count 2u})
        timeline.Animate(property,100.,100u,TimelinePosition.At 0.) |> ignore
        timeline.Reset()
        timeline.Play()
        scheduler.Advance 13.;scheduler.RunFrame()
        let before=writes.Count
        timeline.Animate(property,200.,100u,TimelinePosition.At 100.) |> ignore
        Expect.equal writes.Count before "construction caches values"
        Expect.equal (timeline.IterationDuration,timeline.Duration,timeline.CurrentTime) (200.,600.,0.) "new geometry"
        Expect.isFalse timeline.Playing "authoring pauses scheduling"
        timeline.Seek 250.
        Expect.equal value 50. "second iteration uses new period"

    testCase "invalid speed rejected inside easing does not interrupt the write" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        let mutable value=0.
        let mutable active:Timeline option=None
        let mutable armed=false
        let easing progress =
            if armed then Expect.throwsT<ArgumentException> (fun () -> active.Value.Speed<-nan) "invalid rate"
            progress
        use timeline=new Timeline(scheduler)
        active<-Some timeline
        timeline.Animate(NumericProperty((fun () -> value),(fun next -> value<-next)),100.,100u,ease=easing) |> ignore
        armed<-true
        timeline.Seek 50.
        Expect.equal value 50. "failed validation preserves active generation"

    testCase "borrowed pause cancellation prevents publishing the new entry" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        use timeline=new Timeline(scheduler)
        let mutable value=0.
        use child=new NumericAnimation(scheduler,(fun () -> value),(fun next -> value<-next),100.,100u,
            onPause=(fun _ -> timeline.Cancel()))
        child.Resume()
        timeline.Sync child |> ignore
        Expect.isTrue timeline.Cancelled "pause callback cancelled the parent"
        Expect.equal timeline.Duration 0. "no entry published after cancellation"
        child.Seek 25.
        Expect.equal value 25. "borrowed child remains usable"
]

let tests = testList "timeline shared playback" [referenceTests;lifetimeTests]

