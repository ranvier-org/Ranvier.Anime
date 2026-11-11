module Ranvier.Anime.Tests.TimelineClockTests

open System
open System.Text.Json
open Expecto
open Ranvier.Anime

let private optionalNumber name fallback (value:JsonElement) =
    match value.TryGetProperty(name:string) with true, field -> field.GetDouble() | _ -> fallback

let private optionalBool name (value:JsonElement) =
    match value.TryGetProperty(name:string) with true, field -> field.GetBoolean() | _ -> false

let private verifyState (state:PlaybackState) (expected:JsonElement) =
    for key, actual in ["time",state.Sample.CurrentTime;"iterationTime",state.Sample.IterationCurrentTime] do
        Expect.equal actual (expected.GetProperty(key).GetDouble()) key
    Expect.equal state.Sample.Iteration (expected.GetProperty("iteration").GetInt32()) "iteration"
    for key, actual in ["backwards",state.Sample.Backwards;"paused",state.Paused;"began",state.Began;"completed",state.Completed] do
        Expect.equal actual (expected.GetProperty(key).GetBoolean()) key

let private verifyClock (fixture:JsonElement) =
    let parameters = fixture.GetProperty "parameters"
    let iterations =
        match parameters.TryGetProperty "loop" with
        | true, field when field.ValueKind = JsonValueKind.True -> infinity
        | true, field -> field.GetDouble() + 1.
        | _ -> 1.
    let duration = fixture.GetProperty("iterationDuration").GetDouble()
    let mutable expectedEvents : JsonElement array = [||]
    let mutable eventIndex = 0
    let mutable checking = false
    let mutable active : PlaybackState option = None
    let callback event =
        if checking then
            Expect.isLessThan eventIndex expectedEvents.Length "unexpected root callback"
            let expected = expectedEvents[eventIndex]
            Expect.equal (string event) (expected.GetProperty("event").GetString()) "root phase"
            verifyState active.Value (expected.GetProperty "self")
            eventIndex <- eventIndex + 1
    let state = PlaybackState(duration,iterations,optionalNumber "delay" 0. parameters,optionalNumber "loopDelay" 0. parameters,
                              optionalBool "reversed" parameters,optionalBool "alternate" parameters,callback)
    active <- Some state
    let mutable childrenComplete = false
    state.ConfigureChildren((fun _ _ _ -> ()),(fun _ _ _ -> childrenComplete))
    state.Reset()
    checking <- true
    verifyState state (fixture.GetProperty("initial").GetProperty "root")
    for sample in fixture.GetProperty("samples").EnumerateArray() do
        expectedEvents <-
            sample.GetProperty("events").EnumerateArray()
            |> Seq.filter(fun event -> event.GetProperty("id").GetString()="root" && event.GetProperty("event").GetString()<>"Render")
            |> Seq.toArray
        eventIndex <- 0
        childrenComplete <- sample.GetProperty("state").GetProperty("tracks").EnumerateArray() |> Seq.forall(fun track -> track.GetProperty("completed").GetBoolean())
        let step = sample.GetProperty "step"
        match step.GetProperty("op").GetString() with
        | "seek" -> state.Seek(step.GetProperty("time").GetDouble(),optionalBool "mute" step)
        | "reset" -> state.Reset()
        | "complete" -> state.Complete(optionalBool "mute" step)
        | op -> failtestf "Unsupported clock operation %s" op
        Expect.equal eventIndex expectedEvents.Length "root callback count"
        verifyState state (sample.GetProperty("state").GetProperty "root")

let private clockTests (root:JsonElement) =
    root.GetProperty("fixtures").EnumerateArray()
    |> Seq.filter(fun fixture ->
        fixture.GetProperty("samples").EnumerateArray()
        |> Seq.forall(fun sample ->
            let op = sample.GetProperty("step").GetProperty("op").GetString()
            op="seek" || op="reset" || op="complete"))
    |> Seq.map(fun fixture -> let value=fixture.Clone() in testCase (value.GetProperty("name").GetString()) (fun _ -> verifyClock value))
    |> Seq.toList |> testList "timeline root clock phases"

