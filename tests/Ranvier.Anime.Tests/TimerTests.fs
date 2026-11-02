module Ranvier.Anime.Tests.TimerTests

open System
open System.IO
open System.Globalization
open System.Text.Json
open Expecto
open Ranvier
open Ranvier.Dom
open Ranvier.Anime

let private number (value: JsonElement) =
    if value.ValueKind = JsonValueKind.String then Double.Parse(value.GetString(), CultureInfo.InvariantCulture)
    else value.GetDouble()

let private optionalNumber (name: string) fallback (parameters: JsonElement) =
    match parameters.TryGetProperty name with true, value -> number value | _ -> fallback

let private optionalBool (name: string) (parameters: JsonElement) =
    match parameters.TryGetProperty name with true, value -> value.GetBoolean() | _ -> false

let private verifyState (timer: Timer) (expected: JsonElement) =
    for key, actual in ["time", timer.CurrentTime; "raw", timer.Sample.AbsoluteTime; "iterationTime", timer.IterationCurrentTime
                        "rawIterationTime", timer.Sample.IterationTime; "progress", timer.Progress; "iterationProgress", timer.IterationProgress
                        "deltaTime", timer.DeltaTime; "speed", timer.Speed; "fps", timer.Fps] do
        let expected = number(expected.GetProperty key)
        if Double.IsNaN expected then Expect.isTrue (Double.IsNaN actual) key
        else Expect.equal actual expected key
    Expect.equal timer.CurrentIteration (expected.GetProperty("iteration").GetInt32()) "iteration"
    for key, actual in ["backwards", timer.Backwards; "reversed", timer.Reversed; "began", timer.Began
                        "completed", timer.Completed; "paused", timer.Paused] do
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
    let mutable expectedEvents : JsonElement array = [||]
    let mutable eventIndex = 0
    let mutable checking = true
    let callback name timer =
        if checking then
            Expect.isLessThan eventIndex expectedEvents.Length "unexpected callback"
            let expected = expectedEvents[eventIndex]
            Expect.equal name (expected.GetProperty("event").GetString()) "callback phase"
            verifyState timer (expected.GetProperty "state")
            eventIndex <- eventIndex + 1
    use clock = new ManualFrameScheduler()
    use timer = new Timer(clock, optionalNumber "duration" infinity parameters, options=options,
                          onBegin=callback "Begin", onBeforeUpdate=callback "BeforeUpdate", onUpdate=callback "Update",
                          onLoop=callback "Loop", onPause=callback "Pause", onComplete=callback "Complete")
    use cleanup = { new IDisposable with member _.Dispose() = checking <- false }
    verifyState timer (fixture.GetProperty "initial")
    for sample in fixture.GetProperty("samples").EnumerateArray() do
        expectedEvents <- sample.GetProperty("events").EnumerateArray() |> Seq.toArray
        eventIndex <- 0
        let step = sample.GetProperty "step"
        match step.GetProperty("op").GetString() with
        | "frame" -> clock.Advance(number(step.GetProperty "after")); clock.RunFrame()
        | "advance" -> clock.Advance(number(step.GetProperty "after"))
        | "seek" -> timer.Seek(number(step.GetProperty "time"), muteCallbacks=optionalBool "mute" step)
        | "speed" -> timer.Speed <- number(step.GetProperty "value")
        | "fps" -> timer.Fps <- number(step.GetProperty "value")
        | "currentTime" -> timer.CurrentTime <- number(step.GetProperty "value")
        | "progress" -> timer.Progress <- number(step.GetProperty "value")
        | "iterationProgress" -> timer.IterationProgress <- number(step.GetProperty "value")
        | "iterationCurrentTime" -> timer.IterationCurrentTime <- number(step.GetProperty "value")
        | "currentIteration" -> timer.SeekIteration(step.GetProperty("value").GetUInt32())
        | "reversed" -> timer.Reversed <- step.GetProperty("value").GetBoolean()
        | "reset" -> timer.Reset()
        | "restart" -> timer.Restart()
        | "resume" -> timer.Resume()
        | "pause" -> timer.Pause()
        | "play" -> timer.Play()
        | "reverse" -> timer.Reverse()
        | "alternate" -> timer.Alternate()
        | "complete" -> timer.Complete(muteCallbacks=optionalBool "mute" step)
        | op -> failtestf "Unsupported timer operation %s" op
        Expect.equal eventIndex expectedEvents.Length "callback count"
        verifyState timer (sample.GetProperty "state")

