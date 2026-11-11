module Ranvier.Anime.Tests.TimelinePlaybackEaseTests

open System
open System.IO
open System.Text.Json
open Expecto
open Ranvier
open Ranvier.Dom
open Ranvier.Anime

type private State =
    { Time:float;IterationTime:float;Iteration:int;Backwards:bool;Paused:bool;Began:bool;Completed:bool }
type private Event =
    { Id:string;Name:string;Self:State;Value:float }

let private owned action =
    use graph=new Graph()
    use activation=graph.Activate()
    Api.createRoot(fun owner -> try action() finally owner.Dispose())

let private timelineState (clock:Timeline) =
    {Time=clock.CurrentTime;IterationTime=clock.IterationCurrentTime;Iteration=clock.CurrentIteration
     Backwards=clock.Backwards;Paused=not clock.Playing;Began=clock.Began;Completed=clock.Completed}
let private timerState (clock:Timer) =
    {Time=clock.CurrentTime;IterationTime=clock.IterationCurrentTime;Iteration=clock.CurrentIteration
     Backwards=clock.Backwards;Paused=clock.Paused;Began=clock.Began;Completed=clock.Completed}
let private numericState (clock:NumericAnimation) =
    {Time=clock.CurrentTime;IterationTime=clock.IterationCurrentTime;Iteration=clock.CurrentIteration
     Backwards=clock.Backwards;Paused=not clock.Playing;Began=clock.Began;Completed=clock.Completed}
let private get (name:string) (json:JsonElement)=json.GetProperty name
let private number name json=(get name json).GetDouble()
let private text name json=(get name json).GetString()
let private close label expected actual =
    if Double.IsNaN expected then Expect.isTrue (Double.IsNaN actual) label
    else Expect.isLessThanOrEqual (abs(actual-expected)) (1e-8*max 1. (abs expected)) label
let private compareState label (expected:JsonElement) (actual:State) =
    close (label+" time") (number "time" expected) actual.Time
    close (label+" iterationTime") (number "iterationTime" expected) actual.IterationTime
    Expect.equal actual.Iteration ((get "iteration" expected).GetInt32()) (label+" iteration")
    for name,value in ["backwards",actual.Backwards;"paused",actual.Paused;"began",actual.Began;"completed",actual.Completed] do
        Expect.equal value ((get name expected).GetBoolean()) (label+" "+name)
let private ease = function
    | "square" -> fun t -> t*t
    | "reverse" -> fun t -> 1.-t
    | "overshoot" -> fun t -> 1.25*t
    | "nan" -> fun _ -> nan
    | _ -> id
let private fixturePath() =
    let specified=Environment.GetEnvironmentVariable "RANVIER_ANIME_TIMELINE_EASE_FIXTURE"
    if not(String.IsNullOrWhiteSpace specified) then specified
    else
        let root=Environment.GetEnvironmentVariable "RANVIER_ANIME_REFERENCE_ROOT"
        Path.Combine((if String.IsNullOrWhiteSpace root then Path.Combine(AppContext.BaseDirectory,"reference") else root),"timeline-playback-ease.json")
let private fixtureCases() =
    use document=JsonDocument.Parse(File.ReadAllText(fixturePath()))
    Expect.equal (text "upstreamCommit" document.RootElement) "2c9cf8ea00329f6768c7d7902252ed977d75ce42" "pinned source"
    (get "fixtures" document.RootElement).EnumerateArray() |> Seq.map(fun value -> value.Clone()) |> Seq.toArray

