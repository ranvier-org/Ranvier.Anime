module Ranvier.Anime.Tests.TimelineTests

open System
open System.IO
open System.Text.Json
open Expecto
open Ranvier.Dom
open Ranvier.Anime
open Ranvier

let private fixtures property =
    let root =
        match Environment.GetEnvironmentVariable "RANVIER_ANIME_REFERENCE_ROOT" with
        | null | "" -> Path.Combine(AppContext.BaseDirectory, "reference")
        | value -> value
    use document = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "pure.json")))
    document.RootElement.GetProperty(property: string).EnumerateArray() |> Seq.map _.Clone() |> Seq.toList

let private animation clock (duration: float) initial target =
    let value = ref initial
    let tween = new NumericAnimation(clock, (fun () -> value.Value), (fun number -> value.Value <- number), target, duration)
    tween, value

let tests = testList "timeline synchronization" [
    for fixture in fixtures "timelineProperties" do
        testCase ("property " + fixture.GetProperty("name").GetString()) <| fun _ ->
            let clock = new ManualFrameScheduler()
            let mutable value = 0.
            let property = NumericProperty((fun () -> value), (fun number -> value <- number))
            use timeline = new Timeline(clock)
            for track in fixture.GetProperty("tracks").EnumerateArray() do
                let field name = match track.TryGetProperty(name: string) with true, value -> Some value | _ -> None
                let position = TimelinePosition.At(track.GetProperty("position").GetDouble())
                match field "set" with
                | Some value -> timeline.Set(property, value.GetDouble(), position) |> ignore
                | None ->
                    timeline.Animate(property, track.GetProperty("to").GetDouble(), track.GetProperty("duration").GetDouble(), position,
                        ?from = (field "from" |> Option.map _.GetDouble()),
                        ?ease = (field "ease" |> Option.map(fun value -> if value.GetString() = "constantOvershoot" then fun _ -> 1.25 else Ease.named(value.GetString()))),
                        ?modifier = (field "modifier" |> Option.map(fun _ -> fun number -> number * 2.))) |> ignore
            Expect.equal value (fixture.GetProperty("initial").GetDouble()) "source initialization"
            Expect.floatClose Accuracy.high timeline.Duration (fixture.GetProperty("duration").GetDouble()) "source property duration"
            for sample in fixture.GetProperty("samples").EnumerateArray() do
                timeline.Seek(sample.GetProperty("time").GetDouble())
                Expect.floatClose Accuracy.high value (sample.GetProperty("value").GetDouble()) $"source property sample {sample.GetRawText()}"
            timeline.Reset()
            Expect.floatClose Accuracy.high value (fixture.GetProperty("resetValue").GetDouble()) "source property reset"
    for fixture in fixtures "timelineCallbacks" do
        testCase (fixture.GetProperty("name").GetString()) <| fun _ ->
            let clock = new ManualFrameScheduler()
            let baseline, _ = animation clock 100. 0. 1.
            use baseline = baseline
            use timeline = new Timeline(clock)
            timeline.Sync baseline |> ignore
            let events = ResizeArray<string>()
            for call in fixture.GetProperty("calls").EnumerateArray() do
                let id = call[0].GetString()
                timeline.Call((fun () -> events.Add id), TimelinePosition.At(call[1].GetDouble())) |> ignore
            for sample in fixture.GetProperty("samples").EnumerateArray() do
                events.Clear()
                match sample.TryGetProperty "reset" with
                | true, _ -> timeline.Reset()
                | _ ->
                    let muted = match sample.TryGetProperty "muted" with true, value -> value.GetBoolean() | _ -> false
                    timeline.Seek(sample.GetProperty("time").GetDouble(), muteCallbacks = muted)
                let expected = sample.GetProperty("events").EnumerateArray() |> Seq.map _.GetString() |> Seq.toArray
                Expect.sequenceEqual events expected $"source callbacks at {sample.GetRawText()}"
    testCase "numeric property handles compose independently and preserve unrelated state" <| fun _ ->
        let clock = new ManualFrameScheduler()
        use timeline = new Timeline(clock)
        let mutable a, b, unrelated = 0., 100., 7.
        let first = NumericProperty((fun () -> a), (fun number -> a <- number))
        let second = NumericProperty((fun () -> b), (fun number -> b <- number))
        timeline.Animate(first, 100., 100., TimelinePosition.At 0.).Animate(second, 200., 100., TimelinePosition.At 0.) |> ignore
        Expect.equal (a, b) (0., 100.) "internal preparation does not dispatch writes"
        timeline.Seek 50.
        Expect.equal (a, b, unrelated) (50., 150., 7.) "stable handles identify separate properties"
    testCase "property easing cancellation prevents final dispatch" <| fun _ ->
        let clock = new ManualFrameScheduler()
        use timeline = new Timeline(clock)
        let mutable value = 0.
        let mutable armed = false
        let property = NumericProperty((fun () -> value), (fun number -> value <- number))
        timeline.Animate(property, 100., 100., ease = (fun progress ->
            if armed then timeline.Cancel()
            progress)) |> ignore
        armed <- true
        timeline.Seek 50.
        Expect.equal value 0. "no property write after cancellation inside easing"
    testCase "property modifier seek supersedes the interrupted dispatch" <| fun _ ->
        let clock = new ManualFrameScheduler()
        use timeline = new Timeline(clock)
        let mutable value = 0.
        let mutable armed = false
        let property = NumericProperty((fun () -> value), (fun number -> value <- number))
        timeline.Animate(property, 100., 100., modifier = (fun number ->
            if armed then
                armed <- false
                timeline.Seek 10.
            number)) |> ignore
        armed <- true
        timeline.Seek 50.
        Expect.equal (value, timeline.CurrentTime) (10., 10.) "nested sample wins"
    testCase "property reads cannot publish an insertion after a newer seek" <| fun _ ->
        let clock = new ManualFrameScheduler()
        use timeline = new Timeline(clock)
        let mutable a, b = 0., 0.
        let first = NumericProperty((fun () -> a), (fun number -> a <- number))
        timeline.Animate(first, 100., 100., TimelinePosition.At 0.) |> ignore
        let second = NumericProperty((fun () -> timeline.Seek 75.; b), (fun number -> b <- number))
        timeline.Animate(second, 100., 100., TimelinePosition.At 0.) |> ignore
        Expect.equal (a, b, timeline.CurrentTime) (75., 0., 75.) "superseded insertion did not publish"
        timeline.Seek 100.
        Expect.equal b 0. "no phantom track"
    testCase "ancestor guards protect owned property tracks" <| fun _ ->
        let clock = new ManualFrameScheduler()
        use parent = new Timeline(clock)
        use child = new Timeline(clock)
        let mutable value = 0.
        let mutable armed = false
        let property = NumericProperty((fun () -> value), (fun number -> value <- number))
        child.Animate(property, 100., 100., ease = (fun progress ->
            if armed then parent.Cancel()
            progress)) |> ignore
        parent.Sync child |> ignore
        armed <- true
        parent.Seek 50.
        Expect.equal value 0. "ancestor cancellation spans property easing"
        armed <- false
        child.Seek 25.
        Expect.equal value 25. "borrowed child remains usable"
    testCase "invalid property duration fails before calling accessors" <| fun _ ->
        let clock = new ManualFrameScheduler()
        use timeline = new Timeline(clock)
        let mutable reads = 0
        let property = NumericProperty((fun () -> reads <- reads + 1; 0.), ignore)
        Expect.throwsT<ArgumentException> (fun () -> timeline.Animate(property, 100., -1.) |> ignore) "negative duration"
        Expect.equal reads 0 "input validation precedes capture"
    testCase "property authoring accepts unsigned whole milliseconds without losing the precise path" <| fun _ ->
        let clock = new ManualFrameScheduler()
        use timeline = new Timeline(clock)
        let mutable value = 0.
        let property = NumericProperty((fun () -> value), (fun number -> value <- number))
        timeline.Animate(property, 100., UInt32.MaxValue) |> ignore
        Expect.equal timeline.Duration (float UInt32.MaxValue) "exact whole-millisecond conversion"
        timeline.Seek(float UInt32.MaxValue / 2.)
        Expect.equal value 50. "half of the unsigned duration"
    testCase "positive durations rounding to zero are rejected before property capture" <| fun _ ->
        let clock = new ManualFrameScheduler()
        use timeline = new Timeline(clock)
        let mutable reads = 0
        let property = NumericProperty((fun () -> reads <- reads + 1; 0.), ignore)
        Expect.throwsT<ArgumentException> (fun () -> timeline.Animate(property, 100., 1e-13) |> ignore) "unrepresentable source timing"
        Expect.equal reads 0 "no capture for an invalid timing value"
    for fixture in fixtures "nestedTimelines" do
        testCase (fixture.GetProperty("name").GetString()) <| fun _ ->
            let clock = new ManualFrameScheduler()
            let numeric, value = animation clock 100. 0. 100.
            use numeric = numeric
            use child = new Timeline(clock)
            use parent = new Timeline(clock)
            let events = ResizeArray<string>()
            child.Sync(numeric, TimelinePosition.At 0.).Call((fun () -> events.Add "child"), TimelinePosition.At 50.) |> ignore
            parent.Sync(child, TimelinePosition.At 25.) |> ignore
            for sample in fixture.GetProperty("samples").EnumerateArray() do
                events.Clear()
                parent.Seek(sample.GetProperty("time").GetDouble(), muteCallbacks = fixture.GetProperty("muted").GetBoolean())
                Expect.floatClose Accuracy.high value.Value (sample.GetProperty("value").GetDouble()) "nested source value"
                Expect.sequenceEqual events (sample.GetProperty("events").EnumerateArray() |> Seq.map _.GetString()) "nested source callbacks"
            events.Clear()
            parent.Reset()
            Expect.sequenceEqual events (fixture.GetProperty("resetEvents").EnumerateArray() |> Seq.map _.GetString()) "nested source reset callbacks"
    testCase "timeline synchronization rejects direct and indirect cycles" <| fun _ ->
        let clock = new ManualFrameScheduler()
        use first = new Timeline(clock)
        use second = new Timeline(clock)
        Expect.throwsT<ArgumentException> (fun () -> first.Sync first |> ignore) "direct cycle"
        first.Sync second |> ignore
        Expect.throwsT<ArgumentException> (fun () -> second.Sync first |> ignore) "indirect cycle"
    testCase "reset cannot clear callback state from a newer reentrant seek" <| fun _ ->
        let clock = new ManualFrameScheduler()
        use parent = new Timeline(clock)
        use child = new Timeline(clock)
        let baseline, _ = animation clock 100. 0. 1.
        use baseline = baseline
        let mutable armed = false
        let mutable calls = 0
        child.Sync baseline |> ignore
        child.Call((fun () ->
            if armed then
                armed <- false
                parent.Seek 75.), TimelinePosition.At 50.) |> ignore
        parent.Sync child |> ignore
        parent.Call((fun () -> calls <- calls + 1), TimelinePosition.At 60.) |> ignore
        parent.Seek 75.
        armed <- true
        parent.Reset()
        Expect.equal parent.CurrentTime 75. "new seek retained"
        parent.Seek 75.
        Expect.equal calls 1 "no callback refire without crossing"
    testCase "zero-position callback can run as the only track" <| fun _ ->
        let clock = new ManualFrameScheduler()
        use timeline = new Timeline(clock)
        let mutable calls = 0
        timeline.Call(fun () -> calls <- calls + 1) |> ignore
        timeline.Play()
        clock.Advance 1.; clock.RunFrame()
        Expect.equal calls 1 "single callback track runs"
    testCase "callback can seek forward without recursively refiring itself" <| fun _ ->
        let clock = new ManualFrameScheduler()
        let baseline, _ = animation clock 100. 0. 1.
        use baseline = baseline
        use timeline = new Timeline(clock)
        let mutable calls = 0
        timeline.Sync baseline |> ignore
        timeline.Call((fun () -> calls <- calls + 1; timeline.Seek 75.), TimelinePosition.At 50.) |> ignore
        timeline.Seek 50.
        Expect.equal calls 1 "callback transition is committed before user code"
        Expect.equal timeline.CurrentTime 75. "nested seek wins"
    testCase "ancestor cancellation during nested easing prevents descendant writes" <| fun _ ->
        let clock = new ManualFrameScheduler()
        use root = new Timeline(clock)
        use middle = new Timeline(clock)
        use leaf = new Timeline(clock)
        let mutable armed = false
        let mutable value = 0.
        use numeric = new NumericAnimation(clock, (fun () -> value), (fun number -> value <- number), 100., 100.,
            ease = (fun progress ->
                if armed then root.Cancel()
                progress))
        leaf.Sync numeric |> ignore
        middle.Sync leaf |> ignore
        root.Sync middle |> ignore
        armed <- true
        root.Seek 50.
        Expect.equal value 0. "root generation guards all descendants"
        armed <- false
        leaf.Seek 25.
        Expect.equal value 25. "borrowed descendants remain live"
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
        clock.Advance 13.; clock.RunFrame()
        Expect.equal value.Value 25. "scheduled child sample"
        timeline.Pause()
        clock.Advance 13.; clock.RunFrame()
        Expect.equal value.Value 25. "paused"
        timeline.Play()
        clock.Advance 13.; clock.RunFrame()
        Expect.equal value.Value 50. "resumed"
        timeline.Complete()
        Expect.equal value.Value 100. "complete"
        Expect.isTrue timeline.Completed "completion state"
        timeline.Reset()
        Expect.equal value.Value 0. "rewound"
        Expect.isFalse timeline.Playing "reset remains paused"
        timeline.Complete()
        timeline.Restart()
        clock.Advance 13.; clock.RunFrame()
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
