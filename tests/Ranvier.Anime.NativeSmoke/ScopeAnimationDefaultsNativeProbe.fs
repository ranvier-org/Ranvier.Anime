module Ranvier.Anime.NativeSmoke.ScopeAnimationDefaultsNativeProbe

open System
open Ranvier.Dom
open Ranvier.Css
open Ranvier.Anime

/// <summary>Checks constant scope animation defaults and original inline restoration on a live native element.</summary>
let run (document: Document) =
    Ranvier.Graph.Current.AssertOnGraphThread "ScopeAnimationDefaultsNativeProbe"
    let require condition message = if not condition then invalidOp message
    let marker="scope-animation-defaults-"+Guid.NewGuid().ToString("N")
    let element=document.createElement "div"
    element.id<-marker
    let container=document.documentElement :> Node
    container.appendChild(element :> Node) |> ignore
    try
        let style=(element :?> HTMLElement :> ElementCSSInlineStyle).style
        style.setProperty("width","7px",priority="important")
        use scheduler=new ManualFrameScheduler()
        use scope=Scope.ForDocument(document,selector="#"+marker,
            animationDefaults={AnimationDefaults.Empty with Duration=Some 240.;Ease=Some(fun value -> value*0.5)
                                                            Modifier=Some(fun value -> floor(value/10.+0.5)*10.)
                                                            Composition=Some KeyframeComposition.None;Autoplay=Some false})
        let handles=ResizeArray<KeyframeAnimation>()
        scope.Add(fun _ ->
            let animation=Keyframes.Declaration(scheduler,style,Declaration.create "width" "0px",
                [|Keyframe.toValue(Declaration.create "width" "100px")|])
            handles.Add animation
            None) |> ignore
        require (handles[0].Playback.IterationDuration=240. && handles[0].Playback.Paused)
            "Native animation lost omitted duration or autoplayfalse."
        handles[0].Playback.Seek 120.
        require (style.getPropertyValue "width"="30px") "Native interpolation did not apply scoped easing followed by modifier."
        scope.ClockDefaults.Animation<-{scope.ClockDefaults.Animation with Duration=360.}
        scope.Refresh() |> ignore
        require (handles.Count=2 && handles[1].Playback.IterationDuration=360.) "Native refresh lost retained constant defaults."
        scope.Revert()
        require (style.getPropertyValue "width"="7px" && style.getPropertyPriority "width"="important")
            "Native constant-default animation lost the original inline snapshot."
        require (scope.Errors.Count=0) "Native constant-default scope teardown recorded an error."
        scheduler.Advance 1000.
        scheduler.RunFrame()
        require (style.getPropertyValue "width"="7px") "A pending frame changed restored native CSS."
    finally
        container.removeChild(element :> Node) |> ignore
