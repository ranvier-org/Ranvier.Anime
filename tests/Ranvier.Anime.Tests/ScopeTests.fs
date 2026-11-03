module Ranvier.Anime.Tests.ScopeTests

open Ranvier.Anime
open System
open Ranvier
open Ranvier.Dom
open Expecto

type private ScopeMediaTarget() =
    let listeners = ResizeArray<Event -> unit>()
    let objects = ResizeArray<EventListenerObject>()
    member _.Count = listeners.Count + objects.Count
    member _.Change() =
        for callback in listeners.ToArray() do callback Unchecked.defaultof<Event>
        for callback in objects.ToArray() do callback.handleEvent Unchecked.defaultof<Event>
    interface EventTarget with
        member _.addEventListener(_:string, callback: Event -> unit, _: bool option) = listeners.Add callback
        member _.addEventListener(_:string, callback: EventListenerObject, _: bool option) : unit = objects.Add callback
        member _.addEventListener(_:string, callback: Event -> unit, _: AddEventListenerOptions) = listeners.Add callback
        member _.addEventListener(_:string, callback: EventListenerObject, _: AddEventListenerOptions) : unit = objects.Add callback
        member _.removeEventListener(_:string, callback: Event -> unit, _: bool option) = listeners.Remove callback |> ignore
        member _.removeEventListener(_:string, callback: EventListenerObject, _: bool option) = objects.Remove callback |> ignore
        member _.removeEventListener(_:string, callback: Event -> unit, _: EventListenerOptions) = listeners.Remove callback |> ignore
        member _.removeEventListener(_:string, callback: EventListenerObject, _: EventListenerOptions) = objects.Remove callback |> ignore
        member _.dispatchEvent(_) = true

