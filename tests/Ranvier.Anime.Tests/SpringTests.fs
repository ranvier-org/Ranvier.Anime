module Ranvier.Anime.Tests.SpringTests

open System
open System.IO
open System.Text.Json
open Expecto
open Ranvier.Anime
open Ranvier.Dom

let private fixtures section =
    use document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "reference", "pure.json")))
    [for item in document.RootElement.GetProperty(section: string).EnumerateArray() -> item.Clone()]

let private option name (parameters: JsonElement) =
    match parameters.TryGetProperty(name: string) with
    | true, value -> Some(value.GetDouble())
    | _ -> None

let private create parameters =
    new Spring(?mass = option "mass" parameters, ?stiffness = option "stiffness" parameters,
               ?damping = option "damping" parameters, ?velocity = option "velocity" parameters,
               ?bounce = option "bounce" parameters, ?duration = option "duration" parameters)

let private compare (spring: Spring) (fixture: JsonElement) =
    for name, actual in ["mass", spring.Mass; "stiffness", spring.Stiffness; "damping", spring.Damping;
                         "velocity", spring.Velocity; "bounce", spring.Bounce; "duration", spring.Duration;
                         "solverDuration", spring.SolverDuration; "settlingDuration", spring.SettlingDuration] do
        let expected = fixture.GetProperty(name).GetDouble()
        Expect.isTrue (abs(actual - expected) <= 1e-8) $"{name}: expected {expected:R}, got {actual:R}"
    for pair in fixture.GetProperty("values").EnumerateArray() do
        let time, expected = pair[0].GetDouble(), pair[1].GetDouble()
        let actual = spring.Ease time
        Expect.isTrue (abs(actual - expected) <= 1e-10) $"at {time}: expected {expected:R}, got {actual:R}"

let tests = testList "upstream springs" [
    testCase "a spring animation uses settling duration rather than perceived duration" <| fun _ ->
        let fixture = fixtures "springs" |> List.find(fun item -> item.GetProperty("caseName").GetString() = "physical")
        let expected = fixture.GetProperty("values").EnumerateArray() |> Seq.find(fun pair -> pair[0].GetDouble() = 0.5)
        let clock = new ManualFrameScheduler()
        let spring = new Spring(stiffness = 170., damping = 11.)
        let mutable value = 0.
        use animation = Animation.animateSpring spring clock (fun () -> value) (fun next -> value <- next) 100.
        clock.Advance(spring.SettlingDuration / 2.)
        clock.RunFrame()
        Expect.isFalse animation.Completed "only half of settling time elapsed"
        Expect.isTrue (abs(value - expected[1].GetDouble() * 100.) < 1e-8) "upstream spring midpoint"

    testCase "spring perceived completion can cancel the active animation before its write" <| fun _ ->
        let clock = new ManualFrameScheduler()
        let values = ResizeArray<float>()
        let mutable current: NumericAnimation option = None
        let spring = new Spring(onComplete = (fun () -> current |> Option.iter(fun animation -> animation.Cancel())))
        use animation = Animation.animateSpring spring clock (fun () -> 0.) values.Add 100.
        current <- Some animation
        clock.Advance(spring.Duration + 1.)
        clock.RunFrame()
        Expect.sequenceEqual values [0.] "completion cancellation suppresses the in-flight sample"

    testCase "unsigned perceived duration is converted and clamped using source rules" <| fun _ ->
        Expect.equal (Spring.perceivedMilliseconds 500u 0.25).Duration 500. "ordinary duration"
        Expect.equal (Spring.perceivedMilliseconds 0u 0.25).Duration 10. "source minimum"
        Expect.equal (Spring.perceivedMilliseconds UInt32.MaxValue 0.25).Duration 10000. "source maximum"

    for fixture in fixtures "springs" do
        testCase (fixture.GetProperty("caseName").GetString()) <| fun _ ->
            compare (create (fixture.GetProperty "parameters")) fixture

    testCase "sequential property mutations recompute the correct companion fields" <| fun _ ->
        let spring = new Spring()
        for fixture in fixtures "springMutations" do
            let value = fixture.GetProperty("value").GetDouble()
            match fixture.GetProperty("property").GetString() with
            | "stiffness" -> spring.Stiffness <- value
            | "damping" -> spring.Damping <- value
            | "mass" -> spring.Mass <- value
            | "velocity" -> spring.Velocity <- value
            | "bounce" -> spring.Bounce <- value
            | "duration" -> spring.Duration <- value
            | property -> failwithf "Unknown mutation %s" property
            compare spring fixture

    testCase "perceived completion resets on a backward sample and can fire again" <| fun _ ->
        let mutable calls = 0
        let spring = new Spring(onComplete = (fun () -> calls <- calls + 1))
        spring.Ease 1. |> ignore
        spring.Ease 1. |> ignore
        Expect.equal calls 1 "one forward crossing"
        spring.Ease 0. |> ignore
        Expect.isFalse spring.Completed "backward sample resets completion"
        spring.Ease 1. |> ignore
        Expect.equal calls 2 "second forward crossing"

    testCase "nonfinite parameters are rejected" <| fun _ ->
        Expect.throwsT<ArgumentException> (fun () -> Spring(stiffness = nan) |> ignore) "nonfinite stiffness"
]
