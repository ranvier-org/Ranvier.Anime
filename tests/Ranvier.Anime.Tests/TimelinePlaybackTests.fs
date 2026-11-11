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
                                  FrameRate=optionalNumber "frameRate" 240. parameters;Speed=optionalNumber "playbackRate" 1. parameters}
let private verifyState value (timeline:Timeline) (expected:JsonElement) =
    Expect.equal value (expected.GetProperty("value").GetDouble()) "property value"
    let root=expected.GetProperty "root"
    for key,actual in ["time",timeline.CurrentTime;"iterationTime",timeline.IterationCurrentTime] do
        Expect.equal actual (root.GetProperty(key).GetDouble()) key
    Expect.equal timeline.CurrentIteration (root.GetProperty("iteration").GetInt32()) "iteration"
    for key,actual in ["backwards",timeline.Backwards;"paused",not timeline.Playing;"began",timeline.Began;"completed",timeline.Completed] do
        Expect.equal actual (root.GetProperty(key).GetBoolean()) key


let private verifyTimer (timer:Timer) (expected:JsonElement) =
    for key,actual in ["time",timer.CurrentTime;"iterationTime",timer.IterationCurrentTime] do
        Expect.equal actual (expected.GetProperty(key).GetDouble()) ("child "+key)
    Expect.equal timer.CurrentIteration (expected.GetProperty("iteration").GetInt32()) "child iteration"
    for key,actual in ["backwards",timer.Backwards;"paused",timer.Paused;"began",timer.Began;"completed",timer.Completed] do
        Expect.equal actual (expected.GetProperty(key).GetBoolean()) ("child "+key)

