module Ranvier.Anime.Tests.CurveTests

open System
open System.IO
open System.Text.Json
open Expecto
open Ranvier.Anime

let private cases section =
    let root = Environment.GetEnvironmentVariable "RANVIER_ANIME_REFERENCE_ROOT"
    let path = Path.Combine((if String.IsNullOrWhiteSpace root then Path.Combine(AppContext.BaseDirectory, "reference") else root), "easing-curves.json")
    use document = JsonDocument.Parse(File.ReadAllText path)
    if document.RootElement.GetProperty("upstreamCommit").GetString() <> "2c9cf8ea00329f6768c7d7902252ed977d75ce42" then failwith "Unexpected easing oracle revision."
    document.RootElement.GetProperty(section:string).EnumerateArray() |> Seq.map _.Clone() |> Seq.toList

let private number (value:JsonElement) =
    if value.ValueKind = JsonValueKind.Number then value.GetDouble()
    else
        match value.GetString() with
        | "NaN" -> Double.NaN
        | "Infinity" -> Double.PositiveInfinity
        | "-Infinity" -> Double.NegativeInfinity
        | "-0" -> -0.
        | text -> failwith ("Unexpected oracle value " + text)

let private verify evaluate (entry:JsonElement) =
    for sample in entry.GetProperty("samples").EnumerateArray() do
        let time = sample[0].GetDouble()
        let expected = number sample[1]
        let actual = evaluate time
        let equal =
            if Double.IsNaN expected then Double.IsNaN actual
            elif Double.IsInfinity expected then actual = expected
            else Double.IsFinite actual && abs(actual - expected) <= 1e-12 * max 1. (abs expected)
        Expect.isTrue equal $"at {time}: expected {expected:R}, got {actual:R}"

let private stop (point:JsonElement) =
    let value = point.GetProperty("value").GetDouble()
    let position = point.GetProperty "position"
    if position.ValueKind = JsonValueKind.Null then LinearStop.Value value else LinearStop.At(value, position.GetDouble())

