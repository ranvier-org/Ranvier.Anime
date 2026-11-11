module Ranvier.Anime.Tests.ScopeClockReferenceTests

open System
open System.IO
open System.Text.Json
open Expecto
open Ranvier.Anime
open Ranvier.Dom

let private readCases () =
    let root=Environment.GetEnvironmentVariable "RANVIER_ANIME_REFERENCE_ROOT"
    let root=if String.IsNullOrEmpty root then Path.Combine(AppContext.BaseDirectory,"reference") else root
    use source=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"scope-clock-defaults-reference.json")))
    source.RootElement.GetProperty("cases").Clone()

let clockReferenceTests = testList "scope scalar clock source reference" [
    testCase "all seven inherited fields agree with the actual upstream Timer constructor" <| fun _ ->
        let expected=(readCases()).GetProperty "inherited"
        use graph=new Ranvier.Graph()
        use active=graph.Activate()
        use scheduler=new ManualFrameScheduler()
        use scope=new Scope(clockDefaults={PlaybackDefaults.Empty with Repeat=Some(PlaybackRepeat.Count 4u);Delay=Some 20.;LoopDelay=Some 30.;Reversed=Some true;Alternate=Some true;Speed=Some 3.;FrameRate=Some 60.})
        scope.Execute(fun _ ->
            use clock=new Timer(scheduler,100.)
            for name,value in ["iterationCount",clock.IterationCount;"delay",clock.Delay;"loopDelay",clock.LoopDelay;"speed",clock.Speed;"fps",clock.Fps] do
                Expect.equal value (expected.GetProperty(name).GetDouble()) name
            Expect.equal clock.Reversed (expected.GetProperty("reversed").GetBoolean()) "reversed"
            Expect.equal (clock.CaptureScopeTime().Alternate) (expected.GetProperty("alternate").GetBoolean()) "alternate")

    testCase "false zero and omitted fields match raw source scope defaults before existing controller normalization" <| fun _ ->
        let cases=readCases()
        let expected=cases.GetProperty "partialRaw"
        use graph=new Ranvier.Graph()
        use active=graph.Activate()
        use outer=new Scope(defaults={PlaybackOptions.Default with Repeat=PlaybackRepeat.Count 4u;Delay=20.;LoopDelay=30.;Reversed=true;Alternate=true;Speed=3.;FrameRate=60.})
        outer.Execute(fun _ ->
            use partial=new Scope(clockDefaults={PlaybackDefaults.Empty with Delay=Some 0.;Reversed=Some false;Speed=Some 0.;FrameRate=Some 0.})
            let values=partial.ClockDefaults.Options
            Expect.equal values.Repeat (PlaybackRepeat.Count(expected.GetProperty("loop").GetUInt32())) "omitted repeat inherited"
            for name,value in ["delay",values.Delay;"loopDelay",values.LoopDelay;"playbackRate",values.Speed;"frameRate",values.FrameRate] do
                Expect.equal value (expected.GetProperty(name).GetDouble()) name
            Expect.equal values.Reversed (expected.GetProperty("reversed").GetBoolean()) "explicit false"
            Expect.equal values.Alternate (expected.GetProperty("alternate").GetBoolean()) "inherited alternate")
]
