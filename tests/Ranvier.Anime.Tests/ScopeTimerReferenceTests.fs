module Ranvier.Anime.Tests.ScopeTimerReferenceTests

open System
open System.Text.Json
open Expecto
open Ranvier
open Ranvier.Dom
open Ranvier.Anime

let private scopeTimerFixture () =
    let root =
        match Environment.GetEnvironmentVariable "RANVIER_ANIME_REFERENCE_ROOT" with
        | null | "" -> IO.Path.Combine(AppContext.BaseDirectory,"reference")
        | value -> value
    JsonDocument.Parse(IO.File.ReadAllText(IO.Path.Combine(root,"scope-reference.json")))

let private scopeTimerOptionalBool name (value:JsonElement) =
    match value.TryGetProperty(name:string) with true,field -> field.GetBoolean() | _ -> false

let private scopeTimerOptionalNumber name fallback (value:JsonElement) =
    match value.TryGetProperty(name:string) with true,field -> field.GetDouble() | _ -> fallback

let private verifyScopeTimerState (timer:Timer) (expected:JsonElement) =
    for key,actual in ["time",timer.CurrentTime; "progress",timer.IterationProgress; "duration",timer.Duration; "start",timer.CaptureScopeTime().Anchor] do
        Expect.equal actual (expected.GetProperty(key).GetDouble()) key
    Expect.equal timer.CurrentIteration (expected.GetProperty("iteration").GetInt32()) "iteration"
    Expect.equal timer.Reversed (expected.GetProperty("reversed").GetBoolean()) "reversed"
    Expect.equal timer.Paused (expected.GetProperty("paused").GetBoolean()) "paused"

let scopeTimerTests =
    use document = scopeTimerFixture()
    document.RootElement.GetProperty("cases").GetProperty("scopedTimers").EnumerateArray()
    |> Seq.map(fun value ->
        let fixture = value.Clone()
        let specification = fixture.GetProperty "spec"
        testCase (specification.GetProperty("name").GetString()) (fun _ ->
            use graph = new Graph()
            use active = graph.Activate()
            use scheduler = new ManualFrameScheduler()
            scheduler.Advance 73.
            use scope = new Scope()
            let created = ResizeArray<Timer>()
            let events = ResizeArray<string>()
            let mutable duration = 100.
            let mutable loop = uint(scopeTimerOptionalNumber "loop" 4. specification)
            let options =
                { PlaybackOptions.Default with
                    Repeat = PlaybackRepeat.Count loop
                    Reversed = scopeTimerOptionalBool "reversed" specification
                    Alternate = scopeTimerOptionalBool "alternate" specification
                    LoopDelay = scopeTimerOptionalNumber "loopDelay" 0. specification }
            scope.Add(fun current ->
                current.KeepTime(fun current ->
                    let timer = new Timer(scheduler,duration,options={ options with Repeat=PlaybackRepeat.Count loop },
                        onBegin=(fun _ -> events.Add "begin"),
                        onPause=(fun _ -> events.Add "pause"),
                        onComplete=(fun _ -> events.Add "complete"))
                    current.Register timer.RewindForRevert
                    created.Add timer
                    timer) |> ignore
                None) |> ignore
            created[0].Seek(specification.GetProperty("time").GetDouble())
            verifyScopeTimerState created[0] (fixture.GetProperty "before")
            events.Clear()
            duration <- scopeTimerOptionalNumber "nextDuration" 100. specification
            loop <- uint(scopeTimerOptionalNumber "nextLoop" (float loop) specification)
            scheduler.Advance 427.
            scope.Refresh() |> ignore
            Expect.equal created.Count 2 "recreated"
            Expect.isTrue created[0].Cancelled "old timer reverted"
            verifyScopeTimerState created[1] (fixture.GetProperty "after")
            let expectedEvents = fixture.GetProperty("events").EnumerateArray() |> Seq.map(fun value -> value.GetString())
            Expect.sequenceEqual events expectedEvents "source reconstruction events"
            scope.Revert()
            Expect.isTrue created[1].Cancelled "replacement ends with scope"))
    |> Seq.toList
    |> testList "scope timer keepTime pinned source"