let private referenceTests =
    let root =
        match Environment.GetEnvironmentVariable "RANVIER_ANIME_REFERENCE_ROOT" with
        | null | "" -> Path.Combine(AppContext.BaseDirectory, "reference")
        | value -> value
    use document = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "timer-controls.json")))
    document.RootElement.GetProperty("scheduled").EnumerateArray()
    |> Seq.map(fun fixture ->
        let fixture = fixture.Clone()
        testCase (fixture.GetProperty("name").GetString()) (fun _ -> verify fixture))
    |> Seq.toList
    |> testList "scheduled timer"

type private LateFrameScheduler() =
    let mutable now = 0.
    let frames = ResizeArray<(float -> unit) * bool ref>()
    member _.LiveCount = frames |> Seq.filter(fun (_, live) -> live.Value) |> Seq.length
    member _.Deliver(time: float) =
        now <- time
        let current = frames.ToArray()
        frames.Clear()
        for callback, _ in current do callback now
    interface FrameScheduler with
        member _.Now() = now
        member _.Frame(callback) =
            let live = ref true
            frames.Add(callback, live)
            { new IDisposable with member _.Dispose() = live.Value <- false }
        member _.Timeout(_, _) = failwith "Timer unexpectedly requested a timeout."
        member _.Microtask(_) = failwith "Timer unexpectedly requested a microtask."
    interface IDisposable with
        member _.Dispose() =
            for _, live in frames do live.Value <- false
            frames.Clear()

