module Ranvier.Anime.NativeSmoke.LayoutNativeProbe

open System
open Ranvier.Anime
open Ranvier.Dom
open Ranvier.Css
open Ranvier.Css.Dom

/// <summary>Checks native entry/exit visibility, pause cleanup and restoration after a DOM mutation.</summary>
let runEntryExit (document:Document) (computed:ComputedStyleService) =
    let root=document.createElement "div" :?> HTMLElement
    let child=document.createElement "div" :?> HTMLElement
    let style (element:HTMLElement)=(element:>ElementCSSInlineStyle).style
    let apply element name value=StyleDeclaration.apply (style element) (Declaration.create name value)
    for element in [root;child] do
        for name,value in ["width","100px";"height","100px";"padding","0px";"border","0px";"margin","0px"] do
            apply element name value
    (root:>Node).appendChild(child:>Node) |> ignore
    (document.body:>Node).appendChild(root:>Node) |> ignore
    try
        use scheduler=new ManualFrameScheduler()
        use layout=new AutoLayout(root,computed,scheduler:>FrameScheduler,
                                  options={LayoutOptions.Default with Duration=(fun _ -> 100.);Ease=id;Autoplay=false})
        apply child "display" "none"
        let exit=layout.Animate()
        exit.Seek(25.,muteCallbacks=true)
        let leavingOpacity=Double.Parse((computed.Get(child,None)).getPropertyValue "opacity",Globalization.CultureInfo.InvariantCulture)
        if abs(leavingOpacity-0.75)>0.01 || layout.Leaving<>[|child|] then
            failwith ("Native exit expected one leaving target at opacity .75, got "+string leavingOpacity)
        exit.Resume()
        exit.Pause()
        if (style child).getPropertyValue "display"<>"none" || root.classList.contains "is-animated" then
            failwith "Native exit pause failed to restore post-mutation visibility."
        layout.Record()
        apply child "display" "block"
        let enter=layout.Animate()
        enter.Seek(25.,muteCallbacks=true)
        let enteringOpacity=Double.Parse((computed.Get(child,None)).getPropertyValue "opacity",Globalization.CultureInfo.InvariantCulture)
        if abs(enteringOpacity-0.25)>0.01 || layout.Entering<>[|child|] then
            failwith ("Native entry expected one entering target at opacity .25, got "+string enteringOpacity)
        layout.Revert()
        if (style child).getPropertyValue "display"<>"block" || (style child).getPropertyValue "opacity"<>"" then
            failwith "Native lifecycle revert lost the mutation or retained owned opacity."
        {| LeavingOpacity=leavingOpacity;EnteringOpacity=enteringOpacity
           Scope="Native measured entry/exit opacity, visibility and pause/revert cleanup; no acceleration claim." |}
    finally
        let parent=(root:>Node).parentNode
        if not(isNull(box parent)) then parent.removeChild(root:>Node) |> ignore


/// <summary>Checks eased midpoint swaps and muted callback dispatch against measured native layout.</summary>
let runSwapEasing (document:Document) (computed:ComputedStyleService) =
    let root=document.createElement "div" :?> HTMLElement
    let selected=document.createElement "div" :?> HTMLElement
    let nested=document.createElement "div" :?> HTMLElement
    let style (element:HTMLElement)=(element:>ElementCSSInlineStyle).style
    let apply element name value=StyleDeclaration.apply (style element) (Declaration.create name value)
    for element,width in [root,"300px";selected,"100px";nested,"20px"] do
        for name,value in ["width",width;"height",width;"padding","0px";"border","0px";"margin","0px"] do
            apply element name value
    (selected:>Node).appendChild(nested:>Node) |> ignore
    (root:>Node).appendChild(selected:>Node) |> ignore
    (document.body:>Node).appendChild(root:>Node) |> ignore
    try
        use scheduler=new ManualFrameScheduler()
        use layout=new AutoLayout(root,computed,scheduler:>FrameScheduler,
                                  options={LayoutOptions.Default with Duration=(fun _ -> 100.);Ease=id;Autoplay=false;
                                                                     Targets=Some [|selected|];PlaybackEase=Some(fun t -> t*t)})
        apply selected "width" "200px"
        apply selected "height" "200px"
        apply nested "width" "50px"
        apply nested "height" "50px"
        let animation=layout.Animate()
        animation.Seek 50.
        let before=nested.getBoundingClientRect().width
        animation.Seek(75.,muteCallbacks=true)
        let muted=nested.getBoundingClientRect().width
        animation.Seek 75.
        let after=nested.getBoundingClientRect().width
        if abs(before-20.)>0.01 || abs(muted-20.)>0.01 || abs(after-50.)>0.01 then
            failwith ("Native eased swap expected 20/20/50, got "+string before+"/"+string muted+"/"+string after)
        if abs(animation.CurrentTime-75.)>0.001 || abs(animation.IterationCurrentTime-56.25)>0.001 then
            failwith "Native eased swap exposed incorrect raw or mapped controller time."
        layout.Revert()
        if (style nested).getPropertyValue "width"<>"50px" then
            failwith "Native eased swap revert lost the post-mutation declaration."
        {| Before=before;Muted=muted;After=after
           Scope="Native measured eased Timeline.Call swap and callback muting; no compositor/GPU claim." |}
    finally
        let parent=(root:>Node).parentNode
        if not(isNull(box parent)) then parent.removeChild(root:>Node) |> ignore


