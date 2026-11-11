module Ranvier.Anime.Tests.AnimationPlaybackTests

open System
open System.IO
open System.Text.Json
open Expecto
open Ranvier.Anime
open Ranvier.Dom

let private optionalNumber name fallback (value: JsonElement) =
    match value.TryGetProperty(name: string) with true, field -> field.GetDouble() | _ -> fallback

let private optionalBool name (value: JsonElement) =
    match value.TryGetProperty(name: string) with true, field -> field.GetBoolean() | _ -> false

let private verifyState value (animation: NumericAnimation) (expected: JsonElement) =
    for key,actual in ["duration",animation.Duration;"iterationDuration",animation.IterationDuration] do
        match expected.TryGetProperty key with true,value -> Expect.equal actual (value.GetDouble()) key | _ -> ()
    for key, actual in ["value", value; "time", animation.CurrentTime; "iterationTime", animation.IterationCurrentTime] do
        Expect.equal actual (expected.GetProperty(key).GetDouble()) key
    Expect.equal animation.CurrentIteration (expected.GetProperty("iteration").GetInt32()) "iteration"
    for key, actual in ["backwards", animation.Backwards; "paused", not animation.Playing; "completed", animation.Completed] do
        Expect.equal actual (expected.GetProperty(key).GetBoolean()) key

let private verify (fixture: JsonElement) =
    let parameters = fixture.GetProperty "parameters"
    let repeat =
        match parameters.TryGetProperty "loop" with
        | true, value when value.ValueKind = JsonValueKind.True -> PlaybackRepeat.Forever
        | true, value -> PlaybackRepeat.Count(value.GetUInt32())
        | _ -> PlaybackRepeat.Count 0u
    let options =
        { PlaybackOptions.Default with Repeat=repeat; Delay=optionalNumber "delay" 0. parameters
                                       LoopDelay=optionalNumber "loopDelay" 0. parameters
                                       Reversed=optionalBool "reversed" parameters; Alternate=optionalBool "alternate" parameters
                                       FrameRate=optionalNumber "frameRate" 240. parameters }
    let mutable value = 0.
    let mutable expectedEvents : JsonElement array = [||]
    let mutable eventIndex = 0
    let mutable checking = false
    let callback phase animation =
        if checking then
            Expect.isLessThan eventIndex expectedEvents.Length "unexpected callback"
            let expected = expectedEvents[eventIndex]
            Expect.equal phase (expected.GetProperty("event").GetString()) "callback phase"
            verifyState value animation (expected.GetProperty "state")
            eventIndex <- eventIndex + 1
    let mutable activeAnimation : NumericAnimation option = None
    let ease =
        match fixture.TryGetProperty "easing", parameters.TryGetProperty "ease" with
        | (true, name), _ when name.GetString() = "overshoot" -> fun _ -> 1.25
        | (true, name), _ when name.GetString() = "fps" -> fun time ->
            activeAnimation |> Option.iter(fun animation -> animation.Fps <- 60.)
            time
        | _, (true, name) -> Ease.named(name.GetString())
        | _ -> id
    use scheduler = new ManualFrameScheduler()
    use animation = new NumericAnimation(scheduler, (fun () -> value), (fun next -> value <- next), 100.,
        parameters.GetProperty("duration").GetDouble(), ease=ease, options=options,
        onBegin=callback "Begin", onBeforeUpdate=callback "BeforeUpdate", onRender=callback "Render",
        onUpdate=callback "Update", onLoop=callback "Loop", onPause=callback "Pause", onComplete=callback "Complete")
    use cleanup = { new IDisposable with member _.Dispose() = checking <- false }
    activeAnimation <- Some animation
    animation.Reset()
    checking <- true
    Expect.equal animation.Duration (fixture.GetProperty("duration").GetDouble()) "total duration"
    Expect.equal animation.IterationDuration (fixture.GetProperty("iterationDuration").GetDouble()) "iteration duration"
    verifyState value animation (fixture.GetProperty "initial")
    for sample in fixture.GetProperty("samples").EnumerateArray() do
        let step = sample.GetProperty "step"
        expectedEvents <- sample.GetProperty("events").EnumerateArray() |> Seq.toArray
        eventIndex <- 0
        match step.GetProperty("op").GetString() with
        | "seek" -> animation.Seek(step.GetProperty("time").GetDouble(), muteCallbacks=optionalBool "mute" step)
        | "frame" -> scheduler.Advance(step.GetProperty("after").GetDouble()); scheduler.RunFrame()
        | "speed" -> animation.Speed <- step.GetProperty("value").GetDouble()
        | "resume" -> animation.Resume()
        | "play" -> animation.Play()
        | "reverse" -> animation.Reverse()
        | "pause" -> animation.Pause()
        | "restart" -> animation.Restart()
        | "reset" -> animation.Reset()
        | "complete" -> animation.Complete(muteCallbacks=optionalBool "mute" step)
        | "stretch" -> animation.Stretch(step.GetProperty("value").GetDouble())
        | op -> failtestf "Unknown animation operation %s" op
        Expect.equal eventIndex expectedEvents.Length "callback count"
        verifyState value animation (sample.GetProperty "state")

