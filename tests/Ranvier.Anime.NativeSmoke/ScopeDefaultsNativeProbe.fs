module Ranvier.Anime.NativeSmoke.ScopeDefaultsNativeProbe

open System
open Ranvier.Dom
open Ranvier.Anime

/// <summary>Verifies native nested root routing together with retained clock defaults in methods and refresh.</summary>
let run (document: Document) =
    Ranvier.Graph.Current.AssertOnGraphThread "ScopeDefaultsNativeProbe"
    ScopeRootNativeProbe.run document
    let require condition message = if not condition then invalidOp message
    let marker="scope-defaults-"+Guid.NewGuid().ToString("N")
    let outer=document.createElement "section"
    let inside=document.createElement "div"
    let outside=document.createElement "div"
    outer.id<-marker
    inside.setAttribute("data-scope-defaults-probe",marker)
    outside.setAttribute("data-scope-defaults-probe",marker)
    let container=document.documentElement :> Node
    (outer :> Node).appendChild(inside :> Node) |> ignore
    container.appendChild(outer :> Node) |> ignore
    container.appendChild(outside :> Node) |> ignore
    try
        use frames=new ManualFrameScheduler()
        use scope=Scope.ForDocument(document,selector="#"+marker,
            clockDefaults={PlaybackDefaults.Empty with Speed=Some 3.;Reversed=Some true;Delay=Some 20.})
        let speeds=ResizeArray<float>()
        scope.Add(fun _ ->
            use clock=new Timer(frames,100.)
            speeds.Add clock.Speed
            let targets=ScopeTargets.select document ("[data-scope-defaults-probe=\""+marker+"\"]")
            require (targets.length=1u) "Refresh construction escaped the active native root."
            None) |> ignore
        scope.AddMethod("inspect",fun () ->
            use clock=new Timer(frames,100.)
            let targets=ScopeTargets.select document ("[data-scope-defaults-probe=\""+marker+"\"]")
            clock.Speed,clock.Delay,clock.Reversed,targets.length) |> ignore
        require (scope.Invoke<unit,float*float*bool*uint>("inspect",())=(3.,20.,true,1u))
            "Named method lost native root or typed clock defaults."
        scope.ClockDefaults.Options <- {scope.ClockDefaults.Options with Speed=4.}
        scope.Refresh() |> ignore
        require (Seq.toList speeds=[3.;4.]) "Refresh did not read retained mutable defaults."
        scope.Execute(fun _ ->
            use nested=Scope.ForDocument document
            require (Object.ReferenceEquals(nested.ClockDefaults,scope.ClockDefaults)) "No-override nested defaults lost identity."
            require ((nested.Root.Value :?> Node).isSameNode(document :> Node)) "Omitted nested native root inherited incorrectly.")
    finally
        container.removeChild(outside :> Node) |> ignore
        container.removeChild(outer :> Node) |> ignore
