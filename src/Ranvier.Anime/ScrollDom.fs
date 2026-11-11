namespace Ranvier.Anime

open System
open System.Globalization
open Ranvier.Dom

/// <summary>Shared native element scroll events, frame sampling, velocity and debounced resize refresh.</summary>
type ScrollContainer(scheduler:FrameScheduler, element:HTMLElement, computed:ComputedStyleService,
                     resize:ResizeObservationService, ?viewport:ViewportProvider * ViewportScrollService, ?onDispose:unit -> unit) as this =
    static let mutable debugIndex = -1
    let mutable debugZ = 0
    let mutable debugPositions = 0
    let mutable debugPositionSnapshot:(CSSStyleDeclaration * string * string) option = None
    let nextDebugZ () = let value = debugZ in debugZ <- debugZ+1; value
    let acquireDebugPosition () =
        if debugPositions = 0 && (computed.Get(element,None)).getPropertyValue "position" = "static" then
            let style = (element:>ElementCSSInlineStyle).style :> CSSStyleDeclaration
            debugPositionSnapshot <- Some(style,style.getPropertyValue "position",style.getPropertyPriority "position")
            style.setProperty("position","relative")
        debugPositions <- debugPositions+1
        let mutable released = false
        { new IDisposable with
            member _.Dispose() =
                if not released then
                    released <- true
                    debugPositions <- debugPositions-1
                    if debugPositions = 0 then
                        debugPositionSnapshot |> Option.iter(fun (style,value,priority) ->
                            if value="" then style.removeProperty "position" |> ignore
                            else style.setProperty("position",value,priority))
                        debugPositionSnapshot <- None }
    let children = ResizeArray<ScrollObserver>()
    let mutable disposed = false
    let mutable x,y,previousX,previousY,velocity = 0.,0.,0.,0.,0.
    let mutable backwardX,backwardY = false,false
    let mutable left,top,width,height,scrollWidth,scrollHeight,scale = 0.,0.,0.,0.,0.,0.,1.
    let data = new Timer(scheduler,infinity,options={PlaybackOptions.Default with FrameRate=30.},onUpdate=(fun timer ->
        let dx,dy = previousX-x,previousY-y
        previousX <- x; previousY <- y
        if dx <> 0. then backwardX <- dx > 0.
        if dy <> 0. then backwardY <- dy > 0.
        velocity <- ScrollMath.round 5 (if timer.DeltaTime > 0. then sqrt(dx*dx+dy*dy)/timer.DeltaTime else 0.)))
    let ticker = new Timer(scheduler,infinity,onBegin=(fun _ -> data.Resume()),onUpdate=(fun _ ->
        let ordered = children.ToArray()
        if backwardX || backwardY then Array.Reverse ordered
        for child in ordered do if not disposed then child.HandleScroll()))
    let wake = new Timer(scheduler,500u,onBegin=(fun _ -> ticker.Resume()),onComplete=(fun _ -> ticker.Pause()))
    let resizeTicker = new Timer(scheduler,250u,onComplete=(fun _ -> this.Refresh()))
    let eventTarget = match viewport with Some(provider,_) -> provider.Window | None -> element:>EventTarget
    let readCoords () =
        match viewport with
        | Some(_,scroll) ->
            let position = scroll.Position()
            x <- ScrollMath.round 0 position.X
            y <- ScrollMath.round 0 position.Y
        | None ->
            x <- float element.scrollLeft
            y <- float element.scrollTop
    let number (value:string) =
        let token = value.Trim().TrimEnd([|'p';'x'|])
        match Double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture) with
        | true,value -> value
        | _ -> 0.
    let bounds () =
        let rect = element.getBoundingClientRect()
        let style = computed.Get(element,None)
        match viewport with
        | Some(provider,_) ->
            left <- 0.; top <- 0.; scale <- 1.
            width <- float (provider.Metrics()).InnerWidth
            let probe = provider.Document.createElement("div") :?> HTMLElement
            let node = probe:>Node
            try
                (probe:>ElementCSSInlineStyle).style.setProperty("height","100lvh")
                provider.Document.body.appendChild node |> ignore
                height <- float probe.offsetHeight
            finally
                if not(isNull(box node.parentNode)) then node.parentNode.removeChild node |> ignore
        | None ->
            left <- rect.left; top <- rect.top
            width <- float element.clientWidth; height <- float element.clientHeight
            scale <- if rect.width <> 0. then width/rect.width elif rect.height <> 0. then height/rect.height else 1.
        scrollWidth <- float element.scrollWidth + number(style.getPropertyValue "margin-left") + number(style.getPropertyValue "margin-right")
        scrollHeight <- float element.scrollHeight + number(style.getPropertyValue "margin-top") + number(style.getPropertyValue "margin-bottom")
    let listener =
        { new EventListenerObject with
            member _.handleEvent _ = if not disposed then readCoords(); wake.Restart() }
    let observer = resize.Create(fun _ -> if not disposed then resizeTicker.Restart())
    do
        try
            readCoords(); bounds()
            eventTarget.addEventListener("scroll",listener,false)
            observer.Observe(element)
            wake.Restart()
        with _ ->
            try observer.Disconnect()
            finally
                try eventTarget.removeEventListener("scroll",listener,false)
                finally ticker.Cancel(); data.Cancel(); wake.Cancel(); resizeTicker.Cancel()
            reraise()

    /// <summary>Creates an observer, measuring sticky ancestors at static position and restoring each declaration.</summary>
    member _.Observe(target:HTMLElement, ?options:unit -> ScrollOptions, ?onEvent:ScrollEvent -> ScrollObserver -> unit,
                     ?debug:bool, ?id:string) =
        if disposed then raise(ObjectDisposedException(nameof ScrollContainer))
        let options = defaultArg options (fun () -> ScrollOptions.Default)
        let mutable settings = ScrollOptions.Default
        let mutable child:ScrollObserver option = None
        let mutable initialization:IDisposable option = None
        let measure () =
            let changed = ResizeArray<CSSStyleDeclaration * string * string>()
            try
                let mutable current = target:>Element
                let body = (target:>Node).ownerDocument.body:>Element
                while not(isNull(box current)) && not(Object.ReferenceEquals(current,element)) && not(Object.ReferenceEquals(current,body)) do
                    if (computed.Get(current,None)).getPropertyValue "position" = "sticky" then
                        let inlineStyle = (current :?> ElementCSSInlineStyle).style :> CSSStyleDeclaration
                        changed.Add(inlineStyle,inlineStyle.getPropertyValue "position",inlineStyle.getPropertyPriority "position")
                        inlineStyle.setProperty("position","static")
                    current <- (current:>Node).parentElement
                let rect = target.getBoundingClientRect()
                if settings.Horizontal then
                    { Offset=(rect.left+x-left)*scale; TargetSize=rect.width*scale; ContainerSize=width; ScrollSize=scrollWidth }
                else
                    { Offset=(rect.top+y-top)*scale; TargetSize=rect.height*scale; ContainerSize=height; ScrollSize=scrollHeight }
            finally
                for style,value,priority in changed do
                    if value = "" then style.removeProperty "position" |> ignore
                    else style.setProperty("position",value,priority)
        let cleanup () =
            initialization |> Option.iter(fun frame -> frame.Dispose())
            initialization <- None
            child |> Option.iter(fun child -> children.Remove child |> ignore)
            if children.Count = 0 then this.Dispose()
        let result = new ScrollObserver(
            (fun () -> if settings.Horizontal then x,backwardX else y,backwardY),measure,
            options=(fun () -> settings <- options(); settings),convertUnit=(fun value unitName -> CssUnits.convert target value unitName "px"),
            ?onEvent=onEvent,wake=(fun () -> if not disposed then wake.Restart()),onRevert=cleanup)
        child <- Some result
        let index = System.Threading.Interlocked.Increment(&debugIndex)
        let mutable debugging = defaultArg debug false
        let overlay = new ScrollDebugOverlay(element,(fun () -> width,height,scrollWidth,scrollHeight),nextDebugZ,index,
                                            defaultArg id (string index),acquireDebugPosition)
        result.AttachDebug {
            Request=(fun () -> debugging <- true; overlay.Render(result,settings))
            Refresh=(fun () -> if debugging then overlay.Render(result,settings))
            Sample=(fun () ->
                if debugging then
                    let scroll = if settings.Horizontal then x else y
                    let entering = not result.IsInView && scroll>result.OffsetStart && scroll<result.OffsetEnd
                    overlay.Sample(settings.Horizontal,x,y,entering))
            Remove=(fun () -> overlay.Remove()) }
        children.Add result
        initialization <- Some(scheduler.Frame(fun _ ->
            initialization <- None
            if not disposed && not result.Reverted && not result.Ready then
                try result.Refresh()
                with _ -> result.Revert(); reraise()))
        result
    /// <summary>Pauses borrowed playback and observes the explicit target, first linked DOM target, or document body.</summary>
    /// <remarks>Inferred targets measure immediately at the muted zero playhead. An explicit target initializes on the next frame.</remarks>
    member _.ObserveLinked(link:ScrollLink, targets:HTMLElement seq, ?target:HTMLElement,
                           ?options:unit -> ScrollOptions, ?onEvent:ScrollEvent -> ScrollObserver -> unit) =
        let selected =
            match target with
            | Some target -> target
            | None ->
                targets |> Seq.tryFind(fun target -> not(isNull(box target)))
                |> Option.defaultWith(fun () -> (element:>Node).ownerDocument.body)
        let observer = this.Observe(selected,?options=options,?onEvent=onEvent)
        try
            observer.Link link |> ignore
            if target.IsNone then observer.Refresh()
            observer
        with _ ->
            observer.Revert()
            reraise()

    /// <summary>Refreshes all ready observers and samples current native scroll coordinates.</summary>
    member _.Refresh() =
        if not disposed then
            bounds()
            for child in children.ToArray() do if not disposed && child.Ready then child.Resize()
            if not disposed then readCoords(); wake.Restart()
    /// <summary>Current 30 FPS Euclidean velocity in pixels per millisecond.</summary>
    member _.Velocity = velocity
    /// <summary>Whether the last changed horizontal sample moved backward.</summary>
    member _.BackwardX = backwardX
    /// <summary>Whether the last changed vertical sample moved backward.</summary>
    member _.BackwardY = backwardY
    /// <summary>Ends observers, frame timers, resize observation and the native event listener.</summary>
    member _.Dispose() =
        if not disposed then
            disposed <- true
            ticker.Cancel(); data.Cancel(); wake.Cancel(); resizeTicker.Cancel()
            try observer.Disconnect()
            finally
                try eventTarget.removeEventListener("scroll",listener,false)
                finally
                    for child in children.ToArray() do child.Revert()
                    children.Clear()
                    onDispose |> Option.iter(fun callback -> callback())
    interface IDisposable with member _.Dispose() = this.Dispose()

    /// <summary>Observes the existing host window using its viewport provider and scrolling service.</summary>
    static member ForViewport(scheduler, provider:ViewportProvider, scroll:ViewportScrollService, computed, resize) =
        new ScrollContainer(scheduler,provider.Document.body,computed,resize,viewport=(provider,scroll))

