module Ranvier.Anime.NativeSmoke.SvgNativeProbe

open System
open System.Globalization
open Ranvier
open Ranvier.Dom
open Ranvier.Anime

let private require condition message = if not condition then failwith ("SVG native probe: "+message)
let private close expected actual message = require (abs(expected-actual)<0.001) message
let private number (value:float) = value.ToString("G",CultureInfo.InvariantCulture)

/// <summary>Exercises actual mounted SVG geometry, CTM, drawing, morphing and owned numeric runner disposal.</summary>
/// <remarks>Run on the document's owner thread after native host initialization; this probe creates a temporary SVG root.</remarks>
let run (document:Document) (computed:ComputedStyleService) =
    let ns="http://www.w3.org/2000/svg"
    let svg=document.createElementNS(ns,"svg")
    svg.setAttribute("width","200")
    svg.setAttribute("height","200")
    svg.setAttribute("viewBox","0 0 200 200")
    let path=document.createElementNS(ns,"path") :?> SVGGeometryElement
    let destination=document.createElementNS(ns,"path") :?> SVGGeometryElement
    let follower=document.createElementNS(ns,"path")
    path.setAttribute("d","M0 0 L100 0")
    path.setAttribute("transform","matrix(2 0 0 3 10 20)")
    path.style.setProperty("stroke","black")
    path.style.setProperty("stroke-linecap","round")
    path.style.setProperty("vector-effect","non-scaling-stroke")
    destination.setAttribute("d","M0 0 L0 100")
    follower.setAttribute("d","M0 0 L1 0")
    svg.appendChild path |> ignore
    svg.appendChild destination |> ignore
    svg.appendChild follower |> ignore
    document.body.appendChild svg |> ignore
    try
        close 100. (float(path.getTotalLength())) "native path length"
        let midpoint=path.getPointAtLength(50.f)
        close 50. (float midpoint.x) "native path point x"
        close 0. (float midpoint.y) "native path point y"
        let outside=Svg.createMotionPath path false 0. 50.
        close 110. outside.X "captured CTM x"
        close 20. outside.Y "captured CTM y"
        close 0. outside.Angle "local path angle"
        use graph=new Graph()
        use active=graph.Activate()
        use scheduler=new ManualFrameScheduler()
        let options={KeyframeAnimationOptions.Default with Ease=id}
        let owner,drawable,morph,motionClock,morphClock =
            Api.createRoot(fun owner ->
                let drawable=Svg.createDrawable path (computed.Get(path,None)) 0. 0.
                drawable.Draw(0.,1.)
                require (path.getAttribute("stroke-dasharray")="2500 0") "non-scaling stroke CTM factor"
                let morph=SvgProperties.morph path destination 0.02 None
                let motion=SvgProperties.motion path true 0. (fun position ->
                    follower.setAttribute("transform","translate("+number position.X+" "+number position.Y+")"))
                let motionClock=Keyframes.Numeric(scheduler,motion,[|Keyframe.To 100.|],duration=100u,options=options)
                let morphClock=Keyframes.Numeric(scheduler,morph.Progress,[|Keyframe.To 1.|],duration=100u,options=options)
                owner,drawable,morph,motionClock,morphClock)
        scheduler.Advance 50.
        scheduler.RunFrame()
        require (path.getAttribute("d")="M0 0 L50 50 ") "shared morph midpoint through existing runner"
        require (follower.getAttribute("transform")="translate(50 0)") "typed motion setter through existing runner"
        (owner:>IDisposable).Dispose()
        require drawable.IsReverted "owned drawable disposed"
        require (not(path.hasAttribute("pathLength"))) "drawing pathLength absence restored"
        require (path.getAttribute("d")="M0 0 L100 0") "morph original attribute restored"
        let transform=follower.getAttribute("transform")
        scheduler.Advance 100.
        scheduler.RunFrame()
        require (follower.getAttribute("transform")=transform) "owner disposal cancels future motion writes"
        morph.Progress.Value<-1.
        require (path.getAttribute("d")="M0 0 L100 0") "disposed morph rejects later writes"
        (motionClock:>IDisposable).Dispose()
        (morphClock:>IDisposable).Dispose()
        "geometry, CTM, drawable scale, morph midpoint, motion, owner restoration"
    finally
        document.body.removeChild svg |> ignore




/// <summary>Checks actual persistent-owner selection, SVG snapshot priority and typed-setter scope reentry.</summary>
/// <remarks>Uses mounted native geometry and real Ranvier scopes without replacing native geometry methods.</remarks>
let scopeOwnersAndReentry (document:Document) (computed:ComputedStyleService) =
    let ns="http://www.w3.org/2000/svg"
    let svg=document.createElementNS(ns,"svg")
    svg.setAttribute("width","200")
    svg.setAttribute("height","200")
    let path=document.createElementNS(ns,"path") :?> SVGGeometryElement
    let destination=document.createElementNS(ns,"path") :?> SVGGeometryElement
    path.setAttribute("d","M0 0 L100 0")
    destination.setAttribute("d","M0 0 L0 100")
    path.style.setProperty("stroke-linecap","square",priority="important")
    svg.appendChild path |> ignore
    svg.appendChild destination |> ignore
    document.body.appendChild svg |> ignore
    try
        use graph=new Graph()
        use active=graph.Activate()
        use scope=new Scope()
        let resources=ResizeArray<Svg.Drawable * SvgMorph * NumericProperty * Owner>()
        let mutable applied=0
        scope.Add(fun current->
            current.AddOnce(fun _->None) |> ignore
            let ambient=Api.getOwner()
            let drawable=Svg.createDrawable path (computed.Get(path,None)) 0. 0.
            let morph=SvgProperties.morph path destination 0.02 None
            let motion=SvgProperties.motion path true 0. (fun _->applied<-applied+1)
            resources.Add(drawable,morph,motion,ambient)
            None) |> ignore
        let drawable,morph,motion,ambient=resources[0]
        scope.Refresh() |> ignore
        require ambient.IsDisposed "regular ambient epoch ended"
        require (not drawable.IsReverted) "persistent drawable selects actual persistent owner"
        drawable.Draw(0.25,0.75)
        require (path.getAttribute("draw")="0.25 0.75") "retained drawable writes"
        morph.Progress.Value<-0.5
        require (path.getAttribute("d")="M0 0 L50 50 ") "retained morph writes"
        motion.Value<-10.
        require (applied=1) "retained native motion sampling"
        scope.Revert()
        require drawable.IsReverted "final scope teardown"
        require (path.getAttribute("d")="M0 0 L100 0") "immutable original morph attribute"
        require (not(path.hasAttribute "pathLength")) "drawing normalization absence"
        require (path.style.getPropertyValue("stroke-linecap")="square") "original cap"
        require (path.style.getPropertyPriority("stroke-linecap")="important") "original cap priority"
        motion.Value<-20.
        require (applied=1) "persistent motion closes at final disposal"
        use reentrant=new Scope()
        let mutable calls=0
        let distance=reentrant.Execute(fun _->
            SvgProperties.motion path true 0. (fun _->calls<-calls+1;reentrant.Revert()))
        distance.Value<-5.
        require (calls=1) "typed target setter called"
        require (distance.Value=0.) "scope reentry prevents late distance commit"
        distance.Value<-10.
        require (calls=1) "scope reentry prevents later typed setters"
        "persistent SVG ownership, immutable snapshots, priority, typed motion reentry"
    finally document.body.removeChild svg |> ignore