let private verify (fixture:JsonElement) =
    use scheduler=new ManualFrameScheduler()
    let mutable value=0.
    let mutable expectedEvents:JsonElement array=[||]
    let mutable eventIndex=0
    let mutable checking=false
    let mutable stepDescription="initial"
    let mutable active:Timeline option=None
    let mutable timer:Timer option=None
    let mutable borrowedCheck:(JsonElement -> unit) option=None
    let borrowed=ResizeArray<IDisposable>()
    let mutable resumeBorrowed = fun () -> failtest "No borrowed controller"
    let verifySnapshot timeline snapshot =
        verifyState value timeline snapshot
        timer |> Option.iter(fun timer -> verifyTimer timer (snapshot.GetProperty("children").GetProperty("timer")))
        borrowedCheck |> Option.iter(fun check -> check (snapshot.GetProperty("children").GetProperty("borrowed")))
    let callback id phase timeline =
        if checking then
            Expect.isLessThan eventIndex expectedEvents.Length ("unexpected "+id+" "+phase+" "+stepDescription)
            let expected=expectedEvents[eventIndex]
            Expect.equal (id,phase) (expected.GetProperty("id").GetString(),expected.GetProperty("event").GetString()) ("callback "+stepDescription)
            verifySnapshot timeline (expected.GetProperty "state")
            eventIndex <- eventIndex+1
    use timeline=new Timeline(scheduler,options=options(fixture.GetProperty "parameters"),
        onBegin=callback "root" "Begin",onBeforeUpdate=callback "root" "BeforeUpdate",onRender=callback "root" "Render",
        onUpdate=callback "root" "Update",onLoop=callback "root" "Loop",onPause=callback "root" "Pause",onComplete=callback "root" "Complete")
    active <- Some timeline
    let timerCallback phase child =
        if checking then
            Expect.isLessThan eventIndex expectedEvents.Length ("unexpected timer "+phase+" "+stepDescription)
            let expected=expectedEvents[eventIndex]
            Expect.equal ("timer",phase) (expected.GetProperty("id").GetString(),expected.GetProperty("event").GetString()) ("callback "+stepDescription)
            verifyTimer child (expected.GetProperty "self")
            verifySnapshot active.Value (expected.GetProperty "state")
            eventIndex<-eventIndex+1

    let borrowedCallback phase checkSelf =
        if checking then
            Expect.isLessThan eventIndex expectedEvents.Length ("unexpected borrowed "+phase+" "+stepDescription)
            let expected=expectedEvents[eventIndex]
            Expect.equal ("borrowed",phase) (expected.GetProperty("id").GetString(),expected.GetProperty("event").GetString()) ("callback "+stepDescription)
            checkSelf (expected.GetProperty "self")
            verifySnapshot active.Value (expected.GetProperty "state")
            eventIndex<-eventIndex+1
    let checkNumeric (child:NumericAnimation) (expected:JsonElement) =
        let state = expected
        for key,actual in ["time",child.CurrentTime;"iterationTime",child.IterationCurrentTime] do
            Expect.equal actual (state.GetProperty(key).GetDouble()) ("borrowed "+key)
        Expect.equal child.CurrentIteration (state.GetProperty("iteration").GetInt32()) "borrowed iteration"
        for key,actual in ["backwards",child.Backwards;"paused",not child.Playing;"began",child.Began;"completed",child.Completed] do
            Expect.equal actual (state.GetProperty(key).GetBoolean()) ("borrowed "+key)
    let checkNested (child:Timeline) (expected:JsonElement) =
        for key,actual in ["time",child.CurrentTime;"iterationTime",child.IterationCurrentTime] do
            Expect.equal actual (expected.GetProperty(key).GetDouble()) ("nested "+key)
        Expect.equal child.CurrentIteration (expected.GetProperty("iteration").GetInt32()) "nested iteration"
        for key,actual in ["backwards",child.Backwards;"paused",not child.Playing;"began",child.Began;"completed",child.Completed] do
            Expect.equal actual (expected.GetProperty(key).GetBoolean()) ("nested "+key)

    let property=NumericProperty((fun () -> value),(fun next -> value<-next))
    let call id position = timeline.Call((fun () -> callback id "Call" active.Value),TimelinePosition.At position) |> ignore
    match fixture.GetProperty("kind").GetString() with
    | "syncTimer" ->
        let child=new Timer(scheduler,100u,options=options(fixture.GetProperty "childParameters"),
            onBegin=(fun child -> borrowedCallback "Begin" (verifyTimer child)),
            onBeforeUpdate=(fun child -> borrowedCallback "BeforeUpdate" (verifyTimer child)),
            onUpdate=(fun child -> borrowedCallback "Update" (verifyTimer child)),
            onLoop=(fun child -> borrowedCallback "Loop" (verifyTimer child)),
            onPause=(fun child -> borrowedCallback "Pause" (verifyTimer child)),
            onComplete=(fun child -> borrowedCallback "Complete" (verifyTimer child)))
        borrowed.Add child
        resumeBorrowed <- child.Resume
        borrowedCheck<-Some(verifyTimer child)
        timeline.Sync(child,TimelinePosition.At 25.) |> ignore
    | "syncAnimation" ->
        let child=new NumericAnimation(scheduler,(fun () -> value),(fun next -> value<-next),100.,100u,
            options=options(fixture.GetProperty "childParameters"),
            onBegin=(fun child -> borrowedCallback "Begin" (checkNumeric child)),
            onBeforeUpdate=(fun child -> borrowedCallback "BeforeUpdate" (checkNumeric child)),
            onRender=(fun child -> borrowedCallback "Render" (checkNumeric child)),
            onUpdate=(fun child -> borrowedCallback "Update" (checkNumeric child)),
            onLoop=(fun child -> borrowedCallback "Loop" (checkNumeric child)),
            onPause=(fun child -> borrowedCallback "Pause" (checkNumeric child)),
            onComplete=(fun child -> borrowedCallback "Complete" (checkNumeric child)))
        borrowed.Add child
        resumeBorrowed <- child.Resume
        borrowedCheck<-Some(checkNumeric child)
        timeline.Sync(child,TimelinePosition.At 25.) |> ignore
    | "syncTimeline" ->
        let child=new Timeline(scheduler,options=options(fixture.GetProperty "childParameters"),
            onBegin=(fun child -> borrowedCallback "Begin" (checkNested child)),
            onBeforeUpdate=(fun child -> borrowedCallback "BeforeUpdate" (checkNested child)),
            onRender=(fun child -> borrowedCallback "Render" (checkNested child)),
            onUpdate=(fun child -> borrowedCallback "Update" (checkNested child)),
            onLoop=(fun child -> borrowedCallback "Loop" (checkNested child)),
            onPause=(fun child -> borrowedCallback "Pause" (checkNested child)),
            onComplete=(fun child -> borrowedCallback "Complete" (checkNested child)))
        borrowed.Add child
        resumeBorrowed <- child.Resume
        borrowedCheck<-Some(checkNested child)
        child.Animate(property,100.,100u,TimelinePosition.At 0.,from=0.) |> ignore
        child.Call((fun () -> callback "nestedMid" "Call" active.Value),TimelinePosition.At 50.) |> ignore
        timeline.Sync(child,TimelinePosition.At 25.) |> ignore
    | "timer" ->
        let child=timeline.AddTimer(optionalNumber "duration" 100. (fixture.GetProperty "childParameters"),TimelinePosition.At 25.,options=options(fixture.GetProperty "childParameters"),
            onBegin=timerCallback "Begin",onBeforeUpdate=timerCallback "BeforeUpdate",onUpdate=timerCallback "Update",
            onLoop=timerCallback "Loop",onPause=timerCallback "Pause",onComplete=timerCallback "Complete")
        timer<-Some child
        call "end" timeline.IterationDuration
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
    use cleanup={new IDisposable with member _.Dispose()=checking<-false; for child in borrowed do child.Dispose()}
    Expect.equal timeline.Duration (fixture.GetProperty("duration").GetDouble()) "total duration"
    Expect.equal timeline.IterationDuration (fixture.GetProperty("iterationDuration").GetDouble()) "period"
    verifySnapshot timeline (fixture.GetProperty "initial")
    for sample in fixture.GetProperty("samples").EnumerateArray() do
        let step=sample.GetProperty "step"
        stepDescription<-step.GetRawText()
        expectedEvents <- sample.GetProperty("events").EnumerateArray()
            |> Seq.filter(fun event -> event.GetProperty("id").GetString()="root" || event.GetProperty("id").GetString()="timer" || event.GetProperty("id").GetString()="borrowed" || event.GetProperty("event").GetString()="Call") |> Seq.toArray
        eventIndex<-0
        match step.GetProperty("op").GetString() with
        | "seek" -> timeline.Seek(step.GetProperty("time").GetDouble(),muteCallbacks=optionalBool "mute" step)
        | "frame" -> scheduler.Advance(step.GetProperty("after").GetDouble());scheduler.RunFrame()
        | "speed" -> timeline.Speed<-step.GetProperty("value").GetDouble()
        | "resumeBorrowed" -> resumeBorrowed()
        | "resume" -> timeline.Resume()
        | "play" -> timeline.Play()
        | "reverse" -> timeline.Reverse()
        | "pause" -> timeline.Pause()
        | "restart" -> timeline.Restart()
        | "reset" -> timeline.Reset()
        | "complete" -> timeline.Complete(muteCallbacks=optionalBool "mute" step)
        | op -> failtestf "Unknown timeline operation %s" op
        Expect.equal eventIndex expectedEvents.Length ("callback count "+step.GetRawText())
        verifySnapshot timeline (sample.GetProperty "state")

