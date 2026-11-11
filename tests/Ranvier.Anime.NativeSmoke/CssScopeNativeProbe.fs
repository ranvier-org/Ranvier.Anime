module Ranvier.Anime.NativeSmoke.CssScopeNativeProbe

open System
open Ranvier.Dom
open Ranvier.Css
open Ranvier.Anime

/// <summary>Checks legacy CSS scope restoration against a live host CSS declaration and its native shorthand expansion.</summary>
let run (document: Document) =
    Ranvier.Graph.Current.AssertOnGraphThread "CssScopeNativeProbe"
    let require condition message = if not condition then invalidOp message
    let element = document.createElement "div"
    let container = document.documentElement :> Node
    container.appendChild(element :> Node) |> ignore
    try
        let style = (element :?> HTMLElement :> ElementCSSInlineStyle).style
        use scheduler = new ManualFrameScheduler()
        use scope = new Scope()
        style.setProperty("margin-top","1px",priority="important")
        style.setProperty("margin-right","2px")
        style.setProperty("margin-bottom","3px")
        style.setProperty("margin-left","4px",priority="important")
        style.setProperty("--gap","7px",priority="important")
        style.setProperty("color","red")
        let mutable declaration : Timer option = None
        let mutable custom : NumericAnimation option = None
        let mutable absent : Timer option = None
        scope.Add(fun _ ->
            declaration <- Some(CssAnimation.declaration scheduler style
                (Declaration.create "margin" "0px") (Declaration.create "margin" "100px") 100u)
            custom <- Some(CssAnimation.customProperty scheduler style
                (RuntimeApi.createCustomProperty "--gap") (Generated.number 0. "px") (Generated.number 100. "px") 100.)
            absent <- Some(CssAnimation.declaration scheduler style
                (Declaration.create "width" "0px") (Declaration.create "width" "100px") 100u)
            None) |> ignore
        declaration.Value.Complete()
        custom.Value.Complete()
        absent.Value.Seek 50.
        style.setProperty("color","blue")
        scope.Revert()
        for name,value,priority in
            ["margin-top","1px","important";"margin-right","2px",""
             "margin-bottom","3px","";"margin-left","4px","important";"--gap","7px","important"] do
            require (style.getPropertyValue name=value && style.getPropertyPriority name=priority)
                ("Native inline restoration failed for "+name)
        require (style.getPropertyValue "width"="") "An originally absent native width survived scope restoration."
        require (style.getPropertyValue "color"="blue") "Scope restoration overwrote an unrelated native edit."
        require (declaration.Value.Cancelled && custom.Value.Cancelled && absent.Value.Cancelled)
            "A restored CSS playback driver survived teardown."
        require (scope.Errors.Count=0) "Native CSS scope restoration recorded an error."
        scheduler.Advance 1000.
        scheduler.RunFrame()
        require (style.getPropertyValue "margin-right"="2px" && style.getPropertyValue "--gap"="7px")
            "A pending frame changed the restored native declarations."
    finally
        container.removeChild(element :> Node) |> ignore