let tests = testList "remaining easing curves" [
    for entry in cases "linear" do
        testCase (entry.GetProperty("name").GetString()) <| fun _ ->
            let points = entry.GetProperty("points").EnumerateArray() |> Seq.map stop |> Seq.toArray
            verify (Ease.linear points) entry
    for entry in cases "irregular" do
        testCase (entry.GetProperty("name").GetString()) <| fun _ ->
            let draws = entry.GetProperty("draws").EnumerateArray() |> Seq.map _.GetDouble() |> Seq.toArray
            let mutable calls = 0
            let draw () =
                let result = draws[calls % draws.Length]
                calls <- calls + 1
                result
            let evaluate = Ease.irregularWith draw (entry.GetProperty("count").GetUInt32()) (entry.GetProperty("randomness").GetDouble())
            Expect.equal calls (entry.GetProperty("calls").GetInt32()) "construction consumes one draw per interior stop"
            let constructed = calls
            verify evaluate entry
            verify evaluate entry
            Expect.equal calls constructed "sampling retains the generated curve"
    for entry in cases "families" do
        testCase (entry.GetProperty("name").GetString()) <| fun _ ->
            let args = entry.GetProperty("parameters").EnumerateArray() |> Seq.map _.GetDouble() |> Seq.toArray
            let family =
                match entry.GetProperty("family").GetString() with
                | "Power" -> Power args[0]
                | "Back" -> Back args[0]
                | "Elastic" -> Elastic(args[0], args[1])
                | name -> failwith name
            let direction =
                match entry.GetProperty("direction").GetString() with
                | "in" -> In | "out" -> Out | "inOut" -> InOut | "outIn" -> OutIn
                | name -> failwith name
            verify (Ease.create family direction) entry
    for entry in cases "beziers" do
        testCase (entry.GetProperty("name").GetString()) <| fun _ ->
            let args = entry.GetProperty("parameters").EnumerateArray() |> Seq.map _.GetDouble() |> Seq.toArray
            verify (Ease.cubicBezier args[0] args[1] args[2] args[3]) entry
    for entry in cases "steps" do
        testCase (entry.GetProperty("name").GetString()) <| fun _ ->
            verify (Ease.steps (entry.GetProperty("count").GetUInt32()) (entry.GetProperty("fromStart").GetBoolean())) entry
    testCase "linear captures its caller array" <| fun _ ->
        let points = [| LinearStop.Value 0.; LinearStop.At(2., 0.5); LinearStop.Value 1. |]
        let evaluate = Ease.linear points
        points[1] <- LinearStop.At(50., 0.5)
        Expect.equal (evaluate 0.5) 2. "constructed values are immutable"
    testCase "nonfinite authored stops are rejected" <| fun _ ->
        for points in [ [|LinearStop.Value nan|]; [|LinearStop.Value infinity|]; [|LinearStop.At(1., nan)|]; [|LinearStop.At(1., -infinity)|] ] do
            Expect.throwsT<ArgumentException> (fun () -> Ease.linear points |> ignore) "authored values and positions are finite"
        Expect.throwsT<ArgumentNullException> (fun () -> Ease.linear null |> ignore) "null stop array"
    testCase "curve evaluation rejects nonfinite progress" <| fun _ ->
        for evaluate in [Ease.linear [||]; Ease.linear [|LinearStop.Value 3.|]; Ease.irregular 3u 1.] do
            for time in [nan; infinity; -infinity] do
                Expect.throwsT<ArgumentException> (fun () -> evaluate time |> ignore) "finite progress required"
    testCase "irregular validates before drawing" <| fun _ ->
        let mutable calls = 0
        let draw () = calls <- calls + 1; 0.5
        for randomness in [nan; infinity; -infinity] do
            Expect.throwsT<ArgumentException> (fun () -> Ease.irregularWith draw 10u randomness |> ignore) "finite randomness required"
        Expect.throwsT<ArgumentException> (fun () -> Ease.irregularWith draw UInt32.MaxValue 1. |> ignore) "array capacity checked"
        Expect.throwsT<ArgumentNullException> (fun () -> Ease.irregularWith Unchecked.defaultof<_> 3u 1. |> ignore) "random source required"
        Expect.equal calls 0 "invalid authoring has no random effects"
    testCase "injected random draws follow Math.random range" <| fun _ ->
        for value in [nan; infinity; -infinity; -0.001; 1.; 2.] do
            Expect.throwsT<ArgumentException> (fun () -> Ease.irregularWith (fun () -> value) 3u 1. |> ignore) "draw must be finite in [0,1)"
    testCase "injected random faults retain their identity" <| fun _ ->
        let expected = InvalidOperationException "random failure"
        let mutable actual:exn option = None
        try Ease.irregularWith (fun () -> raise expected) 3u 1. |> ignore with error -> actual <- Some error
        Expect.isTrue (actual |> Option.exists(fun error -> obj.ReferenceEquals(error, expected))) "random failure is not replaced"
    testCase "shared random creates a stable bounded curve" <| fun _ ->
        let evaluate = Ease.irregular 10u 1.
        let first = [|for index in 0..100 -> evaluate(float index / 100.)|]
        let second = [|for index in 0..100 -> evaluate(float index / 100.)|]
        Expect.sequenceEqual second first "repeated sampling is stable"
        Expect.equal first[0] 0. "initial endpoint"
        Expect.equal first[100] 1. "final endpoint"
        for index in 1..100 do Expect.isTrue (first[index] >= first[index-1] && first[index] <= 1.) "irregular curve is monotonic"
    testCase "linear stop diagnostics are invariant" <| fun _ ->
        Expect.equal ((LinearStop.At(1.25, 0.5)).ToString()) "At (1.25, 0.5)" "BCL roundtrip diagnostic"
    testCase "piecewise overshoot reaches the numeric runner" <| fun _ ->
        let mutable value = 0.
        let scheduler = new Ranvier.Dom.ManualFrameScheduler()
        let evaluate = Ease.linear [|LinearStop.Value 0.; LinearStop.At(1.5, 0.5); LinearStop.Value 1.|]
        use animation = new NumericAnimation(scheduler, (fun () -> value), (fun next -> value <- next), 100., 100., ease=evaluate)
        animation.Seek 50.
        Expect.equal value 150. "piecewise overshoot is not clamped"
        animation.Seek 100.
        Expect.equal value 100. "final stop"
    testCase "irregular curve reaches the numeric runner without resampling" <| fun _ ->
        let mutable draws = 0
        let evaluate = Ease.irregularWith (fun () -> draws <- draws + 1; 0.5) 3u 1.
        let mutable value = 0.
        let scheduler = new Ranvier.Dom.ManualFrameScheduler()
        use animation = new NumericAnimation(scheduler, (fun () -> value), (fun next -> value <- next), 100., 100., ease=evaluate)
        animation.Seek 50.
        Expect.equal value 75. "source generated midpoint"
        animation.Seek 25.
        animation.Seek 50.
        Expect.equal value 75. "backward/forward seek retains curve"
        Expect.equal draws 1 "sampling draws once at construction"
]