let private referenceTests =
    let root =
        match Environment.GetEnvironmentVariable "RANVIER_ANIME_REFERENCE_ROOT" with
        | null | "" -> Path.Combine(AppContext.BaseDirectory,"reference")
        | value -> value
    use document=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"timeline-playback.json")))
    document.RootElement.GetProperty("fixtures").EnumerateArray()
    |> Seq.filter(fun fixture -> ["property";"overlap";"sets";"calls";"zeroCalls";"timer";"syncTimer";"syncAnimation";"syncTimeline"] |> List.contains(fixture.GetProperty("kind").GetString()))
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


let private timerLifetimeTests = testList "timeline timer lifetime" [
    testCase "owned timers receive frames exclusively from the parent" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        use timeline=new Timeline(scheduler)
        let mutable updates=0
        let child=timeline.AddTimer(100u,onUpdate=(fun _ -> updates<-updates+1))
        child.Resume()
        scheduler.Advance 10.;scheduler.RunFrame()
        Expect.equal updates 0 "child resume cannot queue an independent frame"
        timeline.Resume()
        scheduler.Advance 13.;scheduler.RunFrame()
        Expect.equal updates 1 "parent supplies one sample"

    testCase "cancelling the parent disposes owned timers and preserves borrowed timers" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        use timeline=new Timeline(scheduler)
        let owned=timeline.AddTimer(100u,TimelinePosition.At 0.)
        use borrowed=new Timer(scheduler,100u)
        timeline.Sync(borrowed,TimelinePosition.At 0.) |> ignore
        timeline.Cancel()
        Expect.isTrue owned.Cancelled "owned child lifetime ended"
        Expect.isFalse borrowed.Cancelled "borrowed child retained"
        Expect.throwsT<ObjectDisposedException> owned.Resume "owned timer is terminal"
        borrowed.Seek 25.
        Expect.equal borrowed.CurrentTime 25. "borrowed timer remains usable"

    testCase "owner disposal in a child begin callback prevents later phases and siblings" <| fun _ ->
        use graph=new Ranvier.Graph()
        use activation=graph.Activate()
        use scheduler=new ManualFrameScheduler()
        let phases=ResizeArray<string>()
        let owner,timeline,first,second=Ranvier.Api.createRoot(fun owner ->
            let timeline=new Timeline(scheduler) |> Timeline.own
            let first=timeline.AddTimer(100u,TimelinePosition.At 0.,
                onBegin=(fun _ -> phases.Add "begin";owner.Dispose()),onUpdate=(fun _ -> phases.Add "update"))
            let second=timeline.AddTimer(100u,TimelinePosition.At 0.,onBegin=(fun _ -> phases.Add "sibling"))
            owner,timeline,first,second)
        use timeline=timeline
        timeline.Resume()
        scheduler.Advance 13.;scheduler.RunFrame()
        Expect.sequenceEqual phases ["begin"] "ancestor interruption"
        Expect.isTrue (timeline.Cancelled && first.Cancelled && second.Cancelled) "owned lifetimes closed"
        owner.Dispose()

    testCase "a child seek callback supersedes the outer parent sample" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        use timeline=new Timeline(scheduler)
        let mutable once=true
        let child=timeline.AddTimer(100u,onBegin=(fun _ ->
            if once then
                once<-false
                timeline.Seek(20.,muteCallbacks=true)))
        let mutable value=0.
        timeline.Animate(NumericProperty((fun () -> value),(fun next -> value<-next)),100.,100u,TimelinePosition.At 0.) |> ignore
        timeline.Seek 75.
        Expect.equal (timeline.CurrentTime,child.CurrentTime,value) (20.,20.,20.) "inner parent operation wins"

    testCase "a zero timer completion seek is finite and preserves the inner flags" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        use timeline=new Timeline(scheduler)
        let mutable completions=0
        let child=timeline.AddTimer(0u,TimelinePosition.At 50.,onComplete=(fun _ ->
            completions<-completions+1
            timeline.Seek(20.,muteCallbacks=true)))
        timeline.Seek 75.
        Expect.equal completions 1 "no recursive completion"
        Expect.equal timeline.CurrentTime 20. "inner position retained"
        Expect.isFalse child.Completed "outer setter cannot commit completion after supersession"

    testCase "child callback faults close root and all owned lifetimes" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        use timeline=new Timeline(scheduler)
        let failure=InvalidOperationException("owned callback fault")
        let first=timeline.AddTimer(100u,TimelinePosition.At 0.,onBeforeUpdate=(fun _ -> raise failure))
        let second=timeline.AddTimer(100u,TimelinePosition.At 0.)
        timeline.Resume()
        scheduler.Advance 13.
        let mutable caught:Exception option=None
        try scheduler.RunFrame() with error -> caught<-Some error
        Expect.isTrue (Object.ReferenceEquals(caught.Value,failure)) "original exception propagated"
        Expect.isTrue (timeline.Cancelled && first.Cancelled && second.Cancelled) "all owned lifetimes ended"

    testCase "cancellation finishes all cleanup while preserving its first error" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        let failure=InvalidOperationException("root pause failure")
        use timeline=new Timeline(scheduler,onPause=(fun _ -> raise failure))
        let cleaned=ResizeArray<int>()
        let first=timeline.AddTimer(100u,TimelinePosition.At 0.,onPause=(fun _ -> cleaned.Add 1;failwith "child pause failure"))
        let second=timeline.AddTimer(100u,TimelinePosition.At 0.,onPause=(fun _ -> cleaned.Add 2))
        first.Resume();second.Resume();timeline.Resume()
        let mutable caught:Exception option=None
        try timeline.Cancel() with error -> caught<-Some error
        Expect.isTrue (Object.ReferenceEquals(caught.Value,failure)) "root error preserved"
        Expect.sequenceEqual cleaned [1;2] "every child cleaned"
        Expect.isTrue (first.Cancelled && second.Cancelled) "children terminal"

    testCase "an explicitly cancelled owned timer is inert while the parent finishes" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        use timeline=new Timeline(scheduler)
        let mutable updates=0
        let child=timeline.AddTimer(100u,onUpdate=(fun _ -> updates<-updates+1))
        child.Cancel()
        timeline.Seek 100.
        Expect.equal updates 0 "no callbacks from a disposed child"
        Expect.isTrue timeline.Completed "cancelled child releases completion gating"
]


