namespace Ranvier.Anime.Tests

open System
open Ranvier.Anime
open Expecto
module SvgLifetimeGuardTests =
    let svgLifetimeGuardTests=testList "SVG actual owner and generation guards" [
        testCase "standalone graph-free operations and explicit restoration stay available" <| fun _ ->
            let lifetime=SvgOperationLifetime()
            lifetime.EnsureConstruction()
            lifetime.Commit()
            Expect.isTrue (lifetime.BeginOperation()()) "standalone operation"
            let restoring=lifetime.BeginRestore().Value
            Expect.isTrue (restoring()) "explicit restoration"
            lifetime.EndRestore()
            Expect.isFalse lifetime.Active "closed"
        testCase "dynamic AddOnce owner survives refresh while ambient epoch owner ends" <| fun _ ->
            use graph=new Ranvier.Graph()
            use active=graph.Activate()
            use scope=new Scope()
            let captured=ResizeArray<SvgOperationLifetime * Ranvier.Owner>()
            scope.Add(fun current->
                current.AddOnce(fun _->None) |> ignore
                let ambient=Ranvier.Api.getOwner()
                let lifetime=SvgOperationLifetime()
                lifetime.Attach {new IDisposable with member _.Dispose()=lifetime.Close()}
                lifetime.Commit()
                captured.Add(lifetime,ambient)
                None) |> ignore
            let first,ambient=captured[0]
            scope.Refresh() |> ignore
            Expect.isTrue ambient.IsDisposed "baseline Api.getOwner selected regular epoch"
            Expect.isTrue first.Active "actual dynamic persistent owner retained"
            Expect.isTrue (first.BeginOperation()()) "new operation remains admitted after refresh"
            scope.Revert()
            Expect.isFalse first.Active "final actual owner disposal"
        testCase "nested operation supersedes remaining outer effects" <| fun _ ->
            let lifetime=SvgOperationLifetime()
            lifetime.Commit()
            let outer=lifetime.BeginOperation()
            let inner=lifetime.BeginOperation()
            Expect.isFalse (outer()) "old setter"
            Expect.isTrue (inner()) "nested setter"
        testCase "scope refresh during a read invalidates construction" <| fun _ ->
            use graph=new Ranvier.Graph()
            use active=graph.Activate()
            use scope=new Scope()
            scope.Execute(fun _->
                let lifetime=SvgOperationLifetime()
                Expect.throwsT<ObjectDisposedException> (fun()->lifetime.ReadConstruction(fun()->scope.Refresh() |> ignore) |> ignore) "generation changed") |> ignore
        testCase "owner disposal cleanup can restore while later operations stop" <| fun _ ->
            use graph=new Ranvier.Graph()
            use active=graph.Activate()
            let mutable restored=false
            let owner,lifetime=Ranvier.Api.createRoot(fun owner->
                let lifetime=SvgOperationLifetime()
                lifetime.Commit()
                lifetime.Attach {new IDisposable with
                    member _.Dispose()=
                        let guard=lifetime.BeginRestore().Value
                        restored<-guard()
                        lifetime.EndRestore()}
                owner,lifetime)
            owner.Dispose()
            Expect.isTrue restored "owner is already disposed during callback"
            Expect.isFalse lifetime.Active "later writes end"
        testCase "live owner disposal during restoration ends its remaining writes" <| fun _ ->
            use graph=new Ranvier.Graph()
            use active=graph.Activate()
            let owner,lifetime=Ranvier.Api.createRoot(fun owner->owner,SvgOperationLifetime())
            lifetime.Commit()
            let guard=lifetime.BeginRestore().Value
            Expect.isTrue (guard()) "initial admission"
            owner.Dispose()
            Expect.isFalse (guard()) "reentry invalidates owner"
            lifetime.EndRestore()
    ]
