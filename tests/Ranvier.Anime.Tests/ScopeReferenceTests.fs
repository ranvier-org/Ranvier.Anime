module Ranvier.Anime.Tests.ScopeReferenceTests

open System
open System.Text.Json
open Expecto
open Ranvier
open Ranvier.Anime

let private fixture () =
    let root =
        match Environment.GetEnvironmentVariable "RANVIER_ANIME_REFERENCE_ROOT" with
        | null | "" -> IO.Path.Combine(AppContext.BaseDirectory,"reference")
        | value -> value
    JsonDocument.Parse(IO.File.ReadAllText(IO.Path.Combine(root,"scope-reference.json")))

let scopeReferenceTests =
    testList "scope pinned source" [
        testCase "refresh and final resource group order" <| fun _ ->
            use document = fixture()
            let expected = document.RootElement.GetProperty "cases"
            use graph = new Graph()
            use active = graph.Activate()
            use scope = new Scope()
            let trace = ResizeArray<string>()
            scope.Add(fun s ->
                trace.Add "construct"
                s.Register(fun () -> trace.Add "regular1")
                s.Register(fun () -> trace.Add "regular2")
                s.AddOnce(fun s ->
                    trace.Add "once"
                    s.Register(fun () -> trace.Add "onceResource")
                    Some(fun () -> trace.Add "onceCleanup")) |> ignore
                Some(fun () -> trace.Add "cleanup")) |> ignore
            scope.Refresh() |> ignore
            let strings name = expected.GetProperty(name:string).EnumerateArray() |> Seq.map(fun value -> value.GetString()) |> Seq.toArray
            Expect.sequenceEqual trace (strings "refresh") "source refresh"
            let offset = trace.Count
            scope.Revert()
            Expect.sequenceEqual (trace |> Seq.skip offset) (strings "final") "source revert"
        testCase "named method effects register for refresh" <| fun _ ->
            use document = fixture()
            let expected = document.RootElement.GetProperty "cases"
            use graph = new Graph()
            use active = graph.Activate()
            use scope = new Scope()
            let trace = ResizeArray<string>()
            scope.AddMethod("times",fun (n:int) ->
                Scope.RegisterCurrent(fun () -> trace.Add "methodRevert")
                n*2) |> ignore
            Expect.equal (scope.Invoke<int,int>("times",21)) (expected.GetProperty("methodResult").GetInt32()) "source result"
            scope.Refresh() |> ignore
            scope.Revert()
            let sourceTrace = expected.GetProperty("methodTrace").EnumerateArray() |> Seq.map(fun value -> value.GetString())
            Expect.sequenceEqual trace sourceTrace "source method resource"
        testCase "keepTime transfers typed iteration progress and native anchor state" <| fun _ ->
            use document = fixture()
            let expected = document.RootElement.GetProperty("cases").GetProperty "keepTime"
            use graph = new Graph()
            use active = graph.Activate()
            use scope = new Scope()
            let created = ResizeArray<(int * float * float) ref>()
            let trace = ResizeArray<string>()
            scope.Add(fun s ->
                s.KeepTime(
                    (fun _ -> let value=ref(0,0.,7.) in created.Add value;value),
                    (fun value -> value.Value),
                    (fun value state -> value.Value <- state),
                    (fun value -> trace.Add("revert"+string(created.IndexOf value)))) |> ignore
                None) |> ignore
            created[0].Value <- 2,0.37,100.
            scope.Refresh() |> ignore
            let iteration,progress,anchor = created[1].Value
            Expect.equal created.Count (expected.GetProperty("count").GetInt32()) "source creations"
            Expect.equal iteration (expected.GetProperty("iteration").GetInt32()) "source iteration"
            Expect.equal progress (expected.GetProperty("progress").GetDouble()) "source progress"
            Expect.equal anchor (expected.GetProperty("start").GetDouble()) "source anchor"
            Expect.sequenceEqual trace (expected.GetProperty("trace").EnumerateArray() |> Seq.map(fun value -> value.GetString())) "source old reversion"
    ]
