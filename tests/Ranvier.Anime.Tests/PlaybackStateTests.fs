module Ranvier.Anime.Tests.PlaybackStateTests

open System
open System.IO
open System.Globalization
open System.Text.Json
open Expecto
open Ranvier.Anime

let private number (value: JsonElement) =
    if value.ValueKind = JsonValueKind.String then Double.Parse(value.GetString(), CultureInfo.InvariantCulture)
    else value.GetDouble()

let private optionalNumber (name: string) fallback (parameters: JsonElement) =
    match parameters.TryGetProperty name with true, value -> number value | _ -> fallback

let private optionalBool (name: string) (parameters: JsonElement) =
    match parameters.TryGetProperty name with true, value -> value.GetBoolean() | _ -> false

let private verifyState (state: PlaybackState) (expected: JsonElement) =
    let sample = state.Sample
    for key, actual in ["time", sample.CurrentTime; "raw", sample.AbsoluteTime; "iterationTime", sample.IterationCurrentTime
                        "rawIterationTime", sample.IterationTime; "progress", sample.Progress; "iterationProgress", sample.IterationProgress
                        "deltaTime", state.DeltaTime; "speed", state.Speed] do
        let expected = number(expected.GetProperty key)
        if Double.IsNaN expected then Expect.isTrue (Double.IsNaN actual) key
        else Expect.equal actual expected key
    Expect.equal sample.Iteration (expected.GetProperty("iteration").GetInt32()) "iteration"
    for key, actual in ["backwards", sample.Backwards; "reversed", state.Reversed; "began", state.Began
                        "completed", state.Completed; "paused", state.Paused] do
        Expect.equal actual (expected.GetProperty(key).GetBoolean()) key

let private verify (fixture: JsonElement) =
    let parameters = fixture.GetProperty "parameters"
    let iterations =
        match parameters.TryGetProperty "loop" with
        | true, value when value.ValueKind = JsonValueKind.True -> infinity
        | true, value -> number value + 1.
        | _ -> 1.
    let delay = optionalNumber "delay" 0. parameters
    let mutable expectedEvents : JsonElement array = [||]
    let mutable eventIndex = 0
    let mutable current : PlaybackState option = None
    let callback event =
        Expect.isLessThan eventIndex expectedEvents.Length "unexpected callback"
        let expected = expectedEvents[eventIndex]
        Expect.equal (string event) (expected.GetProperty("event").GetString()) "callback phase"
        verifyState current.Value (expected.GetProperty "state")
        eventIndex <- eventIndex + 1
    let state = PlaybackState(optionalNumber "duration" infinity parameters, iterations, delay,
                              optionalNumber "loopDelay" 0. parameters, optionalBool "reversed" parameters,
                              optionalBool "alternate" parameters, callback)
    current <- Some state
    verifyState state (fixture.GetProperty "initial")
    for sample in fixture.GetProperty("samples").EnumerateArray() do
        expectedEvents <- sample.GetProperty("events").EnumerateArray() |> Seq.toArray
        eventIndex <- 0
        let step = sample.GetProperty "step"
        let mute = optionalBool "mute" step
        match step.GetProperty("op").GetString() with
        | "seek" -> state.Seek(number(step.GetProperty "time"), mute)
        | "tick" -> state.Tick(number(step.GetProperty "time") + delay, enum<PlaybackTickMode>(step.GetProperty("mode").GetInt32()), mute)
        | "speed" -> state.Speed <- number(step.GetProperty "value")
        | "reset" -> state.Reset()
        | "restart" -> state.Restart()
        | "resume" -> state.Resume()
        | "pause" -> state.Pause()
        | "play" -> state.Play()
        | "reverse" -> state.Reverse()
        | "alternate" -> state.Alternate()
        | "complete" -> state.Complete mute
        | op -> failtestf "Unsupported timer operation %s" op
        Expect.equal eventIndex expectedEvents.Length "callback count"
        verifyState state (sample.GetProperty "state")

let private referenceTests =
    let root =
        match Environment.GetEnvironmentVariable "RANVIER_ANIME_REFERENCE_ROOT" with
        | null | "" -> Path.Combine(AppContext.BaseDirectory, "reference")
        | value -> value
    use document = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "timer-controls.json")))
    document.RootElement.GetProperty("fixtures").EnumerateArray()
    |> Seq.map(fun fixture ->
        let fixture = fixture.Clone()
        testCase (fixture.GetProperty("name").GetString()) (fun _ -> verify fixture))
    |> Seq.toList
    |> testList "playback callbacks and controls"

