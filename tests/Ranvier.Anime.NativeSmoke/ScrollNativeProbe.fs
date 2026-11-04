module Ranvier.Anime.NativeSmoke.ScrollNativeProbe

open System
open Ranvier
open Ranvier.Anime
open Ranvier.Dom
open Ranvier.View.WebCore

let private require name condition =
    if not condition then failwith ("SCROLL_NATIVE " + name)
    Console.WriteLine("PASS scroll " + name)

let private inlineStyle (element:HTMLElement) = (element:>ElementCSSInlineStyle).style
let private close actual expected = abs(actual-expected)<0.02
let private dispatch (host:WebCoreHost) (target:EventTarget) =
    let event = (EventConstruction.forHost host).Create("scroll",Ranvier.Dom.EventConstruction.Event EventOptions.Default)
    target.dispatchEvent event |> ignore

/// <summary>Exercises real native DOM geometry, manual event dispatch, shared subscriptions, debug DOM and borrowed controller ownership.</summary>
let run (host:WebCoreHost) =
    use graph = new Graph()
    use activation = graph.Activate()
    let owner = Api.createRoot id
    use scheduler = new ManualFrameScheduler()
    use registry = new ScrollContainerRegistry(scheduler,ComputedStyles.forHost host,ResizeObservation.forHost host)
    owner.Attach registry
    let element = host.Document.createElement("div") :?> HTMLElement
    let target = host.Document.createElement("div") :?> HTMLElement
    let tail = host.Document.createElement("div") :?> HTMLElement
    let style = inlineStyle element
    style.setProperty("width","200px")
    style.setProperty("height","100px")
    style.setProperty("overflow","auto")
    style.setProperty("position","static",priority="important")
    (inlineStyle target).setProperty("margin-top","300px")
    (inlineStyle target).setProperty("height","50px")
    (inlineStyle target).setProperty("width","50px")
    (inlineStyle tail).setProperty("height","900px")
    element.appendChild target |> ignore
    element.appendChild tail |> ignore
    host.Document.body.appendChild element |> ignore
    try
        let first = registry.ForElement element
        require "registry shares native element identity" (Object.ReferenceEquals(first,registry.ForElement element))
        let nodes = element.childNodes.length
        use timer = new Timer(scheduler,1000u)
        timer.Seek 123.
        let mutable updates = 0
        use observer = first.ObserveLinked(ScrollLink.OfTimer timer,[target],
            options=(fun () -> {ScrollOptions.Default with Sync=ScrollSync.Smooth 1.}),
            onEvent=(fun event _ -> if event=ScrollEvent.Update then updates<-updates+1))
        require "linked target measures native layout immediately" (observer.Ready && close observer.Offset 300.)
        require "measurement restores borrowed playhead" (timer.CurrentTime=123.)
        observer.Debug() |> ignore
        require "debug attaches rulers" (element.childNodes.length=nodes+1u)
        require "debug snapshots container positioning" (style.getPropertyValue "position"="relative")
        use second = first.Observe(target,debug=true,id="second")
        scheduler.RunFrame()
        require "two observers retain independent debug overlays" (element.childNodes.length=nodes+2u)
        element.scrollTop <- int (observer.OffsetStart+observer.Distance/2.)
        dispatch host (element:>EventTarget)
        // The first frame begins the wake timer, which resumes the scroll ticker; Anime.js ticks a root resumed
        // during traversal on the following frame.
        scheduler.Advance 40.; scheduler.RunFrame()
        scheduler.Advance 17.; scheduler.RunFrame()
        require "real native scroll event scrubs borrowed timer" (close timer.Progress 0.5 && updates>0)
        observer.Revert()
        require "remaining debug overlay retains positioning lease" (style.getPropertyValue "position"="relative" && registry.Count=1)
        second.Revert()
        require "last observer removes rulers and evicts registry" (element.childNodes.length=nodes && registry.Count=0)
        require "debug restores native value and importance" (style.getPropertyValue "position"="static" && style.getPropertyPriority "position"="important")
        require "observer cleanup retains borrowed timer" (not timer.Cancelled)
        let priorUpdates,priorTime = updates,timer.CurrentTime
        element.scrollTop <- 0
        dispatch host (element:>EventTarget)
        scheduler.Advance 1000.; scheduler.RunFrame()
        require "disposed observers stop writes after dispatched scroll" (updates=priorUpdates && timer.CurrentTime=priorTime)

        use revived = registry.ForElement element
        let mutable resizes = 0
        use resized = revived.Observe(target,onEvent=(fun event _ -> if event=ScrollEvent.Resize then resizes<-resizes+1))
        scheduler.RunFrame()
        style.setProperty("height","120px")
        host.Cycle(milliseconds=1); host.Frame()
        scheduler.Advance 300.; scheduler.RunFrame()
        require "native resize delivery refreshes geometry" (resizes>0 && close resized.OffsetStart 180.)
        owner.Dispose()
        let retained = resizes
        style.setProperty("height","130px")
        dispatch host (element:>EventTarget)
        host.Cycle(milliseconds=1); host.Frame()
        scheduler.Advance 1000.; scheduler.RunFrame()
        require "owner cleanup closes native resize and scroll callbacks" (registry.Count=0 && resizes=retained && resized.Reverted)
    finally
        owner.Dispose()
        let parent = (element:>Node).parentNode
        if not(isNull(box parent)) then parent.removeChild(element:>Node) |> ignore

/// <summary>Checks the viewport's native window identity, temporary 100lvh measurement and owner shutdown before host destruction.</summary>
let runViewportAndShutdown () =
    use host = WebCoreHost.Create(320,200)
    use scheduler = Frames.forHost host
    use graph = new Graph()
    use activation = graph.Activate()
    let owner = Api.createRoot id
    let provider = Viewport.provider host
    let scroll = ViewportScroll.forHost host
    let registry = new ScrollContainerRegistry(scheduler,ComputedStyles.forHost host,ResizeObservation.forHost host)
    owner.Attach registry
    let before = host.Document.body.childNodes.length
    let first = registry.ForViewport(provider,scroll)
    require "viewport registry shares actual native window identity" (Object.ReferenceEquals(first,registry.ForViewport(provider,scroll)))
    require "100lvh measurement removes temporary native node" (host.Document.body.childNodes.length=before)
    let target = host.Document.createElement("div") :?> HTMLElement
    (inlineStyle host.Document.body).setProperty("height","2000px")
    (inlineStyle target).setProperty("position","absolute")
    (inlineStyle target).setProperty("top","600px")
    (inlineStyle target).setProperty("height","50px")
    host.Document.body.appendChild target |> ignore
    use observer = first.Observe target
    observer.Refresh()
    let midpoint = observer.OffsetStart+observer.Distance/2.
    scroll.ScrollTo {Left=None;Top=Some midpoint;Behavior=ScrollBehavior.Instant}
    dispatch host provider.Window
    require "actual viewport scroll service and dispatched window event update progress" (close observer.Progress 0.5)
    owner.Dispose()
    require "owner closes deferred viewport work before host teardown" (observer.Reverted && registry.Count=0)
    host.Cycle(milliseconds=10); host.Frame()
    require "native viewport teardown reports no listener failures" (host.Errors.Count=0)