/// <summary>Host-local container reuse; disposing the registry releases every listener and observer it created.</summary>
type ScrollContainerRegistry(scheduler:FrameScheduler, computed:ComputedStyleService, resize:ResizeObservationService) as this =
    let containers = System.Collections.Generic.Dictionary<EventTarget,ScrollContainer>(HashIdentity.Reference)
    let mutable disposed = false
    let get key create =
        if disposed then raise(ObjectDisposedException(nameof ScrollContainerRegistry))
        match containers.TryGetValue key with
        | true,container -> container
        | _ ->
            let container = create(fun () -> containers.Remove key |> ignore)
            containers.Add(key,container)
            container

    /// <summary>Reuses the live container registered for the same native element identity.</summary>
    member _.ForElement(element:HTMLElement) =
        get (element:>EventTarget) (fun cleanup -> new ScrollContainer(scheduler,element,computed,resize,onDispose=cleanup))

    /// <summary>Reuses the live container registered for the same native window identity.</summary>
    member _.ForViewport(provider:ViewportProvider, scroll:ViewportScrollService) =
        get provider.Window (fun cleanup ->
            new ScrollContainer(scheduler,provider.Document.body,computed,resize,viewport=(provider,scroll),onDispose=cleanup))

    /// <summary>Number of live native event containers owned by the registry.</summary>
    member _.Count = containers.Count

    /// <summary>Attaches the registry and its current and future containers to the current Ranvier owner.</summary>
    member _.Own() = Ranvier.Api.getOwner().Attach(this :> IDisposable); this

    /// <summary>Reverts all observers and removes each container from the registry.</summary>
    member _.Dispose() =
        if not disposed then
            disposed <- true
            for container in containers.Values |> Seq.toArray do container.Dispose()
            containers.Clear()
    interface IDisposable with member _.Dispose() = this.Dispose()
