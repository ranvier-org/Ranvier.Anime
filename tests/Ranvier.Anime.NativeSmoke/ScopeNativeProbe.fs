module Ranvier.Anime.NativeSmoke.ScopeNativeProbe

open System
open Ranvier
open Ranvier.Dom
open Ranvier.Anime

/// <summary>Exercises scope selectors, actual media matching, reactive ownership and native restoration in an active host graph.</summary>
let run (document: Document) (media: MediaQueryProvider) =
    let graph = Graph.Current
    graph.AssertOnGraphThread "ScopeNativeProbe"
    let require condition message = if not condition then invalidOp message
    let marker = "scope-native-" + Guid.NewGuid().ToString("N")
    let element = document.createElement "div"
    element.id <- marker
    let container = document.documentElement :> Node
    container.appendChild(element :> Node) |> ignore
    let mutable parent : Owner option = None
    try
        let fallback = Scope.ResolveRoot(document,Some("#" + marker + "-missing"))
        require ((fallback :?> Node).isSameNode(document :> Node)) "Scope selector fallback lost the document."
        let resolved = Scope.ResolveRoot(document,Some("#" + marker))
        require ((resolved :?> Node).isSameNode(element :> Node)) "Scope selector did not resolve the actual element."
        let source = Api.createSignal 0
        let owner,scope = Api.createRoot(fun owner ->
            owner,new Scope(root=resolved,media=media,mediaQueries=[("all","all");("never","not all")]))
        parent <- Some owner
        let mutable constructs = 0
        scope.Add(fun current ->
            constructs <- constructs + 1
            require current.Matches["all"] "Native media query all did not match."
            require (not current.Matches["never"]) "Native media query not all unexpectedly matched."
            Scope.RegisterCurrent(fun () -> element.removeAttribute "data-scope-value")
            Api.createEffectOn (fun () -> source.Value) (fun value -> element.setAttribute("data-scope-value",string value))
            None) |> ignore
        require (element.getAttribute "data-scope-value" = "0") "Owned native effect did not initialize."
        source.Value <- 1
        graph.Flush()
        require (element.getAttribute "data-scope-value" = "1") "Owned native effect did not update."
        scope.Refresh() |> ignore
        require (constructs = 2) "Native scope did not reconstruct."
        owner.Dispose()
        require scope.IsDisposed "Native scope survived its parent owner."
        require (not(element.hasAttribute "data-scope-value")) "Native scope did not restore its attribute."
        source.Value <- 2
        graph.Flush()
        require (not(element.hasAttribute "data-scope-value")) "Disposed scope wrote through a native target."
        require (scope.Errors.Count = 0 && owner.ErrorCount = 0) "Native scope teardown recorded an error."
    finally
        parent |> Option.iter(fun owner -> owner.Dispose())
        container.removeChild(element :> Node) |> ignore