let scopeLifecycleTests =
    testList "Scope" [
        testCase "refresh reverts regular resources before cleanup and retains once slots" <| fun _ ->
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
            Expect.sequenceEqual trace ["construct";"once";"regular2";"regular1";"cleanup";"construct"] "source refresh order"
            scope.Revert()
            Expect.sequenceEqual (trace |> Seq.skip 6) ["cleanup";"regular2";"regular1";"onceResource";"onceCleanup"] "source retains once selection after AddOnce"
        testCase "methods retain typed arguments and register into scope" <| fun _ ->
            use graph = new Graph()
            use active = graph.Activate()
            use scope = new Scope()
            let mutable reverted = false
            scope.AddMethod("times",fun (n:int) ->
                Scope.RegisterCurrent(fun () -> reverted <- true)
                n*2) |> ignore
            Expect.equal (scope.Invoke<int,int>("times",21)) 42 "typed result"
            scope.Refresh() |> ignore
            Expect.isTrue reverted "method resource reverted"
            Expect.throws (fun () -> scope.Invoke<string,int>("times","bad") |> ignore) "wrong signature"
        testCase "ambient scope and owner restore on exceptions" <| fun _ ->
            use graph = new Graph()
            use active = graph.Activate()
            let before = Api.getOwner()
            use scope = new Scope()
            Expect.throws(fun () -> scope.Execute(fun _ -> failwith "test") |> ignore) "callback fault"
            Expect.isNone Scope.Current "ambient restored"
            Expect.isTrue (Object.ReferenceEquals(before,Api.getOwner())) "owner restored"
        testCase "refresh disposes old reactive owner and creates live replacement" <| fun _ ->
            use graph = new Graph()
            use active = graph.Activate()
            use scope = new Scope()
            let owners = ResizeArray<Owner>()
            scope.Add(fun _ -> owners.Add(Api.getOwner());None) |> ignore
            scope.Refresh() |> ignore
            Expect.isTrue owners[0].IsDisposed "old owner ended"
            Expect.isFalse owners[1].IsDisposed "new owner live"
            scope.Revert()
            Expect.isTrue owners[1].IsDisposed "new owner ended"
        testCase "parent owner disposal reverts scope and prevents later callbacks" <| fun _ ->
            use graph = new Graph()
            use active = graph.Activate()
            let parent,scope = Api.createRoot(fun owner -> owner,new Scope())
            let mutable reverted = 0
            scope.Add(fun s -> s.Register(fun () -> reverted <- reverted+1); None) |> ignore
            parent.Dispose()
            Expect.isTrue scope.IsDisposed "owner ended scope"
            Expect.equal reverted 1 "restored"
            Expect.throws(fun () -> scope.Execute ignore) "cannot execute after owner"
            scope.Revert()
            Expect.equal reverted 1 "idempotent"
        testCase "parent disposal restores with no caller ambient graph" <| fun _ ->
            use graph = new Graph()
            let parent,scope =
                use active = graph.Activate()
                Api.createRoot(fun owner -> owner,new Scope())
            let mutable restored = false
            do
                use active = graph.Activate()
                scope.Register(fun () -> restored <- true)
            parent.Dispose()
            Expect.isTrue restored "root teardown activated its captured graph"
            Expect.isTrue scope.IsDisposed "terminal"
            Expect.equal parent.ErrorCount 0 "no hidden cleanup error"
            scope.Revert()
        testCase "parent teardown restores before cancelling attached descendants after refresh" <| fun _ ->
            use graph = new Graph()
            use active = graph.Activate()
            let parent,scope = Api.createRoot(fun owner -> owner,new Scope())
            let trace = ResizeArray<string>()
            scope.Add(fun s ->
                Api.getOwner().Attach { new IDisposable with member _.Dispose() = trace.Add "cancel" }
                s.Register(fun () -> trace.Add "restore")
                None) |> ignore
            scope.Refresh() |> ignore
            trace.Clear()
            parent.Dispose()
            Expect.sequenceEqual trace ["restore";"cancel"] "restore precedes owner cancellation"
        testCase "media snapshots refresh constructors and listeners detach" <| fun _ ->
            use graph = new Graph()
            use active = graph.Activate()
            let target = ScopeMediaTarget()
            let mutable matched = false
            let query =
                { new ViewportMediaQuery with
                    member _.Media = "(width > 10px)"
                    member _.Matches = matched
                    member _.Target = target :> EventTarget
                    member _.Dispose() = () }
            let provider : MediaQueryProvider = { MatchMedia = (fun _ -> query); Preferences = None }
            use scope = new Scope(media=provider,mediaQueries=["wide","(width > 10px)"])
            let values = ResizeArray<bool>()
            scope.Add(fun s -> values.Add s.Matches["wide"];None) |> ignore
            matched <- true
            target.Change()
            Expect.sequenceEqual values [false;true] "resampled"
            Expect.equal target.Count 1 "single subscription"
            scope.Revert()
            Expect.equal target.Count 0 "detached"
            target.Change()
            Expect.equal values.Count 2 "no callback after revert"
        testCase "media construction failure releases previously acquired queries" <| fun _ ->
            use graph = new Graph()
            use active = graph.Activate()
            let target = ScopeMediaTarget()
            let mutable disposed = false
            let query =
                { new ViewportMediaQuery with
                    member _.Media = "all"
                    member _.Matches = true
                    member _.Target = target :> EventTarget
                    member _.Dispose() = disposed <- true }
            let provider : MediaQueryProvider =
                { MatchMedia = (fun text -> if text = "broken" then failwith "media failure" else query)
                  Preferences = None }
            Expect.throws(fun () ->
                new Scope(media=provider,mediaQueries=[("first","all");("fail","broken")]) |> ignore) "provider failure"
            Expect.isTrue disposed "earlier query disposed"
            Expect.equal target.Count 0 "earlier listener removed"
        testCase "media callback enters captured graph when the host has no ambient activation" <| fun _ ->
            use graph = new Graph()
            let target = ScopeMediaTarget()
            let query =
                { new ViewportMediaQuery with
                    member _.Media = "all"
                    member _.Matches = true
                    member _.Target = target :> EventTarget
                    member _.Dispose() = () }
            let scope =
                use active = graph.Activate()
                new Scope(media={ MatchMedia=(fun _ -> query); Preferences=None },mediaQueries=[("all","all")])
            let mutable calls = 0
            do
                use active = graph.Activate()
                scope.Add(fun _ -> calls <- calls + 1;None) |> ignore
            target.Change()
            Expect.equal calls 2 "host callback entered the graph"
            use active = graph.Activate()
            scope.Revert()
        testCase "keepTime transfers typed state using positional factory" <| fun _ ->
            use graph = new Graph()
            use active = graph.Activate()
            use scope = new Scope()
            let produced = ResizeArray<int ref>()
            let mutable reverts = 0
            scope.Add(fun s ->
                let playback =
                    s.KeepTime((fun _ -> let value=ref 0 in produced.Add value;value),
                        (fun value -> value.Value), (fun value state -> value.Value <- state),
                        (fun _ -> reverts <- reverts+1))
                s.Register(fun () -> ())
                None) |> ignore
            produced[0].Value <- 37
            scope.Refresh() |> ignore
            Expect.equal produced[1].Value 37 "state restored"
            Expect.equal reverts 1 "prior reverted"
        testCase "nested scope refresh restores child before parent cleanup" <| fun _ ->
            use graph = new Graph()
            use active = graph.Activate()
            use scope = new Scope()
            let trace = ResizeArray<string>()
            scope.Add(fun _ ->
                let child = new Scope()
                child.Add(fun s -> s.Register(fun () -> trace.Add "child");None) |> ignore
                Some(fun () -> trace.Add "parent")) |> ignore
            scope.Refresh() |> ignore
            Expect.sequenceEqual trace ["child";"parent"] "nested registration"
        testCase "scope reports errors recorded by actual child owner disposal" <| fun _ ->
            use graph = new Graph()
            use active = graph.Activate()
            use scope = new Scope()
            scope.Execute(fun _ ->
                Api.getOwner().Attach { new IDisposable with member _.Dispose() = failwith "owned cancellation fault" })
            Expect.throws(fun () -> scope.Revert()) "nested owner failure remains observable"
            Expect.equal scope.Errors.Count 1 "retained for callers after teardown"
        testCase "refresh can recover after an owned cleanup fault" <| fun _ ->
            use graph = new Graph()
            use active = graph.Activate()
            use scope = new Scope()
            let mutable first = true
            let mutable constructed = 0
            scope.Add(fun _ ->
                constructed <- constructed + 1
                if first then
                    first <- false
                    Api.getOwner().Attach { new IDisposable with member _.Dispose() = failwith "first cleanup" }
                None) |> ignore
            Expect.throws(fun () -> scope.Refresh() |> ignore) "first cleanup reported"
            scope.Refresh() |> ignore
            Expect.equal constructed 2 "fresh epoch remains usable"
            Expect.equal scope.Errors.Count 1 "first failure retained once"
        testCase "revert attempts remaining resources after a cleanup fault" <| fun _ ->
            use graph = new Graph()
            use active = graph.Activate()
            use scope = new Scope()
            let mutable later = false
            scope.Register(fun () -> later <- true)
            scope.Register(fun () -> failwith "fault")
            Expect.throws(fun () -> scope.Revert()) "aggregate fault"
            Expect.isTrue later "remaining resource attempted"
            Expect.isTrue scope.IsDisposed "terminal"
    ]
