module Ranvier.Anime.Tests.ScopeResourceHookTests

open System
open Expecto
open Ranvier
open Ranvier.Anime

type private ResourceProbe() =
    let mutable cancelled = false
    let mutable reverted = false
    member _.Cancelled = cancelled
    member _.Reverted = reverted
    member _.Revert() = reverted <- true
    interface IDisposable with member _.Dispose() = cancelled <- true

let scopeResourceTests = testList "dynamic scope resource hooks" [
    testCase "resource owner changes when AddOnce changes group inside a callback" <| fun _ ->
        use graph = new Graph()
        use active = graph.Activate()
        use scope = new Scope()
        let regular = ResizeArray<ResourceProbe>()
        let persistent = ResizeArray<ResourceProbe>()
        scope.Add(fun current ->
            let first = new ResourceProbe()
            regular.Add first
            ScopeRegistration.register first.Revert
            ScopeRegistration.attach first
            current.AddOnce(fun _ -> None) |> ignore
            let last = new ResourceProbe()
            persistent.Add last
            ScopeRegistration.register last.Revert
            ScopeRegistration.attach last
            None) |> ignore
        scope.Refresh() |> ignore
        Expect.isTrue regular[0].Cancelled "regular owner ended"
        Expect.isFalse persistent[0].Cancelled "mid-callback once owner retained"
        scope.Refresh() |> ignore
        Expect.isFalse regular[1].Cancelled "source keeps once group selected at next constructor entry"
        Expect.isFalse persistent[0].Reverted "once registration retained"
        scope.Revert()
        Expect.isTrue persistent[0].Reverted "once restoration"
        Expect.isTrue persistent[0].Cancelled "actual owner cancellation"

    testCase "nested Scope created after AddOnce inherits the selected real parent owner" <| fun _ ->
        use graph = new Graph()
        use active = graph.Activate()
        use scope = new Scope()
        let nested = ResizeArray<Scope>()
        scope.Add(fun current ->
            current.AddOnce(fun _ -> None) |> ignore
            nested.Add(new Scope())
            None) |> ignore
        scope.Refresh() |> ignore
        Expect.isFalse nested[0].IsDisposed "once nested scope retained"
        nested[0].Execute ignore
        scope.Revert()
        Expect.isTrue nested[0].IsDisposed "nested scope reverted at final teardown"
    testCase "suppression preserves owner selection while omitting hidden restoration" <| fun _ ->
        use graph = new Graph()
        use active = graph.Activate()
        use scope = new Scope()
        let hidden = new ResourceProbe()
        let outer = new ResourceProbe()
        scope.Add(fun current ->
            current.AddOnce(fun _ ->
                ScopeRegistration.run ignore (fun () ->
                    ScopeRegistration.register hidden.Revert
                    ScopeRegistration.attach hidden)
                ScopeRegistration.register outer.Revert
                ScopeRegistration.attach outer
                None) |> ignore
            None) |> ignore
        scope.Refresh() |> ignore
        Expect.isFalse hidden.Cancelled "retained actual persistent owner"
        scope.Revert()
        Expect.isFalse hidden.Reverted "hidden registration suppressed"
        Expect.isTrue hidden.Cancelled "hidden lifetime cancelled"
        Expect.isTrue outer.Reverted "outer restoration registered"

    testCase "cleanup returned after reentrant Revert runs immediately" <| fun _ ->
        use graph = new Graph()
        use active = graph.Activate()
        use scope = new Scope()
        let mutable cleaned = 0
        scope.Add(fun current ->
            current.Revert()
            Some(fun () -> cleaned <- cleaned+1)) |> ignore
        Expect.equal cleaned 1 "late constructor cleanup released"

    testCase "reentrant Refresh supersedes remaining outer constructors" <| fun _ ->
        use graph = new Graph()
        use active = graph.Activate()
        use scope = new Scope()
        let mutable trigger = false
        let mutable first = 0
        let mutable second = 0
        scope.Add(fun current ->
            first <- first+1
            if trigger then
                trigger <- false
                current.Refresh() |> ignore
            None) |> ignore
        scope.Add(fun _ -> second <- second+1;None) |> ignore
        first <- 0
        second <- 0
        trigger <- true
        scope.Refresh() |> ignore
        Expect.equal first 2 "outer and inner first constructor"
        Expect.equal second 1 "only inner second constructor"

    testCase "refresh during cleanup preserves the replacement epoch owner" <| fun _ ->
        use graph = new Graph()
        use active = graph.Activate()
        use scope = new Scope()
        let owners = ResizeArray<Owner>()
        let mutable trigger = false
        scope.Add(fun current ->
            owners.Add(Api.getOwner())
            Some(fun () ->
                if trigger then
                    trigger <- false
                    current.Refresh() |> ignore)) |> ignore
        trigger <- true
        scope.Refresh() |> ignore
        Expect.equal owners.Count 2 "nested refresh superseded outer rebuild"
        Expect.isFalse owners[1].IsDisposed "nested replacement owner retained"

    testCase "factory registration after reentrant disposal restores immediately" <| fun _ ->
        use graph = new Graph()
        use active = graph.Activate()
        use scope = new Scope()
        let resource = new ResourceProbe()
        scope.Execute(fun current ->
            current.Revert()
            ScopeRegistration.register resource.Revert)
        Expect.isTrue resource.Reverted "late resource restored"

    testCase "constructor fault cleanup restores the created facade" <| fun _ ->
        use graph = new Graph()
        use active = graph.Activate()
        use scope = new Scope()
        let resource = new ResourceProbe()
        Expect.throws(fun () ->
            scope.Add(fun _ ->
                try
                    ScopeRegistration.register resource.Revert
                    ScopeRegistration.attach resource
                    failwith "factory initialization failed"
                with _ ->
                    resource.Revert()
                    reraise()) |> ignore) "factory failure"
        Expect.isTrue resource.Reverted "factory restored"
        scope.Revert()
        Expect.isTrue resource.Cancelled "owned facade released"
]
