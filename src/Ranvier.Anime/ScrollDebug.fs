namespace Ranvier.Anime

open System
open System.Globalization
open Ranvier.Dom

/// <summary>Source ruler edge flips and pixel positions for resolved anchor labels.</summary>
module internal ScrollDebugMath =
    /// <summary>Returns the source pixel position, border side and edge-flip flag for one resolved anchor.</summary>
    let placement horizontal width height scrollWidth scrollHeight offset coordinate index =
        let size = if width>360. then 320. else 260.
        let half = if horizontal then 24. else size/2.
        let labelHeight,labelWidth = if horizontal then half,60. else 15.,half
        let labelSize = if horizontal then labelWidth else labelHeight
        let view,tail = index>1,index%2=1
        let value = (if view then 0. else offset)+coordinate
        let firstLabel = value<labelSize
        let over = value>(if view then (if horizontal then width else height) else (if horizontal then scrollWidth else scrollHeight))-labelSize
        let flip = (if view then tail && not firstLabel else not tail && not firstLabel) || over
        let direction = if horizontal then (if flip then "right" else "left") else (if flip then "bottom" else "top")
        let flipOffset =
            if flip then (if horizontal then labelWidth else labelHeight)+(if not view then (if horizontal then -1. else -2.) else (if horizontal then -1. elif over then 0. else -2.))
            else (if horizontal then 1. else 0.)
        value-flipOffset+(if horizontal then 1. else 0.),direction,flip