/// <summary>Runs measured layout and restoration checks on the caller's native document and style service.</summary>
let run (document:Document) (computed:ComputedStyleService) =
    let root=document.createElement "div" :?> HTMLElement
    let child=document.createElement "div" :?> HTMLElement
    let style (element:HTMLElement)=(element:>ElementCSSInlineStyle).style
    let apply element name value=StyleDeclaration.apply (style element) (Declaration.create name value)
    for name,value in ["position","relative";"width","300px";"height","300px";"padding","0px";"border","0px";"margin","0px"] do
        apply root name value
    for name,value in ["position","absolute";"left","30px";"top","40px";"width","100px";"height","100px";"padding","0px";"border","0px";"margin","0px"] do
        apply child name value
    (root:>Node).appendChild(child:>Node) |> ignore
    (document.body:>Node).appendChild(root:>Node) |> ignore
    try
        let scheduler=new ManualFrameScheduler()
        use layout=new AutoLayout(root,computed,scheduler:>FrameScheduler,
                                  options={LayoutOptions.Default with Duration=(fun _ -> 100.);Ease=id})
        let original=child.getBoundingClientRect()
        if abs(original.width-100.)>0.01 then failwith ("Native layout setup width "+string original.width)
        apply child "left" "60px"
        apply child "top" "80px"
        apply child "width" "160px"
        apply child "height" "70px"
        let animation=layout.Animate()
        animation.Seek(25.,muteCallbacks=true)
        let measured=child.getBoundingClientRect()
        if abs(measured.width-115.)>0.01 || abs(measured.height-92.5)>0.01 then
            failwith ("Native layout quarter dimensions "+string measured.width+" x "+string measured.height)
        let translation=(style child).getPropertyValue "translate"
        animation.Complete()
        if (style child).getPropertyValue "width"<>"160px" || (style child).getPropertyValue "height"<>"70px" then
            failwith "Native layout completion did not restore post-mutation declarations."
        if root.classList.contains "is-animated" then failwith "Native layout class remained after completion."
        {| Width=measured.width; Height=measured.height; Translate=translation
           Scope="Native measured CSS layout and post-mutation restoration; no compositor/GPU claim." |}
    finally
        let parent=(root:>Node).parentNode
        if not(isNull(box parent)) then parent.removeChild(root:>Node) |> ignore

/// <summary>Checks native transform interpolation and overshoot through the layout's owned effect sampler.</summary>
let runTransforms (document:Document) (computed:ComputedStyleService) =
    let cases=[| "linear",id,35.;"overshoot",(fun t -> if t=0. then 0. else 1.25),135. |]
    cases |> Array.map(fun (name,ease,expected) ->
        let root=document.createElement "div" :?> HTMLElement
        let child=document.createElement "div" :?> HTMLElement
        let style (element:HTMLElement)=(element:>ElementCSSInlineStyle).style
        let apply element property value=StyleDeclaration.apply (style element) (Declaration.create property value)
        for property,value in ["position","relative";"width","300px";"height","300px";"padding","0px";"border","0px"] do
            apply root property value
        for property,value in ["width","100px";"height","100px";"transform","translateX(10px)"] do
            apply child property value
        (root:>Node).appendChild(child:>Node) |> ignore
        (document.body:>Node).appendChild(root:>Node) |> ignore
        try
            let scheduler=new ManualFrameScheduler()
            use layout=new AutoLayout(root,computed,scheduler:>FrameScheduler,
                                      options={LayoutOptions.Default with Duration=(fun _ -> 100.);Ease=ease;Autoplay=false})
            apply child "transform" "translateX(110px)"
            let animation=layout.Animate()
            animation.Seek(25.,muteCallbacks=true)
            let transform=(computed.Get(child,None)).getPropertyValue "transform"
            let numbers=System.Text.RegularExpressions.Regex.Matches(transform,@"-?\d+(?:\.\d+)?(?:[eE][+-]?\d+)?")
                        |> Seq.cast<System.Text.RegularExpressions.Match>
                        |> Seq.map(fun m -> Double.Parse(m.Value,Globalization.CultureInfo.InvariantCulture)) |> Seq.toArray
            if not(transform.StartsWith "matrix(") || numbers.Length<>6 || abs(numbers[4]-expected)>0.01 then
                failwith ("Native "+name+" transform expected translateX "+string expected+", got "+transform)
            layout.Revert()
            if (style child).getPropertyValue "transform"<>"translateX(110px)" then
                failwith "Native transform revert did not retain post-mutation inline transform."
            {| Name=name; Transform=transform; ExpectedTranslation=expected
               Scope="Native CSS transform interpolation, exact sampled easing and cancellation; no acceleration claim." |}
        finally
            let parent=(root:>Node).parentNode
            if not(isNull(box parent)) then parent.removeChild(root:>Node) |> ignore)


