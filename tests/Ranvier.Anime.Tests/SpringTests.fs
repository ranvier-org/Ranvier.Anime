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
    testSequenced <| testCase "spring animations settle at the same host time in both units" (fun _ ->
        for unit, scale in [EngineTimeUnit.Milliseconds, 1.; EngineTimeUnit.Seconds, 0.001] do
            use host = new ManualFrameScheduler()
            let engine = Engine.forScheduler host
            engine.TimeUnit <- unit
            try
                let spring = Spring()
                let mutable value = 0.
                use animation = Animation.animateSpring spring host (fun () -> value) (fun v -> value <- v) 1.
                Expect.equal animation.Duration (spring.SettlingDuration * scale) "public duration in engine units"
                host.Advance(spring.SettlingDuration / 2. - 12.)
                host.RunFrame()
                Expect.isLessThan (abs(value - spring.Ease 0.5)) 1e-8 "same physical midpoint"
                host.Advance(spring.SettlingDuration / 2.)
                host.RunFrame()
                Expect.isTrue animation.Completed "natural settling time"
            finally engine.TimeUnit <- EngineTimeUnit.Milliseconds)

    testCase "a spring animation uses settling duration rather than perceived duration" <| fun _ ->
        let fixture = fixtures "springs" |> List.find(fun item -> item.GetProperty("caseName").GetString() = "physical")
        let expected = fixture.GetProperty("values").EnumerateArray() |> Seq.find(fun pair -> pair[0].GetDouble() = 0.5)
        let clock = new ManualFrameScheduler()
        let spring = new Spring(stiffness = 170., damping = 11.)
        let mutable value = 0.
        use animation = Animation.animateSpring spring clock (fun () -> value) (fun next -> value <- next) 100.
        clock.Advance(spring.SettlingDuration / 2. - 12.)
        clock.RunFrame()
        Expect.isFalse animation.Completed "only half of settling time elapsed"
        Expect.isTrue (abs(value - expected[1].GetDouble() * 100.) < 1e-8) "upstream spring midpoint"

    testCase "a stretched spring plays its original midpoint and finishes on the stretched clock" <| fun _ ->
        let clock = new ManualFrameScheduler()
        let spring = Spring(stiffness = 170., damping = 11.)
        let midpoint = spring.Ease 0.5
        let duration = spring.SettlingDuration * 3.
        let mutable value = 0.
        use animation = Animation.animateSpring (spring.Stretch duration) clock (fun () -> value) (fun next -> value <- next) 100.
        Expect.equal animation.Duration duration "animation adopts the stretched duration"
        clock.Advance(duration / 2. - 12.) // Resume includes the clock lead.
        clock.RunFrame()
        Expect.equal animation.CurrentTime (duration / 2.) "half the stretched duration has elapsed"
        Expect.isFalse animation.Completed "playback continues beyond the natural settling time"
        Expect.isTrue (abs(value - 100. * midpoint) < 1e-8) "original normalized midpoint"
        clock.Advance(duration / 2.)
        clock.RunFrame()
        Expect.isTrue animation.Completed "playback completes at the stretched duration"
        Expect.equal value 100. "target rendered at completion"

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

    testCase "absolute stretch preserves the physical solution and normalized easing samples" <| fun _ ->
        let spring = Spring(stiffness = 170., damping = 11., velocity = 4.)
        let natural = spring.SettlingDuration
        let progress = [-0.1; 0.; 0.15; 0.5; 0.9; 1.; 1.2]
        let samples = List.map spring.Ease progress
        let physical = spring.Solve 0.25, spring.SolverDuration, spring.Bounce, spring.Duration
        for target in [natural * 2.; natural / 4.] do
            Expect.isTrue (Object.ReferenceEquals(spring.Stretch target, spring)) "stretch is chainable"
            Expect.equal spring.SettlingDuration target "absolute playback duration"
            Expect.equal (List.map spring.Ease progress) samples "normalized samples"
            Expect.equal (spring.Solve 0.25, spring.SolverDuration, spring.Bounce, spring.Duration) physical "physical spring"
        spring.Velocity <- spring.Velocity
        Expect.equal spring.SettlingDuration natural "changing a parameter recalculates playback duration"

    testCase "stretch keeps the perceived completion crossing at the same progress" <| fun _ ->
        let mutable calls = 0
        let spring = Spring(onComplete = (fun () -> calls <- calls + 1))
        let threshold = spring.Duration / spring.SettlingDuration
        for target in [spring.SettlingDuration * 3.; spring.MinSettlingDuration] do
            spring.Stretch target |> ignore
            let before = calls
            spring.Ease (threshold - 0.0001) |> ignore
            Expect.isFalse spring.Completed "below the threshold"
            spring.Ease (threshold + 0.0001) |> ignore
            spring.Ease 1. |> ignore
            Expect.equal calls (before + 1) "one crossing after each backward seek"

    testCase "stretch clamps to its positive floor and rejects invalid durations atomically" <| fun _ ->
        let spring = Spring()
        Expect.equal spring.MinSettlingDuration 1. "one millisecond playback floor"
        for target in [0.; spring.MinSettlingDuration / 2.] do
            Expect.equal (spring.Stretch target).SettlingDuration spring.MinSettlingDuration "clamped duration"
        spring.Stretch 500. |> ignore
        for target in [-1.; nan; infinity; -infinity] do
            Expect.throwsT<ArgumentException> (fun () -> spring.Stretch target |> ignore) "invalid duration"
            Expect.equal spring.SettlingDuration 500. "previous playback duration"

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