/// <summary>Source debug ruler DOM, labels and reversible container-position lease.</summary>
type internal ScrollDebugOverlay(element:HTMLElement, dimensions:unit -> float * float * float * float,
                                 nextZ:unit -> int, index:int, id:string, acquirePosition:unit -> IDisposable) =
    let mutable root:HTMLElement option = None
    let mutable position:IDisposable option = None
    let colors = [|"#FF4B4B";"#FF971B";"#FFC730";"#F9F640";"#7AFF5A";"#18FF74";"#17E09B";"#3CFFEC";"#05DBE9";"#33B3F1";"#638CF9";"#C563FE";"#FF4FCF";"#F93F8A"|]
    let number (value:float) = value.ToString("G17",CultureInfo.InvariantCulture)
    let px value = number value+"px"
    let style (node:HTMLElement) = (node:>ElementCSSInlineStyle).style :> CSSStyleDeclaration
    let create () = (element:>Node).ownerDocument.createElement("div") :?> HTMLElement
    let append (parent:HTMLElement) (child:HTMLElement) = parent.appendChild(child:>Node) |> ignore
    let baseCss kind left top width height =
        "position:"+kind+";left:"+px left+";top:"+px top+";width:"+px width+";height:"+px height+";"
    let remove () =
        let oldRoot,oldPosition = root,position
        root <- None; position <- None
        try
            oldRoot |> Option.iter(fun node ->
                let parent = (node:>Node).parentNode
                if not(isNull(box parent)) then parent.removeChild(node:>Node) |> ignore)
        finally oldPosition |> Option.iter(fun lease -> lease.Dispose())

    /// <summary>Replaces the overlay with rulers and four resolved anchor labels.</summary>
    member _.Render(observer:ScrollObserver, options:ScrollOptions) =
        remove()
        if observer.Ready && not observer.Reverted then
            let horizontal = options.Horizontal
            let width,height,scrollWidth,scrollHeight = dimensions()
            let size = if width>360. then 320. else 260.
            let half = if horizontal then 24. else size/2.
            let labelHeight,labelWidth = if horizontal then half,60. else 15.,half
            let color = colors[index % colors.Length]
            let existing = element.querySelector(":scope > .animejs-onscroll-debug")
            let first = isNull(box existing)
            let overlay,thresholds,triggers = create(),create(),create()
            root <- Some overlay
            try
                let repeat = if horizontal then "repeat-x" else "repeat-y"
                let gradientOffset value = if horizontal then "0px "+px value else px value+" 2px"
                let line color = "linear-gradient("+(if horizontal then "90" else "0")+"deg, "+color+" 2px, transparent 1px)"
                (style overlay).cssText <- (baseCss "absolute" (if horizontal then 0. else 10.) (if horizontal then 10. else 0.)
                    (if horizontal then scrollWidth else size) (if horizontal then size else scrollHeight)) +
                    "pointer-events:none;z-index:"+string(nextZ())+";display:flex;flex-direction:"+(if horizontal then "column" else "row")+";filter:drop-shadow(0px 1px 0px rgba(0,0,0,.75));"
                (style thresholds).cssText <- (baseCss "sticky" 0. 0. (if horizontal then width else half) (if horizontal then half else height)) +
                    (if first then "background:"+line "#FFFF"+gradientOffset(half-10.)+" / 100px 100px "+repeat+","+line "#FFF8"+gradientOffset(half-10.)+" / 10px 10px "+repeat+";" else "")
                (style triggers).cssText <- (baseCss "relative" 0. 0. (if horizontal then scrollWidth else half) (if horizontal then half else scrollHeight)) +
                    (if first then "background:"+line "#FFFF"+gradientOffset 0.+" / "+(if horizontal then "100px 10px" else "10px 100px")+" "+repeat+","+line "#FFF8"+gradientOffset 0.+" / "+(if horizontal then "10px 0px" else "0px 10px")+" "+repeat+";" else "")
                let authored = [|options.Enter.Target;options.Leave.Target;options.Enter.Container;options.Leave.Container|]
                observer.Coordinates |> Array.iteri(fun i coordinate ->
                    let view,tail = i>1,i%2=1
                    let labelPosition,direction,flip = ScrollDebugMath.placement horizontal width height scrollWidth scrollHeight observer.Offset coordinate i
                    let label,text = create(),create()
                    (text:>Node).textContent <- id+(if tail then " leave: " else " enter: ")+authored[i]
                    (style label).cssText <- (baseCss "absolute" 0. 0. labelWidth labelHeight) +
                        "display:flex;flex-direction:"+(if horizontal then "column" else "row")+";justify-content:flex-"+(if view then "start" else "end")+";align-items:flex-"+(if flip then "end" else "start")+";border-"+direction+":2px solid "+color+";"
                    let cornerA,cornerB =
                        if horizontal then (if flip then "top-left","bottom-left" else "top-right","bottom-right")
                        else (if flip then "top-left","top-right" else "bottom-left","bottom-right")
                    (style text).cssText <- "overflow:hidden;max-width:"+px(size/2.-10.)+";height:"+px labelHeight+";margin-"+direction+":-2px;padding:1px;font-family:ui-monospace,monospace;font-size:10px;letter-spacing:-.025em;line-height:9px;font-weight:600;text-align:"+(if (horizontal && flip) || (not horizontal && not view) then "right" else "left")+";white-space:pre;text-overflow:ellipsis;color:"+(if tail then color else "rgba(0,0,0,.75)")+";background-color:"+(if tail then "rgba(0,0,0,.65)" else color)+";border:2px solid "+(if tail then color else "transparent")+";border-"+cornerA+"-radius:5px;border-"+cornerB+"-radius:5px;"
                    (style label).setProperty((if horizontal then "left" else "top"),px labelPosition)
                    append label text
                    append (if view then thresholds else triggers) label)
                append overlay thresholds; append overlay triggers
                if first then (overlay:>Element).setAttribute("class","animejs-onscroll-debug")
                position <- Some(acquirePosition())
                append element overlay
            with _ -> remove(); reraise()

    /// <summary>Tracks perpendicular scrolling and raises an entering overlay above earlier observers.</summary>
    member _.Sample(horizontal:bool, x:float, y:float, entering:bool) =
        root |> Option.iter(fun root ->
            (style root).setProperty((if horizontal then "top" else "left"),px((if horizontal then y else x)+10.))
            if entering then (style root).setProperty("z-index",string(nextZ())))

    /// <summary>Removes ruler DOM and releases its reversible position lease.</summary>
    member _.Remove() = remove()