let private replay (input:JsonElement) = owned(fun () ->
    let mutable value=0.
    let events=ResizeArray<Event>()
    let easeInputs=ResizeArray<float>()
    let scheduler=new ManualFrameScheduler()
    let rootParameters=get "parameters" input
    let optional (name:string) fallback (convert:JsonElement -> 't) =
        match rootParameters.TryGetProperty name with true,value -> convert value | _ -> fallback
    let playback=
        {PlaybackOptions.Default with Repeat=PlaybackRepeat.Count(optional "loop" 0u (fun value -> value.GetUInt32()))
                                      LoopDelay=optional "loopDelay" 0. (fun value -> value.GetDouble())
                                      Alternate=optional "alternate" false (fun value -> value.GetBoolean())
                                      Reversed=optional "reversed" false (fun value -> value.GetBoolean())}
    let rootEase=ease(text "ease" input)
    let logTimeline id name clock=events.Add {Id=id;Name=name;Self=timelineState clock;Value=value}
    let logTimer name clock=events.Add {Id="child";Name=name;Self=timerState clock;Value=value}
    let logNumeric name clock=events.Add {Id="child";Name=name;Self=numericState clock;Value=value}
    let createTimeline id playback playbackEase =
        new Timeline(scheduler,options=playback,playbackEase=playbackEase,
                     onBegin=logTimeline id "Begin",onBeforeUpdate=logTimeline id "BeforeUpdate",
                     onRender=logTimeline id "Render",onUpdate=logTimeline id "Update",
                     onLoop=logTimeline id "Loop",onPause=logTimeline id "Pause",onComplete=logTimeline id "Complete")
    use timeline=createTimeline "root" playback (fun t -> easeInputs.Add t;rootEase t)
    let property=NumericProperty((fun () -> value),(fun v -> value<-v))
    let mutable readChild: (unit -> State) option=None
    let borrowed=ResizeArray<IDisposable>()
    let at=TimelinePosition.At
    match text "kind" input with
    | "ownedAnimation" -> timeline.Animate(property,100.,100.,position=at 0.,ease=id,from=0.) |> ignore
    | "constructionOffset" ->
        timeline.Animate(property,100.,100.,position=at 0.,ease=id,from=0.) |> ignore
        timeline.Seek(50.,muteCallbacks=true)
        timeline.Animate(property,200.,100.,position=at 75.,ease=id) |> ignore
    | "ownedTimer" ->
        let child=timeline.AddTimer(100.,position=at 25.,options={PlaybackOptions.Default with Delay=10.;Speed=2.},
                                   onBegin=logTimer "Begin",onBeforeUpdate=logTimer "BeforeUpdate",onUpdate=logTimer "Update",
                                   onLoop=logTimer "Loop",onPause=logTimer "Pause",onComplete=logTimer "Complete")
        readChild<-Some(fun () -> timerState child)
    | "borrowedAnimation" ->
        let child=new NumericAnimation(scheduler,(fun () -> 0.),(fun v -> value<-v),100.,100.,
                                       onBegin=logNumeric "Begin",onBeforeUpdate=logNumeric "BeforeUpdate",
                                       onRender=logNumeric "Render",onUpdate=logNumeric "Update",onLoop=logNumeric "Loop",
                                       onPause=logNumeric "Pause",onComplete=logNumeric "Complete")
        child.Reset()
        borrowed.Add child
        timeline.Sync(child,position=at 25.) |> ignore
        readChild<-Some(fun () -> numericState child)
    | "borrowedTimer" ->
        let child=new Timer(scheduler,100.,options={PlaybackOptions.Default with Delay=10.},
                            onBegin=logTimer "Begin",onBeforeUpdate=logTimer "BeforeUpdate",onUpdate=logTimer "Update",
                            onLoop=logTimer "Loop",onPause=logTimer "Pause",onComplete=logTimer "Complete")
        child.Reset()
        borrowed.Add child
        timeline.Sync(child,position=at 25.) |> ignore
        readChild<-Some(fun () -> timerState child)
    | "nested" ->
        let child=createTimeline "child" PlaybackOptions.Default sqrt
        child.Animate(property,100.,100.,position=at 0.,ease=id,from=0.) |> ignore
        child.Call((fun () -> logTimeline "nestedCall" "Call" child),position=at 50.) |> ignore
        child.Reset()
        borrowed.Add child
        timeline.Sync(child,position=at 25.) |> ignore
        readChild<-Some(fun () -> timelineState child)
    | "set" ->
        timeline.Animate(property,100.,100.,position=at 0.,ease=id,from=0.) |> ignore
        timeline.Set(property,40.,position=at 50.) |> ignore
    | _ -> timeline.AddTimer(100.,position=at 0.) |> ignore
    timeline.Call((fun () -> logTimeline "call" "Call" timeline),position=at 50.) |> ignore
    timeline.Reset()
    let compareSnapshot label expected =
        close (label+" value") (number "value" expected) value
        compareState (label+" root") (get "root" expected) (timelineState timeline)
        match readChild with
        | Some read -> compareState (label+" child") (get "child" expected) (read())
        | None -> ()
    compareSnapshot "initial" (get "initial" input)
    close "duration" (number "duration" input) timeline.Duration
    for sample in (get "samples" input).EnumerateArray() do
        events.Clear();easeInputs.Clear()
        let step=get "step" sample
        timeline.Seek(number "time" step,muteCallbacks=(get "mute" step).GetBoolean())
        let label="sample "+string(number "time" step)
        compareSnapshot label (get "state" sample)
        let expectedEvents=(get "events" sample).EnumerateArray() |> Seq.toArray
        Expect.equal events.Count expectedEvents.Length (label+" event count")
        Array.iteri(fun index expected ->
            let actual=events[index]
            Expect.equal (actual.Id,actual.Name) (text "id" expected,text "event" expected) (label+" event order")
            compareState (label+" event self") (get "self" expected) actual.Self
            close (label+" event value") (number "value" expected) actual.Value) expectedEvents
        let expectedInputs=(get "easeInputs" sample).EnumerateArray() |> Seq.map(fun x -> x.GetDouble()) |> Seq.toArray
        Expect.equal easeInputs.Count expectedInputs.Length (label+" easing invocation count")
        Array.iter2(close (label+" easing input")) expectedInputs (easeInputs.ToArray())
    for child in borrowed do child.Dispose())

let private guardTests = [
    testCase "easing seek preserves successor and discards stale dispatch" <| fun _ -> owned(fun () ->
        let mutable value=0.
        let mutable armed=false
        let mutable current:Timeline option=None
        let calls=ResizeArray<string>()
        use scheduler=new ManualFrameScheduler()
        use timeline=new Timeline(scheduler,playbackEase=(fun t ->
            if armed then armed<-false;current.Value.Seek(80.)
            t*t))
        current<-Some timeline
        timeline.Animate(NumericProperty((fun () -> value),(fun v -> value<-v)),100.,100.,from=0.,ease=id) |> ignore
        timeline.Call((fun () -> calls.Add "call"),position=TimelinePosition.At 50.) |> ignore
        timeline.Reset()
        armed<-true
        timeline.Seek(25.)
        close "successor root time" 80. timeline.CurrentTime
        close "successor mapped value" 64. value
        Expect.equal (Seq.toList calls) ["call"] "stale sample adds no call")
    testCase "easing cancellation prevents property and call writes" <| fun _ -> owned(fun () ->
        let mutable value=0.
        let mutable armed=false
        let mutable current:Timeline option=None
        let mutable calls=0
        use scheduler=new ManualFrameScheduler()
        use timeline=new Timeline(scheduler,playbackEase=(fun t ->
            if armed then current.Value.Cancel()
            t))
        current<-Some timeline
        timeline.Animate(NumericProperty((fun () -> value),(fun v -> value<-v)),100.,100.,from=0.,ease=id) |> ignore
        timeline.Call((fun () -> calls<-calls+1),position=TimelinePosition.At 50.) |> ignore
        timeline.Reset()
        armed<-true
        timeline.Seek(75.)
        close "cancelled value" 0. value
        Expect.equal calls 0 "cancelled call")
    testCase "beforeUpdate seek preserves successor mapped dispatch" <| fun _ -> owned(fun () ->
        let mutable value=0.
        let mutable armed=false
        use scheduler=new ManualFrameScheduler()
        use timeline=new Timeline(scheduler,playbackEase=(fun t -> t*t),
                                  onBeforeUpdate=(fun self -> if armed then armed<-false;self.Seek(80.)))
        timeline.Animate(NumericProperty((fun () -> value),(fun v -> value<-v)),100.,100.,from=0.,ease=id) |> ignore
        timeline.Reset()
        armed<-true
        timeline.Seek(25.)
        close "successor callback time" 80. timeline.CurrentTime
        close "successor callback value" 64. value)
    testCase "null playback easing is rejected" <| fun _ -> owned(fun () ->
        use scheduler=new ManualFrameScheduler()
        Expect.throwsT<ArgumentNullException> (fun () -> new Timeline(scheduler,playbackEase=Unchecked.defaultof<float->float>) |> ignore) "function contract")
]

let tests=testList "Timeline playback easing" [
    for fixture in fixtureCases() do
        testCase (text "name" fixture) <| fun _ -> replay fixture
    yield! guardTests
]

