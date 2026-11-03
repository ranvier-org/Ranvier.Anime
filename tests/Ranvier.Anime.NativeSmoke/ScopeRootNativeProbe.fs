module Ranvier.Anime.NativeSmoke.ScopeRootNativeProbe

open System
open Ranvier.Dom
open Ranvier.Anime

/// <summary>Exercises native document fallback, nested selector roots and scoped target queries in an active host graph.</summary>
let run (document: Document) =
    let require condition message = if not condition then invalidOp message
    let marker = "scope-root-" + Guid.NewGuid().ToString("N")
    let outer = document.createElement "section"
    outer.id <- marker
    let inside = document.createElement "div"
    inside.id <- marker + "-inside"
    inside.setAttribute("data-scope-root-probe",marker)
    let outside = document.createElement "div"
    outside.id <- marker + "-outside"
    outside.setAttribute("data-scope-root-probe",marker)
    (outer :> Node).appendChild(inside :> Node) |> ignore
    let container = document.documentElement :> Node
    container.appendChild(outer :> Node) |> ignore
    container.appendChild(outside :> Node) |> ignore
    try
        use scope = Scope.ForDocument(document,selector="#" + marker)
        scope.Execute(fun _ ->
            use selected = Scope.ForDocument(document,selector="#" + marker + "-inside")
            require ((selected.Root.Value :?> Node).isSameNode(inside :> Node)) "Nested scope missed its parent's selected root."
            use omitted = Scope.ForDocument document
            require ((omitted.Root.Value :?> Node).isSameNode(document :> Node)) "Omitted nested root incorrectly inherited its parent's root."
            use unmatched = Scope.ForDocument(document,selector="#" + marker + "-outside")
            require ((unmatched.Root.Value :?> Node).isSameNode(document :> Node)) "Unmatched nested selector did not fall back to the supplied document."
            let targets = ScopeTargets.select document ("[data-scope-root-probe=\"" + marker + "\"]")
            require (targets.length = 1u) "Scoped selection escaped the parent root."
            require ((targets.item 0u).isSameNode(inside :> Node)) "Scoped selection returned the wrong element.")
        let targets = ScopeTargets.select document ("[data-scope-root-probe=\"" + marker + "\"]")
        require (targets.length = 2u) "Selection outside scope did not use its explicit document."
    finally
        container.removeChild(outside :> Node) |> ignore
        container.removeChild(outer :> Node) |> ignore
