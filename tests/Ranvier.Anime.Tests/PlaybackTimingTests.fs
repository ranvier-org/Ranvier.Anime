module Ranvier.Anime.Tests.PlaybackTimingTests

open System
open System.Globalization
open System.IO
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

let private sameFloat name expected actual =
    if Double.IsNaN expected then Expect.isTrue (Double.IsNaN actual) name
    else Expect.equal actual expected name

let private verify (fixture: JsonElement) =
    let parameters = fixture.GetProperty "parameters"
    let duration = optionalNumber "duration" infinity parameters
    let iterations =
        match parameters.TryGetProperty "loop" with
        | true, value when value.ValueKind = JsonValueKind.True -> infinity
        | true, value -> number value + 1.
        | _ -> 1.
    let delay = optionalNumber "delay" 0. parameters
    let loopDelay = optionalNumber "loopDelay" 0. parameters
    let reversed, alternate = optionalBool "reversed" parameters, optionalBool "alternate" parameters
    let clock = PlaybackTiming(duration, iterations, delay, loopDelay)
    sameFloat "duration" (number (fixture.GetProperty "duration")) clock.Duration
    let mode = fixture.GetProperty("mode").GetString()
    let tickMode = match mode with "force" -> PlaybackTickMode.Force | "skip" -> PlaybackTickMode.Skip | _ -> PlaybackTickMode.Auto
    for expected in fixture.GetProperty("samples").EnumerateArray() do
        let time = number(expected.GetProperty "time")
        let previous = number(expected.GetProperty "previousAbsoluteTime")
        let completed = mode <> "seek" && expected.GetProperty("previousCompleted").GetBoolean()
        let actual = clock.Sample(previous, time + delay, reversed, alternate, completed, tickMode)
        for name, actual in ["absoluteTime", actual.AbsoluteTime; "previousBoundedTime", actual.PreviousTime; "boundedTime", actual.Time
                             "currentTime", actual.CurrentTime; "iterationTime", actual.IterationTime
                             "iterationCurrentTime", actual.IterationCurrentTime; "progress", actual.Progress; "iterationProgress", actual.IterationProgress] do
            sameFloat (name + " at " + time.ToString("R", CultureInfo.InvariantCulture)) (number(expected.GetProperty name)) actual
        Expect.equal actual.Iteration (expected.GetProperty("iteration").GetInt32()) "iteration index"
        Expect.equal actual.Backwards (expected.GetProperty("backwards").GetBoolean()) "sampling direction"
        Expect.equal (actual.ShouldRender && actual.AboveZero) (expected.GetProperty("updated").GetBoolean()) "source before-update eligibility"

let tests =
    let referenceRoot =
        match Environment.GetEnvironmentVariable "RANVIER_ANIME_REFERENCE_ROOT" with
        | null | "" -> Path.Combine(AppContext.BaseDirectory, "reference")
        | root -> root
    use document = JsonDocument.Parse(File.ReadAllText(Path.Combine(referenceRoot, "pure.json")))
    document.RootElement.GetProperty("timerTiming").EnumerateArray()
    |> Seq.map(fun fixture ->
        let fixture = fixture.Clone()
        testCase (fixture.GetProperty("name").GetString()) (fun _ -> verify fixture))
    |> Seq.toList
    |> testList "playback timing"