let private lifetimeTests = testList "timer lifetime and inputs" [
    testCase "unsigned duration retains half-millisecond precision at uint maximum" <| fun _ ->
        use clock = new ManualFrameScheduler()
        use timer = new Timer(clock, UInt32.MaxValue)
        timer.Seek 2147483647.5
        Expect.equal timer.Duration 4294967295. "unsigned conversion is exact"
        Expect.equal timer.CurrentTime 2147483647.5 "fraction retained"
        use fractional = new Timer(clock, 0.125)
        fractional.Seek 0.0625
        Expect.equal fractional.Progress 0.5 "precise duration path"

    testCase "zero duration completes synchronously without scheduling a frame" <| fun _ ->
        use clock = new LateFrameScheduler()
        let events = ResizeArray<string>()
        use timer = new Timer(clock, 0u, onBegin=(fun _ -> events.Add "begin"),
                              onUpdate=(fun _ -> events.Add "update"), onComplete=(fun _ -> events.Add "complete"))
        timer.Resume()
        Expect.sequenceEqual events ["begin"; "update"; "complete"] "synchronous zero timer"
        Expect.isTrue timer.Completed "completed"
        Expect.isTrue timer.Paused "settled"
        Expect.equal clock.LiveCount 0 "no pending frame"

    testCase "cancelled subscriptions delivered late cannot invoke callbacks" <| fun _ ->
        use clock = new LateFrameScheduler()
        let mutable updates = 0
        use timer = new Timer(clock, 100u, onUpdate=(fun _ -> updates <- updates + 1))
        timer.Resume()
        Expect.equal clock.LiveCount 1 "one frame"
        timer.Cancel()
        Expect.equal clock.LiveCount 0 "subscription disposed"
        clock.Deliver 50.
        Expect.equal updates 0 "stale callback rejected"
        Expect.throwsT<ObjectDisposedException> timer.Resume "terminal cancellation"

    testCase "a same-frame seek suppresses outer update and queues one replacement frame" <| fun _ ->
        use clock = new LateFrameScheduler()
        let values = ResizeArray<float>()
        let mutable once = true
        use timer = new Timer(clock, 100u, onBeforeUpdate=(fun timer ->
            if once then
                once <- false
                timer.Seek(75., muteCallbacks=true)), onUpdate=(fun timer -> values.Add timer.CurrentTime))
        timer.Resume()
        clock.Deliver 25.
        Expect.equal timer.CurrentTime 75. "inner seek survives"
        Expect.isEmpty values "outer update stopped"
        Expect.equal clock.LiveCount 1 "one replacement frame"
        clock.Deliver 30.
        Expect.sequenceEqual values [92.] "new seek anchor"
        Expect.equal clock.LiveCount 1 "single continuation"

    testCase "resuming from pause callback supersedes outer pause and retains one frame" <| fun _ ->
        use clock = new LateFrameScheduler()
        let values = ResizeArray<float>()
        let mutable once = true
        use timer = new Timer(clock, 100u, onUpdate=(fun timer -> values.Add timer.CurrentTime),
                              onPause=(fun timer -> if once then once <- false; timer.Resume()))
        timer.Resume()
        clock.Deliver 25.
        timer.Pause()
        Expect.isFalse timer.Paused "callback resumed"
        Expect.equal clock.LiveCount 1 "one live callback"
        clock.Deliver 30.
        Expect.sequenceEqual values [37.; 54.] "stale and live callbacks cannot double sample"

    testCase "owner disposal during before-update prevents update and later frames" <| fun _ ->
        use graph = new Graph()
        use activation = graph.Activate()
        use clock = new ManualFrameScheduler()
        let mutable updates = 0
        let owner, timer = Api.createRoot(fun owner ->
            let timer = new Timer(clock, 100u, onBeforeUpdate=(fun _ -> owner.Dispose()),
                                   onUpdate=(fun _ -> updates <- updates + 1))
            Timer.own timer |> ignore
            owner, timer)
        timer.Resume()
        clock.Advance 25.
        clock.RunFrame()
        Expect.isTrue timer.Cancelled "real owner cancelled timer"
        clock.Advance 25.
        clock.RunFrame()
        Expect.equal updates 0 "disposed owner suppresses same and later frame"
        owner.Dispose()

    testCase "completion callback restart keeps its replacement frame" <| fun _ ->
        use clock = new LateFrameScheduler()
        let mutable completions = 0
        use timer = new Timer(clock, 100u, onComplete=(fun timer ->
            completions <- completions + 1
            if completions = 1 then timer.Restart()))
        timer.Resume()
        clock.Deliver 100.
        Expect.equal completions 1 "one completion"
        Expect.isFalse timer.Completed "restart reset"
        Expect.isFalse timer.Paused "restart resumed"
        Expect.equal clock.LiveCount 1 "replacement retained"
        clock.Deliver 125.
        Expect.equal timer.CurrentTime 37. "restart anchor uses callback time"
        Expect.equal clock.LiveCount 1 "single continuation"

    testCase "frame callback exception cancels scheduling and preserves the original error" <| fun _ ->
        use clock = new LateFrameScheduler()
        use timer = new Timer(clock, 100u, onUpdate=(fun _ -> raise(InvalidOperationException "primary")),
                              onPause=(fun _ -> raise(FormatException "secondary")))
        timer.Resume()
        let message =
            try clock.Deliver 25.; failtest "Expected callback failure"
            with :? InvalidOperationException as error -> error.Message
        Expect.equal message "primary" "cleanup cannot replace original error"
        Expect.isTrue timer.Cancelled "failed timer terminal"
        Expect.equal clock.LiveCount 0 "no queued frame"

    testCase "invalid clock input preserves an active frame subscription" <| fun _ ->
        use clock = new LateFrameScheduler()
        let mutable updates = 0
        use timer = new Timer(clock, 100u, onUpdate=(fun _ -> updates <- updates + 1))
        timer.Resume()
        for value in [nan; infinity; -infinity] do
            Expect.throwsT<ArgumentException> (fun () -> timer.Seek value) "seek validation"
            Expect.throwsT<ArgumentException> (fun () -> timer.Speed <- value) "speed validation"
            Expect.throwsT<ArgumentException> (fun () -> timer.Fps <- value) "fps validation"
        Expect.isFalse timer.Cancelled "validation preserves timer"
        Expect.equal clock.LiveCount 1 "original frame retained"
        clock.Deliver 25.
        Expect.equal updates 1 "original continuation works"

    testCase "invalid construction does not schedule frames" <| fun _ ->
        use clock = new LateFrameScheduler()
        for duration in [-1.; nan; -infinity] do
            Expect.throwsT<ArgumentException> (fun () -> new Timer(clock, duration) |> ignore) "duration"
        for options in [{ PlaybackOptions.Default with Delay=nan }; { PlaybackOptions.Default with LoopDelay=infinity }
                        { PlaybackOptions.Default with Speed=nan }; { PlaybackOptions.Default with FrameRate=infinity }] do
            Expect.throwsT<ArgumentException> (fun () -> new Timer(clock, 100u, options=options) |> ignore) "options"
        Expect.equal clock.LiveCount 0 "invalid timer never queued"

    testCase "finite and forever repeat options retain their distinct iteration counts" <| fun _ ->
        use clock = new LateFrameScheduler()
        use finiteTimer = new Timer(clock, 100u, options={ PlaybackOptions.Default with Repeat=PlaybackRepeat.Count UInt32.MaxValue })
        Expect.equal finiteTimer.IterationCount 4294967296. "uint count adds the initial iteration in float"
        use forever = new Timer(clock, 100u, options={ PlaybackOptions.Default with Repeat=PlaybackRepeat.Forever })
        forever.Resume()
        clock.Deliver 100.
        Expect.isFalse forever.Completed "indefinite repeat"
        Expect.equal clock.LiveCount 1 "repeat continues"
]

let tests = testList "standalone timer" [referenceTests; lifetimeTests]
