module Ranvier.Anime.Tests.EngineRootTests

open System
open System.Text.Json
open Ranvier.Dom
open Ranvier.Anime
open Expecto

let private sourceRoots (fixture:JsonElement) =
    use host=new ManualFrameScheduler()
    let engine=EngineFrames.forScheduler host
    let roots=ResizeArray<Timer*FrameScheduler>()
    let events=ResizeArray<string*float>()
    let mutable second:Timer option=None
    let mutable acted=false
    let mutable value=Unchecked.defaultof<JsonElement>
    let action=if fixture.TryGetProperty("action",&value) then value.GetString() else ""
    let rec make (name:string) (priority:float) : Timer =
        let adapter=EngineFrames.createRoot host
        let timer=new Timer(adapter,1000u,onUpdate=(fun timer ->
            events.Add(name,timer.CurrentTime)
            if name="A" && not acted then
                acted<-true
                match action with
                | "append" -> (make "C" 1.).Resume()
                | "prepend" -> (make "C" 0.).Resume()
                | "pause" -> second.Value.Pause()
                | "resume" -> second.Value.Pause();second.Value.Resume()
                | "cancel" -> second.Value.Cancel()
                | "enginePause" -> engine.Pause()
                | "engineResume" -> engine.Pause();engine.Resume()
                | "engineSpeed" -> engine.Speed<-2.
                | "engineFps" -> engine.Clock.Fps<-60.
                | "engineUnit" -> engine.Seconds<-true
                | "engineManual" -> engine.UseDefaultMainLoop<-false
                | _ -> ()))
        EngineFrames.configure adapter (fun () -> timer.Speed<-timer.Speed) priority
        roots.Add(timer,adapter)
        timer
    try
        let priorityA=if fixture.TryGetProperty("priorityA",&value) then value.GetDouble() else 1.
        let priorityB=if fixture.TryGetProperty("priorityB",&value) then value.GetDouble() else 1.
        let first=make "A" priorityA
        if fixture.GetProperty("other").GetBoolean() then second<-Some(make "B" priorityB)
        first.Resume();second |> Option.iter(fun timer -> timer.Resume())
        for sample in fixture.GetProperty("samples").EnumerateArray() do
            events.Clear()
            host.Advance(sample.GetProperty("after").GetDouble());host.RunFrame()
            let expected=sample.GetProperty("events").EnumerateArray() |> Seq.map(fun event -> event.GetProperty("name").GetString(),event.GetProperty("time").GetDouble()) |> Seq.toArray
            Expect.equal events.Count expected.Length (sample.ToString()+" callback count")
            for index in 0..events.Count-1 do
                let name,time=events[index]
                let expectedName,expectedTime=expected[index]
                Expect.equal name expectedName "Cached-next source traversal"
                Expect.floatClose Accuracy.high expectedTime time "Root playhead"
            Expect.equal engine.Paused (sample.GetProperty("paused").GetBoolean()) "Source pause flag"
    finally
        for timer,adapter in roots do timer.Cancel();adapter.Dispose()

let engineRootTests =
    testList "Shared engine root lifetime" [
        testCase "actual source priority mutation and automatic pause traces" <| fun _ ->
            use document=JsonDocument.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine((match System.Environment.GetEnvironmentVariable "RANVIER_ANIME_REFERENCE_ROOT" with null | "" -> System.IO.Path.Combine(System.AppContext.BaseDirectory,"reference") | root -> root),"engine-roots-reference.json")))
            for fixture in document.RootElement.GetProperty("fixtures").EnumerateArray() do
                try sourceRoots fixture with error -> failtestf "%s: %s" (fixture.GetProperty("name").GetString()) error.Message
        testCase "disposing one root suppresses its frame without disposing borrowed host" <| fun _ ->
            use host=new ManualFrameScheduler()
            let root=EngineFrames.createRoot host
            let mutable calls=0
            root.Frame(fun _ -> calls<-calls+1) |> ignore
            root.Dispose();root.Dispose()
            host.Advance 5.;host.RunFrame()
            Expect.equal calls 0 "Closed root cannot dispatch"
            (host:>FrameScheduler).Frame(fun _ -> calls<-calls+1) |> ignore
            host.Advance 1.;host.RunFrame()
            Expect.equal calls 1 "Borrowed raw host remains live"
        testCase "callback fault retains the prequeued engine frame and stops later roots this frame" <| fun _ ->
            use host=new ManualFrameScheduler()
            use first=EngineFrames.createRoot host
            use second=EngineFrames.createRoot host
            let log=ResizeArray<string>()
            first.Frame(fun _ -> log.Add "first";failwith "root fault") |> ignore
            second.Frame(fun _ -> log.Add "second") |> ignore
            host.Advance 5.
            Expect.throws (fun()->host.RunFrame()) "Engine propagates root fault"
            Expect.sequenceEqual log ["first"] "Traversal aborts at throwing source root"
            host.Advance 5.;host.RunFrame()
            Expect.sequenceEqual log ["first";"second"] "Next engine request survives"
        testCase "root disposal cancels forwarded delayed work exactly once" <| fun _ ->
            use host=new ManualFrameScheduler()
            let root=EngineFrames.createRoot host
            let mutable calls=0
            let timeout=root.Timeout(10.,fun()->calls<-calls+1)
            root.Microtask(fun()->calls<-calls+1) |> ignore
            root.Dispose();timeout.Dispose();root.Dispose()
            host.Advance 20.;host.RunFrame()
            Expect.equal calls 0 "Owned work is suppressed after disposal"
        testCase "host engines retain independent admission phases" <| fun _ ->
            use firstHost=new ManualFrameScheduler()
            use secondHost=new ManualFrameScheduler()
            let first=EngineFrames.forScheduler firstHost
            let second=EngineFrames.forScheduler secondHost
            first.Clock.Fps<-60.
            Expect.equal second.Clock.Fps 240. "FPS belongs to the existing host engine"
    ]
