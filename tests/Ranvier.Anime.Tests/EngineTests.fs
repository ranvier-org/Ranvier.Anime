module Ranvier.Anime.Tests.EngineTests

open System
open System.Text.Json
open Ranvier.Dom
open Ranvier.Anime
open Expecto

let private fixturePath()=System.IO.Path.Combine((match System.Environment.GetEnvironmentVariable "RANVIER_ANIME_REFERENCE_ROOT" with null | "" -> System.IO.Path.Combine(System.AppContext.BaseDirectory,"reference") | root -> root),"engine-reference.json")
let private near label expected actual =
    if abs(expected-actual)>1e-7 then failtestf "%s expected %.12g, actual %.12g" label expected actual

let private sourceCase (fixture:JsonElement) =
    use host=new ManualFrameScheduler()
    let engine=EngineFrames.forScheduler host
    engine.UseDefaultMainLoop<-false
    let mutable value=Unchecked.defaultof<JsonElement>
    if fixture.TryGetProperty("fps",&value) then engine.Clock.Fps<-value.GetDouble()
    let seconds=fixture.TryGetProperty("unit",&value) && value.GetString()="s"
    if seconds then engine.Seconds<-true
    let fps=if fixture.TryGetProperty("childFps",&value) then value.GetDouble() else 240.
    if fixture.TryGetProperty("startAfter",&value) then host.Advance(value.GetDouble())
    let events=ResizeArray<float*float>()
    let inherited=fixture.TryGetProperty("inheritFps",&value) && value.GetBoolean()
    use timer=new Timer(host,(if seconds then 10. else 10000.),?options=(if inherited then None else Some{PlaybackOptions.Default with FrameRate=fps}),onUpdate=(fun timer -> events.Add(timer.CurrentTime,timer.DeltaTime)))
    timer.Resume()
    for sample in fixture.GetProperty("samples").EnumerateArray() do
        events.Clear()
        let step=sample.GetProperty("step")
        match step.GetProperty("op").GetString() with
        | "frame" -> host.Advance(step.GetProperty("after").GetDouble());engine.Update()
        | "advance" -> host.Advance(step.GetProperty("after").GetDouble())
        | "speed" -> engine.Speed<-step.GetProperty("value").GetDouble()
        | "unit" -> engine.Seconds<-step.GetProperty("value").GetString()="s"
        | "pause" -> engine.Pause()
        | "resume" -> engine.Resume()
        | other -> failtestf "Unknown step%s" other
        let expected=sample.GetProperty("events").EnumerateArray() |> Seq.toArray
        Expect.equal events.Count expected.Length (sample.ToString()+" callback count")
        for index in 0..events.Count-1 do
            let time,delta=events[index]
            near "callback time" (expected[index].GetProperty("time").GetDouble()) time
            near "callback delta" (expected[index].GetProperty("delta").GetDouble()) delta
        near "timer time" (sample.GetProperty("time").GetDouble()) timer.CurrentTime
        near "engine phase" (sample.GetProperty("engineLastTick").GetDouble()) engine.Clock.LastTick
        near "engine delta" (sample.GetProperty("engineDelta").GetDouble()) engine.Clock.Delta
        near "engine authored speed" (sample.GetProperty("engineSpeed").GetDouble()) engine.Speed
        near "engine effective speed" (sample.GetProperty("rawSpeed").GetDouble()) engine.EffectiveSpeed
        near "forced-render gap" (sample.GetProperty("tickThreshold").GetDouble()) engine.TickThreshold

let engineClockTests =
    testList "Shared Anime engine" [
        testCase "actual source engine Clock Timer traces" <| fun _ ->
            use document=JsonDocument.Parse(System.IO.File.ReadAllText(fixturePath()))
            for fixture in document.RootElement.GetProperty("fixtures").EnumerateArray() do
                try sourceCase fixture with error -> failtestf "%s: %s" (fixture.GetProperty("name").GetString()) error.Message
        testCase "raw scheduler retains direct frame behavior" <| fun _ ->
            use host=new ManualFrameScheduler()
            let mutable ticks=0
            use frame=(host:>FrameScheduler).Frame(fun _ -> ticks<-ticks+1)
            host.Advance 1.;host.RunFrame()
            Expect.equal ticks 1 "Only engine-root requests are gated"
        testCase "existing root adapter identity prevents duplicate engine gates" <| fun _ ->
            use host=new ManualFrameScheduler()
            use root=EngineFrames.createRoot host
            Expect.isTrue (Object.ReferenceEquals(root,EngineFrames.createRoot root)) "Timer retains factory root"
            Expect.isTrue (Object.ReferenceEquals(EngineFrames.forScheduler host,EngineFrames.forScheduler root)) "Same host engine"
        testCase "one host request orders Timer and Animatable roots together" <| fun _ ->
            use host=new ManualFrameScheduler()
            let log=ResizeArray<string>()
            use first=new Timer(host,1000u,onUpdate=(fun _ -> log.Add "timer"))
            use group=new Animatable(host,onUpdate=(fun _ -> log.Add "animatable"))
            let mutable value=0.
            let property=group.Numeric(NumericProperty((fun()->value),(fun next->value<-next)))
            first.Resume();property.To(100.,duration=100u)
            host.Advance 1.;host.RunFrame()
            Expect.equal log.Count 0 "Source rejects dense early frame for all roots"
            host.Advance 4.;host.RunFrame()
            Expect.sequenceEqual log ["timer"] "Callback appended after the cached last root waits for the next admission"
            Expect.isGreaterThan value 0. "Property root sampled on the admitted frame"
            log.Clear()
            host.Advance 5.;host.RunFrame()
            Expect.sequenceEqual log ["timer";"animatable"] "Callback root joins the same host engine in insertion order"
        testCase "higher root FPS raises inherited defaults while explicit scope values remain fixed" <| fun _ ->
            use host=new ManualFrameScheduler()
            let engine=EngineFrames.forScheduler host
            engine.Clock.Fps<-480.
            use inherited=new Timer(host,1000u)
            Expect.isGreaterThanOrEqual inherited.Fps 480. "Global factory cell receives source Clock FPS increase"
            let scoped=ScopeClockDefaults({PlaybackOptions.Default with FrameRate=30.})
            use explicit=ScopePlaybackContext.run scoped (fun()->new Timer(host,1000u))
            Expect.equal explicit.Fps 30. "Scope overrides retain authored FPS"
            explicit.Fps<-960.
            use next=new Timer(host,1000u)
            Expect.equal next.Fps 960. "Source child Clock setter raises subsequent inherited defaults"
    ]