let private interruptionTests = testList "timeline clock interruption" [
    testCase "loop preparation precedes loop and child rendering" <| fun _ ->
        let phases = ResizeArray<string>()
        let state = PlaybackState(100.,3.,0.,25.,false,false,fun event -> phases.Add(string event))
        state.ConfigureChildren((fun _ _ _ -> phases.Add "prepare"),(fun _ _ _ -> phases.Add "children"; false))
        state.Seek(125.,false)
        Expect.sequenceEqual phases ["Begin";"BeforeUpdate";"Update";"prepare";"Loop";"children"] "phase ordering"

    testCase "child rendering continues when the root render tick is skipped" <| fun _ ->
        let phases = ResizeArray<string>()
        let state = PlaybackState(100.,1.,0.,0.,false,false,fun event -> phases.Add(string event))
        state.ConfigureChildren((fun _ _ _ -> ()),(fun _ mode _ -> phases.Add(mode.ToString()); false))
        state.Tick(25.,PlaybackTickMode.Skip,false)
        Expect.sequenceEqual phases ["Begin";"Skip"] "children choose their own eligibility"

    testCase "completion waits for the child aggregate at the endpoint" <| fun _ ->
        let phases = ResizeArray<string>()
        let mutable childrenComplete = false
        let state = PlaybackState(100.,1.,0.,0.,false,false,fun event -> phases.Add(string event))
        state.ConfigureChildren((fun _ _ _ -> ()),(fun _ _ _ -> childrenComplete))
        state.Seek(100.,false)
        Expect.isTrue state.Paused "clock stops at its endpoint"
        Expect.isFalse state.Completed "unfinished child defers completion"
        Expect.isFalse (phases.Contains "Complete") "no completion callback"
        childrenComplete <- true
        state.Tick(100.,PlaybackTickMode.Skip,false)
        Expect.isTrue state.Completed "completed aggregate commits"
        Expect.equal (phases |> Seq.filter((=) "Complete") |> Seq.length) 1 "completion once"

    testCase "loop preparation cancellation prevents loop and later children" <| fun _ ->
        let phases = ResizeArray<string>()
        let mutable active : PlaybackState option = None
        let state = PlaybackState(100.,3.,0.,25.,false,false,fun event -> phases.Add(string event))
        active <- Some state
        state.ConfigureChildren((fun _ _ _ -> active.Value.Cancel()),(fun _ _ _ -> phases.Add "children"; true))
        state.Seek(125.,false)
        Expect.sequenceEqual phases ["Begin";"BeforeUpdate";"Update"] "interrupted phases"
        Expect.isTrue state.Cancelled "terminal lifetime retained"

    testCase "a loop seek supersedes the outer child render" <| fun _ ->
        let mutable active : PlaybackState option = None
        let mutable renders = 0
        let state = PlaybackState(100.,3.,0.,25.,false,false,fun event ->
            if event = PlaybackEvent.Loop then active.Value.Seek(20.,true))
        active <- Some state
        state.ConfigureChildren((fun _ _ _ -> ()),(fun _ _ _ -> renders <- renders+1; false))
        state.Seek(125.,false)
        Expect.equal state.Sample.CurrentTime 20. "inner sample retained"
        Expect.equal renders 1 "only the inner render executes"

    testCase "child cancellation prevents outer completion" <| fun _ ->
        let mutable active : PlaybackState option = None
        let phases = ResizeArray<string>()
        let state = PlaybackState(100.,1.,0.,0.,false,false,fun event -> phases.Add(string event))
        active <- Some state
        state.ConfigureChildren((fun _ _ _ -> ()),(fun _ _ _ -> active.Value.Cancel(); true))
        state.Seek(100.,false)
        Expect.isFalse state.Completed "cancelled child render cannot complete its ancestor"
        Expect.isFalse (phases.Contains "Complete") "completion suppressed"
]

let tests =
    let root =
        match Environment.GetEnvironmentVariable "RANVIER_ANIME_REFERENCE_ROOT" with
        | null | "" -> IO.Path.Combine(AppContext.BaseDirectory,"reference")
        | value -> value
    use document = JsonDocument.Parse(IO.File.ReadAllText(IO.Path.Combine(root,"timeline-playback.json")))
    testList "timeline shared clock" [clockTests document.RootElement; interruptionTests]