let private referenceTests =
    let root =
        match Environment.GetEnvironmentVariable "RANVIER_ANIME_REFERENCE_ROOT" with
        | null | "" -> Path.Combine(AppContext.BaseDirectory, "reference")
        | value -> value
    use document = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "animation-playback.json")))
    document.RootElement.GetProperty("fixtures").EnumerateArray()
    |> Seq.map(fun fixture ->
        let fixture = fixture.Clone()
        testCase (fixture.GetProperty("name").GetString()) (fun _ -> verify fixture))
    |> Seq.toList
    |> testList "animation playback"

let private interruptionTests = testList "animation interruption" [
    testCase "before-update seek replaces the outer render and its scheduled frame" <| fun _ ->
        use scheduler = new ManualFrameScheduler()
        let mutable value = 0.
        let renders = ResizeArray<float>()
        let mutable once = true
        use animation = new NumericAnimation(scheduler, (fun () -> value), (fun next -> value <- next), 100., 100u,
            onBeforeUpdate=(fun animation ->
                if once then
                    once <- false
                    animation.Seek(75., muteCallbacks=true)),
            onRender=(fun animation -> renders.Add animation.CurrentTime))
        animation.Reset()
        animation.Resume()
        scheduler.Advance 25.
        scheduler.RunFrame()
        Expect.equal value 75. "inner seek owns property"
        Expect.isEmpty renders "muted inner render and interrupted outer callback"
        scheduler.Advance 5.
        scheduler.RunFrame()
        Expect.equal value 92. "replacement anchor"
        Expect.sequenceEqual renders [92.] "one continuation"

    testCase "owner disposal in before-update prevents the property write" <| fun _ ->
        use graph = new Ranvier.Graph()
        use activation = graph.Activate()
        use scheduler = new ManualFrameScheduler()
        let writes = ResizeArray<float>()
        let owner, animation = Ranvier.Api.createRoot(fun owner ->
            let animation = new NumericAnimation(scheduler, (fun () -> 0.), writes.Add, 100., 100u,
                onBeforeUpdate=(fun _ -> owner.Dispose()))
            Animation.own animation |> ignore
            owner, animation)
        use animation = animation
        animation.Reset()
        animation.Resume()
        scheduler.Advance 25.
        scheduler.RunFrame()
        scheduler.Advance 25.
        scheduler.RunFrame()
        Expect.sequenceEqual writes [0.] "only explicit reset wrote"
        Expect.isTrue animation.Cancelled "actual owner ended lifetime"
        owner.Dispose()

    testCase "a seek from on-render supersedes outer completion state" <| fun _ ->
        use scheduler = new ManualFrameScheduler()
        let mutable value = 0.
        let mutable once = true
        use animation = new NumericAnimation(scheduler, (fun () -> value), (fun next -> value <- next), 100., 100u,
            onRender=(fun animation ->
                if once then
                    once <- false
                    animation.Seek(20., muteCallbacks=true)))
        animation.Reset()
        animation.Seek 100.
        Expect.equal value 20. "inner property sample retained"
        Expect.isFalse animation.Completed "outer completion suppressed"
]

let tests = testList "numeric shared playback" [referenceTests; interruptionTests]
