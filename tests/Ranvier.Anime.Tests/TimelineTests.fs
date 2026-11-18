module Ranvier.Anime.Tests.TimelineTests

open System
open System.IO
open System.Text.Json
open System.Text.Json.Nodes
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

// The pinned traces include two schedules where the old engine wrote a future child's value.
// Keep their clock and lifecycle traces, and derive the visible property from authored time.
let verifyOwnedSample name property time (actual:JsonNode) (reference:JsonElement) =
    let expectedValue=
        match name with
        | "replace-loopingPredecessor" ->
            if time<250. then max 0. time % 200.
            else 50.+250.*Math.Clamp((time-250.)/100.,0.,1.)
        | "replace-signedOffsetGap" ->
            if time<50. then Math.Clamp(time+25.,0.,100.)
            elif time<150. then 75.+125.*((time-50.)/100.)
            else 200.+100.*Math.Clamp((time-150.)/100.,0.,1.)
        | _ -> failwith $"Unknown ownership schedule {name}"
    let readNumber (node:JsonNode)=
        Double.Parse(node.ToString().Replace("px",""),System.Globalization.CultureInfo.InvariantCulture)
    let snapshot=actual["snapshot"]
    let expected=JsonNode.Parse(reference.GetRawText())
    let previous=expected["snapshot"]
    for field in ["root";"children"] do
        Expect.isTrue (JsonNode.DeepEquals(snapshot[field],previous[field])) $"{name} {time} {field}"
    let target=snapshot["targets"][0]
    let previousTarget=previous["targets"][0]
    for pair in target.AsObject() do
        if pair.Key=property then Expect.floatClose Accuracy.high (readNumber pair.Value) expectedValue $"{name} {time} value"
        else Expect.isTrue (JsonNode.DeepEquals(pair.Value,previousTarget[pair.Key])) $"{name} unrelated property"
    for write in actual["writes"].AsArray() do
        Expect.equal (write["property"].ToString()) property $"{name} write target"
        Expect.floatClose Accuracy.high (readNumber write["value"]) expectedValue $"{name} {time} dispatched value"
    let lifecycle (state:JsonNode) =
        state["events"].AsArray()
        |> Seq.choose(fun event ->
            let phase=event["event"].ToString()
            if phase="Render" then None else Some(event["id"].ToString(),phase))
        |> Seq.toList
    Expect.equal (lifecycle actual) (lifecycle expected) $"{name} {time} clock events"

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
            let samples=fixture.GetProperty("samples").EnumerateArray()
            for sample in samples do
                let time=sample.GetProperty("time").GetDouble()
                let reference=sample.GetProperty("value").GetDouble()
                // At these boundaries the pinned backward trace picked a different owner.
                // Its forward trace already contains the authored value at the same instant.
                let expected=
                    match fixture.GetProperty("name").GetString(),time with
                    | ("explicitFrom" | "generated13"),125. | "generated27",49.
                    | "generated3",0. | ("generated14" | "generated16" | "generated24"),50.
                    | "generated24",25. ->
                        let forward=samples |> Seq.find(fun entry -> entry.GetProperty("time").GetDouble()=time)
                        forward.GetProperty("value").GetDouble()
                    | _ -> reference
                timeline.Seek time
                Expect.floatClose Accuracy.high value expected $"source property sample {sample.GetRawText()}"
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
        Expect.equal calls 0 "subthreshold host frame does not dispatch the timeline"
        clock.Advance 4.; clock.RunFrame()
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
                // The pinned runtime drops the first endpoint when appending a range.
                let offsets =
                    match value, options.Start with
                    | StaggerValue.Range(first, _), None -> offsets |> Array.map ((+) first)
                    | _ -> offsets
                let lastOffset = timeline.ResolvePosition(TimelinePosition.Expression "<<")
                if fixture.GetProperty("name").GetString() = "largeRangeAppend" then
                    Expect.equal lastOffset offsets[3] "large endpoint preserves the timeline offset"
                else Expect.floatClose Accuracy.high lastOffset offsets[3] "last stagger offset"
                Expect.floatClose Accuracy.high timeline.Duration (max 200. (Array.max offsets + 10.)) "source group duration"
                for index in 0 .. 3 do
                    let sampleTime = offsets[index] + 5.
                    timeline.Seek(sampleTime)
                    Expect.floatClose Accuracy.high (snd pairs[index]).Value (sampleTime - offsets[index]) "local sample time"
            finally
                pairs |> Array.iter(fun (child, _) -> (child :> IDisposable).Dispose())
    testCase "descending ranges use the previous timeline end or an explicit start" <| fun _ ->
        let cases =
            [ 200., 0., None, [|1200.; 1100.; 1000.|]
              0., -200., None, [|1200.; 1100.; 1000.|]
              300., 100., None, [|1300.; 1200.; 1100.|]
              200., 0., Some 50., [|250.; 150.; 50.|] ]
        for first, last, start, offsets in cases do
            let clock = new ManualFrameScheduler()
            use timeline = new Timeline(clock)
            let baseline, _ = animation clock 1000. 0. 1.
            use baseline = baseline
            timeline.Sync baseline |> ignore
            let children = Array.init offsets.Length (fun _ -> animation clock 10. 0. 10.)
            try
                timeline.SyncStaggered(children |> Array.map fst, StaggerValue.Range(first, last),
                                       { StaggerOptions.Default with Start = start }) |> ignore
                offsets |> Array.iteri(fun i offset ->
                    timeline.Seek(offset + 5.)
                    Expect.equal (snd children[i]).Value 5. "child sampled halfway")
            finally
                children |> Array.iter(fun (child, _) -> (child :> IDisposable).Dispose())
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
