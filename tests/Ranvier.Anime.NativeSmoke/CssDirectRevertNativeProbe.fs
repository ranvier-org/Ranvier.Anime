module Ranvier.Anime.NativeSmoke.CssDirectRevertNativeProbe

open System
open Ranvier.Dom
open Ranvier.Css
open Ranvier.Anime

/// <summary>Checks direct custom-property handle reversion and later scope teardown against native inline CSS.</summary>
let run (document: Document) =
    Ranvier.Graph.Current.AssertOnGraphThread "CssDirectRevertNativeProbe"
    let require condition message = if not condition then invalidOp message
    let element=document.createElement "div"
    let container=document.documentElement :> Node
    container.appendChild(element :> Node) |> ignore
    try
        let style=(element :?> HTMLElement :> ElementCSSInlineStyle).style
        use scheduler=new ManualFrameScheduler()
        style.setProperty("--scope-direct-gap","7px",priority="important")
        use scope=new Scope()
        let mutable handle:NumericAnimation option=None
        scope.Add(fun _ ->
            handle<-Some(CssAnimation.customProperty scheduler style
                (RuntimeApi.createCustomProperty "--scope-direct-gap")
                (Generated.number 0. "px") (Generated.number 100. "px") 100.)
            None) |> ignore
        handle.Value.Seek 50.
        handle.Value.Revert()
        require (style.getPropertyValue "--scope-direct-gap"="7px" &&
                 style.getPropertyPriority "--scope-direct-gap"="important") "Direct native CSS revert lost the original declaration."
        scope.Revert()
        require (style.getPropertyValue "--scope-direct-gap"="7px") "Later scope teardown changed direct-restored native CSS."
        use standalone =
            CssAnimation.customProperty scheduler style
                (RuntimeApi.createCustomProperty "--scope-direct-absent")
                (Generated.number 0. "px") (Generated.number 100. "px") 100.
        standalone.Revert()
        require (style.getPropertyValue "--scope-direct-absent"="") "Direct native CSS revert lost original absence."
        require (handle.Value.Cancelled && standalone.Cancelled && scope.Errors.Count=0) "Native direct CSS restoration did not close cleanly."
        scheduler.Advance 1000.
        scheduler.RunFrame()
        require (style.getPropertyValue "--scope-direct-gap"="7px" && style.getPropertyValue "--scope-direct-absent"="")
            "A pending frame changed direct-restored native CSS."
    finally
        container.removeChild(element :> Node) |> ignore