let private lifetimeTests =
    testList "callback interruption" [
        testCase "a pause during direction change supersedes the outer play" <| fun _ ->
            let mutable current : PlaybackState option = None
            let state = PlaybackState(100., 1., 0., 0., true, false, fun event ->
                if event = PlaybackEvent.Begin then current.Value.Pause())
            current <- Some state
            state.Play()
            Expect.isTrue state.Paused "callback pause remains effective"

        testCase "a begin seek supersedes later outer callbacks" <| fun _ ->
            let events = ResizeArray<PlaybackEvent>()
            let mutable current : PlaybackState option = None
            let state = PlaybackState(100., 1., 0., 0., false, false, fun event ->
                events.Add event
                if event = PlaybackEvent.Begin then current.Value.Seek(75., true))
            current <- Some state
            state.Seek(25., false)
            Expect.equal state.Sample.CurrentTime 75. "inner seek owns time"
            Expect.sequenceEqual events [PlaybackEvent.Begin] "outer update suppressed"

        testCase "cancellation during before-update suppresses update and completion" <| fun _ ->
            let events = ResizeArray<PlaybackEvent>()
            let mutable current : PlaybackState option = None
            let state = PlaybackState(100., 1., 0., 0., false, false, fun event ->
                events.Add event
                if event = PlaybackEvent.BeforeUpdate then current.Value.Cancel())
            current <- Some state
            state.Resume()
            state.Tick(100., PlaybackTickMode.Auto, false)
            Expect.sequenceEqual events [PlaybackEvent.Begin; PlaybackEvent.BeforeUpdate; PlaybackEvent.Pause] "cancel interrupts render"
            Expect.isFalse state.Completed "interrupted completion not committed"
            Expect.isTrue state.Cancelled "terminal lifetime"
            Expect.throwsT<ObjectDisposedException> (fun () -> state.Seek(0., true)) "disposed timer cannot revive"

        testCase "a loop pause stops later phases in the same sample" <| fun _ ->
            let events = ResizeArray<PlaybackEvent>()
            let mutable current : PlaybackState option = None
            let state = PlaybackState(100., 3., 0., 0., false, false, fun event ->
                events.Add event
                if event = PlaybackEvent.Loop then current.Value.Pause())
            current <- Some state
            state.Resume()
            state.Tick(125., PlaybackTickMode.Auto, false)
            Expect.sequenceEqual events [PlaybackEvent.Begin; PlaybackEvent.Loop; PlaybackEvent.Pause] "loop interrupts render"
            Expect.isTrue state.Paused "callback pause retained"

        testCase "completion restart supersedes explicit complete cancellation" <| fun _ ->
            let mutable current : PlaybackState option = None
            let state = PlaybackState(100., 1., 0., 0., false, false, fun event ->
                if event = PlaybackEvent.Complete then current.Value.Restart())
            current <- Some state
            state.Complete false
            Expect.equal state.Sample.CurrentTime 0. "restart time retained"
            Expect.isFalse state.Completed "restart clears completion"
            Expect.isFalse state.Paused "restart resumes"
            Expect.isFalse state.Cancelled "outer complete cannot cancel restarted operation"

        testCase "a callback seek supersedes the outer reset direction change" <| fun _ ->
            let mutable current : PlaybackState option = None
            let mutable interrupt = false
            let state = PlaybackState(100., 1., 0., 0., false, false, fun event ->
                if interrupt && event = PlaybackEvent.BeforeUpdate then
                    interrupt <- false
                    current.Value.Seek(33., true))
            current <- Some state
            state.Reverse()
            state.Seek(25., true)
            interrupt <- true
            state.Reset()
            Expect.equal state.Sample.CurrentTime 33. "inner sample survives reset"
            Expect.isTrue state.Began "outer reset cannot clear inner state"

        testCase "nonfinite playback speed is rejected without changing state" <| fun _ ->
            let state = PlaybackState(100., 1., 0., 0., false, false, ignore)
            state.Speed <- 2.
            let generation = state.Generation
            for value in [nan; infinity; -infinity] do
                Expect.throwsT<ArgumentException> (fun () -> state.Speed <- value) "invalid speed"
            Expect.equal state.Speed 2. "speed retained"
            Expect.equal state.Generation generation "invalid input retains generation"
    ]

let tests = testList "standalone playback state" [referenceTests; lifetimeTests]
