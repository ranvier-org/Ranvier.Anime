module Ranvier.Anime.Tests.NumericScopeTests

open System
open System.Text.Json
open Expecto
open Ranvier
open Ranvier.Dom
open Ranvier.Anime

let private sourceCases () =
    let root = Environment.GetEnvironmentVariable "RANVIER_ANIME_REFERENCE_ROOT"
    let root = if String.IsNullOrEmpty root then IO.Path.Combine(AppContext.BaseDirectory,"reference") else root
    use document = JsonDocument.Parse(IO.File.ReadAllText(IO.Path.Combine(root,"scope-reference.json")))
    document.RootElement.GetProperty("cases").GetProperty("numericRevert").EnumerateArray() |> Seq.map _.Clone() |> Seq.toArray

let numericScopeTests = testList "numeric scope restoration" [
    for fixture in sourceCases() do
        let operation = fixture.GetProperty("operation").GetString()
        testCase ("source "+operation) <| fun _ ->
            use scheduler = new ManualFrameScheduler()
            let mutable value = 10.
            let events = ResizeArray<string>()
            use animation = new NumericAnimation(scheduler,(fun () -> value),(fun next -> value<-next),110.,100.,
                onBegin=(fun _ -> events.Add "begin"),onRender=(fun _ -> events.Add "render"),
                onPause=(fun _ -> events.Add "pause"),onComplete=(fun _ -> events.Add "complete"))
            animation.Seek 50.
            Expect.equal value (fixture.GetProperty("before").GetDouble()) "source midpoint"
            events.Clear()
            if operation="completeThenRevert" then animation.Complete()
            if operation="cancel" then animation.Cancel() else animation.Revert()
            Expect.equal value (fixture.GetProperty("after").GetDouble()) "source final value"
            Expect.sequenceEqual events (fixture.GetProperty("events").EnumerateArray() |> Seq.map _.GetString()) "source callbacks"
            Expect.isTrue animation.Cancelled "terminal playback"
    testCase "retargeting retains immutable construction original" <| fun _ ->
        use scheduler = new ManualFrameScheduler()
        let mutable value = 10.
        use animation = new NumericAnimation(scheduler,(fun () -> value),(fun next -> value<-next),110.,100.)
        animation.Seek 50.
        animation.Retarget 210.
        animation.Seek 50.
        Expect.equal value 135. "retargeted midpoint"
        animation.Revert()
        Expect.equal value 10. "construction original"
    testCase "Cancel closes restoration and Revert remains idempotent" <| fun _ ->
        use scheduler = new ManualFrameScheduler()
        let mutable value = 10.
        let mutable writes = 0
        use animation = new NumericAnimation(scheduler,(fun () -> value),(fun next -> writes<-writes+1;value<-next),110.,100.)
        animation.Seek 50.
        animation.Cancel()
        let before = writes
        animation.Revert()
        animation.Revert()
        Expect.equal writes before "no post-cancel restoration writes"
        Expect.equal value 60. "rendered value retained"
    testCase "reentrant Cancel from rewind setter suppresses final snapshot restoration" <| fun _ ->
        use scheduler = new ManualFrameScheduler()
        let mutable value = 10.
        let mutable armed = false
        let mutable current : NumericAnimation option = None
        let mutable writes = 0
        let write next =
            writes <- writes+1
            value <- next
            if armed then current.Value.Cancel()
        use animation = new NumericAnimation(scheduler,(fun () -> value),write,110.,100.)
        current <- Some animation
        animation.Seek 50.
        animation.Retarget 210.
        animation.Seek 50.
        writes <- 0
        armed <- true
        animation.Revert()
        Expect.equal writes 1 "rewind alone"
        Expect.equal value 60. "late original write suppressed"
    testCase "failure restoring original still cancels playback" <| fun _ ->
        use scheduler = new ManualFrameScheduler()
        let mutable value = 10.
        let mutable armed = false
        let write next =
            if armed && next=10. then failwith "snapshot setter"
            value <- next
        use animation = new NumericAnimation(scheduler,(fun () -> value),write,110.,100.)
        animation.Seek 50.
        animation.Retarget 210.
        animation.Seek 50.
        armed <- true
        Expect.throws animation.Revert "original setter fault"
        Expect.isTrue animation.Cancelled "failure cancels"
        animation.Revert()
    testCase "getter-triggered scope disposal prevents constructor restoration writes" <| fun _ ->
        use graph = new Graph()
        use active = graph.Activate()
        use scheduler = new ManualFrameScheduler()
        use scope = new Scope()
        let mutable writes = 0
        scope.Add(fun current ->
            let animation = new NumericAnimation(scheduler,(fun () -> current.Revert();10.),(fun _ -> writes<-writes+1),110.,100.)
            ScopeRegistration.register animation.Revert
            ScopeRegistration.attach animation
            None) |> ignore
        Expect.equal writes 0 "no write after owner disposal"
    testCase "callback faults cannot revive restoration during cancellation pause" <| fun _ ->
        use scheduler = new ManualFrameScheduler()
        let mutable value = 10.
        let mutable writes = 0
        let mutable armed = false
        let mutable current : NumericAnimation option = None
        let ease progress = if armed then failwith "easing fault" else progress
        use animation = new NumericAnimation(scheduler,(fun () -> value),(fun next -> writes<-writes+1;value<-next),110.,100.,
            ease=ease,onPause=(fun _ -> current.Value.Revert()))
        current <- Some animation
        animation.Seek 50.
        animation.Resume()
        let before = writes
        armed <- true
        Expect.throws(fun () -> animation.Seek 60.) "callback fault"
        Expect.equal writes before "fault pause did not revive and restore"
        Expect.isTrue animation.Cancelled "fault remains terminal"
    testCase "actual owner cancellation suppresses callbacks through a closed owner" <| fun _ ->
        use graph = new Graph()
        use active = graph.Activate()
        use scheduler = new ManualFrameScheduler()
        use scope = new Scope()
        let mutable pauses = 0
        let mutable owner : Owner option = None
        scope.Add(fun _ ->
            owner <- Some(ScopeRegistration.resourceOwner())
            let animation = new NumericAnimation(scheduler,(fun () -> 10.),ignore,110.,100.,onPause=(fun _ -> pauses<-pauses+1))
            ScopeRegistration.attach animation
            animation.Resume()
            None) |> ignore
        owner.Value.Dispose()
        Expect.equal pauses 0 "callback suppressed after resource owner ended"
    testCase "scoped Revert restores before its real owner ends" <| fun _ ->
        use graph = new Graph()
        use active = graph.Activate()
        use scheduler = new ManualFrameScheduler()
        use scope = new Scope()
        let mutable value = 10.
        scope.Add(fun _ ->
            let animation = new NumericAnimation(scheduler,(fun () -> value),(fun next -> value<-next),110.,100.)
            ScopeRegistration.register animation.Revert
            ScopeRegistration.attach animation
            animation.Seek 50.
            None) |> ignore
        scope.Revert()
        Expect.equal value 10. "restoration remains permitted during owner teardown"
]