let private reviewTests = testList "playback review regressions" [
    testCase "onPause seek supersedes outer property authoring" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        let mutable armed=false
        use timeline=new Timeline(scheduler,onPause=(fun timeline -> if armed then timeline.Seek(20.,muteCallbacks=true)))
        timeline.Animate(NumericProperty((fun () -> 0.),ignore),100.,100u,TimelinePosition.At 0.) |> ignore
        timeline.Resume()
        armed<-true
        timeline.Animate(NumericProperty((fun () -> 0.),ignore),200.,100u,TimelinePosition.At 100.) |> ignore
        Expect.equal (timeline.CurrentTime,timeline.Duration) (20.,100.) "inner control prevents publishing/reset"

    testCase "onPause cancellation prevents outer property authoring" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        let mutable armed=false
        use timeline=new Timeline(scheduler,onPause=(fun timeline -> if armed then timeline.Cancel()))
        timeline.Animate(NumericProperty((fun () -> 0.),ignore),100.,100u,TimelinePosition.At 0.) |> ignore
        timeline.Resume()
        armed<-true
        timeline.Animate(NumericProperty((fun () -> 0.),ignore),200.,100u,TimelinePosition.At 100.) |> ignore
        Expect.isTrue timeline.Cancelled "terminal callback retained"
        Expect.equal timeline.Duration 100. "no phantom child"

    testCase "borrowed numeric setter pause cancellation prevents descendant writes" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        use parent=new Timeline(scheduler)
        let mutable value=0.
        let mutable updates=0
        use child=new NumericAnimation(scheduler,(fun () -> value),(fun next -> value<-next),100.,100u,
            onPause=(fun _ -> parent.Cancel()),onUpdate=(fun _ -> updates<-updates+1))
        parent.Sync child |> ignore
        child.Resume()
        parent.Seek 50.
        Expect.equal (value,updates) (0.,0) "pause callback precedes guarded sampling"
        Expect.isTrue parent.Cancelled "parent cancelled"
        Expect.isFalse child.Cancelled "borrowed lifetime remains"

    testCase "borrowed nested setter pause seek supersedes the outer sample" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        use parent=new Timeline(scheduler)
        let mutable once=true
        use child=new Timeline(scheduler,onPause=(fun _ ->
            if once then
                once<-false
                parent.Seek(20.,muteCallbacks=true)))
        let mutable value=0.
        child.Animate(NumericProperty((fun () -> value),(fun next -> value<-next)),100.,100u) |> ignore
        parent.Sync child |> ignore
        child.Resume()
        parent.Seek 75.
        Expect.equal (parent.CurrentTime,child.CurrentTime,value) (20.,20.,20.) "inner seek survives setter callbacks"

    testCase "borrowed timer setter pause cancellation prevents later callbacks" <| fun _ ->
        use scheduler=new ManualFrameScheduler()
        use parent=new Timeline(scheduler)
        let mutable updates=0
        use child=new Timer(scheduler,100u,onPause=(fun _ -> parent.Cancel()),onUpdate=(fun _ -> updates<-updates+1))
        parent.Sync child |> ignore
        child.Resume()
        parent.Seek 50.
        Expect.equal (child.CurrentTime,updates) (0.,0) "guard rechecked after pause"
        Expect.isFalse child.Cancelled "borrowed timer remains reusable"
]

let tests = testList "timeline shared playback" [referenceTests;lifetimeTests;timerLifetimeTests;reviewTests]

