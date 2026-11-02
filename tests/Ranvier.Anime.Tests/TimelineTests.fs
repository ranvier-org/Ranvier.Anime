module Ranvier.Anime.Tests.TimelineTests

open System
open System.IO
open System.Text.Json
open Expecto
open Ranvier.Dom
open Ranvier.Anime
open Ranvier

let private fixtures property =
    use document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "reference", "pure.json")))
    document.RootElement.GetProperty(property: string).EnumerateArray() |> Seq.map _.Clone() |> Seq.toList

let private animation clock duration initial target =
    let value = ref initial
    let tween = new NumericAnimation(clock, (fun () -> value.Value), (fun number -> value.Value <- number), target, duration)
    tween, value

let tests = testList "timeline synchronization" [
    for fixture in fixtures "timelinePositions" do
        let position = match fixture.TryGetProperty "position" with
                       | false, _ -> None
                       | true, value when value.ValueKind = JsonValueKind.Number -> Some(TimelinePosition.At(value.GetDouble()))
                       | true, value -> Some(TimelinePosition.Expression(value.GetString()))
        testCase $"position {position}" <| fun _ ->
            let clock = new ManualFrameScheduler()
            use timeline = new Timeline(clock)
            let long, _ = animation clock 350. 0. 1.
            let previous, _ = animation clock 80. 0. 1.
            use long = long
            use previous = previous
            timeline.Sync(long, TimelinePosition.At 0.).Sync(previous, TimelinePosition.At 120.)
                .Label("intro", TimelinePosition.At 50.).Label("outro", TimelinePosition.At 300.) |> ignore
            let expected = fixture.GetProperty "result"
            if expected.ValueKind = JsonValueKind.String then
                Expect.throwsT<ArgumentException> (fun () -> timeline.ResolvePosition(?position = position) |> ignore) "nonfinite relative position"
            else
                Expect.equal (timeline.ResolvePosition(?position = position)) (expected.GetDouble()) "source position"
    for fixture in fixtures "timelineSync" do
        testCase (fixture.GetProperty("name").GetString()) <| fun _ ->
            let clock = new ManualFrameScheduler()
            let first, a = animation clock 100. 0. 100.
            let second, b = animation clock 80. 10. 90.
            use first = first
            use second = second
            use timeline = new Timeline(clock)
            timeline.Sync(first, TimelinePosition.At 0.)
                .Label("second", TimelinePosition.At(fixture.GetProperty("secondPosition").GetDouble()))
                .Sync(second, TimelinePosition.Expression "second") |> ignore
            Expect.equal timeline.Duration (fixture.GetProperty("duration").GetDouble()) "source timeline duration"
            for sample in fixture.GetProperty("samples").EnumerateArray() do
                timeline.Seek(sample.GetProperty("time").GetDouble())
                Expect.floatClose Accuracy.high a.Value (sample.GetProperty("a").GetDouble()) "first child output"
                Expect.floatClose Accuracy.high b.Value (sample.GetProperty("b").GetDouble()) "second child output"
    for fixture in fixtures "timelineStaggering" do
        testCase (fixture.GetProperty("name").GetString()) <| fun _ ->
            let clock = new ManualFrameScheduler()
            use timeline = new Timeline(clock)
            let baseline, _ = animation clock 200. 0. 1.
            use baseline = baseline
            timeline.Sync baseline |> ignore
            let pairs = Array.init 4 (fun _ -> animation clock 10. 0. 10.)
            try
                let input = fixture.GetProperty "value"
                let value = if input.ValueKind = JsonValueKind.Array then StaggerValue.Range(input[0].GetDouble(), input[1].GetDouble()) else StaggerValue.Step(input.GetDouble())
                let parameters = fixture.GetProperty "parameters"
                let field name = match parameters.TryGetProperty(name: string) with true, value -> Some value | _ -> None
                let options =
                    { StaggerOptions.Default with
                        Start = field "start" |> Option.map _.GetDouble()
                        Origin = if (field "from").IsSome then StaggerOrigin.Random else StaggerOrigin.First
                        Seed = field "seed" |> Option.map _.GetDouble()
                        Modifier = field "modifier" |> Option.map(fun _ -> fun number -> number * 2.)
                        Jitter = field "jitter" |> Option.map(fun item -> StaggerJitter.Range(item[0].GetDouble(), item[1].GetDouble())) }
                timeline.SyncStaggered(pairs |> Array.map fst, value, options) |> ignore
                let offsets = fixture.GetProperty("offsets").EnumerateArray() |> Seq.map _.GetDouble() |> Seq.toArray
                Expect.floatClose Accuracy.high (timeline.ResolvePosition(TimelinePosition.Expression "<<")) offsets[3] "last source offset"
                Expect.floatClose Accuracy.high timeline.Duration (max 200. (Array.max offsets + 10.)) "source group duration"
                for index in 0 .. 3 do
                    timeline.Seek(offsets[index] + 5.)
                    Expect.floatClose Accuracy.high (snd pairs[index]).Value 5. "sample at source offset plus half duration"
            finally
                pairs |> Array.iter(fun (child, _) -> (child :> IDisposable).Dispose())
    testCase "manual frames pause resume reset and complete" <| fun _ ->
        let clock = new ManualFrameScheduler()
        let child, value = animation clock 100. 0. 100.
        use child = child
        use timeline = new Timeline(clock)
        timeline.Sync child |> ignore
        timeline.Play()
        clock.Advance 25.; clock.RunFrame()
        Expect.equal value.Value 25. "scheduled child sample"
        timeline.Pause()
        clock.Advance 25.; clock.RunFrame()
        Expect.equal value.Value 25. "paused"
        timeline.Play()
        clock.Advance 25.; clock.RunFrame()
        Expect.equal value.Value 50. "resumed"
        timeline.Complete()
        Expect.equal value.Value 100. "complete"
        Expect.isTrue timeline.Completed "completion state"
        timeline.Reset()
        Expect.equal value.Value 0. "rewound"
        Expect.isFalse timeline.Playing "reset remains paused"
        timeline.Complete()
        timeline.Restart()
        clock.Advance 25.; clock.RunFrame()
        Expect.equal value.Value 25. "restart schedules from zero"
    testCase "reentrant cancellation prevents later sibling writes" <| fun _ ->
        let clock = new ManualFrameScheduler()
        use timeline = new Timeline(clock)
        let mutable armed = false
        use first = new NumericAnimation(clock, (fun () -> 0.), (fun _ -> if armed then timeline.Cancel()), 100., 100.)
        let second, value = animation clock 100. 0. 100.
        use second = second
        timeline.Sync(first, TimelinePosition.At 0.).Sync(second, TimelinePosition.At 0.) |> ignore
        armed <- true
        timeline.Seek 50.
        Expect.equal value.Value 0. "cancelled outer sample stops before sibling"
    testCase "cancellation inside child easing prevents that child's write" <| fun _ ->
        let clock = new ManualFrameScheduler()
        use timeline = new Timeline(clock)
        let mutable armed = false
        let mutable value = 0.
        use child = new NumericAnimation(clock, (fun () -> value), (fun number -> value <- number), 100., 100.,
            ease = (fun progress ->
                if armed then timeline.Cancel()
                progress))
        timeline.Sync child |> ignore
        armed <- true
        timeline.Seek 50.
        Expect.equal value 0. "no write after parent lifetime ends"
        armed <- false
        child.Seek 25.
        Expect.equal value 25. "borrowed child remains live"
    testCase "repeated synchronized child follows backward source samples" <| fun _ ->
        let clock = new ManualFrameScheduler()
        let child, value = animation clock 100. 0. 100.
        use child = child
        use timeline = new Timeline(clock)
        timeline.Sync(child, TimelinePosition.At 0.).Sync(child, TimelinePosition.At 100.) |> ignore
        for sample in fixtures "timelineRepeatedSync" do
            timeline.Seek(sample.GetProperty("time").GetDouble())
            Expect.equal value.Value (sample.GetProperty("value").GetDouble()) "repeated child source output"
    testCase "reentrant seek supersedes the outer sibling sample" <| fun _ ->
        let clock = new ManualFrameScheduler()
        use timeline = new Timeline(clock)
        let mutable armed = false
        use first = new NumericAnimation(clock, (fun () -> 0.), (fun _ -> if armed then armed <- false; timeline.Seek 10.), 100., 100.)
        let second, value = animation clock 100. 0. 100.
        use second = second
        timeline.Sync(first, TimelinePosition.At 0.).Sync(second, TimelinePosition.At 0.) |> ignore
        armed <- true
        timeline.Seek 50.
        Expect.equal value.Value 10. "nested seek wins"
        Expect.equal timeline.CurrentTime 10. "nested timeline time wins"
    testCase "cancellation releases control without disposing a borrowed child" <| fun _ ->
        let clock = new ManualFrameScheduler()
        let child, value = animation clock 100. 0. 100.
        use child = child
        use timeline = new Timeline(clock)
        timeline.Sync child |> ignore
        timeline.Play()
        timeline.Cancel()
        clock.Advance 50.; clock.RunFrame()
        Expect.equal value.Value 0. "no pending timeline sample"
        child.Seek 50.
        Expect.equal value.Value 50. "borrowed child remains usable"
        Expect.throwsT<ObjectDisposedException> timeline.Play "terminal timeline cancellation"
    testCase "owning Ranvier scope cancels scheduled timeline writes" <| fun _ ->
        use graph = new Graph()
        use activation = graph.Activate()
        let clock = new ManualFrameScheduler()
        let child, value = animation clock 100. 0. 100.
        use child = child
        let owner = Api.createRoot(fun owner ->
            let timeline = new Timeline(clock) |> Timeline.own
            timeline.Sync child |> ignore
            timeline.Play()
            owner)
        owner.Dispose()
        clock.Advance 50.; clock.RunFrame()
        Expect.equal value.Value 0. "owner ended timeline lifetime"
]
